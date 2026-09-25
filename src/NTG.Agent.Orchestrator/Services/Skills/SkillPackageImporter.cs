using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using NTG.Agent.Orchestrator.Models.Skills;

namespace NTG.Agent.Orchestrator.Services.Skills;

/// <summary>
/// Turns an uploaded <c>.zip</c> Agent Skill package into a validated <see cref="Skill"/> graph,
/// or into a list of line-item reasons it was rejected.
/// </summary>
/// <remarks>
/// <para>
/// A <c>SKILL.md</c> body becomes instructions in an LLM's context, so this is a trust boundary,
/// not a file parser. Three premises worth stating because they are easy to get backwards:
/// </para>
/// <list type="number">
/// <item>
/// <b>There is no extraction root.</b> Content goes to SQL and <c>ExtractToDirectory</c> is never
/// called, so none of .NET's built-in traversal protection applies. "Escaping the root" here means
/// writing another skill's <see cref="SkillAsset"/> row, not reaching the filesystem.
/// </item>
/// <item>
/// <b>Paths are validated as raw strings, never through <c>Path.*</c>.</b> On Linux <c>\</c> is an
/// ordinary filename character, so <c>..\..\evil.md</c>, <c>C:\evil.md</c> and
/// <c>\\server\share\evil.md</c> all normalise to inert single-component names and a
/// <c>Path.GetFullPath</c> check reports no escape — while the same three are real traversal on
/// Windows. Validation that passes on this CI precisely because it runs on Linux is worse than none.
/// </item>
/// <item>
/// <b>Compression-ratio caps are theatre.</b> Declaring a smaller uncompressed size drops a 1028:1
/// bomb's apparent ratio to 0.0098:1. The streaming byte counter in <see cref="ReadEntry"/> is the
/// actual guarantee; the declared-size precheck is only a cheap early exit.
/// </item>
/// </list>
/// <para>
/// Every check reports rather than throws, and the caller receives <em>all</em> failures. An author
/// fixing one error per upload round-trip starts disabling checks.
/// </para>
/// </remarks>
public sealed partial class SkillPackageImporter
{
    /// <summary>Upper bound on the uploaded archive. Enforced again at the endpoint.</summary>
    public const int MaxPackageBytes = 5 * 1024 * 1024;

    /// <summary>Total decompressed bytes across all entries, enforced by a streaming counter.</summary>
    public const int MaxUncompressedBytes = 20 * 1024 * 1024;

    /// <summary>A real skill is a handful of files; this only has to exclude the absurd.</summary>
    public const int MaxEntries = 200;

    public const int MaxPathLength = 512;

    /// <summary>A sanity bound on nesting. Explicitly <em>not</em> part of the traversal defence.</summary>
    public const int MaxPathDepth = 4;

    public const int MaxNameLength = 64;
    public const int MaxDescriptionLength = 1024;
    public const int MaxCompatibilityLength = 500;

    private const string SkillManifestName = "SKILL.md";

    /// <summary>
    /// Constrains the <em>name</em> of a file, never its bytes. An anti-footgun measure that keeps
    /// executables and archives from being stored by accident — not a security boundary, since an
    /// attacker controls the extension as freely as the content.
    /// </summary>
    private static readonly string[] AllowedExtensions =
        [".md", ".json", ".txt", ".yaml", ".yml", ".png", ".svg"];

    /// <summary>Extensions read as text: character-guarded and stored as UTF-8.</summary>
    private static readonly string[] TextExtensions = [".md", ".json", ".txt", ".yaml", ".yml", ".svg"];

    /// <summary>Local file header magic. Any entry beginning with it is a nested archive.</summary>
    private static readonly byte[] ZipMagic = [0x50, 0x4B, 0x03, 0x04];

    /// <summary>Stored (0) and Deflate (8). Anything else is rejected by number, not by exception.</summary>
    private static readonly ushort[] AllowedCompressionMethods = [0, 8];

    /// <summary>
    /// Anchored, and applied after NFC normalisation. Unanchored, Cyrillic homoglyphs pass; and
    /// .NET's <c>\w</c> matches Unicode letters by default, so the class is spelled out literally.
    /// </summary>
    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SkillNamePattern { get; }

    /// <summary>Strict UTF-8: invalid bytes are an error, not a silent U+FFFD.</summary>
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public sealed record Result(Skill? Skill, IReadOnlyList<string> Errors)
    {
        public bool Succeeded => Skill is not null && Errors.Count == 0;

        public static Result Failed(params string[] errors) => new(null, errors);

        public static Result Failed(IReadOnlyList<string> errors) => new(null, errors);
    }

    /// <summary>
    /// Validates and parses <paramref name="packageBytes"/>. Nothing is persisted here — the caller
    /// owns the transaction, so a rejection cannot leave partial rows behind.
    /// </summary>
    public Result Import(byte[] packageBytes, string sourceFileName, Guid importedByUserId)
    {
        ArgumentNullException.ThrowIfNull(packageBytes);

        if (packageBytes.Length == 0)
        {
            return Result.Failed("The uploaded file is empty.");
        }

        if (packageBytes.Length > MaxPackageBytes)
        {
            return Result.Failed(
                $"Package is {packageBytes.Length:N0} bytes; the limit is {MaxPackageBytes:N0}.");
        }

        // Read the central directory ourselves first: entry count, encryption bit, compression
        // method and symlink attributes are all needed before ZipArchive is worth constructing.
        var directory = ZipCentralDirectory.Read(packageBytes, MaxEntries);
        if (!directory.Ok)
        {
            return Result.Failed($"Not a readable ZIP package: {directory.Error}.");
        }

        var errors = new List<string>();
        var files = ValidateEntries(directory.Entries, errors);

        if (errors.Count > 0)
        {
            return Result.Failed(errors);
        }

        if (files.Count == 0)
        {
            return Result.Failed("Package contains no files.");
        }

        var declaredTotal = files.Sum(e => (long)e.UncompressedSize);
        if (declaredTotal > MaxUncompressedBytes)
        {
            return Result.Failed(
                $"Package declares {declaredTotal:N0} uncompressed bytes; the limit is {MaxUncompressedBytes:N0}.");
        }

        return ReadAndValidateContent(packageBytes, files, sourceFileName, importedByUserId);
    }

    /// <summary>
    /// Container-level checks against the raw central directory. Returns the file entries (directory
    /// markers dropped) when everything passes.
    /// </summary>
    private static List<ZipCentralDirectory.Entry> ValidateEntries(
        IReadOnlyList<ZipCentralDirectory.Entry> entries, List<string> errors)
    {
        var files = new List<ZipCentralDirectory.Entry>();

        // Both comparers, because they catch different attacks. Ordinal duplicates create the
        // validate/store differential — ZipArchive.GetEntry returns the *first* match while
        // dictionary and EF iteration keep the *last*, so the reviewed bytes need not be the
        // stored bytes. Case-insensitive duplicates are legal in ZIP and legal here, but collide
        // on SQL Server's default CI collation and would fail the unique index mid-transaction.
        var ordinal = new HashSet<string>(StringComparer.Ordinal);
        var caseInsensitive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var name = entry.FileName;

            if (!ordinal.Add(name))
            {
                errors.Add($"'{name}': archive contains this entry more than once.");
                continue;
            }

            if (!caseInsensitive.Add(name))
            {
                errors.Add($"'{name}': archive contains another entry differing only in case.");
                continue;
            }

            if (!ValidatePath(name, errors))
            {
                continue;
            }

            if (entry.IsEncrypted)
            {
                errors.Add($"'{name}': entry is encrypted. Encrypted entries cannot be reviewed or scanned.");
                continue;
            }

            if (entry.IsSymlink)
            {
                errors.Add($"'{name}': entry is a symbolic link.");
                continue;
            }

            if (entry.IsSpecialFile)
            {
                errors.Add($"'{name}': entry is not a regular file.");
                continue;
            }

            if (entry.HasElevatedBits)
            {
                errors.Add($"'{name}': entry has setuid or setgid bits set.");
                continue;
            }

            if (entry.IsDirectoryMarker)
            {
                continue;
            }

            if (Array.IndexOf(AllowedCompressionMethods, entry.CompressionMethod) < 0)
            {
                errors.Add(
                    $"'{name}': unsupported compression method {entry.CompressionMethod}. "
                    + "Only stored and deflate are accepted.");
                continue;
            }

            if (!HasAllowedExtension(name))
            {
                errors.Add(
                    $"'{name}': file type is not allowed. Permitted: {string.Join(' ', AllowedExtensions)}.");
                continue;
            }

            files.Add(entry);
        }

        return files;
    }

    /// <summary>
    /// Validates a ZIP entry name as a <em>raw string</em>. Nothing here calls into
    /// <see cref="Path"/>: see the class remarks for why that would pass on Linux and fail on
    /// Windows. These rules must reject identically on both.
    /// </summary>
    private static bool ValidatePath(string name, List<string> errors)
    {
        if (name.Length == 0)
        {
            errors.Add("Archive contains an entry with an empty name.");
            return false;
        }

        if (name.Length > MaxPathLength)
        {
            errors.Add($"'{Truncate(name)}': path exceeds {MaxPathLength} characters.");
            return false;
        }

        if (name.Contains('\\'))
        {
            errors.Add($"'{name}': path contains a backslash.");
            return false;
        }

        if (name[0] == '/')
        {
            errors.Add($"'{name}': path is absolute.");
            return false;
        }

        if (name.Length >= 2 && name[1] == ':' && char.IsAsciiLetter(name[0]))
        {
            errors.Add($"'{name}': path has a drive letter.");
            return false;
        }

        foreach (var c in name)
        {
            if (c < 0x20 || c == 0x7F)
            {
                errors.Add($"'{Escape(name)}': path contains a control character.");
                return false;
            }
        }

        // A trailing '/' is the directory marker and produces a legitimate empty final segment.
        var segments = name.TrimEnd('/').Split('/');

        if (segments.Length > MaxPathDepth)
        {
            errors.Add($"'{name}': path is nested more than {MaxPathDepth} levels deep.");
            return false;
        }

        foreach (var segment in segments)
        {
            if (segment.Length == 0)
            {
                errors.Add($"'{name}': path contains an empty segment.");
                return false;
            }

            if (segment is "." or "..")
            {
                errors.Add($"'{name}': path contains a '{segment}' traversal segment.");
                return false;
            }
        }

        return true;
    }

    private static Result ReadAndValidateContent(
        byte[] packageBytes,
        List<ZipCentralDirectory.Entry> files,
        string sourceFileName,
        Guid importedByUserId)
    {
        var errors = new List<string>();
        var contents = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        using (var stream = new MemoryStream(packageBytes, writable: false))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false))
        {
            // The central directory we validated and the entry list ZipArchive exposes must
            // describe the same archive. A mismatch means one of the two was fooled, and we do
            // not need to know which to reject.
            var expected = files.Select(f => f.FileName).ToHashSet(StringComparer.Ordinal);
            var actual = archive.Entries
                .Select(e => e.FullName)
                .Where(n => expected.Contains(n))
                .ToHashSet(StringComparer.Ordinal);

            if (!expected.SetEquals(actual))
            {
                return Result.Failed("Archive headers are inconsistent; the package may be corrupt or crafted.");
            }

            long total = 0;

            foreach (var entry in archive.Entries.Where(e => expected.Contains(e.FullName)))
            {
                var content = ReadEntry(entry, ref total, errors);
                if (content is null)
                {
                    return Result.Failed(errors);
                }

                if (content.Length >= ZipMagic.Length && content.AsSpan(0, ZipMagic.Length).SequenceEqual(ZipMagic))
                {
                    errors.Add($"'{entry.FullName}': entry is itself a ZIP archive.");
                    continue;
                }

                // Validate and store the same value. Validating FullName while storing Name waves
                // traversal through; the reverse flattens assets/a.json and references/a.json
                // into one row.
                contents[entry.FullName] = content;
            }
        }

        if (errors.Count > 0)
        {
            return Result.Failed(errors);
        }

        return BuildSkill(contents, sourceFileName, importedByUserId, errors);
    }

    /// <summary>
    /// Reads one entry through a bounded loop that aborts mid-stream. <c>CopyTo</c> or
    /// <c>ReadToEnd</c> would decompress the whole bomb before any limit could be consulted.
    /// </summary>
    private static byte[]? ReadEntry(ZipArchiveEntry entry, ref long total, List<string> errors)
    {
        var remaining = MaxUncompressedBytes - total;
        var buffer = new byte[81920];

        using var output = new MemoryStream();

        try
        {
            using var input = entry.Open();

            while (true)
            {
                var read = input.Read(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    break;
                }

                remaining -= read;
                if (remaining < 0)
                {
                    errors.Add(
                        $"Package expands beyond the {MaxUncompressedBytes:N0}-byte limit "
                        + $"while reading '{entry.FullName}'.");
                    return null;
                }

                output.Write(buffer, 0, read);
            }
        }
        catch (InvalidDataException ex)
        {
            errors.Add($"'{entry.FullName}': entry could not be decompressed — {ex.Message}");
            return null;
        }

        total = MaxUncompressedBytes - remaining;
        return output.ToArray();
    }

    private static Result BuildSkill(
        Dictionary<string, byte[]> contents,
        string sourceFileName,
        Guid importedByUserId,
        List<string> errors)
    {
        var prefix = ResolveRootPrefix(contents.Keys, errors);
        if (prefix is null)
        {
            return Result.Failed(errors);
        }

        var manifestKey = prefix + SkillManifestName;
        if (!TryDecode(manifestKey, contents[manifestKey], errors, out var manifestText))
        {
            return Result.Failed(errors);
        }

        SkillContentGuard.CheckBody(SkillManifestName, manifestText, errors);

        var frontmatter = SkillFrontmatter.Parse(manifestText, errors);
        if (frontmatter is null)
        {
            return Result.Failed(errors);
        }

        var directoryName = prefix.Length > 0 ? prefix.TrimEnd('/') : PackageNameFrom(sourceFileName);
        var name = ValidateName(frontmatter.Get("name"), directoryName, prefix.Length > 0, errors);
        var description = ValidateDescription(frontmatter.Get("description"), errors);
        var compatibility = frontmatter.Get("compatibility");

        if (compatibility is { Length: > MaxCompatibilityLength })
        {
            errors.Add($"compatibility: exceeds {MaxCompatibilityLength} characters.");
        }

        if (compatibility is not null)
        {
            SkillContentGuard.CheckMetadataValue("compatibility", compatibility, errors);
        }

        var skill = new Skill
        {
            Id = Guid.NewGuid(),
            Name = name ?? string.Empty,
            Description = description ?? string.Empty,
            Body = frontmatter.Body,
            Version = frontmatter.Get("metadata.version") ?? frontmatter.Get("version"),
            License = frontmatter.Get("license"),
            Compatibility = compatibility,
            SourceFileName = sourceFileName,
            ImportedByUserId = importedByUserId,
        };

        foreach (var (key, bytes) in contents.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (key == manifestKey)
            {
                continue;
            }

            var relativePath = key[prefix.Length..];

            if (IsTextFile(relativePath))
            {
                if (!TryDecode(relativePath, bytes, errors, out var text))
                {
                    continue;
                }

                SkillContentGuard.CheckBody(relativePath, text, errors);

                if (IsSurfaceAsset(relativePath))
                {
                    errors.AddRange(SurfaceValidator.Validate(relativePath, text));
                }
            }

            skill.Assets.Add(new SkillAsset
            {
                Id = Guid.NewGuid(),
                SkillId = skill.Id,
                RelativePath = relativePath,
                Content = bytes,
            });
        }

        return errors.Count > 0 ? Result.Failed(errors) : new Result(skill, []);
    }

    /// <summary>
    /// Finds the single wrapping directory the spec's layout uses, or accepts a bare
    /// <c>SKILL.md</c> at the archive root. The latter is the spec's leniency guidance for client
    /// implementations: fall back to the file name rather than reject.
    /// </summary>
    private static string? ResolveRootPrefix(IEnumerable<string> keys, List<string> errors)
    {
        var paths = keys.ToList();

        if (paths.Contains(SkillManifestName, StringComparer.Ordinal))
        {
            return string.Empty;
        }

        var roots = paths
            .Select(p => p.Split('/', 2)[0])
            .ToHashSet(StringComparer.Ordinal);

        if (roots.Count != 1)
        {
            errors.Add(
                $"Package must contain a single top-level directory holding {SkillManifestName}, "
                + $"or {SkillManifestName} at the archive root. Found: {string.Join(", ", roots.Order(StringComparer.Ordinal))}.");
            return null;
        }

        var prefix = roots.First() + "/";

        if (!paths.Contains(prefix + SkillManifestName, StringComparer.Ordinal))
        {
            errors.Add($"Package has no {SkillManifestName} (looked for '{prefix}{SkillManifestName}').");
            return null;
        }

        return prefix;
    }

    private static string? ValidateName(string? value, string directoryName, bool hasDirectory, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add("name: frontmatter field is required.");
            return null;
        }

        SkillContentGuard.CheckMetadataValue("name", value, errors);

        // Normalise before matching: without it, a Cyrillic 'а' in an otherwise-Latin name is a
        // different code point that no [a-z] class will catch.
        var normalized = value.Normalize(NormalizationForm.FormC);

        if (normalized.Length > MaxNameLength)
        {
            errors.Add($"name: exceeds {MaxNameLength} characters.");
            return null;
        }

        if (!SkillNamePattern.IsMatch(normalized))
        {
            errors.Add(
                "name: must be lowercase letters, digits and single hyphens only, "
                + "with no leading, trailing or repeated hyphens.");
            return null;
        }

        if (hasDirectory && !string.Equals(normalized, directoryName, StringComparison.Ordinal))
        {
            errors.Add($"name: '{normalized}' does not match the package directory '{directoryName}'.");
            return null;
        }

        return normalized;
    }

    private static string? ValidateDescription(string? value, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add("description: frontmatter field is required.");
            return null;
        }

        if (value.Length > MaxDescriptionLength)
        {
            errors.Add($"description: exceeds {MaxDescriptionLength} characters.");
            return null;
        }

        SkillContentGuard.CheckMetadataValue("description", value, errors);
        return value;
    }

    private static bool TryDecode(string location, byte[] bytes, List<string> errors, out string text)
    {
        try
        {
            text = StrictUtf8.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            errors.Add($"'{location}': file is not valid UTF-8 text.");
            text = string.Empty;
            return false;
        }
    }

    private static bool HasAllowedExtension(string name) =>
        AllowedExtensions.Any(ext => name.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

    private static bool IsTextFile(string name) =>
        TextExtensions.Any(ext => name.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

    /// <summary>A2UI surface templates live under <c>assets/</c> per the spec's conventional layout.</summary>
    private static bool IsSurfaceAsset(string relativePath) =>
        relativePath.StartsWith("assets/", StringComparison.OrdinalIgnoreCase)
        && relativePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    private static string PackageNameFrom(string sourceFileName)
    {
        var name = sourceFileName;

        var slash = name.LastIndexOfAny(['/', '\\']);
        if (slash >= 0)
        {
            name = name[(slash + 1)..];
        }

        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        return name;
    }

    private static string Truncate(string value) =>
        value.Length <= 80 ? value : string.Concat(value.AsSpan(0, 77), "...");

    private static string Escape(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            builder.Append(c < 0x20 || c == 0x7F
                ? string.Create(CultureInfo.InvariantCulture, $"<U+{(int)c:X4}>")
                : c.ToString());
        }

        return Truncate(builder.ToString());
    }
}

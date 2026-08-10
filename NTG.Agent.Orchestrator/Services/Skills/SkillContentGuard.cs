using System.Globalization;
using System.Text;

namespace NTG.Agent.Orchestrator.Services.Skills;

/// <summary>
/// Character-level checks on skill text before it is stored. Everything else in the importer
/// guards the <em>container</em>; this guards the <em>payload</em>, which is natural language
/// delivered to the model byte-for-byte intact.
/// </summary>
/// <remarks>
/// <para>
/// The threat is not that a human reviewer approves malicious text — it is that a human reviewer
/// approves text they cannot see. Unicode tag characters render as nothing in every editor and
/// diff tool while remaining fully legible to the model, so a skill can pass review carrying
/// instructions no one ever read. Those runes have no legitimate use in a skill, which makes
/// rejecting them the highest value-per-line control available here.
/// </para>
/// <para>
/// Every character this file names is written as an escape sequence, never as a literal. A source
/// file that pasted the real runes in would be unreviewable in precisely the way it exists to
/// prevent, and would trip its own checks if it were ever scanned.
/// </para>
/// <para>
/// This cannot defend against semantic attacks — instruction override, exfiltration directives,
/// tool-misuse steering — because those are ordinary visible prose. See the plan's
/// "What validation cannot defend".
/// </para>
/// </remarks>
internal static class SkillContentGuard
{
    /// <summary>Unicode tag characters: invisible everywhere, semantically read by the model.</summary>
    private const int TagRangeStart = 0xE0000;
    private const int TagRangeEnd = 0xE007F;

    private const char ByteOrderMark = '\uFEFF';

    /// <summary>Bidirectional overrides and isolates — the Trojan Source class.</summary>
    private static readonly char[] BidiOverrides =
    [
        '\u202A', '\u202B', '\u202C', '\u202D', '\u202E',
        '\u2066', '\u2067', '\u2068', '\u2069',
    ];

    /// <summary>Zero-width characters used to hide instruction text inside innocuous prose.</summary>
    private static readonly char[] ZeroWidth = ['\u200B', '\u200C', '\u200D', ByteOrderMark];

    /// <summary>
    /// Substrings that must never appear in <c>name</c> or <c>description</c>. Tier-1 metadata is
    /// concatenated into a single system message and injected on <em>every</em> run for every
    /// bound skill, with no activation step — so it has the widest blast radius of anything here.
    /// </summary>
    private static readonly string[] MetadataInjectionMarkers =
    [
        "---", "system:", "assistant:", "user:", "<|", "|>", "```",
    ];

    /// <summary>
    /// Checks a free-text file body. <paramref name="location"/> prefixes each error so a package
    /// with several bad files reports which is which.
    /// </summary>
    /// <remarks>
    /// At most one error per distinct problem kind: a file carrying four hundred zero-width
    /// characters should produce one line, not four hundred.
    /// </remarks>
    public static void CheckBody(string location, string text, List<string> errors)
    {
        var reported = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            // A BOM is tolerated only as the very first character of the file.
            if (c == ByteOrderMark && i == 0)
            {
                continue;
            }

            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                var codePoint = char.ConvertToUtf32(c, text[i + 1]);
                if (codePoint is >= TagRangeStart and <= TagRangeEnd)
                {
                    Report(
                        "tag",
                        $"{location}: contains Unicode tag character U+{codePoint:X} at index {i} — "
                        + "invisible to a reviewer but read by the model");
                }

                i++;
                continue;
            }

            if (c < 0x20 && c is not '\t' and not '\r' and not '\n')
            {
                Report("control", $"{location}: contains control character U+{(int)c:X4} at index {i}");
                continue;
            }

            if (Array.IndexOf(BidiOverrides, c) >= 0)
            {
                Report(
                    "bidi",
                    $"{location}: contains bidirectional override U+{(int)c:X4} at index {i} — "
                    + "text can render in a different order than it is read");
                continue;
            }

            if (Array.IndexOf(ZeroWidth, c) >= 0)
            {
                Report("zero-width", $"{location}: contains zero-width character U+{(int)c:X4} at index {i}");
            }
        }

        // HTML comments survive into the token stream while rendering as nothing in the confirm
        // dialog — the same "approved text nobody saw" failure as tag characters.
        var comment = text.IndexOf("<!--", StringComparison.Ordinal);
        if (comment >= 0)
        {
            Report(
                "html-comment",
                $"{location}: contains an HTML comment at index {comment} — "
                + "invisible when rendered but present in the model's context");
        }

        void Report(string kind, string message)
        {
            if (reported.Add(kind))
            {
                errors.Add(message);
            }
        }
    }

    /// <summary>
    /// Checks a frontmatter metadata value. Stricter than <see cref="CheckBody"/>: no newlines and
    /// no markers that could break the value out of the catalog line it is concatenated into.
    /// </summary>
    public static void CheckMetadataValue(string field, string value, List<string> errors)
    {
        CheckBody(field, value, errors);

        if (value.Contains('\n') || value.Contains('\r'))
        {
            errors.Add($"{field}: must be a single line — a newline lets it forge a separate "
                       + "instruction in the skill catalog");
        }

        foreach (var marker in MetadataInjectionMarkers)
        {
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"{field}: must not contain '{marker}'");
            }
        }
    }

    /// <summary>
    /// Escapes invisible characters for display, so an import confirmation shows a reviewer
    /// exactly what the model will receive.
    /// </summary>
    public static string EscapeForDisplay(string text)
    {
        var builder = new StringBuilder(text.Length);

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                var codePoint = char.ConvertToUtf32(c, text[i + 1]);
                if (codePoint is >= TagRangeStart and <= TagRangeEnd)
                {
                    builder.Append(CultureInfo.InvariantCulture, $"<U+{codePoint:X}>");
                    i++;
                    continue;
                }

                builder.Append(c).Append(text[i + 1]);
                i++;
                continue;
            }

            if ((c < 0x20 && c is not '\t' and not '\r' and not '\n')
                || Array.IndexOf(BidiOverrides, c) >= 0
                || Array.IndexOf(ZeroWidth, c) >= 0)
            {
                builder.Append(CultureInfo.InvariantCulture, $"<U+{(int)c:X4}>");
                continue;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }
}

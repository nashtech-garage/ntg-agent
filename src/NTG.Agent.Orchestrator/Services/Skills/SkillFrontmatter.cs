namespace NTG.Agent.Orchestrator.Services.Skills;

/// <summary>
/// The YAML frontmatter block at the top of a <c>SKILL.md</c>, and the markdown body beneath it.
/// </summary>
/// <remarks>
/// <para>
/// Parsed by hand rather than with a YAML library, for two reasons. The solution has no YAML
/// dependency, and adding one to deserialize an untrusted upload would import that library's whole
/// attack surface — anchor expansion ("billion laughs"), merge keys, implicit type coercion, and in
/// some libraries type resolution — to read five scalar fields.
/// </para>
/// <para>
/// The grammar accepted here is deliberately a fraction of YAML: <c>key: value</c> scalars at the
/// top level, and one level of nesting under <c>metadata:</c>. Anything outside that — block
/// scalars, anchors, aliases, flow collections, multi-document markers — is <em>rejected</em>
/// rather than skipped, so a file this parser cannot fully account for never imports.
/// </para>
/// </remarks>
internal sealed class SkillFrontmatter
{
    private SkillFrontmatter(Dictionary<string, string> values, string body)
    {
        Values = values;
        Body = body;
    }

    /// <summary>Scalar fields, nested keys flattened with a dot (<c>metadata.version</c>).</summary>
    public Dictionary<string, string> Values { get; }

    /// <summary>The markdown following the closing delimiter, with leading blank lines trimmed.</summary>
    public string Body { get; }

    public string? Get(string key) => Values.TryGetValue(key, out var value) ? value : null;

    /// <summary>
    /// Splits and parses <paramref name="text"/>. Returns <see langword="null"/> when the document
    /// cannot be parsed, having appended the reasons to <paramref name="errors"/>.
    /// </summary>
    public static SkillFrontmatter? Parse(string text, List<string> errors)
    {
        var before = errors.Count;

        // A BOM before the opening delimiter is common from Windows editors and harmless.
        var content = text.TrimStart('\uFEFF');
        content = content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

        if (!content.StartsWith("---\n", StringComparison.Ordinal))
        {
            errors.Add("SKILL.md: must begin with a '---' YAML frontmatter delimiter");
            return null;
        }

        var closing = content.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (closing < 0)
        {
            errors.Add("SKILL.md: frontmatter has no closing '---' delimiter");
            return null;
        }

        // closing == 3 is the degenerate "---\n---" with an empty block; slicing [4..3] would throw.
        var block = closing > 3 ? content[4..closing] : string.Empty;
        var afterDelimiter = closing + 4;

        // The closing delimiter must be alone on its line.
        var lineEnd = content.IndexOf('\n', afterDelimiter);
        var remainderOfLine = lineEnd < 0 ? content[afterDelimiter..] : content[afterDelimiter..lineEnd];
        if (remainderOfLine.Trim().Length > 0)
        {
            errors.Add("SKILL.md: the closing '---' must be alone on its line");
            return null;
        }

        var body = lineEnd < 0 ? string.Empty : content[(lineEnd + 1)..].TrimStart('\n');
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? section = null;

        foreach (var (raw, number) in block.Split('\n').Select((line, index) => (line, index + 1)))
        {
            if (raw.Trim().Length == 0 || raw.TrimStart().StartsWith('#'))
            {
                continue;
            }

            var indent = raw.Length - raw.TrimStart(' ').Length;
            var line = raw.Trim();

            if (raw.Contains('\t'))
            {
                errors.Add($"SKILL.md frontmatter line {number}: tabs are not valid YAML indentation");
                continue;
            }

            if (line.StartsWith('-') || line.StartsWith('&') || line.StartsWith('*')
                || line.StartsWith('[') || line.StartsWith('{'))
            {
                errors.Add($"SKILL.md frontmatter line {number}: lists, anchors and flow collections "
                           + "are not supported — use simple 'key: value' fields");
                continue;
            }

            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                errors.Add($"SKILL.md frontmatter line {number}: expected 'key: value'");
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();

            if (value is "|" or ">" || (value.StartsWith('|') && value.Length <= 2))
            {
                errors.Add($"SKILL.md frontmatter line {number}: block scalars are not supported");
                continue;
            }

            value = Unquote(value);

            if (indent == 0)
            {
                if (value.Length == 0)
                {
                    // A bare "key:" opens a nested section; only metadata is recognised.
                    section = key;
                    continue;
                }

                section = null;
                Add(key, value, number);
            }
            else if (section is not null)
            {
                Add($"{section}.{key}", value, number);
            }
            else
            {
                errors.Add($"SKILL.md frontmatter line {number}: unexpected indentation");
            }
        }

        return errors.Count > before ? null : new SkillFrontmatter(values, body);

        void Add(string key, string value, int number)
        {
            if (!values.TryAdd(key, value))
            {
                // Last-wins would let a second copy override a reviewed value.
                errors.Add($"SKILL.md frontmatter line {number}: duplicate key '{key}'");
            }
        }
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2
            && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        return value;
    }
}

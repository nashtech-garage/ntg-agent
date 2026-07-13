using System.Text.RegularExpressions;

namespace NTG.Agent.MCP.Server.Services;

/// <summary>
/// Minimal SKILL.md frontmatter parser for the agentskills.io format.
/// Extracts and validates the required 'name' and 'description' fields from the
/// leading YAML frontmatter block without taking a YAML package dependency.
/// </summary>
public static partial class SkillMarkdownParser
{
    public const int MaxNameLength = 64;
    public const int MaxDescriptionLength = 1024;
    public const int MaxContentLength = 512 * 1024;

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex NamePattern();

    public record SkillFrontmatter(string Name, string Description);

    /// <summary>
    /// Parses a SKILL.md document and returns its frontmatter, or an error message.
    /// </summary>
    public static bool TryParse(string? content, out SkillFrontmatter? frontmatter, out string? error)
    {
        frontmatter = null;

        if (string.IsNullOrWhiteSpace(content))
        {
            error = "SKILL.md content is empty.";
            return false;
        }

        if (content.Length > MaxContentLength)
        {
            error = $"SKILL.md content exceeds the maximum size of {MaxContentLength / 1024} KB.";
            return false;
        }

        var lines = content.Replace("\r\n", "\n").Split('\n');
        if (lines.Length < 3 || lines[0].TrimEnd() != "---")
        {
            error = "SKILL.md must start with a YAML frontmatter block delimited by '---'.";
            return false;
        }

        var closingIndex = Array.FindIndex(lines, 1, line => line.TrimEnd() == "---");
        if (closingIndex < 0)
        {
            error = "The YAML frontmatter block is not closed with '---'.";
            return false;
        }

        string? name = null;
        string? description = null;
        for (var i = 1; i < closingIndex; i++)
        {
            var line = lines[i];
            var separator = line.IndexOf(':');
            if (line.StartsWith('#') || separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = Unquote(line[(separator + 1)..].Trim());
            switch (key)
            {
                case "name":
                    name = value;
                    break;
                case "description":
                    description = value;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            error = "The frontmatter is missing the required 'name' field.";
            return false;
        }

        if (name.Length > MaxNameLength || !NamePattern().IsMatch(name))
        {
            error = $"Skill name '{name}' is invalid: use lowercase letters, digits and single hyphens (max {MaxNameLength} characters), e.g. 'expense-report'.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            error = "The frontmatter is missing the required 'description' field.";
            return false;
        }

        if (description.Length > MaxDescriptionLength)
        {
            error = $"The 'description' field exceeds the maximum length of {MaxDescriptionLength} characters.";
            return false;
        }

        frontmatter = new SkillFrontmatter(name, description);
        error = null;
        return true;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        return value;
    }
}

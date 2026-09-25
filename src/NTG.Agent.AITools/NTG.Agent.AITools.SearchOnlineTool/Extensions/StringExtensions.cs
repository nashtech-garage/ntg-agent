using System.Text.RegularExpressions;

namespace NTG.Agent.AITools.SearchOnlineTool.Extensions;

/// <summary>
/// Provides extension methods for string manipulation.
/// </summary>
public static partial class StringExtensions
{
    /// <summary>
    /// Cleans HTML content by removing tags, scripts, styles, and normalizing whitespace.
    /// </summary>
    /// <param name="html">The HTML string to clean.</param>
    /// <returns>A cleaned plain text representation of the HTML content.</returns>
    /// <remarks>
    /// This method performs the following operations:
    /// 1. Removes script, style, and noscript tags with their content
    /// 2. Removes form input elements (input, textarea, select)
    /// 3. Replaces br and p closing tags with line breaks
    /// 4. Removes all remaining HTML tags
    /// 5. Decodes HTML entities
    /// 6. Normalizes whitespace
    /// </remarks>
    public static string CleanHtml(this string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        // Remove scripts, styles, and noscript tags with their content
        html = ScriptTagRegex().Replace(html, string.Empty);
        html = StyleTagRegex().Replace(html, string.Empty);
        html = NoScriptTagRegex().Replace(html, string.Empty);

        // Remove form input elements
        html = FormInputRegex().Replace(html, string.Empty);

        // Replace br and p closing tags with line breaks for readability
        html = BrTagRegex().Replace(html, "\n");
        html = ParagraphClosingTagRegex().Replace(html, "\n");

        // Remove all other HTML tags
        html = HtmlTagRegex().Replace(html, " ");

        // Decode HTML entities
        html = System.Net.WebUtility.HtmlDecode(html);

        // Collapse multiple whitespace and trim
        html = MultipleWhitespaceRegex().Replace(html, " ").Trim();

        return html;
    }

    // Source-generated regex patterns for optimal performance
    [GeneratedRegex(@"<script[\s\S]*?</script>", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ScriptTagRegex();

    [GeneratedRegex(@"<style[\s\S]*?</style>", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex StyleTagRegex();

    [GeneratedRegex(@"<noscript[\s\S]*?</noscript>", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex NoScriptTagRegex();

    [GeneratedRegex(@"<(input|textarea|select)[\s\S]*?>", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex FormInputRegex();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex BrTagRegex();

    [GeneratedRegex(@"</p\s*>", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ParagraphClosingTagRegex();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.Compiled)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s{2,}", RegexOptions.Compiled)]
    private static partial Regex MultipleWhitespaceRegex();
}

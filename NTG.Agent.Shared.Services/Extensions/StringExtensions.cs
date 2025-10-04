using System.Text.RegularExpressions;

namespace NTG.Agent.Shared.Services.Extensions;

public static class StringExtensions
{
    public static string CleanHtml(this string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        // 1. Remove scripts, styles, and noscript
        html = Regex.Replace(html, @"<script[\s\S]*?</script>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<style[\s\S]*?</style>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<noscript[\s\S]*?</noscript>", "", RegexOptions.IgnoreCase);

        // 2. Remove input, textarea, select
        html = Regex.Replace(html, @"<(input|textarea|select)[\s\S]*?>", "", RegexOptions.IgnoreCase);

        // 3. Replace <br> and </p> with line breaks for readability
        html = Regex.Replace(html, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"</p\s*>", "\n", RegexOptions.IgnoreCase);

        // 4. Remove all other HTML tags
        html = Regex.Replace(html, @"<[^>]+>", " ");

        // 5. Decode common entities (basic subset)
        html = System.Net.WebUtility.HtmlDecode(html);

        // 6. Collapse multiple whitespace
        html = Regex.Replace(html, @"\s{2,}", " ").Trim();

        return html;

    }
}

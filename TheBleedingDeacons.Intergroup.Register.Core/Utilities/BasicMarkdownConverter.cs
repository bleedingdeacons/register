using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace TheBleedingDeacons.Intergroup.Register.Utilities;

/// <summary>
/// Converts standard Markdown to valid HTML.
/// Supports: # headings, - bullet lists, --- horizontal rules,
/// *italic*, **bold**, [text](url) to http, https or mailto targets,
/// bare URLs, and paragraphs.
/// </summary>
public static class BasicMarkdownConverter
{
    // Guards against pathological (ReDoS) input on the untrusted markdown.
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    public static string Convert(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return string.Empty;

        var lines = NormaliseLineEndings(markdown).Split('\n');
        var html = new StringBuilder();

        int i = 0;
        while (i < lines.Length)
        {
            var line = lines[i].TrimEnd();

            // Blank line — skip
            if (string.IsNullOrWhiteSpace(line))
            {
                i++;
                continue;
            }

            // Horizontal rule
            if (Regex.IsMatch(line, @"^-{3,}$", RegexOptions.None, RegexTimeout))
            {
                html.AppendLine("<hr>");
                i++;
                continue;
            }

            // ATX headings: # H1, ## H2, ### H3 …
            var headingMatch = Regex.Match(line, @"^(#{1,6})\s+(.+)$", RegexOptions.None, RegexTimeout);
            if (headingMatch.Success)
            {
                int level = headingMatch.Groups[1].Length;
                string text = headingMatch.Groups[2].Value;
                html.AppendLine($"<h{level}>{FormatInline(text)}</h{level}>");
                i++;
                continue;
            }

            // Bullet list block: consume all consecutive list items
            if (IsBulletLine(line))
            {
                html.AppendLine("<ul>");
                while (i < lines.Length && IsBulletLine(lines[i].TrimEnd()))
                {
                    var item = Regex.Match(lines[i].TrimEnd(), @"^[-*+]\s+(.+)$", RegexOptions.None, RegexTimeout).Groups[1].Value;
                    html.AppendLine($"  <li>{FormatInline(item)}</li>");
                    i++;
                }
                html.AppendLine("</ul>");
                continue;
            }

            // Paragraph: consume all consecutive non-special lines
            var para = new List<string>();
            while (i < lines.Length)
            {
                var current = lines[i].TrimEnd();
                if (string.IsNullOrWhiteSpace(current)
                    || Regex.IsMatch(current, @"^#{1,6}\s", RegexOptions.None, RegexTimeout)
                    || Regex.IsMatch(current, @"^-{3,}$", RegexOptions.None, RegexTimeout)
                    || IsBulletLine(current))
                    break;

                para.Add(current);
                i++;
            }

            if (para.Count > 0)
                html.AppendLine($"<p>{FormatInline(string.Join(" ", para))}</p>");
        }

        return html.ToString().TrimEnd();
    }

    private static bool IsBulletLine(string line) =>
        Regex.IsMatch(line.TrimStart(), @"^[-*+]\s+\S", RegexOptions.None, RegexTimeout);

    /// <summary>
    /// Converts inline markdown to HTML:
    /// **bold**, *italic*, [text](url), bare https?:// URLs.
    /// HTML special characters are escaped first.
    /// </summary>
    private static string FormatInline(string text)
    {
        text = EscapeHtml(text.Trim());

        // Markdown links: [text](url). Only a web or mail target becomes a
        // link; anything else (javascript:, data:, vbscript:, a relative
        // path) is rendered as its text alone.
        text = Regex.Replace(text, @"\[(.+?)\]\((.+?)\)",
            m => IsLinkableTarget(m.Groups[2].Value)
                ? $"<a href=\"{m.Groups[2].Value}\">{m.Groups[1].Value}</a>"
                : m.Groups[1].Value,
            RegexOptions.None, RegexTimeout);

        // Bare URLs (not already inside href="")
        text = Regex.Replace(text, @"(?<!href="")https?://[^\s<]+",
            m => $"<a href=\"{m.Value}\">{m.Value}</a>", RegexOptions.None, RegexTimeout);

        // **bold**
        text = Regex.Replace(text, @"\*\*(.+?)\*\*", "<strong>$1</strong>", RegexOptions.None, RegexTimeout);

        // *italic* (single asterisk, not part of a pair)
        text = Regex.Replace(text, @"\*(.+?)\*", "<em>$1</em>", RegexOptions.None, RegexTimeout);

        return text;
    }

    /// <summary>
    /// True when <paramref name="url"/> is an absolute http, https or mailto
    /// URI. The raw text must itself begin with that scheme, so nothing
    /// <see cref="Uri"/> trims or tolerates in front of it can reach the href.
    /// </summary>
    private static bool IsLinkableTarget(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" or "mailto"
        && url.StartsWith(uri.Scheme + ":", StringComparison.OrdinalIgnoreCase);

    private static string EscapeHtml(string text) =>
        text.Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");

    private static string NormaliseLineEndings(string text) =>
        text.Replace("\r\n", "\n").Replace("\r", "\n");
}

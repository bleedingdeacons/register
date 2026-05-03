using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace TheBleedingDeacons.Intergroup.Register.Utilities;

/// <summary>
/// Converts standard Markdown to valid HTML.
/// Supports: # headings, - bullet lists, --- horizontal rules,
/// *italic*, **bold**, [text](url), bare URLs, and paragraphs.
/// </summary>
public static class BasicMarkdownConverter
{
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
            if (Regex.IsMatch(line, @"^-{3,}$"))
            {
                html.AppendLine("<hr>");
                i++;
                continue;
            }

            // ATX headings: # H1, ## H2, ### H3 …
            var headingMatch = Regex.Match(line, @"^(#{1,6})\s+(.+)$");
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
                    var item = Regex.Match(lines[i].TrimEnd(), @"^[-*+]\s+(.+)$").Groups[1].Value;
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
                    || Regex.IsMatch(current, @"^#{1,6}\s")
                    || Regex.IsMatch(current, @"^-{3,}$")
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
        Regex.IsMatch(line.TrimStart(), @"^[-*+]\s+\S");

    /// <summary>
    /// Converts inline markdown to HTML:
    /// **bold**, *italic*, [text](url), bare https?:// URLs.
    /// HTML special characters are escaped first.
    /// </summary>
    private static string FormatInline(string text)
    {
        text = EscapeHtml(text.Trim());

        // Markdown links: [text](url)
        text = Regex.Replace(text, @"\[(.+?)\]\((.+?)\)",
            m => $"<a href=\"{m.Groups[2].Value}\">{m.Groups[1].Value}</a>");

        // Bare URLs (not already inside href="")
        text = Regex.Replace(text, @"(?<!href="")https?://[^\s<]+",
            m => $"<a href=\"{m.Value}\">{m.Value}</a>");

        // **bold**
        text = Regex.Replace(text, @"\*\*(.+?)\*\*", "<strong>$1</strong>");

        // *italic* (single asterisk, not part of a pair)
        text = Regex.Replace(text, @"\*(.+?)\*", "<em>$1</em>");

        return text;
    }

    private static string EscapeHtml(string text) =>
        text.Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");

    private static string NormaliseLineEndings(string text) =>
        text.Replace("\r\n", "\n").Replace("\r", "\n");
}

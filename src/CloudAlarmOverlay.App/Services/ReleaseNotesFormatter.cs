using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace CloudAlarmOverlay.App.Services;

public static class ReleaseNotesFormatter
{
    public static string Format(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "此版本未提供更新說明。";
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        var document = Markdown.Parse(normalized);
        // Authored Markdown keeps its structure, including multiline list items and code blocks.
        if (document.Any(block => block is not ParagraphBlock) || document.OfType<ParagraphBlock>()
            .Any(p => p.Inline is {} inline && inline.Any(i => i is not LiteralInline and not LineBreakInline))) return normalized;

        var lines = normalized.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length <= 1) return normalized;
        var items = new List<string>(); var links = new List<string>();
        foreach (var line in lines)
        {
            var link = Regex.Match(line, @"^(?<label>[^<>\[\]]{1,60}?)\s*[：:]\s*(?<url>https?://\S+)$",
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            if (link.Success && Controls.MarkdownView.WebLink(link.Groups["url"].Value) is {} uri)
                links.Add($"[{link.Groups["label"].Value.Trim()}](<{uri.AbsoluteUri}>)");
            else items.Add("- " + line);
        }
        return string.Join("\n\n", new[] { string.Join("\n\n", items), string.Join("\n\n", links) }.Where(s => s.Length > 0));
    }
}

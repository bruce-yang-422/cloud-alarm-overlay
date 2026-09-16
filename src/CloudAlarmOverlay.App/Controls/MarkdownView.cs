using System.Diagnostics;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

using HtmlAgilityPack;
using Markdig;
using Markdig.Syntax;
using Markdig.Extensions.TaskLists;

namespace CloudAlarmOverlay.App.Controls;

/// <summary>Native, selectable Markdown text. HTML is mapped to text elements, never executed.</summary>
public sealed class MarkdownView : RichTextBox
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(MarkdownView), new PropertyMetadata("", (d, _) => ((MarkdownView)d).Render()));
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public MarkdownView()
    {
        IsReadOnly = true; IsDocumentEnabled = true; BorderThickness = new Thickness(0);
        Background = Brushes.Transparent; Padding = new Thickness(0);
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Render();
    }
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseEmojiAndSmiley().UseTaskLists().UseFootnotes().UsePreciseSourceLocation().Build();
    public bool EditableTasks { get; set; }
    private Queue<int> taskPositions = new();
    private readonly Dictionary<string, TextElement> anchors = new();
    private void Render()
    {
        anchors.Clear();
        taskPositions = new Queue<int>(Markdown.Parse(Text ?? "", Pipeline).Descendants<TaskList>().Select(t => t.Span.Start));
        var html = new HtmlDocument();
        html.LoadHtml(Markdown.ToHtml(Text ?? "", Pipeline));
        var document = new FlowDocument { PagePadding = new Thickness(0) };
        AddBlocks(html.DocumentNode, document.Blocks);
        Document = document;
    }
    private static bool Hidden(HtmlNode node) => node.Name is "script" or "style" or "iframe" or "object" or "embed" or "form";
    private void AddBlocks(HtmlNode parent, BlockCollection blocks)
    {
        Paragraph? loose = null;
        foreach (var node in parent.ChildNodes)
        {
            if (Hidden(node) || node.NodeType == HtmlNodeType.Comment) continue;
            if (node.Name is "ul" or "ol")
            {
                loose = null;
                var list = new System.Windows.Documents.List { MarkerStyle = node.Name == "ol" ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc, Margin = new Thickness(0, 4, 0, 10), Padding = new Thickness(24, 0, 0, 0) };
                if (int.TryParse(node.GetAttributeValue("start", "1"), out var start) && start > 0) list.StartIndex = start;
                foreach (var item in node.Elements("li")) { var li = new ListItem(); RegisterAnchor(item, li); AddBlocks(item, li.Blocks); list.ListItems.Add(li); }
                blocks.Add(list);
            }
            else if (node.Name is "blockquote" or "div" or "section")
            {
                loose = null; var section = new Section();
                if (node.Name == "blockquote") { section.BorderThickness = new Thickness(3, 0, 0, 0); section.BorderBrush = Brushes.SlateGray; section.Padding = new Thickness(12, 4, 0, 4); section.Margin = new Thickness(0, 8, 0, 12); }
                AddBlocks(node, section.Blocks); blocks.Add(section);
            }
            else if (node.Name == "hr")
            {
                loose = null; blocks.Add(new Paragraph { BorderBrush = Brushes.SlateGray, BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 12, 0, 12), FontSize = 1 });
            }
            else if (node.Name is "p" or "pre" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6")
            {
                loose = null; var p = new Paragraph { Margin = new Thickness(0, 0, 0, 12) };
                if (node.Name.StartsWith('h')) { p.FontSize = node.Name == "h1" ? 28 : node.Name == "h2" ? 23 : 19; p.FontWeight = FontWeights.Bold; }
                if (node.Name == "pre") { p.FontFamily = new FontFamily("Consolas"); p.Padding = new Thickness(10); p.BorderBrush = Brushes.SlateGray; p.BorderThickness = new Thickness(1); p.Inlines.Add(new Run(HtmlEntity.DeEntitize(node.InnerText))); }
                else AddInlines(node, p.Inlines);
                blocks.Add(p);
            }
            else
            {
                if (node.NodeType == HtmlNodeType.Text && string.IsNullOrWhiteSpace(node.InnerText) && loose is null) continue;
                if (loose is null) { loose = new Paragraph { Margin = new Thickness(0, 0, 0, 10) }; blocks.Add(loose); }
                AddInline(node, loose.Inlines);
            }
        }
    }
    private void AddInlines(HtmlNode parent, InlineCollection inlines)
    { foreach (var node in parent.ChildNodes) AddInline(node, inlines); }
    private void AddInline(HtmlNode node, InlineCollection inlines)
    {
        if (Hidden(node) || node.NodeType == HtmlNodeType.Comment) return;
        if (node.NodeType == HtmlNodeType.Text)
        {
            var text = HtmlEntity.DeEntitize(node.InnerText);
            if (node.Ancestors("code").Any()) { inlines.Add(new Run(text)); return; }
            var elements = System.Globalization.StringInfo.GetTextElementEnumerator(text);
            var plain = new System.Text.StringBuilder();
            while(elements.MoveNext())
            {
                var element = elements.GetTextElement();
                if (EmojiGlyph.FindImage(element) is {} image)
                {
                    if(plain.Length > 0) { inlines.Add(new Run(plain.ToString())); plain.Clear(); }
                    inlines.Add(new InlineUIContainer(new Image { Source = image, Width = 22, Height = 22, ToolTip = element }) { BaselineAlignment = BaselineAlignment.Center });
                }
                else plain.Append(element);
            }
            if(plain.Length > 0) inlines.Add(new Run(plain.ToString()));
            return;
        }
        if (node.Name == "input")
        {
            if (node.GetAttributeValue("type", "") != "checkbox" || taskPositions.Count == 0) return;
            var position = taskPositions.Dequeue();
            var check = new CheckBox { IsChecked = node.Attributes["checked"] is not null, IsEnabled = EditableTasks, ToolTip = EditableTasks ? "勾選完成；儲存任務後保留" : "任務項目狀態", Margin = new Thickness(0, 0, 6, 0) };
            check.Click += (_, _) => { if (position >= 0 && position + 2 < Text.Length && Text[position] == '[' && Text[position + 2] == ']') SetCurrentValue(TextProperty, Text[..(position + 1)] + (check.IsChecked == true ? "x" : " ") + Text[(position + 2)..]); };
            inlines.Add(new InlineUIContainer(check)); return;
        }
        if (node.Name == "br") { inlines.Add(new LineBreak()); return; }
        if (node.Name == "img") { inlines.Add(new Run(HtmlEntity.DeEntitize(node.GetAttributeValue("alt", "")))); return; }
        Span span = node.Name switch { "strong" or "b" => new Bold(), "em" or "i" => new Italic(), "u" => new Underline(), _ => new Span() };
        if (node.Name == "sup") { span.BaselineAlignment = BaselineAlignment.Superscript; span.FontSize = 11; }
        if (node.Name == "code") span.FontFamily = new FontFamily("Consolas");
        if (node.Name == "abbr") { span.ToolTip = HtmlEntity.DeEntitize(node.GetAttributeValue("title", "")); span.TextDecorations = TextDecorations.Underline; }
        if (node.Name == "a" && WebLink(node.GetAttributeValue("href", "")) is { } uri)
        {
            var link = new Hyperlink { NavigateUri = uri, Foreground = Brushes.DodgerBlue, ToolTip = uri.AbsoluteUri };
            link.RequestNavigate += (_, e) => { try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); } catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { link.ToolTip = "無法開啟連結，請複製網址：" + uri; } e.Handled = true; };
            span = link;
        }
        else if (node.Name == "a" && node.GetAttributeValue("href", "").StartsWith('#'))
        {
            var target = node.GetAttributeValue("href", "")[1..];
            var link = new Hyperlink { Foreground = Brushes.DodgerBlue, ToolTip = "跳至註腳／返回原文" };
            link.Click += (_, _) => { if (anchors.TryGetValue(target, out var element)) element.BringIntoView(); };
            span = link;
        }
        RegisterAnchor(node, span);
        AddInlines(node, span.Inlines); inlines.Add(span);
    }
    private void RegisterAnchor(HtmlNode node, TextElement element)
    {
        var id = node.GetAttributeValue("id", "");
        if (id.Length > 0) anchors[id] = element;
    }
    public static Uri? WebLink(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" ? uri : null;
}

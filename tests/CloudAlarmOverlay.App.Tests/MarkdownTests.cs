using System.Windows.Documents;
using CloudAlarmOverlay.App.Controls;
using CloudAlarmOverlay.App.Services;
using CloudAlarmOverlay.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
namespace CloudAlarmOverlay.App.Tests;

public sealed class MarkdownTests
{
    [Fact] public async Task Toolbar_inserts_multiline_lists_and_unique_footnotes()
    {
        await MilestoneOneTests.RunSta(() =>
        {
            var editor = new MarkdownEditor { MaxLength = 1000, Text = "出貨\n核帳" };
            var panel = (System.Windows.Controls.StackPanel)((System.Windows.Controls.TabItem)editor.Items[1]).Content;
            var toolbar = (System.Windows.Controls.WrapPanel)panel.Children[0];
            var input = (System.Windows.Controls.TextBox)panel.Children[1];
            var buttons = toolbar.Children.OfType<System.Windows.Controls.Button>().ToArray();
            Assert.Equal(new[] { "大標", "次標", "小標", "粗體", "斜體", "無序清單", "有序清單", "分隔線", "簡易超連結", "程式碼", "程式碼區塊", "註腳", "勾選框", "Windows emoji" }, buttons.Select(b => (string)b.Content));
            void Click(string label) => buttons.Single(b => (string)b.Content == label).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            input.SelectAll(); Click("有序清單");
            Assert.Contains("1. 出貨\n2. 核帳", editor.Text);
            Click("註腳"); Click("註腳");
            Assert.Contains("[^1]:", editor.Text); Assert.Contains("[^2]:", editor.Text);
            var view = new MarkdownView { Text = "請核對[^1]。\n\n[^1]: 核對發票與金額。" };
            var text = new TextRange(view.Document.ContentStart, view.Document.ContentEnd).Text;
            Assert.Contains("核對發票與金額", text); Assert.DoesNotContain("[^1]", text);
            var first = (Paragraph)view.Document.Blocks.FirstBlock;
            Assert.Contains(first.Inlines.OfType<Hyperlink>().SelectMany(link => link.Inlines).OfType<Span>(), s => s.BaselineAlignment == System.Windows.BaselineAlignment.Superscript);
            return Task.CompletedTask;
        });
    }
    [Fact] public async Task Emoji_library_persists_reloads_and_rejects_oversized_entries()
    {
        var paths = new EmojiPaths();
        using var host = new HostBuilder().ConfigureServices(s => { CompositionRoot.ConfigureServices(s); s.AddSingleton<IAppPaths>(paths); }).Build();
        try
        {
            await host.Services.GetRequiredService<IDatabaseInitializer>().InitializeAsync();
            var library = host.Services.GetRequiredService<EmojiLibrary>();
            await library.LoadAsync(); Assert.Contains("📦", library.Items);
            await library.SaveAsync("🛒\n✅\n🛒");
            var reloaded = new EmojiLibrary(host.Services.GetRequiredService<CloudAlarmOverlay.Core.Repositories.ISettingsRepository>());
            await reloaded.LoadAsync(); Assert.Equal(new[] { "🛒", "✅" }, reloaded.Items);
            await Assert.ThrowsAsync<ArgumentException>(() => library.SaveAsync(new string('x', 25)));
            await reloaded.LoadAsync(); Assert.Equal(2, reloaded.Items.Count);
            await library.SaveAsync(""); await reloaded.LoadAsync(); Assert.Empty(reloaded.Items);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if(System.IO.Directory.Exists(paths.DataDirectory)) System.IO.Directory.Delete(paths.DataDirectory, true); }
    }
    private sealed class EmojiPaths : IAppPaths
    {
        public string DataDirectory { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CloudAlarmEmojiTests", Guid.NewGuid().ToString("N"));
        public string DatabasePath => System.IO.Path.Combine(DataDirectory, "test.db");
    }
    [Fact] public async Task Task_boxes_update_markdown_and_code_blocks_stay_literal()
    {
        await MilestoneOneTests.RunSta(() =>
        {
            var view = new MarkdownView { EditableTasks = true, Text = "- [ ] 出貨\n- [x] 核帳\n\n```\n- [ ] 範例\n```" };
            var list = view.Document.Blocks.OfType<System.Windows.Documents.List>().Single();
            var boxes = list.ListItems.SelectMany(i => i.Blocks.OfType<Paragraph>()).SelectMany(p => p.Inlines).OfType<InlineUIContainer>().Select(i => (System.Windows.Controls.CheckBox)i.Child).ToArray();
            Assert.Equal(2, boxes.Length); Assert.False(boxes[0].IsChecked); Assert.True(boxes[1].IsChecked);
            boxes[0].IsChecked = true;
            boxes[0].RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.StartsWith("- [x] 出貨", view.Text);
            Assert.EndsWith("- [ ] 範例\n```", view.Text);
            var notice = new MarkdownView { Text = "- [ ] 出貨" };
            var box = (System.Windows.Controls.CheckBox)((Paragraph)((System.Windows.Documents.List)notice.Document.Blocks.FirstBlock).ListItems.FirstListItem.Blocks.FirstBlock).Inlines.OfType<InlineUIContainer>().Single().Child;
            Assert.False(box.IsEnabled);
            var editor = new MarkdownEditor { Text = "- [ ] 本機項目" };
            var preview = (MarkdownView)((System.Windows.Controls.ScrollViewer)((System.Windows.Controls.TabItem)editor.Items[2]).Content).Content;
            var editableBox = (System.Windows.Controls.CheckBox)((Paragraph)((System.Windows.Documents.List)preview.Document.Blocks.FirstBlock).ListItems.FirstListItem.Blocks.FirstBlock).Inlines.OfType<InlineUIContainer>().Single().Child;
            editableBox.IsChecked = true;
            editableBox.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.Equal("- [x] 本機項目", editor.Text);
            return Task.CompletedTask;
        });
    }
    [Fact]
    public async Task Markdown_renders_text_formats_and_never_loads_images_or_active_html()
    {
        await MilestoneOneTests.RunSta(() =>
        {
            var view = new MarkdownView { Text = "# 一級標題\n\n## 二級標題\n\n*斜體* **粗體** `Monospace`  \n換行\n\n---\n\n* 張三\n* 李四\n\n1. 不論\n2. 三七\n\n> 引用\n\n```text\ncode block\n```\n\n[連結](https://example.com)\n\n<abbr title=\"Hypertext Markup Language\">HTML</abbr>\n\n![替代文字](https://example.com/image.png)\n\n<script>forbidden</script>\n\n[危險](javascript:alert(1))" };
            var blocks = view.Document.Blocks.ToArray();
            Assert.Equal(28, ((Paragraph)blocks[0]).FontSize);
            Assert.Equal(23, ((Paragraph)blocks[1]).FontSize);
            var paragraph = (Paragraph)blocks[2];
            Assert.Contains(paragraph.Inlines, i => i is Bold);
            Assert.Contains(paragraph.Inlines, i => i is Italic);
            Assert.Contains(paragraph.Inlines, i => i is LineBreak);
            Assert.Equal(2, blocks.OfType<System.Windows.Documents.List>().Count());
            Assert.Contains(blocks, b => b is Section);
            var inlines = blocks.OfType<Paragraph>().SelectMany(p => p.Inlines).ToArray();
            Assert.Single(inlines.OfType<Hyperlink>());
            Assert.Contains(inlines, i => i.ToolTip?.ToString() == "Hypertext Markup Language");
            Assert.DoesNotContain(inlines, i => i is InlineUIContainer);
            var text = new TextRange(view.Document.ContentStart, view.Document.ContentEnd).Text;
            Assert.Contains("替代文字", text); Assert.Contains("code block", text); Assert.DoesNotContain("forbidden", text);
            Assert.Null(MarkdownView.WebLink("file:///C:/test"));
            Assert.Null(MarkdownView.WebLink("javascript:alert(1)"));
            view.Text = "新的 **內容** :smile: 📦";
            Assert.DoesNotContain("一級標題", new TextRange(view.Document.ContentStart, view.Document.ContentEnd).Text);
            Assert.DoesNotContain(":smile:", new TextRange(view.Document.ContentStart, view.Document.ContentEnd).Text);
            var editor = new MarkdownEditor { Text = "- 出貨", MaxLength = 500 };
            Assert.Equal(new[] { "純文字", "MD 格式", "預覽" }, editor.Items.Cast<System.Windows.Controls.TabItem>().Select(t => t.Header));
            var plainPanel = (System.Windows.Controls.StackPanel)((System.Windows.Controls.TabItem)editor.Items[0]).Content;
            var plain = (System.Windows.Controls.TextBox)plainPanel.Children[1];
            Assert.Equal("- 出貨", plain.Text); Assert.Equal(500, plain.MaxLength);
            plain.Text = "**核對訂單**"; Assert.Equal(plain.Text, editor.Text);
            return Task.CompletedTask;
        });
    }

    [Fact] public async Task Plain_text_toolbar_inserts_symbols_and_common_emoji_at_caret()
    {
        await MilestoneOneTests.RunSta(async () =>
        {
            var editor = new MarkdownEditor { Text = "ABC", MaxLength = 6, Emojis = new[] { "📦" } };
            var window = new System.Windows.Window { Content = editor, Width = 600, Height = 400, ShowInTaskbar = false, ShowActivated = false };
            try
            {
                window.Show();
                window.UpdateLayout();
                var plainPanel = (System.Windows.Controls.StackPanel)((System.Windows.Controls.TabItem)editor.Items[0]).Content;
                var toolbar = (System.Windows.Controls.WrapPanel)plainPanel.Children[0];
                var input = (System.Windows.Controls.TextBox)plainPanel.Children[1];
                Assert.Single(toolbar.Children.OfType<SymbolPicker>());
                Assert.Single(toolbar.Children.OfType<EmojiPicker>());
                Assert.Contains(toolbar.Children.OfType<System.Windows.Controls.Button>(), button => (string)button.Content == "Windows emoji");

                input.Select(1, 0);
                var symbols = toolbar.Children.OfType<SymbolPicker>().Single();
                var requestedSymbols = new[] { "⭠", "↑", "→", "↓", "√", "▶", "◀", "●", "★", "☐", "☑", "✓", "✔", "✘", "☺", "☹", "☻", "☯", "❤", "➤" };
                Assert.Equal(requestedSymbols, symbols.Symbols.Take(requestedSymbols.Length));
                Assert.Equal(symbols.Symbols.Count, symbols.Symbols.Distinct().Count());
                ((System.Windows.Controls.Button)symbols.FindName("OpenButton")).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                var symbolPopup = (System.Windows.Controls.Primitives.Popup)symbols.FindName("PickerPopup");
                Assert.True(symbolPopup.IsOpen);
                FindButton(symbolPopup.Child, "☑").RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.Equal("A☑BC", editor.Text);
                Assert.Equal(2, input.CaretIndex);

                var emojis = toolbar.Children.OfType<EmojiPicker>().Single();
                ((System.Windows.Controls.Button)emojis.FindName("OpenButton")).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                var emojiPopup = (System.Windows.Controls.Primitives.Popup)emojis.FindName("PickerPopup");
                FindButton(emojiPopup.Child, "📦").RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.Equal("A☑📦BC", editor.Text);
                Assert.Equal(4, input.CaretIndex);

                ((System.Windows.Controls.Button)symbols.FindName("OpenButton")).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                FindButton(symbolPopup.Child, "★").RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.Equal("A☑📦BC", editor.Text);
                editor.SelectedIndex = 1;
                var formatted = (System.Windows.Controls.StackPanel)((System.Windows.Controls.TabItem)editor.Items[1]).Content;
                Assert.Equal(editor.Text, ((System.Windows.Controls.TextBox)formatted.Children[1]).Text);
            }
            finally { window.Close(); }
        });
    }

    private static System.Windows.Controls.Button FindButton(System.Windows.DependencyObject root, string content)
    {
        if (root is System.Windows.Controls.Button { Content: string text } button && text == content) return button;
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            try { return FindButton(child, content); } catch (InvalidOperationException) { }
        }
        throw new InvalidOperationException($"Button not found: {content}");
    }
}

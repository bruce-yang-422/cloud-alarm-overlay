using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace CloudAlarmOverlay.App.Controls;

public sealed class MarkdownEditor : TabControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(MarkdownEditor), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public static readonly DependencyProperty MaxLengthProperty = DependencyProperty.Register(nameof(MaxLength), typeof(int), typeof(MarkdownEditor), new PropertyMetadata(1000));
    public int MaxLength { get => (int)GetValue(MaxLengthProperty); set => SetValue(MaxLengthProperty, value); }
    public static readonly DependencyProperty EmojisProperty = DependencyProperty.Register(nameof(Emojis), typeof(System.Collections.IEnumerable), typeof(MarkdownEditor), new PropertyMetadata(null));
    public System.Collections.IEnumerable? Emojis { get => (System.Collections.IEnumerable?)GetValue(EmojisProperty); set => SetValue(EmojisProperty, value); }
    private TextBox MakeInput(string name)
    {
        var input = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 180, MaxHeight = 320, FontSize = 14, Padding = new Thickness(12), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 8, 0, 0) };
        input.SetBinding(TextBox.TextProperty, new Binding(nameof(Text)) { Source = this, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        input.SetBinding(TextBox.MaxLengthProperty, new Binding(nameof(MaxLength)) { Source = this });
        System.Windows.Automation.AutomationProperties.SetName(input, name);
        return input;
    }
    private void InsertText(TextBox input, string value)
    {
        if (string.IsNullOrEmpty(value) || input.Text.Length - input.SelectionLength + value.Length > MaxLength) return;
        var start = input.SelectionStart;
        input.SelectedText = value;
        input.Focus();
        input.Select(start + value.Length, 0);
    }
    public MarkdownEditor()
    {
        SetResourceReference(StyleProperty, "SegmentedTabs");
        var input = MakeInput("MD 格式編輯");
        var formatted = new StackPanel();
        var toolbar = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        void Add(string label, string before, string after = "", string placeholder = "文字")
        {
            var button = new Button { Content = label, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 4, 4), ToolTip = "插入" + label };
            button.Click += (_, _) =>
            {
                var selection = input.SelectedText;
                var insert = before + (selection.Length > 0 ? selection : placeholder) + after;
                if (input.Text.Length - selection.Length + insert.Length > MaxLength) return;
                var start = input.SelectionStart;
                input.SelectedText = insert;
                input.Focus(); input.Select(start + before.Length, selection.Length > 0 ? selection.Length : placeholder.Length);
            };
            toolbar.Children.Add(button);
        }
        void ActionButton(string label, Action action)
        {
            var button = new Button { Content = label, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 4, 4), ToolTip = "插入" + label };
            button.Click += (_, _) => action(); toolbar.Children.Add(button);
        }
        void Lines(string label, Func<int, string> prefix)
        {
            ActionButton(label, () =>
            {
                var start = input.SelectionStart;
                var selected = input.SelectedText.Length > 0 ? input.SelectedText : "項目";
                var lines = selected.Replace("\r\n", "\n").Split('\n');
                var insert = "\n\n" + string.Join("\n", lines.Select((line, index) => prefix(index) + line)) + "\n\n";
                if (input.Text.Length - input.SelectionLength + insert.Length > MaxLength) return;
                input.SelectedText = insert; input.Focus(); input.Select(start + 2, insert.Length - 4);
            });
        }
        Add("大標", "\n\n# ", "\n\n", "標題");
        Add("次標", "\n\n## ", "\n\n", "標題");
        Add("小標", "\n\n### ", "\n\n", "標題");
        Add("粗體", "**", "**"); Add("斜體", "*", "*");
        Lines("無序清單", _ => "- "); Lines("有序清單", i => $"{i + 1}. ");
        ActionButton("分隔線", () =>
        {
            const string rule = "\n\n---\n\n";
            if (input.Text.Length + rule.Length > MaxLength) return;
            var position = input.SelectionStart; input.Select(position, 0); input.SelectedText = rule; input.Focus(); input.Select(position + rule.Length, 0);
        });
        Add("簡易超連結", "[", "](https://example.com)", "連結文字");
        Add("程式碼", "`", "`", "程式碼");
        Add("程式碼區塊", "\n\n```\n", "\n```\n\n", "程式碼");
        ActionButton("註腳", () =>
        {
            var id = 1;
            while (input.Text.Contains($"[^{id}]", StringComparison.Ordinal)) id++;
            var reference = $"[^{id}]";
            var definition = $"\n\n[^{id}]: 註腳說明";
            if (input.Text.Length + reference.Length + definition.Length > MaxLength) return;
            var position = input.SelectionStart + input.SelectionLength;
            var updated = input.Text.Insert(position, reference) + definition;
            input.Text = updated; input.Focus(); input.Select(updated.Length - 4, 4);
        });
        Lines("勾選框", _ => "- [ ] ");
        var picker = new EmojiPicker();
        picker.SetBinding(EmojiPicker.ItemsProperty, new Binding(nameof(Emojis)) { Source = this });
        picker.Selected += emoji => InsertText(input, emoji);
        toolbar.Children.Add(picker);
        ActionButton("Windows emoji", () => CloudAlarmOverlay.App.Services.WindowsEmojiPanel.Open(input));
        formatted.Children.Add(toolbar); formatted.Children.Add(input);
        var preview = new MarkdownView { MinHeight = 180, EditableTasks = true };
        preview.SetBinding(MarkdownView.TextProperty, new Binding(nameof(Text)) { Source = this, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        var plainInput = MakeInput("純文字編輯");
        var plain = new StackPanel();
        var plainToolbar = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var symbolPicker = new SymbolPicker();
        symbolPicker.Selected += symbol => InsertText(plainInput, symbol);
        plainToolbar.Children.Add(symbolPicker);
        var plainEmojiPicker = new EmojiPicker();
        plainEmojiPicker.SetBinding(EmojiPicker.ItemsProperty, new Binding(nameof(Emojis)) { Source = this });
        plainEmojiPicker.Selected += emoji => InsertText(plainInput, emoji);
        plainToolbar.Children.Add(plainEmojiPicker);
        var windowsEmoji = new Button { Content = "Windows emoji", Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 4, 4), ToolTip = "開啟 Windows emoji 面板（Win + .）" };
        System.Windows.Automation.AutomationProperties.SetName(windowsEmoji, "開啟 Windows emoji");
        windowsEmoji.Click += (_, _) => CloudAlarmOverlay.App.Services.WindowsEmojiPanel.Open(plainInput);
        plainToolbar.Children.Add(windowsEmoji);
        plain.Children.Add(plainToolbar);
        plain.Children.Add(plainInput);
        Items.Add(new TabItem { Header = "純文字", Content = plain });
        Items.Add(new TabItem { Header = "MD 格式", Content = formatted });
        Items.Add(new TabItem { Header = "預覽", Content = new ScrollViewer { Content = preview, MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace CloudAlarmOverlay.App.Controls;

public sealed class EmojiGlyph : ContentControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(EmojiGlyph), new PropertyMetadata("", (d, _) => ((EmojiGlyph)d).Render()));
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    private static readonly Dictionary<string, string> Names = new()
    {
        ["✅"]="check_mark_button", ["📦"]="package", ["⚠"]="warning", ["🛒"]="shopping_cart", ["🚚"]="delivery_truck", ["💰"]="money_bag", ["🧾"]="receipt", ["📅"]="calendar", ["📞"]="telephone_receiver", ["⏰"]="alarm_clock", ["📌"]="pushpin", ["👍"]="thumbs_up", ["😊"]="smiling_face_with_smiling_eyes"
    };
    private static readonly Dictionary<string, ImageSource> Cache = new();
    public static ImageSource? FindImage(string text)
    {
        if (!Names.TryGetValue(text.Replace("\uFE0F", ""), out var name)) return null;
        if (Cache.TryGetValue(name, out var cached)) return cached;
        var suffix = name == "thumbs_up" ? "_3d_default.png" : "_3d.png";
        var image = new BitmapImage(new Uri("pack://application:,,,/CloudAlarmOverlay.App;component/Assets/Emoji/" + name + suffix));
        image.Freeze(); Cache[name] = image; return image;
    }
    private void Render()
    {
        Content = FindImage(Text ?? "") is {} image
            ? new Image { Source = image, Stretch = Stretch.Uniform, ToolTip = Text }
            : new TextBlock { Text = Text, FontFamily = new FontFamily("Segoe UI Emoji"), FontSize = 22, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(this, Text);
    }
}

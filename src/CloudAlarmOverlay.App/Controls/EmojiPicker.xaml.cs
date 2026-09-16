using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
namespace CloudAlarmOverlay.App.Controls;

public partial class EmojiPicker : UserControl
{
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(nameof(Items), typeof(IEnumerable), typeof(EmojiPicker));
    public IEnumerable? Items { get => (IEnumerable?)GetValue(ItemsProperty); set => SetValue(ItemsProperty, value); }
    public event Action<string>? Selected;
    public EmojiPicker() { InitializeComponent(); Unloaded += (_, _) => PickerPopup.IsOpen = false; }
    private void OpenPicker(object sender, RoutedEventArgs e) => PickerPopup.IsOpen = !PickerPopup.IsOpen;
    private void OnOpened(object? sender, EventArgs e) => Choices.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    private void OnClosed(object? sender, EventArgs e) => OpenButton.Focus();
    private void OnPopupKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { PickerPopup.IsOpen = false; e.Handled = true; }
    }
    private void ChooseEmoji(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Content: string emoji }) { PickerPopup.IsOpen = false; Selected?.Invoke(emoji); }
    }
}

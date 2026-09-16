using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CloudAlarmOverlay.App.Controls;

public partial class SymbolPicker : UserControl
{
    public IReadOnlyList<string> Symbols { get; } =
    [
        "⭠", "↑", "→", "↓", "√", "▶", "◀", "●",
        "★", "☐", "☑", "✓", "✔", "✘", "☺", "☹",
        "☻", "☯", "❤", "➤",
        "✕", "☆", "✦", "•", "※", "←", "↗", "↘", "…", "—",
        "「", "」", "『", "』", "①", "②", "③", "④",
        "⑤", "°", "℃", "±", "≥", "≤", "≈", "∞",
        "©", "®", "™"
    ];

    public event Action<string>? Selected;

    public SymbolPicker()
    {
        InitializeComponent();
        Unloaded += (_, _) => PickerPopup.IsOpen = false;
    }

    private void OpenPicker(object sender, RoutedEventArgs e) => PickerPopup.IsOpen = !PickerPopup.IsOpen;
    private void OnOpened(object? sender, EventArgs e) => Choices.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    private void OnClosed(object? sender, EventArgs e) => OpenButton.Focus();
    private void OnPopupKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { PickerPopup.IsOpen = false; e.Handled = true; }
    }
    private void ChooseSymbol(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Content: string symbol }) { PickerPopup.IsOpen = false; Selected?.Invoke(symbol); }
    }
}

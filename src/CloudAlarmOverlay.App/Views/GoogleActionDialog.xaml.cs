using System.Windows;
using System.Windows.Input;

namespace CloudAlarmOverlay.App.Views;

public partial class GoogleActionDialog : Window
{
    public GoogleActionDialog(string title, string subject, string description, string action, bool destructive = false, bool information = false)
    {
        InitializeComponent();
        Title = Heading.Text = title;
        Subject.Text = subject;
        SubjectCard.Visibility = string.IsNullOrEmpty(subject) ? Visibility.Collapsed : Visibility.Visible;
        Description.Text = description;
        ConfirmButton.Content = action;
        ConfirmButton.Style = (Style)FindResource(destructive ? "SettingsDestructive" : "SettingsPrimary");
        Symbol.Text = destructive ? "\uE74D" : "\uE77B";
        if (information)
        {
            CancelButton.Visibility = Visibility.Collapsed;
            CancelButton.IsDefault = false;
            ConfirmButton.IsDefault = true;
        }
        Loaded += (_, _) => { if (information) ConfirmButton.Focus(); else CancelButton.Focus(); };
    }

    private void Confirm(object sender, RoutedEventArgs e) => DialogResult = true;
    private void DragHeader(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
}

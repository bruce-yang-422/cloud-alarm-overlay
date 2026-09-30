using System.Windows;
using System.Windows.Input;
using CloudAlarmOverlay.App.ViewModels;

namespace CloudAlarmOverlay.App.Views;

public partial class GoogleSignInDialog : Window
{
    private readonly GoogleWorkspaceViewModel workspace;
    public GoogleSignInDialog(GoogleWorkspaceViewModel workspace)
    {
        this.workspace = workspace;
        InitializeComponent();
        MaxHeight = Math.Max(400, SystemParameters.WorkArea.Height - 32);
        AccountLabelInput.Text = workspace.AccountLabel;
        SheetsToggle.IsChecked = workspace.UseSheets;
        CalendarToggle.IsChecked = workspace.UseCalendar;
        WriteToggle.IsChecked = workspace.UseSheets && workspace.WriteSheets;
        UpdateInput();
        Loaded += (_, _) => { AccountLabelInput.Focus(); AccountLabelInput.SelectAll(); };
    }

    private void InputChanged(object sender, RoutedEventArgs e) => UpdateInput();
    private void UpdateInput()
    {
        if (ContinueButton is null) return;
        WriteToggle.IsEnabled = SheetsToggle.IsChecked == true;
        if (!WriteToggle.IsEnabled) WriteToggle.IsChecked = false;
        ContinueButton.IsEnabled = !string.IsNullOrWhiteSpace(AccountLabelInput.Text) && (SheetsToggle.IsChecked == true || CalendarToggle.IsChecked == true);
        ValidationText.Visibility = ContinueButton.IsEnabled ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Continue(object sender, RoutedEventArgs e)
    {
        if (!ContinueButton.IsEnabled) return;
        workspace.AccountLabel = AccountLabelInput.Text.Trim();
        workspace.UseSheets = SheetsToggle.IsChecked == true;
        workspace.UseCalendar = CalendarToggle.IsChecked == true;
        workspace.WriteSheets = WriteToggle.IsChecked == true && workspace.UseSheets;
        DialogResult = true;
    }

    private void DragHeader(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
}

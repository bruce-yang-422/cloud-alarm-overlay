using System.Windows.Controls;
namespace CloudAlarmOverlay.App.Views;
public partial class PreferencesView:UserControl
{
    public PreferencesView()=>InitializeComponent();
    private void OpenWindowsEmoji(object sender, System.Windows.RoutedEventArgs e) => CloudAlarmOverlay.App.Services.WindowsEmojiPanel.Open(NewEmojiInput);
}

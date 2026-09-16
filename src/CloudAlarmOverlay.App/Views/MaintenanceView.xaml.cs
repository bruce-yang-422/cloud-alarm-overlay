using System.Windows;
using System.Windows.Controls;
namespace CloudAlarmOverlay.App.Views;
public partial class MaintenanceView : UserControl
{
    public MaintenanceView()=>InitializeComponent();
    private async void ResetAll(object sender,RoutedEventArgs e)
    {
        if(Application.Current is App app)await app.RequestFactoryResetAsync();
    }
}

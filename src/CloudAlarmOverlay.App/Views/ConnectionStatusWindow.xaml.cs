using System.Windows;
using CloudAlarmOverlay.App.ViewModels;

namespace CloudAlarmOverlay.App.Views;
public partial class ConnectionStatusWindow : Window
{
    public string? Destination {get;private set;}
    public ConnectionStatusWindow(MainViewModel vm)
    {
        InitializeComponent();DataContext=vm;MaxHeight=Math.Max(400,SystemParameters.WorkArea.Height-40);
    }
    private async void Refresh(object sender,RoutedEventArgs e)
    {
        RefreshButton.IsEnabled=false;
        try{await ((MainViewModel)DataContext).RefreshHealthAsync();}
        finally{RefreshButton.IsEnabled=true;}
    }
    private void ManageGoogle(object sender,RoutedEventArgs e){Destination="Google";DialogResult=true;}
    private void ManageShared(object sender,RoutedEventArgs e){Destination="Shared";DialogResult=true;}
}

using System.Windows;
using System.Windows.Input;
using CloudAlarmOverlay.Core.Models;

namespace CloudAlarmOverlay.App.Views;

public partial class GoogleAccountNameDialog : Window
{
    public string AccountName=>NameInput.Text.Trim();
    public GoogleAccountNameDialog(GoogleAccount account)
    {
        InitializeComponent();
        AccountEmail.Text=account.Email;NameInput.Text=account.Label;
        Loaded+=(_,_)=>{NameInput.Focus();NameInput.SelectAll();};
    }
    private void InputChanged(object sender,RoutedEventArgs e)
    {
        if(SaveButton is not null)SaveButton.IsEnabled=AccountName.Length is >=1 and <=40 && !AccountName.Any(char.IsControl);
    }
    private void Save(object sender,RoutedEventArgs e){if(SaveButton.IsEnabled)DialogResult=true;}
    private void DragHeader(object sender,MouseButtonEventArgs e){if(e.LeftButton==MouseButtonState.Pressed)DragMove();}
}

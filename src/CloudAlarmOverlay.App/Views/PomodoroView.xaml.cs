using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using CloudAlarmOverlay.App.ViewModels;
namespace CloudAlarmOverlay.App.Views;
public partial class PomodoroView:UserControl
{
    private PomodoroViewModel? model;
    public PomodoroView()
    {
        InitializeComponent();
        Loaded+=(_,_)=>{model=DataContext as PomodoroViewModel;if(model is not null)model.PropertyChanged+=Changed;MoveEncouragement();};
        Unloaded+=(_,_)=>{if(model is not null)model.PropertyChanged-=Changed;};
    }
    private void Changed(object? sender,PropertyChangedEventArgs e){if(e.PropertyName==nameof(PomodoroViewModel.HasProgress))MoveEncouragement();}
    private void MoveEncouragement()
    {
        var target=model?.HasProgress==true?ProgressSide:TrendSide;
        if(ReferenceEquals(EncouragementNode.Parent,target))return;
        (EncouragementNode.Parent as Panel)?.Children.Remove(EncouragementNode);
        if(target==TrendSide)target.Children.Insert(0,EncouragementNode);else target.Children.Add(EncouragementNode);
    }
    private void OpenHistory(object sender,RoutedEventArgs e)
    {
        if(Window.GetWindow(this)?.DataContext is MainViewModel main){main.PageIndex=2;main.HistoryTabIndex=1;}
    }
}
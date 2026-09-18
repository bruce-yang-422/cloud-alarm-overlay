using System.Windows;
using System.Windows.Controls;
using CloudAlarmOverlay.App.ViewModels;

namespace CloudAlarmOverlay.App.Views;

public partial class CountdownsView : UserControl
{
    public static readonly DependencyProperty CardColumnsProperty = DependencyProperty.Register(
        nameof(CardColumns), typeof(int), typeof(CountdownsView), new PropertyMetadata(1));
    public int CardColumns { get => (int)GetValue(CardColumnsProperty); set => SetValue(CardColumnsProperty, value); }
    public CountdownsView() => InitializeComponent();
    private void UpdateCardColumns(object sender, SizeChangedEventArgs e)
        => CardColumns = e.NewSize.Width >= 1080 ? 3 : e.NewSize.Width >= 760 ? 2 : 1;
    private void ShowEditor(object sender, RoutedEventArgs e)
    {
        if (DataContext is not CountdownsViewModel vm || vm.IsBusy) return;
        if (sender is FrameworkElement { DataContext: CountdownRow row }) vm.EditCommand.Execute(row);
        else vm.NewCommand.Execute(null);
        var editor = new CountdownEditorWindow(vm) { Owner = Window.GetWindow(this) };
        editor.ShowDialog();
    }
}

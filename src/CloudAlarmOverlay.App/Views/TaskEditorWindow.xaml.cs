using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CloudAlarmOverlay.App.ViewModels;
namespace CloudAlarmOverlay.App.Views;

public partial class TaskEditorWindow : Window
{
    private bool? compactLayout;
    public TaskEditorWindow()
    {
        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height;
        MaxWidth = SystemParameters.WorkArea.Width;
        SizeChanged += (_, _) => UpdateLayoutColumns();
        Loaded += (_, _) => UpdateLayoutColumns();
        PreviewKeyDown += async (_, e) =>
        {
            if(e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled=true;
                HourWheel.CommitInput(); MinuteWheel.CommitInput();
                if(DataContext is TaskEditorViewModel vm && vm.SaveCommand.CanExecute(null))
                    await vm.SaveCommand.ExecuteAsync(null);
            }
        };
    }

    private void UpdateLayoutColumns()
    {
        var compact = ActualWidth < 760;
        if (compactLayout == compact) return;
        compactLayout = compact;
        FormColumns.ColumnDefinitions[0].Width = new GridLength(compact ? 1 : 1.3, GridUnitType.Star);
        FormColumns.ColumnDefinitions[1].Width = new GridLength(compact ? 0 : 18);
        FormColumns.ColumnDefinitions[2].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(ScheduleColumn, compact ? 0 : 2);
        Grid.SetRow(ScheduleColumn, compact ? 1 : 0);
        Grid.SetRow(DetailsSection, compact ? 2 : 1);
    }

    private void OnDateSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (DateToggle is not null && e.AddedItems.Count > 0) DateToggle.IsChecked = false;
    }

    private void OnFormScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (ScrollHint is not null)
            ScrollHint.Visibility = FormScroll.ScrollableHeight - FormScroll.VerticalOffset > 2
                ? Visibility.Visible : Visibility.Collapsed;
    }
}

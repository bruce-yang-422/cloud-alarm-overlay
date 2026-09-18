using System.ComponentModel;
using System.Windows;
using CloudAlarmOverlay.App.ViewModels;

namespace CloudAlarmOverlay.App.Views;

public partial class CountdownEditorWindow : Window
{
    private readonly CountdownsViewModel viewModel;
    public CountdownEditorWindow(CountdownsViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;
        MaxHeight = SystemParameters.WorkArea.Height;
        MaxWidth = SystemParameters.WorkArea.Width;
        viewModel.Saved += OnSaved;
        viewModel.PropertyChanged += OnDraftChanged;
        foreach (var choice in viewModel.Weekdays.Concat(viewModel.Months).Concat(viewModel.LunarDays))
            choice.PropertyChanged += OnDraftChanged;
        Loaded += (_, _) =>
        {
            if (DirectionSelector.ItemContainerGenerator.ContainerFromIndex(DirectionSelector.SelectedIndex) is UIElement selectedDirection)
                selectedDirection.Focus();
            else DirectionSelector.Focus();
            RefreshPreview(); ResizePreview();
        };
        SizeChanged += (_, _) => ResizePreview();
        Closed += (_, _) =>
        {
            viewModel.Saved -= OnSaved;
            viewModel.PropertyChanged -= OnDraftChanged;
            foreach (var choice in viewModel.Weekdays.Concat(viewModel.Months).Concat(viewModel.LunarDays))
                choice.PropertyChanged -= OnDraftChanged;
        };
    }

    private void ResizePreview()
    {
        var sideBySide = ActualWidth >= 860;
        PreviewColumn.Width = new GridLength(sideBySide ? 310 : 0);
        PreviewGap.Width = new GridLength(sideBySide ? 20 : 0);
        System.Windows.Controls.Grid.SetRow(PreviewPanel, sideBySide ? 0 : 1);
        System.Windows.Controls.Grid.SetColumn(PreviewPanel, sideBySide ? 2 : 0);
        PreviewPanel.Margin = sideBySide ? new Thickness(0) : new Thickness(0, 18, 10, 0);
    }

    private void OnDraftChanged(object? sender, PropertyChangedEventArgs e) => RefreshPreview();

    private void RefreshPreview()
    {
        try
        {
            PreviewCard.Content = viewModel.CreateEditorPreview();
            PreviewError.Text = "";
        }
        catch (ArgumentException ex)
        {
            PreviewCard.Content = null;
            PreviewError.Text = ex.Message;
        }
    }

    private void OnSaved(object? sender, EventArgs e) => Close();
    protected override void OnClosing(CancelEventArgs e)
    {
        // Keep the dialog open while a save is in flight, including Escape and the title-bar close button.
        if (viewModel.IsBusy) e.Cancel = true;
        base.OnClosing(e);
    }
}

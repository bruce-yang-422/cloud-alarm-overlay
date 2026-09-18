using System.ComponentModel;
using System.Windows;
using CloudAlarmOverlay.App.ViewModels;
using CloudAlarmOverlay.Core.Recurrence;

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
        Loaded += (_, _) => { EventTitle.Focus(); RefreshPreview(); ResizePreview(); };
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
        PreviewTitle.Text = string.IsNullOrWhiteSpace(viewModel.Title) ? "我的紀念事件" : viewModel.Title;
        try
        {
            var row = viewModel.CreateEditorPreview();
            PreviewValue.Text = row.HomeSummary;
            PreviewDate.Text = row.IsScheduleUnavailable ? "尚無可用提醒日期" : row.DateCaption;
            PreviewRepeat.Text = RecurrenceRule.Describe(row.Item.EffectiveRecurrence);
            PreviewReminder.Text = row.HasNextReminderLabel ? row.NextReminderLabel : "不提醒";
        }
        catch (ArgumentException ex)
        {
            PreviewValue.Text = "—";
            PreviewDate.Text = ex.Message;
            PreviewRepeat.Text = viewModel.Repeat;
            PreviewReminder.Text = "完成設定後顯示預覽";
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

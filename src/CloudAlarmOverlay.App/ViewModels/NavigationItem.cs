using CommunityToolkit.Mvvm.ComponentModel;

namespace CloudAlarmOverlay.App.ViewModels;

public sealed partial class NavigationItem(string title, string icon, int pageIndex) : ObservableObject
{
    public string Title { get; } = title;
    public string Icon { get; } = icon;
    // A negative index is an action that opens a tool without changing the current page.
    public int PageIndex { get; } = pageIndex;
    [ObservableProperty] private bool isSelected = pageIndex == 0;
}

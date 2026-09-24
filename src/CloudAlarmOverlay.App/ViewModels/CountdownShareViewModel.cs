using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CloudAlarmOverlay.App.Services;
using CloudAlarmOverlay.Core.Models;
namespace CloudAlarmOverlay.App.ViewModels;

public partial class CountdownShareViewModel : ObservableObject
{
    private readonly CountdownShareRenderer renderer;
    private readonly bool dark;
    private readonly ThemeColorStyle colorStyle;
    private bool ready;
    public CountdownShareSnapshot Snapshot { get; }
    public string[] Sizes { get; } = ["1:1 正方形 · 1080 × 1080", "2:3 直式 · 1080 × 1620"];
    public string[] Backgrounds { get; } = ["柔和漸層", "純色", "幾何光圈"];
    [ObservableProperty] private int sizeIndex;
    [ObservableProperty] private int backgroundIndex;
    [ObservableProperty] private bool showBranding;
    [ObservableProperty] private BitmapSource? previewImage;
    [ObservableProperty] private string message="";
    public string CapturedLabel => $"數字固定於 {Snapshot.CapturedAt:yyyy/MM/dd HH:mm}，重新開啟可更新。";
    public CountdownShareViewModel(CountdownShareSnapshot snapshot,CountdownShareRenderer renderer,bool dark,ThemeColorStyle colorStyle,bool branding)
    {
        Snapshot=snapshot;this.renderer=renderer;this.dark=dark;this.colorStyle=colorStyle;
        ShowBranding=branding;ready=true;Refresh();
    }
    partial void OnSizeIndexChanged(int value)=>Refresh();
    partial void OnBackgroundIndexChanged(int value)=>Refresh();
    partial void OnShowBrandingChanged(bool value)=>Refresh();
    private void Refresh()
    {
        if(!ready)return;
        try
        {
            PreviewImage=renderer.Render(Snapshot,new(dark,colorStyle,(CountdownShareSize)SizeIndex,(CountdownShareBackground)BackgroundIndex,ShowBranding));
            Message="";
        }
        catch(Exception ex){PreviewImage=null;Message="產生圖片失敗："+ex.Message;}
    }
}

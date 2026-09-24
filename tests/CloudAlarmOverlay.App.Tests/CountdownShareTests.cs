using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CloudAlarmOverlay.App.Services;
using CloudAlarmOverlay.App.ViewModels;
using CloudAlarmOverlay.App.Views;
using CloudAlarmOverlay.Core.Models;
namespace CloudAlarmOverlay.App.Tests;
public sealed class CountdownShareTests
{
    private static CountdownShareSnapshot Snapshot=>CountdownShareSnapshot.Create(new(){Title="日本旅行 ✈️",Category="旅行",TargetAt=new(2026,10,24),CreatedAt=new(2026,9,24)},new(2026,9,24,12,0,0));
    [Theory]
    [InlineData(ThemeColorStyle.Default,false)][InlineData(ThemeColorStyle.Default,true)]
    [InlineData(ThemeColorStyle.Pink,false)][InlineData(ThemeColorStyle.Pink,true)]
    [InlineData(ThemeColorStyle.Bamboo,false)][InlineData(ThemeColorStyle.Bamboo,true)]
    [InlineData(ThemeColorStyle.Lavender,false)][InlineData(ThemeColorStyle.Lavender,true)]
    [InlineData(ThemeColorStyle.Sunset,false)][InlineData(ThemeColorStyle.Sunset,true)]
    [InlineData(ThemeColorStyle.Silver,false)][InlineData(ThemeColorStyle.Silver,true)]
    public Task Both_png_sizes_render_all_themes_and_backgrounds(ThemeColorStyle style,bool dark)=>MilestoneOneTests.RunSta(()=>
    {
        var renderer=new CountdownShareRenderer();
        foreach(var size in Enum.GetValues<CountdownShareSize>())
        foreach(var background in Enum.GetValues<CountdownShareBackground>())
        {
            var bitmap=renderer.Render(Snapshot,new(dark,style,size,background,true));
            Assert.True(bitmap.IsFrozen);Assert.Equal(1080,bitmap.PixelWidth);Assert.Equal(size==CountdownShareSize.Square?1080:1620,bitmap.PixelHeight);
            using var png=new MemoryStream();CountdownShareRenderer.WritePng(bitmap,png);png.Position=0;
            var decoded=BitmapDecoder.Create(png,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad).Frames[0];
            Assert.Equal(bitmap.PixelWidth,decoded.PixelWidth);Assert.Equal(bitmap.PixelHeight,decoded.PixelHeight);Assert.InRange(decoded.DpiX,191.9,192.1);
            var pixel=new byte[4];bitmap.CopyPixels(new Int32Rect(0,0,1,1),pixel,4,0);Assert.Equal(255,pixel[3]);
            Color Pixel(int x,int y){var bytes=new byte[4];bitmap.CopyPixels(new Int32Rect(x,y,1,1),bytes,4,0);return Color.FromRgb(bytes[2],bytes[1],bytes[0]);}
            Assert.True(Contrast(Pixel(120,122),Pixel(500,180))>=4.5,"Share text must remain readable in the selected theme.");
            Save(bitmap,$"share-{style}-{(dark?"dark":"light")}-{size}-{background}");
        }
        return Task.CompletedTask;
    });
    [Fact] public Task Changing_options_keeps_the_same_capture_and_branding_is_optional()=>MilestoneOneTests.RunSta(()=>
    {
        var snapshot=Snapshot;var vm=new CountdownShareViewModel(snapshot,new(),false,ThemeColorStyle.Default,true);
        var initial=Pixels(vm.PreviewImage!);vm.ShowBranding=false;var unbranded=Pixels(vm.PreviewImage!);
        Assert.False(initial.SequenceEqual(unbranded));Assert.Same(snapshot,vm.Snapshot);
        vm.SizeIndex=1;Assert.Equal(1620,vm.PreviewImage!.PixelHeight);Assert.Equal(new DateTime(2026,9,24,12,0,0),vm.Snapshot.CapturedAt);
        vm.BackgroundIndex=2;vm.SizeIndex=0;Assert.Equal(1080,vm.PreviewImage.PixelHeight);
        var noNotes=Snapshot with{Title=new string('旅',70)+" 🎉✈️",Value="9 年 11 個月 30 天",Unit=""};
        Save(new CountdownShareRenderer().Render(noNotes,new(true,ThemeColorStyle.Lavender,CountdownShareSize.Square,CountdownShareBackground.Rings,false)),"share-long-title");
        Assert.DoesNotContain("/",CountdownShareRenderer.FileName(Snapshot with{Title="a/b:c*?"}));
        return Task.CompletedTask;
    });
    [Fact] public Task Preview_remains_the_same_image_when_the_dialog_is_resized()=>MilestoneOneTests.RunSta(async()=>
    {
        var snapshot=Snapshot;var vm=new CountdownShareViewModel(snapshot,new(),false,ThemeColorStyle.Default,true);
        var window=new CountdownShareWindow(vm){ShowActivated=false,ShowInTaskbar=false};
        try
        {
            window.Show();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();
            var original=vm.PreviewImage;window.Width=600;window.Height=550;window.UpdateLayout();
            Assert.Same(original,vm.PreviewImage);Assert.Same(original,((System.Windows.Controls.Image)window.FindName("SharePreview")).Source);
            vm.SizeIndex=1;await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();Assert.Equal(1620,vm.PreviewImage!.PixelHeight);
            var root=(FrameworkElement)window.Content;var capture=new RenderTargetBitmap((int)root.ActualWidth,(int)root.ActualHeight,96,96,PixelFormats.Pbgra32);capture.Render(root);Save(capture,"share-dialog-small");
        }
        finally{window.Close();}
    });
    private static double Contrast(Color a,Color b)
    {
        double L(Color c){double F(byte x){var v=x/255d;return v<=.04045?v/12.92:Math.Pow((v+.055)/1.055,2.4);}return .2126*F(c.R)+.7152*F(c.G)+.0722*F(c.B);}
        var x=L(a);var y=L(b);return (Math.Max(x,y)+.05)/(Math.Min(x,y)+.05);
    }
    private static byte[] Pixels(BitmapSource bitmap)
    {
        var pixels=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];bitmap.CopyPixels(pixels,bitmap.PixelWidth*4,0);return pixels;
    }
    private static void Save(BitmapSource bitmap,string name)
    {
        var path=Environment.GetEnvironmentVariable("CLOUD_ALARM_SHARE_SCREENSHOT_DIR");if(path is null)return;
        Directory.CreateDirectory(path);using var file=File.Create(Path.Combine(path,name+".png"));CountdownShareRenderer.WritePng(bitmap,file);
    }
}

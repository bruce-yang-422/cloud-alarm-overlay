using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using CloudAlarmOverlay.App.Services;
using CloudAlarmOverlay.App.ViewModels;
using Microsoft.Win32;
namespace CloudAlarmOverlay.App.Views;
public partial class CountdownShareWindow : Window
{
    private readonly CountdownShareViewModel vm;
    public CountdownShareWindow(CountdownShareViewModel vm)
    {
        InitializeComponent();DataContext=this.vm=vm;
        MaxHeight=SystemParameters.WorkArea.Height;MaxWidth=SystemParameters.WorkArea.Width;
    }
    private void CopyImage(object sender,RoutedEventArgs e)
    {
        if(vm.PreviewImage is not {} image)return;
        try
        {
            // SetImage gives Windows applications a standard bitmap; PNG preserves the original export too.
            using var png=new MemoryStream();CountdownShareRenderer.WritePng(image,png);png.Position=0;
            var data=new DataObject();data.SetImage(image);data.SetData("PNG",png);
            Clipboard.SetDataObject(data,true);
            vm.Message="圖片已複製，可貼到 LINE、Teams 或其他應用程式。";
        }
        catch(ExternalException){vm.Message="剪貼簿目前忙碌，請稍後再試，或使用另存 PNG。";}
        catch(Exception ex){vm.Message="複製失敗："+ex.Message;}
    }
    private void SaveImage(object sender,RoutedEventArgs e)
    {
        if(vm.PreviewImage is not {} image)return;
        try
        {
            var picker=new SaveFileDialog{Filter="PNG 圖片|*.png",DefaultExt=".png",AddExtension=true,FileName=CountdownShareRenderer.FileName(vm.Snapshot)};
            if(picker.ShowDialog(this)!=true)return;
            // Encode before touching the destination, then replace it only after the PNG is complete.
            using var png=new MemoryStream();CountdownShareRenderer.WritePng(image,png);
            File.WriteAllBytes(picker.FileName,png.ToArray());
            vm.Message="PNG 圖片已儲存。";
        }
        catch(Exception ex){vm.Message="儲存失敗："+ex.Message;}
    }
}

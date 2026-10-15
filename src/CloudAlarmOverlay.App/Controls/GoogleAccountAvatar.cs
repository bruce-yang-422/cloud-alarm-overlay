using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CloudAlarmOverlay.Core.Models;

namespace CloudAlarmOverlay.App.Controls;

public sealed class GoogleAccountAvatar : UserControl
{
    private static readonly HttpClient Images=new(new HttpClientHandler{AllowAutoRedirect=false})
        {Timeout=TimeSpan.FromSeconds(8),MaxResponseContentBufferSize=512*1024};
    private readonly Image photo=new(){Stretch=Stretch.UniformToFill};
    private readonly TextBlock placeholder=new(){Text="\uE77B",FontFamily=new("Segoe MDL2 Assets"),FontSize=18,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
    private CancellationTokenSource? download;
    private string loadedUrl="";
    private readonly HttpClient client;
    public static readonly DependencyProperty PictureUrlProperty=DependencyProperty.Register(
        nameof(PictureUrl),typeof(string),typeof(GoogleAccountAvatar),new PropertyMetadata("",(owner,_)=>((GoogleAccountAvatar)owner).LoadPicture()));
    public string PictureUrl {get=>(string)GetValue(PictureUrlProperty);set=>SetValue(PictureUrlProperty,value);}
    public GoogleAccountAvatar():this(Images) { }
    public GoogleAccountAvatar(HttpClient client)
    {
        this.client=client;
        Width=38;Height=38;
        placeholder.SetBinding(TextBlock.ForegroundProperty,new Binding(nameof(Foreground)){Source=this});
        var grid=new Grid{Clip=new RectangleGeometry(new Rect(0,0,38,38),10,10)};
        grid.Children.Add(placeholder);grid.Children.Add(photo);Content=grid;
        Loaded+=(_,_)=>LoadPicture();
        Unloaded+=(_,_)=>{download?.Cancel();download?.Dispose();download=null;};
    }
    private async void LoadPicture()
    {
        if(!IsLoaded)return;
        var url=GoogleProfilePicture.NormalizeUrl(PictureUrl);
        if(url==loadedUrl&&photo.Source is not null)return;
        download?.Cancel();download?.Dispose();download=new();
        var ct=download.Token;
        photo.Source=null;placeholder.Visibility=Visibility.Visible;loadedUrl="";
        if(url.Length==0)return;
        try
        {
            // Public photo requests carry no OAuth token and never follow redirects.
            var bytes=await client.GetByteArrayAsync(new Uri(url),ct);
            ct.ThrowIfCancellationRequested();
            using var stream=new MemoryStream(bytes);
            var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth=96;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();
            photo.Source=bitmap;placeholder.Visibility=Visibility.Collapsed;loadedUrl=url;
        }
        catch(Exception ex) when(ex is HttpRequestException or OperationCanceledException or IOException or NotSupportedException or ArgumentException or InvalidOperationException or FormatException or System.Runtime.InteropServices.COMException)
        { /* Missing, unavailable or invalid photos retain the default account icon. */ }
    }
}

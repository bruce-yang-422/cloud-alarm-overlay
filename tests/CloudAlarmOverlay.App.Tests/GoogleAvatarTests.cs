using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CloudAlarmOverlay.App.Controls;

namespace CloudAlarmOverlay.App.Tests;

public sealed class GoogleAvatarTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Avatar_downloads_without_credentials_and_falls_back_on_http_or_decode_error(bool invalidImage)=>MilestoneOneTests.RunSta(async()=>
    {
        using var stream=new MemoryStream();
        var png=new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(BitmapSource.Create(1,1,96,96,PixelFormats.Bgra32,null,new byte[]{200,100,50,255},4)));png.Save(stream);
        using var handler=new PhotoHandler(stream.ToArray());using var http=new HttpClient(handler);
        var avatar=new GoogleAccountAvatar(http){PictureUrl="https://lh3.googleusercontent.com/a/photo"};
        var window=new Window{Content=avatar,Width=100,Height=100,ShowInTaskbar=false,ShowActivated=false};
        try
        {
            window.Show();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();
            var content=(Grid)avatar.Content;var image=(Image)content.Children[1];var fallback=(TextBlock)content.Children[0];
            Assert.NotNull(image.Source);Assert.Equal(Visibility.Collapsed,fallback.Visibility);Assert.Equal(1,handler.Calls);
            avatar.PictureUrl="file:///C:/private.png";
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Null(image.Source);Assert.Equal(Visibility.Visible,fallback.Visibility);Assert.Equal(1,handler.Calls);
            handler.Fail=!invalidImage;handler.InvalidImage=invalidImage;
            avatar.PictureUrl="https://lh3.googleusercontent.com/a/changed";
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Null(image.Source);Assert.Equal(Visibility.Visible,fallback.Visibility);Assert.Equal(2,handler.Calls);
        }
        finally {window.Close();}
    });
    private sealed class PhotoHandler(byte[] photo):HttpMessageHandler
    {
        public int Calls;public bool Fail,InvalidImage;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Calls++;Assert.Null(request.Headers.Authorization);Assert.Equal("lh3.googleusercontent.com",request.RequestUri!.Host);
            return Task.FromResult(new HttpResponseMessage(Fail?HttpStatusCode.NotFound:HttpStatusCode.OK){Content=new ByteArrayContent(InvalidImage?new byte[]{1,2,3}:photo)});
        }
    }
}

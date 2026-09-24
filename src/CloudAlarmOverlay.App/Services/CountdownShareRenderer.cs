using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CloudAlarmOverlay.App.Styles;
using CloudAlarmOverlay.Core.Models;
using ThemeMode = CloudAlarmOverlay.Core.Models.ThemeMode;
namespace CloudAlarmOverlay.App.Services;

public enum CountdownShareSize { Square, Portrait }
public enum CountdownShareBackground { Gradient, Solid, Rings }
public sealed record CountdownShareAppearance(bool Dark,ThemeColorStyle ColorStyle,CountdownShareSize Size,
    CountdownShareBackground Background,bool ShowBranding);

public sealed class CountdownShareRenderer
{
    public const double Dpi = 192;
    public BitmapSource Render(CountdownShareSnapshot snapshot,CountdownShareAppearance appearance)
    {
        if(!Enum.IsDefined(appearance.Size) || !Enum.IsDefined(appearance.Background) || !Enum.IsDefined(appearance.ColorStyle))
            throw new ArgumentException("分享圖片選項無效。");
        const double width=540;
        var height=appearance.Size==CountdownShareSize.Portrait?810d:540d;
        var background=Color("#F7FAFE",appearance);var surface=Color("#FFFFFF",appearance);
        var text=Brush("#16243E",appearance);var muted=Brush("#697C99",appearance);var accent=Brush("#0766FF",appearance);
        var visual=new DrawingVisual();
        using(var dc=visual.RenderOpen())
        {
            dc.PushClip(new RectangleGeometry(new Rect(0,0,width,height)));
            Brush canvas=appearance.Background==CountdownShareBackground.Gradient
                ? new LinearGradientBrush(background,Color("#E6F0FF",appearance),new Point(0,0),new Point(1,1))
                : new SolidColorBrush(background);
            dc.DrawRectangle(canvas,null,new Rect(0,0,width,height));
            if(appearance.Background!=CountdownShareBackground.Solid)
            {
                dc.PushOpacity(appearance.Dark?.18:.12);
                if(appearance.Background==CountdownShareBackground.Rings)
                {
                    foreach(var radius in new[]{95d,140,185})
                    {
                        dc.DrawEllipse(null,new Pen(accent,1.5),new Point(510,45),radius,radius);
                        dc.DrawEllipse(null,new Pen(accent,1.5),new Point(20,height-60),radius,radius);
                    }
                }
                else
                {
                    dc.DrawEllipse(accent,null,new Point(480,30),190,190);
                    dc.DrawEllipse(accent,null,new Point(20,height-30),155,155);
                }
                dc.Pop();
            }
            // A dedicated fixed-size composition; no live UI, monitor scale or card size is captured.
            dc.DrawRoundedRectangle(new SolidColorBrush(surface),null,new Rect(28,28,width-56,height-56),24,24);
            dc.DrawRoundedRectangle(accent,null,new Rect(54,60,34,4),2,2);
            Draw(dc,snapshot.Direction,13,muted,new Rect(100,52,300,26),false);
            Draw(dc,CategoryGlyph(snapshot.Category),24,accent,new Rect(54,98,32,34),false,"Segoe MDL2 Assets");
            Draw(dc,snapshot.Category,14,muted,new Rect(94,102,300,30),false);
            var portrait=appearance.Size==CountdownShareSize.Portrait;
            var titleY=portrait?213:142;var titleHeight=portrait?150:90;
            var leadY=titleY+titleHeight+12;
            var valueY=leadY+30;var valueHeight=portrait?120:100;
            var unitY=valueY+valueHeight+2;
            var lineY=unitY+40;
            Draw(dc,snapshot.Title,32,text,new Rect(54,titleY,width-108,titleHeight),true);
            Draw(dc,snapshot.Lead,18,muted,new Rect(54,leadY,width-108,30),true);
            Draw(dc,snapshot.Value,snapshot.Unit.Length>0?88:48,accent,new Rect(48,valueY,width-96,valueHeight),true);
            Draw(dc,snapshot.Unit,20,muted,new Rect(54,unitY,width-108,32),true);
            dc.DrawLine(new Pen(Brush("#D6E1EF",appearance),1),new Point(180,lineY),new Point(360,lineY));
            Draw(dc,snapshot.DateCaption,16,text,new Rect(54,lineY+12,width-108,32),true);
            if(snapshot.Status.Length>0 && snapshot.Value!=snapshot.Status)
                Draw(dc,snapshot.Status,13,muted,new Rect(width-180,102,126,30),false,alignment:TextAlignment.Right);
            Draw(dc,$"記錄於 {snapshot.CapturedAt:yyyy.MM.dd}",10,muted,new Rect(54,height-65,200,22),false);
            if(appearance.ShowBranding)
                Draw(dc,"Cloud Alarm Overlay",10,muted,new Rect(width-222,height-65,168,22),false,alignment:TextAlignment.Right);
            dc.Pop();
        }
        var bitmap=new RenderTargetBitmap(1080,(int)(height*2),Dpi,Dpi,PixelFormats.Pbgra32);
        bitmap.Render(visual);bitmap.Freeze();return bitmap;
    }
    private static string CategoryGlyph(string category)=>category switch
    { "工作"=>"\uE821","生活"=>"\uE80F","節日"=>"\uE787","旅行"=>"\uE709",_=>"\uE734" };
    private static Color Color(string value,CountdownShareAppearance a)
    {
        if(a.ColorStyle is ThemeColorStyle.Lavender or ThemeColorStyle.Sunset or ThemeColorStyle.Silver)
            return ExtendedThemePalette.Resolve(value,a.Dark,a.ColorStyle);
        var mode=a.ColorStyle switch{ThemeColorStyle.Pink=>ThemeMode.Pink,ThemeColorStyle.Bamboo=>ThemeMode.Bamboo,_=>a.Dark?ThemeMode.Dark:ThemeMode.Light};
        return AdaptiveBrushExtension.Map((Color)ColorConverter.ConvertFromString(value),a.Dark,mode);
    }
    private static SolidColorBrush Brush(string value,CountdownShareAppearance a)=>new(Color(value,a));
    private static void Draw(DrawingContext dc,string value,double size,Brush brush,Rect box,bool centered,
        string family="Segoe UI, Microsoft JhengHei UI, Segoe UI Emoji",TextAlignment alignment=TextAlignment.Left)
    {
        if(value.Length==0)return;
        var typeface=new Typeface(new FontFamily(family),FontStyles.Normal,size>=30?FontWeights.SemiBold:FontWeights.Normal,FontStretches.Normal);
        FormattedText Make(double fontSize)=>new(value,CultureInfo.GetCultureInfo("zh-TW"),FlowDirection.LeftToRight,typeface,fontSize,brush,2)
            {MaxTextWidth=box.Width,TextAlignment=centered?TextAlignment.Center:alignment};
        var text=Make(size);
        while((text.Height>box.Height || text.MinWidth>box.Width) && size>10){size-=1;text=Make(size);}
        dc.DrawText(text,new Point(box.X,box.Y+(box.Height-text.Height)/2));
    }
    public static void WritePng(BitmapSource image,Stream output)
    {
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));encoder.Save(output);
    }
    public static string FileName(CountdownShareSnapshot snapshot)
    {
        var invalid=Path.GetInvalidFileNameChars();
        var title=new string(snapshot.Title.Select(c=>invalid.Contains(c)||char.IsControl(c)?'_':c).ToArray()).Trim().TrimEnd('.');
        if(title.Length>50)title=title[..50];
        return $"倒數分享_{(title.Length==0?"事件":title)}_{snapshot.CapturedAt:yyyyMMdd}.png";
    }
}

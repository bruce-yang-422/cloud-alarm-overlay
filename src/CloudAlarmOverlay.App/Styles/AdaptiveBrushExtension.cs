using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Data;
using System.ComponentModel;
using CloudAlarmOverlay.Core.Models;
using ThemeMode = CloudAlarmOverlay.Core.Models.ThemeMode;
namespace CloudAlarmOverlay.App.Styles;

/// <summary>Shared brushes keep existing and newly opened views in the same theme.</summary>
public sealed class AdaptiveBrushExtension(string color) : MarkupExtension
{
    private sealed class ColorSource : INotifyPropertyChanged
    {
        public SolidColorBrush Brush {get; private set;} = new();
        public event PropertyChangedEventHandler? PropertyChanged;
        public void Set(Color color) { Brush = new(color); Brush.Freeze(); PropertyChanged?.Invoke(this,new(nameof(Brush))); }
    }
    private static readonly Dictionary<string,ColorSource> Brushes = new();
    private static bool dark;
    private static ThemeMode mode;
    private static ThemeColorStyle colorStyle;
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if(!Brushes.TryGetValue(color,out var brush))
        {
            brush=new ColorSource(); brush.Set(Resolve(color));
            Brushes.Add(color,brush);
        }
        return new Binding("Brush") { Source=brush }.ProvideValue(serviceProvider);
    }
    public static void Apply(bool isDark)=>Apply(isDark,isDark?ThemeMode.Dark:ThemeMode.Light);
    public static void Apply(bool isDark,ThemeMode themeMode)=>Apply(isDark,themeMode switch{ThemeMode.Pink=>ThemeColorStyle.Pink,ThemeMode.Bamboo=>ThemeColorStyle.Bamboo,_=>ThemeColorStyle.Default});
    public static void Apply(bool isDark,ThemeColorStyle style)
    {
        if(Application.Current is {} app && !app.Dispatcher.CheckAccess()) { app.Dispatcher.BeginInvoke(()=>Apply(isDark,style)); return; }
        dark=isDark; colorStyle=style;
        mode=style switch{ThemeColorStyle.Pink=>ThemeMode.Pink,ThemeColorStyle.Bamboo=>ThemeMode.Bamboo,_=>isDark?ThemeMode.Dark:ThemeMode.Light};
        foreach(var (key,brush) in Brushes)brush.Set(Resolve(key));
    }
    private static Color Resolve(string value)
    {
        // Countdown state colors retain their meaning in every color style.
        // Explicit dark surfaces avoid flattening every pale tint into the same navy.
        string? countdownColor = value switch
        {
            "CountdownCalmSurface" => dark ? "#20364F" : "#EFF6FF",
            "CountdownCalmAccent" => dark ? "#90C4FF" : "#205AB0",
            "CountdownSoonSurface" => dark ? "#403326" : "#FFF5E8",
            "CountdownSoonAccent" => dark ? "#FFC27D" : "#995000",
            "CountdownDueSurface" => dark ? "#442C39" : "#FFF0F3",
            "CountdownDueAccent" => dark ? "#FFADC1" : "#AD284B",
            "CountdownUpSurface" => dark ? "#203D35" : "#ECF8F1",
            "CountdownUpAccent" => dark ? "#86DEC0" : "#196A4F",
            "CountdownNeutralSurface" => dark ? "#303847" : "#F0F3F7",
            "CountdownNeutralAccent" => dark ? "#C0CDDF" : "#52647A",
            _ => null
        };
        if (countdownColor is not null) return (Color)ColorConverter.ConvertFromString(countdownColor);
        if(colorStyle is ThemeColorStyle.Lavender or ThemeColorStyle.Sunset or ThemeColorStyle.Silver)
            return ExtendedThemePalette.Resolve(value,dark,colorStyle);
        if(value=="Accent")return (Color)ColorConverter.ConvertFromString(mode switch
        {
            ThemeMode.Pink=>dark?"#C2185B":"#FFB8C8",
            ThemeMode.Bamboo=>dark?"#00695C":"#39A67F",
            _=>dark?"#3478F6":"#2563EB"
        });
        else if(value=="AccentText")return (Color)ColorConverter.ConvertFromString(dark?"#FFFFFF":mode==ThemeMode.Pink?"#46202D":mode==ThemeMode.Bamboo?"#102D24":"#FFFFFF");
        return Map((Color)ColorConverter.ConvertFromString(value),dark,mode);
    }
    public static Color Map(Color source,bool isDark,ThemeMode themeMode)
    {
        if(isDark && themeMode is (ThemeMode.Pink or ThemeMode.Bamboo))return MapDarkColorTheme(source,themeMode==ThemeMode.Pink);
        if(themeMode is not (ThemeMode.Pink or ThemeMode.Bamboo))return Map(source,isDark);
        bool pink=themeMode==ThemeMode.Pink;
        var hex=$"{source.R:X2}{source.G:X2}{source.B:X2}";
        string? mapped=hex switch
        {
            "F7FAFE"=>pink?"#FFF0F5":"#F0FDF4",
            "F0F5FB" or "EAF4FF"=>pink?"#FFE4EC":"#DFF1E8",
            "FDFEFF" or "FAFCFF"=>pink?"#FFFAFB":"#F9FDFB",
            "F0F5FC" or "EEF3F9" or "EAF0F8" or "F1F5FA"=>pink?"#FCE9EF":"#E5F3EB",
            "E6F0FF" or "D9E8FA" or "E8F1FF"=>pink?"#FFB8C8":"#BCE5D4",
            "D6E1EF" or "D9E3EF" or "D9E4F1" or "DEE6F1" or "DCE5F1" or "E8EEF6"=>pink?"#D9A6B4":"#9FC6B6",
            "0766FF" or "1668CA" or "2563EB"=>pink?"#A33459":"#196D50",
            "16243E" or "253B55"=>pink?"#46202D":"#183B2C",
            "697C99" or "637691" or "7184A0" or "576C8B"=>pink?"#805565":"#4D6C5D",
            _=>null
        };
        if(mapped is null)return source;
        var result=(Color)ColorConverter.ConvertFromString(mapped);
        return Color.FromArgb(source.A,result.R,result.G,result.B);
    }
    private static Color MapDarkColorTheme(Color source,bool pink)
    {
        if(source.A==0)return source;
        var hex=$"{source.R:X2}{source.G:X2}{source.B:X2}";
        string? mapped=hex switch
        {
            "F7FAFE"=>pink?"#21171F":"#101F1B", // page
            "F0F5FB" or "EAF4FF"=>pink?"#2D202B":"#172C26", // navigation
            "FFFFFF"=>pink?"#382735":"#203B32", // cards
            "FDFEFF" or "FAFCFF"=>pink?"#281C26":"#152B24", // inputs
            "EEF3F9" or "EAF0F8"=>pink?"#251B23":"#13271F", // tab tracks
            "F0F5FC" or "F1F5FA"=>pink?"#513446":"#315448", // buttons / table headers
            "E6F0FF" or "D9E8FA" or "E8F1FF"=>pink?"#682743":"#175E4D", // selected items
            "DEE6F1" or "DCE5F1" or "E8EEF6"=>pink?"#745267":"#4E7967", // outlines
            "D6E1EF" or "D9E3EF" or "D9E4F1"=>pink?"#AD8095":"#80AC97", // input outlines
            "16243E" or "253B55"=>pink?"#FFF0F5":"#EAF9F0", // primary text
            "697C99" or "637691" or "7184A0" or "576C8B"=>pink?"#DFC0CF":"#B9D8C8", // secondary text
            "0766FF" or "1668CA"=>pink?"#FFB8C8":"#80DDB4", // links
            "2563EB"=>pink?"#C2185B":"#00695C", // switch track
            "A7B3C6"=>pink?"#75576B":"#4B7060",
            _=>null
        };
        // Preserve distinct semantic colors for errors, warnings and notification levels.
        if(mapped is null)return Map(source,true);
        var result=(Color)ColorConverter.ConvertFromString(mapped);
        return Color.FromArgb(source.A,result.R,result.G,result.B);
    }
    public static Color Map(Color source,bool isDark)
    {
        if(!isDark || source.A==0)return source;
        // Keep surfaces, controls and outlines distinct instead of compressing all
        // pale colors into nearly the same dark shade.
        var palette = $"{source.R:X2}{source.G:X2}{source.B:X2}" switch
        {
            "18964A" => "#7EDDAD",
            "BA3D45" => "#FF979B",
            "64748B" => "#CCD5E2",
            "F7FAFE" => "#141E2D", // page
            "F0F5FB" => "#1B293C", // navigation
            "FFFFFF" => "#243449", // cards and selected tabs
            "FDFEFF" => "#18283D", // editable fields
            "F0F5FC" => "#354B67", // secondary buttons
            "EEF3F9" or "EAF0F8" => "#182639", // tab track
            "E6F0FF" => "#244D7D", // selected navigation
            "D9E8FA" => "#29496B", // inactive folder tab
            "DEE6F1" or "DCE5F1" => "#465B76", // card outline
            "D6E1EF" or "D9E3EF" or "D9E4F1" => "#7891AF", // field outline
            "F1F5FA" => "#30445F", // table header
            "F3EFFA" => "#332E48", // cloud read-only rows
            "70568F" => "#CEBAEC", // cloud read-only labels
            "FAFCFF" => "#293B52",
            "E8EEF6" => "#465B76",
            "16243E" or "253B55" => "#EDF3FC",
            "697C99" or "637691" => "#B5C6DD",
            "0766FF" => "#78B4FF",
            "A7B3C6" => "#52647C", // switch off track
            "2563EB" => "#3478F6", // switch on track
            "F9CFD5" => "#493747", // countdown ring track
            "E12A43" => "#F2798C", // countdown progress
            "F4DBD2" => "#483C38", // focus progress track
            "DE624E" => "#F3987E", // focus progress
            "1668CA" => "#91C5FF",
            _ => null
        };
        if(palette is not null)
        {
            var mapped=(Color)ColorConverter.ConvertFromString(palette);
            return Color.FromArgb(source.A,mapped.R,mapped.G,mapped.B);
        }
        var max=Math.Max(source.R,Math.Max(source.G,source.B));
        var min=Math.Min(source.R,Math.Min(source.G,source.B));
        var light=(max+min)/510d;
        if(light>.78) { var offset=(byte)((1-light)*65); return Color.FromArgb(source.A,(byte)(27+offset),(byte)(36+offset),(byte)(50+offset)); }
        if(light<.38 || max-min<65) return Color.FromArgb(source.A,191,207,229);
        return Color.FromArgb(source.A,(byte)(source.R*.6+100),(byte)(source.G*.6+100),(byte)(source.B*.6+100));
    }
}

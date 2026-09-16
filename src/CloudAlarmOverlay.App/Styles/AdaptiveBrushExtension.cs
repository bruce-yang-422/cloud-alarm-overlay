using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Data;
using System.ComponentModel;
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
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if(!Brushes.TryGetValue(color,out var brush))
        {
            brush=new ColorSource(); brush.Set(Map((Color)ColorConverter.ConvertFromString(color),dark));
            Brushes.Add(color,brush);
        }
        return new Binding("Brush") { Source=brush }.ProvideValue(serviceProvider);
    }
    public static void Apply(bool isDark)
    {
        if(Application.Current is {} app && !app.Dispatcher.CheckAccess()) { app.Dispatcher.BeginInvoke(()=>Apply(isDark)); return; }
        dark=isDark;
        foreach(var (key,brush) in Brushes)brush.Set(Map((Color)ColorConverter.ConvertFromString(key),dark));
    }
    public static Color Map(Color source,bool isDark)
    {
        if(!isDark || source.A==0)return source;
        // Keep surfaces, controls and outlines distinct instead of compressing all
        // pale colors into nearly the same dark shade.
        var palette = $"{source.R:X2}{source.G:X2}{source.B:X2}" switch
        {
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

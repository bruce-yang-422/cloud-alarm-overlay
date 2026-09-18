using System.Windows.Media;
using CloudAlarmOverlay.Core.Models;

namespace CloudAlarmOverlay.App.Styles;

internal static class ExtendedThemePalette
{
    // Page, navigation, card, input, track, button, selection, outline,
    // input outline, text, muted text, link, primary fill, primary text, off switch.
    private static readonly string[] LavenderLight = ["#F8F5FF","#F0EAFC","#FFFFFF","#FDFBFF","#EEE8F6","#EDE7F7","#C4B5FD","#C9BCDF","#9C87BA","#2E2045","#66517D","#6430B5","#C4B5FD","#302047","#9D91AE"];
    private static readonly string[] LavenderDark = ["#1D1829","#292137","#352B46","#251E33","#211A2E","#4B3B61","#503377","#74608C","#AA93C4","#F5EFFF","#D2BFEB","#CFB6FF","#7C3AED","#FFFFFF","#6D5E81"];
    private static readonly string[] SunsetLight = ["#FFF8F1","#FFF0DF","#FFFFFF","#FFFCF8","#F7EBDD","#F9E7D3","#FDBA74","#DCBE9F","#AD805A","#422819","#78563C","#9B400A","#FDBA74","#422819","#AE9780"];
    private static readonly string[] SunsetDark = ["#241C18","#30251E","#3D2F25","#2A211B","#251D18","#57412F","#693919","#8B674B","#C39C77","#FFF3E8","#E5C8AD","#FDBA74","#EA580C","#231409","#7D6551"];
    private static readonly string[] SilverLight = ["#F8FAFC","#F1F5F9","#FFFFFF","#FCFDFE","#EDF1F5","#E8EDF2","#E2E8F0","#C6D0DC","#8B9AAF","#1E293B","#526176","#475569","#E2E8F0","#1E293B","#94A3B8"];
    private static readonly string[] SilverDark = ["#181C22","#232830","#2D343E","#20262E","#1D2229","#414B59","#465363","#637084","#94A3B8","#F1F5F9","#C5D0DE","#CBD5E1","#64748B","#FFFFFF","#566273"];

    public static Color Resolve(string value,bool dark,ThemeColorStyle style)
    {
        var palette = style switch
        {
            ThemeColorStyle.Lavender => dark ? LavenderDark : LavenderLight,
            ThemeColorStyle.Sunset => dark ? SunsetDark : SunsetLight,
            ThemeColorStyle.Silver => dark ? SilverDark : SilverLight,
            _ => throw new ArgumentOutOfRangeException(nameof(style))
        };
        if(value=="Accent")return Parse(palette[12]);
        if(value=="AccentText")return Parse(palette[13]);
        var source=Parse(value);
        var key=$"{source.R:X2}{source.G:X2}{source.B:X2}";
        var role=key switch
        {
            "F7FAFE" => 0,
            "F0F5FB" or "EAF4FF" or "F6FAFF" => 1,
            "FFFFFF" => 2,
            "FDFEFF" or "FAFCFF" => 3,
            "EEF3F9" or "EAF0F8" => 4,
            "F0F5FC" or "F1F5FA" => 5,
            "E6F0FF" or "D9E8FA" or "E8F1FF" => 6,
            "DEE6F1" or "DCE5F1" or "E8EEF6" or "E2ECF7" => 7,
            "D6E1EF" or "D9E3EF" or "D9E4F1" => 8,
            "16243E" or "253B55" => 9,
            "697C99" or "637691" or "7184A0" or "576C8B" or "536982" => 10,
            "0766FF" or "1668CA" or "3664A0" => 11,
            "2563EB" => 12,
            "A7B3C6" => 14,
            _ => -1
        };
        // Keep errors, warning levels and category/status colors distinguishable.
        if(role<0)return AdaptiveBrushExtension.Map(source,dark);
        var result=Parse(palette[role]);
        return Color.FromArgb(source.A,result.R,result.G,result.B);
    }

    private static Color Parse(string value)=>(Color)ColorConverter.ConvertFromString(value);
}

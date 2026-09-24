using System.Globalization;
using CloudAlarmOverlay.Core.Recurrence;
using CsvHelper;

namespace CloudAlarmOverlay.Data.Tests;

public sealed class BuiltinLunarTests
{
    [Fact] public void Builtin_days_match_all_730_existing_2026_2027_sheet_dates()
    {
        using var reader=File.OpenText(Path.Combine(AppContext.BaseDirectory,"Samples","SheetA_LunarCalendar.csv"));
        using var csv=new CsvReader(reader,CultureInfo.InvariantCulture);
        csv.Read();csv.ReadHeader();csv.Read(); // Chinese description row.
        var dates=new HashSet<DateOnly>();
        while(csv.Read())
        {
            var date=DateOnly.ParseExact(csv.GetField("Date")!,"yyyy-MM-dd",CultureInfo.InvariantCulture);
            var day=int.Parse(csv.GetField("LunarDay")!,CultureInfo.InvariantCulture);
            Assert.True(dates.Add(date));
            Assert.True(RecurrenceRule.Matches($"LunarDay:{day}",date,new DateTime(2026,1,1)),date.ToString());
        }
        Assert.Equal(730,dates.Count);Assert.Equal(new DateOnly(2026,1,1),dates.Min());Assert.Equal(new DateOnly(2027,12,31),dates.Max());
    }

    [Fact] public void Monthly_first_day_includes_leap_month_without_sheet_and_ignores_wrong_cache()
    {
        var start=new DateTime(2023,1,1,9,0,0);
        Assert.Equal(new DateTime(2023,2,20,9,0,0),RecurrenceCalendar.Next("LunarDay:1",start,new DateTime(2023,2,1)));
        Assert.Equal(new DateTime(2023,3,22,9,0,0),RecurrenceCalendar.Next("LunarDay:1",start,new DateTime(2023,3,1)));
        var wrong=new Dictionary<DateOnly,int>{{new(2023,3,2),1}};
        Assert.Equal(new DateTime(2023,3,22,9,0,0),RecurrenceCalendar.Next("LunarDay:1",start,new DateTime(2023,3,1),lunarDays:wrong));
    }

    [Fact] public void Day_thirty_skips_short_month_and_future_years_work_without_data()
    {
        // 2024 first lunar month has 29 days; the next day 30 is in lunar month two.
        Assert.Equal(new DateTime(2024,4,8,9,0,0),RecurrenceCalendar.Next("LunarDay:30",new DateTime(2024,2,10,9,0,0),new DateTime(2024,2,10)));
        Assert.Equal(new DateTime(2028,1,26,9,0,0),RecurrenceCalendar.Next("LunarDay:1",new DateTime(2028,1,1,9,0,0),new DateTime(2028,1,1)));
    }

    [Fact] public void Unsupported_dates_do_not_throw_or_use_sheet_as_fallback()
    {
        var calendar=new ChineseLunisolarCalendar();
        foreach(var date in new[]{calendar.MinSupportedDateTime.AddDays(-1),calendar.MaxSupportedDateTime.Date.AddDays(1)})
            Assert.False(RecurrenceRule.Matches("LunarDay:1",DateOnly.FromDateTime(date),DateTime.MinValue,1));
        Assert.Null(RecurrenceCalendar.Next("LunarDay:1",new DateTime(2200,1,1),new DateTime(2200,1,1)));
    }
}

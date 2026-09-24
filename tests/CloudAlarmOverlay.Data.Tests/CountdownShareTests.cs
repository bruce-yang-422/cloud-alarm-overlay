using CloudAlarmOverlay.Core.Models;
namespace CloudAlarmOverlay.Data.Tests;
public sealed class CountdownShareTests
{
    private static readonly DateTime Now=new(2026,9,24,12,0,0);
    private static CountdownItem Item=>new(){Title="日本旅行 ✈️",Category="旅行",TargetAt=Now.Date.AddDays(30),CreatedAt=Now.Date};
    [Fact] public void Day_countdown_and_countup_share_the_card_dates()
    {
        var down=CountdownShareSnapshot.Create(Item,Now);Assert.Equal("30",down.Value);Assert.Equal("天",down.Unit);Assert.Equal("還有",down.Lead);Assert.Equal("目標日期  2026.10.24",down.DateCaption);
        var up=CountdownShareSnapshot.Create(Item with{Direction="Up",TargetAt=Now.Date.AddDays(-1000)},Now);
        Assert.Equal("1,000",up.Value);Assert.Equal("已過",up.Lead);Assert.StartsWith("起始日期",up.DateCaption);
    }
    [Theory][InlineData("WeeksDays","4 週 2 天")][InlineData("MonthsDays","1 個月 0 天")][InlineData("YearsMonthsDays","0 年 1 個月 0 天")]
    public void Share_uses_calendar_based_display_formats(string format,string expected)
    {
        Assert.Equal(expected,CountdownShareSnapshot.Create(Item with{DisplayFormat=format},Now).Value);
    }
    [Fact] public void Time_mode_omits_hours_and_seconds_but_keeps_actual_deadline()
    {
        var item=Item with{Mode="Time",TargetAt=Now.AddHours(25)};
        Assert.Equal("1",CountdownShareSnapshot.Create(item,Now).Value);
        Assert.Equal("不到 1",CountdownShareSnapshot.Create(item with{TargetAt=Now.AddMinutes(1)},Now).Value);
        Assert.Equal("已到期",CountdownShareSnapshot.Create(item with{TargetAt=Now},Now).Value);
        Assert.Equal("0",CountdownShareSnapshot.Create(item with{Direction="Up",TargetAt=Now.AddHours(-23)},Now).Value);
    }
    [Fact] public void States_do_not_invent_a_remaining_duration()
    {
        Assert.Equal("就是今天",CountdownShareSnapshot.Create(Item with{TargetAt=Now.Date},Now).Value);
        Assert.Equal("已到期",CountdownShareSnapshot.Create(Item with{TargetAt=Now.Date.AddDays(-1)},Now).Value);
        Assert.Equal("已完成",CountdownShareSnapshot.Create(Item with{CompletedAt=Now},Now).Value);
        Assert.Equal("尚未開始",CountdownShareSnapshot.Create(Item with{Direction="Up"},Now).Value);
        var unknown=CountdownShareSnapshot.Create(Item with{TargetAt=new DateTime(2200,1,1),Recurrence="LunarDay:1"},Now);
        Assert.Equal("待確認日期",unknown.Value);Assert.Equal("尚無可用目標日期",unknown.DateCaption);
    }
    [Fact] public void Recurrence_holidays_and_lunar_data_use_the_existing_calendar()
    {
        var daily=Item with{TargetAt=Now.Date.AddDays(-10),Recurrence="Daily",SkipOnHoliday=true};
        var snapshot=CountdownShareSnapshot.Create(daily,Now,null,[new(){Date=DateOnly.FromDateTime(Now),Type="國定假日"}]);
        Assert.Equal("1",snapshot.Value);Assert.Equal("目標日期  2026.09.25",snapshot.DateCaption);
        var lunar=Item with{TargetAt=Now.Date,Recurrence="LunarDay:1"};
        snapshot=CountdownShareSnapshot.Create(lunar,Now,new Dictionary<DateOnly,int>{{DateOnly.FromDateTime(Now.AddDays(3)),1}});
        Assert.Equal("16",snapshot.Value);Assert.Equal("目標日期  2026.10.10",snapshot.DateCaption);
    }
    [Fact] public void Completed_countup_freezes_at_completion_and_private_notes_are_not_in_snapshot()
    {
        var item=Item with{Direction="Up",TargetAt=Now.Date.AddDays(-10),CompletedAt=Now.AddDays(-2),Notes="PRIVATE-NOTE"};
        var snapshot=CountdownShareSnapshot.Create(item,Now);
        Assert.Equal("8",snapshot.Value);Assert.Equal("已完成",snapshot.Status);
        Assert.Equal(snapshot,CountdownShareSnapshot.Create(item with{Notes="different private text"},Now));
        Assert.DoesNotContain("PRIVATE",System.Text.Json.JsonSerializer.Serialize(snapshot));
    }
}

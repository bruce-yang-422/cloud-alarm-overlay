using CloudAlarmOverlay.Core.Models;
namespace CloudAlarmOverlay.Core.Recurrence;

public static class RecurrenceCalendar
{
    public static DateTime? Next(string rule, DateTime scheduled, DateTime from, bool inclusive = true,
        IReadOnlyDictionary<DateOnly,int>? lunarDays = null, IReadOnlyList<Holiday>? holidays = null, bool skipOnHoliday = false,
        CancellationToken cancellationToken = default)
    {
        RecurrenceRule.Validate(rule);
        var start = from.Date > scheduled.Date ? from.Date : scheduled.Date;
        // Gregorian Feb 29 can require eight years across a non-leap century.
        var end = rule == "None" ? scheduled.Date : start.AddYears(Math.Min(8,9999-start.Year));
        for(var day=start; day<=end;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate=day+scheduled.TimeOfDay;
            if(candidate>from || inclusive && candidate==from)
            {
                var date=DateOnly.FromDateTime(day);
                bool match=RecurrenceRule.Matches(rule,date,scheduled);
                var today=holidays?.Where(h=>h.Date==date).ToArray() ?? [];
                if(rule.StartsWith("Weekly:",StringComparison.Ordinal) && today.Any(h=>h.Type=="補班日"))match=true;
                if(skipOnHoliday && today.Any(h=>h.Type!="補班日"))match=false;
                if(match)return candidate;
            }
            if(day==DateTime.MaxValue.Date)break;
            day=day.AddDays(1);
        }
        return null;
    }
}

using CloudAlarmOverlay.Core.Models;

namespace CloudAlarmOverlay.Core.Services;

/// <summary>Contract scaffold. Business implementation follows in a later milestone.</summary>
public interface ICsvSheetParser
{
    IReadOnlyList<AlarmTask> ParseTasks(string csv, string source);
    IReadOnlyList<Holiday> ParseHolidays(string csv);
    IReadOnlyList<Employee> ParseEmployees(string csv);
    IReadOnlyList<LunarCalendarEntry> ParseLunarCalendar(string csv);
}


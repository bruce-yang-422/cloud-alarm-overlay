using CloudAlarmOverlay.Core.Models;

namespace CloudAlarmOverlay.Core.Services;

public interface ICalendarIcsExporter
{
    Task<CalendarExportResult> ExportAsync(CalendarExportOptions options,CancellationToken ct=default);
}

using CloudAlarmOverlay.Core.Models;

namespace CloudAlarmOverlay.Core.Services;

/// <summary>Contract scaffold. Business implementation follows in a later milestone.</summary>
public interface IAudienceFilterService
{
    bool IsIncluded(AlarmTask task, Device device, IReadOnlyList<Employee> employees);
}


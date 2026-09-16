using Microsoft.Extensions.DependencyInjection;
using CloudAlarmOverlay.Core.Services;

namespace CloudAlarmOverlay.Core;

public static class DependencyInjection
{
    public static IServiceCollection AddCore(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<AdminSession>();
        services.AddSingleton<NotificationPreferences>();
        services.AddSingleton<ChangeSignal>();
        services.AddSingleton<SyncConfiguration>();
        services.AddSingleton<RuntimeState>();
        services.AddSingleton<IAlarmService, AlarmService>();
        services.AddSingleton<ITaskService, TaskService>();
        services.AddSingleton<ITaskSchedulingService, TaskSchedulingService>();
        services.AddSingleton<ISyncService, SyncService>();
        services.AddSingleton<ICsvSheetParser, CsvSheetParser>();
        services.AddSingleton<IAudienceFilterService, AudienceFilterService>();
        services.AddSingleton<IEmployeeLevelPolicyService, EmployeeLevelPolicyService>();
        services.AddSingleton<IHolidayService, HolidayService>();
        services.AddSingleton<ILunarScheduleService, LunarScheduleService>();
        services.AddSingleton<IOverdueGraceService, OverdueGraceService>();
        services.AddSingleton<IAckCodeGenerator, AckCodeGenerator>();
        services.AddSingleton<IPomodoroService, PomodoroService>();
        services.AddSingleton<IDeviceIdentityService, DeviceIdentityService>();
        services.AddSingleton<IAuthenticationService, AuthenticationService>();
        services.AddSingleton<IExitProtectionService, ExitProtectionService>();
        services.AddSingleton<IAuditService, AuditService>();

        services.AddSingleton<IUpdateCheckService, UpdateCheckService>();
        services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(20) });
        return services;
    }
}

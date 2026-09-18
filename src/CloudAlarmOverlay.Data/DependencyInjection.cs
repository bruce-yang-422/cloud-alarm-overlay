using Microsoft.Extensions.DependencyInjection;
using CloudAlarmOverlay.Core.Services;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Data.Repositories;

namespace CloudAlarmOverlay.Data;

public static class DependencyInjection
{
    public static IServiceCollection AddData(this IServiceCollection services)
    {
        services.AddSingleton<Database>();
        services.AddSingleton<IBackupRestoreService, BackupRestoreService>();
        services.AddSingleton<ISystemEventStore, SystemEventStore>();
        services.AddSingleton<IAdminSettingsStore, AdminSettingsStore>();
        services.AddSingleton<IRuntimeStore, RuntimeStore>();
        services.AddSingleton<ISqliteConnectionFactory, SqliteConnectionFactory>();
        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();
        services.AddSingleton<ITaskRepository, TaskRepository>();
        services.AddSingleton<ICountdownRepository, CountdownRepository>();
        services.AddSingleton<ITaskHomePinRepository, TaskHomePinRepository>();
        services.AddSingleton<IHolidayRepository, HolidayRepository>();
        services.AddSingleton<ILunarCalendarRepository, LunarCalendarRepository>();
        services.AddSingleton<IEmployeeRepository, EmployeeRepository>();
        services.AddSingleton<IDeviceRepository, DeviceRepository>();
        services.AddSingleton<IUserRepository, UserRepository>();
        services.AddSingleton<IAckLogRepository, AckLogRepository>();
        services.AddSingleton<ISyncLogRepository, SyncLogRepository>();
        services.AddSingleton<IAuditLogRepository, AuditLogRepository>();
        services.AddSingleton<ISettingsRepository, SettingsRepository>();
        services.AddSingleton<IPomodoroRepository, PomodoroRepository>();
        return services;
    }
}

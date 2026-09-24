using CloudAlarmOverlay.App.ViewModels;
using CloudAlarmOverlay.App.Views;
using CloudAlarmOverlay.BackgroundServices;
using CloudAlarmOverlay.Core;
using CloudAlarmOverlay.Data;
using CloudAlarmOverlay.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using CloudAlarmOverlay.App.Services;
using CloudAlarmOverlay.Core.Services;
using Microsoft.Extensions.Logging;

namespace CloudAlarmOverlay.App;

public static class CompositionRoot
{
    public static IHost CreateHost()
        => new HostBuilder()
            .UseDefaultServiceProvider(options =>
            {
                options.ValidateOnBuild = true;
                options.ValidateScopes = true;
            })
            .ConfigureServices(services => ConfigureServices(services))
            .Build();

    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddCore();
        services.AddInfrastructure();
        services.AddData();
        services.AddHostedService<DatabaseInitializationService>();
        services.AddHostedService<SyncWorker>();
        services.AddHostedService<AlarmWorker>();
        services.AddHostedService<PomodoroWorker>();
        services.AddHostedService<WeatherWorker>();
        services.AddSingleton<RuntimeLogCleanup>();
        services.AddHostedService<RuntimeLogCleanupWorker>();
        services.AddSingleton<WeatherViewModel>();
        services.AddSingleton<AlarmPresenter>();
        services.AddSingleton<IAlarmPresenter>(provider=>provider.GetRequiredService<AlarmPresenter>());
        services.AddSingleton<IUserDialogs, UserDialogs>();
        services.AddSingleton<CountdownShareRenderer>();
        services.AddSingleton<CountdownShareService>();
        services.AddSingleton<IAutoStartService, AutoStartService>();
        services.AddSingleton<TaskBuilderHostService>();
        services.AddSingleton<IBrowserLauncher, BrowserLauncher>();
        services.AddSingleton<ILoggerProvider, LocalFileLoggerProvider>();
        services.AddSingleton<PreferencesViewModel>();
        services.AddSingleton<MaintenanceViewModel>();
        services.AddSingleton<EmojiLibrary>();
        services.AddSingleton<AdminViewModel>();
        services.AddSingleton<PomodoroViewModel>();
        services.AddSingleton<CountdownsViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
    }
}

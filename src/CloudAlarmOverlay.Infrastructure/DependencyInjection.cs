using Microsoft.Extensions.DependencyInjection;
using CloudAlarmOverlay.Core.Services;
using CloudAlarmOverlay.Infrastructure.Services;

namespace CloudAlarmOverlay.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IAppPaths, AppPaths>();
        services.AddSingleton<Google.IGoogleVault,Google.GoogleVault>();
        services.AddSingleton<Google.GoogleBuiltInClient>();
        services.AddSingleton<Google.GoogleApi>();
        services.AddSingleton<IGoogleWorkspace,Google.GoogleWorkspace>();
        services.AddSingleton<OpenMeteoWeatherClient>();
        services.AddSingleton<IWeatherService, WeatherService>();
        services.AddSingleton<ISheetCsvClient, SheetCsvClient>();
        services.AddSingleton<ICalendarDataSource, GitHubCalendarDataSource>();
        services.AddSingleton<ISoundService, SoundService>();
        services.AddSingleton<IThemeService, ThemeService>();
        return services;
    }
}

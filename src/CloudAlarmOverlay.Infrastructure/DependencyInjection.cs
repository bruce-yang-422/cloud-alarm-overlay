using Microsoft.Extensions.DependencyInjection;
using CloudAlarmOverlay.Core.Services;
using CloudAlarmOverlay.Infrastructure.Services;

namespace CloudAlarmOverlay.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IAppPaths, AppPaths>();
        services.AddSingleton<OpenMeteoWeatherClient>();
        services.AddSingleton<IWeatherService, WeatherService>();
        services.AddSingleton<ISheetCsvClient, SheetCsvClient>();
        services.AddSingleton<ISoundService, SoundService>();
        services.AddSingleton<IThemeService, ThemeService>();
        return services;
    }
}

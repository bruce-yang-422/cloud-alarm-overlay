using System.IO;
using CloudAlarmOverlay.Core.Services;
using Microsoft.Extensions.Logging;
namespace CloudAlarmOverlay.App.Services;
public sealed class LocalFileLoggerProvider(IAppPaths paths):ILoggerProvider
{
    private readonly IAppPaths appPaths=paths;
    private readonly object gate=new();
    public ILogger CreateLogger(string categoryName)=>new LocalLogger(this,categoryName);
    public void Dispose(){}
    private sealed class LocalLogger(LocalFileLoggerProvider owner,string category):ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState:notnull=>null;
        public bool IsEnabled(LogLevel level)=>level>=LogLevel.Warning;
        public void Log<TState>(LogLevel level,EventId eventId,TState state,Exception? exception,Func<TState,Exception?,string> formatter)
        {
            if(!IsEnabled(level))return;
            try
            {
                lock(owner.gate)
                {
                    var folder=Path.Combine(owner.appPaths.DataDirectory,"logs");Directory.CreateDirectory(folder);
                    File.AppendAllText(Path.Combine(folder,$"runtime-{DateTime.Today:yyyyMMdd}.log"),
                        $"{DateTime.Now:O} [{level}] {category}: {formatter(state,exception)}{Environment.NewLine}{exception}{Environment.NewLine}");
                }
            }
            catch(IOException){/* Logging must not terminate the reminder worker. */}
            catch(UnauthorizedAccessException){}
        }
    }
}

using System.IO;
using System.Windows.Controls;
using CloudAlarmOverlay.App.ViewModels;
using CloudAlarmOverlay.App.Views;
using CloudAlarmOverlay.BackgroundServices;
using CloudAlarmOverlay.Core;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Services;
using CloudAlarmOverlay.Data;
using CloudAlarmOverlay.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CloudAlarmOverlay.App.Tests;

public sealed class CompositionTests
{
    [Fact]
    public void Cloud_selection_allows_copy_but_disables_edit_toggle_and_delete()
    {
        using var host=CompositionRoot.CreateHost();
        var vm=host.Services.GetRequiredService<MainViewModel>();
        var task=new CloudAlarmOverlay.Core.Models.AlarmTask {
            Id="cloud",Title="雲端",Source=CloudAlarmOverlay.Core.Models.TaskSources.SheetA,
            ScheduledAt=DateTime.Now,CreatedAt=DateTime.Now,UpdatedAt=DateTime.Now};
        var cloud=new TaskRow(task);
        var local=new TaskRow(task with {Id="local",Source=CloudAlarmOverlay.Core.Models.TaskSources.Local});
        vm.SetTaskSelection([cloud]);
        Assert.True(vm.CopyTaskCommand.CanExecute(null));
        Assert.True(vm.BatchTasksCommand.CanExecute("copy"));
        Assert.False(vm.EditTaskCommand.CanExecute(null));
        Assert.False(vm.ToggleTaskCommand.CanExecute(null));
        Assert.False(vm.DeleteTaskCommand.CanExecute(null));
        foreach(var operation in new[]{"enable","disable","delete"})Assert.False(vm.BatchTasksCommand.CanExecute(operation));
        vm.SetTaskSelection([cloud,local]);
        Assert.False(vm.EditTaskCommand.CanExecute(null));
        Assert.True(vm.BatchTasksCommand.CanExecute("enable"));
        Assert.Contains("只處理本機",vm.SelectionHint);
        vm.SetTaskSelection([local]);
        Assert.True(vm.EditTaskCommand.CanExecute(null));
        Assert.True(vm.ToggleTaskCommand.CanExecute(null));
        Assert.True(vm.DeleteTaskCommand.CanExecute(null));
        vm.SetTaskSelection([]);
        Assert.False(vm.CopyTaskCommand.CanExecute(null));
        Assert.False(vm.EditTaskCommand.CanExecute(null));
    }
    [Fact]
    public void Every_service_and_repository_contract_resolves_from_production_DI()
    {
        using var host = CompositionRoot.CreateHost();
        var contracts = typeof(IAlarmService).Assembly.GetTypes()
            .Where(type => type.IsInterface &&
                type.Namespace is "CloudAlarmOverlay.Core.Services" or "CloudAlarmOverlay.Core.Repositories")
            .Append(typeof(ISqliteConnectionFactory)).ToArray();
        Assert.NotEmpty(contracts);
        foreach (var contract in contracts)
            Assert.IsAssignableFrom(contract, host.Services.GetRequiredService(contract));
        Assert.NotNull(host.Services.GetRequiredService<MainViewModel>());
        Assert.Equal(5, host.Services.GetServices<IHostedService>().Count());
    }

    [Fact]
    public void Default_database_path_is_in_roaming_AppData()
    {
        var paths = new AppPaths();
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CloudAlarmOverlay", "cloud_alarm_overlay.db"), paths.DatabasePath);
    }

    [Fact]
    public async Task Host_start_initializes_database_with_default_admin_without_creating_identity()
    {
        var paths = new TestPaths();
        using var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                CompositionRoot.ConfigureServices(services);
                // Later registration replaces only the filesystem location for test isolation.
                services.AddSingleton<IAppPaths>(paths);
            }).Build();
        try
        {
            await host.StartAsync();
            Assert.True(File.Exists(paths.DatabasePath));
            await using var connection = await host.Services.GetRequiredService<ISqliteConnectionFactory>().OpenConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            Assert.Equal((long)DatabaseInitializer.CurrentSchemaVersion, await command.ExecuteScalarAsync());
            command.CommandText = "SELECT COUNT(*) FROM Devices;";
            Assert.Equal(0L, await command.ExecuteScalarAsync());
            Assert.True(await host.Services.GetRequiredService<IAuthenticationService>().AuthenticateAsync("admin","12345"));
        }
        finally
        {
            await host.StopAsync();
            if (Directory.Exists(paths.DataDirectory))
                Directory.Delete(paths.DataDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task MainWindow_can_be_resolved_shown_and_closed_on_STA_with_bound_title_and_navigation_content()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            MainWindow? window = null;
            try
            {
                using var host = CompositionRoot.CreateHost();
                window = host.Services.GetRequiredService<MainWindow>();
                window.ShowInTaskbar = false;
                window.ShowActivated = false;
                window.Show();
                window.UpdateLayout();
                Assert.True(window.IsVisible);
                Assert.IsType<MainViewModel>(window.DataContext);
                Assert.Equal("Cloud Alarm Overlay", window.Title);
                Assert.NotEmpty(Assert.IsType<Grid>(window.Content).Children);
                window.Close();
                Assert.False(window.IsVisible);
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
            finally
            {
                window?.Close();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }

    private sealed class TestPaths : IAppPaths
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(),
            "CloudAlarmOverlay.App.Tests", Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "test.db");
    }
}

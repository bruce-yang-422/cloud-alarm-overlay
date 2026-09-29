using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CloudAlarmOverlay.App.Controls;
using CloudAlarmOverlay.App.Services;
using CloudAlarmOverlay.App.ViewModels;
using CloudAlarmOverlay.App.Views;
using CloudAlarmOverlay.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CloudAlarmOverlay.App.Tests;

public sealed class UsageModeUiTests
{
    [Theory]
    [InlineData("個人使用",true)]
    [InlineData("公司使用",false)]
    public async Task First_setup_saves_selection_and_opens_only_personal_admin_without_login(string selection,bool personal)
    {
        await MilestoneOneTests.RunSta(async()=>
        {
            using var fixture=new Fixture();await fixture.Get<IDatabaseInitializer>().InitializeAsync();
            var identity=fixture.Get<IDeviceIdentityService>();
            var setup=new IdentityWindow(identity){ShowActivated=false,ShowInTaskbar=false};
            var vm=(IdentityViewModel)setup.DataContext;
            Exception? error=null;
            setup.Loaded+=async(_,_)=>
            {
                try
                {
                    Assert.False(vm.SaveCommand.CanExecute(null));
                    var selector=(SlidingSegmentedControl)setup.FindName("UsageModeSelector");
                    Assert.Equal(new[]{"個人使用","公司使用"},selector.Items.Cast<string>());
                    selector.SelectedItem=selection;Assert.Equal(selection,vm.UsageSelection);
                    if(!personal)vm.DeviceId="COMPANY-TEST";
                    vm.DisplayName="首次設定測試";
                    Assert.Equal(!personal,vm.ShowDeviceCode);Assert.True(vm.SaveCommand.CanExecute(null));
                    setup.UpdateLayout();await Task.Delay(180);Capture(setup,personal?"setup-personal":"setup-company");
                    await vm.SaveCommand.ExecuteAsync(null);Assert.Empty(vm.Error);
                }
                catch(Exception ex){error=ex;setup.Close();}
            };
            var result=setup.ShowDialog();if(error is not null)throw error;
            Assert.True(result);Assert.Equal(personal,fixture.Get<AdminSession>().IsAuthenticated);
            await identity.InitializeAccessAsync();
            var main=fixture.Get<MainViewModel>();var window=fixture.Get<MainWindow>();
            try
            {
                await main.InitializeAsync();Assert.Equal(personal,main.Admin.IsAuthenticated);
                Assert.Equal(!personal,main.Admin.IsCompanyMode);
                window.ShowActivated=false;window.ShowInTaskbar=false;window.Show();
                main.PageIndex=5;Assert.Equal(personal?5:0,main.PageIndex);
                if(personal)
                {
                    main.Admin.SelectedTab=1;window.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    Capture(window,"personal-admin");
                    main.Admin.LogoutCommand.Execute(null);Assert.True(main.Admin.Session.IsAuthenticated);
                    Assert.False(await fixture.Get<IExitProtectionService>().IsRequiredAsync());
                }
            }
            finally{window.ForceClose();}
        });
    }
    private static void Capture(Window window,string name)
    {
        var folder=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../artifacts/screenshots"));Directory.CreateDirectory(folder);
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),(int)Math.Ceiling(window.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(window);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(folder,name+".png"));png.Save(file);
    }
    private sealed class Fixture:IDisposable,IAppPaths
    {
        public string DataDirectory{get;}=Path.Combine(Path.GetTempPath(),"CloudAlarmUsageUi",Guid.NewGuid().ToString("N"));
        public string DatabasePath=>Path.Combine(DataDirectory,"test.db");
        private readonly IHost host;
        public Fixture()=>host=new HostBuilder().ConfigureServices(s=>{CompositionRoot.ConfigureServices(s);s.AddSingleton<IAppPaths>(this);}).Build();
        public T Get<T>() where T:notnull=>host.Services.GetRequiredService<T>();
        public void Dispose(){host.Dispose();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(DataDirectory))Directory.Delete(DataDirectory,true);}
    }
}

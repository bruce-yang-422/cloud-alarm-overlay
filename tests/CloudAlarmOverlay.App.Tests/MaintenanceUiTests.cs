using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CloudAlarmOverlay.App.Styles;
using CloudAlarmOverlay.App.Views;
using CloudAlarmOverlay.App.ViewModels;
using ThemeMode = CloudAlarmOverlay.Core.Models.ThemeMode;
using CloudAlarmOverlay.Core.Services;
using CloudAlarmOverlay.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
namespace CloudAlarmOverlay.App.Tests;

public sealed class MaintenanceUiTests
{
    [Fact] public async Task Sound_preview_reports_execution_even_when_notification_sound_is_off()
    {
        await MilestoneOneTests.RunSta(async()=>
        {
            var fake=new PreviewSound();
            using var host=new HostBuilder().ConfigureServices(services=>
            {
                CompositionRoot.ConfigureServices(services);
                services.AddSingleton<ISoundService>(fake);
            }).Build();
            var vm=host.Services.GetRequiredService<PreferencesViewModel>();
            var row=new SoundRow{Level=AlarmLevels.Low,Name="test",Available=["test"],Enabled=false};
            var playing=vm.PreviewSoundCommand.ExecuteAsync(row);
            Assert.True(row.IsPreviewing);
            Assert.Equal("◉ 試聽中",row.PreviewButtonLabel);
            await playing;
            Assert.False(row.IsPreviewing);
            Assert.Equal("▷ 試聽",row.PreviewButtonLabel);
            Assert.Contains("已執行試聽",row.Note);
            Assert.Equal(1,fake.Plays);
        });
    }
    private sealed class PreviewSound:ISoundService
    {
        public int Plays {get;private set;}
        public IReadOnlyList<string> GetAvailableSounds()=>["test"];
        public Task PlayAsync(string soundName,CancellationToken cancellationToken=default){Plays++;return Task.CompletedTask;}
    }
    [Fact] public async Task Checkbox_clicks_accumulate_and_uncheck_only_the_clicked_task()
    {
        await MilestoneOneTests.RunSta(async()=>
        {
            using var host=CompositionRoot.CreateHost();
            var vm=host.Services.GetRequiredService<MainViewModel>();
            var window=host.Services.GetRequiredService<MainWindow>();
            try
            {
                for(var i=0;i<4;i++)vm.Tasks.Add(new TaskRow(new CloudAlarmOverlay.Core.Models.AlarmTask {
                    Id=i.ToString(),Title="任務"+i,ScheduledAt=DateTime.Now,CreatedAt=DateTime.Now,UpdatedAt=DateTime.Now}));
                vm.PageIndex=1;window.Show();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var grid=(System.Windows.Controls.DataGrid)window.FindName("TaskGrid");
                static System.Windows.Controls.CheckBox? Check(DependencyObject node)
                {
                    if(node is System.Windows.Controls.CheckBox box)return box;
                    for(var i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)if(Check(VisualTreeHelper.GetChild(node,i)) is {} child)return child;
                    return null;
                }
                void Click(int index)
                {
                    var row=(System.Windows.Controls.DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(index);
                    var box=Check(row)!;
                    box.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0,System.Windows.Input.MouseButton.Left){RoutedEvent=UIElement.PreviewMouseLeftButtonDownEvent});
                }
                for(var i=0;i<4;i++){Click(i);Assert.Equal(i+1,vm.SelectedTasks.Count);}
                Click(1);Assert.Equal(3,vm.SelectedTasks.Count);Assert.DoesNotContain(vm.SelectedTasks,t=>t.Task.Id=="1");
            }
            finally {window.ForceClose();}
        });
    }
    [Fact] public async Task Tray_menu_and_submenu_follow_theme()
    {
        await MilestoneOneTests.RunSta(async()=>
        {
            var menu=new System.Windows.Controls.ContextMenu();
            menu.Resources.MergedDictionaries.Add(new ResourceDictionary {Source=new Uri("pack://application:,,,/CloudAlarmOverlay.App;component/Styles/TrayMenu.xaml")});
            var parent=new System.Windows.Controls.MenuItem {Header="測試通知"};
            var child=new System.Windows.Controls.MenuItem {Header="一般提醒"};
            parent.Items.Add(child);menu.Items.Add(parent);
            try
            {
                menu.IsOpen=true;
                foreach(var dark in new[]{false,true})
                {
                    AdaptiveBrushExtension.Apply(dark);parent.IsSubmenuOpen=true;
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    Assert.Equal(dark,((SolidColorBrush)menu.Background).Color.R<128);
                    Assert.Equal(dark,((SolidColorBrush)child.Foreground).Color.R>128);
                    var popup=Assert.IsType<System.Windows.Controls.Primitives.Popup>(parent.Template.FindName("PART_Popup",parent));
                    var border=Assert.IsType<System.Windows.Controls.Border>(popup.Child);
                    Assert.Equal(dark,((SolidColorBrush)border.Background).Color.R<128);
                    parent.IsSubmenuOpen=false;
                }
            }
            finally {menu.IsOpen=false;AdaptiveBrushExtension.Apply(false);}
        });
    }
    [Fact] public async Task Folder_tabs_remain_compact_and_side_by_side()
    {
        await MilestoneOneTests.RunSta(async()=>
        {
            var tabs=new System.Windows.Controls.TabControl();
            var first=new System.Windows.Controls.TabItem {Header="任務提醒"};
            var second=new System.Windows.Controls.TabItem {Header="番茄鐘"};
            tabs.Items.Add(first);tabs.Items.Add(second);
            var window=new Window {Content=tabs,Width=800,Height=400,ShowActivated=false,ShowInTaskbar=false};
            window.Resources.MergedDictionaries.Add(new ResourceDictionary {Source=new Uri("pack://application:,,,/CloudAlarmOverlay.App;component/Styles/LightTheme.xaml")});
            tabs.Style=(Style)window.FindResource("FolderTabs");
            try
            {
                window.Show();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                foreach(var selected in new[]{0,1})
                {
                    tabs.SelectedIndex=selected;window.UpdateLayout();
                    var a=first.TranslatePoint(new Point(),tabs);var b=second.TranslatePoint(new Point(),tabs);
                    Assert.Equal(a.Y,b.Y,1);Assert.True(b.X>a.X);
                    Assert.InRange(first.ActualWidth,70,180);Assert.InRange(second.ActualWidth,60,180);
                    Assert.InRange(first.ActualHeight,25,55);
                }
            }
            finally {window.Close();}
        });
    }
    [Fact] public async Task Primary_button_rendered_text_stays_white_in_both_themes()
    {
        await MilestoneOneTests.RunSta(async()=>
        {
            var button=new System.Windows.Controls.Button {Content="查詢紀錄"};
            var window=new Window {Content=button,Width=220,Height=120,ShowActivated=false,ShowInTaskbar=false};
            window.Resources.MergedDictionaries.Add(new ResourceDictionary {Source=new Uri("pack://application:,,,/CloudAlarmOverlay.App;component/Styles/LightTheme.xaml")});
            button.Style=(Style)window.FindResource("Primary");
            static IEnumerable<System.Windows.Controls.TextBlock> Texts(DependencyObject node)
            {
                for(var i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)
                {
                    var child=VisualTreeHelper.GetChild(node,i);
                    if(child is System.Windows.Controls.TextBlock text)yield return text;
                    foreach(var nested in Texts(child))yield return nested;
                }
            }
            try
            {
                window.Show();
                foreach(var dark in new[]{false,true})
                {
                    AdaptiveBrushExtension.Apply(dark);await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    var text=Assert.Single(Texts(button));
                    Assert.Equal(Colors.White,((SolidColorBrush)text.Foreground).Color);
                }
            }
            finally {AdaptiveBrushExtension.Apply(false);window.Close();}
        });
    }
    [Fact] public async Task Form_controls_switch_theme_and_editable_combo_keeps_input()
    {
        await MilestoneOneTests.RunSta(async()=>
        {
            var panel=new System.Windows.Controls.StackPanel();
            var window=new Window {Content=panel,Width=420,Height=400,ShowActivated=false,ShowInTaskbar=false};
            window.Resources.MergedDictionaries.Add(new ResourceDictionary {Source=new Uri("pack://application:,,,/CloudAlarmOverlay.App;component/Styles/LightTheme.xaml")});
            var password=new System.Windows.Controls.PasswordBox();
            var combo=new System.Windows.Controls.ComboBox {IsEditable=true,ItemsSource=new[]{"200","500","800"},SelectedIndex=1};
            var toggle=new System.Windows.Controls.CheckBox {Content="播放提示音",Style=(Style)window.FindResource("PillSwitch")};
            var label=new System.Windows.Controls.Label {Content="密碼"};
            panel.Children.Add(password);panel.Children.Add(combo);panel.Children.Add(toggle);panel.Children.Add(label);
            try
            {
                AdaptiveBrushExtension.Apply(false);window.Show();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var light=((SolidColorBrush)password.Background).Color;
                AdaptiveBrushExtension.Apply(true);await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.NotEqual(light,((SolidColorBrush)password.Background).Color);
                Assert.True(((SolidColorBrush)toggle.Foreground).Color.R>200);
                Assert.True(((SolidColorBrush)label.Foreground).Color.R>200);
                var input=Assert.IsType<System.Windows.Controls.TextBox>(combo.Template.FindName("PART_EditableTextBox",combo));
                Assert.Equal(Visibility.Visible,input.Visibility);
                combo.Text="1000";await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Equal("1000",input.Text);
                combo.IsDropDownOpen=true;await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var item=Assert.IsType<System.Windows.Controls.ComboBoxItem>(combo.ItemContainerGenerator.ContainerFromIndex(0));
                Assert.True(((SolidColorBrush)item.Foreground).Color.R>200);
                combo.IsDropDownOpen=false;
                AdaptiveBrushExtension.Apply(false);await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Equal(light,((SolidColorBrush)password.Background).Color);
            }
            finally {AdaptiveBrushExtension.Apply(false);window.Close();}
        });
    }
    [Fact] public async Task Theme_changes_existing_windows_and_maintenance_page_loads()
    {
        await MilestoneOneTests.RunSta(async()=>
        {
            using var host=CompositionRoot.CreateHost();
            var theme=host.Services.GetRequiredService<IThemeService>();
            var window=host.Services.GetRequiredService<MainWindow>();
            theme.Changed+=AdaptiveBrushExtension.Apply;
            try
            {
                theme.Apply(ThemeMode.Light);
                window.ShowInTaskbar=false;window.ShowActivated=false;window.Show();
                var initial=((SolidColorBrush)window.Background).Color;
                theme.Apply(ThemeMode.Dark); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.NotEqual(initial,((SolidColorBrush)window.Background).Color);
                Assert.True(theme.IsDark);
                var view=new MaintenanceView{DataContext=host.Services.GetRequiredService<MaintenanceViewModel>()};
                view.Measure(new Size(800,800));view.Arrange(new Rect(0,0,800,800));view.UpdateLayout();
                Assert.True(view.ActualHeight>0);
                Assert.Null(view.FindName("IncludeAdmin"));
                Assert.Throws<UnauthorizedAccessException>(()=>host.Services.GetRequiredService<MaintenanceViewModel>().RequireAdministrator());
                theme.Apply(ThemeMode.Light);await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Equal(initial,((SolidColorBrush)window.Background).Color);
            }
            finally {theme.Apply(ThemeMode.Light);theme.Changed-=AdaptiveBrushExtension.Apply;window.ForceClose();}
        });
    }
}

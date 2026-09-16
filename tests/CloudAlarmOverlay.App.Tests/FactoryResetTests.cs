using System.IO;
using CloudAlarmOverlay.App.Services;
namespace CloudAlarmOverlay.App.Tests;

public sealed class FactoryResetTests
{
    [Fact] public void Reset_removes_all_data_only_after_request_and_preserves_external_backup()
    {
        var parent=Path.Combine(Path.GetTempPath(),"reset-test-"+Guid.NewGuid());
        var root=Path.Combine(parent,"CloudAlarmOverlay");
        Directory.CreateDirectory(Path.Combine(root,"logs"));
        var database=Path.Combine(root,"cloud_alarm_overlay.db");
        var backup=Path.Combine(parent,"backup.calbak");
        File.WriteAllText(database,"data");File.WriteAllText(backup,"backup");
        File.WriteAllText(Path.Combine(root,"logs","app.log"),"log");
        var reset=new FactoryReset(root);
        Assert.False(reset.ApplyPending());Assert.True(File.Exists(database));
        reset.Request();Assert.True(File.Exists(database));
        Assert.True(reset.ApplyPending());Assert.Empty(Directory.EnumerateFileSystemEntries(root));
        Assert.Equal("backup",File.ReadAllText(backup));Assert.False(reset.ApplyPending());
        File.Delete(backup);Directory.Delete(root);Directory.Delete(parent);
    }
    [Fact] public void Failed_cleanup_keeps_request_for_next_startup()
    {
        var parent=Path.Combine(Path.GetTempPath(),"reset-test-"+Guid.NewGuid());
        var root=Path.Combine(parent,"CloudAlarmOverlay");
        var reset=new FactoryReset(root);reset.Request();
        var database=Path.Combine(root,"locked.db");
        using(var locked=new FileStream(database,FileMode.Create,FileAccess.ReadWrite,FileShare.None))
            Assert.Throws<IOException>(()=>reset.ApplyPending());
        Assert.True(reset.ApplyPending());Assert.Empty(Directory.EnumerateFileSystemEntries(root));
        Directory.Delete(root);Directory.Delete(parent);
    }
    [Fact] public void Rejects_unrelated_directory()
    {
        var reset=new FactoryReset(Path.GetTempPath());
        Assert.Throws<InvalidOperationException>(()=>reset.Request());
        Assert.Throws<InvalidOperationException>(()=>reset.ApplyPending());
    }
}

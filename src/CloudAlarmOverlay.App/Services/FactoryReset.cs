using System.IO;
namespace CloudAlarmOverlay.App.Services;

// Applied before the host opens SQLite or starts any background workers.
public sealed class FactoryReset(string dataDirectory)
{
    private string Root
    {
        get
        {
            var root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataDirectory));
            if(!string.Equals(Path.GetFileName(root),"CloudAlarmOverlay",StringComparison.OrdinalIgnoreCase)
                || string.Equals(root,Path.GetPathRoot(root),StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("重置資料路徑不正確。");
            if(Directory.Exists(root)&&(File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)
                throw new InvalidOperationException("資料目錄為連結，無法安全重置。");
            return root;
        }
    }
    private const string Marker=".factory-reset-pending";
    public void Request()
    {
        var root=Root;
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root,Marker),"Reset on next startup");
    }
    public bool ApplyPending()
    {
        var root=Root;
        var marker=Path.Combine(root,Marker);
        if(!File.Exists(marker))return false;
        // Keep the marker until all data is removed, so an interrupted reset retries.
        foreach(var path in Directory.EnumerateFileSystemEntries(root))
            if(!string.Equals(path,marker,StringComparison.OrdinalIgnoreCase))DeleteEntry(path,root);
        File.Delete(marker);
        return true;
    }
    private static void DeleteEntry(string path,string root)
    {
        var full=Path.GetFullPath(path);
        if(!full.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("清除目標超出程式資料目錄。");
        var attributes=File.GetAttributes(full);
        if((attributes&FileAttributes.Directory)!=0)
        {
            if((attributes&FileAttributes.ReparsePoint)==0)
                foreach(var child in Directory.EnumerateFileSystemEntries(full))DeleteEntry(child,root);
            Directory.Delete(full);
        }
        else File.Delete(full);
    }
}

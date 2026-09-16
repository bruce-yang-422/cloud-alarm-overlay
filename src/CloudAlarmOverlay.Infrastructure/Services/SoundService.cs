using System.Runtime.InteropServices;
using Microsoft.Win32;
using CloudAlarmOverlay.Core.Services;
namespace CloudAlarmOverlay.Infrastructure.Services;
internal sealed class SoundService:ISoundService
{
    private static Dictionary<string,string> ReadSounds()
    {
        var sounds=new Dictionary<string,string>();
        try
        {
            using var root=Registry.CurrentUser.OpenSubKey(@"AppEvents\Schemes\Apps\.Default");
            if(root is null)return sounds;
            foreach(var name in root.GetSubKeyNames())
            {
                using var key=root.OpenSubKey(name+@"\.Current");
                if(key?.GetValue(null) is string path&&File.Exists(Environment.ExpandEnvironmentVariables(path)))sounds[name]=Environment.ExpandEnvironmentVariables(path);
            }
        }
        catch(System.Security.SecurityException){}
        catch(UnauthorizedAccessException){}
        return sounds;
    }
    public IReadOnlyList<string> GetAvailableSounds()=>ReadSounds().Keys.Order().ToArray();
    public Task PlayAsync(string soundName,CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if(!ReadSounds().TryGetValue(soundName,out var path))throw new InvalidOperationException("此 Windows 音效已不存在，請重新選擇。");
        if(!PlaySound(path,IntPtr.Zero,0x00020000|0x0001|0x0002))throw new InvalidOperationException("無法播放選取的音效。");
        return Task.CompletedTask;
    }
    [DllImport("winmm.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySound(string pszSound,IntPtr hmod,uint fdwSound);
}

using System.Runtime.InteropServices;
using System.Windows.Controls;
namespace CloudAlarmOverlay.App.Services;

public static class WindowsEmojiPanel
{
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
    }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort Key, Scan; public uint Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    public static void Open(TextBox target)
    {
        target.Focus();
        static Input Key(ushort key, uint flags = 0) => new() { Type = 1, Data = new() { Keyboard = new() { Key = key, Flags = flags } } };
        Input[] keys = [Key(0x5B), Key(0xBE), Key(0xBE, 2), Key(0x5B, 2)];
        if (SendInput(4, keys, Marshal.SizeOf<Input>()) != 4) target.ToolTip = "請按 Windows 鍵 + 句點開啟 emoji 面板。";
    }
}

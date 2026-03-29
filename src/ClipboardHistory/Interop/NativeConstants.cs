namespace ClipboardHistory.Interop;

/// <summary>Win32 hotkey modifier flags for RegisterHotKey.</summary>
public static class NativeConstants
{
    public const int ModAlt = 0x0001;
    public const int ModControl = 0x0002;
    public const int ModShift = 0x0004;
    public const int ModWin = 0x0008; /* undocumented in some docs; Win key modifier for RegisterHotKey */

    public const int WmHotkey = 0x0312;
    public const int WmClipboardUpdate = 0x031D;

    public const int HotkeyIdToggleWindow = 1;
}

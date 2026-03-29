using ClipboardHistory.Interop;

namespace ClipboardHistory.Services;

public sealed class HotkeyService : IDisposable
{
    private readonly SettingsService _settings;
    private IntPtr _hwnd;

    public HotkeyService(SettingsService settings) => _settings = settings;

    public void Register(IntPtr windowHandle)
    {
        UnregisterInternal();
        _hwnd = windowHandle;
        if (_hwnd == IntPtr.Zero)
            return;

        var s = _settings.Current;
        if (!NativeMethods.RegisterHotKey(_hwnd, NativeConstants.HotkeyIdToggleWindow, (uint)s.ToggleHotkeyModifiers, (uint)s.ToggleHotkeyVk))
        {
            // Hotkey may be taken by another app; fail silently in V1.
        }
    }

    public void Unregister()
    {
        UnregisterInternal();
        _hwnd = IntPtr.Zero;
    }

    private void UnregisterInternal()
    {
        if (_hwnd != IntPtr.Zero)
            NativeMethods.UnregisterHotKey(_hwnd, NativeConstants.HotkeyIdToggleWindow);
    }

    public void Dispose() => Unregister();
}

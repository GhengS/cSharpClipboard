using System.Windows;
using ClipboardHistory.Interop;

namespace ClipboardHistory.Services;

/// <summary>
/// Listens for WM_CLIPBOARDUPDATE via AddClipboardFormatListener. Must be called on the UI thread (STA) when reading WPF Clipboard.
/// </summary>
public sealed class ClipboardMonitorService : IDisposable
{
    private readonly SettingsService _settings;
    private IntPtr _hwnd;
    private string? _lastAcceptedText;

    public ClipboardMonitorService(SettingsService settings) => _settings = settings;

    public void Attach(IntPtr windowHandle)
    {
        Detach();
        _hwnd = windowHandle;
        if (_hwnd == IntPtr.Zero)
            return;
        if (!NativeMethods.AddClipboardFormatListener(_hwnd))
            throw new InvalidOperationException("AddClipboardFormatListener failed.");
    }

    public void Detach()
    {
        if (_hwnd == IntPtr.Zero)
            return;
        NativeMethods.RemoveClipboardFormatListener(_hwnd);
        _hwnd = IntPtr.Zero;
    }

    /// <summary>Call from WndProc when msg == WM_CLIPBOARDUPDATE.</summary>
    public void OnClipboardUpdated()
    {
        if (ClipboardSuppression.IsActive)
            return;

        string? text;
        try
        {
            if (!Clipboard.ContainsText(TextDataFormat.UnicodeText))
                return;
            text = Clipboard.GetText(TextDataFormat.UnicodeText);
        }
        catch
        {
            return;
        }

        if (string.IsNullOrEmpty(text))
            return;

        var maxLen = Math.Max(1, _settings.Current.MaxEntryLength);
        if (text.Length > maxLen)
            text = text[..maxLen];

        if (_settings.Current.DedupeConsecutive && text == _lastAcceptedText)
            return;

        _lastAcceptedText = text;
        TextCaptured?.Invoke(this, text);
    }

    public event EventHandler<string>? TextCaptured;

    public void Dispose() => Detach();
}

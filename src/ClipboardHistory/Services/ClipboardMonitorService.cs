using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.IO;
using ClipboardHistory.Interop;
using ClipboardHistory.Models;

namespace ClipboardHistory.Services;

public record ClipboardData(ClipboardItemType Type, string Content, byte[]? ImageData = null);

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

        try
        {
            if (Clipboard.ContainsText(TextDataFormat.UnicodeText))
            {
                var text = Clipboard.GetText(TextDataFormat.UnicodeText);
                if (string.IsNullOrEmpty(text)) return;

                var maxLen = Math.Max(1, _settings.Current.MaxEntryLength);
                if (text.Length > maxLen)
                    text = text[..maxLen];

                var capsMerge = Keyboard.IsKeyToggled(Key.CapsLock);
                if (_settings.Current.DedupeConsecutive && !capsMerge && text == _lastAcceptedText)
                    return;

                _lastAcceptedText = text;
                DataCaptured?.Invoke(this, new ClipboardData(ClipboardItemType.Text, text));
            }
            else if (Clipboard.ContainsImage())
            {
                var image = Clipboard.GetImage();
                if (image != null)
                {
                    var data = EncodeImage(image);
                    if (data != null)
                    {
                        _lastAcceptedText = null; // Reset text tracking when image is captured
                        DataCaptured?.Invoke(this, new ClipboardData(ClipboardItemType.Image, "Image", data));
                    }
                }
            }
            else if (Clipboard.ContainsFileDropList())
            {
                var files = Clipboard.GetFileDropList();
                if (files.Count > 0)
                {
                    var paths = new string[files.Count];
                    files.CopyTo(paths, 0);
                    var text = string.Join(Environment.NewLine, paths);
                    _lastAcceptedText = null;
                    DataCaptured?.Invoke(this, new ClipboardData(ClipboardItemType.File, text));
                }
            }
        }
        catch
        {
            // Clipboard access might fail if another app is using it.
        }
    }

    private byte[]? EncodeImage(BitmapSource bitmap)
    {
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }
        catch { return null; }
    }

    public event EventHandler<ClipboardData>? DataCaptured;

    public void Dispose() => Detach();
}

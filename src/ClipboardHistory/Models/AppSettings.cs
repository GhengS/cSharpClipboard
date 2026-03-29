using ClipboardHistory.Interop;

namespace ClipboardHistory.Models;

/// <summary>User preferences persisted to disk (JSON).</summary>
public sealed class AppSettings
{
    public int MaxHistoryEntries { get; set; } = 500;
    public int MaxEntryLength { get; set; } = 512_000;
    public bool DedupeConsecutive { get; set; } = true;
    public ViewDisplayMode ViewMode { get; set; } = ViewDisplayMode.List;
    public string AppendSeparator { get; set; } = Environment.NewLine;
    public bool CloseOnCopy { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public int ToggleHotkeyModifiers { get; set; } = NativeConstants.ModControl | NativeConstants.ModShift;
    public int ToggleHotkeyVk { get; set; } = 0x56; /* V */
    public double WindowWidth { get; set; } = 720;
    public double WindowHeight { get; set; } = 560;
}

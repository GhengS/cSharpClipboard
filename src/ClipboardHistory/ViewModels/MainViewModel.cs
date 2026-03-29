using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClipboardHistory.Interop;
using ClipboardHistory.Models;
using ClipboardHistory.Services;
using ClipboardHistory.Views;

namespace ClipboardHistory.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly HistoryRepository _repo;
    private readonly SettingsService _settings;
    private readonly ClipboardMonitorService _clipboard;
    private readonly List<ClipboardItem> _selectionOrdered = new();
    private readonly SemaphoreSlim _captureGate = new(1, 1);
    private CancellationTokenSource? _searchDebounceCts;
    /// <summary>After Caps-merge we <see cref="System.Windows.Clipboard.SetText(string)"/>; ignore the echo <see cref="ClipboardMonitorService.TextCaptured"/> for that exact payload.</summary>
    private string? _skipNextClipboardCaptureIfEquals;

    public MainViewModel(HistoryRepository repo, SettingsService settings, ClipboardMonitorService clipboard)
    {
        _repo = repo;
        _settings = settings;
        _clipboard = clipboard;
        ViewMode = settings.Current.ViewMode;
        _clipboard.DataCaptured += OnClipboardDataCaptured;
    }

    public ObservableCollection<ClipboardItem> Items { get; } = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ViewDisplayMode _viewMode;

    [ObservableProperty]
    private string _statusHint = string.Empty;

    /// <summary>Ask host window to hide (e.g. after copy when CloseOnCopy).</summary>
    public Action? RequestHideWindow { get; set; }

    partial void OnViewModeChanged(ViewDisplayMode value)
    {
        _settings.Current.ViewMode = value;
        _settings.Save();
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchDebounceCts?.Cancel();
        _searchDebounceCts = new CancellationTokenSource();
        var token = _searchDebounceCts.Token;
        var queryText = value;
        var limit = _settings.Current.MaxHistoryEntries;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(280, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            IReadOnlyList<ClipboardItem> list;
            try
            {
                list = await _repo.QueryAsync(string.IsNullOrWhiteSpace(queryText) ? null : queryText, limit, token).ConfigureAwait(false);
            }
            catch
            {
                return;
            }

            try
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Items.Clear();
                    foreach (var item in list)
                        Items.Add(item);
                }, DispatcherPriority.Background, token);
            }
            catch (TaskCanceledException)
            {
                // Window closed while refreshing results.
            }
        }, token);
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var list = await _repo.QueryAsync(null, _settings.Current.MaxHistoryEntries, ct).ConfigureAwait(true);
        Items.Clear();
        foreach (var item in list)
            Items.Add(item);

        StatusHint = BuildHotkeyHint();
    }

    private void OnClipboardDataCaptured(object? sender, ClipboardData data)
    {
        _ = CaptureFromClipboardAsync(data);
    }

    private async Task CaptureFromClipboardAsync(ClipboardData data)
    {
        await _captureGate.WaitAsync().ConfigureAwait(true);
        try
        {
            var text = data.Content;
            if (_skipNextClipboardCaptureIfEquals is { } expectedSkip && text == expectedSkip)
            {
                _skipNextClipboardCaptureIfEquals = null;
                return;
            }

            var maxLen = Math.Max(1, _settings.Current.MaxEntryLength);
            if (data.Type == ClipboardItemType.Text && Keyboard.IsKeyToggled(Key.CapsLock))
            {
                var latest = await _repo.GetLatestAsync().ConfigureAwait(true);
                if (latest != null && latest.Type == ClipboardItemType.Text)
                {
                    var merged = latest.Content + '\n' + text;
                    if (merged.Length > maxLen)
                        merged = merged[^maxLen..];
                    await _repo.UpdateAsync(latest.Id, merged).ConfigureAwait(true);
                    // Replace system clipboard (and Windows Win+V history entry) with merged text; suppress monitor loop.
                    _skipNextClipboardCaptureIfEquals = merged;
                    ClipboardSuppression.ExecuteSuppressed(() =>
                        Clipboard.SetText(merged, TextDataFormat.UnicodeText));
                }
                else
                {
                    await _repo.InsertAsync(text, DateTime.UtcNow, data.Type, data.ImageData).ConfigureAwait(true);
                }
            }
            else
            {
                await _repo.InsertAsync(text, DateTime.UtcNow, data.Type, data.ImageData).ConfigureAwait(true);
            }

            await _repo.TrimToMaxAsync(_settings.Current.MaxHistoryEntries).ConfigureAwait(true);

            if (!string.IsNullOrWhiteSpace(SearchText))
                return;

            var latestEntry = await _repo.GetLatestAsync().ConfigureAwait(true);
            if (latestEntry != null)
            {
                // If it was an update (merged), replace in UI
                var existing = Items.FirstOrDefault(i => i.Id == latestEntry.Id);
                if (existing != null)
                {
                    existing.Content = latestEntry.Content;
                }
                else
                {
                    Items.Insert(0, latestEntry);
                    if (Items.Count > _settings.Current.MaxHistoryEntries)
                        Items.RemoveAt(Items.Count - 1);
                }
            }
        }
        catch
        {
            // Clipboard or DB edge cases should not crash the app.
        }
        finally
        {
            _captureGate.Release();
        }
    }

    public void SyncSelection(IReadOnlyList<ClipboardItem> orderedInListSequence)
    {
        _selectionOrdered.Clear();
        _selectionOrdered.AddRange(orderedInListSequence);
        DeleteSelectionCommand.NotifyCanExecuteChanged();
        EditSelectionCommand.NotifyCanExecuteChanged();
        CopySelectionCommand.NotifyCanExecuteChanged();
        AppendMergedCommand.NotifyCanExecuteChanged();
    }

    private ClipboardItem? PrimaryItem => _selectionOrdered.Count > 0 ? _selectionOrdered[0] : null;

    private bool HasSelection() => _selectionOrdered.Count > 0;

    private bool HasPrimary() => PrimaryItem != null;

    [RelayCommand(CanExecute = nameof(HasPrimary))]
    private void CopySelection()
    {
        var item = PrimaryItem;
        if (item == null)
            return;

        ClipboardSuppression.ExecuteSuppressed(() =>
        {
            if (item.Type == ClipboardItemType.Image && item.ImageData != null)
            {
                using var ms = new MemoryStream(item.ImageData);
                var decoder = new PngBitmapDecoder(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                Clipboard.SetImage(decoder.Frames[0]);
            }
            else if (item.Type == ClipboardItemType.File)
            {
                var paths = item.Content.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
                var sc = new System.Collections.Specialized.StringCollection();
                sc.AddRange(paths);
                Clipboard.SetFileDropList(sc);
            }
            else
            {
                Clipboard.SetText(item.Content, TextDataFormat.UnicodeText);
            }
        });

        if (_settings.Current.CloseOnCopy)
            RequestHideWindow?.Invoke();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void AppendMerged()
    {
        if (_selectionOrdered.Count == 0)
            return;

        var sep = _settings.Current.AppendSeparator;
        var sb = new StringBuilder();
        var count = 0;
        for (var i = 0; i < _selectionOrdered.Count; i++)
        {
            var item = _selectionOrdered[i];
            if (item.Type != ClipboardItemType.Text) continue;

            if (count > 0)
                sb.Append(sep);
            sb.Append(item.Content);
            count++;
        }

        if (count == 0) return;

        var merged = sb.ToString();
        ClipboardSuppression.ExecuteSuppressed(() =>
            Clipboard.SetText(merged, TextDataFormat.UnicodeText));

        if (_settings.Current.CloseOnCopy)
            RequestHideWindow?.Invoke();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DeleteSelectionAsync()
    {
        if (_selectionOrdered.Count == 0)
            return;

        var n = _selectionOrdered.Count;
        var msg = n == 1 ? "确定删除此条目？" : $"确定删除选中的 {n} 条记录？";
        var result = MessageBox.Show(msg, "剪贴板历史", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
            return;

        var toDelete = _selectionOrdered.ToArray();
        foreach (var item in toDelete)
        {
            await _repo.DeleteAsync(item.Id).ConfigureAwait(true);
            Items.Remove(item);
        }

        _selectionOrdered.Clear();
        NotifySelectionCommands();
    }

    [RelayCommand(CanExecute = nameof(HasPrimary))]
    private async Task EditSelectionAsync()
    {
        var item = PrimaryItem;
        if (item == null)
            return;

        if (item.Type != ClipboardItemType.Text)
        {
            MessageBox.Show("目前仅支持编辑文本条目。", "剪贴板历史", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new EditItemDialog(item.Content)
        {
            Owner = System.Windows.Application.Current.MainWindow,
        };

        if (dlg.ShowDialog() != true)
            return;

        var text = dlg.EditedText;
        var max = Math.Max(1, _settings.Current.MaxEntryLength);
        if (text.Length > max)
            text = text[..max];

        await _repo.UpdateAsync(item.Id, text).ConfigureAwait(true);
        item.Content = text;
    }

    [RelayCommand]
    private void ToggleListView() => ViewMode = ViewDisplayMode.List;

    [RelayCommand]
    private void ToggleCardView() => ViewMode = ViewDisplayMode.Card;

    private void NotifySelectionCommands()
    {
        DeleteSelectionCommand.NotifyCanExecuteChanged();
        EditSelectionCommand.NotifyCanExecuteChanged();
        CopySelectionCommand.NotifyCanExecuteChanged();
        AppendMergedCommand.NotifyCanExecuteChanged();
    }

    private string BuildHotkeyHint()
    {
        var s = _settings.Current;
        var parts = new List<string>();
        if ((s.ToggleHotkeyModifiers & NativeConstants.ModControl) != 0)
            parts.Add("Ctrl");
        if ((s.ToggleHotkeyModifiers & NativeConstants.ModShift) != 0)
            parts.Add("Shift");
        if ((s.ToggleHotkeyModifiers & NativeConstants.ModAlt) != 0)
            parts.Add("Alt");
        if ((s.ToggleHotkeyModifiers & NativeConstants.ModWin) != 0)
            parts.Add("Win");

        var vk = s.ToggleHotkeyVk;
        var ch = vk is >= 0x41 and <= 0x5A ? (char)vk : '?';
        parts.Add(ch.ToString());
        return $"显示/隐藏窗口：{string.Join("+", parts)}（可在设置 JSON 中调整 ToggleHotkeyModifiers / ToggleHotkeyVk）。开启 Caps Lock 时复制：追加到最新历史（换行分隔），并写回系统剪贴板（Win+V / Ctrl+V 为合并全文）。";
    }
}

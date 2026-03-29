using CommunityToolkit.Mvvm.ComponentModel;

namespace ClipboardHistory.Models;

public partial class ClipboardItem : ObservableObject
{
    public long Id { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview))]
    private string _content = string.Empty;

    public DateTime CreatedAtUtc { get; init; }

    public string Preview => Content.Length > 200 ? string.Concat(Content.AsSpan(0, 200), "…") : Content;
}

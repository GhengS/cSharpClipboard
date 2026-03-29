using CommunityToolkit.Mvvm.ComponentModel;

namespace ClipboardHistory.Models;

public enum ClipboardItemType
{
    Text,
    Image,
    File
}

public partial class ClipboardItem : ObservableObject
{
    public long Id { get; init; }

    public ClipboardItemType Type { get; init; } = ClipboardItemType.Text;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview))]
    private string _content = string.Empty;

    public byte[]? ImageData { get; init; }

    public DateTime CreatedAtUtc { get; init; }

    public string Preview => Type switch
    {
        ClipboardItemType.Text => Content.Length > 200 ? string.Concat(Content.AsSpan(0, 200), "…") : Content,
        ClipboardItemType.Image => "🖼️ [图片数据]",
        ClipboardItemType.File => "📂 [文件内容]",
        _ => Content
    };
}

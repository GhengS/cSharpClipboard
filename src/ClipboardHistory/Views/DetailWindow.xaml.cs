using System.Windows;
using System.Windows.Media.Imaging;
using System.IO;
using ClipboardHistory.Models;
using ClipboardHistory.Services;

namespace ClipboardHistory.Views;

public partial class DetailWindow : Window
{
    private readonly ClipboardItem _item;

    public DetailWindow(ClipboardItem item)
    {
        InitializeComponent();
        _item = item;
        DataContext = _item;
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        ClipboardSuppression.ExecuteSuppressed(() =>
        {
            if (_item.Type == ClipboardItemType.Image && _item.ImageData != null)
            {
                using var ms = new MemoryStream(_item.ImageData);
                var decoder = new PngBitmapDecoder(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                Clipboard.SetImage(decoder.Frames[0]);
            }
            else if (_item.Type == ClipboardItemType.File)
            {
                var paths = _item.Content.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
                var sc = new System.Collections.Specialized.StringCollection();
                sc.AddRange(paths);
                Clipboard.SetFileDropList(sc);
            }
            else
            {
                Clipboard.SetText(_item.Content, TextDataFormat.UnicodeText);
            }
        });
        
        DialogResult = true;
        Close();
    }
}

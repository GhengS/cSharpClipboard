using System.Windows;

namespace ClipboardHistory.Views;

public partial class EditItemDialog : Window
{
    public string EditedText => Editor.Text;

    public EditItemDialog(string initialText)
    {
        InitializeComponent();
        Editor.Text = initialText;
        Loaded += (_, _) =>
        {
            Editor.Focus();
            Editor.SelectAll();
        };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}

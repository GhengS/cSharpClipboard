using System.Drawing;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Input;
using ClipboardHistory.Interop;
using ClipboardHistory.Models;
using ClipboardHistory.Services;
using ClipboardHistory.ViewModels;

namespace ClipboardHistory;

public partial class MainWindow : Window
{
    private readonly SettingsService _settings;
    private readonly ClipboardMonitorService _clipboardMonitor;
    private readonly HotkeyService _hotkeyService;
    private readonly MainViewModel _viewModel;
    private HwndSource? _hwndSource;
    private Forms.NotifyIcon? _trayIcon;
    private bool _reallyExit;

    private System.Windows.Point _dragStartPoint;
    private bool _isDragging;
    private ListBoxItem? _draggedItemContainer;

    public MainWindow(
        HistoryRepository repository,
        SettingsService settings,
        ClipboardMonitorService clipboardMonitor,
        HotkeyService hotkeyService)
    {
        InitializeComponent();
        _settings = settings;
        _clipboardMonitor = clipboardMonitor;
        _hotkeyService = hotkeyService;

        _viewModel = new MainViewModel(repository, settings, clipboardMonitor)
        {
            RequestHideWindow = Hide,
        };
        DataContext = _viewModel;

        Width = settings.Current.WindowWidth;
        Height = settings.Current.WindowHeight;

        Loaded += MainWindow_OnLoaded;
        Closing += MainWindow_OnClosing;
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized && _settings.Current.MinimizeToTray)
                Hide();
        };
    }

    private async void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadAsync().ConfigureAwait(true);
        InitTray();
    }

    private void InitTray()
    {
        if (_trayIcon != null)
            return;

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "剪贴板历史",
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示窗口", null, (_, _) => ShowAndActivate());
        menu.Items.Add("退出", null, (_, _) => RequestExit());
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => ShowAndActivate();
    }

    private void ShowAndActivate()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void RequestExit()
    {
        _reallyExit = true;
        _trayIcon?.Dispose();
        _trayIcon = null;
        Close();
    }

    private void MainWindow_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_settings.Current.MinimizeToTray && !_reallyExit)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        _settings.Current.WindowWidth = Width;
        _settings.Current.WindowHeight = Height;
        _settings.Save();

        _hotkeyService.Unregister();
        _clipboardMonitor.Detach();

        if (_hwndSource != null)
            _hwndSource.RemoveHook(WndProc);
        _hwndSource = null;

        _trayIcon?.Dispose();
        _trayIcon = null;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var helper = new WindowInteropHelper(this);
        _hwndSource = HwndSource.FromHwnd(helper.Handle);
        _hwndSource?.AddHook(WndProc);

        try
        {
            _clipboardMonitor.Attach(helper.Handle);
        }
        catch
        {
            Dispatcher.InvokeAsync(() =>
                MessageBox.Show("无法注册剪贴板监听。", "剪贴板历史", MessageBoxButton.OK, MessageBoxImage.Warning));
        }

        _hotkeyService.Register(helper.Handle);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeConstants.WmClipboardUpdate)
        {
            _clipboardMonitor.OnClipboardUpdated();
            return IntPtr.Zero;
        }

        if (msg == NativeConstants.WmHotkey && wParam.ToInt32() == NativeConstants.HotkeyIdToggleWindow)
        {
            Dispatcher.BeginInvoke(ToggleVisibilityFromHotkey, DispatcherPriority.Normal);
            handled = true;
            return IntPtr.Zero;
        }

        return IntPtr.Zero;
    }

    private void ToggleVisibilityFromHotkey()
    {
        if (Visibility == Visibility.Visible && IsActive)
        {
            Hide();
            return;
        }

        ShowAndActivate();
    }

    private void HistoryList_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBox lb)
            return;

        var set = new HashSet<ClipboardItem>(lb.SelectedItems.Cast<ClipboardItem>());
        var ordered = _viewModel.Items.Where(set.Contains).ToList();
        _viewModel.SyncSelection(ordered);
    }

    private void HistoryList_OnMouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_viewModel.ShowDetailCommand.CanExecute(null))
            _viewModel.ShowDetailCommand.Execute(null);
    }

    private void HistoryList_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        _draggedItemContainer = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
    }

    private void HistoryList_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && !_isDragging && _draggedItemContainer != null)
        {
            System.Windows.Point position = e.GetPosition(null);
            if (Math.Abs(position.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(position.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                if (sender is ListBox listBox)
                {
                    try
                    {
                        _isDragging = true;
                        var item = (ClipboardItem)listBox.ItemContainerGenerator.ItemFromContainer(_draggedItemContainer);
                        if (item != null)
                        {
                            DataObject dragData = new DataObject("ClipboardItem", item);
                            DragDrop.DoDragDrop(_draggedItemContainer, dragData, DragDropEffects.Move);
                        }
                    }
                    finally
                    {
                        _isDragging = false;
                        _draggedItemContainer = null;
                    }
                }
            }
        }
    }

    private void HistoryList_OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("ClipboardItem"))
        {
            e.Effects = DragDropEffects.None;
        }
        else
        {
            e.Effects = DragDropEffects.Move;
        }
        e.Handled = true;
    }

    private async void HistoryList_OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("ClipboardItem"))
        {
            if (e.Data.GetData("ClipboardItem") is ClipboardItem droppedItem && sender is ListBox listBox)
            {
                // We need to find the target item more robustly.
                // e.OriginalSource might be the TextBlock or something else.
                var targetItem = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
                
                // If we dropped on the ListBox itself (blank area), we don't know the target.
                // But usually the user drops it ON an item.
                if (targetItem != null)
                {
                    var target = (ClipboardItem)listBox.ItemContainerGenerator.ItemFromContainer(targetItem);
                    if (target != null && droppedItem != target)
                    {
                        int oldIndex = _viewModel.Items.IndexOf(droppedItem);
                        int newIndex = _viewModel.Items.IndexOf(target);

                        if (oldIndex != -1 && newIndex != -1)
                        {
                            await _viewModel.MoveItemAsync(oldIndex, newIndex);
                        }
                    }
                }
            }
        }
        _isDragging = false;
        _draggedItemContainer = null;
    }

    private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        do
        {
            if (current is T ancestor)
            {
                return ancestor;
            }
            current = VisualTreeHelper.GetParent(current);
        }
        while (current != null);
        return null;
    }
}

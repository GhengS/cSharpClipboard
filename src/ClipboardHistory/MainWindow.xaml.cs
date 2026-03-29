using System.Drawing;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
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
        if (_viewModel.CopySelectionCommand.CanExecute(null))
            _viewModel.CopySelectionCommand.Execute(null);
    }
}

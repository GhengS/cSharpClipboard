using System.Windows;
using ClipboardHistory.Services;

namespace ClipboardHistory;

public partial class App : System.Windows.Application
{
    private HistoryRepository? _repository;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _repository = new HistoryRepository();
        try
        {
            await _repository.InitializeAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法初始化数据库：{ex.Message}", "剪贴板历史", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        var settings = new SettingsService();
        var clipboardMonitor = new ClipboardMonitorService(settings);
        var hotkeyService = new HotkeyService(settings);

        var window = new MainWindow(_repository, settings, clipboardMonitor, hotkeyService);
        MainWindow = window;
        window.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_repository != null)
                await _repository.DisposeAsync().ConfigureAwait(true);
        }
        catch
        {
            // best effort
        }

        base.OnExit(e);
    }
}

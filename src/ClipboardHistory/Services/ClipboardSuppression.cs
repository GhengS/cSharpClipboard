using System.Windows;
using System.Windows.Threading;

namespace ClipboardHistory.Services;

/// <summary>
/// Prevents the clipboard monitor from treating our own <see cref="System.Windows.Clipboard"/> writes as new history.
/// Increment before SetData; decrement on the dispatcher after the clipboard owner change has been processed (ApplicationIdle).
/// </summary>
public static class ClipboardSuppression
{
    private static int _depth;

    public static bool IsActive => _depth > 0;

    public static void ExecuteSuppressed(Action action)
    {
        Interlocked.Increment(ref _depth);
        try
        {
            action();
        }
        finally
        {
            var d = Dispatcher.CurrentDispatcher;
            _ = d.BeginInvoke(static () => Interlocked.Decrement(ref _depth), DispatcherPriority.ApplicationIdle);
        }
    }

    public static async Task ExecuteSuppressedAsync(Func<Task> action)
    {
        Interlocked.Increment(ref _depth);
        try
        {
            await action().ConfigureAwait(true);
        }
        finally
        {
            var d = Dispatcher.CurrentDispatcher;
            _ = d.BeginInvoke(static () => Interlocked.Decrement(ref _depth), DispatcherPriority.ApplicationIdle);
        }
    }
}

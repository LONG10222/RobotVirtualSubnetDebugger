#if ROBOTNET_MACOS
using Avalonia.Threading;
#else
using System.Windows;
#endif

namespace RobotNet.Windows.Wpf.Utils;

internal static class UiThread
{
    public static void Post(Action action)
    {
#if ROBOTNET_MACOS
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
#else
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.BeginInvoke(action);
#endif
    }

    public static void Invoke(Action action)
    {
#if ROBOTNET_MACOS
        Post(action);
#else
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
#endif
    }
}

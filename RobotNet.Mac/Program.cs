using Avalonia;
using Avalonia.Native;
using RobotNet.Windows.Wpf.Utils;

namespace RobotNet.Mac;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (!OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("RobotNet.Mac requires macOS.");

        // The UI writes one configuration and rollback journal per user.
        FileStream instance;
        try
        {
            instance = File.Open(Path.Combine(AppPaths.AppDataDirectory, "app.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            Console.Error.WriteLine("RobotNet 已运行，或无法取得配置目录锁。");
            return 1;
        }

        using (instance)
            return AppBuilder.Configure<App>().UseAvaloniaNative().UseSkia().StartWithClassicDesktopLifetime(args);
    }
}

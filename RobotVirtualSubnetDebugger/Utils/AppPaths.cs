using System.IO;

namespace RobotNet.Windows.Wpf.Utils;

public static class AppPaths
{
    public static string AppDataDirectory
    {
        get
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "RobotNet.Windows.Wpf");
            Directory.CreateDirectory(directory);
            return directory;
        }
    }

    public static string LogsDirectory => EnsureSubdirectory("logs");

    public static string CrashReportsDirectory => EnsureSubdirectory("crashes");

    public static string UpdatesDirectory => EnsureSubdirectory("updates");

    public static string ReleaseDirectory => EnsureSubdirectory("release");

    private static string EnsureSubdirectory(string name)
    {
        var directory = Path.Combine(AppDataDirectory, name);
        Directory.CreateDirectory(directory);
        return directory;
    }
}

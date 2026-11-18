using System.IO;
using RobotNet.Windows.Wpf.Utils;

namespace RobotNet.Windows.Wpf.Services.Platform;

internal static class NetworkOperationLock
{
    internal static FileStream Acquire(string? directory = null)
    {
        directory ??= Path.Combine(AppPaths.AppDataDirectory, "operations");
        Directory.CreateDirectory(directory);
        // Keep the file after release: deleting it can let another process lock a new inode.
        return new FileStream(Path.Combine(directory, "network-operation.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
}

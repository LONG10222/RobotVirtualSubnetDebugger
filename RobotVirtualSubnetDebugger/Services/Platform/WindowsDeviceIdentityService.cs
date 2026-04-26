using System.IO;

namespace RobotNet.Windows.Wpf.Services.Platform;

public sealed class WindowsDeviceIdentityService : IDeviceIdentityService
{
    private readonly string _identityFilePath;

    public WindowsDeviceIdentityService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appDirectory = Path.Combine(appData, "RobotNet.Windows.Wpf");
        _identityFilePath = Path.Combine(appDirectory, "device.id");
    }

    public string GetOrCreateDeviceId()
    {
        var directory = Path.GetDirectoryName(_identityFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(_identityFilePath))
        {
            var existing = File.ReadAllText(_identityFilePath).Trim();
            if (!string.IsNullOrWhiteSpace(existing))
            {
                return existing;
            }
        }

        var deviceId = Guid.NewGuid().ToString("N");
        File.WriteAllText(_identityFilePath, deviceId);
        return deviceId;
    }

    public string GetComputerName()
    {
        return Environment.MachineName;
    }
}

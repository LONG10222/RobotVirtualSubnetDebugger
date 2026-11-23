using RobotNet.Windows.Wpf.Models;

namespace RobotNet.Windows.Wpf.Services.Updates;

public interface IUpdateInstaller
{
    Task PrepareAsync(UpdateCheckResult release, UpdateDownloadResult download);
    void ExitForUpdate();
}

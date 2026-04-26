using RobotNet.Windows.Wpf.Models;

namespace RobotNet.Windows.Wpf.Services.Updates;

public interface IUpdateService
{
    string CurrentVersion { get; }

    string UpdatesDirectory { get; }

    Task<UpdateCheckResult> CheckForUpdatesAsync(AppConfig config, CancellationToken cancellationToken = default);

    Task<UpdateDownloadResult> DownloadUpdateAsync(UpdateCheckResult update, CancellationToken cancellationToken = default);
}

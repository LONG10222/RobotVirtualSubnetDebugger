using RobotNet.Windows.Wpf.Models;

namespace RobotNet.Windows.Wpf.Services.Platform;

public interface IAdminElevationService
{
    bool IsReadOnlyMode { get; }

    PlatformOperationResult CheckAdminPrivilege();

    AdminElevationResult EnsureRunningAsAdmin(string[] args);
}

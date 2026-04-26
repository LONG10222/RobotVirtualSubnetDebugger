using RobotNet.Windows.Wpf.Models;

namespace RobotNet.Windows.Wpf.Services.Platform;

public interface IPrivilegeService
{
    bool IsRunningAsAdministrator();

    PlatformOperationResult CheckAdministrator();
}

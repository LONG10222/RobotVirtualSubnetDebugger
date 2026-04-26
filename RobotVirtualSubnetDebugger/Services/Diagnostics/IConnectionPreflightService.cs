using RobotNet.Windows.Wpf.Models;

namespace RobotNet.Windows.Wpf.Services.Diagnostics;

public interface IConnectionPreflightService
{
    ConnectionPreflightResult EnsurePortsReady(AppConfig config);
}

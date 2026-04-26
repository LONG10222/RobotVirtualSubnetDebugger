using RobotNet.Windows.Wpf.Models;

namespace RobotNet.Windows.Wpf.Services.Platform;

public interface IPortAvailabilityService
{
    PortCheckResult CheckPort(int port, PortProtocol protocol);

    PortCheckResult FindAvailablePort(int preferredPort, PortProtocol protocol, int maxAttempts = 100);
}

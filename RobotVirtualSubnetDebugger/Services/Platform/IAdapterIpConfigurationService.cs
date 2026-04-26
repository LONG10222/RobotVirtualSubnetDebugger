using RobotNet.Windows.Wpf.Models;

namespace RobotNet.Windows.Wpf.Services.Platform;

public interface IAdapterIpConfigurationService
{
    VirtualSubnetOperationStep? CreateEnsureAdapterIpStep(string interfaceAlias, string ipAddress, string subnetMask);
}

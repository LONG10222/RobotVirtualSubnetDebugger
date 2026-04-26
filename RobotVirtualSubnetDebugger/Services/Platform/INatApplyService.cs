using RobotNet.Windows.Wpf.Models;

namespace RobotNet.Windows.Wpf.Services.Platform;

public interface INatApplyService
{
    VirtualSubnetOperationStep CreateNatStep(string natName, string lanSubnetCidr);
}

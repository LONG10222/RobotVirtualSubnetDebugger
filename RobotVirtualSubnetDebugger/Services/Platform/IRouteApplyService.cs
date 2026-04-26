using RobotNet.Windows.Wpf.Models;

namespace RobotNet.Windows.Wpf.Services.Platform;

public interface IRouteApplyService
{
    VirtualSubnetOperationStep CreatePreciseRouteStep(VirtualSubnetPlan plan, AppConfig config, string interfaceAlias);
}

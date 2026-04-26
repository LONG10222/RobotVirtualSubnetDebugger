using RobotNet.Windows.Wpf.Models;

namespace RobotNet.Windows.Wpf.Services.Platform;

public interface IVirtualSubnetService
{
    VirtualSubnetPlan BuildPlan(AppConfig config);

    VirtualSubnetScriptExportResult ExportScripts(VirtualSubnetPlan plan);

    Task<PlatformOperationResult> CheckAsync(AppConfig config);
}

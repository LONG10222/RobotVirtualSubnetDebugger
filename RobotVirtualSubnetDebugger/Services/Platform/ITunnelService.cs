using RobotNet.Windows.Wpf.Models;

namespace RobotNet.Windows.Wpf.Services.Platform;

public interface ITunnelService
{
    Task<PlatformOperationResult> CheckAsync(AppConfig config);

    Task<PlatformOperationResult> StartAsync(AppConfig config);

    Task<PlatformOperationResult> StopAsync();
}

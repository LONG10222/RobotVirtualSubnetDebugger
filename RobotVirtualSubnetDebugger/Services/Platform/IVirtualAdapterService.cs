using RobotNet.Windows.Wpf.Models;

namespace RobotNet.Windows.Wpf.Services.Platform;

public interface IVirtualAdapterService
{
    Task<PlatformOperationResult> CheckAsync(AppConfig config);

    Task<PlatformOperationResult> EnsureAsync(AppConfig config);
}

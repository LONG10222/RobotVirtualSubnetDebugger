using RobotNet.Windows.Wpf.Models;

namespace RobotNet.Windows.Wpf.Services.Platform;

public interface IRouteService
{
    Task<PlatformOperationResult> CheckAsync(AppConfig config);

    Task<PlatformOperationResult> ApplyAsync(AppConfig config);
}

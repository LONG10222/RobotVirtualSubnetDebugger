using RobotNet.Windows.Wpf.Models;

namespace RobotNet.Windows.Wpf.Services.Proxy;

public interface ITcpProxyService
{
    event EventHandler<TcpProxySessionInfo>? StateChanged;

    TcpProxySessionInfo Current { get; }

    Task<PlatformOperationResult> CheckAsync(AppConfig config);

    Task<TcpProxySessionInfo> StartAsync(AppConfig config);

    Task<TcpProxySessionInfo> StopAsync();
}

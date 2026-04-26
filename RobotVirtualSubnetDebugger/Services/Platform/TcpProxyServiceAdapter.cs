using RobotNet.Windows.Wpf.Models;
using RobotNet.Windows.Wpf.Services.Proxy;

namespace RobotNet.Windows.Wpf.Services.Platform;

public sealed class TcpProxyServiceAdapter : ITcpProxyServiceAdapter
{
    private readonly ITcpProxyService _tcpProxyService;

    public TcpProxyServiceAdapter(ITcpProxyService tcpProxyService)
    {
        _tcpProxyService = tcpProxyService;
    }

    public async Task StartGatewayAsync(AppConfig config, List<string> logs)
    {
        if (config.Role != DeviceRole.GatewayAgent)
        {
            config.Role = DeviceRole.GatewayAgent;
        }

        logs.Add($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} 启动网关端 TCP 代理服务。");
        await _tcpProxyService.StartAsync(config).ConfigureAwait(false);
    }

    public async Task StartClientAsync(AppConfig config, List<string> logs)
    {
        if (config.Role != DeviceRole.DebugClient)
        {
            config.Role = DeviceRole.DebugClient;
        }

        logs.Add($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} 启动调试端本地 TCP 代理监听。");
        await _tcpProxyService.StartAsync(config).ConfigureAwait(false);
    }
}

using RobotNet.Windows.Wpf.Models;
using RobotNet.Windows.Wpf.Services.Logging;
using RobotNet.Windows.Wpf.Services.Platform;

namespace RobotNet.Windows.Wpf.Services.Diagnostics;

public sealed class ConnectionPreflightService : IConnectionPreflightService
{
    private const int DefaultDiscoveryPort = 47831;
    private const int DefaultLocalListenPort = 30003;
    private const int DefaultProxyControlPort = 47832;

    private readonly IPortAvailabilityService _portAvailabilityService;
    private readonly ILogService _logService;

    public ConnectionPreflightService(
        IPortAvailabilityService portAvailabilityService,
        ILogService logService)
    {
        _portAvailabilityService = portAvailabilityService;
        _logService = logService;
    }

    public ConnectionPreflightResult EnsurePortsReady(AppConfig config)
    {
        var items = new List<DiagnosticResult>();
        var result = new ConnectionPreflightResult();

        EnsurePort(
            items,
            "UDP 发现端口",
            config.DiscoveryPort,
            DefaultDiscoveryPort,
            PortProtocol.Udp,
            allowCurrentProcessOwner: true,
            port => config.DiscoveryPort = port,
            result);

        if (config.Role == DeviceRole.DebugClient)
        {
            EnsurePort(
                items,
                "本地监听端口",
                config.LocalListenPort,
                DefaultLocalListenPort,
                PortProtocol.Tcp,
                allowCurrentProcessOwner: true,
                port => config.LocalListenPort = port,
                result);
        }

        if (config.Role == DeviceRole.GatewayAgent)
        {
            EnsurePort(
                items,
                "代理控制端口",
                config.ProxyControlPort,
                DefaultProxyControlPort,
                PortProtocol.Tcp,
                allowCurrentProcessOwner: true,
                port => config.ProxyControlPort = port,
                result);
        }

        result.Items = items;
        result.Summary = result.CanContinue
            ? result.ConfigChanged
                ? "端口冲突已自动处理，配置已更新。"
                : "端口检查通过。"
            : "端口检查失败，无法继续一键连接。";

        return result;
    }

    private void EnsurePort(
        ICollection<DiagnosticResult> items,
        string name,
        int configuredPort,
        int defaultPort,
        PortProtocol protocol,
        bool allowCurrentProcessOwner,
        Action<int> updatePort,
        ConnectionPreflightResult result)
    {
        var port = configuredPort is > 0 and <= 65535 ? configuredPort : defaultPort;
        if (port != configuredPort)
        {
            updatePort(port);
            result.ConfigChanged = true;
        }

        var check = _portAvailabilityService.CheckPort(port, protocol);
        if (check.IsAvailable || (allowCurrentProcessOwner && check.IsOwnedByCurrentProcess))
        {
            items.Add(new DiagnosticResult
            {
                Name = name,
                Status = DiagnosticStatus.Success,
                Message = $"{protocol} {port} 可用。{check.OwnerText}",
                Suggestion = "无需处理。"
            });
            return;
        }

        var available = _portAvailabilityService.FindAvailablePort(port + 1, protocol);
        if (!available.IsAvailable)
        {
            result.CanContinue = false;
            items.Add(new DiagnosticResult
            {
                Name = name,
                Status = DiagnosticStatus.Error,
                Message = $"{protocol} {port} 被占用：{check.OwnerText}，并且未找到可用替代端口。",
                Suggestion = "请关闭占用端口的程序，或手动修改配置端口。"
            });
            return;
        }

        updatePort(available.Port);
        result.ConfigChanged = true;

        var message = $"{protocol} {port} 被占用：{check.OwnerText}，已自动切换到 {available.Port}。";
        items.Add(new DiagnosticResult
        {
            Name = name,
            Status = DiagnosticStatus.Warning,
            Message = message,
            Suggestion = "一键连接会继续使用自动选择的新端口。"
        });
        _logService.Warning(message);
    }
}

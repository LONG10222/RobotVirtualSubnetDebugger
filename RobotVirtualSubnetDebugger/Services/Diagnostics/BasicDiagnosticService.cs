using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using RobotNet.Windows.Wpf.Models;
using RobotNet.Windows.Wpf.Services.Logging;
using RobotNet.Windows.Wpf.Services.Platform;
using RobotNet.Windows.Wpf.Services.Proxy;

namespace RobotNet.Windows.Wpf.Services.Diagnostics;

public sealed class BasicDiagnosticService : IDiagnosticService
{
    private readonly INetworkAdapterService _networkAdapterService;
    private readonly IPrivilegeService _privilegeService;
    private readonly IVirtualAdapterService _virtualAdapterService;
    private readonly IRouteService _routeService;
    private readonly ITunnelService _tunnelService;
    private readonly ITcpProxyService _tcpProxyService;
    private readonly IVirtualSubnetService _virtualSubnetService;
    private readonly IPortAvailabilityService _portAvailabilityService;
    private readonly ILogService _logService;

    public BasicDiagnosticService(
        INetworkAdapterService networkAdapterService,
        IPrivilegeService privilegeService,
        IVirtualAdapterService virtualAdapterService,
        IRouteService routeService,
        ITunnelService tunnelService,
        ITcpProxyService tcpProxyService,
        IVirtualSubnetService virtualSubnetService,
        IPortAvailabilityService portAvailabilityService,
        ILogService logService)
    {
        _networkAdapterService = networkAdapterService;
        _privilegeService = privilegeService;
        _virtualAdapterService = virtualAdapterService;
        _routeService = routeService;
        _tunnelService = tunnelService;
        _tcpProxyService = tcpProxyService;
        _virtualSubnetService = virtualSubnetService;
        _portAvailabilityService = portAvailabilityService;
        _logService = logService;
    }

    public async Task<IReadOnlyList<DiagnosticResult>> RunAllAsync(AppConfig config)
    {
        _logService.Info("开始运行角色化诊断。");

        var results = new List<DiagnosticResult>();
        var adapters = _networkAdapterService.GetAdapters();
        var lanCandidates = _networkAdapterService.FindLanCandidates();
        var targetCandidates = _networkAdapterService.FindTargetNetworkCandidates(config.TargetDeviceIp);

        AddCommonDiagnostics(results, config, adapters, lanCandidates);
        AddNetworkCoexistenceDiagnostics(results, config, adapters);
        AddPortDiagnostics(results, config);

        switch (config.Role)
        {
            case DeviceRole.DebugClient:
                await AddDebugClientDiagnosticsAsync(results, config, lanCandidates);
                break;
            case DeviceRole.GatewayAgent:
                await AddGatewayAgentDiagnosticsAsync(results, config, targetCandidates);
                break;
            default:
                results.Add(new DiagnosticResult
                {
                    Name = "角色检查",
                    Status = DiagnosticStatus.Warning,
                    Message = "当前角色仍为 Unknown。",
                    Suggestion = "请先在配置页选择“调试端”或“网关端”，诊断会按角色给出更准确的结果。"
                });
                break;
        }

        await AddPlatformCapabilityDiagnosticsAsync(results, config);

        _logService.Info("诊断运行完成。");
        return results;
    }

    public async Task<bool> PingAsync(string ip)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(ip, 1000);
            return reply.Status == IPStatus.Success;
        }
        catch (Exception ex)
        {
            _logService.Warning($"Ping {ip} 失败：{ex.Message}");
            return false;
        }
    }

    public async Task<bool> TestTcpPortAsync(string ip, int port)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(ip, port);
            var completedTask = await Task.WhenAny(connectTask, Task.Delay(1000));
            return completedTask == connectTask && client.Connected;
        }
        catch (Exception ex)
        {
            _logService.Warning($"TCP {ip}:{port} 测试失败：{ex.Message}");
            return false;
        }
    }

    private static void AddCommonDiagnostics(
        ICollection<DiagnosticResult> results,
        AppConfig config,
        IReadOnlyList<NetworkAdapterInfo> adapters,
        IReadOnlyList<NetworkAdapterInfo> lanCandidates)
    {
        results.Add(new DiagnosticResult
        {
            Name = "当前角色",
            Status = config.Role == DeviceRole.Unknown ? DiagnosticStatus.Warning : DiagnosticStatus.Success,
            Message = $"当前配置角色：{config.Role}。",
            Suggestion = config.Role == DeviceRole.Unknown
                ? "请在配置页选择本机是调试端还是网关端。"
                : "角色已配置。"
        });

        results.Add(new DiagnosticResult
        {
            Name = "本机网卡检查",
            Status = adapters.Count > 0 ? DiagnosticStatus.Success : DiagnosticStatus.Error,
            Message = $"检测到 {adapters.Count} 个网卡，{lanCandidates.Count} 个局域网候选。",
            Suggestion = lanCandidates.Count > 0
                ? "局域网网卡识别正常。"
                : "请确认 WiFi 或以太网已连接，并且网卡有 IPv4 地址。"
        });

        AddIpFormatResult(results, "目标设备 IP 格式", config.TargetDeviceIp, "请在配置页填写正确的目标设备 IPv4 地址。");
        AddIpFormatResult(results, "虚拟 IP 格式", config.VirtualIp, "请填写调试端计划使用的虚拟 IPv4 地址。");
        AddIpFormatResult(results, "虚拟掩码格式", config.VirtualSubnetMask, "请填写有效的 IPv4 子网掩码，例如 255.255.255.0。");

        results.Add(new DiagnosticResult
        {
            Name = "目标设备端口",
            Status = config.TargetDevicePort is > 0 and <= 65535 ? DiagnosticStatus.Success : DiagnosticStatus.Error,
            Message = $"当前目标设备端口：{config.TargetDevicePort}。",
            Suggestion = config.TargetDevicePort is > 0 and <= 65535
                ? "端口格式正常。"
                : "端口必须在 1 到 65535 之间。"
        });

        results.Add(new DiagnosticResult
        {
            Name = "UDP 发现端口",
            Status = config.DiscoveryPort is > 0 and <= 65535 ? DiagnosticStatus.Success : DiagnosticStatus.Error,
            Message = $"当前 UDP 发现端口：{config.DiscoveryPort}。",
            Suggestion = config.DiscoveryPort is > 0 and <= 65535
                ? "发现端口格式正常。"
                : "发现端口必须在 1 到 65535 之间。"
        });

        results.Add(new DiagnosticResult
        {
            Name = "共享密钥",
            Status = string.IsNullOrWhiteSpace(config.SharedKey)
                ? DiagnosticStatus.Error
                : config.SharedKey.Trim().Length >= 8 ? DiagnosticStatus.Success : DiagnosticStatus.Warning,
            Message = string.IsNullOrWhiteSpace(config.SharedKey)
                ? "SharedKey 为空，第四阶段 TCP 代理会拒绝启动。"
                : $"SharedKey 已配置，长度 {config.SharedKey.Trim().Length}。",
            Suggestion = string.IsNullOrWhiteSpace(config.SharedKey)
                ? "请在配置页生成或填写共享密钥，并在两台电脑上保持一致。"
                : config.SharedKey.Trim().Length >= 8
                    ? "共享密钥满足第四阶段认证要求。"
                    : "建议使用至少 8 个字符；推荐直接点击“生成密钥”。"
        });

        var stabilityOk =
            config.ProxyHeartbeatIntervalSeconds is > 0 and <= 60 &&
            config.ProxyIdleTimeoutSeconds > config.ProxyHeartbeatIntervalSeconds &&
            config.ProxyIdleTimeoutSeconds <= 300 &&
            config.ProxyReconnectAttempts is >= 0 and <= 10;

        results.Add(new DiagnosticResult
        {
            Name = "代理稳定性参数",
            Status = stabilityOk ? DiagnosticStatus.Success : DiagnosticStatus.Warning,
            Message = $"心跳 {config.ProxyHeartbeatIntervalSeconds}s，空闲超时 {config.ProxyIdleTimeoutSeconds}s，重试 {config.ProxyReconnectAttempts} 次。",
            Suggestion = stabilityOk
                ? "心跳、超时和重试参数正常。"
                : "心跳应为 1-60 秒，空闲超时应大于心跳且不超过 300 秒，重试次数应为 0-10。"
        });
    }

    private void AddNetworkCoexistenceDiagnostics(
        ICollection<DiagnosticResult> results,
        AppConfig config,
        IReadOnlyList<NetworkAdapterInfo> adapters)
    {
        var upAdapters = adapters
            .Where(adapter => string.Equals(adapter.Status, OperationalStatus.Up.ToString(), StringComparison.OrdinalIgnoreCase) &&
                              !string.IsNullOrWhiteSpace(adapter.IPv4Address))
            .ToList();
        var internetAdapters = upAdapters
            .Where(adapter => !string.IsNullOrWhiteSpace(adapter.Gateway))
            .ToList();
        var wifiAdapters = upAdapters
            .Where(IsWifiAdapter)
            .ToList();
        var vpnAdapters = upAdapters
            .Where(IsVpnOrTunnelAdapter)
            .ToList();

        results.Add(new DiagnosticResult
        {
            Name = "网络共存策略",
            Status = DiagnosticStatus.Success,
            Message = "当前阶段只打开用户态 TCP/UDP 端口，不修改默认网关、DNS、路由、NAT、桥接或防火墙。",
            Suggestion = "WiFi、VPN 和正常上网可以同时使用。第五阶段如加入真实虚拟网段，只允许添加目标网段精确路由，禁止接管 0.0.0.0/0 默认路由。"
        });

        results.Add(new DiagnosticResult
        {
            Name = "默认上网链路",
            Status = internetAdapters.Count > 0 ? DiagnosticStatus.Success : DiagnosticStatus.Warning,
            Message = internetAdapters.Count > 0
                ? $"检测到 {internetAdapters.Count} 个带默认网关的上网候选：{FormatAdapterNames(internetAdapters)}。"
                : "未检测到带 IPv4 默认网关的上网候选网卡。",
            Suggestion = internetAdapters.Count > 0
                ? "后续真实虚拟网段实现不得删除或覆盖这些网卡的默认网关。"
                : "如果当前电脑可以上网但这里为空，可能是 IPv6、VPN 接管或系统路由策略导致；第五阶段前需要单独核验。"
        });

        results.Add(new DiagnosticResult
        {
            Name = "WiFi 共存检查",
            Status = wifiAdapters.Count > 0 ? DiagnosticStatus.Success : DiagnosticStatus.Info,
            Message = wifiAdapters.Count > 0
                ? $"检测到 {wifiAdapters.Count} 个活动 WiFi 网卡：{FormatAdapterNames(wifiAdapters)}。"
                : "未检测到活动 WiFi 网卡，可能正在使用有线网络或 VPN。",
            Suggestion = wifiAdapters.Count > 0
                ? "当前 TCP 代理不会影响 WiFi 上网；后续路由/NAT 只能作用于目标设备网段。"
                : "如果需要两台电脑通过 WiFi 互相发现，请确认 WiFi 已连接并允许局域网访问。"
        });

        results.Add(new DiagnosticResult
        {
            Name = "VPN/Tunnel 共存检查",
            Status = DiagnosticStatus.Info,
            Message = vpnAdapters.Count > 0
                ? $"检测到 {vpnAdapters.Count} 个可能的 VPN/Tunnel/虚拟网卡：{FormatAdapterNames(vpnAdapters)}。"
                : "未检测到明显的 VPN/Tunnel/虚拟网卡。",
            Suggestion = vpnAdapters.Count > 0
                ? "当前阶段不会改 VPN 路由。第五阶段必须避免把目标设备路由错误下发到 VPN 网卡，也不能改 VPN DNS。"
                : "未启用 VPN 时也应保持分流设计，避免未来开启 VPN 后互相影响。"
        });

        AddSubnetOverlapDiagnostic(results, config, upAdapters);
    }

    private void AddSubnetOverlapDiagnostic(
        ICollection<DiagnosticResult> results,
        AppConfig config,
        IReadOnlyList<NetworkAdapterInfo> upAdapters)
    {
        if (!IPAddress.TryParse(config.TargetDeviceIp, out _))
        {
            results.Add(new DiagnosticResult
            {
                Name = "目标网段冲突检查",
                Status = DiagnosticStatus.Warning,
                Message = "目标设备 IP 无效，无法检查它是否与 WiFi/VPN/上网网段冲突。",
                Suggestion = "请先在配置页填写正确的目标设备 IPv4 地址。"
            });
            return;
        }

        var overlaps = upAdapters
            .Where(adapter => !string.IsNullOrWhiteSpace(adapter.SubnetMask) &&
                              _networkAdapterService.IsSameSubnet(adapter.IPv4Address, adapter.SubnetMask, config.TargetDeviceIp))
            .ToList();

        var nonTargetOverlaps = overlaps
            .Where(adapter => !adapter.IsTargetNetworkCandidate)
            .ToList();

        results.Add(new DiagnosticResult
        {
            Name = "目标网段冲突检查",
            Status = nonTargetOverlaps.Count == 0 ? DiagnosticStatus.Success : DiagnosticStatus.Warning,
            Message = nonTargetOverlaps.Count == 0
                ? $"未发现目标设备 IP {config.TargetDeviceIp} 与当前 WiFi/VPN/上网网段明显冲突。"
                : $"目标设备 IP {config.TargetDeviceIp} 与这些活动网卡处于同一网段：{FormatAdapterNames(nonTargetOverlaps)}。",
            Suggestion = nonTargetOverlaps.Count == 0
                ? "后续真实虚拟网段可优先使用目标网段精确路由，不影响默认上网。"
                : "如果目标设备网段与 WiFi 或 VPN 网段重叠，真实虚拟网卡/路由阶段可能影响上网或 VPN；建议更换目标设备网段，或使用 TCP 代理模式避免系统路由冲突。"
        });
    }

    private async Task AddDebugClientDiagnosticsAsync(
        ICollection<DiagnosticResult> results,
        AppConfig config,
        IReadOnlyList<NetworkAdapterInfo> lanCandidates)
    {
        var gatewayConfigured = IPAddress.TryParse(config.GatewayLanIp, out _);
        results.Add(new DiagnosticResult
        {
            Name = "调试端网关配置",
            Status = gatewayConfigured ? DiagnosticStatus.Success : DiagnosticStatus.Warning,
            Message = gatewayConfigured
                ? $"网关端 LAN IP：{config.GatewayLanIp}。"
                : "尚未配置网关端 LAN IP。",
            Suggestion = gatewayConfigured
                ? "可以测试调试端到网关端的局域网连通性。"
                : "请在配置页填写电脑 A 的 WiFi/LAN IP，例如 192.168.31.20。"
        });

        if (gatewayConfigured)
        {
            var gatewayReachable = await PingAsync(config.GatewayLanIp);
            results.Add(new DiagnosticResult
            {
                Name = "调试端到网关 Ping",
                Status = gatewayReachable ? DiagnosticStatus.Success : DiagnosticStatus.Warning,
                Message = gatewayReachable
                    ? $"Ping 网关端 {config.GatewayLanIp} 成功。"
                    : $"Ping 网关端 {config.GatewayLanIp} 未收到响应。",
                Suggestion = gatewayReachable
                    ? "两台电脑处于可通信的局域网。"
                    : "请确认两台电脑在同一 WiFi/LAN，或检查 Windows 防火墙 ICMP 设置。"
            });
        }

        var virtualIpLooksRight =
            IPAddress.TryParse(config.VirtualIp, out _) &&
            IPAddress.TryParse(config.TargetDeviceIp, out _) &&
            IPAddress.TryParse(config.VirtualSubnetMask, out _) &&
            _networkAdapterService.IsSameSubnet(config.VirtualIp, config.VirtualSubnetMask, config.TargetDeviceIp);

        results.Add(new DiagnosticResult
        {
            Name = "调试端虚拟 IP 规划",
            Status = virtualIpLooksRight ? DiagnosticStatus.Success : DiagnosticStatus.Warning,
            Message = virtualIpLooksRight
                ? $"虚拟 IP {config.VirtualIp}/{config.VirtualSubnetMask} 与目标设备 {config.TargetDeviceIp} 处于同一网段。"
                : $"虚拟 IP {config.VirtualIp}/{config.VirtualSubnetMask} 与目标设备 {config.TargetDeviceIp} 不在同一网段或格式无效。",
            Suggestion = virtualIpLooksRight
                ? "这个规划符合后续虚拟网段目标。"
                : "调试端虚拟 IP 应与目标设备 IP 同网段，例如 192.168.1.101/24。"
        });

        results.Add(new DiagnosticResult
        {
            Name = "调试端局域网网卡",
            Status = lanCandidates.Count > 0 ? DiagnosticStatus.Success : DiagnosticStatus.Error,
            Message = lanCandidates.Count > 0
                ? $"检测到 {lanCandidates.Count} 个可用于连接网关端的局域网网卡。"
                : "未发现可用于连接网关端的局域网网卡。",
            Suggestion = lanCandidates.Count > 0
                ? "调试端 LAN 基础条件正常。"
                : "请先连接 WiFi 或以太网。"
        });
    }

    private async Task AddGatewayAgentDiagnosticsAsync(
        ICollection<DiagnosticResult> results,
        AppConfig config,
        IReadOnlyList<NetworkAdapterInfo> targetCandidates)
    {
        results.Add(new DiagnosticResult
        {
            Name = "网关端目标设备网卡",
            Status = targetCandidates.Count > 0 ? DiagnosticStatus.Success : DiagnosticStatus.Warning,
            Message = targetCandidates.Count > 0
                ? $"检测到 {targetCandidates.Count} 个可能连接目标设备网段的网卡。"
                : $"未发现与目标设备 IP {config.TargetDeviceIp} 同网段的非虚拟网卡。",
            Suggestion = targetCandidates.Count > 0
                ? "目标设备侧网卡识别正常。"
                : "电脑 A 应有一个网卡与目标设备处于同一网段，例如 192.168.1.100/24。"
        });

        if (!string.IsNullOrWhiteSpace(config.TargetDeviceAdapterIp))
        {
            var adapterIpMatches =
                IPAddress.TryParse(config.TargetDeviceAdapterIp, out _) &&
                IPAddress.TryParse(config.TargetDeviceIp, out _) &&
                targetCandidates.Any(adapter => adapter.IPv4Address == config.TargetDeviceAdapterIp ||
                                                _networkAdapterService.IsSameSubnet(adapter.IPv4Address, adapter.SubnetMask, config.TargetDeviceIp));

            results.Add(new DiagnosticResult
            {
                Name = "网关端目标设备网卡 IP",
                Status = adapterIpMatches ? DiagnosticStatus.Success : DiagnosticStatus.Warning,
                Message = $"配置的目标设备网卡 IP：{config.TargetDeviceAdapterIp}。",
                Suggestion = adapterIpMatches
                    ? "配置的目标设备网卡 IP 与检测结果匹配。"
                    : "请确认该 IP 是电脑 A 连接目标设备网段的网卡地址，例如 192.168.1.100。"
            });
        }

        if (IPAddress.TryParse(config.TargetDeviceIp, out _))
        {
            var pingOk = await PingAsync(config.TargetDeviceIp);
            results.Add(new DiagnosticResult
            {
                Name = "网关端 Ping 目标设备",
                Status = pingOk ? DiagnosticStatus.Success : DiagnosticStatus.Warning,
                Message = pingOk ? $"Ping {config.TargetDeviceIp} 成功。" : $"Ping {config.TargetDeviceIp} 未收到响应。",
                Suggestion = pingOk
                    ? "网关端到目标设备基础连通性正常。"
                    : "目标设备可能禁 Ping，或网关端目标设备网卡 IP/掩码不正确。"
            });

            var portOk = await TestTcpPortAsync(config.TargetDeviceIp, config.TargetDevicePort);
            results.Add(new DiagnosticResult
            {
                Name = "网关端目标设备 TCP",
                Status = portOk ? DiagnosticStatus.Success : DiagnosticStatus.Warning,
                Message = portOk
                    ? $"TCP {config.TargetDeviceIp}:{config.TargetDevicePort} 可连接。"
                    : $"TCP {config.TargetDeviceIp}:{config.TargetDevicePort} 暂不可连接。",
                Suggestion = portOk
                    ? "目标设备控制端口可达。"
                    : "请确认目标设备端口、网线、设备状态和防火墙设置。"
            });
        }
    }

    private async Task AddPlatformCapabilityDiagnosticsAsync(ICollection<DiagnosticResult> results, AppConfig config)
    {
        AddPlatformResult(results, _privilegeService.CheckAdministrator());
        AddPlatformResult(results, await _virtualAdapterService.CheckAsync(config));
        AddPlatformResult(results, await _routeService.CheckAsync(config));
        AddPlatformResult(results, await _tunnelService.CheckAsync(config));
        AddPlatformResult(results, await _tcpProxyService.CheckAsync(config));
        AddPlatformResult(results, await _virtualSubnetService.CheckAsync(config));

        results.Add(new DiagnosticResult
        {
            Name = "NAT/桥接能力检查",
            Status = DiagnosticStatus.Info,
            Message = "当前阶段未提供 NAT 或桥接服务实现，也不会修改系统转发配置。",
            Suggestion = "后续如需要 NAT/桥接，应新增独立平台服务接口并严格封装管理员权限操作。"
        });
    }

    private void AddPortDiagnostics(ICollection<DiagnosticResult> results, AppConfig config)
    {
        AddPortResult(results, "UDP 发现端口占用", _portAvailabilityService.CheckPort(config.DiscoveryPort, PortProtocol.Udp), allowCurrentProcessOwner: true);
        AddPortResult(results, "本地监听端口占用", _portAvailabilityService.CheckPort(config.LocalListenPort, PortProtocol.Tcp), allowCurrentProcessOwner: true);
        AddPortResult(results, "代理控制端口占用", _portAvailabilityService.CheckPort(config.ProxyControlPort, PortProtocol.Tcp), allowCurrentProcessOwner: true);
    }

    private static void AddPortResult(
        ICollection<DiagnosticResult> results,
        string name,
        PortCheckResult check,
        bool allowCurrentProcessOwner)
    {
        var canUse = check.IsAvailable || (allowCurrentProcessOwner && check.IsOwnedByCurrentProcess);
        results.Add(new DiagnosticResult
        {
            Name = name,
            Status = canUse ? DiagnosticStatus.Success : DiagnosticStatus.Warning,
            Message = canUse
                ? $"{check.Protocol} {check.Port} 可用。{check.OwnerText}"
                : $"{check.Protocol} {check.Port} 被占用：{check.OwnerText}",
            Suggestion = canUse
                ? "一键连接可以继续。"
                : "一键连接会尝试自动切换到下一个可用端口；如果仍失败，请关闭占用端口的程序。"
        });
    }

    private static void AddPlatformResult(ICollection<DiagnosticResult> results, PlatformOperationResult result)
    {
        results.Add(new DiagnosticResult
        {
            Name = result.Name,
            Status = result.Status,
            Message = result.Message,
            Suggestion = result.RequiresAdministrator
                ? $"{result.Suggestion} 该能力后续需要管理员权限。"
                : result.Suggestion
        });
    }

    private static bool IsWifiAdapter(NetworkAdapterInfo adapter)
    {
        return adapter.Type.Contains("Wireless80211", StringComparison.OrdinalIgnoreCase) ||
               adapter.Name.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase) ||
               adapter.Description.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase) ||
               adapter.Description.Contains("Wireless", StringComparison.OrdinalIgnoreCase) ||
               adapter.Description.Contains("WLAN", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVpnOrTunnelAdapter(NetworkAdapterInfo adapter)
    {
        var text = $"{adapter.Name} {adapter.Description} {adapter.Type}".ToLowerInvariant();
        string[] markers =
        [
            "vpn",
            "tunnel",
            "wireguard",
            "tailscale",
            "zerotier",
            "openvpn",
            "tap",
            "tun",
            "anyconnect",
            "fortinet",
            "forticlient",
            "clash",
            "sing-box",
            "v2ray",
            "wintun"
        ];

        return adapter.IsVirtual || markers.Any(marker => text.Contains(marker, StringComparison.Ordinal));
    }

    private static string FormatAdapterNames(IReadOnlyList<NetworkAdapterInfo> adapters)
    {
        return string.Join("、", adapters
            .Take(4)
            .Select(adapter => string.IsNullOrWhiteSpace(adapter.IPv4Address)
                ? adapter.Name
                : $"{adapter.Name}({adapter.IPv4Address})")) +
               (adapters.Count > 4 ? " 等" : string.Empty);
    }

    private static void AddIpFormatResult(
        ICollection<DiagnosticResult> results,
        string name,
        string value,
        string invalidSuggestion)
    {
        var valid = IPAddress.TryParse(value, out _);
        results.Add(new DiagnosticResult
        {
            Name = name,
            Status = valid ? DiagnosticStatus.Success : DiagnosticStatus.Error,
            Message = valid ? $"{value} 是有效 IPv4 地址。" : $"{value} 不是有效 IPv4 地址。",
            Suggestion = valid ? "格式正常。" : invalidSuggestion
        });
    }
}

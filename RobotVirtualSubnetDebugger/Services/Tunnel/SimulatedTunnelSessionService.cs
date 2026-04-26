using System.Net;
using RobotNet.Windows.Wpf.Models;
using RobotNet.Windows.Wpf.Services.Logging;

namespace RobotNet.Windows.Wpf.Services.Tunnel;

public sealed class SimulatedTunnelSessionService : ITunnelSessionService
{
    private readonly ILogService _logService;
    private readonly List<HandshakeStepInfo> _steps = [];
    private TunnelSessionInfo _current = new();
    private bool _isRunning;

    public SimulatedTunnelSessionService(ILogService logService)
    {
        _logService = logService;
    }

    public event EventHandler<TunnelSessionInfo>? SessionChanged;

    public event EventHandler<HandshakeStepInfo>? StepAdded;

    public TunnelSessionInfo Current => _current;

    public IReadOnlyList<HandshakeStepInfo> GetSteps()
    {
        return _steps.ToList();
    }

    public async Task<TunnelSessionInfo> StartAsync(AppConfig config, DeviceInfo? peer = null)
    {
        if (_isRunning)
        {
            return _current;
        }

        if (IsLockedByAnotherPeer(peer, out var lockMessage))
        {
            await AddStepAsync("连接锁定", DiagnosticStatus.Warning, lockMessage);
            SetStatus(_current.Status, lockMessage);
            _logService.Warning(lockMessage);
            return _current;
        }

        if (peer is { IsOnline: false })
        {
            var offlineMessage = $"设备 {peer.ComputerName} 已离线，不能建立连接。";
            await AddStepAsync("设备状态", DiagnosticStatus.Error, offlineMessage);
            SetStatus(SessionStatus.Failed, offlineMessage);
            _logService.Warning(offlineMessage);
            return _current;
        }

        if (peer is { IsBusy: true } &&
            !string.IsNullOrWhiteSpace(peer.ConnectedPeerDeviceId) &&
            peer.ConnectedPeerDeviceId != config.DeviceId)
        {
            var busyMessage = $"设备 {peer.ComputerName} 已连接其他设备，当前不能连接。";
            await AddStepAsync("对端锁定", DiagnosticStatus.Warning, busyMessage);
            SetStatus(SessionStatus.Failed, busyMessage);
            _logService.Warning(busyMessage);
            return _current;
        }

        _isRunning = true;
        _steps.Clear();

        try
        {
            SetCurrent(new TunnelSessionInfo
            {
                SessionId = Guid.NewGuid().ToString("N")[..8],
                Status = SessionStatus.Preparing,
                LocalRole = config.Role,
                LocalDeviceId = config.DeviceId,
                LocalDeviceName = config.DeviceName,
                PeerDeviceId = peer?.DeviceId ?? string.Empty,
                PeerDeviceName = peer?.ComputerName ?? string.Empty,
                PeerLanIp = peer?.LanIp ?? string.Empty,
                PeerRole = peer?.Role ?? DeviceRole.Unknown,
                GatewayLanIp = config.GatewayLanIp,
                TargetDeviceIp = config.TargetDeviceIp,
                VirtualIp = config.VirtualIp,
                StartedAt = DateTimeOffset.Now,
                LastUpdated = DateTimeOffset.Now,
                Message = "正在准备模拟连接会话。"
            });

            _logService.Info("开始模拟隧道会话。");

            await AddStepAsync("读取配置", DiagnosticStatus.Success, "已读取本地配置，未修改系统网络设置。");

            var validationError = ValidateConfig(config);
            if (!string.IsNullOrWhiteSpace(validationError))
            {
                await FailAsync("配置校验", validationError);
                return _current;
            }

            await AddStepAsync("角色校验", DiagnosticStatus.Success, $"当前角色：{GetRoleText(config.Role)}。");

            SetStatus(SessionStatus.DiscoveringPeer, "正在模拟发现对端。");
            if (config.Role == DeviceRole.DebugClient)
            {
                var gatewayText = peer is null ? config.GatewayLanIp : $"{peer.ComputerName} / {peer.LanIp}";
                await AddStepAsync("发现网关端", DiagnosticStatus.Success, $"已选择网关端：{gatewayText}。");
            }
            else
            {
                var clientText = peer is null ? "模拟调试端" : $"{peer.ComputerName} / {peer.LanIp}";
                await AddStepAsync("等待调试端", DiagnosticStatus.Success, $"网关端已进入模拟监听状态：{clientText}。");
            }

            SetStatus(SessionStatus.Handshaking, "正在执行模拟握手。");
            await AddStepAsync("交换会话信息", DiagnosticStatus.Success, $"目标设备 IP：{config.TargetDeviceIp}，虚拟 IP：{config.VirtualIp}，本地监听端口：{config.LocalListenPort}。");

            if (string.IsNullOrWhiteSpace(config.SharedKey))
            {
                await AddStepAsync("共享密钥校验", DiagnosticStatus.Warning, "SharedKey 为空。第二阶段模拟会话允许继续；第四阶段 TCP 代理会拒绝未配置密钥的真实转发。");
            }
            else
            {
                await AddStepAsync("共享密钥校验", DiagnosticStatus.Success, "SharedKey 已配置。模拟会话只做本地检查，TCP 代理会执行真实认证。");
            }

            await AddStepAsync("建立数据通道", DiagnosticStatus.Info, "第二阶段不创建真实隧道，不转发真实流量。");

            SetStatus(SessionStatus.Connected, "模拟连接已建立；当前不会创建虚拟网卡、路由、NAT 或桥接。");
            await AddStepAsync("会话状态", DiagnosticStatus.Success, "模拟会话已进入 Connected 状态。");
            _logService.Info("模拟隧道会话已建立。");

            return _current;
        }
        finally
        {
            _isRunning = false;
        }
    }

    public async Task<TunnelSessionInfo> StopAsync()
    {
        if (_current.Status == SessionStatus.Idle)
        {
            return _current;
        }

        SetStatus(SessionStatus.Disconnecting, "正在停止模拟连接。");
        await AddStepAsync("停止会话", DiagnosticStatus.Info, "正在释放模拟会话状态。");

        SetCurrent(new TunnelSessionInfo
        {
            Status = SessionStatus.Idle,
            LastUpdated = DateTimeOffset.Now,
            Message = "模拟连接已停止。"
        });

        await AddStepAsync("会话状态", DiagnosticStatus.Success, "已回到空闲状态。");
        _logService.Info("模拟隧道会话已停止。");
        return _current;
    }

    public TunnelSessionInfo ReleaseIfPeerOffline(IReadOnlyList<DeviceInfo> discoveredDevices)
    {
        if (_current.Status != SessionStatus.Connected || string.IsNullOrWhiteSpace(_current.PeerDeviceId))
        {
            return _current;
        }

        var peerOnline = discoveredDevices.Any(device =>
            device.DeviceId == _current.PeerDeviceId &&
            device.IsOnline);

        if (peerOnline)
        {
            return _current;
        }

        var peerName = string.IsNullOrWhiteSpace(_current.PeerDeviceName) ? _current.PeerDeviceId : _current.PeerDeviceName;
        var message = $"已连接设备 {peerName} 离线或搜索不到，会话锁定已自动释放。";
        var step = new HandshakeStepInfo
        {
            Name = "对端离线",
            Status = DiagnosticStatus.Warning,
            Message = message,
            Timestamp = DateTimeOffset.Now
        };

        _steps.Add(step);
        StepAdded?.Invoke(this, step);

        SetCurrent(new TunnelSessionInfo
        {
            Status = SessionStatus.Idle,
            LastUpdated = DateTimeOffset.Now,
            Message = message
        });

        _logService.Warning(message);
        return _current;
    }

    private static string ValidateConfig(AppConfig config)
    {
        if (config.Role == DeviceRole.Unknown)
        {
            return "请先在配置页选择调试端或网关端角色。";
        }

        if (!IsValidIp(config.TargetDeviceIp))
        {
            return "目标设备 IP 无效，请在配置页填写正确的 IPv4 地址。";
        }

        if (config.TargetDevicePort is < 1 or > 65535)
        {
            return "目标设备端口无效，应在 1 到 65535 之间。";
        }

        if (config.LocalListenPort is < 1 or > 65535)
        {
            return "本地监听端口无效，应在 1 到 65535 之间。";
        }

        if (!IsValidIp(config.VirtualIp))
        {
            return "虚拟 IP 无效，请在配置页填写正确的 IPv4 地址。";
        }

        if (!IsValidIp(config.VirtualSubnetMask))
        {
            return "虚拟子网掩码无效，请在配置页填写正确的 IPv4 掩码。";
        }

        if (config.Role == DeviceRole.DebugClient && !IsValidIp(config.GatewayLanIp))
        {
            return "调试端需要配置网关端 LAN IP。";
        }

        if (config.Role == DeviceRole.GatewayAgent && !IsValidIp(config.TargetDeviceAdapterIp))
        {
            return "网关端需要配置目标设备网卡 IP。";
        }

        return string.Empty;
    }

    private async Task FailAsync(string name, string message)
    {
        await AddStepAsync(name, DiagnosticStatus.Error, message);
        SetStatus(SessionStatus.Failed, message);
        _logService.Warning($"模拟隧道会话失败：{message}");
    }

    private async Task AddStepAsync(string name, DiagnosticStatus status, string message)
    {
        await Task.Delay(220);

        var step = new HandshakeStepInfo
        {
            Name = name,
            Status = status,
            Message = message,
            Timestamp = DateTimeOffset.Now
        };

        _steps.Add(step);
        StepAdded?.Invoke(this, step);
    }

    private void SetStatus(SessionStatus status, string message)
    {
        _current.Status = status;
        _current.Message = message;
        _current.LastUpdated = DateTimeOffset.Now;
        SessionChanged?.Invoke(this, _current);
    }

    private void SetCurrent(TunnelSessionInfo session)
    {
        _current = session;
        SessionChanged?.Invoke(this, _current);
    }

    private bool IsLockedByAnotherPeer(DeviceInfo? requestedPeer, out string message)
    {
        message = string.Empty;

        if (_current.Status != SessionStatus.Connected || string.IsNullOrWhiteSpace(_current.PeerDeviceId))
        {
            return false;
        }

        if (requestedPeer is not null && requestedPeer.DeviceId == _current.PeerDeviceId)
        {
            message = $"当前已经连接 {_current.PeerDeviceName}，无需重复连接。";
            return true;
        }

        var peerName = string.IsNullOrWhiteSpace(_current.PeerDeviceName) ? _current.PeerDeviceId : _current.PeerDeviceName;
        var requestedName = requestedPeer is null ? "其他设备" : requestedPeer.ComputerName;
        message = $"当前已经连接 {peerName}，不能再连接 {requestedName}。请先手动断开，或等待原设备离线后重新搜索。";
        return true;
    }

    private static bool IsValidIp(string value)
    {
        return IPAddress.TryParse(value, out var address) && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;
    }

    private static string GetRoleText(DeviceRole role)
    {
        return role switch
        {
            DeviceRole.DebugClient => "调试端",
            DeviceRole.GatewayAgent => "网关端",
            _ => "未知"
        };
    }
}

namespace RobotNet.Windows.Wpf.Models;

public sealed class TcpProxySessionInfo
{
    public string SessionId { get; set; } = string.Empty;

    public TcpProxyStatus Status { get; set; } = TcpProxyStatus.Stopped;

    public DeviceRole Role { get; set; } = DeviceRole.Unknown;

    public string LocalBindAddress { get; set; } = "127.0.0.1";

    public int LocalListenPort { get; set; }

    public string GatewayLanIp { get; set; } = string.Empty;

    public int ProxyControlPort { get; set; }

    public string TargetDeviceIp { get; set; } = string.Empty;

    public int TargetDevicePort { get; set; }

    public int ActiveConnections { get; set; }

    public long BytesSent { get; set; }

    public long BytesReceived { get; set; }

    public string SecurityMode { get; set; } = "未启用";

    public int HeartbeatIntervalSeconds { get; set; }

    public int IdleTimeoutSeconds { get; set; }

    public int ReconnectAttempts { get; set; }

    public DateTimeOffset? LastHeartbeatAt { get; set; }

    public string LastError { get; set; } = string.Empty;

    public string Message { get; set; } = "TCP 代理尚未启动。";

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.Now;

    public string StatusText => Status switch
    {
        TcpProxyStatus.Stopped => "已停止",
        TcpProxyStatus.Checking => "检查中",
        TcpProxyStatus.Ready => "就绪",
        TcpProxyStatus.Starting => "启动中",
        TcpProxyStatus.Listening => "监听中",
        TcpProxyStatus.Forwarding => "转发中",
        TcpProxyStatus.Stopping => "停止中",
        TcpProxyStatus.Failed => "失败",
        _ => "未知"
    };

    public string StatusColor => Status switch
    {
        TcpProxyStatus.Listening or TcpProxyStatus.Forwarding => "#1F7A3D",
        TcpProxyStatus.Failed => "#B42318",
        TcpProxyStatus.Checking or TcpProxyStatus.Starting or TcpProxyStatus.Stopping => "#2563A6",
        _ => "#5B6673"
    };

    public string EndpointText => $"{LocalBindAddress}:{LocalListenPort}";

    public string TargetEndpointText => $"{TargetDeviceIp}:{TargetDevicePort}";

    public string ControlEndpointText => ProxyControlPort <= 0 ? "-" : $"{GatewayLanIp}:{ProxyControlPort}";

    public string LastUpdatedText => LastUpdated.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");

    public string StabilityText => HeartbeatIntervalSeconds <= 0 || IdleTimeoutSeconds <= 0
        ? "-"
        : $"心跳 {HeartbeatIntervalSeconds}s，空闲超时 {IdleTimeoutSeconds}s，重试 {ReconnectAttempts} 次";

    public string LastHeartbeatText => LastHeartbeatAt.HasValue
        ? LastHeartbeatAt.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss")
        : "-";
}

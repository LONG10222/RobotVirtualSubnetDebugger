namespace RobotNet.Windows.Wpf.Models;

public sealed class TunnelSessionInfo
{
    public string SessionId { get; set; } = string.Empty;

    public SessionStatus Status { get; set; } = SessionStatus.Idle;

    public DeviceRole LocalRole { get; set; } = DeviceRole.Unknown;

    public string LocalDeviceId { get; set; } = string.Empty;

    public string LocalDeviceName { get; set; } = string.Empty;

    public string PeerDeviceId { get; set; } = string.Empty;

    public string PeerDeviceName { get; set; } = string.Empty;

    public string PeerLanIp { get; set; } = string.Empty;

    public DeviceRole PeerRole { get; set; } = DeviceRole.Unknown;

    public string GatewayLanIp { get; set; } = string.Empty;

    public string TargetDeviceIp { get; set; } = string.Empty;

    public string VirtualIp { get; set; } = string.Empty;

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.Now;

    public string Message { get; set; } = "尚未启动模拟连接。";

    public bool IsConnected => Status == SessionStatus.Connected;

    public string StatusText => Status switch
    {
        SessionStatus.Idle => "空闲",
        SessionStatus.Preparing => "准备中",
        SessionStatus.DiscoveringPeer => "发现对端",
        SessionStatus.Handshaking => "握手中",
        SessionStatus.Connected => "已连接（模拟）",
        SessionStatus.Disconnecting => "断开中",
        SessionStatus.Failed => "失败",
        _ => "未知"
    };

    public string StatusColor => Status switch
    {
        SessionStatus.Connected => "#1F7A3D",
        SessionStatus.Failed => "#B42318",
        SessionStatus.Preparing or SessionStatus.DiscoveringPeer or SessionStatus.Handshaking or SessionStatus.Disconnecting => "#2563A6",
        _ => "#5B6673"
    };

    public string LastUpdatedText => LastUpdated.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");

    public string PeerText => string.IsNullOrWhiteSpace(PeerDeviceName) ? "-" : PeerDeviceName;
}

namespace RobotNet.Windows.Wpf.Models;

public sealed class DeviceInfo
{
    public string DeviceId { get; set; } = string.Empty;

    public string ComputerName { get; set; } = string.Empty;

    public string LanIp { get; set; } = string.Empty;

    public DeviceRole Role { get; set; } = DeviceRole.Unknown;

    public DateTimeOffset LastSeen { get; set; }

    public bool IsOnline { get; set; }

    public bool IsBusy { get; set; }

    public string ConnectedPeerDeviceId { get; set; } = string.Empty;

    public string ConnectedPeerName { get; set; } = string.Empty;

    public string RoleText => Role switch
    {
        DeviceRole.DebugClient => "调试端",
        DeviceRole.GatewayAgent => "网关端",
        _ => "未知"
    };

    public string OnlineText => IsOnline ? "在线" : "离线";

    public string BusyText => IsBusy ? "已连接" : "空闲";

    public string ConnectedPeerText => string.IsNullOrWhiteSpace(ConnectedPeerName) ? "-" : ConnectedPeerName;

    public string LastSeenText => LastSeen.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
}

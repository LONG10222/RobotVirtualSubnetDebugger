namespace RobotNet.Windows.Wpf.Models;

public enum SessionStatus
{
    Idle,
    Preparing,
    DiscoveringPeer,
    Handshaking,
    Connected,
    Disconnecting,
    Failed
}

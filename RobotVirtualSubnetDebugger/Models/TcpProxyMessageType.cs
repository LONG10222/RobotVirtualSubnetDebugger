namespace RobotNet.Windows.Wpf.Models;

public enum TcpProxyMessageType
{
    Hello,
    OpenConnection,
    OpenConnectionResult,
    Data,
    CloseConnection,
    Heartbeat,
    Error
}

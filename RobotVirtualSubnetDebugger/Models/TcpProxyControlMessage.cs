namespace RobotNet.Windows.Wpf.Models;

public sealed class TcpProxyControlMessage
{
    public int ProtocolVersion { get; set; } = 1;

    public string SessionId { get; set; } = string.Empty;

    public string ConnectionId { get; set; } = string.Empty;

    public string SessionToken { get; set; } = string.Empty;

    public TcpProxyMessageType MessageType { get; set; } = TcpProxyMessageType.Hello;

    public bool Success { get; set; }

    public long Sequence { get; set; }

    public string TargetHost { get; set; } = string.Empty;

    public int TargetPort { get; set; }

    public string PayloadText { get; set; } = string.Empty;

    public string PayloadBase64 { get; set; } = string.Empty;

    public string EncryptionNonceBase64 { get; set; } = string.Empty;

    public string EncryptionTagBase64 { get; set; } = string.Empty;

    public string ErrorMessage { get; set; } = string.Empty;

    public string Nonce { get; set; } = string.Empty;

    public string Signature { get; set; } = string.Empty;

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
}

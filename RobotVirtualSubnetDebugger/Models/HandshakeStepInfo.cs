namespace RobotNet.Windows.Wpf.Models;

public sealed class HandshakeStepInfo
{
    public string Name { get; set; } = string.Empty;

    public DiagnosticStatus Status { get; set; } = DiagnosticStatus.Info;

    public string StatusText => Status switch
    {
        DiagnosticStatus.Success => "成功",
        DiagnosticStatus.Warning => "警告",
        DiagnosticStatus.Error => "错误",
        _ => "信息"
    };

    public string StatusColor => Status switch
    {
        DiagnosticStatus.Success => "#1F7A3D",
        DiagnosticStatus.Warning => "#A45A00",
        DiagnosticStatus.Error => "#B42318",
        _ => "#2563A6"
    };

    public string Message { get; set; } = string.Empty;

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;

    public string TimestampText => Timestamp.LocalDateTime.ToString("HH:mm:ss");
}

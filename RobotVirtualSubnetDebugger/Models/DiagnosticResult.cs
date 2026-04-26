namespace RobotNet.Windows.Wpf.Models;

public sealed class DiagnosticResult
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

    public string Suggestion { get; set; } = string.Empty;
}

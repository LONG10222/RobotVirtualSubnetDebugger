namespace RobotNet.Windows.Wpf.Models;

public sealed class PlatformOperationResult
{
    public string Name { get; set; } = string.Empty;

    public DiagnosticStatus Status { get; set; } = DiagnosticStatus.Info;

    public string Message { get; set; } = string.Empty;

    public string Suggestion { get; set; } = string.Empty;

    public bool RequiresAdministrator { get; set; }
}

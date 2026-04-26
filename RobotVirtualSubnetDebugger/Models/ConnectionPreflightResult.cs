namespace RobotNet.Windows.Wpf.Models;

public sealed class ConnectionPreflightResult
{
    public bool CanContinue { get; set; } = true;

    public bool ConfigChanged { get; set; }

    public string Summary { get; set; } = string.Empty;

    public IReadOnlyList<DiagnosticResult> Items { get; set; } = [];
}

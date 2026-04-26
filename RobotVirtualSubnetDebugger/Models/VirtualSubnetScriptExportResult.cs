namespace RobotNet.Windows.Wpf.Models;

public sealed class VirtualSubnetScriptExportResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;

    public string ApplyScriptPath { get; set; } = string.Empty;

    public string RollbackScriptPath { get; set; } = string.Empty;
}

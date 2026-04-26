namespace RobotNet.Windows.Wpf.Models;

public sealed class AdminElevationResult
{
    public bool IsAdministrator { get; set; }

    public bool RelaunchStarted { get; set; }

    public bool IsReadOnlyMode { get; set; }

    public string Message { get; set; } = string.Empty;

    public string AdminStatusText => IsAdministrator
        ? "管理员模式：已启用"
        : "管理员模式：未启用，请重新启动";
}

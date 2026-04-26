namespace RobotNet.Windows.Wpf.Models;

public sealed class PortCheckResult
{
    public int Port { get; set; }

    public PortProtocol Protocol { get; set; }

    public bool IsAvailable { get; set; }

    public int? ProcessId { get; set; }

    public string ProcessName { get; set; } = string.Empty;

    public bool IsOwnedByCurrentProcess { get; set; }

    public string OwnerText => IsAvailable
        ? "未占用"
        : ProcessId is null
            ? "已占用，未识别 PID"
            : $"{ProcessName} (PID {ProcessId})";
}

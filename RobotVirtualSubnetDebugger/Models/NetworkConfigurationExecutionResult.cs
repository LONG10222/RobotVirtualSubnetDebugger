namespace RobotNet.Windows.Wpf.Models;

public sealed class NetworkConfigurationExecutionResult
{
    public bool Success { get; set; }

    public NetworkConfigurationApplyStatus Status { get; set; } = NetworkConfigurationApplyStatus.NotConfigured;

    public string Message { get; set; } = string.Empty;

    public string Error { get; set; } = string.Empty;

    public DateTimeOffset ExecutedAt { get; set; } = DateTimeOffset.Now;

    public NetworkOperationRecord? Record { get; set; }

    public List<string> Logs { get; set; } = [];
}

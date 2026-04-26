namespace RobotNet.Windows.Wpf.Models;

public sealed class NetworkOperationRecord
{
    public string OperationId { get; set; } = Guid.NewGuid().ToString("N");

    public DeviceRole Role { get; set; }

    public string TargetSubnetCidr { get; set; } = string.Empty;

    public string GatewayLanIp { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public NetworkConfigurationApplyStatus Status { get; set; } = NetworkConfigurationApplyStatus.NotConfigured;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public DateTimeOffset? AppliedAt { get; set; }

    public DateTimeOffset? RolledBackAt { get; set; }

    public List<string> AppliedCommands { get; set; } = [];

    public List<string> RollbackCommands { get; set; } = [];

    public List<string> ExecutionLog { get; set; } = [];

    public string LastError { get; set; } = string.Empty;
}

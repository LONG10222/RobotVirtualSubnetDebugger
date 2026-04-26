namespace RobotNet.Windows.Wpf.Models;

public sealed class VirtualSubnetOperationStep
{
    public int Order { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Command { get; set; } = string.Empty;

    public string RollbackCommand { get; set; } = string.Empty;

    public bool RequiresAdministrator { get; set; }

    public bool WillModifySystem { get; set; }

    public string RiskLevel { get; set; } = "Info";

    public string RiskColor => RiskLevel switch
    {
        "High" => "#B42318",
        "Medium" => "#B7791F",
        "Low" => "#1F7A3D",
        _ => "#2563A6"
    };
}

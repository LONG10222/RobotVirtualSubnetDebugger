namespace RobotNet.Windows.Wpf.Models;

public sealed class VirtualSubnetPlan
{
    public string PlanId { get; set; } = Guid.NewGuid().ToString("N")[..8];

    public VirtualSubnetMode Mode { get; set; } = VirtualSubnetMode.RouteNat;

    public DeviceRole Role { get; set; } = DeviceRole.Unknown;

    public DiagnosticStatus Status { get; set; } = DiagnosticStatus.Info;

    public string Summary { get; set; } = string.Empty;

    public string TargetSubnetCidr { get; set; } = string.Empty;

    public string LanSubnetCidr { get; set; } = string.Empty;

    public string GatewayLanIp { get; set; } = string.Empty;

    public string TargetDeviceIp { get; set; } = string.Empty;

    public string VirtualIp { get; set; } = string.Empty;

    public string VirtualSubnetMask { get; set; } = string.Empty;

    public string LanInterfaceName { get; set; } = string.Empty;

    public string TargetInterfaceName { get; set; } = string.Empty;

    public bool RequiresAdministrator { get; set; } = true;

    public bool WillModifySystem { get; set; }

    public bool CanGenerateScripts { get; set; }

    public bool PreservesDefaultRoute { get; set; } = true;

    public bool PreservesDns { get; set; } = true;

    public List<string> Warnings { get; set; } = [];

    public List<VirtualSubnetOperationStep> Steps { get; set; } = [];

    public string StatusText => Status switch
    {
        DiagnosticStatus.Success => "可应用",
        DiagnosticStatus.Warning => "需要确认",
        DiagnosticStatus.Error => "不可生成",
        _ => "信息"
    };

    public string StatusColor => Status switch
    {
        DiagnosticStatus.Success => "#1F7A3D",
        DiagnosticStatus.Warning => "#B7791F",
        DiagnosticStatus.Error => "#B42318",
        _ => "#2563A6"
    };
}

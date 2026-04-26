using System.Text.Json.Serialization;

namespace RobotNet.Windows.Wpf.Models;

public sealed class AppConfig
{
    public string DeviceId { get; set; } = string.Empty;

    public string DeviceName { get; set; } = string.Empty;

    public DeviceRole Role { get; set; } = DeviceRole.Unknown;

    public string VirtualIp { get; set; } = "192.168.1.101";

    public string VirtualSubnetMask { get; set; } = "255.255.255.0";

    public string TargetDeviceIp { get; set; } = string.Empty;

    public int TargetDevicePort { get; set; }

    public int DiscoveryPort { get; set; } = 47831;

    public int LocalListenPort { get; set; } = 30003;

    public int ProxyControlPort { get; set; } = 47832;

    public string GatewayLanIp { get; set; } = string.Empty;

    public string TargetDeviceAdapterIp { get; set; } = string.Empty;

    public string SharedKey { get; set; } = string.Empty;

    public int ProxyHeartbeatIntervalSeconds { get; set; } = 5;

    public int ProxyIdleTimeoutSeconds { get; set; } = 20;

    public int ProxyReconnectAttempts { get; set; } = 2;

    public bool EnableNat { get; set; } = true;

    public bool EnablePreciseRoute { get; set; } = true;

    public bool EnableTcpProxyMode { get; set; } = true;

    public bool EnableVirtualSubnetMode { get; set; } = true;

    public string GitHubRepositoryOwner { get; set; } = "LONG10222";

    public string GitHubRepositoryName { get; set; } = "RobotVirtualSubnetDebugger";

    public bool EnableUpdateCheckOnStartup { get; set; } = true;

    [JsonPropertyName("RobotIp")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyRobotIp { get; set; }

    [JsonPropertyName("RobotPort")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? LegacyRobotPort { get; set; }

    [JsonPropertyName("RobotAdapterIp")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyRobotAdapterIp { get; set; }
}

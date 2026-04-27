using System.Text.Json.Serialization;

namespace RobotNet.Windows.Wpf.Models;

public sealed class DiscoveryMessage
{
    public const string CurrentProtocol = "RobotNetDiscoveryV1";

    public string Protocol { get; set; } = CurrentProtocol;

    public string DeviceId { get; set; } = string.Empty;

    public string ComputerName { get; set; } = string.Empty;

    public string LanIp { get; set; } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DeviceRole Role { get; set; } = DeviceRole.Unknown;

    public bool IsBusy { get; set; }

    public string ConnectedPeerDeviceId { get; set; } = string.Empty;

    public string ConnectedPeerName { get; set; } = string.Empty;

    public string AutoPairingToken { get; set; } = string.Empty;

    public DateTimeOffset Timestamp { get; set; }
}

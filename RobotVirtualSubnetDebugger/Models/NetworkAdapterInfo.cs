namespace RobotNet.Windows.Wpf.Models;

public sealed class NetworkAdapterInfo
{
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string MacAddress { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string IPv4Address { get; set; } = string.Empty;

    public string SubnetMask { get; set; } = string.Empty;

    public string Gateway { get; set; } = string.Empty;

    public List<string> DnsServers { get; set; } = [];

    public bool IsVirtual { get; set; }

    public bool IsTargetNetworkCandidate { get; set; }

    public bool IsLanCandidate { get; set; }

    public string DnsServersText => DnsServers.Count == 0 ? "-" : string.Join(", ", DnsServers);
}

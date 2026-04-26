using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using RobotNet.Windows.Wpf.Models;

namespace RobotNet.Windows.Wpf.Services.Platform;

public sealed class WindowsNetworkAdapterService : INetworkAdapterService
{
    private const string DefaultTargetDeviceIp = "192.168.1.10";
    private const string LanReferenceIp = "192.168.31.1";

    public IReadOnlyList<NetworkAdapterInfo> GetAdapters()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Select(adapter => CreateAdapterInfo(adapter, DefaultTargetDeviceIp))
            .OrderByDescending(adapter => adapter.IsLanCandidate)
            .ThenByDescending(adapter => adapter.IsTargetNetworkCandidate)
            .ThenBy(adapter => adapter.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<NetworkAdapterInfo> FindTargetNetworkCandidates(string targetDeviceIp)
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Select(adapter => CreateAdapterInfo(adapter, targetDeviceIp))
            .Where(adapter => adapter.IsTargetNetworkCandidate)
            .ToList();
    }

    public IReadOnlyList<NetworkAdapterInfo> FindLanCandidates()
    {
        return GetAdapters()
            .Where(adapter => adapter.IsLanCandidate)
            .ToList();
    }

    public bool IsSameSubnet(string ip1, string mask1, string ip2)
    {
        if (!TryReadIPv4(ip1, out var left) ||
            !TryReadIPv4(mask1, out var mask) ||
            !TryReadIPv4(ip2, out var right))
        {
            return false;
        }

        return (left & mask) == (right & mask);
    }

    private NetworkAdapterInfo CreateAdapterInfo(NetworkInterface adapter, string targetDeviceIp)
    {
        var properties = adapter.GetIPProperties();
        var unicast = properties.UnicastAddresses
            .FirstOrDefault(address =>
                address.Address.AddressFamily == AddressFamily.InterNetwork &&
                !IPAddress.IsLoopback(address.Address));

        var ipv4Address = unicast?.Address.ToString() ?? string.Empty;
        var subnetMask = unicast?.IPv4Mask?.ToString() ?? string.Empty;
        var gateway = properties.GatewayAddresses
            .Select(address => address.Address)
            .FirstOrDefault(address =>
                address.AddressFamily == AddressFamily.InterNetwork &&
                !address.Equals(IPAddress.Any))
            ?.ToString() ?? string.Empty;

        var dnsServers = properties.DnsAddresses
            .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
            .Select(address => address.ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var isVirtual = IsVirtualAdapter(adapter);
        var isTargetCandidate =
            adapter.OperationalStatus == OperationalStatus.Up &&
            !isVirtual &&
            !string.IsNullOrWhiteSpace(ipv4Address) &&
            IsSameSubnet(ipv4Address, subnetMask, targetDeviceIp);

        var isLanCandidate = IsLanCandidate(
            adapter,
            ipv4Address,
            subnetMask,
            gateway,
            isVirtual,
            isTargetCandidate);

        return new NetworkAdapterInfo
        {
            Name = adapter.Name,
            Description = adapter.Description,
            MacAddress = FormatMacAddress(adapter.GetPhysicalAddress()),
            Status = adapter.OperationalStatus.ToString(),
            Type = adapter.NetworkInterfaceType.ToString(),
            IPv4Address = ipv4Address,
            SubnetMask = subnetMask,
            Gateway = gateway,
            DnsServers = dnsServers,
            IsVirtual = isVirtual,
            IsTargetNetworkCandidate = isTargetCandidate,
            IsLanCandidate = isLanCandidate
        };
    }

    private bool IsLanCandidate(
        NetworkInterface adapter,
        string ipv4Address,
        string subnetMask,
        string gateway,
        bool isVirtual,
        bool isTargetCandidate)
    {
        if (adapter.OperationalStatus != OperationalStatus.Up ||
            isVirtual ||
            isTargetCandidate ||
            string.IsNullOrWhiteSpace(ipv4Address))
        {
            return false;
        }

        if (IsSameSubnet(ipv4Address, subnetMask, LanReferenceIp))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(gateway);
    }

    private static bool IsVirtualAdapter(NetworkInterface adapter)
    {
        if (adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
        {
            return true;
        }

        var text = $"{adapter.Name} {adapter.Description}".ToLowerInvariant();
        string[] virtualMarkers =
        [
            "virtual",
            "vmware",
            "hyper-v",
            "virtualbox",
            "vbox",
            "wsl",
            "vpn",
            "tap",
            "tunnel",
            "wireguard",
            "zerotier",
            "npcap",
            "loopback",
            "vethernet"
        ];

        return virtualMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal));
    }

    private static string FormatMacAddress(PhysicalAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 0
            ? string.Empty
            : string.Join("-", bytes.Select(value => value.ToString("X2")));
    }

    private static bool TryReadIPv4(string value, out uint result)
    {
        result = 0;

        if (!IPAddress.TryParse(value, out var address))
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        if (bytes.Length != 4)
        {
            return false;
        }

        result =
            ((uint)bytes[0] << 24) |
            ((uint)bytes[1] << 16) |
            ((uint)bytes[2] << 8) |
            bytes[3];

        return true;
    }
}

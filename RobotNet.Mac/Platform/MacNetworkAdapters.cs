using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using RobotNet.Windows.Wpf.Models;
using RobotNet.Windows.Wpf.Services.Platform;

namespace RobotNet.Mac.Platform;

internal sealed class MacNetworkAdapters : INetworkAdapterService
{
    public IReadOnlyList<NetworkAdapterInfo> GetAdapters()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .SelectMany(interfaceInfo =>
            {
                var properties = interfaceInfo.GetIPProperties();
                var addresses = properties.UnicastAddresses
                    .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
                    .ToArray();
                var gateway = properties.GatewayAddresses
                    .FirstOrDefault(address => address.Address.AddressFamily == AddressFamily.InterNetwork);
                return (addresses.Length == 0 ? new UnicastIPAddressInformation?[] { null } : addresses)
                    .Select(address =>
                    {
                        var name = interfaceInfo.Name;
                        var physical = IsHardwareInterface(name);
                        var ipv4 = address?.Address.ToString() ?? string.Empty;
                        var mask = address?.IPv4Mask?.ToString() ?? string.Empty;
                        var up = interfaceInfo.OperationalStatus == OperationalStatus.Up;
                        return new NetworkAdapterInfo
                        {
                            Name = name,
                            Description = interfaceInfo.Description,
                            MacAddress = interfaceInfo.GetPhysicalAddress().ToString(),
                            Status = interfaceInfo.OperationalStatus.ToString(),
                            Type = interfaceInfo.NetworkInterfaceType.ToString(),
                            IPv4Address = ipv4,
                            SubnetMask = mask,
                            Gateway = gateway?.Address.ToString() ?? string.Empty,
                            IsVirtual = !physical,
                            IsLanCandidate = physical && up && MacIpv4.IsHost(ipv4) && !ipv4.StartsWith("169.254.", StringComparison.Ordinal),
                            IsTargetNetworkCandidate = physical && up && IsSameSubnet(ipv4, mask, "192.168.1.10")
                        };
                    });
            })
            .OrderByDescending(adapter => !string.IsNullOrEmpty(adapter.Gateway))
            .ThenByDescending(adapter => adapter.IsLanCandidate)
            .ThenBy(adapter => adapter.Name, StringComparer.Ordinal)
            .ToList();
    }

    public IReadOnlyList<NetworkAdapterInfo> FindTargetNetworkCandidates(string targetDeviceIp)
    {
        return GetAdapters()
            .Where(adapter => !adapter.IsVirtual && adapter.Status == "Up" &&
                              IsSameSubnet(adapter.IPv4Address, adapter.SubnetMask, targetDeviceIp))
            .ToArray();
    }

    public IReadOnlyList<NetworkAdapterInfo> FindLanCandidates() =>
        GetAdapters().Where(adapter => adapter.IsLanCandidate).ToArray();

    public bool IsSameSubnet(string ip1, string mask1, string ip2) =>
        MacIpv4.TryNetwork(ip1, mask1, out var network) && network.Contains(ip2);

    public static bool IsHardwareInterface(string name) =>
        Regex.IsMatch(name, @"\Aen[0-9]+\z", RegexOptions.CultureInvariant);
}

internal readonly record struct MacIpv4(uint First, uint Mask, int PrefixLength)
{
    public string Cidr => $"{ToAddress(First)}/{PrefixLength}";
    public string DottedMask => ToAddress(Mask);

    public bool Contains(string address) => TryRead(address, out var value) && (value & Mask) == First;
    public bool Overlaps(MacIpv4 other) => ((First ^ other.First) & (Mask & other.Mask)) == 0;

    public bool IsUsableHost(string address) =>
        IsHost(address) && TryRead(address, out var value) && Contains(address) &&
        (PrefixLength >= 31 || (value != First && value != (First | ~Mask)));

    public static bool IsHost(string address) =>
        TryRead(address, out var value) && value >> 24 is > 0 and < 224 and not 127;

    public static bool TryNetwork(string address, string mask, out MacIpv4 network)
    {
        network = default;
        if (!TryRead(address, out var value) || !TryRead(mask, out var bits) || bits == 0)
            return false;
        var inverted = ~bits;
        if ((inverted & (inverted + 1)) != 0)
            return false;
        network = new(value & bits, bits, System.Numerics.BitOperations.PopCount(bits));
        return true;
    }

    private static bool TryRead(string value, out uint address)
    {
        address = 0;
        if (!IPAddress.TryParse(value, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork ||
            !string.Equals(ip.ToString(), value, StringComparison.Ordinal))
            return false;
        address = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(ip.GetAddressBytes());
        return true;
    }

    private static string ToAddress(uint address) =>
        $"{address >> 24}.{(address >> 16) & 255}.{(address >> 8) & 255}.{address & 255}";
}

using System.Net;
using RobotNet.Windows.Wpf.Models;

namespace RobotNet.Windows.Wpf.Services.Platform;

public sealed class WindowsAdapterIpConfigurationService : IAdapterIpConfigurationService
{
    public VirtualSubnetOperationStep? CreateEnsureAdapterIpStep(string interfaceAlias, string ipAddress, string subnetMask)
    {
        if (string.IsNullOrWhiteSpace(interfaceAlias) ||
            string.IsNullOrWhiteSpace(ipAddress) ||
            string.IsNullOrWhiteSpace(subnetMask) ||
            !TryGetPrefixLength(subnetMask, out var prefixLength))
        {
            return null;
        }

        var escapedAlias = EscapePowerShell(interfaceAlias);
        var step = new VirtualSubnetOperationStep
        {
            Order = 12,
            Name = "配置设备网口 IP",
            Description = $"确认 {interfaceAlias} 拥有 {ipAddress}/{prefixLength}。如果已存在则不重复添加。",
            RequiresAdministrator = true,
            WillModifySystem = true,
            RiskLevel = "Medium"
        };
        var ip = OwnedNetworkChange.Quote(ipAddress);
        OwnedNetworkChange.Configure(step,
            $"Get-NetIPAddress -AddressFamily IPv4 -ErrorAction Stop | Where-Object {{ $_.InterfaceAlias -eq '{escapedAlias}' -and $_.IPAddress -eq '{ip}' }}",
            $"$current.Count -eq 1 -and $current[0].PrefixLength -eq {prefixLength}",
            $"$ipInterface = @(Get-NetIPInterface -AddressFamily IPv4 -ErrorAction Stop | Where-Object {{ $_.InterfaceAlias -eq '{escapedAlias}' }}); if ($ipInterface.Count -ne 1 -or $ipInterface[0].Dhcp -ne 'Disabled') {{ throw 'Refusing to change DHCP configuration; configure this adapter manually first.' }}; New-NetIPAddress -InterfaceAlias '{escapedAlias}' -IPAddress '{ip}' -PrefixLength {prefixLength} -AddressFamily IPv4 -PolicyStore ActiveStore -ErrorAction Stop | Out-Null",
            "$current | Remove-NetIPAddress -Confirm:$false -ErrorAction Stop",
            "InterfaceIndex, InterfaceAlias, IPAddress, PrefixLength, AddressFamily, PrefixOrigin, SuffixOrigin, InstanceID");
        return step;
    }

    private static bool TryGetPrefixLength(string subnetMask, out int prefixLength)
    {
        prefixLength = 0;
        if (!IPAddress.TryParse(subnetMask, out var maskAddress))
        {
            return false;
        }

        var bytes = maskAddress.GetAddressBytes();
        if (bytes.Length != 4)
        {
            return false;
        }

        var mask =
            ((uint)bytes[0] << 24) |
            ((uint)bytes[1] << 16) |
            ((uint)bytes[2] << 8) |
            bytes[3];
        var seenZero = false;
        for (var i = 31; i >= 0; i--)
        {
            var bit = (mask & (1u << i)) != 0;
            if (bit && seenZero)
            {
                return false;
            }

            if (bit)
            {
                prefixLength++;
            }
            else
            {
                seenZero = true;
            }
        }

        return true;
    }

    private static string EscapePowerShell(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }
}

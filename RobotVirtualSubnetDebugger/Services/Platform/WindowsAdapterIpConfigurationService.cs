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
        return new VirtualSubnetOperationStep
        {
            Order = 12,
            Name = "配置设备网口 IP",
            Description = $"确认 {interfaceAlias} 拥有 {ipAddress}/{prefixLength}。如果已存在则不重复添加。",
            Command = $"if (-not (Get-NetIPAddress -InterfaceAlias '{escapedAlias}' -IPAddress '{ipAddress}' -AddressFamily IPv4 -ErrorAction SilentlyContinue)) {{ New-NetIPAddress -InterfaceAlias '{escapedAlias}' -IPAddress '{ipAddress}' -PrefixLength {prefixLength} -AddressFamily IPv4 -PolicyStore ActiveStore }}",
            RollbackCommand = $"Remove-NetIPAddress -InterfaceAlias '{escapedAlias}' -IPAddress '{ipAddress}' -Confirm:$false -ErrorAction SilentlyContinue",
            RequiresAdministrator = true,
            WillModifySystem = true,
            RiskLevel = "Medium"
        };
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

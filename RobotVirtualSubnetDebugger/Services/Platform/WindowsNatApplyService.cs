using RobotNet.Windows.Wpf.Models;

namespace RobotNet.Windows.Wpf.Services.Platform;

public sealed class WindowsNatApplyService : INatApplyService
{
    public VirtualSubnetOperationStep CreateNatStep(string natName, string lanSubnetCidr)
    {
        var step = new VirtualSubnetOperationStep
        {
            Order = 40,
            Name = "创建或复用网关端 NAT",
            Description = $"把来自 {lanSubnetCidr} 的调试端流量 NAT 到目标设备网段，避免目标设备需要回程路由。",
            RequiresAdministrator = true,
            WillModifySystem = true,
            RiskLevel = "High"
        };
        var name = EscapePowerShell(natName);
        var prefix = EscapePowerShell(lanSubnetCidr);
        OwnedNetworkChange.Configure(step,
            $"Get-NetNat -ErrorAction Stop | Where-Object {{ $_.Name -eq '{name}' }}",
            $"$current.Count -eq 1 -and $current[0].InternalIPInterfaceAddressPrefix -eq '{prefix}' -and [string]::IsNullOrWhiteSpace([string]$current[0].ExternalIPInterfaceAddressPrefix)",
            CreatePreflightCommand(natName, lanSubnetCidr) + Environment.NewLine + $"New-NetNat -Name '{name}' -InternalIPInterfaceAddressPrefix '{prefix}' -ErrorAction Stop | Out-Null",
            "$current | Remove-NetNat -Confirm:$false -ErrorAction Stop",
            "Name, InternalIPInterfaceAddressPrefix, ExternalIPInterfaceAddressPrefix, InstanceID");
        step.Command = CreatePreflightCommand(natName, lanSubnetCidr) + Environment.NewLine + step.Command;
        return step;
    }

    // WinNAT must not be added alongside an unrelated NAT (including Hyper-V/container NAT).
    // Run before any IP/forwarding mutations, then repeat just before NAT creation.
    internal static string CreatePreflightCommand(string natName, string lanSubnetCidr)
    {
        var name = EscapePowerShell(natName);
        var prefix = EscapePowerShell(lanSubnetCidr);
        return $$"""
            $allRobotNetNats = @(Get-NetNat -ErrorAction Stop)
            if ($allRobotNetNats.Count -gt 0 -and -not (
                $allRobotNetNats.Count -eq 1 -and
                $allRobotNetNats[0].Name -eq '{{name}}' -and
                $allRobotNetNats[0].InternalIPInterfaceAddressPrefix -eq '{{prefix}}' -and
                [string]::IsNullOrWhiteSpace([string]$allRobotNetNats[0].ExternalIPInterfaceAddressPrefix))) {
                throw 'Existing NAT is incompatible or unrelated. No existing NAT will be removed or modified. Review Hyper-V, containers, VPN and ICS separately.'
            }
            """;
    }

    private static string EscapePowerShell(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }
}

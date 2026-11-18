using RobotNet.Windows.Wpf.Models;

namespace RobotNet.Windows.Wpf.Services.Platform;

public sealed class WindowsRouteApplyService : IRouteApplyService
{
    public VirtualSubnetOperationStep CreatePreciseRouteStep(VirtualSubnetPlan plan, AppConfig config, string interfaceAlias)
    {
        var escapedAlias = EscapePowerShell(interfaceAlias);
        var step = new VirtualSubnetOperationStep
        {
            Order = 20,
            Name = "添加目标网段精确路由",
            Description = $"仅把 {plan.TargetSubnetCidr} 指向网关端 {config.GatewayLanIp}，不影响默认上网、DNS 或 VPN。",
            RequiresAdministrator = true,
            WillModifySystem = true,
            RiskLevel = "Medium"
        };
        var prefix = OwnedNetworkChange.Quote(plan.TargetSubnetCidr);
        var nextHop = OwnedNetworkChange.Quote(config.GatewayLanIp);
        var preflight = CreatePreflightCommand(plan.TargetSubnetCidr, interfaceAlias, config.GatewayLanIp);
        OwnedNetworkChange.Configure(step,
            $"Get-NetRoute -AddressFamily IPv4 -PolicyStore ActiveStore -ErrorAction Stop | Where-Object {{ $_.DestinationPrefix -eq '{prefix}' -and $_.InterfaceAlias -eq '{escapedAlias}' }}",
            $"$current.Count -eq 1 -and $current[0].NextHop -eq '{nextHop}'",
            preflight + Environment.NewLine + $"New-NetRoute -DestinationPrefix '{prefix}' -InterfaceAlias '{escapedAlias}' -NextHop '{nextHop}' -RouteMetric 5 -PolicyStore ActiveStore -ErrorAction Stop | Out-Null",
            "$current | Remove-NetRoute -Confirm:$false -ErrorAction Stop",
            "InterfaceIndex, InterfaceAlias, DestinationPrefix, NextHop, RouteMetric, Protocol, InstanceID");
        step.Command = preflight + Environment.NewLine + step.Command;
        return step;
    }

    // Inspect all interfaces before mutation. A VPN /32 on a different interface
    // can otherwise win route selection, regardless of the route metric we request.
    internal static string CreatePreflightCommand(string destination, string alias, string gateway)
    {
        var prefix = EscapePowerShell(destination);
        var iface = EscapePowerShell(alias);
        var hop = EscapePowerShell(gateway);
        return $$"""
            $robotHostRoutes = @(Get-NetRoute -AddressFamily IPv4 -PolicyStore ActiveStore -ErrorAction Stop | Where-Object { $_.DestinationPrefix -eq '{{prefix}}' })
            if ($robotHostRoutes.Count -gt 0 -and -not (
                $robotHostRoutes.Count -eq 1 -and
                $robotHostRoutes[0].InterfaceAlias -eq '{{iface}}' -and
                $robotHostRoutes[0].NextHop -eq '{{hop}}')) {
                throw 'Conflicting robot host route exists, possibly on another adapter or VPN. No route will be removed or overridden. Review route selection before continuing.'
            }
            """;
    }

    private static string EscapePowerShell(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }
}

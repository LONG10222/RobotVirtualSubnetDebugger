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
        OwnedNetworkChange.Configure(step,
            $"Get-NetRoute -AddressFamily IPv4 -PolicyStore ActiveStore -ErrorAction Stop | Where-Object {{ $_.DestinationPrefix -eq '{prefix}' -and $_.InterfaceAlias -eq '{escapedAlias}' }}",
            $"$current.Count -eq 1 -and $current[0].NextHop -eq '{nextHop}'",
            $"New-NetRoute -DestinationPrefix '{prefix}' -InterfaceAlias '{escapedAlias}' -NextHop '{nextHop}' -RouteMetric 5 -PolicyStore ActiveStore -ErrorAction Stop | Out-Null",
            "$current | Remove-NetRoute -Confirm:$false -ErrorAction Stop",
            "InterfaceIndex, InterfaceAlias, DestinationPrefix, NextHop, RouteMetric, Protocol, InstanceID");
        return step;
    }

    private static string EscapePowerShell(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }
}

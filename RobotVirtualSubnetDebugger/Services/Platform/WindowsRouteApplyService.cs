using RobotNet.Windows.Wpf.Models;

namespace RobotNet.Windows.Wpf.Services.Platform;

public sealed class WindowsRouteApplyService : IRouteApplyService
{
    public VirtualSubnetOperationStep CreatePreciseRouteStep(VirtualSubnetPlan plan, AppConfig config, string interfaceAlias)
    {
        var escapedAlias = EscapePowerShell(interfaceAlias);
        return new VirtualSubnetOperationStep
        {
            Order = 20,
            Name = "添加目标网段精确路由",
            Description = $"仅把 {plan.TargetSubnetCidr} 指向网关端 {config.GatewayLanIp}，不影响默认上网、DNS 或 VPN。",
            Command = $"Get-NetRoute -DestinationPrefix '{plan.TargetSubnetCidr}' -ErrorAction SilentlyContinue | Where-Object {{ $_.NextHop -eq '{config.GatewayLanIp}' -or $_.InterfaceAlias -eq '{escapedAlias}' }} | Remove-NetRoute -Confirm:$false; New-NetRoute -DestinationPrefix '{plan.TargetSubnetCidr}' -InterfaceAlias '{escapedAlias}' -NextHop '{config.GatewayLanIp}' -RouteMetric 5 -PolicyStore ActiveStore",
            RollbackCommand = $"Remove-NetRoute -DestinationPrefix '{plan.TargetSubnetCidr}' -NextHop '{config.GatewayLanIp}' -Confirm:$false -ErrorAction SilentlyContinue",
            RequiresAdministrator = true,
            WillModifySystem = true,
            RiskLevel = "Medium"
        };
    }

    private static string EscapePowerShell(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }
}

using RobotNet.Windows.Wpf.Models;

namespace RobotNet.Windows.Wpf.Services.Platform;

public sealed class WindowsNatApplyService : INatApplyService
{
    public VirtualSubnetOperationStep CreateNatStep(string natName, string lanSubnetCidr)
    {
        return new VirtualSubnetOperationStep
        {
            Order = 40,
            Name = "创建或更新网关端 NAT",
            Description = $"把来自 {lanSubnetCidr} 的调试端流量 NAT 到目标设备网段，避免目标设备需要回程路由。",
            Command = $"if (Get-NetNat -Name '{EscapePowerShell(natName)}' -ErrorAction SilentlyContinue) {{ Remove-NetNat -Name '{EscapePowerShell(natName)}' -Confirm:$false }}; New-NetNat -Name '{EscapePowerShell(natName)}' -InternalIPInterfaceAddressPrefix '{lanSubnetCidr}'",
            RollbackCommand = $"Remove-NetNat -Name '{EscapePowerShell(natName)}' -Confirm:$false -ErrorAction SilentlyContinue",
            RequiresAdministrator = true,
            WillModifySystem = true,
            RiskLevel = "High"
        };
    }

    private static string EscapePowerShell(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }
}

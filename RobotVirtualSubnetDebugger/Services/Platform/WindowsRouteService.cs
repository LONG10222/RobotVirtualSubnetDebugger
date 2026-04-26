using RobotNet.Windows.Wpf.Models;

namespace RobotNet.Windows.Wpf.Services.Platform;

public sealed class WindowsRouteService : IRouteService
{
    public Task<PlatformOperationResult> CheckAsync(AppConfig config)
    {
        return Task.FromResult(new PlatformOperationResult
        {
            Name = "路由能力检查",
            Status = DiagnosticStatus.Info,
            Message = "第五阶段已提供目标网段精确路由脚本生成能力，程序本身不会静默修改系统路由表。",
            Suggestion = config.Role == DeviceRole.DebugClient
                ? $"调试端脚本会把目标设备网段精确导向网关端 {config.GatewayLanIp}，不会接管默认路由。"
                : "网关端脚本会启用指定网卡转发并创建 NAT，不会修改默认网关或 DNS。",
            RequiresAdministrator = true
        });
    }

    public Task<PlatformOperationResult> ApplyAsync(AppConfig config)
    {
        return Task.FromResult(new PlatformOperationResult
        {
            Name = "路由应用占位",
            Status = DiagnosticStatus.Warning,
            Message = "ApplyAsync 当前不直接执行 route、netsh 或 PowerShell 命令。",
            Suggestion = "请在“虚拟网段 Virtual Subnet”页面生成应用/回滚脚本，并用管理员 PowerShell 手动审阅执行。",
            RequiresAdministrator = true
        });
    }
}

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
            Message = "当前已提供目标网段精确路由预览和一键应用能力，程序不会静默修改系统路由表。",
            Suggestion = config.Role == DeviceRole.DebugClient
                ? $"调试端会把目标设备网段精确导向网关端 {config.GatewayLanIp}，不会接管默认路由。"
                : "网关端会启用指定网卡转发并创建 NAT，不会修改默认网关或 DNS。",
            RequiresAdministrator = true
        });
    }

    public Task<PlatformOperationResult> ApplyAsync(AppConfig config)
    {
        return Task.FromResult(new PlatformOperationResult
        {
            Name = "路由应用占位",
            Status = DiagnosticStatus.Warning,
            Message = "路由应用请通过 INetworkConfigurationExecutor 统一执行，避免绕过预览、确认、审计和回滚。",
            Suggestion = "请在“虚拟网段 Virtual Subnet”页面预览后一键应用，或导出脚本做高级审计。",
            RequiresAdministrator = true
        });
    }
}

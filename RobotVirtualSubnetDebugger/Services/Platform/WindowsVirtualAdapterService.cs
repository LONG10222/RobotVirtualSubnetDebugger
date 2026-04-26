using RobotNet.Windows.Wpf.Models;

namespace RobotNet.Windows.Wpf.Services.Platform;

public sealed class WindowsVirtualAdapterService : IVirtualAdapterService
{
    public Task<PlatformOperationResult> CheckAsync(AppConfig config)
    {
        return Task.FromResult(new PlatformOperationResult
        {
            Name = "虚拟网卡能力检查",
            Status = DiagnosticStatus.Info,
            Message = "第五阶段 MVP 采用目标网段精确路由 + 网关 NAT，不强制创建本机虚拟网卡。",
            Suggestion = $"如果后续需要让调试端拥有真实虚拟 IP {config.VirtualIp}/{config.VirtualSubnetMask}，应单独接入 Wintun/TUN 驱动并保留回滚策略。",
            RequiresAdministrator = true
        });
    }

    public Task<PlatformOperationResult> EnsureAsync(AppConfig config)
    {
        return Task.FromResult(new PlatformOperationResult
        {
            Name = "虚拟网卡创建占位",
            Status = DiagnosticStatus.Warning,
            Message = "EnsureAsync 当前不会安装驱动或创建虚拟网卡。第五阶段主路线不依赖虚拟网卡驱动。",
            Suggestion = "需要驱动级透明虚拟 IP 时，应新增 Wintun/TUN 专用实现，并要求管理员确认、操作预览和卸载回滚。",
            RequiresAdministrator = true
        });
    }
}

using RobotNet.Windows.Wpf.Models;

namespace RobotNet.Windows.Wpf.Services.Platform;

public sealed class WindowsTunnelService : ITunnelService
{
    public Task<PlatformOperationResult> CheckAsync(AppConfig config)
    {
        return Task.FromResult(new PlatformOperationResult
        {
            Name = "隧道能力检查",
            Status = DiagnosticStatus.Info,
            Message = "第四阶段已提供安全 TCP 代理数据面；第五阶段主路线使用系统路由/NAT 脚本，不在此服务内启动驱动级隧道。",
            Suggestion = "如后续接入 Wintun/TUN，应在独立平台服务中实现驱动加载、包读写、权限确认和回滚。",
            RequiresAdministrator = false
        });
    }

    public Task<PlatformOperationResult> StartAsync(AppConfig config)
    {
        return Task.FromResult(new PlatformOperationResult
        {
            Name = "隧道启动占位",
            Status = DiagnosticStatus.Warning,
            Message = "StartAsync 当前不启动驱动级隧道。真实 TCP 流量已由 TCP 代理服务处理。",
            Suggestion = "第五阶段需要系统级网段访问时，请使用虚拟网段页面生成路由/NAT 脚本。",
            RequiresAdministrator = false
        });
    }

    public Task<PlatformOperationResult> StopAsync()
    {
        return Task.FromResult(new PlatformOperationResult
        {
            Name = "隧道停止占位",
            Status = DiagnosticStatus.Info,
            Message = "StopAsync 当前没有需要停止的真实隧道资源。",
            Suggestion = "真实实现时应确保释放 socket、取消后台任务并清理状态。",
            RequiresAdministrator = false
        });
    }
}

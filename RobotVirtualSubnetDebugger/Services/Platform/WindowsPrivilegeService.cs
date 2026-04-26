using System.Security.Principal;
using RobotNet.Windows.Wpf.Models;

namespace RobotNet.Windows.Wpf.Services.Platform;

public sealed class WindowsPrivilegeService : IPrivilegeService
{
    public bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public PlatformOperationResult CheckAdministrator()
    {
        var isAdministrator = IsRunningAsAdministrator();
        return new PlatformOperationResult
        {
            Name = "管理员权限检查",
            Status = isAdministrator ? DiagnosticStatus.Success : DiagnosticStatus.Info,
            Message = isAdministrator
                ? "当前进程已使用管理员权限运行。"
                : "当前进程未使用管理员权限运行，第一阶段 MVP 不需要管理员权限。",
            Suggestion = isAdministrator
                ? "后续执行真实虚拟网卡、路由、NAT 操作时具备权限基础。"
                : "后续涉及真实系统网络修改时，需要增加显式提权流程。",
            RequiresAdministrator = false
        };
    }
}

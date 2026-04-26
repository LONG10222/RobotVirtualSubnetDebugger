using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using RobotNet.Windows.Wpf.Models;
using RobotNet.Windows.Wpf.Services.Logging;

namespace RobotNet.Windows.Wpf.Services.Platform;

public sealed class WindowsAdminElevationService : IAdminElevationService
{
    private const string NoElevateArgument = "--robotnet-no-elevate";
    private readonly ILogService _logService;

    public WindowsAdminElevationService(ILogService logService)
    {
        _logService = logService;
    }

    public bool IsReadOnlyMode { get; private set; }

    public PlatformOperationResult CheckAdminPrivilege()
    {
        var isAdmin = IsAdministrator();
        return new PlatformOperationResult
        {
            Name = "管理员权限",
            Status = isAdmin ? DiagnosticStatus.Success : DiagnosticStatus.Warning,
            Message = isAdmin ? "管理员模式：已启用。" : "管理员模式：未启用，请重新启动。",
            Suggestion = isAdmin
                ? "可以在程序内应用网络配置。"
                : "当前只能执行只读诊断，不能修改网卡、路由、NAT 或转发设置。",
            RequiresAdministrator = true
        };
    }

    public AdminElevationResult EnsureRunningAsAdmin(string[] args)
    {
        if (IsAdministrator())
        {
            IsReadOnlyMode = false;
            return new AdminElevationResult
            {
                IsAdministrator = true,
                Message = "管理员模式：已启用。"
            };
        }

        if (args.Any(arg => string.Equals(arg, NoElevateArgument, StringComparison.OrdinalIgnoreCase)))
        {
            IsReadOnlyMode = true;
            return new AdminElevationResult
            {
                IsAdministrator = false,
                IsReadOnlyMode = true,
                Message = "管理员模式未启用，当前为只读/诊断模式。"
            };
        }

        try
        {
            var executablePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                throw new InvalidOperationException("无法确定当前程序路径。");
            }

            var arguments = string.Join(" ", args.Select(QuoteArgument));
            Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = arguments,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            });

            _logService.Info("已请求 UAC 管理员权限，当前进程将退出。");
            return new AdminElevationResult
            {
                RelaunchStarted = true,
                Message = "已请求管理员权限，正在重新启动。"
            };
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            IsReadOnlyMode = true;
            _logService.Warning("用户取消了 UAC 提权，进入只读/诊断模式。");
            return new AdminElevationResult
            {
                IsReadOnlyMode = true,
                Message = "用户取消了管理员权限请求，当前为只读/诊断模式。"
            };
        }
        catch (Exception ex)
        {
            IsReadOnlyMode = true;
            _logService.Error("请求管理员权限失败，进入只读/诊断模式。", ex);
            return new AdminElevationResult
            {
                IsReadOnlyMode = true,
                Message = $"请求管理员权限失败：{ex.Message}"
            };
        }
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string QuoteArgument(string argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            return "\"\"";
        }

        return argument.Contains(' ', StringComparison.Ordinal) || argument.Contains('"', StringComparison.Ordinal)
            ? $"\"{argument.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : argument;
    }
}

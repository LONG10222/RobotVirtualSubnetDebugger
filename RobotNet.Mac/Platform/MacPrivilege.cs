using System.Diagnostics;
using System.Runtime.InteropServices;
using RobotNet.Windows.Wpf.Models;
using RobotNet.Windows.Wpf.Services.Platform;
using RobotNet.Windows.Wpf.Utils;

namespace RobotNet.Mac.Platform;

internal sealed class MacPrivilege : IPrivilegeService, IAdminElevationService
{
    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern uint getuid();

    public static uint UserId => getuid();
    public bool IsReadOnlyMode => false;
    public bool IsRunningAsAdministrator() => UserId == 0;
    public PlatformOperationResult CheckAdminPrivilege() => CheckAdministrator();
    public PlatformOperationResult CheckAdministrator() => new()
    {
        Name = "macOS 管理员授权",
        Status = DiagnosticStatus.Info,
        Message = "仅在应用或回滚系统路由、网口和 NAT 时请求管理员授权。",
        Suggestion = "设备发现和 TCP 代理可以直接使用。"
    };

    public AdminElevationResult EnsureRunningAsAdmin(string[] args) => new()
    {
        IsAdministrator = IsRunningAsAdministrator(),
        Message = CheckAdministrator().Message
    };

    public static async Task<string> RunAsync(string command)
    {
        const string authorization = """
            on run argv
                return do shell script (item 1 of argv) with administrator privileges
            end run
            """;
        using var process = Process.Start(new ProcessStartInfo("/usr/bin/osascript")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            ArgumentList = { "-e", authorization, "--", command }
        }) ?? throw new InvalidOperationException("无法启动 macOS 授权对话框。");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().ConfigureAwait(false);
        var error = (await errorTask.ConfigureAwait(false)).Trim();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(error.Contains("(-128)", StringComparison.Ordinal)
                ? "已取消 macOS 管理员授权。"
                : error.Length == 0 ? "macOS 网络命令失败。" : error);
        return (await outputTask.ConfigureAwait(false)).Trim();
    }

    public static string Quote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
}

internal sealed class MacDeviceIdentity : IDeviceIdentityService
{
    public string GetOrCreateDeviceId()
    {
        var path = Path.Combine(AppPaths.AppDataDirectory, "device.id");
        if (File.Exists(path) && Guid.TryParseExact(File.ReadAllText(path).Trim(), "N", out var id))
            return id.ToString("N");
        var created = Guid.NewGuid().ToString("N");
        File.WriteAllText(path, created);
        return created;
    }

    public string GetComputerName() => Environment.MachineName;
}

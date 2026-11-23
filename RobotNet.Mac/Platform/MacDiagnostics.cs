using System.Security.Cryptography;
using RobotNet.Windows.Wpf.Models;
using RobotNet.Windows.Wpf.Services.Platform;

namespace RobotNet.Mac.Platform;

internal sealed class MacDiagnostics : IRouteService, ITunnelService, IVirtualAdapterService
{
    private const string RoutePage = "请在虚拟网段页面预览、应用或回滚 macOS 系统配置。";

    Task<PlatformOperationResult> IRouteService.CheckAsync(AppConfig config) =>
        Info("目标网段路由", RoutePage);

    public Task<PlatformOperationResult> ApplyAsync(AppConfig config) =>
        Info("目标网段路由", RoutePage);

    Task<PlatformOperationResult> IVirtualAdapterService.CheckAsync(AppConfig config) =>
        Info("虚拟网卡", "当前虚拟网段使用 IPv4 路由和 NAT，虚拟网卡功能未接入。");

    public Task<PlatformOperationResult> EnsureAsync(AppConfig config) =>
        ((IVirtualAdapterService)this).CheckAsync(config);

    Task<PlatformOperationResult> ITunnelService.CheckAsync(AppConfig config) =>
        Info("TCP 代理加密", AesGcm.IsSupported ? "系统支持 AES-GCM。" : "当前系统缺少 AES-GCM 支持。",
            AesGcm.IsSupported ? DiagnosticStatus.Success : DiagnosticStatus.Error);

    public Task<PlatformOperationResult> StartAsync(AppConfig config) =>
        Info("TCP 代理", "请在 TCP 代理页面启动服务。");

    public Task<PlatformOperationResult> StopAsync() =>
        Info("TCP 代理", "请在 TCP 代理页面停止服务。");

    private static Task<PlatformOperationResult> Info(string name, string message, DiagnosticStatus status = DiagnosticStatus.Info) =>
        Task.FromResult(new PlatformOperationResult { Name = name, Message = message, Status = status });
}

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using RobotNet.Mac.Platform;
using RobotNet.Windows.Wpf.Models;
using RobotNet.Windows.Wpf.Services.Configuration;
using RobotNet.Windows.Wpf.Services.CrashReporting;
using RobotNet.Windows.Wpf.Services.Diagnostics;
using RobotNet.Windows.Wpf.Services.Discovery;
using RobotNet.Windows.Wpf.Services.Logging;
using RobotNet.Windows.Wpf.Services.Platform;
using RobotNet.Windows.Wpf.Services.Proxy;
using RobotNet.Windows.Wpf.Services.Tunnel;
using RobotNet.Windows.Wpf.Services.Updates;
using RobotNet.Windows.Wpf.ViewModels;

namespace RobotNet.Mac;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var logs = new SimpleLogService();
            var crashes = new FileCrashReportService(logs);
            crashes.RegisterGlobalHandlers();
            var privilege = new MacPrivilege();
            var adapters = new MacNetworkAdapters();
            var ports = new MacPortAvailability();
            var configuration = new JsonConfigurationService(new MacDeviceIdentity(), logs);
            var startupConfig = configuration.Load();
            if (startupConfig.Role == DeviceRole.Unknown &&
                string.IsNullOrWhiteSpace(startupConfig.SharedKey) &&
                adapters.GetAdapters().Where(adapter => !adapter.IsVirtual && adapter.Status == "Up")
                    .Select(adapter => adapter.Name).Distinct().Take(2).Count() == 1)
            {
                startupConfig.Role = DeviceRole.DebugClient;
                startupConfig.EnableTcpProxyMode = true;
                startupConfig.EnableVirtualSubnetMode = false;
                configuration.Save(startupConfig);
            }

            var sessions = new SimulatedTunnelSessionService(logs);
            var discovery = new UdpDiscoveryService(configuration, adapters, logs, sessions);
            var proxy = new SafeTcpProxyService(ports, logs);
            var plans = new MacNetworkPlan(adapters);
            var executor = new MacNetworkExecutor(plans, new FileOperationRollbackService(logs), proxy, discovery, logs);
            var checks = new MacDiagnostics();
            var diagnostics = new BasicDiagnosticService(adapters, privilege, checks, checks, checks, proxy, plans, ports, logs);
            var updates = new GitHubReleaseUpdateService(logs);
            var preflight = new ConnectionPreflightService(ports, logs);
            var window = new MainWindow();
            var installer = new MacUpdateInstaller(proxy, executor, () => desktop.Shutdown());
            window.DataContext = new MainViewModel(adapters, configuration, discovery, diagnostics, sessions, proxy, plans,
                executor, privilege, new MacConfirmation(window), updates, crashes, preflight, logs, installer);
            desktop.MainWindow = window;
            if (startupConfig.EnableUpdateCheckOnStartup)
                _ = CheckForUpdatesAsync(configuration, updates, logs);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task CheckForUpdatesAsync(IConfigurationService configuration, IUpdateService updates, ILogService logs)
    {
        try
        {
            var result = await updates.CheckForUpdatesAsync(configuration.Load()).ConfigureAwait(false);
            if (result.IsUpdateAvailable)
                logs.Audit($"发现 macOS 新版本 {result.LatestVersion}：{result.ReleaseUrl}");
        }
        catch (Exception ex)
        {
            logs.Warning($"后台更新检查失败：{ex.Message}");
        }
    }
}

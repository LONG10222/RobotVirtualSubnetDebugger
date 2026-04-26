using System.Windows;
using RobotNet.Windows.Wpf.Services.Configuration;
using RobotNet.Windows.Wpf.Services.CrashReporting;
using RobotNet.Windows.Wpf.Services.Diagnostics;
using RobotNet.Windows.Wpf.Services.Discovery;
using RobotNet.Windows.Wpf.Services.Logging;
using RobotNet.Windows.Wpf.Services.Platform;
using RobotNet.Windows.Wpf.Services.Proxy;
using RobotNet.Windows.Wpf.Services.Tunnel;
using RobotNet.Windows.Wpf.Services.Ui;
using RobotNet.Windows.Wpf.Services.Updates;
using RobotNet.Windows.Wpf.ViewModels;

namespace RobotNet.Windows.Wpf;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var logService = new SimpleLogService();
        var adminElevationService = new WindowsAdminElevationService(logService);
        var elevationResult = adminElevationService.EnsureRunningAsAdmin(e.Args);
        if (elevationResult.RelaunchStarted)
        {
            Shutdown();
            return;
        }

        var crashReportService = new FileCrashReportService(logService);
        crashReportService.RegisterGlobalHandlers();
        var identityService = new WindowsDeviceIdentityService();
        var networkAdapterService = new WindowsNetworkAdapterService();
        var privilegeService = new WindowsPrivilegeService();
        var routeApplyService = new WindowsRouteApplyService();
        var natApplyService = new WindowsNatApplyService();
        var adapterIpConfigurationService = new WindowsAdapterIpConfigurationService();
        var virtualAdapterService = new WindowsVirtualAdapterService();
        var routeService = new WindowsRouteService();
        var tunnelService = new WindowsTunnelService();
        var portAvailabilityService = new WindowsPortAvailabilityService(logService);
        var virtualSubnetService = new WindowsVirtualSubnetService(
            networkAdapterService,
            privilegeService,
            routeApplyService,
            natApplyService,
            adapterIpConfigurationService,
            logService);
        var tcpProxyService = new SafeTcpProxyService(portAvailabilityService, logService);
        var tcpProxyServiceAdapter = new TcpProxyServiceAdapter(tcpProxyService);
        var rollbackService = new FileOperationRollbackService(logService);
        var networkConfigurationExecutor = new WindowsNetworkConfigurationExecutor(
            virtualSubnetService,
            adminElevationService,
            rollbackService,
            tcpProxyServiceAdapter,
            logService);
        var connectionPreflightService = new ConnectionPreflightService(portAvailabilityService, logService);
        var configurationService = new JsonConfigurationService(identityService, logService);
        var userConfirmationService = new WpfUserConfirmationService();
        var updateService = new GitHubReleaseUpdateService(logService);
        var tunnelSessionService = new SimulatedTunnelSessionService(logService);
        var discoveryService = new UdpDiscoveryService(configurationService, networkAdapterService, logService, tunnelSessionService);
        var diagnosticService = new BasicDiagnosticService(
            networkAdapterService,
            privilegeService,
            virtualAdapterService,
            routeService,
            tunnelService,
            tcpProxyService,
            virtualSubnetService,
            portAvailabilityService,
            logService);

        logService.Info("应用启动。");

        var mainWindow = new MainWindow
        {
            DataContext = new MainViewModel(
                networkAdapterService,
                configurationService,
                discoveryService,
                diagnosticService,
                tunnelSessionService,
                tcpProxyService,
                virtualSubnetService,
                networkConfigurationExecutor,
                adminElevationService,
                userConfirmationService,
                updateService,
                crashReportService,
                connectionPreflightService,
                logService)
        };

        mainWindow.Show();
        StartBackgroundUpdateCheck(configurationService, updateService, logService);
    }

    private static void StartBackgroundUpdateCheck(
        IConfigurationService configurationService,
        IUpdateService updateService,
        ILogService logService)
    {
        var config = configurationService.Load();
        if (!config.EnableUpdateCheckOnStartup)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            var result = await updateService.CheckForUpdatesAsync(config);
            if (result.IsUpdateAvailable)
            {
                logService.Audit($"启动检查发现新版本：{result.LatestVersion}，发布页：{result.ReleaseUrl}");
            }
        });
    }
}

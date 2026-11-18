using System.Collections.ObjectModel;
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
using RobotNet.Windows.Wpf.Utils;

namespace RobotNet.Windows.Wpf.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private NavigationItemViewModel? _selectedNavigationItem;
    private object? _currentViewModel;

    public MainViewModel(
        INetworkAdapterService networkAdapterService,
        IConfigurationService configurationService,
        IDiscoveryService discoveryService,
        IDiagnosticService diagnosticService,
        ITunnelSessionService tunnelSessionService,
        ITcpProxyService tcpProxyService,
        IVirtualSubnetService virtualSubnetService,
        INetworkConfigurationExecutor networkConfigurationExecutor,
        IAdminElevationService adminElevationService,
        IUserConfirmationService userConfirmationService,
        IUpdateService updateService,
        ICrashReportService crashReportService,
        IConnectionPreflightService connectionPreflightService,
        ILogService logService)
    {
        var dashboardViewModel = new DashboardViewModel(configurationService, networkAdapterService, adminElevationService);
        var tutorialViewModel = new TutorialViewModel();
        var easyModeViewModel = new EasyModeViewModel(
            configurationService,
            networkAdapterService,
            discoveryService,
            networkConfigurationExecutor,
            connectionPreflightService,
            userConfirmationService,
            logService);
        var adaptersViewModel = new AdaptersViewModel(networkAdapterService);
        var discoveryViewModel = new DiscoveryViewModel(configurationService, discoveryService, tunnelSessionService, connectionPreflightService, logService);
        var sessionViewModel = new SessionViewModel(configurationService, tunnelSessionService, connectionPreflightService);
        var tcpProxyViewModel = new TcpProxyViewModel(configurationService, connectionPreflightService, tcpProxyService);
        var virtualSubnetViewModel = new VirtualSubnetViewModel(
            configurationService,
            virtualSubnetService,
            networkConfigurationExecutor,
            userConfirmationService);
        var productionViewModel = new ProductionViewModel(configurationService, updateService, crashReportService, logService);
        var settingsViewModel = new SettingsViewModel(configurationService, logService);
        var diagnosticsViewModel = new DiagnosticsViewModel(configurationService, diagnosticService);
        var logsViewModel = new LogsViewModel(logService);

        settingsViewModel.ConfigSaved += (_, _) =>
        {
            dashboardViewModel.Refresh();
            easyModeViewModel.ReloadSavedConfiguration();
        };

        NavigationItems =
        [
            new NavigationItemViewModel("Dashboard", "状态总览", dashboardViewModel, true),
            new NavigationItemViewModel("EasyMode", "连接向导（从这里开始）", easyModeViewModel, true),
            new NavigationItemViewModel("Tutorial", "新手教程", tutorialViewModel, true),
            new NavigationItemViewModel("Adapters", "查看网卡与 IP", adaptersViewModel, true),
            new NavigationItemViewModel("Discovery", "查找另一台电脑", discoveryViewModel, true),
            new NavigationItemViewModel("Session", "连接状态", sessionViewModel, true),
            new NavigationItemViewModel("TcpProxy", "单端口代理（高级）", tcpProxyViewModel, true),
            new NavigationItemViewModel("VirtualSubnet", "原 IP 路由（高级）", virtualSubnetViewModel, true),
            new NavigationItemViewModel("Production", "版本与更新", productionViewModel, true),
            new NavigationItemViewModel("Settings", "高级设置", settingsViewModel, true),
            new NavigationItemViewModel("Diagnostics", "连接故障排查", diagnosticsViewModel, true),
            new NavigationItemViewModel("Logs", "运行日志", logsViewModel, true)
        ];

        SelectedNavigationItem = NavigationItems[1];
    }

    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }

    public NavigationItemViewModel? SelectedNavigationItem
    {
        get => _selectedNavigationItem;
        set
        {
            if (value is null || !value.IsEnabled)
            {
                return;
            }

            if (SetProperty(ref _selectedNavigationItem, value))
            {
                CurrentViewModel = value.ViewModel;
            }
        }
    }

    public object? CurrentViewModel
    {
        get => _currentViewModel;
        private set => SetProperty(ref _currentViewModel, value);
    }
}

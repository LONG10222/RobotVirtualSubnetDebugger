using System.Collections.ObjectModel;
using RobotNet.Windows.Wpf.Services.Configuration;
using RobotNet.Windows.Wpf.Services.CrashReporting;
using RobotNet.Windows.Wpf.Services.Diagnostics;
using RobotNet.Windows.Wpf.Services.Discovery;
using RobotNet.Windows.Wpf.Services.Logging;
using RobotNet.Windows.Wpf.Services.Platform;
using RobotNet.Windows.Wpf.Services.Proxy;
using RobotNet.Windows.Wpf.Services.Tunnel;
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
        IUpdateService updateService,
        ICrashReportService crashReportService,
        IConnectionPreflightService connectionPreflightService,
        ILogService logService)
    {
        var dashboardViewModel = new DashboardViewModel(configurationService, networkAdapterService);
        var tutorialViewModel = new TutorialViewModel();
        var adaptersViewModel = new AdaptersViewModel(networkAdapterService);
        var discoveryViewModel = new DiscoveryViewModel(configurationService, discoveryService, tunnelSessionService, connectionPreflightService, logService);
        var sessionViewModel = new SessionViewModel(configurationService, tunnelSessionService, connectionPreflightService);
        var tcpProxyViewModel = new TcpProxyViewModel(configurationService, connectionPreflightService, tcpProxyService);
        var virtualSubnetViewModel = new VirtualSubnetViewModel(configurationService, virtualSubnetService);
        var productionViewModel = new ProductionViewModel(configurationService, updateService, crashReportService, logService);
        var settingsViewModel = new SettingsViewModel(configurationService, logService);
        var diagnosticsViewModel = new DiagnosticsViewModel(configurationService, diagnosticService);
        var logsViewModel = new LogsViewModel(logService);

        settingsViewModel.ConfigSaved += (_, _) => dashboardViewModel.Refresh();

        NavigationItems =
        [
            new NavigationItemViewModel("Dashboard", "首页 Dashboard", dashboardViewModel, true),
            new NavigationItemViewModel("Tutorial", "操作教程 Tutorial", tutorialViewModel, true),
            new NavigationItemViewModel("Adapters", "网卡管理 Adapters", adaptersViewModel, true),
            new NavigationItemViewModel("Discovery", "设备发现 Discovery", discoveryViewModel, true),
            new NavigationItemViewModel("Session", "连接会话 Session", sessionViewModel, true),
            new NavigationItemViewModel("TcpProxy", "TCP 代理 Proxy", tcpProxyViewModel, true),
            new NavigationItemViewModel("VirtualSubnet", "虚拟网段 Virtual Subnet", virtualSubnetViewModel, true),
            new NavigationItemViewModel("Production", "发布更新 Release", productionViewModel, true),
            new NavigationItemViewModel("Settings", "配置 Settings", settingsViewModel, true),
            new NavigationItemViewModel("Diagnostics", "诊断 Diagnostics", diagnosticsViewModel, true),
            new NavigationItemViewModel("Logs", "日志 Logs", logsViewModel, true)
        ];

        SelectedNavigationItem = NavigationItems[0];
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

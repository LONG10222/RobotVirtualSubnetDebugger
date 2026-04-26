using System.Collections.ObjectModel;
using RobotNet.Windows.Wpf.Commands;
using RobotNet.Windows.Wpf.Models;
using RobotNet.Windows.Wpf.Services.Configuration;
using RobotNet.Windows.Wpf.Services.Platform;
using RobotNet.Windows.Wpf.Utils;

namespace RobotNet.Windows.Wpf.ViewModels;

public sealed class VirtualSubnetViewModel : ObservableObject
{
    private readonly IConfigurationService _configurationService;
    private readonly IVirtualSubnetService _virtualSubnetService;
    private VirtualSubnetPlan _plan = new();
    private string _scriptMessage = string.Empty;
    private string _applyScriptPath = string.Empty;
    private string _rollbackScriptPath = string.Empty;

    public VirtualSubnetViewModel(
        IConfigurationService configurationService,
        IVirtualSubnetService virtualSubnetService)
    {
        _configurationService = configurationService;
        _virtualSubnetService = virtualSubnetService;
        RefreshCommand = new RelayCommand(Refresh);
        ExportScriptsCommand = new RelayCommand(ExportScripts);
        Refresh();
    }

    public RelayCommand RefreshCommand { get; }

    public RelayCommand ExportScriptsCommand { get; }

    public ObservableCollection<string> Warnings { get; } = [];

    public ObservableCollection<VirtualSubnetOperationStep> Steps { get; } = [];

    public string StatusText => _plan.StatusText;

    public string StatusColor => _plan.StatusColor;

    public string Summary => _plan.Summary;

    public string ModeText => _plan.Mode switch
    {
        VirtualSubnetMode.RouteNat => "目标网段精确路由 + 网关 NAT",
        VirtualSubnetMode.TunRouteNat => "Wintun/TUN + 路由/NAT",
        VirtualSubnetMode.TapBridge => "TAP/桥接",
        _ => _plan.Mode.ToString()
    };

    public string RoleText => _plan.Role switch
    {
        DeviceRole.DebugClient => "调试端",
        DeviceRole.GatewayAgent => "网关端",
        _ => "未知"
    };

    public string TargetSubnetCidr => EmptyToDash(_plan.TargetSubnetCidr);

    public string LanSubnetCidr => EmptyToDash(_plan.LanSubnetCidr);

    public string LanInterfaceName => EmptyToDash(_plan.LanInterfaceName);

    public string TargetInterfaceName => EmptyToDash(_plan.TargetInterfaceName);

    public string GatewayLanIp => EmptyToDash(_plan.GatewayLanIp);

    public string TargetDeviceIp => EmptyToDash(_plan.TargetDeviceIp);

    public string RouteSafetyText => _plan.PreservesDefaultRoute && _plan.PreservesDns
        ? "保留默认路由和 DNS，不接管 WiFi/VPN 上网"
        : "存在路由或 DNS 风险，需要人工复核";

    public string ScriptMessage
    {
        get => _scriptMessage;
        private set => SetProperty(ref _scriptMessage, value);
    }

    public string ApplyScriptPath
    {
        get => _applyScriptPath;
        private set => SetProperty(ref _applyScriptPath, value);
    }

    public string RollbackScriptPath
    {
        get => _rollbackScriptPath;
        private set => SetProperty(ref _rollbackScriptPath, value);
    }

    private void Refresh()
    {
        _plan = _virtualSubnetService.BuildPlan(_configurationService.Load());
        Warnings.Clear();
        foreach (var warning in _plan.Warnings)
        {
            Warnings.Add(warning);
        }

        Steps.Clear();
        foreach (var step in _plan.Steps.OrderBy(step => step.Order))
        {
            Steps.Add(step);
        }

        ScriptMessage = string.Empty;
        ApplyScriptPath = string.Empty;
        RollbackScriptPath = string.Empty;
        RaisePlanPropertiesChanged();
    }

    private void ExportScripts()
    {
        var result = _virtualSubnetService.ExportScripts(_plan);
        ScriptMessage = result.Message;
        ApplyScriptPath = result.ApplyScriptPath;
        RollbackScriptPath = result.RollbackScriptPath;
    }

    private void RaisePlanPropertiesChanged()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusColor));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(ModeText));
        OnPropertyChanged(nameof(RoleText));
        OnPropertyChanged(nameof(TargetSubnetCidr));
        OnPropertyChanged(nameof(LanSubnetCidr));
        OnPropertyChanged(nameof(LanInterfaceName));
        OnPropertyChanged(nameof(TargetInterfaceName));
        OnPropertyChanged(nameof(GatewayLanIp));
        OnPropertyChanged(nameof(TargetDeviceIp));
        OnPropertyChanged(nameof(RouteSafetyText));
    }

    private static string EmptyToDash(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "-" : value;
    }
}

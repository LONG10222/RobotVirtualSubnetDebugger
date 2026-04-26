using System.Collections.ObjectModel;
using System.Windows;
using RobotNet.Windows.Wpf.Commands;
using RobotNet.Windows.Wpf.Models;
using RobotNet.Windows.Wpf.Services.Configuration;
using RobotNet.Windows.Wpf.Services.Platform;
using RobotNet.Windows.Wpf.Services.Ui;
using RobotNet.Windows.Wpf.Utils;

namespace RobotNet.Windows.Wpf.ViewModels;

public sealed class VirtualSubnetViewModel : ObservableObject
{
    private readonly IConfigurationService _configurationService;
    private readonly IVirtualSubnetService _virtualSubnetService;
    private readonly INetworkConfigurationExecutor _networkConfigurationExecutor;
    private readonly IUserConfirmationService _userConfirmationService;
    private VirtualSubnetPlan _plan = new();
    private string _scriptMessage = string.Empty;
    private string _applyScriptPath = string.Empty;
    private string _rollbackScriptPath = string.Empty;
    private string _executionStatus = "未配置";
    private string _lastExecutionTime = "-";

    public VirtualSubnetViewModel(
        IConfigurationService configurationService,
        IVirtualSubnetService virtualSubnetService,
        INetworkConfigurationExecutor networkConfigurationExecutor,
        IUserConfirmationService userConfirmationService)
    {
        _configurationService = configurationService;
        _virtualSubnetService = virtualSubnetService;
        _networkConfigurationExecutor = networkConfigurationExecutor;
        _userConfirmationService = userConfirmationService;
        RefreshCommand = new RelayCommand(Refresh);
        ApplyCommand = new AsyncRelayCommand(ApplyAsync);
        RollbackCommand = new AsyncRelayCommand(RollbackAsync);
        ExportScriptsCommand = new RelayCommand(ExportScripts);
        _networkConfigurationExecutor.ExecutionLogAdded += (_, entry) => RunOnUiThread(() => ExecutionLogs.Add(entry));
        Refresh();
    }

    public RelayCommand RefreshCommand { get; }

    public AsyncRelayCommand ApplyCommand { get; }

    public AsyncRelayCommand RollbackCommand { get; }

    public RelayCommand ExportScriptsCommand { get; }

    public ObservableCollection<string> Warnings { get; } = [];

    public ObservableCollection<VirtualSubnetOperationStep> Steps { get; } = [];

    public ObservableCollection<string> ExecutionLogs { get; } = [];

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

    public string ExecutionStatus
    {
        get => _executionStatus;
        private set => SetProperty(ref _executionStatus, value);
    }

    public string LastExecutionTime
    {
        get => _lastExecutionTime;
        private set => SetProperty(ref _lastExecutionTime, value);
    }

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
        var last = _networkConfigurationExecutor.GetLastOperation();
        if (last is not null)
        {
            ExecutionStatus = last.Status switch
            {
                NetworkConfigurationApplyStatus.Previewed => "已预览",
                NetworkConfigurationApplyStatus.Applying => "应用中",
                NetworkConfigurationApplyStatus.Applied => "已应用",
                NetworkConfigurationApplyStatus.Failed => "应用失败",
                NetworkConfigurationApplyStatus.RolledBack => "已回滚",
                _ => "未配置"
            };
            LastExecutionTime = (last.AppliedAt ?? last.RolledBackAt ?? last.CreatedAt).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
            ExecutionLogs.Clear();
            foreach (var log in last.ExecutionLog.TakeLast(100))
            {
                ExecutionLogs.Add(log);
            }
        }
        else
        {
            ExecutionStatus = "已预览";
        }

        RaisePlanPropertiesChanged();
    }

    private async Task ApplyAsync()
    {
        var config = _configurationService.Load();
        if (config.Role == DeviceRole.Unknown)
        {
            ScriptMessage = "请先在高级模式 Settings 中选择本机角色。";
            ExecutionStatus = "应用失败";
            return;
        }

        Refresh();
        if (!_userConfirmationService.Confirm(
                "确认一键应用网络配置",
                $"即将在程序内应用当前虚拟网段计划：{Summary}{Environment.NewLine}{Environment.NewLine}程序会执行上方命令预览中的系统网络配置，并写入审计日志和回滚记录。不会修改默认路由、DNS，也不会关闭 WiFi 或 VPN。是否继续？"))
        {
            ScriptMessage = "已取消应用，未修改网络配置。";
            return;
        }

        ExecutionStatus = "应用中";
        ExecutionLogs.Clear();
        var result = config.Role == DeviceRole.GatewayAgent
            ? await _networkConfigurationExecutor.ApplyGatewayConfigurationAsync(config)
            : await _networkConfigurationExecutor.ApplyClientConfigurationAsync(config);
        ExecutionStatus = result.Status switch
        {
            NetworkConfigurationApplyStatus.Applied => "已应用",
            NetworkConfigurationApplyStatus.Failed => "应用失败",
            _ => result.Status.ToString()
        };
        LastExecutionTime = result.ExecutedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        ScriptMessage = result.Message;
        AddResultLogs(result);
    }

    private async Task RollbackAsync()
    {
        if (!_userConfirmationService.Confirm(
                "确认回滚网络配置",
                "将回滚本程序上一次应用的网络配置记录。请确认当前没有其他工具依赖这些规则。是否继续？"))
        {
            ScriptMessage = "已取消回滚。";
            return;
        }

        ExecutionStatus = "应用中";
        var result = await _networkConfigurationExecutor.RollbackLastConfigurationAsync();
        ExecutionStatus = result.Status == NetworkConfigurationApplyStatus.RolledBack ? "已回滚" : "应用失败";
        LastExecutionTime = result.ExecutedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        ScriptMessage = result.Message;
        AddResultLogs(result);
    }

    private void ExportScripts()
    {
        var result = _virtualSubnetService.ExportScripts(_plan);
        ScriptMessage = result.Message;
        ApplyScriptPath = result.ApplyScriptPath;
        RollbackScriptPath = result.RollbackScriptPath;
    }

    private void AddResultLogs(NetworkConfigurationExecutionResult result)
    {
        foreach (var log in result.Logs)
        {
            if (!ExecutionLogs.Contains(log))
            {
                ExecutionLogs.Add(log);
            }
        }

        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            ExecutionLogs.Add(result.Error);
        }
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
        OnPropertyChanged(nameof(ExecutionStatus));
        OnPropertyChanged(nameof(LastExecutionTime));
    }

    private static string EmptyToDash(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "-" : value;
    }

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }
}

using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using RobotNet.Windows.Wpf.Commands;
using RobotNet.Windows.Wpf.Models;
using RobotNet.Windows.Wpf.Services.Configuration;
using RobotNet.Windows.Wpf.Services.Diagnostics;
using RobotNet.Windows.Wpf.Services.Discovery;
using RobotNet.Windows.Wpf.Services.Logging;
using RobotNet.Windows.Wpf.Services.Platform;
using RobotNet.Windows.Wpf.Services.Ui;
using RobotNet.Windows.Wpf.Utils;

namespace RobotNet.Windows.Wpf.ViewModels;

public sealed class EasyModeViewModel : ObservableObject
{
    private readonly IConfigurationService _configurationService;
    private readonly INetworkAdapterService _networkAdapterService;
    private readonly IDiscoveryService _discoveryService;
    private readonly INetworkConfigurationExecutor _networkConfigurationExecutor;
    private readonly IConnectionPreflightService _connectionPreflightService;
    private readonly IUserConfirmationService _userConfirmationService;
    private readonly ILogService _logService;
    private DeviceRole _selectedRole;
    private DeviceInfo? _selectedGateway;
    private string _targetDeviceIp = "192.168.1.10";
    private int _targetDevicePort = 30003;
    private string _targetDeviceAdapterIp = "192.168.1.100";
    private string _virtualSubnetMask = "255.255.255.0";
    private int _localListenPort = 30003;
    private string _gatewayLanIp = string.Empty;
    private string _status = "请选择这台电脑的角色。";
    private string _planSummary = string.Empty;
    private NetworkConfigurationApplyStatus _applyStatus = NetworkConfigurationApplyStatus.NotConfigured;

    public EasyModeViewModel(
        IConfigurationService configurationService,
        INetworkAdapterService networkAdapterService,
        IDiscoveryService discoveryService,
        INetworkConfigurationExecutor networkConfigurationExecutor,
        IConnectionPreflightService connectionPreflightService,
        IUserConfirmationService userConfirmationService,
        ILogService logService)
    {
        _configurationService = configurationService;
        _networkAdapterService = networkAdapterService;
        _discoveryService = discoveryService;
        _networkConfigurationExecutor = networkConfigurationExecutor;
        _connectionPreflightService = connectionPreflightService;
        _userConfirmationService = userConfirmationService;
        _logService = logService;

        SelectGatewayRoleCommand = new RelayCommand(SelectGatewayRole);
        SelectClientRoleCommand = new RelayCommand(SelectClientRole);
        PreviewCommand = new RelayCommand(Preview);
        RefreshGatewaysCommand = new AsyncRelayCommand(RefreshGatewaysAsync);
        ApplyGatewayCommand = new AsyncRelayCommand(ApplyGatewayAsync);
        ApplyClientCommand = new AsyncRelayCommand(ApplyClientAsync);
        RollbackCommand = new AsyncRelayCommand(RollbackAsync);

        _discoveryService.DeviceDiscovered += OnDeviceDiscovered;
        _networkConfigurationExecutor.ExecutionLogAdded += OnExecutionLogAdded;
        LoadFromConfig();
        DetectAdapters();
    }

    public RelayCommand SelectGatewayRoleCommand { get; }

    public RelayCommand SelectClientRoleCommand { get; }

    public RelayCommand PreviewCommand { get; }

    public AsyncRelayCommand RefreshGatewaysCommand { get; }

    public AsyncRelayCommand ApplyGatewayCommand { get; }

    public AsyncRelayCommand ApplyClientCommand { get; }

    public AsyncRelayCommand RollbackCommand { get; }

    public ObservableCollection<DeviceInfo> Gateways { get; } = [];

    public ObservableCollection<VirtualSubnetOperationStep> PreviewSteps { get; } = [];

    public ObservableCollection<string> ExecutionLogs { get; } = [];

    public DeviceRole SelectedRole
    {
        get => _selectedRole;
        private set
        {
            if (SetProperty(ref _selectedRole, value))
            {
                OnPropertyChanged(nameof(IsGatewayRole));
                OnPropertyChanged(nameof(IsClientRole));
                OnPropertyChanged(nameof(RoleText));
            }
        }
    }

    public bool IsGatewayRole => SelectedRole == DeviceRole.GatewayAgent;

    public bool IsClientRole => SelectedRole == DeviceRole.DebugClient;

    public string RoleText => SelectedRole switch
    {
        DeviceRole.GatewayAgent => "这台电脑连接硬件设备（主机 A / 网关端）",
        DeviceRole.DebugClient => "这台电脑运行控制代码（主机 B / 调试端）",
        _ => "尚未选择"
    };

    public DeviceInfo? SelectedGateway
    {
        get => _selectedGateway;
        set
        {
            if (SetProperty(ref _selectedGateway, value) && value is not null)
            {
                GatewayLanIp = value.LanIp;
            }
        }
    }

    public string TargetDeviceIp
    {
        get => _targetDeviceIp;
        set => SetProperty(ref _targetDeviceIp, value);
    }

    public int TargetDevicePort
    {
        get => _targetDevicePort;
        set => SetProperty(ref _targetDevicePort, value);
    }

    public string TargetDeviceAdapterIp
    {
        get => _targetDeviceAdapterIp;
        set => SetProperty(ref _targetDeviceAdapterIp, value);
    }

    public string VirtualSubnetMask
    {
        get => _virtualSubnetMask;
        set => SetProperty(ref _virtualSubnetMask, value);
    }

    public int LocalListenPort
    {
        get => _localListenPort;
        set => SetProperty(ref _localListenPort, value);
    }

    public string GatewayLanIp
    {
        get => _gatewayLanIp;
        set => SetProperty(ref _gatewayLanIp, value);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public string PlanSummary
    {
        get => _planSummary;
        private set => SetProperty(ref _planSummary, value);
    }

    public NetworkConfigurationApplyStatus ApplyStatus
    {
        get => _applyStatus;
        private set
        {
            if (SetProperty(ref _applyStatus, value))
            {
                OnPropertyChanged(nameof(ApplyStatusText));
            }
        }
    }

    public string ApplyStatusText => ApplyStatus switch
    {
        NetworkConfigurationApplyStatus.NotConfigured => "未配置",
        NetworkConfigurationApplyStatus.Previewed => "已预览",
        NetworkConfigurationApplyStatus.Applying => "应用中",
        NetworkConfigurationApplyStatus.Applied => "已应用",
        NetworkConfigurationApplyStatus.Failed => "应用失败",
        NetworkConfigurationApplyStatus.RolledBack => "已回滚",
        _ => ApplyStatus.ToString()
    };

    private void SelectGatewayRole()
    {
        SelectedRole = DeviceRole.GatewayAgent;
        DetectAdapters();
        Preview();
    }

    private void SelectClientRole()
    {
        SelectedRole = DeviceRole.DebugClient;
        DetectAdapters();
        _ = RefreshGatewaysAsync();
        Preview();
    }

    private void Preview()
    {
        var config = BuildConfigFromInput();
        var plan = SelectedRole == DeviceRole.GatewayAgent
            ? _networkConfigurationExecutor.PreviewGatewayConfiguration(config)
            : _networkConfigurationExecutor.PreviewClientConfiguration(config);

        PreviewSteps.Clear();
        foreach (var step in plan.Steps.OrderBy(step => step.Order))
        {
            PreviewSteps.Add(step);
        }

        PlanSummary = plan.Summary;
        ApplyStatus = NetworkConfigurationApplyStatus.Previewed;
        Status = plan.Status == DiagnosticStatus.Error
            ? $"预览失败：{string.Join("；", plan.Warnings)}"
            : "已生成操作预览。确认无误后点击一键按钮。";
    }

    private async Task RefreshGatewaysAsync()
    {
        var config = BuildConfigFromInput();
        config.Role = DeviceRole.DebugClient;
        _configurationService.Save(config);

        var preflight = _connectionPreflightService.EnsurePortsReady(config);
        if (preflight.ConfigChanged)
        {
            _configurationService.Save(config);
        }

        if (!preflight.CanContinue)
        {
            Status = preflight.Summary;
            return;
        }

        await _discoveryService.StartAsync();
        RefreshGatewayList();
        Status = $"正在发现主机 A，当前发现 {Gateways.Count} 台网关端。";
    }

    private async Task ApplyGatewayAsync()
    {
        var config = BuildConfigFromInput();
        config.Role = DeviceRole.GatewayAgent;
        SelectedRole = DeviceRole.GatewayAgent;
        Preview();
        if (!ConfirmNetworkChange("确认配置主机 A / 网关端"))
        {
            Status = "已取消应用，未修改网络配置。";
            return;
        }

        _configurationService.Save(config);
        ApplyStatus = NetworkConfigurationApplyStatus.Applying;
        Status = "正在配置网口并启动网关。";
        ExecutionLogs.Clear();

        var result = await _networkConfigurationExecutor.ApplyGatewayConfigurationAsync(config);
        ApplyStatus = result.Status;
        Status = result.Success
            ? "主机 A 已准备好，等待主机 B 连接。"
            : result.Message;
        AddResultLogs(result);
    }

    private async Task ApplyClientAsync()
    {
        var config = BuildConfigFromInput();
        config.Role = DeviceRole.DebugClient;
        SelectedRole = DeviceRole.DebugClient;
        Preview();
        if (!ConfirmNetworkChange("确认连接主机 A / 调试端"))
        {
            Status = "已取消应用，未修改网络配置。";
            return;
        }

        _configurationService.Save(config);
        ApplyStatus = NetworkConfigurationApplyStatus.Applying;
        Status = "正在连接主机 A 并启动调试通道。";
        ExecutionLogs.Clear();

        var result = await _networkConfigurationExecutor.ApplyClientConfigurationAsync(config);
        ApplyStatus = result.Status;
        Status = result.Success
            ? "主机 B 已连接，现在你的代码可以访问目标设备 IP/端口。"
            : result.Message;
        AddResultLogs(result);
    }

    private async Task RollbackAsync()
    {
        if (!_userConfirmationService.Confirm(
                "确认恢复网络配置",
                "将回滚本程序上一次应用的网络配置记录。此操作不会修改默认路由、DNS 或关闭网卡。是否继续？"))
        {
            Status = "已取消恢复网络配置。";
            return;
        }

        ApplyStatus = NetworkConfigurationApplyStatus.Applying;
        Status = "正在恢复网络配置。";
        var result = await _networkConfigurationExecutor.RollbackLastConfigurationAsync();
        ApplyStatus = result.Status;
        Status = result.Message;
        AddResultLogs(result);
    }

    private bool ConfirmNetworkChange(string title)
    {
        var modifyingSteps = PreviewSteps
            .Where(step => step.WillModifySystem)
            .OrderBy(step => step.Order)
            .Select(step => $"[{step.RiskLevel}] {step.Name}：{step.Description}")
            .ToList();

        var preview = modifyingSteps.Count == 0
            ? "当前没有需要修改系统网络配置的步骤。"
            : string.Join(Environment.NewLine, modifyingSteps);

        return _userConfirmationService.Confirm(
            title,
            $"即将在程序内执行以下网络配置。请确认这些步骤符合预期：{Environment.NewLine}{Environment.NewLine}{preview}{Environment.NewLine}{Environment.NewLine}程序不会修改默认路由、DNS，也不会关闭 WiFi 或 VPN。是否继续？");
    }

    private AppConfig BuildConfigFromInput()
    {
        var config = _configurationService.Load();
        config.Role = SelectedRole == DeviceRole.Unknown ? config.Role : SelectedRole;
        config.TargetDeviceIp = TargetDeviceIp.Trim();
        config.TargetDevicePort = TargetDevicePort;
        config.TargetDeviceAdapterIp = TargetDeviceAdapterIp.Trim();
        config.VirtualSubnetMask = VirtualSubnetMask.Trim();
        config.LocalListenPort = LocalListenPort;
        config.GatewayLanIp = GatewayLanIp.Trim();
        config.EnableNat = true;
        config.EnablePreciseRoute = true;
        config.EnableTcpProxyMode = true;
        config.EnableVirtualSubnetMode = true;
        if (string.IsNullOrWhiteSpace(config.SharedKey))
        {
            config.SharedKey = CreateSimpleModeSharedKey();
        }

        return config;
    }

    private void LoadFromConfig()
    {
        var config = _configurationService.Load();
        SelectedRole = config.Role;
        TargetDeviceIp = config.TargetDeviceIp;
        TargetDevicePort = config.TargetDevicePort;
        TargetDeviceAdapterIp = string.IsNullOrWhiteSpace(config.TargetDeviceAdapterIp)
            ? TargetDeviceAdapterIp
            : config.TargetDeviceAdapterIp;
        VirtualSubnetMask = config.VirtualSubnetMask;
        LocalListenPort = config.LocalListenPort;
        GatewayLanIp = config.GatewayLanIp;

        var lastRecord = _networkConfigurationExecutor.GetLastOperation();
        if (lastRecord is not null)
        {
            ApplyStatus = lastRecord.Status;
            Status = $"上次配置状态：{ApplyStatusText}，时间 {lastRecord.AppliedAt?.LocalDateTime:yyyy-MM-dd HH:mm:ss}。";
        }
    }

    private void DetectAdapters()
    {
        var config = _configurationService.Load();
        var lan = _networkAdapterService.FindLanCandidates().FirstOrDefault();
        if (lan is not null && string.IsNullOrWhiteSpace(GatewayLanIp))
        {
            GatewayLanIp = lan.IPv4Address;
        }

        var target = _networkAdapterService.FindTargetNetworkCandidates(TargetDeviceIp).FirstOrDefault();
        if (target is not null && string.IsNullOrWhiteSpace(TargetDeviceAdapterIp))
        {
            TargetDeviceAdapterIp = target.IPv4Address;
        }
        else if (string.IsNullOrWhiteSpace(TargetDeviceAdapterIp))
        {
            TargetDeviceAdapterIp = config.TargetDeviceAdapterIp;
        }
    }

    private void RefreshGatewayList()
    {
        var selectedId = SelectedGateway?.DeviceId;
        Gateways.Clear();
        foreach (var device in _discoveryService.GetOnlineDevices()
                     .Where(device => device.Role == DeviceRole.GatewayAgent && device.IsOnline))
        {
            Gateways.Add(device);
        }

        SelectedGateway = string.IsNullOrWhiteSpace(selectedId)
            ? Gateways.FirstOrDefault()
            : Gateways.FirstOrDefault(device => device.DeviceId == selectedId) ?? Gateways.FirstOrDefault();
    }

    private void OnDeviceDiscovered(object? sender, DeviceInfo device)
    {
        if (device.Role != DeviceRole.GatewayAgent)
        {
            return;
        }

        RunOnUiThread(RefreshGatewayList);
    }

    private void OnExecutionLogAdded(object? sender, string entry)
    {
        RunOnUiThread(() => ExecutionLogs.Add(entry));
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
            _logService.Error(result.Error);
        }
    }

    private static string CreateSimpleModeSharedKey()
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes("RobotNet.Windows.Wpf.SimpleMode.LocalPreviewKey.v1"));
        return Convert.ToBase64String(bytes);
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

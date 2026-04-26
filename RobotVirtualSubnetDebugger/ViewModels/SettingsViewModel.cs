using System.Collections.ObjectModel;
using System.Security.Cryptography;
using RobotNet.Windows.Wpf.Commands;
using RobotNet.Windows.Wpf.Models;
using RobotNet.Windows.Wpf.Services.Configuration;
using RobotNet.Windows.Wpf.Services.Logging;
using RobotNet.Windows.Wpf.Utils;

namespace RobotNet.Windows.Wpf.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly IConfigurationService _configurationService;
    private readonly ILogService _logService;
    private readonly AppConfig _config;
    private RoleOptionViewModel? _selectedRole;
    private string _targetDeviceIp = string.Empty;
    private int _targetDevicePort;
    private int _discoveryPort;
    private int _localListenPort;
    private int _proxyControlPort;
    private string _virtualIp = string.Empty;
    private string _virtualSubnetMask = string.Empty;
    private string _gatewayLanIp = string.Empty;
    private string _targetDeviceAdapterIp = string.Empty;
    private string _sharedKey = string.Empty;
    private int _proxyHeartbeatIntervalSeconds;
    private int _proxyIdleTimeoutSeconds;
    private int _proxyReconnectAttempts;
    private string _gitHubRepositoryOwner = string.Empty;
    private string _gitHubRepositoryName = string.Empty;
    private bool _enableUpdateCheckOnStartup;
    private string _saveMessage = string.Empty;

    public SettingsViewModel(IConfigurationService configurationService, ILogService logService)
    {
        _configurationService = configurationService;
        _logService = logService;
        _config = _configurationService.Load();

        RoleOptions =
        [
            new RoleOptionViewModel(DeviceRole.Unknown, "未选择"),
            new RoleOptionViewModel(DeviceRole.DebugClient, "调试端 DebugClient"),
            new RoleOptionViewModel(DeviceRole.GatewayAgent, "网关端 GatewayAgent")
        ];

        DeviceId = _config.DeviceId;
        DeviceName = _config.DeviceName;
        SelectedRole = RoleOptions.FirstOrDefault(option => option.Role == _config.Role) ?? RoleOptions[0];
        TargetDeviceIp = _config.TargetDeviceIp;
        TargetDevicePort = _config.TargetDevicePort;
        DiscoveryPort = _config.DiscoveryPort;
        LocalListenPort = _config.LocalListenPort;
        ProxyControlPort = _config.ProxyControlPort;
        VirtualIp = _config.VirtualIp;
        VirtualSubnetMask = _config.VirtualSubnetMask;
        GatewayLanIp = _config.GatewayLanIp;
        TargetDeviceAdapterIp = _config.TargetDeviceAdapterIp;
        SharedKey = _config.SharedKey;
        ProxyHeartbeatIntervalSeconds = _config.ProxyHeartbeatIntervalSeconds;
        ProxyIdleTimeoutSeconds = _config.ProxyIdleTimeoutSeconds;
        ProxyReconnectAttempts = _config.ProxyReconnectAttempts;
        GitHubRepositoryOwner = _config.GitHubRepositoryOwner;
        GitHubRepositoryName = _config.GitHubRepositoryName;
        EnableUpdateCheckOnStartup = _config.EnableUpdateCheckOnStartup;

        SaveCommand = new RelayCommand(Save);
        GenerateSharedKeyCommand = new RelayCommand(GenerateSharedKey);
    }

    public event EventHandler? ConfigSaved;

    public string DeviceId { get; }

    public string DeviceName { get; }

    public ObservableCollection<RoleOptionViewModel> RoleOptions { get; }

    public RelayCommand SaveCommand { get; }

    public RelayCommand GenerateSharedKeyCommand { get; }

    public RoleOptionViewModel? SelectedRole
    {
        get => _selectedRole;
        set => SetProperty(ref _selectedRole, value);
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

    public int DiscoveryPort
    {
        get => _discoveryPort;
        set => SetProperty(ref _discoveryPort, value);
    }

    public int LocalListenPort
    {
        get => _localListenPort;
        set => SetProperty(ref _localListenPort, value);
    }

    public int ProxyControlPort
    {
        get => _proxyControlPort;
        set => SetProperty(ref _proxyControlPort, value);
    }

    public string VirtualIp
    {
        get => _virtualIp;
        set => SetProperty(ref _virtualIp, value);
    }

    public string VirtualSubnetMask
    {
        get => _virtualSubnetMask;
        set => SetProperty(ref _virtualSubnetMask, value);
    }

    public string GatewayLanIp
    {
        get => _gatewayLanIp;
        set => SetProperty(ref _gatewayLanIp, value);
    }

    public string TargetDeviceAdapterIp
    {
        get => _targetDeviceAdapterIp;
        set => SetProperty(ref _targetDeviceAdapterIp, value);
    }

    public string SharedKey
    {
        get => _sharedKey;
        set => SetProperty(ref _sharedKey, value);
    }

    public int ProxyHeartbeatIntervalSeconds
    {
        get => _proxyHeartbeatIntervalSeconds;
        set => SetProperty(ref _proxyHeartbeatIntervalSeconds, value);
    }

    public int ProxyIdleTimeoutSeconds
    {
        get => _proxyIdleTimeoutSeconds;
        set => SetProperty(ref _proxyIdleTimeoutSeconds, value);
    }

    public int ProxyReconnectAttempts
    {
        get => _proxyReconnectAttempts;
        set => SetProperty(ref _proxyReconnectAttempts, value);
    }

    public string GitHubRepositoryOwner
    {
        get => _gitHubRepositoryOwner;
        set => SetProperty(ref _gitHubRepositoryOwner, value);
    }

    public string GitHubRepositoryName
    {
        get => _gitHubRepositoryName;
        set => SetProperty(ref _gitHubRepositoryName, value);
    }

    public bool EnableUpdateCheckOnStartup
    {
        get => _enableUpdateCheckOnStartup;
        set => SetProperty(ref _enableUpdateCheckOnStartup, value);
    }

    public string SaveMessage
    {
        get => _saveMessage;
        private set => SetProperty(ref _saveMessage, value);
    }

    private void Save()
    {
        if (!IsValidPort(TargetDevicePort, "目标设备端口") ||
            !IsValidPort(DiscoveryPort, "UDP 发现端口") ||
            !IsValidPort(LocalListenPort, "本地监听端口") ||
            !IsValidPort(ProxyControlPort, "代理控制端口") ||
            !IsValidProxyStability())
        {
            return;
        }

        _config.Role = SelectedRole?.Role ?? DeviceRole.Unknown;
        _config.TargetDeviceIp = TargetDeviceIp.Trim();
        _config.TargetDevicePort = TargetDevicePort;
        _config.DiscoveryPort = DiscoveryPort;
        _config.LocalListenPort = LocalListenPort;
        _config.ProxyControlPort = ProxyControlPort;
        _config.VirtualIp = VirtualIp.Trim();
        _config.VirtualSubnetMask = VirtualSubnetMask.Trim();
        _config.GatewayLanIp = GatewayLanIp.Trim();
        _config.TargetDeviceAdapterIp = TargetDeviceAdapterIp.Trim();
        _config.SharedKey = SharedKey.Trim();
        _config.ProxyHeartbeatIntervalSeconds = ProxyHeartbeatIntervalSeconds;
        _config.ProxyIdleTimeoutSeconds = ProxyIdleTimeoutSeconds;
        _config.ProxyReconnectAttempts = ProxyReconnectAttempts;
        _config.GitHubRepositoryOwner = GitHubRepositoryOwner.Trim();
        _config.GitHubRepositoryName = GitHubRepositoryName.Trim();
        _config.EnableUpdateCheckOnStartup = EnableUpdateCheckOnStartup;

        _configurationService.Save(_config);
        SaveMessage = $"{DateTime.Now:HH:mm:ss} 配置已保存。";
        ConfigSaved?.Invoke(this, EventArgs.Empty);
    }

    private void GenerateSharedKey()
    {
        SharedKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        SaveMessage = "已生成新的 SharedKey。请把同一个密钥配置到另一台电脑。";
        _logService.Audit("已生成新的 SharedKey，密钥内容未写入日志。");
    }

    private bool IsValidPort(int port, string name)
    {
        if (port is > 0 and <= 65535)
        {
            return true;
        }

        SaveMessage = $"{name}必须在 1 到 65535 之间。";
        _logService.Warning(SaveMessage);
        return false;
    }

    private bool IsValidProxyStability()
    {
        if (ProxyHeartbeatIntervalSeconds is < 1 or > 60)
        {
            SaveMessage = "代理心跳间隔必须在 1 到 60 秒之间。";
            _logService.Warning(SaveMessage);
            return false;
        }

        if (ProxyIdleTimeoutSeconds <= ProxyHeartbeatIntervalSeconds || ProxyIdleTimeoutSeconds > 300)
        {
            SaveMessage = "代理空闲超时必须大于心跳间隔，且不能超过 300 秒。";
            _logService.Warning(SaveMessage);
            return false;
        }

        if (ProxyReconnectAttempts is < 0 or > 10)
        {
            SaveMessage = "代理重试次数必须在 0 到 10 之间。";
            _logService.Warning(SaveMessage);
            return false;
        }

        return true;
    }
}

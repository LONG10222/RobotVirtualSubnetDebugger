using System.IO;
using System.Text.Json;
using RobotNet.Windows.Wpf.Models;
using RobotNet.Windows.Wpf.Services.Logging;
using RobotNet.Windows.Wpf.Services.Platform;

namespace RobotNet.Windows.Wpf.Services.Configuration;

public sealed class JsonConfigurationService : IConfigurationService
{
    private const string DefaultTargetDeviceIp = "192.168.1.10";
    private const int DefaultTargetDevicePort = 30003;
    private const int DefaultDiscoveryPort = 47831;
    private const int DefaultLocalListenPort = 30003;
    private const int DefaultProxyControlPort = 47832;
    private const int DefaultProxyHeartbeatIntervalSeconds = 5;
    private const int DefaultProxyIdleTimeoutSeconds = 20;
    private const int DefaultProxyReconnectAttempts = 2;
    private const string DefaultGitHubRepositoryOwner = "LONG10222";
    private const string DefaultGitHubRepositoryName = "RobotVirtualSubnetDebugger";

    private readonly IDeviceIdentityService _identityService;
    private readonly ILogService _logService;
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        WriteIndented = true
    };

    public JsonConfigurationService(IDeviceIdentityService identityService, ILogService logService)
    {
        _identityService = identityService;
        _logService = logService;

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appDirectory = Path.Combine(appData, "RobotNet.Windows.Wpf");
        ConfigFilePath = Path.Combine(appDirectory, "config.json");
    }

    public string ConfigFilePath { get; }

    public AppConfig Load()
    {
        if (!File.Exists(ConfigFilePath))
        {
            var defaultConfig = CreateDefault();
            Save(defaultConfig);
            _logService.Info($"首次启动，已创建默认配置：{ConfigFilePath}");
            return defaultConfig;
        }

        try
        {
            var json = File.ReadAllText(ConfigFilePath);
            var config = JsonSerializer.Deserialize<AppConfig>(json, _serializerOptions) ?? CreateDefault();
            if (Normalize(config))
            {
                Save(config);
            }

            return config;
        }
        catch (Exception ex)
        {
            _logService.Error("读取配置失败，已回退到默认配置。", ex);
            var defaultConfig = CreateDefault();
            Save(defaultConfig);
            return defaultConfig;
        }
    }

    public void Save(AppConfig config)
    {
        Normalize(config);

        var directory = Path.GetDirectoryName(ConfigFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(config, _serializerOptions);
        File.WriteAllText(ConfigFilePath, json);
        _logService.Info($"配置已保存：{ConfigFilePath}");
    }

    public AppConfig CreateDefault()
    {
        return new AppConfig
        {
            DeviceId = _identityService.GetOrCreateDeviceId(),
            DeviceName = _identityService.GetComputerName(),
            Role = DeviceRole.Unknown,
            TargetDeviceIp = DefaultTargetDeviceIp,
            TargetDevicePort = DefaultTargetDevicePort,
            DiscoveryPort = DefaultDiscoveryPort,
            LocalListenPort = DefaultLocalListenPort,
            ProxyControlPort = DefaultProxyControlPort,
            ProxyHeartbeatIntervalSeconds = DefaultProxyHeartbeatIntervalSeconds,
            ProxyIdleTimeoutSeconds = DefaultProxyIdleTimeoutSeconds,
            ProxyReconnectAttempts = DefaultProxyReconnectAttempts,
            VirtualIp = "192.168.1.101",
            VirtualSubnetMask = "255.255.255.0",
            GitHubRepositoryOwner = DefaultGitHubRepositoryOwner,
            GitHubRepositoryName = DefaultGitHubRepositoryName,
            EnableUpdateCheckOnStartup = true
        };
    }

    private bool Normalize(AppConfig config)
    {
        var updated = false;

        if (string.IsNullOrWhiteSpace(config.DeviceId))
        {
            config.DeviceId = _identityService.GetOrCreateDeviceId();
            updated = true;
        }

        if (string.IsNullOrWhiteSpace(config.DeviceName))
        {
            config.DeviceName = _identityService.GetComputerName();
            updated = true;
        }

        if (string.IsNullOrWhiteSpace(config.TargetDeviceIp) && !string.IsNullOrWhiteSpace(config.LegacyRobotIp))
        {
            config.TargetDeviceIp = config.LegacyRobotIp;
            updated = true;
        }

        if (config.TargetDevicePort <= 0 && config.LegacyRobotPort is > 0)
        {
            config.TargetDevicePort = config.LegacyRobotPort.Value;
            updated = true;
        }

        if (string.IsNullOrWhiteSpace(config.TargetDeviceAdapterIp) && !string.IsNullOrWhiteSpace(config.LegacyRobotAdapterIp))
        {
            config.TargetDeviceAdapterIp = config.LegacyRobotAdapterIp;
            updated = true;
        }

        if (config.LegacyRobotIp is not null || config.LegacyRobotPort is not null || config.LegacyRobotAdapterIp is not null)
        {
            config.LegacyRobotIp = null;
            config.LegacyRobotPort = null;
            config.LegacyRobotAdapterIp = null;
            updated = true;
        }

        if (string.IsNullOrWhiteSpace(config.TargetDeviceIp))
        {
            config.TargetDeviceIp = DefaultTargetDeviceIp;
            updated = true;
        }

        if (config.TargetDevicePort <= 0)
        {
            config.TargetDevicePort = DefaultTargetDevicePort;
            updated = true;
        }

        if (config.DiscoveryPort <= 0 || config.DiscoveryPort > 65535)
        {
            config.DiscoveryPort = DefaultDiscoveryPort;
            updated = true;
        }

        if (config.LocalListenPort <= 0 || config.LocalListenPort > 65535)
        {
            config.LocalListenPort = DefaultLocalListenPort;
            updated = true;
        }

        if (config.ProxyControlPort <= 0 || config.ProxyControlPort > 65535)
        {
            config.ProxyControlPort = DefaultProxyControlPort;
            updated = true;
        }

        if (config.ProxyHeartbeatIntervalSeconds <= 0 || config.ProxyHeartbeatIntervalSeconds > 60)
        {
            config.ProxyHeartbeatIntervalSeconds = DefaultProxyHeartbeatIntervalSeconds;
            updated = true;
        }

        if (config.ProxyIdleTimeoutSeconds <= config.ProxyHeartbeatIntervalSeconds ||
            config.ProxyIdleTimeoutSeconds > 300)
        {
            config.ProxyIdleTimeoutSeconds = Math.Max(DefaultProxyIdleTimeoutSeconds, config.ProxyHeartbeatIntervalSeconds * 3);
            updated = true;
        }

        if (config.ProxyReconnectAttempts < 0 || config.ProxyReconnectAttempts > 10)
        {
            config.ProxyReconnectAttempts = DefaultProxyReconnectAttempts;
            updated = true;
        }

        if (string.IsNullOrWhiteSpace(config.GitHubRepositoryOwner))
        {
            config.GitHubRepositoryOwner = DefaultGitHubRepositoryOwner;
            updated = true;
        }

        if (string.IsNullOrWhiteSpace(config.GitHubRepositoryName))
        {
            config.GitHubRepositoryName = DefaultGitHubRepositoryName;
            updated = true;
        }

        return updated;
    }
}

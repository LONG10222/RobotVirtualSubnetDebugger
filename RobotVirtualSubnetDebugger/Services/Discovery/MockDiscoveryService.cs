using RobotNet.Windows.Wpf.Models;
using RobotNet.Windows.Wpf.Services.Configuration;
using RobotNet.Windows.Wpf.Services.Logging;

namespace RobotNet.Windows.Wpf.Services.Discovery;

public sealed class MockDiscoveryService : IDiscoveryService
{
    private readonly IConfigurationService _configurationService;
    private readonly ILogService _logService;
    private readonly List<DeviceInfo> _devices = [];
    private bool _isRunning;

    public MockDiscoveryService(IConfigurationService configurationService, ILogService logService)
    {
        _configurationService = configurationService;
        _logService = logService;
    }

    public event EventHandler<DeviceInfo>? DeviceDiscovered;

    public Task StartAsync()
    {
        _isRunning = true;
        _devices.Clear();

        var config = _configurationService.Load();
        AddDevice(new DeviceInfo
        {
            DeviceId = config.DeviceId,
            ComputerName = config.DeviceName,
            LanIp = string.IsNullOrWhiteSpace(config.GatewayLanIp) ? "本机" : config.GatewayLanIp,
            Role = config.Role,
            LastSeen = DateTimeOffset.Now,
            IsOnline = true
        });

        AddDevice(new DeviceInfo
        {
            DeviceId = "mock-gateway-agent",
            ComputerName = "Gateway-PC",
            LanIp = "192.168.31.20",
            Role = DeviceRole.GatewayAgent,
            LastSeen = DateTimeOffset.Now.AddSeconds(-3),
            IsOnline = true
        });

        AddDevice(new DeviceInfo
        {
            DeviceId = "mock-debug-client",
            ComputerName = "Debug-PC",
            LanIp = "192.168.31.30",
            Role = DeviceRole.DebugClient,
            LastSeen = DateTimeOffset.Now.AddSeconds(-9),
            IsOnline = true
        });

        _logService.Info("模拟设备发现已启动。");
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _isRunning = false;
        _logService.Info("模拟设备发现已停止。");
        return Task.CompletedTask;
    }

    public IReadOnlyList<DeviceInfo> GetOnlineDevices()
    {
        return _devices
            .Where(device => _isRunning || device.IsOnline)
            .ToList();
    }

    private void AddDevice(DeviceInfo device)
    {
        _devices.Add(device);
        DeviceDiscovered?.Invoke(this, device);
    }
}

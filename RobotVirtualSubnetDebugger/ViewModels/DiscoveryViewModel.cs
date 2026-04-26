using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using RobotNet.Windows.Wpf.Commands;
using RobotNet.Windows.Wpf.Models;
using RobotNet.Windows.Wpf.Services.Configuration;
using RobotNet.Windows.Wpf.Services.Diagnostics;
using RobotNet.Windows.Wpf.Services.Discovery;
using RobotNet.Windows.Wpf.Services.Logging;
using RobotNet.Windows.Wpf.Services.Tunnel;
using RobotNet.Windows.Wpf.Utils;

namespace RobotNet.Windows.Wpf.ViewModels;

public sealed class DiscoveryViewModel : ObservableObject
{
    private readonly IConfigurationService _configurationService;
    private readonly IDiscoveryService _discoveryService;
    private readonly ITunnelSessionService _sessionService;
    private readonly IConnectionPreflightService _connectionPreflightService;
    private readonly ILogService _logService;
    private readonly DispatcherTimer _refreshTimer;
    private DeviceInfo? _selectedDevice;
    private string _status = "未启动";

    public DiscoveryViewModel(
        IConfigurationService configurationService,
        IDiscoveryService discoveryService,
        ITunnelSessionService sessionService,
        IConnectionPreflightService connectionPreflightService,
        ILogService logService)
    {
        _configurationService = configurationService;
        _discoveryService = discoveryService;
        _sessionService = sessionService;
        _connectionPreflightService = connectionPreflightService;
        _logService = logService;

        _discoveryService.DeviceDiscovered += OnDeviceDiscovered;

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _refreshTimer.Tick += (_, _) => RefreshDevices();

        StartCommand = new AsyncRelayCommand(StartAsync);
        StopCommand = new AsyncRelayCommand(StopAsync);
        RefreshCommand = new RelayCommand(RefreshDevices);
        ConnectSelectedCommand = new AsyncRelayCommand(ConnectSelectedAsync);
    }

    public ObservableCollection<DeviceInfo> Devices { get; } = [];

    public AsyncRelayCommand StartCommand { get; }

    public AsyncRelayCommand StopCommand { get; }

    public RelayCommand RefreshCommand { get; }

    public AsyncRelayCommand ConnectSelectedCommand { get; }

    public DeviceInfo? SelectedDevice
    {
        get => _selectedDevice;
        set => SetProperty(ref _selectedDevice, value);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    private async Task StartAsync()
    {
        Devices.Clear();
        var config = _configurationService.Load();
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
        RefreshDevices();
        _refreshTimer.Start();

        Status = $"已启动 UDP 发现，当前 {Devices.Count} 个设备。";
    }

    private async Task StopAsync()
    {
        _refreshTimer.Stop();
        await _discoveryService.StopAsync();
        RefreshDevices();
        Status = "已停止发现。";
    }

    private async Task ConnectSelectedAsync()
    {
        var selected = SelectedDevice;
        if (selected is null)
        {
            Status = "请先选择一个网关端设备。";
            return;
        }

        var config = _configurationService.Load();
        if (selected.DeviceId == config.DeviceId)
        {
            Status = "不能连接本机。";
            return;
        }

        if (config.Role != DeviceRole.DebugClient)
        {
            Status = "当前设备需要先在配置页设置为调试端 DebugClient。";
            return;
        }

        if (selected.Role != DeviceRole.GatewayAgent)
        {
            Status = "请选择角色为网关端 GatewayAgent 的设备。";
            return;
        }

        if (!selected.IsOnline)
        {
            Status = "选中的设备已离线，请刷新后重新选择。";
            _sessionService.ReleaseIfPeerOffline(_discoveryService.GetOnlineDevices());
            return;
        }

        if (selected.IsBusy &&
            !string.IsNullOrWhiteSpace(selected.ConnectedPeerDeviceId) &&
            selected.ConnectedPeerDeviceId != config.DeviceId)
        {
            Status = $"设备 {selected.ComputerName} 已连接其他设备，当前不能连接。";
            return;
        }

        config.GatewayLanIp = selected.LanIp;
        var preflight = _connectionPreflightService.EnsurePortsReady(config);
        if (!preflight.CanContinue)
        {
            Status = preflight.Summary;
            return;
        }

        _configurationService.Save(config);

        Status = $"正在连接网关端 {selected.ComputerName} / {selected.LanIp}。";
        var session = await _sessionService.StartAsync(config, selected);
        Status = session.Message;
        _logService.Info($"已从发现页发起模拟连接：{selected.ComputerName} / {selected.LanIp}");
    }

    private void OnDeviceDiscovered(object? sender, DeviceInfo device)
    {
        RunOnUiThread(() =>
        {
            AddOrUpdateDevice(device);
            Status = $"UDP 发现运行中，当前 {Devices.Count} 个设备。";
        });
    }

    private void RefreshDevices()
    {
        var selectedId = SelectedDevice?.DeviceId;
        var devices = _discoveryService.GetOnlineDevices();
        _sessionService.ReleaseIfPeerOffline(devices);

        Devices.Clear();
        foreach (var device in devices)
        {
            Devices.Add(device);
        }

        SelectedDevice = string.IsNullOrWhiteSpace(selectedId)
            ? null
            : Devices.FirstOrDefault(device => device.DeviceId == selectedId);

        var onlineCount = Devices.Count(device => device.IsOnline);
        Status = $"已刷新设备列表：在线 {onlineCount} 个，总计 {Devices.Count} 个。";
    }

    private void AddOrUpdateDevice(DeviceInfo device)
    {
        var existingIndex = -1;
        for (var index = 0; index < Devices.Count; index++)
        {
            if (Devices[index].DeviceId == device.DeviceId)
            {
                existingIndex = index;
                break;
            }
        }

        if (existingIndex >= 0)
        {
            Devices[existingIndex] = device;
        }
        else
        {
            Devices.Add(device);
        }

        if (SelectedDevice?.DeviceId == device.DeviceId)
        {
            SelectedDevice = device;
        }
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

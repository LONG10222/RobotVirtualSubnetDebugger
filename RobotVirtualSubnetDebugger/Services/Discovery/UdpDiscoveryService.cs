using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using RobotNet.Windows.Wpf.Models;
using RobotNet.Windows.Wpf.Services.Configuration;
using RobotNet.Windows.Wpf.Services.Logging;
using RobotNet.Windows.Wpf.Services.Platform;
using RobotNet.Windows.Wpf.Services.Tunnel;

namespace RobotNet.Windows.Wpf.Services.Discovery;

public sealed class UdpDiscoveryService : IDiscoveryService
{
    private const int DefaultDiscoveryPort = 47831;
    private static readonly TimeSpan BroadcastInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan OnlineTimeout = TimeSpan.FromSeconds(12);

    private readonly IConfigurationService _configurationService;
    private readonly INetworkAdapterService _networkAdapterService;
    private readonly ILogService _logService;
    private readonly ITunnelSessionService? _tunnelSessionService;
    private readonly Dictionary<string, DeviceInfo> _devices = [];
    private readonly object _syncRoot = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private CancellationTokenSource? _cancellationTokenSource;
    private UdpClient? _listener;
    private UdpClient? _sender;
    private Task? _listenTask;
    private Task? _broadcastTask;
    private int _activeDiscoveryPort = DefaultDiscoveryPort;

    public UdpDiscoveryService(
        IConfigurationService configurationService,
        INetworkAdapterService networkAdapterService,
        ILogService logService,
        ITunnelSessionService? tunnelSessionService = null)
    {
        _configurationService = configurationService;
        _networkAdapterService = networkAdapterService;
        _logService = logService;
        _tunnelSessionService = tunnelSessionService;
    }

    public event EventHandler<DeviceInfo>? DeviceDiscovered;

    public async Task StartAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            Start();
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private void Start()
    {
        if (_cancellationTokenSource is not null)
        {
            return;
        }

        var config = _configurationService.Load();
        _activeDiscoveryPort = config.DiscoveryPort;

        _listener = CreateListener(_activeDiscoveryPort);
        _cancellationTokenSource = new CancellationTokenSource();
        _sender = new UdpClient(AddressFamily.InterNetwork)
        {
            EnableBroadcast = true
        };

        AddOrUpdateDevice(CreateLocalDeviceInfo(config));

        var token = _cancellationTokenSource.Token;
        _listenTask = Task.Run(() => ListenAsync(token), token);
        _broadcastTask = Task.Run(() => BroadcastAsync(token), token);

        _logService.Info($"UDP 设备发现已启动，端口 {_activeDiscoveryPort}。");
    }

    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private async Task StopCoreAsync()
    {
        var cancellationTokenSource = _cancellationTokenSource;
        if (cancellationTokenSource is null)
        {
            return;
        }

        cancellationTokenSource.Cancel();
        _listener?.Close();
        _sender?.Close();

        await WaitForBackgroundTaskAsync(_listenTask).ConfigureAwait(false);
        await WaitForBackgroundTaskAsync(_broadcastTask).ConfigureAwait(false);

        _listener?.Dispose();
        _sender?.Dispose();
        cancellationTokenSource.Dispose();

        _listener = null;
        _sender = null;
        _listenTask = null;
        _broadcastTask = null;
        _cancellationTokenSource = null;

        _logService.Info("UDP 设备发现已停止。");
    }

    public IReadOnlyList<DeviceInfo> GetOnlineDevices()
    {
        lock (_syncRoot)
        {
            RefreshOnlineState();
            return _devices.Values
                .OrderByDescending(device => device.IsOnline)
                .ThenBy(device => device.ComputerName, StringComparer.CurrentCultureIgnoreCase)
                .Select(CloneDevice)
                .ToList();
        }
    }

    private static UdpClient CreateListener(int discoveryPort)
    {
        var listener = new UdpClient(AddressFamily.InterNetwork)
        {
            ExclusiveAddressUse = false
        };

        listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        try
        {
            listener.Client.Bind(new IPEndPoint(IPAddress.Any, discoveryPort));
            return listener;
        }
        catch
        {
            listener.Dispose();
            throw;
        }
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await _listener!.ReceiveAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException ex)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    _logService.Warning($"UDP 接收失败：{ex.Message}");
                }

                break;
            }

            HandleMessage(result.Buffer, result.RemoteEndPoint);
        }
    }

    private async Task BroadcastAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await SendHeartbeatAsync(cancellationToken);
                await Task.Delay(BroadcastInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logService.Warning($"UDP 广播失败：{ex.Message}");
                await Task.Delay(BroadcastInterval, cancellationToken);
            }
        }
    }

    private async Task SendHeartbeatAsync(CancellationToken cancellationToken)
    {
        var config = _configurationService.Load();
        var message = new DiscoveryMessage
        {
            DeviceId = config.DeviceId,
            ComputerName = config.DeviceName,
            LanIp = ResolveLanIp(config),
            Role = config.Role,
            IsBusy = _tunnelSessionService?.Current.IsConnected ?? false,
            ConnectedPeerDeviceId = _tunnelSessionService?.Current.PeerDeviceId ?? string.Empty,
            ConnectedPeerName = _tunnelSessionService?.Current.PeerDeviceName ?? string.Empty,
            ProxyControlPort = config.Role == DeviceRole.GatewayAgent ? config.ProxyControlPort : 0,
            TargetDeviceIp = config.Role == DeviceRole.GatewayAgent ? config.TargetDeviceIp : string.Empty,
            TargetDevicePort = config.Role == DeviceRole.GatewayAgent ? config.TargetDevicePort : 0,
            TargetSubnetMask = config.Role == DeviceRole.GatewayAgent ? config.VirtualSubnetMask : string.Empty,
            AutoPairingToken = config.Role == DeviceRole.GatewayAgent ? config.AutoPairingToken : string.Empty,
            Timestamp = DateTimeOffset.Now
        };

        var json = JsonSerializer.Serialize(message, _serializerOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _sender!.SendAsync(bytes, new IPEndPoint(ResolveBroadcastAddress(message.LanIp), _activeDiscoveryPort), cancellationToken);

        AddOrUpdateDevice(new DeviceInfo
        {
            DeviceId = message.DeviceId,
            ComputerName = message.ComputerName,
            LanIp = message.LanIp,
            Role = message.Role,
            IsBusy = message.IsBusy,
            ConnectedPeerDeviceId = message.ConnectedPeerDeviceId,
            ConnectedPeerName = message.ConnectedPeerName,
            ProxyControlPort = message.ProxyControlPort,
            TargetDeviceIp = message.TargetDeviceIp,
            TargetDevicePort = message.TargetDevicePort,
            TargetSubnetMask = message.TargetSubnetMask,
            AutoPairingToken = message.AutoPairingToken,
            LastSeen = DateTimeOffset.Now,
            IsOnline = true
        });
    }

    private void HandleMessage(byte[] bytes, IPEndPoint remoteEndPoint)
    {
        try
        {
            var json = Encoding.UTF8.GetString(bytes);
            var message = JsonSerializer.Deserialize<DiscoveryMessage>(json, _serializerOptions);
            if (message is null ||
                message.Protocol != DiscoveryMessage.CurrentProtocol ||
                string.IsNullOrWhiteSpace(message.DeviceId))
            {
                return;
            }

            AddOrUpdateDevice(new DeviceInfo
            {
                DeviceId = message.DeviceId,
                ComputerName = string.IsNullOrWhiteSpace(message.ComputerName) ? remoteEndPoint.Address.ToString() : message.ComputerName,
                LanIp = string.IsNullOrWhiteSpace(message.LanIp) ? remoteEndPoint.Address.ToString() : message.LanIp,
                Role = message.Role,
                IsBusy = message.IsBusy,
                ConnectedPeerDeviceId = message.ConnectedPeerDeviceId,
                ConnectedPeerName = message.ConnectedPeerName,
                ProxyControlPort = message.ProxyControlPort,
                TargetDeviceIp = message.TargetDeviceIp,
                TargetDevicePort = message.TargetDevicePort,
                TargetSubnetMask = message.TargetSubnetMask,
                AutoPairingToken = message.AutoPairingToken,
                LastSeen = DateTimeOffset.Now,
                IsOnline = true
            });
        }
        catch (JsonException)
        {
        }
        catch (DecoderFallbackException)
        {
        }
    }

    private DeviceInfo CreateLocalDeviceInfo(AppConfig? config = null)
    {
        config ??= _configurationService.Load();
        return new DeviceInfo
        {
            DeviceId = config.DeviceId,
            ComputerName = config.DeviceName,
            LanIp = ResolveLanIp(config),
            Role = config.Role,
            IsBusy = _tunnelSessionService?.Current.IsConnected ?? false,
            ConnectedPeerDeviceId = _tunnelSessionService?.Current.PeerDeviceId ?? string.Empty,
            ConnectedPeerName = _tunnelSessionService?.Current.PeerDeviceName ?? string.Empty,
            ProxyControlPort = config.Role == DeviceRole.GatewayAgent ? config.ProxyControlPort : 0,
            TargetDeviceIp = config.Role == DeviceRole.GatewayAgent ? config.TargetDeviceIp : string.Empty,
            TargetDevicePort = config.Role == DeviceRole.GatewayAgent ? config.TargetDevicePort : 0,
            TargetSubnetMask = config.Role == DeviceRole.GatewayAgent ? config.VirtualSubnetMask : string.Empty,
            AutoPairingToken = config.Role == DeviceRole.GatewayAgent ? config.AutoPairingToken : string.Empty,
            LastSeen = DateTimeOffset.Now,
            IsOnline = true
        };
    }

    private string ResolveLanIp(AppConfig config)
    {
        var candidates = _networkAdapterService.FindLanCandidates();
        if (config.Role == DeviceRole.GatewayAgent &&
            candidates.Any(adapter => adapter.IPv4Address == config.GatewayLanIp))
            return config.GatewayLanIp;

        var lanIp = candidates.FirstOrDefault()?.IPv4Address;
        if (!string.IsNullOrWhiteSpace(lanIp))
        {
            return lanIp;
        }

        return string.IsNullOrWhiteSpace(config.GatewayLanIp) ? "0.0.0.0" : config.GatewayLanIp;
    }

    private IPAddress ResolveBroadcastAddress(string lanIp)
    {
        var local = _networkAdapterService.FindLanCandidates().FirstOrDefault(adapter => adapter.IPv4Address == lanIp);
        if (local is null || !IPAddress.TryParse(lanIp, out var ip) ||
            !IPAddress.TryParse(local.SubnetMask, out var mask) ||
            ip.AddressFamily != AddressFamily.InterNetwork || mask.AddressFamily != AddressFamily.InterNetwork)
            return IPAddress.Broadcast;

        var address = ip.GetAddressBytes();
        var netmask = mask.GetAddressBytes();
        if (netmask[3] >= 254)
            return IPAddress.Broadcast;
        for (var index = 0; index < address.Length; index++)
            address[index] |= (byte)~netmask[index];
        return new IPAddress(address);
    }

    private void AddOrUpdateDevice(DeviceInfo device)
    {
        DeviceInfo snapshot;
        lock (_syncRoot)
        {
            _devices[device.DeviceId] = device;
            snapshot = CloneDevice(device);
        }

        DeviceDiscovered?.Invoke(this, snapshot);
    }

    private void RefreshOnlineState()
    {
        var now = DateTimeOffset.Now;
        foreach (var device in _devices.Values)
        {
            device.IsOnline = now - device.LastSeen <= OnlineTimeout;
        }
    }

    private static DeviceInfo CloneDevice(DeviceInfo device)
    {
        return new DeviceInfo
        {
            DeviceId = device.DeviceId,
            ComputerName = device.ComputerName,
            LanIp = device.LanIp,
            Role = device.Role,
            LastSeen = device.LastSeen,
            IsOnline = device.IsOnline,
            IsBusy = device.IsBusy,
            ConnectedPeerDeviceId = device.ConnectedPeerDeviceId,
            ConnectedPeerName = device.ConnectedPeerName,
            ProxyControlPort = device.ProxyControlPort,
            TargetDeviceIp = device.TargetDeviceIp,
            TargetDevicePort = device.TargetDevicePort,
            TargetSubnetMask = device.TargetSubnetMask,
            AutoPairingToken = device.AutoPairingToken
        };
    }

    private static async Task WaitForBackgroundTaskAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }
}

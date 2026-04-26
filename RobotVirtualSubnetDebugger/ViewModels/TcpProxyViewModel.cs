using System.Windows;
using RobotNet.Windows.Wpf.Commands;
using RobotNet.Windows.Wpf.Models;
using RobotNet.Windows.Wpf.Services.Configuration;
using RobotNet.Windows.Wpf.Services.Diagnostics;
using RobotNet.Windows.Wpf.Services.Proxy;
using RobotNet.Windows.Wpf.Utils;

namespace RobotNet.Windows.Wpf.ViewModels;

public sealed class TcpProxyViewModel : ObservableObject
{
    private readonly IConfigurationService _configurationService;
    private readonly IConnectionPreflightService _connectionPreflightService;
    private readonly ITcpProxyService _tcpProxyService;
    private TcpProxySessionInfo _session;

    public TcpProxyViewModel(
        IConfigurationService configurationService,
        IConnectionPreflightService connectionPreflightService,
        ITcpProxyService tcpProxyService)
    {
        _configurationService = configurationService;
        _connectionPreflightService = connectionPreflightService;
        _tcpProxyService = tcpProxyService;
        _session = tcpProxyService.Current;

        StartCommand = new AsyncRelayCommand(StartAsync);
        StopCommand = new AsyncRelayCommand(StopAsync);
        _tcpProxyService.StateChanged += OnStateChanged;
    }

    public AsyncRelayCommand StartCommand { get; }

    public AsyncRelayCommand StopCommand { get; }

    public string StatusText => _session.StatusText;

    public string StatusColor => _session.StatusColor;

    public string EndpointText => _session.LocalListenPort <= 0 ? "-" : _session.EndpointText;

    public string RoleText => _session.Role switch
    {
        DeviceRole.DebugClient => "调试端",
        DeviceRole.GatewayAgent => "网关端",
        _ => "未知"
    };

    public string GatewayLanIp => EmptyToDash(_session.GatewayLanIp);

    public string ControlEndpointText => EmptyToDash(_session.ControlEndpointText);

    public string TargetEndpointText => string.IsNullOrWhiteSpace(_session.TargetDeviceIp) || _session.TargetDevicePort <= 0
        ? "-"
        : _session.TargetEndpointText;

    public string SecurityMode => EmptyToDash(_session.SecurityMode);

    public string StabilityText => _session.StabilityText;

    public int ActiveConnections => _session.ActiveConnections;

    public long BytesSent => _session.BytesSent;

    public long BytesReceived => _session.BytesReceived;

    public string Message => _session.Message;

    public string LastError => EmptyToDash(_session.LastError);

    public string LastUpdatedText => _session.LastUpdatedText;

    public string LastHeartbeatText => _session.LastHeartbeatText;

    private async Task StartAsync()
    {
        var config = _configurationService.Load();
        var preflight = _connectionPreflightService.EnsurePortsReady(config);
        if (preflight.ConfigChanged)
        {
            _configurationService.Save(config);
        }

        if (!preflight.CanContinue)
        {
            _session = new TcpProxySessionInfo
            {
                Status = TcpProxyStatus.Failed,
                LastUpdated = DateTimeOffset.Now,
                Message = preflight.Summary,
                LastError = preflight.Summary
            };
            RaiseSessionPropertiesChanged();
            return;
        }

        await _tcpProxyService.StartAsync(config);
    }

    private async Task StopAsync()
    {
        await _tcpProxyService.StopAsync();
    }

    private void OnStateChanged(object? sender, TcpProxySessionInfo session)
    {
        RunOnUiThread(() =>
        {
            _session = session;
            RaiseSessionPropertiesChanged();
        });
    }

    private void RaiseSessionPropertiesChanged()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusColor));
        OnPropertyChanged(nameof(EndpointText));
        OnPropertyChanged(nameof(RoleText));
        OnPropertyChanged(nameof(GatewayLanIp));
        OnPropertyChanged(nameof(ControlEndpointText));
        OnPropertyChanged(nameof(TargetEndpointText));
        OnPropertyChanged(nameof(SecurityMode));
        OnPropertyChanged(nameof(StabilityText));
        OnPropertyChanged(nameof(ActiveConnections));
        OnPropertyChanged(nameof(BytesSent));
        OnPropertyChanged(nameof(BytesReceived));
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(LastError));
        OnPropertyChanged(nameof(LastUpdatedText));
        OnPropertyChanged(nameof(LastHeartbeatText));
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

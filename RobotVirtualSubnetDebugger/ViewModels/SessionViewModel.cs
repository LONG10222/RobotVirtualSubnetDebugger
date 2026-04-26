using System.Collections.ObjectModel;
using System.Windows;
using RobotNet.Windows.Wpf.Commands;
using RobotNet.Windows.Wpf.Models;
using RobotNet.Windows.Wpf.Services.Configuration;
using RobotNet.Windows.Wpf.Services.Diagnostics;
using RobotNet.Windows.Wpf.Services.Tunnel;
using RobotNet.Windows.Wpf.Utils;

namespace RobotNet.Windows.Wpf.ViewModels;

public sealed class SessionViewModel : ObservableObject
{
    private readonly IConfigurationService _configurationService;
    private readonly ITunnelSessionService _sessionService;
    private readonly IConnectionPreflightService _connectionPreflightService;
    private TunnelSessionInfo _session;

    public SessionViewModel(
        IConfigurationService configurationService,
        ITunnelSessionService sessionService,
        IConnectionPreflightService connectionPreflightService)
    {
        _configurationService = configurationService;
        _sessionService = sessionService;
        _connectionPreflightService = connectionPreflightService;
        _session = sessionService.Current;

        StartCommand = new AsyncRelayCommand(StartAsync);
        StopCommand = new AsyncRelayCommand(StopAsync);

        _sessionService.SessionChanged += OnSessionChanged;
        _sessionService.StepAdded += OnStepAdded;
    }

    public ObservableCollection<HandshakeStepInfo> Steps { get; } = [];

    public AsyncRelayCommand StartCommand { get; }

    public AsyncRelayCommand StopCommand { get; }

    public string StatusText => _session.StatusText;

    public string StatusColor => _session.StatusColor;

    public string SessionId => string.IsNullOrWhiteSpace(_session.SessionId) ? "-" : _session.SessionId;

    public string RoleText => _session.LocalRole switch
    {
        DeviceRole.DebugClient => "调试端",
        DeviceRole.GatewayAgent => "网关端",
        _ => "未知"
    };

    public string PeerDeviceName => EmptyToDash(_session.PeerDeviceName);

    public string PeerLanIp => EmptyToDash(_session.PeerLanIp);

    public string PeerRoleText => _session.PeerRole switch
    {
        DeviceRole.DebugClient => "调试端",
        DeviceRole.GatewayAgent => "网关端",
        _ => "-"
    };

    public string GatewayLanIp => EmptyToDash(_session.GatewayLanIp);

    public string TargetDeviceIp => EmptyToDash(_session.TargetDeviceIp);

    public string VirtualIp => EmptyToDash(_session.VirtualIp);

    public string Message => _session.Message;

    public string LastUpdatedText => _session.LastUpdatedText;

    private async Task StartAsync()
    {
        Steps.Clear();
        var config = _configurationService.Load();
        var preflight = _connectionPreflightService.EnsurePortsReady(config);
        if (preflight.ConfigChanged)
        {
            _configurationService.Save(config);
        }

        foreach (var item in preflight.Items)
        {
            Steps.Add(new HandshakeStepInfo
            {
                Name = item.Name,
                Status = item.Status,
                Message = item.Message,
                Timestamp = DateTimeOffset.Now
            });
        }

        if (!preflight.CanContinue)
        {
            return;
        }

        var existingSteps = _sessionService.GetSteps();
        foreach (var step in existingSteps)
        {
            Steps.Add(step);
        }

        await _sessionService.StartAsync(config);
    }

    private async Task StopAsync()
    {
        await _sessionService.StopAsync();
    }

    private void OnSessionChanged(object? sender, TunnelSessionInfo session)
    {
        RunOnUiThread(() =>
        {
            _session = session;
            RaiseSessionPropertiesChanged();
        });
    }

    private void OnStepAdded(object? sender, HandshakeStepInfo step)
    {
        RunOnUiThread(() => Steps.Add(step));
    }

    private void RaiseSessionPropertiesChanged()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusColor));
        OnPropertyChanged(nameof(SessionId));
        OnPropertyChanged(nameof(RoleText));
        OnPropertyChanged(nameof(PeerDeviceName));
        OnPropertyChanged(nameof(PeerLanIp));
        OnPropertyChanged(nameof(PeerRoleText));
        OnPropertyChanged(nameof(GatewayLanIp));
        OnPropertyChanged(nameof(TargetDeviceIp));
        OnPropertyChanged(nameof(VirtualIp));
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(LastUpdatedText));
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

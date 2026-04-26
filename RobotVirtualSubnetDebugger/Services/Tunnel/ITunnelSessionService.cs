using RobotNet.Windows.Wpf.Models;

namespace RobotNet.Windows.Wpf.Services.Tunnel;

public interface ITunnelSessionService
{
    event EventHandler<TunnelSessionInfo>? SessionChanged;

    event EventHandler<HandshakeStepInfo>? StepAdded;

    TunnelSessionInfo Current { get; }

    IReadOnlyList<HandshakeStepInfo> GetSteps();

    Task<TunnelSessionInfo> StartAsync(AppConfig config, DeviceInfo? peer = null);

    Task<TunnelSessionInfo> StopAsync();

    TunnelSessionInfo ReleaseIfPeerOffline(IReadOnlyList<DeviceInfo> discoveredDevices);
}

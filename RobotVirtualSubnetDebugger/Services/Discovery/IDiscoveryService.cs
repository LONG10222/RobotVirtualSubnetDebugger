using RobotNet.Windows.Wpf.Models;

namespace RobotNet.Windows.Wpf.Services.Discovery;

public interface IDiscoveryService
{
    event EventHandler<DeviceInfo>? DeviceDiscovered;

    Task StartAsync();

    Task StopAsync();

    IReadOnlyList<DeviceInfo> GetOnlineDevices();
}

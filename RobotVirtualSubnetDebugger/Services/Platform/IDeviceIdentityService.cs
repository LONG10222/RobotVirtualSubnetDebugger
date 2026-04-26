namespace RobotNet.Windows.Wpf.Services.Platform;

public interface IDeviceIdentityService
{
    string GetOrCreateDeviceId();

    string GetComputerName();
}

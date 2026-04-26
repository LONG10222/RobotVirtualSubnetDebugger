using RobotNet.Windows.Wpf.Models;

namespace RobotNet.Windows.Wpf.ViewModels;

public sealed class RoleOptionViewModel
{
    public RoleOptionViewModel(DeviceRole role, string displayName)
    {
        Role = role;
        DisplayName = displayName;
    }

    public DeviceRole Role { get; }

    public string DisplayName { get; }
}

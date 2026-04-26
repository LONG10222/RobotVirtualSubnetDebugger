using RobotNet.Windows.Wpf.Commands;
using RobotNet.Windows.Wpf.Models;
using RobotNet.Windows.Wpf.Services.Configuration;
using RobotNet.Windows.Wpf.Services.Platform;
using RobotNet.Windows.Wpf.Utils;

namespace RobotNet.Windows.Wpf.ViewModels;

public sealed class DashboardViewModel : ObservableObject
{
    private readonly IConfigurationService _configurationService;
    private readonly INetworkAdapterService _networkAdapterService;
    private readonly IAdminElevationService _adminElevationService;
    private string _deviceName = string.Empty;
    private string _deviceId = string.Empty;
    private DeviceRole _role;
    private string _localLanIp = string.Empty;
    private string _targetDeviceAdapterIp = string.Empty;
    private string _targetDeviceIp = string.Empty;
    private string _virtualIp = string.Empty;
    private string _adminStatus = string.Empty;
    private string _status = string.Empty;

    public DashboardViewModel(
        IConfigurationService configurationService,
        INetworkAdapterService networkAdapterService,
        IAdminElevationService adminElevationService)
    {
        _configurationService = configurationService;
        _networkAdapterService = networkAdapterService;
        _adminElevationService = adminElevationService;
        RefreshCommand = new RelayCommand(Refresh);
        Refresh();
    }

    public RelayCommand RefreshCommand { get; }

    public string DeviceName
    {
        get => _deviceName;
        private set => SetProperty(ref _deviceName, value);
    }

    public string DeviceId
    {
        get => _deviceId;
        private set => SetProperty(ref _deviceId, value);
    }

    public DeviceRole Role
    {
        get => _role;
        private set => SetProperty(ref _role, value);
    }

    public string LocalLanIp
    {
        get => _localLanIp;
        private set => SetProperty(ref _localLanIp, value);
    }

    public string TargetDeviceAdapterIp
    {
        get => _targetDeviceAdapterIp;
        private set => SetProperty(ref _targetDeviceAdapterIp, value);
    }

    public string TargetDeviceIp
    {
        get => _targetDeviceIp;
        private set => SetProperty(ref _targetDeviceIp, value);
    }

    public string VirtualIp
    {
        get => _virtualIp;
        private set => SetProperty(ref _virtualIp, value);
    }

    public string AdminStatus
    {
        get => _adminStatus;
        private set => SetProperty(ref _adminStatus, value);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public void Refresh()
    {
        var config = _configurationService.Load();
        var lanAdapter = _networkAdapterService.FindLanCandidates().FirstOrDefault();
        var targetAdapter = _networkAdapterService.FindTargetNetworkCandidates(config.TargetDeviceIp).FirstOrDefault();

        DeviceName = config.DeviceName;
        DeviceId = config.DeviceId;
        Role = config.Role;
        LocalLanIp = lanAdapter?.IPv4Address ?? "-";
        TargetDeviceAdapterIp = targetAdapter?.IPv4Address ?? "-";
        TargetDeviceIp = config.TargetDeviceIp;
        VirtualIp = config.VirtualIp;
        AdminStatus = _adminElevationService.CheckAdminPrivilege().Message;
        Status = "简易模式可一键预览、应用和回滚网络配置；高级模式保留脚本导出与审计。";
    }
}

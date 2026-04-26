using RobotNet.Windows.Wpf.Models;

namespace RobotNet.Windows.Wpf.Services.Platform;

public interface INetworkAdapterService
{
    IReadOnlyList<NetworkAdapterInfo> GetAdapters();

    IReadOnlyList<NetworkAdapterInfo> FindTargetNetworkCandidates(string targetDeviceIp);

    IReadOnlyList<NetworkAdapterInfo> FindLanCandidates();

    bool IsSameSubnet(string ip1, string mask1, string ip2);
}

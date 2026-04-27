using System.Security.Cryptography;
using System.Text;

namespace RobotNet.Windows.Wpf.Utils;

public static class PairingKeyDeriver
{
    public static string DeriveSharedKey(string gatewayDeviceId, string pairingToken)
    {
        var seed = $"RobotNet.Windows.Wpf.AutoPairing.v1.{gatewayDeviceId}.{pairingToken}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return Convert.ToBase64String(bytes);
    }

    public static string CreatePairingToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
    }
}

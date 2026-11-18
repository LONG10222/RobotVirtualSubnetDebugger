using System.Net;
namespace RobotNet.Windows.Wpf.Services.Proxy;
internal static class GatewayTargetPolicy
{
    // Authority is immutable gateway startup configuration. Never resolve peer names.
    internal static (string Host, int Port) Authorize(
        string configuredHost, int configuredPort, string? requestedHost, int requestedPort)
    {
        var host = configuredHost.Trim();
        if (string.IsNullOrWhiteSpace(host) || configuredPort is < 1 or > 65535)
            throw new UnauthorizedAccessException("网关尚未配置有效的授权目标。");
        if (requestedPort != 0 && requestedPort != configuredPort)
            throw new UnauthorizedAccessException("请求的目标端口不在网关授权范围内。");
        if (!string.IsNullOrWhiteSpace(requestedHost))
        {
            var requested = requestedHost.Trim();
            var same = IPAddress.TryParse(host, out var allowedIp) && IPAddress.TryParse(requested, out var requestedIp)
                ? allowedIp.Equals(requestedIp)
                : string.Equals(host, requested, StringComparison.OrdinalIgnoreCase);
            if (!same)
                throw new UnauthorizedAccessException("请求的目标地址不在网关授权范围内。");
        }
        return (host, configuredPort);
    }
}

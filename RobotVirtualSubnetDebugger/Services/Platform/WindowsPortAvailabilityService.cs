using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using RobotNet.Windows.Wpf.Models;
using RobotNet.Windows.Wpf.Services.Logging;

namespace RobotNet.Windows.Wpf.Services.Platform;

public sealed class WindowsPortAvailabilityService : IPortAvailabilityService
{
    private readonly ILogService _logService;

    public WindowsPortAvailabilityService(ILogService logService)
    {
        _logService = logService;
    }

    public PortCheckResult CheckPort(int port, PortProtocol protocol)
    {
        var result = new PortCheckResult
        {
            Port = port,
            Protocol = protocol
        };

        if (port is < 1 or > 65535)
        {
            result.IsAvailable = false;
            return result;
        }

        var occupied = protocol == PortProtocol.Tcp
            ? IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(endpoint => endpoint.Port == port)
            : IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners().Any(endpoint => endpoint.Port == port);

        result.IsAvailable = !occupied;
        if (!occupied)
        {
            return result;
        }

        var processId = TryFindOwningProcessId(port, protocol);
        result.ProcessId = processId;
        result.IsOwnedByCurrentProcess = processId == Environment.ProcessId;
        result.ProcessName = ResolveProcessName(processId);
        return result;
    }

    public PortCheckResult FindAvailablePort(int preferredPort, PortProtocol protocol, int maxAttempts = 100)
    {
        var startPort = Math.Clamp(preferredPort, 1, 65535);
        var attempts = Math.Clamp(maxAttempts, 1, 65535);

        for (var offset = 0; offset < attempts; offset++)
        {
            var port = startPort + offset;
            if (port > 65535)
            {
                break;
            }

            var result = CheckPort(port, protocol);
            if (result.IsAvailable)
            {
                return result;
            }
        }

        return new PortCheckResult
        {
            Port = preferredPort,
            Protocol = protocol,
            IsAvailable = false
        };
    }

    private int? TryFindOwningProcessId(int port, PortProtocol protocol)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c netstat -ano -p {protocol.ToString().ToLowerInvariant()}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(2000);

            foreach (var line in output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith(protocol.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var parts = Regex.Split(trimmed, "\\s+");
                if (parts.Length < 3)
                {
                    continue;
                }

                var localEndpoint = parts[1];
                if (!EndpointMatchesPort(localEndpoint, port))
                {
                    continue;
                }

                if (int.TryParse(parts[^1], out var processId))
                {
                    return processId;
                }
            }
        }
        catch (Exception ex)
        {
            _logService.Warning($"端口 PID 检测失败：{ex.Message}");
        }

        return null;
    }

    private static bool EndpointMatchesPort(string endpoint, int port)
    {
        var separatorIndex = endpoint.LastIndexOf(':');
        if (separatorIndex < 0 || separatorIndex == endpoint.Length - 1)
        {
            return false;
        }

        var portText = endpoint[(separatorIndex + 1)..].Trim(']');
        return int.TryParse(portText, out var parsedPort) && parsedPort == port;
    }

    private static string ResolveProcessName(int? processId)
    {
        if (processId is null)
        {
            return string.Empty;
        }

        try
        {
            return Process.GetProcessById(processId.Value).ProcessName;
        }
        catch
        {
            return "Unknown";
        }
    }
}

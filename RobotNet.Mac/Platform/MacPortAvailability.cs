using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using RobotNet.Windows.Wpf.Models;
using RobotNet.Windows.Wpf.Services.Platform;

namespace RobotNet.Mac.Platform;

internal sealed class MacPortAvailability : IPortAvailabilityService
{
    public PortCheckResult CheckPort(int port, PortProtocol protocol)
    {
        var result = new PortCheckResult { Port = port, Protocol = protocol };
        if (port is < 1 or > 65535)
            return result;

        var tcp = protocol == PortProtocol.Tcp;
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("/usr/sbin/lsof")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                ArgumentList = { "-nP", "-a", $"-i{protocol.ToString().ToUpperInvariant()}:{port}", "-Fpcn" }
            }
        };
        if (tcp)
            process.StartInfo.ArgumentList.Add("-sTCP:LISTEN");
        process.Start();
        var listeners = new Dictionary<int, string>();
        var pid = 0;
        var name = string.Empty;
        while (process.StandardOutput.ReadLine() is { } line)
        {
            if (line.Length < 2)
                continue;
            switch (line[0])
            {
                case 'p':
                    int.TryParse(line.AsSpan(1), out pid);
                    name = string.Empty;
                    break;
                case 'c':
                    name = line[1..];
                    break;
                case 'n' when pid > 0:
                    var endpoint = line[1..].Split("->", 2, StringSplitOptions.None)[0];
                    if (endpoint.EndsWith($":{port}", StringComparison.Ordinal))
                        listeners[pid] = name;
                    break;
            }
        }
        process.WaitForExit();

        if (listeners.Count > 0)
        {
            var first = listeners.First();
            result.ProcessId = first.Key;
            result.ProcessName = first.Value;
            result.IsOwnedByCurrentProcess = listeners.Count == 1 && first.Key == Environment.ProcessId;
            return result;
        }

        // lsof may hide sockets owned by a different macOS user.
        result.IsAvailable = CanBind(IPAddress.Any, port, tcp) && CanBind(IPAddress.Loopback, port, tcp);
        return result;
    }

    private static bool CanBind(IPAddress address, int port, bool tcp)
    {
        using var socket = new Socket(AddressFamily.InterNetwork,
            tcp ? SocketType.Stream : SocketType.Dgram, tcp ? ProtocolType.Tcp : ProtocolType.Udp);
        try
        {
            socket.ExclusiveAddressUse = true;
            socket.Bind(new IPEndPoint(address, port));
            if (tcp)
                socket.Listen(1);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    public PortCheckResult FindAvailablePort(int preferredPort, PortProtocol protocol, int maxAttempts = 100)
    {
        var first = Math.Max(1, preferredPort);
        for (var port = first; port <= 65535 && port - first < maxAttempts; port++)
        {
            var result = CheckPort(port, protocol);
            if (result.IsAvailable)
                return result;
        }
        return new PortCheckResult { Port = preferredPort, Protocol = protocol };
    }
}

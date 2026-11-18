using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using RobotNet.Windows.Wpf.Models;
using RobotNet.Windows.Wpf.Services.Proxy;
using RobotNet.Windows.Wpf.Services.Logging;
using RobotNet.Windows.Wpf.Services.Platform;

internal static class Program
{
    private static readonly string Key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private static readonly HashSet<int> PortsUsed = [];
    private static int _passed;
    private static int Port()
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var p = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            if (PortsUsed.Add(p)) return p;
        }
        throw new IOException("Could not select distinct ephemeral test ports.");
    }
    private static AppConfig Config(DeviceRole role, int gateway, int local, int target) => new()
    {
        Role = role, ProxyControlPort = gateway, LocalListenPort = local,
        TargetDeviceIp = "127.0.0.1", TargetDevicePort = target, GatewayLanIp = "127.0.0.1",
        SharedKey = Key, ProxyHeartbeatIntervalSeconds = 1, ProxyIdleTimeoutSeconds = 3,
        ProxyReconnectAttempts = 0
    };
    private static SafeTcpProxyService Service() => new(new Ports(), new Log());
    private static void Check(bool passed, string message)
    {
        if (!passed) throw new InvalidOperationException(message);
    }
    private static byte[] Payload(int size) => Enumerable.Range(0, size).Select(i => (byte)(i % 251)).ToArray();
    private static async Task Closed(TcpClient socket, CancellationToken ct, bool allowBoundedError = false)
    {
        try
        {
            var total = 0; var buffer = new byte[512];
            while (true)
            {
                var read = await socket.GetStream().ReadAsync(buffer, ct);
                if (read == 0) return;
                total += read;
                Check(allowBoundedError && total <= 4096, "Peer sent unexpected or unbounded data instead of closing.");
            }
        }
        catch (IOException) { /* Either EOF or connection reset is an accepted rejection. */ }
    }
    private static async Task NoConnections(SafeTcpProxyService a, SafeTcpProxyService? b = null)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (a.Current.ActiveConnections != 0 || (b?.Current.ActiveConnections ?? 0) != 0)
            await Task.Delay(25, deadline.Token);
    }
    private static async Task Transfer(int bytes, bool targetFirst)
    {
        var target = new TcpListener(IPAddress.Loopback, 0); target.Start();
        var tp = ((IPEndPoint)target.LocalEndpoint).Port; var gp = Port(); var lp = Port();
        var gateway = Service(); var client = Service();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = deadline.Token; var data = Payload(bytes);
        Task? peer = null;
        try
        {
            await gateway.StartAsync(Config(DeviceRole.GatewayAgent, gp, lp, tp));
            await client.StartAsync(Config(DeviceRole.DebugClient, gp, lp, tp));
            peer = Task.Run(async () =>
            {
                using var incoming = await target.AcceptTcpClientAsync(ct);
                var stream = incoming.GetStream(); using var received = new MemoryStream();
                if (targetFirst)
                {
                    await stream.WriteAsync(data, ct); incoming.Client.Shutdown(SocketShutdown.Send);
                    await stream.CopyToAsync(received, ct);
                    Check(received.ToArray().SequenceEqual(data), "Reverse payload differs after target half-close.");
                }
                else
                {
                    await stream.CopyToAsync(received, ct);
                    Check(received.ToArray().SequenceEqual(data), "Target received corrupt request.");
                    // Longer than the configured idle timeout. Authenticated heartbeat must keep the tunnel alive.
                    await Task.Delay(bytes == 65536 ? 4500 : 75, ct);
                    await stream.WriteAsync(received.ToArray(), ct); incoming.Client.Shutdown(SocketShutdown.Send);
                }
            }, ct);
            using var local = new TcpClient(); await local.ConnectAsync(IPAddress.Loopback, lp, ct);
            using var response = new MemoryStream(); var s = local.GetStream();
            if (targetFirst)
            {
                await s.CopyToAsync(response, ct); Check(response.ToArray().SequenceEqual(data), "Forward payload differs after target FIN.");
                await s.WriteAsync(data, ct); local.Client.Shutdown(SocketShutdown.Send);
            }
            else
            {
                // Deliberately split writes to exercise protocol buffering independently of socket packet sizes.
                for (var offset = 0; offset < data.Length; offset += 997)
                    await s.WriteAsync(data.AsMemory(offset, Math.Min(997, data.Length - offset)), ct);
                local.Client.Shutdown(SocketShutdown.Send);
                await s.CopyToAsync(response, ct); Check(response.ToArray().SequenceEqual(data), $"Response truncated after client FIN: expected={data.Length}, actual={response.Length}.");
            }
            await peer.WaitAsync(ct); await NoConnections(gateway, client);
        }
        finally
        {
            deadline.Cancel(); await client.StopAsync(); await gateway.StopAsync(); target.Stop();
            if (peer != null) try { await peer.WaitAsync(TimeSpan.FromSeconds(2)); } catch { /* Main error is retained. */ }
        }
    }
    private static async Task Unauthenticated(bool partial)
    {
        var gateway = Service(); var gp = Port();
        try
        {
            await gateway.StartAsync(Config(DeviceRole.GatewayAgent, gp, Port(), Port()));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(7));
            using var socket = new TcpClient(); await socket.ConnectAsync(IPAddress.Loopback, gp, deadline.Token);
            if (partial) await socket.GetStream().WriteAsync("{\"protocolVersion\":"u8.ToArray(), deadline.Token);
            await Closed(socket, deadline.Token); await NoConnections(gateway);
        }
        finally { await gateway.StopAsync(); }
    }
    private static async Task Reject(bool wrongKey)
    {
        var allowed = new TcpListener(IPAddress.Loopback, 0); allowed.Start();
        var denied = new TcpListener(IPAddress.Loopback, 0); denied.Start();
        var gp = Port(); var lp = Port(); var gateway = Service(); var client = Service();
        var tp = ((IPEndPoint)allowed.LocalEndpoint).Port;
        try
        {
            var gc = Config(DeviceRole.GatewayAgent, gp, lp, tp);
            var cc = Config(DeviceRole.DebugClient, gp, lp, wrongKey ? tp : ((IPEndPoint)denied.LocalEndpoint).Port);
            if (wrongKey) cc.SharedKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            await gateway.StartAsync(gc); await client.StartAsync(cc);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            using var socket = new TcpClient(); await socket.ConnectAsync(IPAddress.Loopback, lp, deadline.Token);
            await Closed(socket, deadline.Token, allowBoundedError: true); await NoConnections(gateway, client);
            Check(!allowed.Pending() && !denied.Pending(), "Rejected request reached a target listener.");
        }
        finally { await client.StopAsync(); await gateway.StopAsync(); allowed.Stop(); denied.Stop(); }
    }
    private static async Task Run(string name, Func<Task> action)
    {
        await action().WaitAsync(TimeSpan.FromSeconds(45)); Interlocked.Increment(ref _passed); Console.WriteLine($"PASS {name}");
    }
    private static async Task<int> Main(string[] args)
    {
        Console.WriteLine($"OS={RuntimeInformation.OSDescription}; ARCH={RuntimeInformation.ProcessArchitecture}; RUNTIME={RuntimeInformation.FrameworkDescription}");
        Console.WriteLine("Actual production proxy over real local TCP sockets; no IP/routes/NAT/firewall changes. This is not multi-host routing validation.");
        try
        {
            if (args.Contains("--stress"))
            {
                for (var iteration = 0; iteration < 25; iteration++) await Run($"repeat client FIN 1MiB #{iteration}", () => Transfer(1048576, false));
                Console.WriteLine($"RESULT {_passed}/25 PASS"); return 0;
            }
            foreach (var size in new[] { 0, 1, 65536, 1048576 }) await Run($"client FIN {size} bytes", () => Transfer(size, false));
            await Run("target FIN preserves reverse channel", () => Transfer(65536, true));
            await Run("silent unauthenticated connection times out", () => Unauthenticated(false));
            await Run("partial handshake times out", () => Unauthenticated(true));
            await Run("wrong key cannot reach target", () => Reject(true));
            await Run("unapproved target cannot be reached", () => Reject(false));
            Console.WriteLine($"RESULT {_passed}/9 PASS"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine($"FAIL after {_passed} passed: {ex}"); return 1; }
    }
}
internal sealed class Ports : IPortAvailabilityService
{
    // Only the preflight availability check is stubbed. Production listeners, sockets, crypto, and policy run unchanged.
    public PortCheckResult CheckPort(int port, PortProtocol protocol) => new() { Port = port, IsAvailable = true };
    public PortCheckResult FindAvailablePort(int port, PortProtocol protocol, int maxAttempts = 100) => CheckPort(port, protocol);
}
internal sealed class Log : ILogService
{
    public event EventHandler<string>? EntryAdded { add { } remove { } }
    public string LogFilePath => "";
    public void Info(string message) { } public void Warning(string message) { Console.WriteLine("DIAGNOSTIC " + message); }
    public void Error(string message, Exception? exception = null) { Console.WriteLine("DIAGNOSTIC " + message + " " + exception?.Message); }
    public void Audit(string message) { }
    public IReadOnlyList<string> GetLogs() => Array.Empty<string>();
}

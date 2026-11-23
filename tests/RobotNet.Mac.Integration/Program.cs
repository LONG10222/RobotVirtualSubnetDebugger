using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RobotNet.Mac.Platform;
using RobotNet.Windows.Wpf.Models;
using RobotNet.Windows.Wpf.Services.Configuration;
using RobotNet.Windows.Wpf.Services.Discovery;
using RobotNet.Windows.Wpf.Services.Logging;
using RobotNet.Windows.Wpf.Services.Platform;
using RobotNet.Windows.Wpf.Services.Proxy;
using RobotNet.Windows.Wpf.Services.Updates;
using RobotNet.Windows.Wpf.Utils;

if (args is ["--stage-update", var archivePath])
{
    await TestUpdateArchiveAsync(archivePath);
    return;
}
if (args is ["--live-update", var destination])
{
    using var http = new HttpClient();
    var service = new GitHubReleaseUpdateService(http, new QuietLog());
    var release = await service.CheckForUpdatesAsync(new AppConfig());
    Require(release.Success && release.LatestVersion == service.CurrentVersion && release.Assets.Count > 0, release.Message);
    var update = await service.DownloadUpdateAsync(release);
    Require(update.Success, update.Message);
    File.Copy(update.FilePath, destination);
    await TestUpdateArchiveAsync(destination);
    Console.WriteLine($"通过 · 在线 GitHub Release {release.LatestVersion} 和 SHA256 下载");
    return;
}

var realAdapters = new MacNetworkAdapters().GetAdapters();
Require(realAdapters.Any(adapter => adapter.Name == "lo0"), "macOS 网卡枚举");
Require(CryptographicOperations.FixedTimeEquals(SHA256.HashData([1, 2]), SHA256.HashData([1, 2])),
    "macOS 系统加密");
TestNetworkPlans();
await TestDiscoveryAsync();
await TestProxyAsync();
await TestUpdateSelectionAsync();
Console.WriteLine("macOS 集成验证通过。");

static void TestNetworkPlans()
{
    var lan = new NetworkAdapterInfo
    {
        Name = "en0", IPv4Address = "192.168.88.2", SubnetMask = "255.255.255.0", Status = "Up", IsLanCandidate = true
    };
    var device = new NetworkAdapterInfo { Name = "en5", Status = "Up" };
    var planService = new MacNetworkPlan(new Adapters([lan, device]));
    var config = new AppConfig
    {
        Role = DeviceRole.GatewayAgent, GatewayLanIp = lan.IPv4Address,
        TargetDeviceIp = "192.168.1.10", TargetDevicePort = 30003, TargetDeviceAdapterIp = "192.168.1.101"
    };
    var plan = planService.BuildPlan(config);
    Require(plan.CanGenerateScripts && plan.TargetInterfaceName == "en5" && plan.WillModifySystem, "Mac 网关端计划");
    Require(plan.Steps.Any(step => step.Command == "nat on en5 inet from 192.168.88.0/24 to 192.168.1.0/24 -> 192.168.1.101"),
        "NAT 来源、目标、接口和转换地址");
    Require(MacNetworkPlan.ApplyCommand(plan).Contains("'gateway'"), "网关端命令");
    config.TargetDeviceInterfaceName = "en5; exit 1";
    Require(planService.BuildPlan(config).Status == DiagnosticStatus.Error, "无效接口输入");
    config.TargetDeviceInterfaceName = string.Empty;

    config.Role = DeviceRole.DebugClient;
    config.GatewayLanIp = "192.168.88.3";
    plan = planService.BuildPlan(config);
    Require(plan.CanGenerateScripts && plan.Steps.Single().Command == "route -n add -net 192.168.1.0/24 192.168.88.3", "Mac 调试端路由");
    config.TargetDeviceIp = "192.168.88.10";
    Require(planService.BuildPlan(config).Status == DiagnosticStatus.Error, "网段冲突");
    config.EnableVirtualSubnetMode = false;
    plan = planService.BuildPlan(config);
    Require(plan.Status == DiagnosticStatus.Success && !plan.WillModifySystem && !plan.RequiresAdministrator,
        "代理模式无需管理员授权");
    Console.WriteLine("通过 · 网卡枚举、网关 NAT 和调试端路由计划、无管理员代理模式");
}

static async Task TestDiscoveryAsync()
{
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    using var reservedPort = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
    var port = ((IPEndPoint)reservedPort.Client.LocalEndPoint!).Port;
    reservedPort.Close();
    var config = new AppConfig
    {
        DeviceId = Guid.NewGuid().ToString("N"), DeviceName = "Mac 调试端", Role = DeviceRole.DebugClient,
        DiscoveryPort = port
    };
    var service = new UdpDiscoveryService(new MemoryConfiguration(config), new Adapters([]), new QuietLog());
    var discoveryToken = PairingKeyDeriver.CreatePairingToken();
    var seen = new TaskCompletionSource<DeviceInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
    service.DeviceDiscovered += (_, device) =>
    {
        if (device.DeviceId == "gateway-id")
            seen.TrySetResult(device);
    };
    await service.StartAsync();
    try
    {
        using var sender = new UdpClient(AddressFamily.InterNetwork);
        var peer = new DiscoveryMessage
        {
            DeviceId = "gateway-id", ComputerName = "网关端", Role = DeviceRole.GatewayAgent, LanIp = "192.168.88.3",
            AutoPairingToken = discoveryToken, ProxyControlPort = 47833, TargetDeviceIp = "192.168.1.20",
            TargetDevicePort = 31000, TargetSubnetMask = "255.255.255.0", Timestamp = DateTimeOffset.Now
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(peer);
        await sender.SendAsync(bytes, new IPEndPoint(IPAddress.Loopback, port), deadline.Token);
        var actual = await seen.Task.WaitAsync(deadline.Token);
        Require(actual.SupportsAutoPairing && actual.ProxyControlPort == 47833 &&
            actual.TargetDeviceIp == "192.168.1.20" && actual.TargetDevicePort == 31000, "UDP 广播载荷兼容");
        Require(PairingKeyDeriver.DeriveSharedKey(peer.DeviceId, discoveryToken) ==
            PairingKeyDeriver.DeriveSharedKey(actual.DeviceId, actual.AutoPairingToken), "双方配对密钥一致");
    }
    finally
    {
        await service.StopAsync();
    }
    Console.WriteLine("通过 · UDP 发现接收、非默认控制端口、目标设备信息、自动配对");
}

static async Task TestProxyAsync()
{
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    var ports = new MacPortAvailability();
    var logs = new QuietLog();
    var target = new TcpListener(IPAddress.Loopback, 0);
    target.Start();
    var targetPort = ((IPEndPoint)target.LocalEndpoint).Port;
    Require(!ports.CheckPort(targetPort, PortProtocol.Tcp).IsAvailable, "识别已占用 TCP 端口");
    var controlPort = GetFreeTcpPort();
    var localPort = GetFreeTcpPort();
    var gateway = new SafeTcpProxyService(ports, logs);
    var client = new SafeTcpProxyService(ports, logs);
    var clientConfig = new AppConfig
    {
        Role = DeviceRole.DebugClient, GatewayLanIp = "127.0.0.1", ProxyControlPort = controlPort,
        LocalListenPort = localPort, SharedKey = "integration-test-shared-key",
        TargetDeviceIp = "127.0.0.1", TargetDevicePort = targetPort
    };
    var gatewayConfig = new AppConfig
    {
        Role = DeviceRole.GatewayAgent, ProxyControlPort = controlPort, SharedKey = clientConfig.SharedKey,
        TargetDeviceIp = clientConfig.TargetDeviceIp, TargetDevicePort = targetPort
    };
    try
    {
        Require((await gateway.StartAsync(gatewayConfig)).Status == TcpProxyStatus.Listening, "网关控制监听");
        Require((await client.StartAsync(clientConfig)).Status == TcpProxyStatus.Listening, "调试端代理监听");

        var payload = RandomNumberGenerator.GetBytes(80_000);
        using var local = new TcpClient();
        await local.ConnectAsync(IPAddress.Loopback, localPort, deadline.Token);
        using var endpoint = await target.AcceptTcpClientAsync(deadline.Token);
        var fromClient = new byte[payload.Length];
        var echoed = new byte[payload.Length];
        await Task.WhenAll(
            local.GetStream().WriteAsync(payload, deadline.Token).AsTask(),
            endpoint.GetStream().ReadExactlyAsync(fromClient, deadline.Token).AsTask());
        Require(payload.SequenceEqual(fromClient), "AES-GCM 数据从调试端到网关后的设备");
        await Task.WhenAll(
            endpoint.GetStream().WriteAsync(fromClient, deadline.Token).AsTask(),
            local.GetStream().ReadExactlyAsync(echoed, deadline.Token).AsTask());
        Require(payload.SequenceEqual(echoed), "AES-GCM 数据从设备返回调试端");
        await client.StopAsync();

        clientConfig.SharedKey = "wrong-shared-key";
        Require((await client.StartAsync(clientConfig)).Status == TcpProxyStatus.Listening, "错误密钥场景已准备");
        using var rejected = new TcpClient();
        await rejected.ConnectAsync(IPAddress.Loopback, localPort, deadline.Token);
        try
        {
            var rejection = await rejected.GetStream().ReadAsync(new byte[1024], deadline.Token);
            Require(rejection == 0, "网关拒绝错误密钥");
        }
        catch (IOException) when (!deadline.IsCancellationRequested)
        {
            // Rejecting authentication may close the TCP connection with a reset.
        }
        Require(!target.Pending(), "认证失败没有打开设备连接");
    }
    finally
    {
        await client.StopAsync();
        await gateway.StopAsync();
        target.Stop();
    }
    Console.WriteLine("通过 · TCP 端口检查、80 KB 加密双向传输、错误密钥拒绝、连接清理");
}

static async Task TestUpdateSelectionAsync()
{
    var architecture = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
    using var http = new HttpClient(new ReleaseResponse(architecture));
    var service = new GitHubReleaseUpdateService(http, new QuietLog());
    var result = await service.CheckForUpdatesAsync(new AppConfig());
    Require(result.Success && result.IsUpdateAvailable && result.LatestVersion == "0.9.0", "选择本机架构版本");
    Require(result.Assets.Any(item => item.Name.EndsWith(architecture + ".zip", StringComparison.Ordinal)), "选择本机架构 ZIP");
    Console.WriteLine("通过 · macOS 更新检查忽略仅含 Windows 安装包的发布");
}

static async Task TestUpdateArchiveAsync(string filePath)
{
    await using var file = File.OpenRead(filePath);
    var actual = Convert.ToHexString(await SHA256.HashDataAsync(file));
    var match = System.Text.RegularExpressions.Regex.Match(Path.GetFileName(filePath), @"RobotNet-(\d+\.\d+\.\d+)");
    Require(match.Success, "安装包文件名包含版本");
    var download = new UpdateDownloadResult { FilePath = Path.GetFullPath(filePath), Sha256 = actual };
    var stage = Directory.CreateTempSubdirectory("robotnet-test-");
    try
    {
        await MacUpdateInstaller.StageUpdateAsync(stage.FullName, download, match.Groups[1].Value);
        Require(File.Exists(Path.Combine(stage.FullName, "RobotNet.app", "Contents", "MacOS", "RobotNet.Mac")),
            "更新包正确解压和验证");
        Console.WriteLine($"通过 · 安装包 SHA256、签名、应用标识和版本 · {Path.GetFileName(filePath)}");
    }
    finally
    {
        stage.Delete(recursive: true);
    }
}

static int GetFreeTcpPort()
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    return port;
}

static void Require(bool condition, string description)
{
    if (!condition)
        throw new InvalidOperationException(description);
}

sealed class Adapters(IReadOnlyList<NetworkAdapterInfo> values) : INetworkAdapterService
{
    public IReadOnlyList<NetworkAdapterInfo> GetAdapters() => values;
    public IReadOnlyList<NetworkAdapterInfo> FindLanCandidates() => values.Where(value => value.IsLanCandidate).ToArray();
    public IReadOnlyList<NetworkAdapterInfo> FindTargetNetworkCandidates(string targetDeviceIp) =>
        values.Where(value => IsSameSubnet(value.IPv4Address, value.SubnetMask, targetDeviceIp)).ToArray();
    public bool IsSameSubnet(string ip1, string mask1, string ip2) =>
        MacIpv4.TryNetwork(ip1, mask1, out var network) && network.Contains(ip2);
}

sealed class MemoryConfiguration(AppConfig value) : IConfigurationService
{
    public string ConfigFilePath => string.Empty;
    public AppConfig CreateDefault() => new();
    public AppConfig Load() => value;
    public void Save(AppConfig config) => value = config;
}

sealed class QuietLog : ILogService
{
    public event EventHandler<string>? EntryAdded { add { } remove { } }
    public IReadOnlyList<string> Entries => [];
    public string LogFilePath => string.Empty;
    public void Info(string message) { }
    public void Warning(string message) { }
    public void Error(string message, Exception? exception = null) { }
    public void Audit(string message) { }
    public IReadOnlyList<string> GetLogs() => [];
}

sealed class ReleaseResponse(string architecture) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var releases = JsonSerializer.Serialize(new[]
        {
            new { tag_name = "v1.0.0", assets = new[] { Asset("RobotNet-win-x64.zip") } },
            new { tag_name = "macos-v0.9.1", assets = new[] { Asset($"RobotNet-0.9.1-{architecture}.zip") } },
            new { tag_name = "macos-v0.9.0", assets = new[] { Asset($"RobotNet-0.9.0-{architecture}.zip"), Asset("checksums-macos.sha256") } }
        });
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(releases, Encoding.UTF8, "application/json")
        });
    }

    private static object Asset(string name) => new { name, browser_download_url = "https://example.invalid/" + name };
}

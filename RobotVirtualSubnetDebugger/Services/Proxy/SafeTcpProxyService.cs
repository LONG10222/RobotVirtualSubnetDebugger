using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RobotNet.Windows.Wpf.Models;
using RobotNet.Windows.Wpf.Services.Logging;
using RobotNet.Windows.Wpf.Services.Platform;

namespace RobotNet.Windows.Wpf.Services.Proxy;

public sealed class SafeTcpProxyService : ITcpProxyService
{
    private const string DebugClientBindAddress = "127.0.0.1";
    private const string GatewayBindAddress = "0.0.0.0";
    private const int ConnectTimeoutMs = 5000;
    private const int MinSharedKeyLength = 8;
    private const int MaxControlLineBytes = 512 * 1024;
    private const int DataFrameBufferSize = 16 * 1024;
    private const int AesGcmNonceSize = 12;
    private const int AesGcmTagSize = 16;
    private static readonly TimeSpan MaxTimestampSkew = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan NonceRetention = TimeSpan.FromMinutes(10);

    private readonly IPortAvailabilityService _portAvailabilityService;
    private readonly ILogService _logService;
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, DateTimeOffset> _recentNonces = [];
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private TcpProxySessionInfo _current = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _acceptTask;
    private ProxyRuntimeOptions? _activeOptions;

    public SafeTcpProxyService(
        IPortAvailabilityService portAvailabilityService,
        ILogService logService)
    {
        _portAvailabilityService = portAvailabilityService;
        _logService = logService;
    }

    public event EventHandler<TcpProxySessionInfo>? StateChanged;

    public TcpProxySessionInfo Current => _current;

    public Task<PlatformOperationResult> CheckAsync(AppConfig config)
    {
        if (config.Role == DeviceRole.Unknown)
        {
            return Task.FromResult(CreateResult(
                DiagnosticStatus.Warning,
                "TCP 代理能力检查",
                "当前角色仍为 Unknown，TCP 代理需要明确调试端或网关端。",
                "请先在配置页选择本机角色。"));
        }

        if (!IsValidSharedKey(config.SharedKey))
        {
            return Task.FromResult(CreateResult(
                DiagnosticStatus.Error,
                "TCP 代理安全检查",
                "SharedKey 未配置或长度不足，第四阶段代理会拒绝未认证连接。",
                "请在配置页生成或填写至少 8 个字符的共享密钥，并确保两台电脑一致。"));
        }

        if (!IsValidPort(config.LocalListenPort) ||
            !IsValidPort(config.ProxyControlPort) ||
            !IsValidPort(config.TargetDevicePort))
        {
            return Task.FromResult(CreateResult(
                DiagnosticStatus.Error,
                "TCP 代理能力检查",
                "TCP 代理相关端口存在无效值。",
                "本地监听端口、代理控制端口和目标设备端口都必须在 1 到 65535 之间。"));
        }

        if (!IsValidStabilityOptions(config))
        {
            return Task.FromResult(CreateResult(
                DiagnosticStatus.Warning,
                "TCP 代理稳定性检查",
                "心跳、超时或重试参数不在推荐范围内。",
                "心跳应为 1-60 秒，空闲超时应大于心跳且不超过 300 秒，重试次数应为 0-10。"));
        }

        if (config.Role == DeviceRole.DebugClient)
        {
            var port = _portAvailabilityService.CheckPort(config.LocalListenPort, PortProtocol.Tcp);
            if (!port.IsAvailable && _current.Status != TcpProxyStatus.Listening)
            {
                return Task.FromResult(CreateResult(
                    DiagnosticStatus.Warning,
                    "TCP 代理能力检查",
                    $"本地监听端口 {config.LocalListenPort} 当前被占用：{port.OwnerText}。",
                    "一键连接前置检查会尝试自动切换到可用端口。"));
            }

            if (!IPAddress.TryParse(config.GatewayLanIp, out _))
            {
                return Task.FromResult(CreateResult(
                    DiagnosticStatus.Warning,
                    "TCP 代理能力检查",
                    "调试端尚未配置有效网关端 LAN IP。",
                    "请先通过设备发现选择网关端，或在配置页手动填写。"));
            }
        }

        if (config.Role == DeviceRole.GatewayAgent)
        {
            var port = _portAvailabilityService.CheckPort(config.ProxyControlPort, PortProtocol.Tcp);
            if (!port.IsAvailable && _current.Status != TcpProxyStatus.Listening)
            {
                return Task.FromResult(CreateResult(
                    DiagnosticStatus.Warning,
                    "TCP 代理能力检查",
                    $"代理控制端口 {config.ProxyControlPort} 当前被占用：{port.OwnerText}。",
                    "一键连接前置检查会尝试自动切换到可用端口。"));
            }

            if (!IPAddress.TryParse(config.TargetDeviceIp, out _))
            {
                return Task.FromResult(CreateResult(
                    DiagnosticStatus.Warning,
                    "TCP 代理能力检查",
                    "网关端尚未配置有效目标设备 IP。",
                    "请在配置页填写网关端可以访问的目标设备 IP。"));
            }
        }

        return Task.FromResult(CreateResult(
            DiagnosticStatus.Success,
            "TCP 代理安全与稳定性检查",
            "第四阶段代理已启用 SharedKey、HMAC-SHA256 签名、AES-GCM 数据加密、会话令牌、心跳、空闲超时和连接重试。",
            "调试端连接本机 LocalListenPort，网关端验证签名后连接 TargetDeviceIp:TargetDevicePort。"));
    }

    public Task<TcpProxySessionInfo> StartAsync(AppConfig config)
    {
        lock (_syncRoot)
        {
            if (_listener is not null)
            {
                return Task.FromResult(_current);
            }

            if (!IsValidSharedKey(config.SharedKey))
            {
                return Task.FromResult(SetFailed(config, "SharedKey 未配置或长度不足。请在两台电脑上配置相同的共享密钥。"));
            }

            if (!IsValidStabilityOptions(config))
            {
                return Task.FromResult(SetFailed(config, "代理心跳、超时或重试参数无效。请先在配置页修正。"));
            }

            return config.Role switch
            {
                DeviceRole.DebugClient => StartListener(
                    config,
                    IPAddress.Loopback,
                    DebugClientBindAddress,
                    config.LocalListenPort,
                    "调试端本地代理正在监听。调试代码可连接该本地端口。"),
                DeviceRole.GatewayAgent => StartListener(
                    config,
                    IPAddress.Any,
                    GatewayBindAddress,
                    config.ProxyControlPort,
                    "网关端代理控制端口正在监听。调试端可通过认证握手请求连接目标设备。"),
                _ => Task.FromResult(SetFailed(config, "请先在配置页选择调试端或网关端角色。"))
            };
        }
    }

    public async Task<TcpProxySessionInfo> StopAsync()
    {
        TcpListener? listener;
        CancellationTokenSource? cancellationTokenSource;
        Task? acceptTask;

        lock (_syncRoot)
        {
            listener = _listener;
            cancellationTokenSource = _cancellationTokenSource;
            acceptTask = _acceptTask;

            if (listener is null)
            {
                SetState(new TcpProxySessionInfo
                {
                    Status = TcpProxyStatus.Stopped,
                    LastUpdated = DateTimeOffset.Now,
                    Message = "TCP 代理已停止。"
                });
                return _current;
            }

            _current.Status = TcpProxyStatus.Stopping;
            _current.Message = "正在停止 TCP 监听。";
            _current.LastUpdated = DateTimeOffset.Now;
            StateChanged?.Invoke(this, Clone(_current));

            _listener = null;
            _cancellationTokenSource = null;
            _acceptTask = null;
            _activeOptions = null;
        }

        cancellationTokenSource?.Cancel();
        listener.Stop();

        if (acceptTask is not null)
        {
            try
            {
                await acceptTask;
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        cancellationTokenSource?.Dispose();

        SetState(new TcpProxySessionInfo
        {
            Status = TcpProxyStatus.Stopped,
            LastUpdated = DateTimeOffset.Now,
            Message = "TCP 监听已停止。"
        });
        _logService.Audit("TCP 代理监听已停止。");
        return _current;
    }

    private Task<TcpProxySessionInfo> StartListener(
        AppConfig config,
        IPAddress bindAddress,
        string bindAddressText,
        int listenPort,
        string message)
    {
        var portCheck = _portAvailabilityService.CheckPort(listenPort, PortProtocol.Tcp);
        if (!portCheck.IsAvailable)
        {
            return Task.FromResult(SetFailed(config, $"TCP 监听端口被占用：{portCheck.OwnerText}"));
        }

        try
        {
            _activeOptions = CreateRuntimeOptions(config);
            _cancellationTokenSource = new CancellationTokenSource();
            _listener = new TcpListener(bindAddress, listenPort);
            _listener.Start();
            SetState(CreateSession(config, TcpProxyStatus.Listening, bindAddressText, listenPort, message));
            _acceptTask = Task.Run(() => AcceptLoopAsync(_cancellationTokenSource.Token));
            _logService.Audit($"TCP 代理监听已启动：{bindAddressText}:{listenPort}，安全模式：HMAC-SHA256 + AES-GCM。");
        }
        catch (Exception ex)
        {
            _listener = null;
            _activeOptions = null;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            SetState(CreateSession(config, TcpProxyStatus.Failed, bindAddressText, listenPort, $"启动 TCP 监听失败：{ex.Message}", ex.Message));
            _logService.Error("TCP 代理监听启动失败。", ex);
        }

        return Task.FromResult(_current);
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener!.AcceptTcpClientAsync(cancellationToken);
                ConfigureTcpClient(client);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException ex)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    Fail($"接受 TCP 连接失败：{ex.Message}");
                }

                break;
            }

            _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
        }
    }

    private Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        return _current.Role == DeviceRole.GatewayAgent
            ? HandleGatewayControlClientAsync(client, cancellationToken)
            : HandleDebugClientLocalClientAsync(client, cancellationToken);
    }

    private async Task HandleDebugClientLocalClientAsync(TcpClient localClient, CancellationToken cancellationToken)
    {
        var connectionId = Guid.NewGuid().ToString("N")[..8];
        var session = Clone(_current);
        var options = GetActiveOptions();
        var context = new FrameBridgeContext(session.SessionId, connectionId, CreateToken(), options);
        UpdateConnectionCount(1, TcpProxyStatus.Forwarding, $"本地连接 {connectionId} 已进入，正在连接网关端。");

        TcpClient? gatewayClient = null;
        NetworkStream? localStream = null;
        NetworkStream? gatewayStream = null;
        try
        {
            gatewayClient = await ConnectWithRetryAsync(
                session.GatewayLanIp,
                session.ProxyControlPort,
                options,
                cancellationToken,
                "网关端代理");

            localStream = localClient.GetStream();
            gatewayStream = gatewayClient.GetStream();

            var openMessage = new TcpProxyControlMessage
            {
                MessageType = TcpProxyMessageType.OpenConnection,
                TargetHost = session.TargetDeviceIp,
                TargetPort = session.TargetDevicePort,
                PayloadText = "Open target connection"
            };

            await SendFrameAsync(gatewayStream, openMessage, context, cancellationToken);
            var result = await ReadVerifiedMessageAsync(gatewayStream, options.SharedKey, context.SessionToken, cancellationToken);
            if (result.MessageType != TcpProxyMessageType.OpenConnectionResult || !result.Success)
            {
                var error = string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? "网关端未能打开目标连接。"
                    : result.ErrorMessage;
                await WriteTextToClientAsync(localStream, $"RobotNet TCP proxy failed: {error}\r\n", cancellationToken);
                UpdateMessage($"连接 {connectionId} 打开失败：{error}");
                _logService.Audit($"TCP 代理连接被拒绝：ConnectionId={connectionId}，原因={error}");
                return;
            }

            TouchHeartbeat();
            UpdateMessage($"连接 {connectionId} 已通过认证并建立，正在转发加密签名数据帧。");
            _logService.Audit($"TCP 代理连接已建立：ConnectionId={connectionId}，Target={session.TargetEndpointText}");

            await RunFramedBridgeAsync(
                localClient,
                gatewayClient,
                localStream,
                gatewayStream,
                context,
                cancellationToken);

            UpdateMessage($"连接 {connectionId} 已关闭。");
            _logService.Audit($"TCP 代理连接已关闭：ConnectionId={connectionId}");
        }
        catch (Exception ex) when (IsExpectedConnectionException(ex))
        {
            UpdateError($"调试端代理连接 {connectionId} 失败：{ex.Message}");
            _logService.Audit($"TCP 代理连接失败：ConnectionId={connectionId}，原因={ex.Message}");
        }
        finally
        {
            gatewayStream?.Dispose();
            localStream?.Dispose();
            gatewayClient?.Dispose();
            localClient.Dispose();
            context.Dispose();
            UpdateConnectionCount(-1, TcpProxyStatus.Listening, $"继续监听 {_current.EndpointText}。");
        }
    }

    private async Task HandleGatewayControlClientAsync(TcpClient controlClient, CancellationToken cancellationToken)
    {
        var connectionId = string.Empty;
        var options = GetActiveOptions();
        FrameBridgeContext? context = null;
        UpdateConnectionCount(1, TcpProxyStatus.Forwarding, "收到调试端代理控制连接，正在等待认证 OpenConnection。");

        TcpClient? targetClient = null;
        NetworkStream? controlStream = null;
        NetworkStream? targetStream = null;
        try
        {
            controlStream = controlClient.GetStream();
            var openMessage = await ReadVerifiedMessageAsync(controlStream, options.SharedKey, expectedSessionToken: null, cancellationToken);
            connectionId = string.IsNullOrWhiteSpace(openMessage.ConnectionId)
                ? Guid.NewGuid().ToString("N")[..8]
                : openMessage.ConnectionId;

            context = new FrameBridgeContext(openMessage.SessionId, connectionId, openMessage.SessionToken, options);
            if (openMessage.MessageType != TcpProxyMessageType.OpenConnection)
            {
                await SendFrameAsync(controlStream, CreateOpenResult(false, "无效的代理控制消息。"), context, cancellationToken);
                return;
            }

            var targetHost = string.IsNullOrWhiteSpace(openMessage.TargetHost) ? _current.TargetDeviceIp : openMessage.TargetHost;
            var targetPort = openMessage.TargetPort > 0 ? openMessage.TargetPort : _current.TargetDevicePort;

            _logService.Audit($"网关端认证通过：ConnectionId={connectionId}，Target={targetHost}:{targetPort}");
            targetClient = await ConnectWithRetryAsync(
                targetHost,
                targetPort,
                options,
                cancellationToken,
                "目标设备");

            targetStream = targetClient.GetStream();
            await SendFrameAsync(controlStream, CreateOpenResult(true, string.Empty), context, cancellationToken);

            TouchHeartbeat();
            UpdateMessage($"网关端连接 {connectionId} 已连接目标 {targetHost}:{targetPort}，正在转发加密签名数据帧。");
            await RunFramedBridgeAsync(
                targetClient,
                controlClient,
                targetStream,
                controlStream,
                context,
                cancellationToken);

            UpdateMessage($"网关端连接 {connectionId} 已关闭。");
            _logService.Audit($"网关端代理连接已关闭：ConnectionId={connectionId}");
        }
        catch (Exception ex) when (IsExpectedConnectionException(ex))
        {
            UpdateError($"网关端代理连接 {connectionId} 失败：{ex.Message}");
            _logService.Audit($"网关端代理连接失败：ConnectionId={connectionId}，原因={ex.Message}");
            try
            {
                if (context is not null && controlStream is not null && controlClient.Connected)
                {
                    await SendFrameAsync(controlStream, CreateOpenResult(false, ex.Message), context, cancellationToken);
                }
            }
            catch
            {
            }
        }
        finally
        {
            targetStream?.Dispose();
            controlStream?.Dispose();
            targetClient?.Dispose();
            controlClient.Dispose();
            context?.Dispose();
            UpdateConnectionCount(-1, TcpProxyStatus.Listening, $"继续监听 {_current.EndpointText}。");
        }
    }

    private async Task RunFramedBridgeAsync(
        TcpClient plainClient,
        TcpClient framedClient,
        Stream plainStream,
        Stream framedStream,
        FrameBridgeContext context,
        CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = linkedCts.Token;
        SetLastInboundFrameTime(context);

        var plainToFrames = CopyPlainToFramesAsync(plainStream, framedStream, context, token);
        var framesToPlain = CopyFramesToPlainAsync(framedStream, plainStream, context, token);
        var heartbeat = SendHeartbeatLoopAsync(framedStream, context, token);
        var watchdog = WatchdogAsync(context, token);

        var completed = await Task.WhenAny(plainToFrames, framesToPlain, heartbeat, watchdog);
        if (completed == watchdog || completed == heartbeat)
        {
            await completed;
        }

        linkedCts.Cancel();
        CloseTcpClient(plainClient);
        CloseTcpClient(framedClient);

        try
        {
            await Task.WhenAll(plainToFrames, framesToPlain, heartbeat, watchdog);
        }
        catch (Exception ex) when (IsExpectedConnectionException(ex))
        {
        }
    }

    private async Task CopyPlainToFramesAsync(
        Stream plainStream,
        Stream framedStream,
        FrameBridgeContext context,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[DataFrameBufferSize];
        while (!cancellationToken.IsCancellationRequested)
        {
            var read = await plainStream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                await SendFrameAsync(
                    framedStream,
                    new TcpProxyControlMessage
                    {
                        MessageType = TcpProxyMessageType.CloseConnection,
                        PayloadText = "Plain stream closed"
                    },
                    context,
                    cancellationToken);
                break;
            }

            await SendFrameAsync(
                framedStream,
                new TcpProxyControlMessage
                {
                    MessageType = TcpProxyMessageType.Data,
                    PayloadBase64 = Convert.ToBase64String(buffer.AsSpan(0, read))
                },
                context,
                cancellationToken);
            AddBytes(sent: read, received: 0);
        }
    }

    private async Task CopyFramesToPlainAsync(
        Stream framedStream,
        Stream plainStream,
        FrameBridgeContext context,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var frame = await ReadVerifiedMessageAsync(
                framedStream,
                context.Options.SharedKey,
                context.SessionToken,
                cancellationToken);
            SetLastInboundFrameTime(context);

            switch (frame.MessageType)
            {
                case TcpProxyMessageType.Heartbeat:
                    TouchHeartbeat();
                    break;
                case TcpProxyMessageType.Data:
                    var payload = DecryptPayload(frame, context);
                    await plainStream.WriteAsync(payload, cancellationToken);
                    await plainStream.FlushAsync(cancellationToken);
                    AddBytes(sent: 0, received: payload.Length);
                    break;
                case TcpProxyMessageType.CloseConnection:
                    return;
                case TcpProxyMessageType.Error:
                    throw new IOException(string.IsNullOrWhiteSpace(frame.ErrorMessage)
                        ? "对端返回代理错误。"
                        : frame.ErrorMessage);
                default:
                    throw new InvalidDataException($"代理数据阶段收到不支持的消息类型：{frame.MessageType}。");
            }
        }
    }

    private async Task SendHeartbeatLoopAsync(
        Stream framedStream,
        FrameBridgeContext context,
        CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(context.Options.HeartbeatIntervalSeconds);
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(interval, cancellationToken);
            await SendFrameAsync(
                framedStream,
                new TcpProxyControlMessage
                {
                    MessageType = TcpProxyMessageType.Heartbeat,
                    PayloadText = "PING"
                },
                context,
                cancellationToken);
            TouchHeartbeat();
        }
    }

    private async Task WatchdogAsync(FrameBridgeContext context, CancellationToken cancellationToken)
    {
        var idleTimeout = TimeSpan.FromSeconds(context.Options.IdleTimeoutSeconds);
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            var lastInbound = new DateTime(Interlocked.Read(ref context.LastInboundFrameUtcTicks), DateTimeKind.Utc);
            var idle = DateTime.UtcNow - lastInbound;
            if (idle > idleTimeout)
            {
                throw new TimeoutException($"代理连接 {context.ConnectionId} 心跳超时，已空闲 {idle.TotalSeconds:F0} 秒。");
            }
        }
    }

    private async Task<TcpClient> ConnectWithRetryAsync(
        string host,
        int port,
        ProxyRuntimeOptions options,
        CancellationToken cancellationToken,
        string label)
    {
        Exception? lastException = null;
        var totalAttempts = Math.Max(1, options.ReconnectAttempts + 1);
        for (var attempt = 1; attempt <= totalAttempts; attempt++)
        {
            var client = new TcpClient();
            ConfigureTcpClient(client);
            try
            {
                await ConnectWithTimeoutAsync(client, host, port, cancellationToken);
                if (attempt > 1)
                {
                    _logService.Audit($"{label} {host}:{port} 第 {attempt} 次尝试连接成功。");
                }

                return client;
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or TimeoutException)
            {
                lastException = ex;
                client.Dispose();
                if (attempt >= totalAttempts)
                {
                    break;
                }

                _logService.Warning($"{label} {host}:{port} 第 {attempt} 次连接失败：{ex.Message}，准备重试。");
                await Task.Delay(RetryDelay, cancellationToken);
            }
        }

        throw new IOException($"{label} {host}:{port} 连接失败，已尝试 {totalAttempts} 次。", lastException);
    }

    private static async Task ConnectWithTimeoutAsync(TcpClient client, string host, int port, CancellationToken cancellationToken)
    {
        var connectTask = client.ConnectAsync(host, port, cancellationToken).AsTask();
        var timeoutTask = Task.Delay(ConnectTimeoutMs, cancellationToken);
        var completed = await Task.WhenAny(connectTask, timeoutTask);
        if (completed == timeoutTask)
        {
            throw new TimeoutException($"连接 {host}:{port} 超时。");
        }

        await connectTask;
    }

    private async Task<TcpProxyControlMessage> ReadVerifiedMessageAsync(
        Stream stream,
        string sharedKey,
        string? expectedSessionToken,
        CancellationToken cancellationToken)
    {
        var message = await ReadControlMessageAsync(stream, cancellationToken)
            ?? throw new IOException("代理控制连接已关闭。");
        var validationError = ValidateSignedMessage(message, sharedKey, expectedSessionToken);
        if (!string.IsNullOrWhiteSpace(validationError))
        {
            throw new UnauthorizedAccessException(validationError);
        }

        return message;
    }

    private async Task<TcpProxyControlMessage?> ReadControlMessageAsync(Stream stream, CancellationToken cancellationToken)
    {
        var line = await ReadLineAsync(stream, cancellationToken);
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        return JsonSerializer.Deserialize<TcpProxyControlMessage>(line, _jsonOptions);
    }

    private async Task SendFrameAsync(
        Stream stream,
        TcpProxyControlMessage message,
        FrameBridgeContext context,
        CancellationToken cancellationToken)
    {
        message.ProtocolVersion = 1;
        message.SessionId = context.SessionId;
        message.ConnectionId = context.ConnectionId;
        message.SessionToken = context.SessionToken;
        message.Sequence = Interlocked.Increment(ref context.Sequence);
        await WriteSignedMessageAsync(stream, message, context.Options.SharedKey, context.WriterLock, cancellationToken);
    }

    private async Task WriteSignedMessageAsync(
        Stream stream,
        TcpProxyControlMessage message,
        string sharedKey,
        SemaphoreSlim writerLock,
        CancellationToken cancellationToken)
    {
        EncryptPayloadIfNeeded(message, sharedKey);
        SignMessage(message, sharedKey);
        var json = JsonSerializer.Serialize(message, _jsonOptions) + "\n";
        var bytes = Encoding.UTF8.GetBytes(json);

        await writerLock.WaitAsync(cancellationToken);
        try
        {
            await stream.WriteAsync(bytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        finally
        {
            writerLock.Release();
        }
    }

    private static async Task<string> ReadLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new List<byte>();
        var singleByte = new byte[1];
        while (buffer.Count < MaxControlLineBytes)
        {
            var read = await stream.ReadAsync(singleByte, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (singleByte[0] == (byte)'\n')
            {
                break;
            }

            if (singleByte[0] != (byte)'\r')
            {
                buffer.Add(singleByte[0]);
            }
        }

        if (buffer.Count >= MaxControlLineBytes)
        {
            throw new InvalidDataException("代理控制消息超过最大长度。");
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static async Task WriteTextToClientAsync(Stream stream, string message, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static TcpProxyControlMessage CreateOpenResult(bool success, string errorMessage)
    {
        return new TcpProxyControlMessage
        {
            MessageType = TcpProxyMessageType.OpenConnectionResult,
            Success = success,
            ErrorMessage = errorMessage,
            PayloadText = success ? "OK" : "ERROR"
        };
    }

    private static void SignMessage(TcpProxyControlMessage message, string sharedKey)
    {
        message.Timestamp = DateTimeOffset.UtcNow;
        message.Nonce = string.IsNullOrWhiteSpace(message.Nonce) ? CreateToken() : message.Nonce;
        message.Signature = string.Empty;
        message.Signature = ComputeSignature(message, sharedKey);
    }

    private string ValidateSignedMessage(
        TcpProxyControlMessage message,
        string sharedKey,
        string? expectedSessionToken)
    {
        if (message.ProtocolVersion != 1)
        {
            return $"不支持的代理协议版本：{message.ProtocolVersion}。";
        }

        if (string.IsNullOrWhiteSpace(message.SessionToken))
        {
            return "代理控制消息缺少会话令牌。";
        }

        if (!string.IsNullOrWhiteSpace(expectedSessionToken) &&
            !string.Equals(message.SessionToken, expectedSessionToken, StringComparison.Ordinal))
        {
            return "代理控制消息会话令牌不匹配。";
        }

        if (string.IsNullOrWhiteSpace(message.Nonce))
        {
            return "代理控制消息缺少 nonce。";
        }

        if (string.IsNullOrWhiteSpace(message.Signature))
        {
            return "代理控制消息缺少签名。";
        }

        var skew = (DateTimeOffset.UtcNow - message.Timestamp.ToUniversalTime()).Duration();
        if (skew > MaxTimestampSkew)
        {
            return "代理控制消息时间戳超出允许范围。";
        }

        if (!TryRememberNonce(message.Nonce))
        {
            return "检测到重复 nonce，疑似重放消息。";
        }

        var expectedSignature = ComputeSignature(message, sharedKey);
        var actualBytes = Encoding.UTF8.GetBytes(message.Signature);
        var expectedBytes = Encoding.UTF8.GetBytes(expectedSignature);
        if (actualBytes.Length != expectedBytes.Length ||
            !CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes))
        {
            return "代理控制消息签名校验失败。";
        }

        return string.Empty;
    }

    private static string ComputeSignature(TcpProxyControlMessage message, string sharedKey)
    {
        var canonical = string.Join('\n',
            message.ProtocolVersion.ToString(CultureInfo.InvariantCulture),
            message.SessionId,
            message.ConnectionId,
            message.SessionToken,
            message.MessageType.ToString(),
            message.Success ? "1" : "0",
            message.Sequence.ToString(CultureInfo.InvariantCulture),
            message.TargetHost,
            message.TargetPort.ToString(CultureInfo.InvariantCulture),
            message.PayloadText,
            message.PayloadBase64,
            message.EncryptionNonceBase64,
            message.EncryptionTagBase64,
            message.ErrorMessage,
            message.Nonce,
            message.Timestamp.ToUniversalTime().ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(sharedKey));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical)));
    }

    private static void EncryptPayloadIfNeeded(TcpProxyControlMessage message, string sharedKey)
    {
        if (message.MessageType != TcpProxyMessageType.Data || string.IsNullOrWhiteSpace(message.PayloadBase64))
        {
            return;
        }

        var plaintext = Convert.FromBase64String(message.PayloadBase64);
        var ciphertext = new byte[plaintext.Length];
        var nonce = RandomNumberGenerator.GetBytes(AesGcmNonceSize);
        var tag = new byte[AesGcmTagSize];
        var key = DeriveEncryptionKey(sharedKey, message.SessionToken);
        var associatedData = CreateEncryptionAssociatedData(message);

        using var aes = new AesGcm(key, AesGcmTagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);

        message.PayloadBase64 = Convert.ToBase64String(ciphertext);
        message.EncryptionNonceBase64 = Convert.ToBase64String(nonce);
        message.EncryptionTagBase64 = Convert.ToBase64String(tag);
        message.PayloadText = "AES-GCM";
    }

    private static byte[] DecryptPayload(TcpProxyControlMessage message, FrameBridgeContext context)
    {
        if (message.MessageType != TcpProxyMessageType.Data)
        {
            return [];
        }

        if (string.IsNullOrWhiteSpace(message.PayloadBase64) ||
            string.IsNullOrWhiteSpace(message.EncryptionNonceBase64) ||
            string.IsNullOrWhiteSpace(message.EncryptionTagBase64))
        {
            throw new InvalidDataException("数据帧缺少加密载荷。");
        }

        var ciphertext = Convert.FromBase64String(message.PayloadBase64);
        var plaintext = new byte[ciphertext.Length];
        var nonce = Convert.FromBase64String(message.EncryptionNonceBase64);
        var tag = Convert.FromBase64String(message.EncryptionTagBase64);
        var key = DeriveEncryptionKey(context.Options.SharedKey, context.SessionToken);
        var associatedData = CreateEncryptionAssociatedData(message);

        using var aes = new AesGcm(key, AesGcmTagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
        return plaintext;
    }

    private static byte[] DeriveEncryptionKey(string sharedKey, string sessionToken)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes($"{sharedKey}\n{sessionToken}\nRobotNetTcpProxyFrameV1"));
    }

    private static byte[] CreateEncryptionAssociatedData(TcpProxyControlMessage message)
    {
        return Encoding.UTF8.GetBytes(string.Join('|',
            message.ProtocolVersion.ToString(CultureInfo.InvariantCulture),
            message.SessionId,
            message.ConnectionId,
            message.SessionToken,
            message.Sequence.ToString(CultureInfo.InvariantCulture),
            message.MessageType.ToString()));
    }

    private bool TryRememberNonce(string nonce)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_syncRoot)
        {
            var expired = _recentNonces
                .Where(pair => now - pair.Value > NonceRetention)
                .Select(pair => pair.Key)
                .ToList();
            foreach (var key in expired)
            {
                _recentNonces.Remove(key);
            }

            if (_recentNonces.ContainsKey(nonce))
            {
                return false;
            }

            _recentNonces[nonce] = now;
            return true;
        }
    }

    private static string CreateToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }

    private static void ConfigureTcpClient(TcpClient client)
    {
        client.NoDelay = true;
        client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
    }

    private static void CloseTcpClient(TcpClient client)
    {
        try
        {
            client.Client.Shutdown(SocketShutdown.Both);
        }
        catch
        {
        }

        client.Dispose();
    }

    private void AddBytes(long sent, long received)
    {
        lock (_syncRoot)
        {
            _current.BytesSent += sent;
            _current.BytesReceived += received;
            _current.LastUpdated = DateTimeOffset.Now;
            StateChanged?.Invoke(this, Clone(_current));
        }
    }

    private void UpdateConnectionCount(int delta, TcpProxyStatus status, string message)
    {
        lock (_syncRoot)
        {
            _current.ActiveConnections = Math.Max(0, _current.ActiveConnections + delta);
            var listenerStopped = _listener is null && _current.Status is TcpProxyStatus.Stopping or TcpProxyStatus.Stopped;
            _current.Status = listenerStopped
                ? TcpProxyStatus.Stopped
                : _current.ActiveConnections > 0 ? TcpProxyStatus.Forwarding : status;
            _current.Message = message;
            _current.LastUpdated = DateTimeOffset.Now;
            StateChanged?.Invoke(this, Clone(_current));
        }
    }

    private void UpdateMessage(string message)
    {
        lock (_syncRoot)
        {
            _current.Message = message;
            _current.LastUpdated = DateTimeOffset.Now;
            StateChanged?.Invoke(this, Clone(_current));
        }

        _logService.Info(message);
    }

    private void UpdateError(string message)
    {
        lock (_syncRoot)
        {
            _current.LastError = message;
            _current.Message = message;
            _current.LastUpdated = DateTimeOffset.Now;
            StateChanged?.Invoke(this, Clone(_current));
        }

        _logService.Warning(message);
    }

    private void TouchHeartbeat()
    {
        lock (_syncRoot)
        {
            _current.LastHeartbeatAt = DateTimeOffset.Now;
            _current.LastUpdated = DateTimeOffset.Now;
            StateChanged?.Invoke(this, Clone(_current));
        }
    }

    private static void SetLastInboundFrameTime(FrameBridgeContext context)
    {
        Interlocked.Exchange(ref context.LastInboundFrameUtcTicks, DateTime.UtcNow.Ticks);
    }

    private void Fail(string message)
    {
        lock (_syncRoot)
        {
            _current.Status = TcpProxyStatus.Failed;
            _current.LastError = message;
            _current.Message = message;
            _current.LastUpdated = DateTimeOffset.Now;
            StateChanged?.Invoke(this, Clone(_current));
        }

        _logService.Warning(message);
    }

    private TcpProxySessionInfo SetFailed(AppConfig config, string message)
    {
        var bindAddress = config.Role == DeviceRole.GatewayAgent ? GatewayBindAddress : DebugClientBindAddress;
        var port = config.Role == DeviceRole.GatewayAgent ? config.ProxyControlPort : config.LocalListenPort;
        SetState(CreateSession(config, TcpProxyStatus.Failed, bindAddress, port, message, message));
        return _current;
    }

    private void SetState(TcpProxySessionInfo session)
    {
        _current = session;
        StateChanged?.Invoke(this, Clone(_current));
    }

    private ProxyRuntimeOptions GetActiveOptions()
    {
        lock (_syncRoot)
        {
            return _activeOptions ?? throw new InvalidOperationException("TCP 代理运行参数尚未初始化。");
        }
    }

    private static ProxyRuntimeOptions CreateRuntimeOptions(AppConfig config)
    {
        return new ProxyRuntimeOptions(
            config.SharedKey.Trim(),
            config.ProxyHeartbeatIntervalSeconds,
            config.ProxyIdleTimeoutSeconds,
            config.ProxyReconnectAttempts);
    }

    private static TcpProxySessionInfo CreateSession(
        AppConfig config,
        TcpProxyStatus status,
        string bindAddress,
        int listenPort,
        string message,
        string lastError = "")
    {
        return new TcpProxySessionInfo
        {
            SessionId = Guid.NewGuid().ToString("N")[..8],
            Status = status,
            Role = config.Role,
            LocalBindAddress = bindAddress,
            LocalListenPort = listenPort,
            GatewayLanIp = config.GatewayLanIp,
            ProxyControlPort = config.ProxyControlPort,
            TargetDeviceIp = config.TargetDeviceIp,
            TargetDevicePort = config.TargetDevicePort,
            SecurityMode = IsValidSharedKey(config.SharedKey) ? "SharedKey + HMAC-SHA256 + AES-GCM" : "未启用",
            HeartbeatIntervalSeconds = config.ProxyHeartbeatIntervalSeconds,
            IdleTimeoutSeconds = config.ProxyIdleTimeoutSeconds,
            ReconnectAttempts = config.ProxyReconnectAttempts,
            StartedAt = DateTimeOffset.Now,
            LastUpdated = DateTimeOffset.Now,
            Message = message,
            LastError = lastError
        };
    }

    private static TcpProxySessionInfo Clone(TcpProxySessionInfo session)
    {
        return new TcpProxySessionInfo
        {
            SessionId = session.SessionId,
            Status = session.Status,
            Role = session.Role,
            LocalBindAddress = session.LocalBindAddress,
            LocalListenPort = session.LocalListenPort,
            GatewayLanIp = session.GatewayLanIp,
            ProxyControlPort = session.ProxyControlPort,
            TargetDeviceIp = session.TargetDeviceIp,
            TargetDevicePort = session.TargetDevicePort,
            ActiveConnections = session.ActiveConnections,
            BytesSent = session.BytesSent,
            BytesReceived = session.BytesReceived,
            SecurityMode = session.SecurityMode,
            HeartbeatIntervalSeconds = session.HeartbeatIntervalSeconds,
            IdleTimeoutSeconds = session.IdleTimeoutSeconds,
            ReconnectAttempts = session.ReconnectAttempts,
            LastHeartbeatAt = session.LastHeartbeatAt,
            LastError = session.LastError,
            Message = session.Message,
            StartedAt = session.StartedAt,
            LastUpdated = session.LastUpdated
        };
    }

    private static PlatformOperationResult CreateResult(
        DiagnosticStatus status,
        string name,
        string message,
        string suggestion)
    {
        return new PlatformOperationResult
        {
            Name = name,
            Status = status,
            Message = message,
            Suggestion = suggestion,
            RequiresAdministrator = false
        };
    }

    private static bool IsValidPort(int port)
    {
        return port is > 0 and <= 65535;
    }

    private static bool IsValidSharedKey(string sharedKey)
    {
        return !string.IsNullOrWhiteSpace(sharedKey) && sharedKey.Trim().Length >= MinSharedKeyLength;
    }

    private static bool IsValidStabilityOptions(AppConfig config)
    {
        return config.ProxyHeartbeatIntervalSeconds is > 0 and <= 60 &&
               config.ProxyIdleTimeoutSeconds > config.ProxyHeartbeatIntervalSeconds &&
               config.ProxyIdleTimeoutSeconds <= 300 &&
               config.ProxyReconnectAttempts is >= 0 and <= 10;
    }

    private static bool IsExpectedConnectionException(Exception ex)
    {
        return ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException or TimeoutException or UnauthorizedAccessException or InvalidDataException or JsonException or FormatException or InvalidOperationException;
    }

    private sealed record ProxyRuntimeOptions(
        string SharedKey,
        int HeartbeatIntervalSeconds,
        int IdleTimeoutSeconds,
        int ReconnectAttempts);

    private sealed class FrameBridgeContext : IDisposable
    {
        public FrameBridgeContext(
            string sessionId,
            string connectionId,
            string sessionToken,
            ProxyRuntimeOptions options)
        {
            SessionId = sessionId;
            ConnectionId = connectionId;
            SessionToken = sessionToken;
            Options = options;
            LastInboundFrameUtcTicks = DateTime.UtcNow.Ticks;
        }

        public string SessionId { get; }

        public string ConnectionId { get; }

        public string SessionToken { get; }

        public ProxyRuntimeOptions Options { get; }

        public SemaphoreSlim WriterLock { get; } = new(1, 1);

        public long Sequence;

        public long LastInboundFrameUtcTicks;

        public void Dispose()
        {
            WriterLock.Dispose();
        }
    }
}

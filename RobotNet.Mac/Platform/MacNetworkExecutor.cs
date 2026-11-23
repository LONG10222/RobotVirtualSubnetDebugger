using RobotNet.Windows.Wpf.Models;
using RobotNet.Windows.Wpf.Services.Discovery;
using RobotNet.Windows.Wpf.Services.Logging;
using RobotNet.Windows.Wpf.Services.Platform;
using RobotNet.Windows.Wpf.Services.Proxy;

namespace RobotNet.Mac.Platform;

internal sealed class MacNetworkExecutor(
    MacNetworkPlan plans,
    IOperationRollbackService journal,
    ITcpProxyService proxy,
    IDiscoveryService discovery,
    ILogService logs) : INetworkConfigurationExecutor
{
    private readonly SemaphoreSlim _operation = new(1, 1);
    public event EventHandler<string>? ExecutionLogAdded;

    public VirtualSubnetPlan PreviewGatewayConfiguration(AppConfig config) => Preview(config, DeviceRole.GatewayAgent);
    public VirtualSubnetPlan PreviewClientConfiguration(AppConfig config) => Preview(config, DeviceRole.DebugClient);
    public Task<NetworkConfigurationExecutionResult> ApplyGatewayConfigurationAsync(AppConfig config) =>
        ApplyAsync(config, DeviceRole.GatewayAgent);
    public Task<NetworkConfigurationExecutionResult> ApplyClientConfigurationAsync(AppConfig config) =>
        ApplyAsync(config, DeviceRole.DebugClient);

    public NetworkOperationRecord? GetLastOperation() => journal.LoadLastOperation();

    private VirtualSubnetPlan Preview(AppConfig config, DeviceRole role)
    {
        var savedRole = config.Role;
        try
        {
            config.Role = role;
            return plans.BuildPlan(config);
        }
        finally
        {
            config.Role = savedRole;
        }
    }

    private async Task<NetworkConfigurationExecutionResult> ApplyAsync(AppConfig config, DeviceRole role)
    {
        await _operation.WaitAsync().ConfigureAwait(false);
        NetworkOperationRecord? record = null;
        var privilegedChange = false;
        var proxyAttempted = false;
        try
        {
            config.Role = role;
            var plan = plans.BuildPlan(config);
            if (plan.Status == DiagnosticStatus.Error)
                return Failure(plan.Summary);
            var previous = GetLastOperation();
            if (previous is { RolledBackAt: null } && previous.RollbackCommands.Count > 0)
                return Failure("已有系统网络配置记录。请回滚后再应用。");
            if (config.EnableTcpProxyMode)
            {
                var check = await proxy.CheckAsync(config).ConfigureAwait(false);
                if (check.Status == DiagnosticStatus.Error)
                    return Failure(check.Message);
                if (proxy.Current.Status is TcpProxyStatus.Listening or TcpProxyStatus.Forwarding)
                    return Failure("已有 TCP 代理正在运行。请停止或回滚后重试。");
            }

            record = new NetworkOperationRecord
            {
                Role = role,
                Summary = plan.Summary,
                TargetSubnetCidr = plan.TargetSubnetCidr,
                GatewayLanIp = plan.GatewayLanIp,
                Status = NetworkConfigurationApplyStatus.Applying,
                AppliedCommands = plan.Steps.Select(step => step.Command).ToList()
            };
            if (plan.WillModifySystem)
                record.RollbackCommands.Add("恢复 RobotNet 系统网络配置");
            journal.SaveOperation(record);

            if (plan.WillModifySystem)
            {
                var output = await MacPrivilege.RunAsync(MacNetworkPlan.ApplyCommand(plan)).ConfigureAwait(false);
                privilegedChange = true;
                Log(record, output);
            }
            if (config.EnableTcpProxyMode)
            {
                proxyAttempted = true;
                var result = await proxy.StartAsync(config).ConfigureAwait(false);
                if (result.Status != TcpProxyStatus.Listening)
                    throw new InvalidOperationException(result.Message);
                Log(record, result.Message);
            }

            record.AppliedAt = DateTimeOffset.Now;
            record.Status = NetworkConfigurationApplyStatus.Applied;
            journal.SaveOperation(record);
            return Result(record, true, "调试通道已就绪。请用目标设备验证实际连通性。");
        }
        catch (Exception ex)
        {
            if (record is null)
                return Failure(ex.Message);
            if (proxyAttempted)
                await proxy.StopAsync().ConfigureAwait(false);

            record.LastError = ex.Message;
            if (privilegedChange)
            {
                try
                {
                    Log(record, await MacPrivilege.RunAsync(MacNetworkPlan.RollbackCommand()).ConfigureAwait(false));
                    record.RolledBackAt = DateTimeOffset.Now;
                }
                catch (Exception rollbackError)
                {
                    record.LastError += $" 自动回滚失败：{rollbackError.Message}";
                }
            }
            else if (!ex.Message.Contains("自动回滚未完成", StringComparison.Ordinal))
            {
                record.RolledBackAt = DateTimeOffset.Now;
            }

            record.Status = NetworkConfigurationApplyStatus.Failed;
            Log(record, record.LastError);
            journal.SaveOperation(record);
            return Result(record, false, record.LastError);
        }
        finally
        {
            _operation.Release();
        }
    }

    public async Task<NetworkConfigurationExecutionResult> RollbackLastConfigurationAsync()
    {
        await _operation.WaitAsync().ConfigureAwait(false);
        try
        {
            var record = GetLastOperation();
            await discovery.StopAsync().ConfigureAwait(false);
            await proxy.StopAsync().ConfigureAwait(false);
            if (record is { RolledBackAt: null } && record.RollbackCommands.Count > 0)
                await MacPrivilege.RunAsync(MacNetworkPlan.RollbackCommand()).ConfigureAwait(false);

            record ??= new NetworkOperationRecord();
            Log(record, "已停止发现与代理，并恢复已应用的 RobotNet 网络配置。");
            journal.SaveRollback(record);
            return Result(record, true, record.ExecutionLog[^1]);
        }
        catch (Exception ex)
        {
            return Failure(ex.Message);
        }
        finally
        {
            _operation.Release();
        }
    }

    private void Log(NetworkOperationRecord record, string message)
    {
        record.ExecutionLog.Add(message);
        logs.Audit(message);
        ExecutionLogAdded?.Invoke(this, message);
    }

    private static NetworkConfigurationExecutionResult Failure(string message) => new()
    {
        Status = NetworkConfigurationApplyStatus.Failed,
        Message = message,
        Error = message
    };

    private static NetworkConfigurationExecutionResult Result(NetworkOperationRecord record, bool success, string message) => new()
    {
        Success = success,
        Record = record,
        Status = record.Status,
        Message = message,
        Error = success ? string.Empty : record.LastError,
        Logs = record.ExecutionLog.ToList()
    };
}

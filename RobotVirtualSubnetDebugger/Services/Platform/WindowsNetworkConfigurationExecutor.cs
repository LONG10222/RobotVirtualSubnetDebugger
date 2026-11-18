using System.Diagnostics;
using System.IO;
using System.Text;
using RobotNet.Windows.Wpf.Models;
using RobotNet.Windows.Wpf.Services.Logging;
using RobotNet.Windows.Wpf.Utils;

namespace RobotNet.Windows.Wpf.Services.Platform;

public sealed class WindowsNetworkConfigurationExecutor : INetworkConfigurationExecutor
{
    private readonly IVirtualSubnetService _virtualSubnetService;
    private readonly IAdminElevationService _adminElevationService;
    private readonly IOperationRollbackService _rollbackService;
    private readonly ITcpProxyServiceAdapter _tcpProxyServiceAdapter;
    private readonly ILogService _logService;

    public WindowsNetworkConfigurationExecutor(
        IVirtualSubnetService virtualSubnetService,
        IAdminElevationService adminElevationService,
        IOperationRollbackService rollbackService,
        ITcpProxyServiceAdapter tcpProxyServiceAdapter,
        ILogService logService)
    {
        _virtualSubnetService = virtualSubnetService;
        _adminElevationService = adminElevationService;
        _rollbackService = rollbackService;
        _tcpProxyServiceAdapter = tcpProxyServiceAdapter;
        _logService = logService;
    }

    public event EventHandler<string>? ExecutionLogAdded;

    public VirtualSubnetPlan PreviewGatewayConfiguration(AppConfig config)
    {
        var copy = CloneForRole(config, DeviceRole.GatewayAgent);
        return _virtualSubnetService.BuildPlan(copy);
    }

    public VirtualSubnetPlan PreviewClientConfiguration(AppConfig config)
    {
        var copy = CloneForRole(config, DeviceRole.DebugClient);
        return _virtualSubnetService.BuildPlan(copy);
    }

    public Task<NetworkConfigurationExecutionResult> ApplyGatewayConfigurationAsync(AppConfig config) =>
        WithOperationLockAsync(() => ApplyGatewayCoreAsync(config));

    public Task<NetworkConfigurationExecutionResult> ApplyClientConfigurationAsync(AppConfig config) =>
        WithOperationLockAsync(() => ApplyClientCoreAsync(config));

    public Task<NetworkConfigurationExecutionResult> RollbackLastConfigurationAsync() =>
        WithOperationLockAsync(RollbackLastCoreAsync);

    private static async Task<NetworkConfigurationExecutionResult> WithOperationLockAsync(
        Func<Task<NetworkConfigurationExecutionResult>> operation)
    {
        System.IO.FileStream operationLock;
        try { operationLock = NetworkOperationLock.Acquire(); }
        catch (Exception ex)
        {
            return new() { Success = false, Status = NetworkConfigurationApplyStatus.Failed,
                Message = "另一项网络操作正在运行，或无法获得操作锁。本次未执行，请勿同时运行多份程序或脚本。",
                Error = ex.Message };
        }
        using (operationLock) { return await operation().ConfigureAwait(false); }
    }

    private async Task<NetworkConfigurationExecutionResult> ApplyGatewayCoreAsync(AppConfig config)
    {
        var copy = CloneForRole(config, DeviceRole.GatewayAgent);
        var plan = _virtualSubnetService.BuildPlan(copy);
        var result = await ApplyPlanAsync(plan, copy, "网关端配置").ConfigureAwait(false);
        if (result.Success && copy.EnableTcpProxyMode)
        {
            await CompleteTcpProxyStartupAsync(
                result,
                copy,
                "网关端 TCP 代理服务",
                _tcpProxyServiceAdapter.StartGatewayAsync).ConfigureAwait(false);
        }

        return result;
    }

    private async Task<NetworkConfigurationExecutionResult> ApplyClientCoreAsync(AppConfig config)
    {
        var copy = CloneForRole(config, DeviceRole.DebugClient);
        var plan = _virtualSubnetService.BuildPlan(copy);
        var result = await ApplyPlanAsync(plan, copy, "调试端配置").ConfigureAwait(false);
        if (result.Success && copy.EnableTcpProxyMode)
        {
            await CompleteTcpProxyStartupAsync(
                result,
                copy,
                "调试端本地 TCP 代理监听",
                _tcpProxyServiceAdapter.StartClientAsync).ConfigureAwait(false);
        }

        return result;
    }

    private async Task<NetworkConfigurationExecutionResult> RollbackLastCoreAsync()
    {
        NetworkOperationRecord? record;
        try { record = _rollbackService.LoadLastOperation(); }
        catch (Exception ex) { return new() { Success = false, Status = NetworkConfigurationApplyStatus.Failed, Message = ex.Message, Error = ex.Message }; }
        if (record is null || record.RollbackCommands.Count == 0)
        {
            return new NetworkConfigurationExecutionResult
            {
                Success = false,
                Status = NetworkConfigurationApplyStatus.Failed,
                Message = "没有可回滚的网络配置记录。",
                Error = "没有可回滚的网络配置记录。"
            };
        }

        if (record.RollbackSchemaVersion != 1)
        {
            return new NetworkConfigurationExecutionResult
            {
                Success = false, Status = NetworkConfigurationApplyStatus.Failed,
                Message = "旧版本记录没有资源归属收据，已阻止自动回滚。请人工核对，避免删除原有网络配置。",
                Error = "Unsafe legacy rollback record."
            };
        }

        if (!_adminElevationService.CheckAdminPrivilege().Status.Equals(DiagnosticStatus.Success))
        {
            return new NetworkConfigurationExecutionResult
            {
                Success = false,
                Status = NetworkConfigurationApplyStatus.Failed,
                Message = "当前不是管理员模式，不能回滚网络配置。",
                Error = "当前不是管理员模式。"
            };
        }

        var result = new NetworkConfigurationExecutionResult
        {
            Status = NetworkConfigurationApplyStatus.Applying,
            Message = "正在回滚上次网络配置。",
            Record = record
        };

        try
        {
            AddLog(result, $"开始回滚：{record.OperationId}");
            await NetworkApplyTransaction.RollbackAsync(record.RollbackCommands.AsEnumerable().Reverse(),
                command => ExecutePowerShellAsync(command, result.Logs)).ConfigureAwait(false);

            record.ExecutionLog.AddRange(result.Logs);
            _rollbackService.SaveRollback(record);
            AddLog(result, "网络配置已回滚。");
            result.Success = true;
            result.Status = NetworkConfigurationApplyStatus.RolledBack;
            result.Message = "网络配置已回滚。";
            _logService.Audit($"网络配置已回滚：{record.OperationId}");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Status = NetworkConfigurationApplyStatus.Failed;
            result.Error = ex.Message;
            result.Message = $"回滚不完整，已尝试其余步骤，请检查凭据：{ex.Message}";
            record.Status = NetworkConfigurationApplyStatus.Failed;
            record.LastError = ex.Message;
            record.ExecutionLog.AddRange(result.Logs);
            _rollbackService.SaveOperation(record);
            _logService.Error("回滚网络配置失败。", ex);
        }

        return result;
    }

    public NetworkOperationRecord? GetLastOperation()
    {
        return _rollbackService.LoadLastOperation();
    }

    private async Task CompleteTcpProxyStartupAsync(
        NetworkConfigurationExecutionResult result,
        AppConfig config,
        string serviceName,
        Func<AppConfig, List<string>, Task> startAsync)
    {
        try
        {
            AddLog(result, $"正在启动{serviceName}。");
            await startAsync(config, result.Logs).ConfigureAwait(false);
            AddLog(result, $"{serviceName}已启动。");

            if (result.Record is not null)
            {
                result.Record.ExecutionLog.AddRange(result.Logs);
                _rollbackService.SaveOperation(result.Record);
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Status = NetworkConfigurationApplyStatus.Failed;
            result.Error = ex.Message;
            result.Message = $"{serviceName}启动失败：{ex.Message}";

            if (result.Record is not null)
            {
                result.Record.Status = NetworkConfigurationApplyStatus.Failed;
                result.Record.LastError = ex.Message;
                result.Record.ExecutionLog.AddRange(result.Logs);
                _rollbackService.SaveOperation(result.Record);
            }

            _logService.Error($"{serviceName}启动失败。", ex);
        }
    }

    private async Task<NetworkConfigurationExecutionResult> ApplyPlanAsync(
        VirtualSubnetPlan plan,
        AppConfig config,
        string operationName)
    {
        var result = new NetworkConfigurationExecutionResult
        {
            Status = NetworkConfigurationApplyStatus.Applying,
            Message = $"正在应用{operationName}。"
        };

        if (_adminElevationService.IsReadOnlyMode ||
            _adminElevationService.CheckAdminPrivilege().Status != DiagnosticStatus.Success)
        {
            result.Success = false;
            result.Status = NetworkConfigurationApplyStatus.Failed;
            result.Message = "当前不是管理员模式，不能应用网络配置。";
            result.Error = result.Message;
            return result;
        }

        if (plan.Status == DiagnosticStatus.Error || !plan.CanGenerateScripts)
        {
            result.Success = false;
            result.Status = NetworkConfigurationApplyStatus.Failed;
            result.Message = "当前计划不可应用，请先修正配置。";
            result.Error = string.Join("；", plan.Warnings);
            return result;
        }

        var commands = WindowsVirtualSubnetService.GetApplySteps(plan);

        var validationError = ValidateCommands(commands);
        if (!string.IsNullOrWhiteSpace(validationError))
        {
            result.Success = false;
            result.Status = NetworkConfigurationApplyStatus.Failed;
            result.Message = validationError;
            result.Error = validationError;
            return result;
        }

        NetworkOperationRecord? previous;
        try { previous = _rollbackService.LoadLastOperation(); }
        catch (Exception ex)
        {
            result.Success = false; result.Status = NetworkConfigurationApplyStatus.Failed;
            result.Error = ex.Message; result.Message = ex.Message;
            return result;
        }
        if (previous is not null &&
            previous.Status != NetworkConfigurationApplyStatus.RolledBack)
        {
            result.Status = NetworkConfigurationApplyStatus.Failed;
            result.Message = "上次网络配置尚未回滚。请先回滚或人工核对，不能覆盖其恢复记录。";
            result.Error = result.Message;
            return result;
        }

        var record = new NetworkOperationRecord
        {
            RollbackSchemaVersion = 1,
            Role = config.Role,
            TargetSubnetCidr = plan.TargetSubnetCidr,
            GatewayLanIp = config.GatewayLanIp,
            Summary = plan.Summary,
            Status = NetworkConfigurationApplyStatus.Applying,
            AppliedCommands = commands.Select(step => step.Command).ToList(),
            RollbackCommands = commands
                .Where(step => !string.IsNullOrWhiteSpace(step.RollbackCommand) && step.RollbackCommand != "无")
                .Select(step => step.RollbackCommand)
                .ToList()
        };
        result.Record = record;

        try
        {
            // Persist the receipt-guarded undo plan before any mutation, including partial failures.
            _rollbackService.SaveOperation(record);
            AddLog(result, $"开始应用{operationName}：{plan.Summary}");
            await NetworkApplyTransaction.ExecuteAsync(commands,
                command => ExecutePowerShellAsync(command, result.Logs),
                complete => {
                    record.Status = complete ? NetworkConfigurationApplyStatus.RolledBack : NetworkConfigurationApplyStatus.Failed;
                    AddLog(result, complete ? "失败后已完成本次操作的凭据保护回滚。" : "自动回滚不完整，请检查操作凭据，勿继续应用。");
                }).ConfigureAwait(false);

            record.Status = NetworkConfigurationApplyStatus.Applied;
            record.AppliedAt = DateTimeOffset.Now;
            record.ExecutionLog.AddRange(result.Logs);
            _rollbackService.SaveOperation(record);

            result.Success = true;
            result.Status = NetworkConfigurationApplyStatus.Applied;
            result.Message = $"{operationName}已应用。";
            _logService.Audit($"{operationName}已应用，操作 ID：{record.OperationId}");
        }
        catch (Exception ex)
        {
            if (record.Status != NetworkConfigurationApplyStatus.RolledBack)
                record.Status = NetworkConfigurationApplyStatus.Failed;
            record.LastError = ex.Message;
            record.ExecutionLog.AddRange(result.Logs);
            _rollbackService.SaveOperation(record);
            result.Success = false;
            result.Status = NetworkConfigurationApplyStatus.Failed;
            result.Error = ex.Message;
            result.Message = $"{operationName}应用失败：{ex.Message}";
            _logService.Error($"{operationName}应用失败。", ex);
        }

        return result;
    }

    private async Task ExecutePowerShellAsync(string command, List<string> logs)
    {
        var scriptDirectory = Path.Combine(AppPaths.AppDataDirectory, "operations", "scripts");
        Directory.CreateDirectory(scriptDirectory);
        var scriptPath = Path.Combine(scriptDirectory, $"operation-{DateTime.Now:yyyyMMdd-HHmmss-fff}.ps1");
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            {{command}}
            """;
        await File.WriteAllTextAsync(scriptPath, script, Encoding.UTF8).ConfigureAwait(false);

        var processStartInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = Process.Start(processStartInfo) ?? throw new InvalidOperationException("无法启动 PowerShell。");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().ConfigureAwait(false);
        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(output))
        {
            AddLog(logs, output.Trim());
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            AddLog(logs, error.Trim());
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? $"PowerShell 命令执行失败，退出码 {process.ExitCode}。"
                : error.Trim());
        }
    }

    private static string ValidateCommands(IEnumerable<VirtualSubnetOperationStep> steps)
    {
        foreach (var step in steps)
        {
            var command = step.Command;
            if (command.Contains("<请填写", StringComparison.Ordinal))
            {
                return "计划中仍有需要手动填写的网卡名称，请先在配置页或网卡页确认网卡。";
            }

            if (command.Contains("0.0.0.0/0", StringComparison.Ordinal) &&
                (command.Contains("New-NetRoute", StringComparison.OrdinalIgnoreCase) ||
                 command.Contains("Remove-NetRoute", StringComparison.OrdinalIgnoreCase)))
            {
                return "拒绝修改默认路由 0.0.0.0/0。";
            }

            if (command.Contains("Dns", StringComparison.OrdinalIgnoreCase) ||
                command.Contains("Disable-NetAdapter", StringComparison.OrdinalIgnoreCase))
            {
                return "拒绝修改 DNS 或关闭网卡。";
            }
        }

        return string.Empty;
    }

    private void AddLog(NetworkConfigurationExecutionResult result, string message)
    {
        AddLog(result.Logs, message);
    }

    private void AddLog(List<string> logs, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}";
        logs.Add(line);
        ExecutionLogAdded?.Invoke(this, line);
        _logService.Audit(line);
    }

    private static AppConfig CloneForRole(AppConfig config, DeviceRole role)
    {
        return new AppConfig
        {
            DeviceId = config.DeviceId,
            DeviceName = config.DeviceName,
            Role = role,
            VirtualIp = config.VirtualIp,
            VirtualSubnetMask = config.VirtualSubnetMask,
            TargetDeviceIp = config.TargetDeviceIp,
            TargetDevicePort = config.TargetDevicePort,
            DiscoveryPort = config.DiscoveryPort,
            LocalListenPort = config.LocalListenPort,
            ProxyControlPort = config.ProxyControlPort,
            GatewayLanIp = config.GatewayLanIp,
            TargetDeviceAdapterIp = config.TargetDeviceAdapterIp,
            SharedKey = config.SharedKey,
            AutoPairingToken = config.AutoPairingToken,
            PairingSecurityVersion = config.PairingSecurityVersion,
            ProxyHeartbeatIntervalSeconds = config.ProxyHeartbeatIntervalSeconds,
            ProxyIdleTimeoutSeconds = config.ProxyIdleTimeoutSeconds,
            ProxyReconnectAttempts = config.ProxyReconnectAttempts,
            EnableNat = config.EnableNat,
            EnablePreciseRoute = config.EnablePreciseRoute,
            EnableTcpProxyMode = config.EnableTcpProxyMode,
            EnableVirtualSubnetMode = config.EnableVirtualSubnetMode,
            GitHubRepositoryOwner = config.GitHubRepositoryOwner,
            GitHubRepositoryName = config.GitHubRepositoryName,
            EnableUpdateCheckOnStartup = config.EnableUpdateCheckOnStartup
        };
    }
}

public interface ITcpProxyServiceAdapter
{
    Task StartGatewayAsync(AppConfig config, List<string> logs);

    Task StartClientAsync(AppConfig config, List<string> logs);
}

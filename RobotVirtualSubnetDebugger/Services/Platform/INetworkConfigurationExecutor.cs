using RobotNet.Windows.Wpf.Models;

namespace RobotNet.Windows.Wpf.Services.Platform;

public interface INetworkConfigurationExecutor
{
    event EventHandler<string>? ExecutionLogAdded;

    VirtualSubnetPlan PreviewGatewayConfiguration(AppConfig config);

    VirtualSubnetPlan PreviewClientConfiguration(AppConfig config);

    Task<NetworkConfigurationExecutionResult> ApplyGatewayConfigurationAsync(AppConfig config);

    Task<NetworkConfigurationExecutionResult> ApplyClientConfigurationAsync(AppConfig config);

    Task<NetworkConfigurationExecutionResult> RollbackLastConfigurationAsync();

    NetworkOperationRecord? GetLastOperation();
}

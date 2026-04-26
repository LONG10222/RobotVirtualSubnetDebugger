using RobotNet.Windows.Wpf.Models;

namespace RobotNet.Windows.Wpf.Services.Diagnostics;

public interface IDiagnosticService
{
    Task<IReadOnlyList<DiagnosticResult>> RunAllAsync(AppConfig config);

    Task<bool> PingAsync(string ip);

    Task<bool> TestTcpPortAsync(string ip, int port);
}

namespace RobotNet.Windows.Wpf.Services.CrashReporting;

public interface ICrashReportService
{
    string CrashReportsDirectory { get; }

    string? LastCrashReportPath { get; }

    void RegisterGlobalHandlers();

    string WriteCrashReport(string source, Exception exception);
}

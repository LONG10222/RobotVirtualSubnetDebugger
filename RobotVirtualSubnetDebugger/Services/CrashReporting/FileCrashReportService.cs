using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using RobotNet.Windows.Wpf.Services.Logging;
using RobotNet.Windows.Wpf.Utils;

namespace RobotNet.Windows.Wpf.Services.CrashReporting;

public sealed class FileCrashReportService : ICrashReportService
{
    private readonly ILogService _logService;
    private bool _registered;

    public FileCrashReportService(ILogService logService)
    {
        _logService = logService;
    }

    public string CrashReportsDirectory => AppPaths.CrashReportsDirectory;

    public string? LastCrashReportPath { get; private set; }

    public void RegisterGlobalHandlers()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        if (Application.Current is not null)
        {
            Application.Current.DispatcherUnhandledException += OnDispatcherUnhandledException;
        }

        _logService.Info($"崩溃报告已启用：{CrashReportsDirectory}");
    }

    public string WriteCrashReport(string source, Exception exception)
    {
        var timestamp = DateTime.Now;
        var fileName = $"crash-{timestamp:yyyyMMdd-HHmmss-fff}.log";
        var path = Path.Combine(CrashReportsDirectory, fileName);
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        var report = new StringBuilder()
            .AppendLine("RobotNet.Windows.Wpf crash report")
            .AppendLine($"Time: {timestamp:O}")
            .AppendLine($"Source: {source}")
            .AppendLine($"Version: {version}")
            .AppendLine($"OS: {Environment.OSVersion}")
            .AppendLine($".NET: {Environment.Version}")
            .AppendLine($"Machine: {Environment.MachineName}")
            .AppendLine($"User: {Environment.UserName}")
            .AppendLine()
            .AppendLine(exception.ToString())
            .ToString();

        File.WriteAllText(path, report, Encoding.UTF8);
        LastCrashReportPath = path;
        _logService.Error($"崩溃报告已写入：{path}", exception);
        return path;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            WriteCrashReport("AppDomain.UnhandledException", exception);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteCrashReport("TaskScheduler.UnobservedTaskException", e.Exception);
        e.SetObserved();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var path = WriteCrashReport("Application.DispatcherUnhandledException", e.Exception);
        e.Handled = true;
        MessageBox.Show(
            $"程序发生未处理异常，崩溃报告已保存：\n{path}\n\n程序将关闭，请把该文件附到 GitHub Issue 或反馈中。",
            "RobotNet.Windows.Wpf",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        Application.Current.Shutdown(1);
    }
}

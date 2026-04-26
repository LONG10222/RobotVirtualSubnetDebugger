namespace RobotNet.Windows.Wpf.Services.Logging;

public interface ILogService
{
    event EventHandler<string>? EntryAdded;

    string LogFilePath { get; }

    void Info(string message);

    void Warning(string message);

    void Error(string message, Exception? exception = null);

    void Audit(string message);

    IReadOnlyList<string> GetLogs();
}

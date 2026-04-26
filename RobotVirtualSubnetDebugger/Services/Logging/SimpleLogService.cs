using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using RobotNet.Windows.Wpf.Utils;

namespace RobotNet.Windows.Wpf.Services.Logging;

public sealed class SimpleLogService : ILogService
{
    private readonly ObservableCollection<string> _entries = [];
    private readonly object _fileLock = new();

    public event EventHandler<string>? EntryAdded;

    public string LogFilePath { get; } = Path.Combine(AppPaths.LogsDirectory, $"robotnet-{DateTime.Now:yyyyMMdd}.log");

    public IReadOnlyList<string> Entries => _entries;

    public void Info(string message)
    {
        Add("INFO", message);
    }

    public void Warning(string message)
    {
        Add("WARN", message);
    }

    public void Error(string message, Exception? exception = null)
    {
        var text = exception is null ? message : $"{message} {exception.Message}";
        Add("ERROR", text);
    }

    public void Audit(string message)
    {
        Add("AUDIT", message);
    }

    public IReadOnlyList<string> GetLogs()
    {
        return _entries.ToList();
    }

    private void Add(string level, string message)
    {
        var entry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";
        AppendToFile(entry);

        void AddOnUiThread()
        {
            _entries.Add(entry);
            EntryAdded?.Invoke(this, entry);
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            AddOnUiThread();
        }
        else
        {
            dispatcher.Invoke(AddOnUiThread);
        }
    }

    private void AppendToFile(string entry)
    {
        try
        {
            lock (_fileLock)
            {
                File.AppendAllText(LogFilePath, entry + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must not break the application.
        }
    }
}

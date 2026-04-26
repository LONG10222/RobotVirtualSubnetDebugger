using System.Collections.ObjectModel;
using RobotNet.Windows.Wpf.Commands;
using RobotNet.Windows.Wpf.Services.Logging;
using RobotNet.Windows.Wpf.Utils;

namespace RobotNet.Windows.Wpf.ViewModels;

public sealed class LogsViewModel : ObservableObject
{
    private readonly ILogService _logService;

    public LogsViewModel(ILogService logService)
    {
        _logService = logService;
        _logService.EntryAdded += OnEntryAdded;
        RefreshCommand = new RelayCommand(Refresh);
        Refresh();
    }

    public ObservableCollection<string> Entries { get; } = [];

    public RelayCommand RefreshCommand { get; }

    public string LogFilePath => _logService.LogFilePath;

    private void Refresh()
    {
        Entries.Clear();
        foreach (var entry in _logService.GetLogs())
        {
            Entries.Add(entry);
        }
    }

    private void OnEntryAdded(object? sender, string entry)
    {
        Entries.Add(entry);
    }
}

using System.Collections.ObjectModel;
using RobotNet.Windows.Wpf.Commands;
using RobotNet.Windows.Wpf.Models;
using RobotNet.Windows.Wpf.Services.Configuration;
using RobotNet.Windows.Wpf.Services.Diagnostics;
using RobotNet.Windows.Wpf.Utils;

namespace RobotNet.Windows.Wpf.ViewModels;

public sealed class DiagnosticsViewModel : ObservableObject
{
    private readonly IConfigurationService _configurationService;
    private readonly IDiagnosticService _diagnosticService;
    private string _status = "未运行";

    public DiagnosticsViewModel(
        IConfigurationService configurationService,
        IDiagnosticService diagnosticService)
    {
        _configurationService = configurationService;
        _diagnosticService = diagnosticService;
        RunCommand = new AsyncRelayCommand(RunAsync);
    }

    public ObservableCollection<DiagnosticResult> Results { get; } = [];

    public AsyncRelayCommand RunCommand { get; }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    private async Task RunAsync()
    {
        Status = "正在运行诊断...";
        Results.Clear();

        var config = _configurationService.Load();
        var results = await _diagnosticService.RunAllAsync(config);
        foreach (var result in results)
        {
            Results.Add(result);
        }

        Status = $"{DateTime.Now:HH:mm:ss} 诊断完成，共 {Results.Count} 项";
    }
}

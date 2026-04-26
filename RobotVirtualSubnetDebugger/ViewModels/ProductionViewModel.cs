using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using RobotNet.Windows.Wpf.Commands;
using RobotNet.Windows.Wpf.Models;
using RobotNet.Windows.Wpf.Services.Configuration;
using RobotNet.Windows.Wpf.Services.CrashReporting;
using RobotNet.Windows.Wpf.Services.Logging;
using RobotNet.Windows.Wpf.Services.Updates;
using RobotNet.Windows.Wpf.Utils;

namespace RobotNet.Windows.Wpf.ViewModels;

public sealed class ProductionViewModel : ObservableObject
{
    private readonly IConfigurationService _configurationService;
    private readonly IUpdateService _updateService;
    private readonly ICrashReportService _crashReportService;
    private readonly ILogService _logService;
    private UpdateCheckResult? _lastUpdateResult;
    private string _updateStatus = "尚未检查更新。";
    private string _downloadStatus = string.Empty;
    private string _downloadPath = string.Empty;

    public ProductionViewModel(
        IConfigurationService configurationService,
        IUpdateService updateService,
        ICrashReportService crashReportService,
        ILogService logService)
    {
        _configurationService = configurationService;
        _updateService = updateService;
        _crashReportService = crashReportService;
        _logService = logService;

        CheckUpdatesCommand = new AsyncRelayCommand(CheckUpdatesAsync);
        DownloadUpdateCommand = new AsyncRelayCommand(DownloadUpdateAsync, () => _lastUpdateResult?.Assets.Count > 0);
        OpenReleasePageCommand = new RelayCommand(OpenReleasePage);
        OpenLogsFolderCommand = new RelayCommand(() => OpenFolder(AppPaths.LogsDirectory));
        OpenCrashFolderCommand = new RelayCommand(() => OpenFolder(_crashReportService.CrashReportsDirectory));
        OpenUpdatesFolderCommand = new RelayCommand(() => OpenFolder(_updateService.UpdatesDirectory));

        BuildChecklist();
    }

    public ObservableCollection<ReleaseAssetInfo> Assets { get; } = [];

    public ObservableCollection<ReleaseChecklistItem> Checklist { get; } = [];

    public AsyncRelayCommand CheckUpdatesCommand { get; }

    public AsyncRelayCommand DownloadUpdateCommand { get; }

    public RelayCommand OpenReleasePageCommand { get; }

    public RelayCommand OpenLogsFolderCommand { get; }

    public RelayCommand OpenCrashFolderCommand { get; }

    public RelayCommand OpenUpdatesFolderCommand { get; }

    public string CurrentVersion => _updateService.CurrentVersion;

    public string RepositoryText
    {
        get
        {
            var config = _configurationService.Load();
            return $"{config.GitHubRepositoryOwner}/{config.GitHubRepositoryName}";
        }
    }

    public string ReleasePageUrl
    {
        get
        {
            var config = _configurationService.Load();
            return $"https://github.com/{config.GitHubRepositoryOwner}/{config.GitHubRepositoryName}/releases";
        }
    }

    public string LogFilePath => _logService.LogFilePath;

    public string CrashReportsDirectory => _crashReportService.CrashReportsDirectory;

    public string UpdatesDirectory => _updateService.UpdatesDirectory;

    public string UpdateStatus
    {
        get => _updateStatus;
        private set => SetProperty(ref _updateStatus, value);
    }

    public string DownloadStatus
    {
        get => _downloadStatus;
        private set => SetProperty(ref _downloadStatus, value);
    }

    public string DownloadPath
    {
        get => _downloadPath;
        private set => SetProperty(ref _downloadPath, value);
    }

    private async Task CheckUpdatesAsync()
    {
        UpdateStatus = "正在通过 GitHub Releases 检查更新...";
        DownloadStatus = string.Empty;
        DownloadPath = string.Empty;

        _lastUpdateResult = await _updateService.CheckForUpdatesAsync(_configurationService.Load());
        Assets.Clear();
        foreach (var asset in _lastUpdateResult.Assets)
        {
            Assets.Add(asset);
        }

        UpdateStatus = _lastUpdateResult.Message;
        DownloadUpdateCommand.RaiseCanExecuteChanged();
    }

    private async Task DownloadUpdateAsync()
    {
        if (_lastUpdateResult is null)
        {
            DownloadStatus = "请先检查更新。";
            return;
        }

        DownloadStatus = "正在下载 GitHub Release 发布包...";
        var result = await _updateService.DownloadUpdateAsync(_lastUpdateResult);
        DownloadStatus = result.Message;
        DownloadPath = result.FilePath;
    }

    private void BuildChecklist()
    {
        Checklist.Clear();
        Checklist.Add(new ReleaseChecklistItem
        {
            Name = "GitHub Releases 更新",
            Status = "完成",
            Detail = $"默认仓库：{RepositoryText}。应用会查询 latest release，并下载 win-x64 发布包。"
        });
        Checklist.Add(new ReleaseChecklistItem
        {
            Name = "崩溃报告",
            Status = "完成",
            Detail = $"未处理异常会写入：{CrashReportsDirectory}"
        });
        Checklist.Add(new ReleaseChecklistItem
        {
            Name = "持久化日志",
            Status = "完成",
            Detail = $"运行日志会写入：{LogFilePath}"
        });
        Checklist.Add(new ReleaseChecklistItem
        {
            Name = "发布脚本",
            Status = "完成",
            Detail = "scripts/publish-release.ps1 可生成框架依赖版、自包含版、压缩包和 SHA256 校验文件。"
        });
        Checklist.Add(new ReleaseChecklistItem
        {
            Name = "代码签名接入点",
            Status = "完成",
            Detail = "发布脚本支持 SignTool；证书不应提交到仓库，需要发布者本机或 GitHub Secrets 配置。"
        });
    }

    private void OpenReleasePage()
    {
        OpenUrl(ReleasePageUrl);
    }

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }
}

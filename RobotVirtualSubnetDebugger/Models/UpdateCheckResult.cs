namespace RobotNet.Windows.Wpf.Models;

public sealed class UpdateCheckResult
{
    public bool Success { get; set; }

    public bool IsUpdateAvailable { get; set; }

    public string CurrentVersion { get; set; } = string.Empty;

    public string LatestVersion { get; set; } = string.Empty;

    public string ReleaseName { get; set; } = string.Empty;

    public string ReleaseUrl { get; set; } = string.Empty;

    public DateTimeOffset? PublishedAt { get; set; }

    public string Message { get; set; } = string.Empty;

    public List<ReleaseAssetInfo> Assets { get; set; } = [];
}

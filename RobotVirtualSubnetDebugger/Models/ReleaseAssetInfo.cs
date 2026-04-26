namespace RobotNet.Windows.Wpf.Models;

public sealed class ReleaseAssetInfo
{
    public string Name { get; set; } = string.Empty;

    public string DownloadUrl { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string SizeText => SizeBytes <= 0
        ? "-"
        : $"{SizeBytes / 1024d / 1024d:0.0} MB";
}

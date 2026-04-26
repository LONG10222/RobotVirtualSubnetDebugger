namespace RobotNet.Windows.Wpf.Models;

public sealed class UpdateDownloadResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;
}

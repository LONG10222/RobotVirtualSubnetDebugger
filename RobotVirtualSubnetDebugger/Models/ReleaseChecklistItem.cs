namespace RobotNet.Windows.Wpf.Models;

public sealed class ReleaseChecklistItem
{
    public string Name { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;

    public string StatusColor => Status switch
    {
        "完成" => "#1F7A3D",
        "需配置" => "#B7791F",
        "风险" => "#B42318",
        _ => "#2563A6"
    };
}

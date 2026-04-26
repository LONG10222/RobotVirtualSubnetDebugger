namespace RobotNet.Windows.Wpf.ViewModels;

public sealed class NavigationItemViewModel
{
    public NavigationItemViewModel(string key, string title, object? viewModel, bool isEnabled)
    {
        Key = key;
        Title = title;
        ViewModel = viewModel;
        IsEnabled = isEnabled;
    }

    public string Key { get; }

    public string Title { get; }

    public object? ViewModel { get; }

    public bool IsEnabled { get; }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using RobotNet.Windows.Wpf.Services.Ui;

namespace RobotNet.Mac.Platform;

internal sealed class MacConfirmation(Window owner) : IUserConfirmationService
{
    public Task<bool> ConfirmAsync(string title, string message)
    {
        var cancel = new Button { Content = "取消", IsCancel = true };
        var confirm = new Button { Content = "确认" };
        var dialog = new Window
        {
            Title = title,
            Width = 500,
            SizeToContent = SizeToContent.Height,
            MaxHeight = 600,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(22),
                Spacing = 18,
                Children =
                {
                    new ScrollViewer
                    {
                        MaxHeight = 440,
                        Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 10,
                        Children = { cancel, confirm }
                    }
                }
            }
        };
        cancel.Click += (_, _) => dialog.Close(false);
        confirm.Click += (_, _) => dialog.Close(true);
        return dialog.ShowDialog<bool>(owner);
    }
}

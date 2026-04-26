using System.Windows;

namespace RobotNet.Windows.Wpf.Services.Ui;

public sealed class WpfUserConfirmationService : IUserConfirmationService
{
    public bool Confirm(string title, string message)
    {
        var result = MessageBox.Show(
            Application.Current?.MainWindow,
            message,
            title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        return result == MessageBoxResult.Yes;
    }
}

namespace RobotNet.Windows.Wpf.Services.Ui;

public interface IUserConfirmationService
{
    Task<bool> ConfirmAsync(string title, string message);
}

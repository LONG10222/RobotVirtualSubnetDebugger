namespace RobotNet.Windows.Wpf.Services.Ui;

public interface IUserConfirmationService
{
    bool Confirm(string title, string message);
}

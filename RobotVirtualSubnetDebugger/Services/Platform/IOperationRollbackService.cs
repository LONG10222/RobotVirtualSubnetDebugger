using RobotNet.Windows.Wpf.Models;

namespace RobotNet.Windows.Wpf.Services.Platform;

public interface IOperationRollbackService
{
    NetworkOperationRecord? LoadLastOperation();

    void SaveOperation(NetworkOperationRecord record);

    void SaveRollback(NetworkOperationRecord record);
}

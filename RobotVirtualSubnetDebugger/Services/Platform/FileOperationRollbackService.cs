using System.IO;
using System.Text.Json;
using RobotNet.Windows.Wpf.Models;
using RobotNet.Windows.Wpf.Services.Logging;
using RobotNet.Windows.Wpf.Utils;

namespace RobotNet.Windows.Wpf.Services.Platform;

public sealed class FileOperationRollbackService : IOperationRollbackService
{
    private readonly ILogService _logService;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    public FileOperationRollbackService(ILogService logService)
    {
        _logService = logService;
    }

    private string OperationsDirectory => Path.Combine(AppPaths.AppDataDirectory, "operations");

    private string LastOperationPath => Path.Combine(OperationsDirectory, "last-network-operation.json");

    public NetworkOperationRecord? LoadLastOperation()
    {
        try
        {
            if (!File.Exists(LastOperationPath))
            {
                return null;
            }

            var json = File.ReadAllText(LastOperationPath);
            return JsonSerializer.Deserialize<NetworkOperationRecord>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logService.Error("读取上次网络配置记录失败。", ex);
            return null;
        }
    }

    public void SaveOperation(NetworkOperationRecord record)
    {
        Directory.CreateDirectory(OperationsDirectory);
        var historyPath = Path.Combine(OperationsDirectory, $"{record.OperationId}.json");
        var json = JsonSerializer.Serialize(record, _jsonOptions);
        File.WriteAllText(historyPath, json);
        File.WriteAllText(LastOperationPath, json);
        _logService.Audit($"网络配置操作记录已保存：{historyPath}");
    }

    public void SaveRollback(NetworkOperationRecord record)
    {
        record.Status = NetworkConfigurationApplyStatus.RolledBack;
        record.RolledBackAt = DateTimeOffset.Now;
        SaveOperation(record);
    }
}

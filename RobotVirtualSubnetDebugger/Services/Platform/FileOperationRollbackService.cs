using System.IO;
using System.Text.Json;
using RobotNet.Windows.Wpf.Models;
using RobotNet.Windows.Wpf.Services.Logging;
using RobotNet.Windows.Wpf.Utils;

namespace RobotNet.Windows.Wpf.Services.Platform;

public sealed class FileOperationRollbackService : IOperationRollbackService
{
    private readonly ILogService _logService;
    private readonly string? _operationsDirectory;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    public FileOperationRollbackService(ILogService logService)
    {
        _logService = logService;
    }

    internal FileOperationRollbackService(ILogService logService, string operationsDirectory) : this(logService)
    {
        _operationsDirectory = operationsDirectory;
    }

    private string OperationsDirectory => _operationsDirectory ?? Path.Combine(AppPaths.AppDataDirectory, "operations");

    private string LastOperationPath => Path.Combine(OperationsDirectory, "last-network-operation.json");

    public NetworkOperationRecord? LoadLastOperation()
    {
        try
        {
            var json = File.ReadAllText(LastOperationPath);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("OperationId", out _) ||
                !root.TryGetProperty("Status", out _) || !root.TryGetProperty("RollbackCommands", out _))
                throw new InvalidDataException("Incomplete network operation record.");
            var record = JsonSerializer.Deserialize<NetworkOperationRecord>(json, _jsonOptions);
            if (record is null || !Guid.TryParseExact(record.OperationId, "N", out _) ||
                !Enum.IsDefined(record.Status) || record.RollbackCommands is null ||
                record.AppliedCommands is null || record.ExecutionLog is null ||
                record.RollbackCommands.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException("Invalid network operation record.");
            return record;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (Exception ex)
        {
            _logService.Error("读取上次网络配置记录失败。", ex);
            throw new InvalidOperationException("无法可靠读取网络操作记录，已阻止进一步应用。请保留文件并人工检查。", ex);
        }
    }

    public void SaveOperation(NetworkOperationRecord record)
    {
        if (!Guid.TryParseExact(record.OperationId, "N", out _)) throw new InvalidDataException("Invalid operation ID.");
        Directory.CreateDirectory(OperationsDirectory);
        var historyPath = Path.Combine(OperationsDirectory, $"{record.OperationId}.json");
        var json = JsonSerializer.Serialize(record, _jsonOptions);
        AtomicWrite(historyPath, json);
        AtomicWrite(LastOperationPath, json);
        _logService.Audit($"网络配置操作记录已保存：{historyPath}");
    }

    private static void AtomicWrite(string path, string json)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var data = System.Text.Encoding.UTF8.GetBytes(json);
                stream.Write(data);
                stream.Flush(flushToDisk: true);
            }
            // Same-directory rename is atomic; no delete-then-write interval.
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public void SaveRollback(NetworkOperationRecord record)
    {
        record.Status = NetworkConfigurationApplyStatus.RolledBack;
        record.RolledBackAt = DateTimeOffset.Now;
        SaveOperation(record);
    }
}

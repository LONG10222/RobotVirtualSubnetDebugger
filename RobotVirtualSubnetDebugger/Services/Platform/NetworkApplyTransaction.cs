using RobotNet.Windows.Wpf.Models;

namespace RobotNet.Windows.Wpf.Services.Platform;

internal static class NetworkApplyTransaction
{
    internal static async Task RollbackAsync(IEnumerable<string> commands, Func<string, Task> execute)
    {
        var failures = new List<Exception>();
        foreach (var command in commands)
        {
            try { await execute(command).ConfigureAwait(false); }
            catch (Exception ex) { failures.Add(ex); }
        }
        if (failures.Count > 0) throw new AggregateException("Rollback incomplete; all remaining steps were attempted.", failures);
    }

    // Each undo is receipt-guarded. Include a failing step because it may have mutated
    // before failing, but never undo unattempted steps or delete uncertain resources.
    internal static async Task ExecuteAsync(IEnumerable<VirtualSubnetOperationStep> steps,
        Func<string, Task> execute, Action<bool> rollbackOutcome)
    {
        var attempted = new List<VirtualSubnetOperationStep>();
        try
        {
            foreach (var step in steps)
            {
                attempted.Add(step);
                await execute(step.Command).ConfigureAwait(false);
            }
        }
        catch (Exception original)
        {
            var errors = new List<Exception>();
            foreach (var step in attempted.AsEnumerable().Reverse())
            {
                if (string.IsNullOrWhiteSpace(step.RollbackCommand) || step.RollbackCommand == "无") continue;
                try { await execute(step.RollbackCommand).ConfigureAwait(false); }
                catch (Exception error) { errors.Add(error); }
            }
            rollbackOutcome(errors.Count == 0);
            if (errors.Count > 0)
                throw new AggregateException("Application failed; automatic rollback was incomplete. Inspect receipts before further changes.", new[] { original }.Concat(errors));
            throw;
        }
    }
}

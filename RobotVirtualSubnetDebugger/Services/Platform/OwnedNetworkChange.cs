using RobotNet.Windows.Wpf.Models;

namespace RobotNet.Windows.Wpf.Services.Platform;

/// <summary>Persist per-step ownership before allowing rollback. Existing or uncertain resources
/// are never deleted. Shared by exported scripts and in-app execution.</summary>
internal static class OwnedNetworkChange
{
    public static void Configure(VirtualSubnetOperationStep step, string query, string compatible,
        string create, string remove, string signatureProperties)
    {
        var id = Guid.NewGuid().ToString("N");
        var prelude = $$"""
            $receiptDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'RobotNet/operations/receipts'
            $receiptPath = Join-Path $receiptDirectory '{{id}}.json'
            function Get-RobotNetSignature($items) {
                return (ConvertTo-Json -InputObject @($items | Select-Object {{signatureProperties}}) -Compress -Depth 4)
            }
            """;
        step.Command = prelude + Environment.NewLine + $$"""
            if (Test-Path -LiteralPath $receiptPath) { throw 'This step has already been attempted; roll it back or inspect its receipt before retrying.' }
            $current = @({{query}})
            if ($current.Count -gt 0) {
                if (-not ({{compatible}})) { throw 'Existing network configuration conflicts with this plan; it has not been changed.' }
                New-Item -ItemType Directory -Path $receiptDirectory -Force | Out-Null
                @{ State = 'Unchanged' } | ConvertTo-Json | Set-Content -LiteralPath $receiptPath -Encoding UTF8 -ErrorAction Stop
            } else {
                New-Item -ItemType Directory -Path $receiptDirectory -Force | Out-Null
                @{ State = 'Pending' } | ConvertTo-Json | Set-Content -LiteralPath $receiptPath -Encoding UTF8 -ErrorAction Stop
                {{create}}
                $created = @({{query}})
                if ($created.Count -ne 1) { throw 'Cannot verify the created network resource; inspect the pending receipt manually.' }
                @{ State = 'Created'; Signature = (Get-RobotNetSignature $created) } | ConvertTo-Json | Set-Content -LiteralPath $receiptPath -Encoding UTF8 -ErrorAction Stop
            }
            """;
        step.RollbackCommand = prelude + Environment.NewLine + $$"""
            if (Test-Path -LiteralPath $receiptPath) {
                $receipt = Get-Content -LiteralPath $receiptPath -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
                if ($receipt.State -eq 'Created') {
                    $current = @({{query}})
                    if ($current.Count -gt 0) {
                        if ((Get-RobotNetSignature $current) -ne $receipt.Signature) { throw 'Network resource changed after application; refusing destructive rollback.' }
                        {{remove}}
                    }
                    @{ State = 'RolledBack' } | ConvertTo-Json | Set-Content -LiteralPath $receiptPath -Encoding UTF8 -ErrorAction Stop
                } elseif ($receipt.State -notin @('Unchanged', 'RolledBack')) {
                    throw 'Network change outcome is uncertain; inspect the pending receipt manually before rollback.'
                }
            }
            """;
    }

    public static VirtualSubnetOperationStep Forwarding(string alias, int order, string name, string description)
    {
        var escaped = Quote(alias);
        var step = new VirtualSubnetOperationStep
        {
            Order = order, Name = name, Description = description,
            RequiresAdministrator = true, WillModifySystem = true, RiskLevel = "Medium"
        };
        var id = Guid.NewGuid().ToString("N");
        var prelude = $$"""
            $receiptDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'RobotNet/operations/receipts'
            $receiptPath = Join-Path $receiptDirectory '{{id}}.json'
            """;
        var query = $$"""
            $interfaces = @(Get-NetIPInterface -AddressFamily IPv4 -ErrorAction Stop | Where-Object { $_.InterfaceAlias -eq '{{escaped}}' })
            if ($interfaces.Count -ne 1) { throw 'Expected one IPv4 interface; refusing ambiguous forwarding change.' }
            """;
        step.Command = prelude + Environment.NewLine + query + Environment.NewLine + """
            if (Test-Path -LiteralPath $receiptPath) { throw 'This forwarding step has already been attempted.' }
            New-Item -ItemType Directory -Path $receiptDirectory -Force | Out-Null
            if ($interfaces[0].Forwarding -eq 'Enabled') {
                @{ State = 'Unchanged' } | ConvertTo-Json | Set-Content -LiteralPath $receiptPath -Encoding UTF8 -ErrorAction Stop
            } elseif ($interfaces[0].Forwarding -eq 'Disabled') {
                @{ State = 'Pending' } | ConvertTo-Json | Set-Content -LiteralPath $receiptPath -Encoding UTF8 -ErrorAction Stop
                $interfaces[0] | Set-NetIPInterface -Forwarding Enabled -ErrorAction Stop
                @{ State = 'Created'; InterfaceIndex = $interfaces[0].InterfaceIndex } | ConvertTo-Json | Set-Content -LiteralPath $receiptPath -Encoding UTF8 -ErrorAction Stop
            } else { throw 'Unknown forwarding state; no changes applied.' }
            """;
        step.RollbackCommand = prelude + Environment.NewLine + $$"""
            if (Test-Path -LiteralPath $receiptPath) {
                $receipt = Get-Content -LiteralPath $receiptPath -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
                if ($receipt.State -eq 'Created') {
                    {{query}}
                    if ($interfaces[0].InterfaceIndex -ne $receipt.InterfaceIndex) { throw 'Interface identity changed; refusing rollback.' }
                    $interfaces[0] | Set-NetIPInterface -Forwarding Disabled -ErrorAction Stop
                    @{ State = 'RolledBack' } | ConvertTo-Json | Set-Content -LiteralPath $receiptPath -Encoding UTF8 -ErrorAction Stop
                } elseif ($receipt.State -notin @('Unchanged', 'RolledBack')) { throw 'Forwarding change outcome uncertain; inspect manually.' }
            }
            """;
        return step;
    }

    internal static string Quote(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}

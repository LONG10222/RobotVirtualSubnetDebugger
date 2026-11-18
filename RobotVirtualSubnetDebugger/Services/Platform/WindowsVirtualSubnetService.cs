using System.IO;
using System.Net;
using RobotNet.Windows.Wpf.Models;
using RobotNet.Windows.Wpf.Services.Logging;

namespace RobotNet.Windows.Wpf.Services.Platform;

public sealed class WindowsVirtualSubnetService : IVirtualSubnetService
{
    public const string RouteNatSafetyNotice = "原 IP 路由/NAT 不校验共享密钥；本程序未设置仅允许 B 电脑或指定机械臂端口的转发防火墙。启用后可能允许互联 LAN 上其他主机经 A 转发，请仅在已隔离且可信的实验网络使用，不连接公共或校园共享网络。";
    private const string NatName = "RobotNetTargetNat";
    private readonly INetworkAdapterService _networkAdapterService;
    private readonly IPrivilegeService _privilegeService;
    private readonly IRouteApplyService _routeApplyService;
    private readonly INatApplyService _natApplyService;
    private readonly IAdapterIpConfigurationService _adapterIpConfigurationService;
    private readonly ILogService _logService;

    public WindowsVirtualSubnetService(
        INetworkAdapterService networkAdapterService,
        IPrivilegeService privilegeService,
        IRouteApplyService routeApplyService,
        INatApplyService natApplyService,
        IAdapterIpConfigurationService adapterIpConfigurationService,
        ILogService logService)
    {
        _networkAdapterService = networkAdapterService;
        _privilegeService = privilegeService;
        _routeApplyService = routeApplyService;
        _natApplyService = natApplyService;
        _adapterIpConfigurationService = adapterIpConfigurationService;
        _logService = logService;
    }

    public VirtualSubnetPlan BuildPlan(AppConfig config)
    {
        var plan = new VirtualSubnetPlan
        {
            Mode = VirtualSubnetMode.RouteNat,
            Role = config.Role,
            GatewayLanIp = config.GatewayLanIp,
            TargetDeviceIp = config.TargetDeviceIp,
            VirtualIp = config.VirtualIp,
            VirtualSubnetMask = config.VirtualSubnetMask,
            RequiresAdministrator = true,
            WillModifySystem = true,
            Summary = "第五阶段采用目标网段精确路由 + 网关端 NAT 路线，避免接管默认路由、DNS、WiFi 或 VPN。"
        };

        if (!TryCreateCidr(config.TargetDeviceIp, config.VirtualSubnetMask, out var targetCidr, out var targetError))
        {
            plan.Status = DiagnosticStatus.Error;
            plan.Warnings.Add(targetError);
            plan.Summary = "目标设备 IP 或虚拟掩码无效，无法生成真实虚拟网段计划。";
            return plan;
        }

        if (!IsUsableHost(config.TargetDeviceIp, config.VirtualSubnetMask))
        {
            plan.Status = DiagnosticStatus.Error;
            plan.Warnings.Add("机械臂目标必须是有效 IPv4 单播主机地址，不可使用网络或广播地址。");
            return plan;
        }
        plan.TargetSubnetCidr = $"{IPAddress.Parse(config.TargetDeviceIp)}/32";
        if (targetCidr == "0.0.0.0/0")
        {
            plan.Status = DiagnosticStatus.Error;
            plan.Warnings.Add("目标网段不能是 0.0.0.0/0，禁止接管默认路由。");
            plan.Summary = "目标网段非法：会影响所有上网流量。";
            return plan;
        }

        var adapters = _networkAdapterService.GetAdapters();
        AddCoexistenceWarnings(plan, config, adapters);
        if (plan.Status == DiagnosticStatus.Error) return plan;
        if (!config.EnableVirtualSubnetMode)
        {
            plan.Status = DiagnosticStatus.Warning;
            plan.Warnings.Add("虚拟网段模式未启用。当前只会使用 TCP 代理模式。");
        }

        switch (config.Role)
        {
            case DeviceRole.DebugClient:
                BuildDebugClientPlan(plan, config, adapters);
                break;
            case DeviceRole.GatewayAgent:
                BuildGatewayAgentPlan(plan, config, adapters);
                break;
            default:
                plan.Status = DiagnosticStatus.Error;
                plan.Warnings.Add("请先在配置页选择调试端或网关端角色。");
                plan.Summary = "无法生成计划：角色未知。";
                break;
        }

        plan.CanGenerateScripts = plan.Status != DiagnosticStatus.Error && plan.Steps.Any(step => step.WillModifySystem);
        return plan;
    }

    public VirtualSubnetScriptExportResult ExportScripts(VirtualSubnetPlan plan)
    {
        if (!plan.CanGenerateScripts)
        {
            return new VirtualSubnetScriptExportResult
            {
                Success = false,
                Message = "当前计划不可生成脚本，请先修正错误配置。"
            };
        }

        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RobotNet.Windows.Wpf",
            "stage5");
        Directory.CreateDirectory(directory);

        var safeRole = plan.Role == DeviceRole.GatewayAgent ? "gateway" : "debug";
        var applyPath = Path.Combine(directory, $"robotnet-stage5-{safeRole}-apply.ps1");
        var rollbackPath = Path.Combine(directory, $"robotnet-stage5-{safeRole}-rollback.ps1");

        File.WriteAllText(applyPath, CreateApplyScript(plan));
        File.WriteAllText(rollbackPath, CreateRollbackScript(plan));

        _logService.Audit($"第五阶段脚本已生成：{applyPath}；{rollbackPath}");
        return new VirtualSubnetScriptExportResult
        {
            Success = true,
            Message = "第五阶段应用/回滚脚本已生成。脚本默认带确认开关，需要管理员 PowerShell 手动审阅后执行。",
            ApplyScriptPath = applyPath,
            RollbackScriptPath = rollbackPath
        };
    }

    public Task<PlatformOperationResult> CheckAsync(AppConfig config)
    {
        var plan = BuildPlan(config);
        return Task.FromResult(new PlatformOperationResult
        {
            Name = "真实虚拟网段计划",
            Status = plan.Status,
            Message = plan.Summary,
            Suggestion = plan.CanGenerateScripts
                ? $"可在“虚拟网段 Virtual Subnet”页面预览后一键应用，也可导出脚本审计。目标网段：{plan.TargetSubnetCidr}。"
                : string.Join("；", plan.Warnings),
            RequiresAdministrator = true
        });
    }

    private void BuildDebugClientPlan(
        VirtualSubnetPlan plan,
        AppConfig config,
        IReadOnlyList<NetworkAdapterInfo> adapters)
    {
        if (!IsUnicastAddress(config.GatewayLanIp))
        {
            plan.Status = DiagnosticStatus.Error;
            plan.Warnings.Add("调试端必须配置网关端 LAN IP。");
            plan.Summary = "无法生成调试端路由计划：网关端 LAN IP 无效。";
            return;
        }

        var candidates = adapters.Where(adapter => IsActiveIPv4(adapter) &&
            adapter.IPv4Address != config.GatewayLanIp && IsUsableHost(config.GatewayLanIp, adapter.SubnetMask) &&
            _networkAdapterService.IsSameSubnet(adapter.IPv4Address, adapter.SubnetMask, config.GatewayLanIp)).ToList();
        var lanAdapter = candidates.Count == 1 && !adapters.Any(a => a.IPv4Address == config.GatewayLanIp) ? candidates[0] : null;
        if (lanAdapter is null)
        {
            plan.Status = DiagnosticStatus.Error;
            plan.Warnings.Add("找不到唯一可直达网关端的活动 IPv4 网卡，或存在多个候选出口。请先确认两台电脑的互联地址及网卡配置。");
            plan.Summary = "无法安全确定调试端路由出口，已阻止应用。";
            return;
        }
        else
        {
            plan.LanInterfaceName = lanAdapter.Name;
        }

        plan.Steps.Add(new VirtualSubnetOperationStep
        {
            Order = 1,
            Name = "确认默认路由不变",
            Description = "显示当前默认路由，仅用于审计，不修改默认网关。",
            Command = "Get-NetRoute -DestinationPrefix '0.0.0.0/0' | Sort-Object RouteMetric | Format-Table -AutoSize",
            RollbackCommand = "无",
            RequiresAdministrator = false,
            WillModifySystem = false,
            RiskLevel = "Low"
        });
        if (config.EnablePreciseRoute && config.EnableVirtualSubnetMode)
        {
            plan.Steps.Add(_routeApplyService.CreatePreciseRouteStep(plan, config, plan.LanInterfaceName));
        }
        else
        {
            plan.Warnings.Add("精确路由未启用。调试端只能使用 TCP 代理模式，不能直接访问目标设备 IP。");
        }

        if (plan.Status != DiagnosticStatus.Warning)
        {
            plan.Status = DiagnosticStatus.Success;
        }

        plan.Summary = $"调试端计划：添加 {plan.TargetSubnetCidr} 精确路由到网关端 {config.GatewayLanIp}，保留默认上网和 VPN。";
    }

    private void BuildGatewayAgentPlan(
        VirtualSubnetPlan plan,
        AppConfig config,
        IReadOnlyList<NetworkAdapterInfo> adapters)
    {
        var lanMatches = adapters.Where(a => IsActiveIPv4(a) && a.IPv4Address == config.GatewayLanIp).ToList();
        var lanAdapter = lanMatches.Count == 1 ? lanMatches[0] : null;
        var targetMatches = adapters.Where(a => IsActiveIPv4(a) && a.IPv4Address == config.TargetDeviceAdapterIp).ToList();
        var targetAdapter = targetMatches.Count == 1 ? targetMatches[0] : null;
        if (lanAdapter is null || targetAdapter is null || lanAdapter.Name == targetAdapter.Name ||
            !_networkAdapterService.IsSameSubnet(config.TargetDeviceAdapterIp, config.VirtualSubnetMask, config.TargetDeviceIp) ||
            config.TargetDeviceAdapterIp == config.TargetDeviceIp ||
            targetAdapter.SubnetMask != config.VirtualSubnetMask ||
            !IsUsableHost(config.GatewayLanIp, lanAdapter.SubnetMask) ||
            !IsUsableHost(config.TargetDeviceIp, config.VirtualSubnetMask) ||
            !IsUsableHost(config.TargetDeviceAdapterIp, config.VirtualSubnetMask))
        {
            plan.Status = DiagnosticStatus.Error;
            plan.Warnings.Add("网关端必须已有两个唯一、活动且不同的网口：LAN 地址精确匹配本机，机械臂网口地址须已配置且与机械臂同网段、不同地址。禁止猜测或自动改错网卡。");
            return;
        }

        if (lanAdapter is null)
        {
            plan.Status = DiagnosticStatus.Warning;
            plan.Warnings.Add("未能自动确定网关端 LAN/WiFi 网卡，脚本会要求你手动填写 LAN InterfaceAlias。");
            plan.LanInterfaceName = "<请填写网关端 WiFi/LAN 网卡名称>";
        }
        else
        {
            plan.LanInterfaceName = lanAdapter.Name;
            if (TryCreateCidr(lanAdapter.IPv4Address, lanAdapter.SubnetMask, out var lanCidr, out _))
            {
                plan.LanSubnetCidr = lanCidr;
            }
        }

        if (targetAdapter is null)
        {
            plan.Status = DiagnosticStatus.Warning;
            plan.Warnings.Add("未能自动确定连接目标设备网段的网卡，脚本会要求你手动填写 Target InterfaceAlias。");
            plan.TargetInterfaceName = "<请填写连接目标设备网段的网卡名称>";
        }
        else
        {
            plan.TargetInterfaceName = targetAdapter.Name;
        }

        if (string.IsNullOrWhiteSpace(plan.LanSubnetCidr))
        {
            plan.LanSubnetCidr = "<请填写调试端所在 LAN 网段，例如 192.168.31.0/24>";
            plan.Warnings.Add("未能自动计算 LAN 网段，NAT 脚本需要你手动确认 InternalIPInterfaceAddressPrefix。");
            plan.Status = DiagnosticStatus.Warning;
        }

        if (!config.EnableVirtualSubnetMode)
        {
            plan.Status = DiagnosticStatus.Warning;
            plan.Summary = "虚拟网段模式未启用，网关端不会应用网口 IP、转发或 NAT。";
            return;
        }

        if (config.EnableNat)
        {
            plan.Steps.Add(new VirtualSubnetOperationStep
            {
                Order = 0, Name = "只读检查全部现有 NAT", RequiresAdministrator = true,
                WillModifySystem = false, RiskLevel = "Low", RollbackCommand = "无",
                Description = "任何不兼容或其他软件的 NAT 都阻止执行，不删除、不覆盖。",
                Command = WindowsNatApplyService.CreatePreflightCommand(NatName, plan.LanSubnetCidr)
            });
        }

        var ensureAdapterIpStep = _adapterIpConfigurationService.CreateEnsureAdapterIpStep(
            plan.TargetInterfaceName,
            config.TargetDeviceAdapterIp,
            config.VirtualSubnetMask);
        if (ensureAdapterIpStep is not null)
        {
            plan.Steps.Add(ensureAdapterIpStep);
        }

        plan.Steps.Add(new VirtualSubnetOperationStep
        {
            Order = 1,
            Name = "确认默认路由不变",
            Description = "显示当前默认路由，仅用于审计，不修改默认网关。",
            Command = "Get-NetRoute -DestinationPrefix '0.0.0.0/0' | Sort-Object RouteMetric | Format-Table -AutoSize",
            RollbackCommand = "无",
            RequiresAdministrator = false,
            WillModifySystem = false,
            RiskLevel = "Low"
        });
        plan.Steps.Add(OwnedNetworkChange.Forwarding(plan.LanInterfaceName, 20,
            "启用 LAN 网卡转发", "仅在原先关闭时启用 IPv4 转发，回滚时恢复原状态。"));
        plan.Steps.Add(OwnedNetworkChange.Forwarding(plan.TargetInterfaceName, 30,
            "启用目标设备网卡转发", "仅在原先关闭时启用 IPv4 转发，回滚时恢复原状态。"));
        if (config.EnableNat && config.EnableVirtualSubnetMode)
        {
            plan.Steps.Add(_natApplyService.CreateNatStep(NatName, plan.LanSubnetCidr));
        }
        else
        {
            plan.Warnings.Add("NAT 未启用。目标设备可能需要配置回程路由才能访问调试端。");
        }

        if (plan.Status != DiagnosticStatus.Warning)
        {
            plan.Status = DiagnosticStatus.Success;
        }

        plan.Summary = $"网关端计划：启用 {plan.LanInterfaceName} 与 {plan.TargetInterfaceName} 转发，并为 {plan.LanSubnetCidr} 创建 NAT。";
    }

    private void AddCoexistenceWarnings(
        VirtualSubnetPlan plan,
        AppConfig config,
        IReadOnlyList<NetworkAdapterInfo> adapters)
    {
        // Compare both network ranges, not just whether the robot address is in the NIC range.
        TryReadIPv4(config.TargetDeviceIp, out var robot);
        TryReadIPv4(config.VirtualSubnetMask, out var robotMask);
        var overlaps = adapters.Where(adapter => IsActiveIPv4(adapter) &&
            !(config.Role == DeviceRole.GatewayAgent && adapter.IPv4Address == config.TargetDeviceAdapterIp &&
              adapter.IPv4Address != config.GatewayLanIp) &&
            SubnetsOverlap(adapter.IPv4Address, adapter.SubnetMask, robot, robotMask)).ToList();
        if (overlaps.Count > 0)
        {
            plan.Status = DiagnosticStatus.Error;
            plan.Summary = "目标网段与非机械臂活动网卡重叠，已阻止应用。";
            plan.Warnings.Add($"请使用独立且不重叠的电脑互联网络；冲突网卡：{string.Join("、", overlaps.Select(a => a.Name))}。不会替你修改现有网络。");
        }

        plan.Warnings.Add(RouteNatSafetyNotice);
        plan.Warnings.Add("网络配置只允许目标网段精确路由，不修改 0.0.0.0/0 默认路由。");
        plan.Warnings.Add("网络配置不修改 DNS，不接管 VPN DNS。");

        var admin = _privilegeService.CheckAdministrator();
        if (!IsAdministratorSuccess(admin))
        {
            plan.Warnings.Add("当前进程不是管理员。预览和导出脚本可用，但一键应用或回滚必须启用管理员模式。");
        }
    }

    private static bool IsAdministratorSuccess(PlatformOperationResult result)
    {
        return result.Status == DiagnosticStatus.Success;
    }

    private static bool IsUnicastAddress(string ip) => TryReadIPv4(ip, out var value) &&
        (value >> 24) != 0 && (value >> 24) != 127 && (value >> 24) < 224;

    private static bool IsUsableHost(string ip, string mask)
    {
        if (!IsUnicastAddress(ip) || !TryReadIPv4(ip, out var address) ||
            !TryReadIPv4(mask, out var netmask) || !TryGetPrefixLength(netmask, out var prefix)) return false;
        return prefix >= 31 || ((address & ~netmask) != 0 && (address & ~netmask) != ~netmask);
    }

    private static bool SubnetsOverlap(string ip, string mask, uint robot, uint robotMask)
    {
        if (!TryReadIPv4(ip, out var address) || !TryReadIPv4(mask, out var netmask)) return false;
        var common = netmask & robotMask;
        return (address & common) == (robot & common);
    }

    private static bool IsActiveIPv4(NetworkAdapterInfo adapter) =>
        string.Equals(adapter.Status, "Up", StringComparison.OrdinalIgnoreCase) &&
        TryReadIPv4(adapter.IPv4Address, out _) &&
        TryReadIPv4(adapter.SubnetMask, out var mask) && TryGetPrefixLength(mask, out var prefix) && prefix > 0;

    private static NetworkAdapterInfo? FindAdapterByIp(IReadOnlyList<NetworkAdapterInfo> adapters, string ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
        {
            return null;
        }

        return adapters.FirstOrDefault(adapter => string.Equals(adapter.IPv4Address, ip, StringComparison.OrdinalIgnoreCase));
    }

    internal static List<VirtualSubnetOperationStep> GetApplySteps(VirtualSubnetPlan plan) =>
        plan.Steps.Where(step => !string.IsNullOrWhiteSpace(step.Command) && step.Command != "无")
            .OrderBy(step => step.Order).ToList();

    internal static string CreateApplyScript(VirtualSubnetPlan plan)
    {
        var commands = string.Join(Environment.NewLine, GetApplySteps(plan)
            .Select(step => $$"""
                Write-Host "[{{step.Order}}] {{EscapeScriptText(step.Name)}}"
                $robotUndo.Push({
                    {{((string.IsNullOrWhiteSpace(step.RollbackCommand) || step.RollbackCommand == "无") ? "# no mutation" : step.RollbackCommand)}}
                })
                {{step.Command}}
                """));

        return $$"""
            # RobotNet.Windows.Wpf Stage 5 Apply Script
            # Plan: {{plan.PlanId}}
            # Role: {{plan.Role}}
            # Mode: {{plan.Mode}}
            # Target subnet: {{plan.TargetSubnetCidr}}
            # This script is generated for manual review. Run in Administrator PowerShell only.
            # SECURITY: Route/NAT is NOT protected by the shared pairing key.
            # It enables LAN-source forwarding without a B-only or robot-port firewall.
            # Use only an isolated, trusted lab network; never a public/shared campus LAN.

            #Requires -RunAsAdministrator
            $ErrorActionPreference = 'Stop'
            Write-Warning '{{RouteNatSafetyNotice}}'
            $ConfirmRobotNetStage5 = $false

            if (-not $ConfirmRobotNetStage5) {
                throw 'Review this script first, then set $ConfirmRobotNetStage5 = $true to apply Stage 5 network changes.'
            }

            if ('{{plan.TargetSubnetCidr}}' -eq '0.0.0.0/0') {
                throw 'Refusing to modify default route 0.0.0.0/0.'
            }

            $operationDirectory = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'RobotNet.Windows.Wpf/operations'
            New-Item -ItemType Directory -Path $operationDirectory -Force | Out-Null
            $operationLock = [System.IO.File]::Open((Join-Path $operationDirectory 'network-operation.lock'), [System.IO.FileMode]::OpenOrCreate, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
            try {
            Write-Host 'RobotNet Stage 5 apply started.' 
            Write-Host 'Default route preview. This script must not modify it.'
            Get-NetRoute -DestinationPrefix '0.0.0.0/0' | Sort-Object RouteMetric | Format-Table -AutoSize

            $robotUndo = [System.Collections.Generic.Stack[scriptblock]]::new()
            try {
                {{commands}}
            } catch {
                $originalFailure = $_
                $undoFailures = @()
                while ($robotUndo.Count -gt 0) {
                    try { & ($robotUndo.Pop()) } catch { $undoFailures += $_.Exception.Message }
                }
                if ($undoFailures.Count -gt 0) {
                    throw ("Application failed: " + $originalFailure.Exception.Message + "; rollback incomplete, inspect receipts: " + ($undoFailures -join '; '))
                }
                throw $originalFailure
            }

            Write-Host 'RobotNet Stage 5 apply completed.'
            } finally { $operationLock.Dispose() } 
            """;
    }

    internal static string CreateRollbackScript(VirtualSubnetPlan plan)
    {
        var commands = string.Join(Environment.NewLine, plan.Steps
            .Where(step => !string.IsNullOrWhiteSpace(step.RollbackCommand) &&
                           !string.Equals(step.RollbackCommand, "无", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(step => step.Order)
            .Select(step => $$"""
                Write-Host "[Rollback {{step.Order}}] {{EscapeScriptText(step.Name)}}"
                try {
                    {{step.RollbackCommand}}
                } catch { $rollbackErrors += $_.Exception.Message }
                """));

        return $$"""
            # RobotNet.Windows.Wpf Stage 5 Rollback Script
            # Plan: {{plan.PlanId}}
            # Role: {{plan.Role}}
            # Mode: {{plan.Mode}}
            # This script is generated for manual review. Run in Administrator PowerShell only.
            # SECURITY: Route/NAT is NOT protected by the shared pairing key.
            # It enables LAN-source forwarding without a B-only or robot-port firewall.
            # Use only an isolated, trusted lab network; never a public/shared campus LAN.

            #Requires -RunAsAdministrator
            $ErrorActionPreference = 'Stop'
            $ConfirmRobotNetStage5Rollback = $false

            if (-not $ConfirmRobotNetStage5Rollback) {
                throw 'Review this rollback script first, then set $ConfirmRobotNetStage5Rollback = $true to rollback Stage 5 network changes.'
            }

            $operationDirectory = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'RobotNet.Windows.Wpf/operations'
            New-Item -ItemType Directory -Path $operationDirectory -Force | Out-Null
            $operationLock = [System.IO.File]::Open((Join-Path $operationDirectory 'network-operation.lock'), [System.IO.FileMode]::OpenOrCreate, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
            try {
            $rollbackErrors = @()
            {{commands}}
            if ($rollbackErrors.Count -gt 0) { throw ("Rollback incomplete; all steps attempted: " + ($rollbackErrors -join '; ')) }

            Write-Host 'RobotNet Stage 5 rollback completed.'
            } finally { $operationLock.Dispose() } 
            """;
    }

    private static string EscapePowerShell(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }

    private static string EscapeScriptText(string value)
    {
        return value.Replace("\"", "`\"", StringComparison.Ordinal);
    }

    private static bool TryCreateCidr(string ip, string mask, out string cidr, out string error)
    {
        cidr = string.Empty;
        error = string.Empty;

        if (!TryReadIPv4(ip, out var ipValue))
        {
            error = $"无效 IPv4 地址：{ip}";
            return false;
        }

        if (!TryReadIPv4(mask, out var maskValue))
        {
            error = $"无效 IPv4 掩码：{mask}";
            return false;
        }

        if (!TryGetPrefixLength(maskValue, out var prefixLength))
        {
            error = $"子网掩码不是连续掩码：{mask}";
            return false;
        }

        var network = ipValue & maskValue;
        cidr = $"{ToIPv4String(network)}/{prefixLength}";
        return true;
    }

    private static bool TryGetPrefixLength(uint mask, out int prefixLength)
    {
        prefixLength = 0;
        var seenZero = false;
        for (var i = 31; i >= 0; i--)
        {
            var bit = (mask & (1u << i)) != 0;
            if (bit && seenZero)
            {
                return false;
            }

            if (bit)
            {
                prefixLength++;
            }
            else
            {
                seenZero = true;
            }
        }

        return true;
    }

    private static bool TryReadIPv4(string value, out uint result)
    {
        result = 0;
        // Reject alternate IPv4 spellings (integer, hex, short form and padded octets).
        // Address ownership and conflict checks compare strings, so accepting aliases
        // could mistake the robot address for a distinct local adapter address.
        if (string.IsNullOrWhiteSpace(value) || !IPAddress.TryParse(value, out var address) ||
            address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
            !string.Equals(value, address.ToString(), StringComparison.Ordinal))
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        if (bytes.Length != 4)
        {
            return false;
        }

        result =
            ((uint)bytes[0] << 24) |
            ((uint)bytes[1] << 16) |
            ((uint)bytes[2] << 8) |
            bytes[3];
        return true;
    }

    private static string ToIPv4String(uint value)
    {
        return string.Join(".",
            (value >> 24) & 0xFF,
            (value >> 16) & 0xFF,
            (value >> 8) & 0xFF,
            value & 0xFF);
    }
}

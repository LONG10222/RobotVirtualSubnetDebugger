using System.IO;
using System.Net;
using RobotNet.Windows.Wpf.Models;
using RobotNet.Windows.Wpf.Services.Logging;

namespace RobotNet.Windows.Wpf.Services.Platform;

public sealed class WindowsVirtualSubnetService : IVirtualSubnetService
{
    private const string NatName = "RobotNetTargetNat";
    private readonly INetworkAdapterService _networkAdapterService;
    private readonly IPrivilegeService _privilegeService;
    private readonly ILogService _logService;

    public WindowsVirtualSubnetService(
        INetworkAdapterService networkAdapterService,
        IPrivilegeService privilegeService,
        ILogService logService)
    {
        _networkAdapterService = networkAdapterService;
        _privilegeService = privilegeService;
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

        plan.TargetSubnetCidr = targetCidr;
        if (targetCidr == "0.0.0.0/0")
        {
            plan.Status = DiagnosticStatus.Error;
            plan.Warnings.Add("目标网段不能是 0.0.0.0/0，禁止接管默认路由。");
            plan.Summary = "目标网段非法：会影响所有上网流量。";
            return plan;
        }

        var adapters = _networkAdapterService.GetAdapters();
        AddCoexistenceWarnings(plan, config, adapters);

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

        plan.CanGenerateScripts = plan.Status != DiagnosticStatus.Error && plan.Steps.Count > 0;
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
                ? $"可在“虚拟网段 Virtual Subnet”页面生成管理员脚本。目标网段：{plan.TargetSubnetCidr}。"
                : string.Join("；", plan.Warnings),
            RequiresAdministrator = true
        });
    }

    private void BuildDebugClientPlan(
        VirtualSubnetPlan plan,
        AppConfig config,
        IReadOnlyList<NetworkAdapterInfo> adapters)
    {
        if (!IPAddress.TryParse(config.GatewayLanIp, out _))
        {
            plan.Status = DiagnosticStatus.Error;
            plan.Warnings.Add("调试端必须配置网关端 LAN IP。");
            plan.Summary = "无法生成调试端路由计划：网关端 LAN IP 无效。";
            return;
        }

        var lanAdapter = FindBestLanAdapter(adapters, config.GatewayLanIp);
        if (lanAdapter is null)
        {
            plan.Status = DiagnosticStatus.Warning;
            plan.Warnings.Add("未能自动确定调试端 LAN/WiFi 网卡，脚本会要求你手动填写 InterfaceAlias。");
            plan.LanInterfaceName = "<请填写调试端 WiFi/LAN 网卡名称>";
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
        plan.Steps.Add(new VirtualSubnetOperationStep
        {
            Order = 2,
            Name = "添加目标网段精确路由",
            Description = $"仅把 {plan.TargetSubnetCidr} 指向网关端 {config.GatewayLanIp}，不影响其他互联网或 VPN 流量。",
            Command = $"New-NetRoute -DestinationPrefix '{plan.TargetSubnetCidr}' -InterfaceAlias '{EscapePowerShell(plan.LanInterfaceName)}' -NextHop '{config.GatewayLanIp}' -RouteMetric 5 -PolicyStore ActiveStore",
            RollbackCommand = $"Remove-NetRoute -DestinationPrefix '{plan.TargetSubnetCidr}' -NextHop '{config.GatewayLanIp}' -Confirm:$false",
            RequiresAdministrator = true,
            WillModifySystem = true,
            RiskLevel = "Medium"
        });

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
        var lanAdapter = FindAdapterByIp(adapters, config.GatewayLanIp) ?? FindBestLanAdapter(adapters, config.GatewayLanIp);
        var targetAdapter = FindAdapterByIp(adapters, config.TargetDeviceAdapterIp) ??
                            adapters.FirstOrDefault(adapter => adapter.IsTargetNetworkCandidate);

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
        plan.Steps.Add(new VirtualSubnetOperationStep
        {
            Order = 2,
            Name = "启用 LAN 网卡转发",
            Description = "允许网关端转发来自调试端 LAN 的目标网段流量。",
            Command = $"Set-NetIPInterface -InterfaceAlias '{EscapePowerShell(plan.LanInterfaceName)}' -Forwarding Enabled",
            RollbackCommand = $"Set-NetIPInterface -InterfaceAlias '{EscapePowerShell(plan.LanInterfaceName)}' -Forwarding Disabled",
            RequiresAdministrator = true,
            WillModifySystem = true,
            RiskLevel = "Medium"
        });
        plan.Steps.Add(new VirtualSubnetOperationStep
        {
            Order = 3,
            Name = "启用目标设备网卡转发",
            Description = "允许网关端把流量转发到目标设备网段。",
            Command = $"Set-NetIPInterface -InterfaceAlias '{EscapePowerShell(plan.TargetInterfaceName)}' -Forwarding Enabled",
            RollbackCommand = $"Set-NetIPInterface -InterfaceAlias '{EscapePowerShell(plan.TargetInterfaceName)}' -Forwarding Disabled",
            RequiresAdministrator = true,
            WillModifySystem = true,
            RiskLevel = "Medium"
        });
        plan.Steps.Add(new VirtualSubnetOperationStep
        {
            Order = 4,
            Name = "创建网关端 NAT",
            Description = $"把来自 {plan.LanSubnetCidr} 的调试端流量 NAT 到目标设备网段，避免目标设备需要回程路由。",
            Command = $"New-NetNat -Name '{NatName}' -InternalIPInterfaceAddressPrefix '{plan.LanSubnetCidr}'",
            RollbackCommand = $"Remove-NetNat -Name '{NatName}' -Confirm:$false",
            RequiresAdministrator = true,
            WillModifySystem = true,
            RiskLevel = "High"
        });

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
        var overlaps = adapters
            .Where(adapter => string.Equals(adapter.Status, "Up", StringComparison.OrdinalIgnoreCase) &&
                              !string.IsNullOrWhiteSpace(adapter.IPv4Address) &&
                              !string.IsNullOrWhiteSpace(adapter.SubnetMask) &&
                              _networkAdapterService.IsSameSubnet(adapter.IPv4Address, adapter.SubnetMask, config.TargetDeviceIp) &&
                              !adapter.IsTargetNetworkCandidate)
            .ToList();

        if (overlaps.Count > 0)
        {
            plan.Status = DiagnosticStatus.Warning;
            plan.Warnings.Add($"目标设备网段与活动网卡重叠：{string.Join("、", overlaps.Select(adapter => $"{adapter.Name}({adapter.IPv4Address})"))}。");
        }

        plan.Warnings.Add("第五阶段脚本只允许目标网段精确路由，不修改 0.0.0.0/0 默认路由。");
        plan.Warnings.Add("第五阶段脚本不修改 DNS，不接管 VPN DNS。");

        var admin = _privilegeService.CheckAdministrator();
        if (!IsAdministratorSuccess(admin))
        {
            plan.Warnings.Add("当前进程不是管理员。生成脚本不需要管理员，但执行脚本必须使用管理员 PowerShell。");
        }
    }

    private static bool IsAdministratorSuccess(PlatformOperationResult result)
    {
        return result.Status == DiagnosticStatus.Success;
    }

    private NetworkAdapterInfo? FindBestLanAdapter(IReadOnlyList<NetworkAdapterInfo> adapters, string gatewayLanIp)
    {
        var direct = FindAdapterByIp(adapters, gatewayLanIp);
        if (direct is not null)
        {
            return direct;
        }

        return adapters.FirstOrDefault(adapter => adapter.IsLanCandidate && !adapter.IsVirtual) ??
               adapters.FirstOrDefault(adapter => !string.IsNullOrWhiteSpace(adapter.Gateway) && !adapter.IsVirtual);
    }

    private static NetworkAdapterInfo? FindAdapterByIp(IReadOnlyList<NetworkAdapterInfo> adapters, string ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
        {
            return null;
        }

        return adapters.FirstOrDefault(adapter => string.Equals(adapter.IPv4Address, ip, StringComparison.OrdinalIgnoreCase));
    }

    private static string CreateApplyScript(VirtualSubnetPlan plan)
    {
        var commands = string.Join(Environment.NewLine, plan.Steps
            .Where(step => !string.Equals(step.Command, "无", StringComparison.OrdinalIgnoreCase))
            .Select(step => $"""
                Write-Host "[{step.Order}] {EscapeScriptText(step.Name)}"
                {step.Command}
                """));

        return $$"""
            # RobotNet.Windows.Wpf Stage 5 Apply Script
            # Plan: {{plan.PlanId}}
            # Role: {{plan.Role}}
            # Mode: {{plan.Mode}}
            # Target subnet: {{plan.TargetSubnetCidr}}
            # This script is generated for manual review. Run in Administrator PowerShell only.

            #Requires -RunAsAdministrator
            $ErrorActionPreference = 'Stop'
            $ConfirmRobotNetStage5 = $false

            if (-not $ConfirmRobotNetStage5) {
                throw 'Review this script first, then set $ConfirmRobotNetStage5 = $true to apply Stage 5 network changes.'
            }

            if ('{{plan.TargetSubnetCidr}}' -eq '0.0.0.0/0') {
                throw 'Refusing to modify default route 0.0.0.0/0.'
            }

            Write-Host 'RobotNet Stage 5 apply started.'
            Write-Host 'Default route preview. This script must not modify it.'
            Get-NetRoute -DestinationPrefix '0.0.0.0/0' | Sort-Object RouteMetric | Format-Table -AutoSize

            {{commands}}

            Write-Host 'RobotNet Stage 5 apply completed.'
            """;
    }

    private static string CreateRollbackScript(VirtualSubnetPlan plan)
    {
        var commands = string.Join(Environment.NewLine, plan.Steps
            .Where(step => !string.IsNullOrWhiteSpace(step.RollbackCommand) &&
                           !string.Equals(step.RollbackCommand, "无", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(step => step.Order)
            .Select(step => $"""
                Write-Host "[Rollback {step.Order}] {EscapeScriptText(step.Name)}"
                {step.RollbackCommand}
                """));

        return $$"""
            # RobotNet.Windows.Wpf Stage 5 Rollback Script
            # Plan: {{plan.PlanId}}
            # Role: {{plan.Role}}
            # Mode: {{plan.Mode}}
            # This script is generated for manual review. Run in Administrator PowerShell only.

            #Requires -RunAsAdministrator
            $ErrorActionPreference = 'Continue'
            $ConfirmRobotNetStage5Rollback = $false

            if (-not $ConfirmRobotNetStage5Rollback) {
                throw 'Review this rollback script first, then set $ConfirmRobotNetStage5Rollback = $true to rollback Stage 5 network changes.'
            }

            {{commands}}

            Write-Host 'RobotNet Stage 5 rollback completed.'
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
        if (!IPAddress.TryParse(value, out var address))
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

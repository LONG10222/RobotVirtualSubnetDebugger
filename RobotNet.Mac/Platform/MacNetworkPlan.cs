using RobotNet.Windows.Wpf.Models;
using RobotNet.Windows.Wpf.Services.Platform;
using RobotNet.Windows.Wpf.Utils;

namespace RobotNet.Mac.Platform;

internal sealed class MacNetworkPlan(INetworkAdapterService adapters) : IVirtualSubnetService
{
    private static readonly string Script = ReadScript();

    public VirtualSubnetPlan BuildPlan(AppConfig config)
    {
        var plan = new VirtualSubnetPlan
        {
            Role = config.Role,
            TargetDeviceIp = config.TargetDeviceIp,
            TargetDeviceAdapterIp = config.TargetDeviceAdapterIp,
            GatewayLanIp = config.GatewayLanIp,
            VirtualIp = config.VirtualIp,
            VirtualSubnetMask = config.VirtualSubnetMask
        };
        if (config.Role is not (DeviceRole.GatewayAgent or DeviceRole.DebugClient))
            return Reject(plan, "请先选择主机 A 或主机 B。");
        if (!MacIpv4.IsHost(config.TargetDeviceIp) || config.TargetDevicePort is < 1 or > 65535)
            return Reject(plan, "请输入有效的设备 IPv4 地址和 TCP 端口。");

        if (!config.EnableVirtualSubnetMode || (config.Role == DeviceRole.DebugClient && !config.EnablePreciseRoute))
        {
            if (!config.EnableTcpProxyMode)
                return Reject(plan, "请至少启用 TCP 代理或目标网段路由。");
            plan.Summary = $"TCP 代理模式 · 目标 {config.TargetDeviceIp}:{config.TargetDevicePort}";
            plan.Status = DiagnosticStatus.Success;
            plan.RequiresAdministrator = false;
            return plan;
        }

        if (!MacIpv4.TryNetwork(config.TargetDeviceIp, config.VirtualSubnetMask, out var target) ||
            !target.IsUsableHost(config.TargetDeviceIp))
            return Reject(plan, "请输入与设备地址匹配的连续子网掩码。");
        plan.TargetSubnetCidr = target.Cidr;

        if (!MacIpv4.IsHost(config.GatewayLanIp))
            return Reject(plan, "请输入主机 A 的局域网 IPv4 地址。");

        var active = adapters.GetAdapters().Where(adapter => !adapter.IsVirtual && adapter.Status == "Up").ToArray();
        NetworkAdapterInfo? lan;
        if (config.Role == DeviceRole.GatewayAgent)
            lan = active.FirstOrDefault(adapter => adapter.IPv4Address == config.GatewayLanIp);
        else
            lan = active.Where(adapter => adapter.IPv4Address != config.GatewayLanIp &&
                                          adapters.IsSameSubnet(adapter.IPv4Address, adapter.SubnetMask, config.GatewayLanIp))
                .OrderByDescending(adapter => MacIpv4.TryNetwork(adapter.IPv4Address, adapter.SubnetMask, out var network)
                    ? network.PrefixLength : 0)
                .FirstOrDefault();

        if (lan is null || !MacNetworkAdapters.IsHardwareInterface(lan.Name) ||
            !MacIpv4.TryNetwork(lan.IPv4Address, lan.SubnetMask, out var lanNetwork) ||
            !lanNetwork.IsUsableHost(config.GatewayLanIp))
            return Reject(plan, "找不到与主机 A 地址对应的活动局域网网卡。");
        if (lanNetwork.Overlaps(target))
            return Reject(plan, $"局域网与设备网段 {target.Cidr} 重叠。请选择不同网段，或改用 TCP 代理模式。");

        plan.LanInterfaceName = lan.Name;
        plan.LanSubnetCidr = lanNetwork.Cidr;
        if (config.Role == DeviceRole.DebugClient)
        {
            if (active.Any(adapter => MacIpv4.TryNetwork(adapter.IPv4Address, adapter.SubnetMask, out var local) &&
                                      local.Overlaps(target)))
                return Reject(plan, $"设备网段 {target.Cidr} 与本机已连接网段重叠。");
            plan.Summary = $"将 {target.Cidr} 经 {lan.Name} 精确路由到主机 A（{config.GatewayLanIp}）";
            AddStep(plan, "添加目标网段路由", plan.Summary,
                $"route -n add -net {target.Cidr} {config.GatewayLanIp}",
                $"route -n delete -net {target.Cidr} {config.GatewayLanIp}");
        }
        else
        {
            if (!target.IsUsableHost(config.TargetDeviceAdapterIp) ||
                config.TargetDeviceAdapterIp == config.TargetDeviceIp)
                return Reject(plan, "请给设备网口填写目标网段内另一个可用 IPv4 地址。");

            var otherInterfaces = active.Where(adapter => adapter.Name != lan.Name &&
                MacNetworkAdapters.IsHardwareInterface(adapter.Name)).ToArray();
            var choices = otherInterfaces.Where(adapter => adapter.IPv4Address == config.TargetDeviceAdapterIp ||
                (MacIpv4.TryNetwork(adapter.IPv4Address, adapter.SubnetMask, out var existing) &&
                 existing.Contains(config.TargetDeviceIp))).Select(adapter => adapter.Name).Distinct().ToArray();
            if (choices.Length == 0)
                choices = otherInterfaces.Select(adapter => adapter.Name).Distinct().ToArray();
            var device = string.IsNullOrWhiteSpace(config.TargetDeviceInterfaceName)
                ? choices.Length == 1 ? choices[0] : string.Empty
                : config.TargetDeviceInterfaceName.Trim();
            if (!otherInterfaces.Any(adapter => adapter.Name == device))
                return Reject(plan, "请填写机械臂网线所接的 macOS 网口（例如 en5）。网口须处于活动状态，且与局域网网卡不同。");
            if (active.Any(adapter => adapter.Name != device &&
                MacIpv4.TryNetwork(adapter.IPv4Address, adapter.SubnetMask, out var network) && network.Overlaps(target)))
                return Reject(plan, $"设备网段 {target.Cidr} 已配置在其他网口。");
            plan.TargetInterfaceName = device;
            plan.Summary = $"{lan.Name} 上的调试端可经 {device} 访问设备网段 {target.Cidr}";
            AddStep(plan, "确保设备网口 IP",
                $"若 {device} 缺少 {config.TargetDeviceAdapterIp}，添加 IPv4 别名。",
                $"ifconfig {device} inet {config.TargetDeviceAdapterIp} netmask {target.DottedMask} alias",
                "移除本次新增的别名");
            if (config.EnableNat)
                AddStep(plan, "应用 PF NAT",
                    $"仅转换从 {lanNetwork.Cidr} 发往 {target.Cidr} 的流量。",
                    NatRule(device, lanNetwork.Cidr, target.Cidr, config.TargetDeviceAdapterIp),
                    "清理 RobotNet 专用 NAT 锚点和 PF 引用");
            else
                plan.Warnings.Add("NAT 已关闭，设备需要配置返回调试端网段的路由。");
            AddStep(plan, "启用 IPv4 转发", "启用 macOS 系统级 IPv4 转发。",
                "sysctl -w net.inet.ip.forwarding=1", "恢复应用前的 IPv4 转发状态");
        }

        plan.RequiresAdministrator = true;
        plan.CanGenerateScripts = true;
        plan.WillModifySystem = true;
        plan.Status = plan.Warnings.Count == 0 ? DiagnosticStatus.Success : DiagnosticStatus.Warning;
        return plan;
    }

    public Task<PlatformOperationResult> CheckAsync(AppConfig config)
    {
        var plan = BuildPlan(config);
        return Task.FromResult(new PlatformOperationResult
        {
            Name = "macOS 网络配置",
            Status = plan.Status,
            RequiresAdministrator = plan.RequiresAdministrator && plan.WillModifySystem,
            Message = plan.Summary,
            Suggestion = string.Join("；", plan.Warnings)
        });
    }

    public VirtualSubnetScriptExportResult ExportScripts(VirtualSubnetPlan plan)
    {
        var result = new VirtualSubnetScriptExportResult();
        if (!plan.CanGenerateScripts || plan.Status == DiagnosticStatus.Error)
        {
            result.Message = "当前计划没有可导出的系统网络操作。";
            return result;
        }

        try
        {
            var directory = Path.Combine(AppPaths.AppDataDirectory, "scripts", plan.PlanId);
            Directory.CreateDirectory(directory);
            result.ApplyScriptPath = Path.Combine(directory, "apply.sh");
            result.RollbackScriptPath = Path.Combine(directory, "rollback.sh");
            File.WriteAllText(result.ApplyScriptPath, "#!/bin/sh\nexec " + ApplyCommand(plan) + "\n");
            File.WriteAllText(result.RollbackScriptPath, "#!/bin/sh\nexec " + RollbackCommand() + "\n");
            result.Success = true;
            result.Message = "已导出。审阅后用 sudo /bin/sh 执行脚本。脚本仅配置系统网络。";
        }
        catch (IOException ex)
        {
            result.Message = $"脚本导出失败：{ex.Message}";
        }
        catch (UnauthorizedAccessException ex)
        {
            result.Message = $"脚本导出失败：{ex.Message}";
        }
        return result;
    }

    public static string ApplyCommand(VirtualSubnetPlan plan)
    {
        if (plan.Role == DeviceRole.DebugClient)
            return Command("client", plan.TargetSubnetCidr, plan.GatewayLanIp, plan.LanInterfaceName,
                plan.TargetDeviceIp, plan.VirtualSubnetMask);

        // Use the same canonical values shown in the preview.
        MacIpv4.TryNetwork(plan.TargetDeviceIp, plan.VirtualSubnetMask, out var target);
        var enableNat = plan.Steps.Any(step => step.Command.StartsWith("nat on ", StringComparison.Ordinal));
        return Command("gateway", plan.TargetInterfaceName, plan.TargetDeviceAdapterIp, target.DottedMask,
            plan.TargetSubnetCidr, plan.LanSubnetCidr, enableNat ? "yes" : "no", $"0x{target.Mask:x8}", plan.TargetDeviceIp);
    }

    public static string RollbackCommand() => Command("rollback");

    private static string NatRule(string device, string lan, string target, string alias) =>
        $"nat on {device} inet from {lan} to {target} -> {alias}";

    private static string Command(string action, params string[] args) =>
        "/bin/sh -c " + MacPrivilege.Quote(Script) + " robotnet " +
        string.Join(" ", new[] { action, MacPrivilege.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture) }
            .Concat(args).Select(MacPrivilege.Quote));

    private static string ReadScript()
    {
        using var stream = typeof(MacNetworkPlan).Assembly.GetManifestResourceStream("RobotNet.Mac.Platform.network.sh")
            ?? throw new InvalidOperationException("找不到 macOS 网络配置脚本。");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static VirtualSubnetPlan Reject(VirtualSubnetPlan plan, string message)
    {
        plan.Status = DiagnosticStatus.Error;
        plan.Summary = message;
        plan.Warnings.Add(message);
        return plan;
    }

    private static void AddStep(VirtualSubnetPlan plan, string name, string description, string command, string rollback)
    {
        plan.Steps.Add(new VirtualSubnetOperationStep
        {
            Order = plan.Steps.Count + 1,
            Name = name,
            Description = description,
            Command = command,
            RollbackCommand = rollback,
            RiskLevel = "Medium",
            WillModifySystem = true,
            RequiresAdministrator = true
        });
    }
}

# 第五阶段：真实虚拟网段安全落地

第五阶段的目标是让调试端能够访问网关端后的目标设备网段，同时不破坏用户现有 WiFi、VPN 和正常上网链路。

当前实现选择“目标网段精确路由 + 网关端 NAT”作为 MVP 主路线。它比直接内置驱动级虚拟网卡更保守，也更容易审计和回滚。

## 已完成范围

- 新增 `虚拟网段 Virtual Subnet` 页面。
- 新增 `IVirtualSubnetService` 平台接口。
- 新增 `WindowsVirtualSubnetService` Windows 实现。
- 按角色生成虚拟网段计划。
- 调试端生成目标网段精确路由脚本。
- 网关端生成 IP 转发和 NAT 脚本。
- 同时生成应用脚本和回滚脚本。
- 诊断页加入真实虚拟网段计划检查。
- 检查管理员权限、目标网段冲突、WiFi/VPN 共存风险。
- 脚本默认带确认开关，必须人工改为 `true` 后才会执行。

## 调试端逻辑

调试端不会接管默认路由，只生成类似下面的目标网段精确路由：

```powershell
New-NetRoute -DestinationPrefix '192.168.1.0/24' -InterfaceAlias '<WiFi/LAN 网卡>' -NextHop '192.168.31.20' -RouteMetric 5 -PolicyStore ActiveStore
```

这表示只有访问 `192.168.1.0/24` 目标设备网段的流量会交给网关端 LAN IP，普通互联网、WiFi 和 VPN 流量仍按系统原有路由走。

## 网关端逻辑

网关端脚本会启用 LAN 网卡与目标设备网卡的转发，并创建 Windows NAT：

```powershell
Set-NetIPInterface -InterfaceAlias '<WiFi/LAN 网卡>' -Forwarding Enabled
Set-NetIPInterface -InterfaceAlias '<目标设备网卡>' -Forwarding Enabled
New-NetNat -Name 'RobotNetTargetNat' -InternalIPInterfaceAddressPrefix '192.168.31.0/24'
```

NAT 的目的，是避免目标设备必须额外配置回程路由。对很多工业设备、控制器或封闭设备来说，这比要求设备认识调试端网段更容易落地。

## 安全边界

当前 WPF 程序不会静默执行任何真实系统网络修改。它只生成脚本，脚本本身也包含：

- `#Requires -RunAsAdministrator`
- `$ConfirmRobotNetStage5 = $false`
- `$ConfirmRobotNetStage5Rollback = $false`
- 默认路由 `0.0.0.0/0` 防护检查

用户必须用管理员 PowerShell 打开脚本，审阅命令，把确认变量改为 `true`，才会真正修改系统网络配置。

## WiFi/VPN 共存原则

第五阶段必须满足以下约束：

1. 不修改或删除系统默认路由 `0.0.0.0/0`。
2. 不修改 DNS，不接管 VPN DNS。
3. 只添加目标设备网段的精确路由。
4. 不把目标设备网段错误绑定到 VPN/Tunnel 网卡。
5. 检测到网段重叠时提示风险。
6. 任何真实修改都提供回滚脚本。

## 使用流程

1. 两台电脑都先完成设置页配置。
2. 网关端选择 `GatewayAgent`，填写目标设备 IP、目标设备端口、目标设备网卡 IP。
3. 调试端选择 `DebugClient`，填写目标设备 IP、虚拟掩码、网关端 LAN IP。
4. 打开诊断页，运行诊断。
5. 打开虚拟网段页，点击“刷新计划”。
6. 确认计划中没有错误；如果网卡名为空或是占位符，先人工修正配置或生成脚本后手动改脚本。
7. 点击“生成脚本”。
8. 到 `%AppData%\RobotNet.Windows.Wpf\stage5` 查看应用脚本和回滚脚本。
9. 用管理员 PowerShell 审阅并执行应用脚本。
10. 需要撤销时，用管理员 PowerShell 审阅并执行回滚脚本。

## 当前仍不包含

- 不内置 Wintun/TUN 驱动。
- 不内置 TAP/桥接。
- 不提供透明 UDP、ICMP、广播或组播转发。
- 不自动提权执行脚本。
- 不自动结束其他进程或抢占端口。
- 不保证所有目标设备协议都能通过 TCP 代理覆盖。

## 下一阶段

第六阶段应该进入生产化：

- 自动化测试。
- 安装包。
- 代码签名。
- 崩溃日志。
- 持久化操作审计。
- 脚本执行前后状态快照。
- 长时间稳定性测试。

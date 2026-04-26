# RobotNet.Windows.Wpf

## 本次阶段化设计更新

本次继续按阶段推进项目设计，重点完成第六阶段：`生产发布与 GitHub 更新`。

新增文档：

- `docs/PHASE_ROADMAP.md`：完整阶段路线图。
- `docs/STAGE3_TCP_PROXY_DESIGN.md`：第三阶段 TCP 端口代理设计。
- `docs/STAGE4_SECURITY_STABILITY.md`：第四阶段认证、加密、心跳和稳定性设计。
- `docs/STAGE5_VIRTUAL_SUBNET_DESIGN.md`：第五阶段真实虚拟网段、路由/NAT、WiFi/VPN 共存和回滚设计。
- `docs/STAGE6_PRODUCTION_RELEASE.md`：第六阶段 GitHub Releases 更新、崩溃报告、持久化日志和发布流程。

新增/完成代码：

- `Models/TcpProxyStatus.cs`
- `Models/TcpProxySessionInfo.cs`
- `Models/TcpProxyControlMessage.cs`
- `Models/VirtualSubnetPlan.cs`
- `Models/VirtualSubnetOperationStep.cs`
- `Models/VirtualSubnetMode.cs`
- `Models/VirtualSubnetScriptExportResult.cs`
- `Services/Proxy/ITcpProxyService.cs`
- `Services/Proxy/SafeTcpProxyService.cs`
- `Services/Platform/IVirtualSubnetService.cs`
- `Services/Platform/WindowsVirtualSubnetService.cs`
- `ViewModels/VirtualSubnetViewModel.cs`
- `Views/VirtualSubnetView.xaml`
- `Services/Updates/IUpdateService.cs`
- `Services/Updates/GitHubReleaseUpdateService.cs`
- `Services/CrashReporting/ICrashReportService.cs`
- `Services/CrashReporting/FileCrashReportService.cs`
- `ViewModels/ProductionViewModel.cs`
- `Views/ProductionView.xaml`
- `scripts/publish-release.ps1`
- `.github/workflows/release.yml`

当前第六阶段状态：

- 第一、二、三、四、五、六阶段 MVP 均已完成。
- `SharedKey` 已从预留字段变成 TCP 代理强制认证配置。
- 控制消息已加入协议版本、会话令牌、nonce、时间戳和 HMAC-SHA256 签名。
- 数据帧已加入 AES-GCM 加密封装，业务字节流不再以明文帧载荷传输。
- TCP 代理已从“握手后裸流复制”升级为“签名加密帧协议”，支持 `Data`、`Heartbeat`、`CloseConnection` 和 `Error` 消息。
- 已加入心跳、空闲超时、连接重试、断线清理和 `AUDIT` 审计日志。
- 设置页已支持生成共享密钥，并可配置心跳间隔、空闲超时和重试次数。
- TCP 代理页已显示安全模式、稳定性策略和最后心跳时间。
- 新增 `虚拟网段 Virtual Subnet` 页面。
- 第五阶段主路线采用“目标网段精确路由 + 网关端 NAT”，避免接管默认路由、DNS、WiFi 或 VPN。
- 程序会生成管理员 PowerShell 应用/回滚脚本，但不会静默执行系统网络修改。
- 生成脚本默认带确认开关，必须人工审阅并在管理员 PowerShell 中显式确认后才能生效。
- 诊断页已加入真实虚拟网段计划检查。
- 新增 `发布与更新 Release` 页面。
- 默认使用 GitHub 仓库 `LONG10222/RobotVirtualSubnetDebugger` 做更新源。
- 支持通过 GitHub Releases 检查 latest 版本并下载 Windows 发布包。
- 启动时可后台检查更新，设置页可关闭。
- 已加入崩溃报告、持久化日志、发布脚本、SHA256 校验文件和 GitHub Actions 标签发布流程。

第六阶段已经完成发布工程化 MVP：可以通过 GitHub Releases 发布 portable zip 或自包含 exe，应用可以检查并下载 GitHub latest release。程序仍然不会静默自替换、不会静默安装驱动、不会静默修改系统网络配置；如果后续要内置 Wintun/TUN 驱动级透明虚拟 IP，需要进入网络能力增强。

## 开发进度总览

当前状态：项目已经完成第六阶段及之前的 MVP，可以编译、运行、发布到 GitHub Releases，并通过应用内更新页检查和下载新版本；已经具备带认证、签名、加密、心跳和审计的应用层 TCP 代理转发能力，也能生成真实目标网段精确路由/NAT 的管理员应用与回滚脚本。

一句话判断：

- 可以用于演示：可以。
- 可以用于两台电脑之间做设备发现、配置、诊断、模拟连接：可以。
- 可以让调试端代码通过本机 TCP 端口真实访问网关端后的目标设备端口：可以。
- 可以生成脚本让调试端通过目标网段精确路由访问远端目标设备网段：可以，需要管理员手动审阅执行脚本。
- 可以通过 GitHub Releases 检查和下载更新：可以。
- 可以生成发布包和 SHA256 校验文件：可以。
- 可以记录崩溃报告和持久化运行日志：可以。
- 可以让调试端自动拥有驱动级虚拟 IP 并透明接入远端网段：尚未内置，需要后续 Wintun/TUN 驱动方案。
- 可以打包给别人试用 Windows 预览版：可以。
- 可以作为正式签名生产网络工具发布：需要配置代码签名证书并完成实机长稳测试。

| 阶段 | 内容 | 状态 | 完成度 |
| --- | --- | --- | --- |
| 第一阶段 | WPF 骨架、MVVM、配置、网卡识别、日志、诊断、基础界面 | 已完成 | 100% |
| 第二阶段 | UDP 设备发现、模拟会话、连接锁定、端口占用检测、自动端口选择 | 已完成 MVP | 100% |
| 第三阶段 | TCP 端口代理，一键从调试端访问网关端后的目标设备端口 | 已完成 MVP | 100% |
| 第四阶段 | 认证、共享密钥校验、加密封装、心跳、断线清理、连接重试、审计日志 | 已完成 MVP | 100% |
| 第五阶段 | 目标网段精确路由、网关端 NAT、WiFi/VPN 共存、操作预览、回滚脚本 | 已完成 MVP | 100% |
| 第六阶段 | GitHub Releases 更新、发布包、代码签名接入点、崩溃日志、持久化日志、发布 workflow | 已完成 MVP | 100% |

按最终目标估算：

- “安全模拟版 MVP”：100% 完成。
- “可演示预览版”：100% 完成。
- “可真实转发 TCP 设备调试流量”：100% 完成。
- “可生成真实目标网段路由/NAT 脚本的工具”：100% 完成。
- “可通过 GitHub Releases 发布和更新的 Windows 工具”：100% 完成。
- “完整虚拟网段远程调试工具”：约 80% 完成。
- “可签名生产发布的正式工具”：约 70% 完成，剩余主要取决于签名证书、实机长稳测试和是否接入 Wintun/TUN。

还剩的核心内容：

1. 测试覆盖：配置迁移、端口检测、代理转发、认证失败、心跳超时、连接中断、脚本生成和多连接场景。
2. 驱动级透明虚拟 IP：如必须让调试端拥有 `192.168.1.101/24` 这类真实虚拟网卡地址，需要接入 Wintun/TUN。
3. 生产签名：需要你准备代码签名证书，并在发布脚本或 GitHub Actions 中配置证书密钥。
4. 生产安全：证书体系、自动密钥轮换、脚本执行审计、长期稳定性实测。

推荐下一步：用 GitHub 创建 `v0.6.0` Release 验证更新链路；随后补实机长稳测试、签名证书和是否需要 Wintun/TUN 的决策。

## 一键连接与端口占用处理

为保证后续“一键连接”更顺畅，项目已加入端口占用检测和自动端口选择能力。

当前处理逻辑：

- 启动设备发现前，会检查 `DiscoveryPort` 对应的 UDP 端口是否可用。
- 启动模拟连接前，会检查 `DiscoveryPort`、`LocalListenPort` 和网关端 `ProxyControlPort`。
- 如果端口空闲，流程直接继续。
- 如果端口被本程序自身占用，例如发现服务已经启动，则允许继续。
- 如果端口被其他程序占用，会尝试识别占用进程的 PID 和进程名。
- 如果能找到替代端口，会自动切换到下一个可用端口，并保存到本地配置。
- 如果找不到可用端口，会停止一键连接流程，并在诊断、日志或状态栏提示原因。

新增配置项：

| 配置项 | 默认值 | 说明 |
| --- | --- | --- |
| `DiscoveryPort` | `47831` | UDP 局域网设备发现端口 |
| `LocalListenPort` | `30003` | 后续 TCP 代理/本地监听使用的端口 |
| `ProxyControlPort` | `47832` | 网关端代理控制通道监听端口 |
| `ProxyHeartbeatIntervalSeconds` | `5` | TCP 代理心跳间隔 |
| `ProxyIdleTimeoutSeconds` | `20` | TCP 代理空闲超时 |
| `ProxyReconnectAttempts` | `2` | TCP 代理连接失败重试次数 |

新增代码：

- `Models/PortProtocol.cs`
- `Models/PortCheckResult.cs`
- `Models/ConnectionPreflightResult.cs`
- `Services/Platform/IPortAvailabilityService.cs`
- `Services/Platform/WindowsPortAvailabilityService.cs`
- `Services/Diagnostics/IConnectionPreflightService.cs`
- `Services/Diagnostics/ConnectionPreflightService.cs`

设计边界：

- 端口检测和 PID 识别封装在 Windows 平台服务中。
- ViewModel 只调用服务，不直接执行系统命令。
- 当前不会自动结束占用端口的进程。
- 后续如果要做“关闭占用进程”或“释放端口”，必须增加明确用户确认，不能静默执行。

## 项目审视与打包状态

结论：当前项目可以打包为 Windows MVP 预览版，并通过 GitHub Releases 做版本发布和更新包分发。它可以演示应用层 TCP 代理转发，也可以生成真实目标网段精确路由/NAT 的管理员应用与回滚脚本；但不能宣称已经内置驱动级虚拟网卡或透明 TUN/TAP 隧道。

当前已经具备发布条件：

- WPF 主程序可以编译运行。
- 本机网卡枚举、配置保存、日志、诊断、UDP 发现、模拟连接会话已经接入界面。
- 第二阶段会话锁定逻辑已具备：已连接一个对端后，不能再连接其他设备；只有手动断开，或发现服务确认原对端离线/搜索不到，才释放锁定。
- 第三阶段 TCP 代理闭环已具备：调试端监听本机端口，网关端监听控制端口，网关端连接目标设备端口，双端进行字节流转发。
- 第四阶段安全稳定能力已具备：SharedKey、HMAC-SHA256、AES-GCM、会话令牌、nonce 防重放、心跳、空闲超时、连接重试和审计日志已经接入。
- 第五阶段虚拟网段计划已具备：按角色生成调试端精确路由脚本、网关端转发/NAT 脚本和对应回滚脚本。
- 第六阶段发布工程化已具备：GitHub Releases 更新检查、发布包下载、启动后台检查、崩溃报告、持久化日志、发布脚本和 GitHub Actions 标签发布 workflow。
- 所有真实系统修改仍封装在平台服务接口中，程序只生成脚本，不静默执行。
- 已验证 `Debug` 构建、`Release` 框架依赖发布、自包含单文件发布均可成功生成。

当前作为正式生产工具发布前仍需确认：

- 尚未内置 Wintun/TUN 或 TAP 驱动级虚拟网卡。
- 尚未自动执行管理员脚本；真实路由/NAT 变更需要用户手动审阅并执行。
- 尚未实现透明 UDP/ICMP/广播/组播网段转发。
- 尚未实现生产级证书体系、自动密钥轮换和跨版本协议兼容测试。
- 代码签名证书不能提交到仓库，需要由发布者本机或 GitHub Secrets 配置。
- 当前发布形态是 portable zip 和自包含 exe，未内置 MSI/MSIX 图形安装器。

已验证/推荐的发布命令：

```powershell
cd "c:\Users\51426\Documents\python项目\Visual studio\RobotVirtualSubnetDebugger"
.\scripts\publish-release.ps1 -SkipSign
```

发布产物：

- 框架依赖版 zip：`artifacts\release\RobotNet.Windows.Wpf-0.6.0-win-x64-framework.zip`，目标电脑需要安装 .NET 8 Desktop Runtime。
- 自包含单文件版：`artifacts\release\RobotNet.Windows.Wpf-0.6.0-win-x64-self-contained.exe`，体积较大，但不要求目标电脑预装 .NET 运行时。
- 校验文件：`artifacts\release\checksums.sha256`。

GitHub Release 流程：

```powershell
git tag v0.6.0
git push origin v0.6.0
```

`.github/workflows/release.yml` 会在 tag 推送后构建发布包并创建 GitHub Release。应用内更新页默认检查 `https://github.com/LONG10222/RobotVirtualSubnetDebugger/releases/latest`。

`artifacts/` 已加入 `.gitignore`，发布产物不应提交到源码仓库。

## 同类项目调研

参考项目与文档：

- Tailscale：<https://tailscale.com/kb/1508/control-data-planes>
- Tailscale 连接类型：<https://tailscale.com/docs/reference/connection-types>
- ZeroTier 物理网络集成：<https://docs.zerotier.com/integrating-physical-networks/>
- ZeroTier 路由到物理网络：<https://docs.zerotier.com/route-between-phys-and-virt/>
- Wintun：<https://www.wintun.net/>
- SoftEther VPN Bridge：<https://www.softether.org/4-docs/1-manual/5/5.3>
- OpenVPN Ethernet Bridging：<https://openvpn.net/community-docs/ethernet-bridging.html>
- .NET 发布说明：<https://learn.microsoft.com/en-us/dotnet/core/deploying/deploy-with-cli>
- .NET 单文件发布：<https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview>

从这些项目可以提炼出几个稳定架构规律：

1. 控制面和数据面分离。
   Tailscale 明确区分控制面和数据面。控制面负责身份、设备发现、策略、路由配置和连接协调；数据面才负责真实加密传输和包转发。本项目当前已经在做控制面雏形：配置、发现、诊断、会话状态、连接锁定。后续真实隧道应继续保持这个边界。

2. 真实虚拟网段通常需要虚拟网卡。
   Wintun 是 Windows 上常见的三层 TUN 驱动，适合用户态程序读写 IP 包。OpenVPN/SoftEther 则展示了 TAP、桥接和虚拟交换机路线。后续如果要让本机程序像访问本地网段一样访问目标设备，需要明确选择三层 TUN 路由/NAT，还是二层 TAP/桥接。

3. 路由优先，桥接谨慎。
   ZeroTier 文档建议多数场景优先使用路由，只有设备依赖广播、组播或二层发现时才考虑桥接。桥接更像“把两个以太网段拼起来”，风险更高，也更容易出现环路、DHCP 冲突和 WiFi/Windows 桥接限制。

4. NAT/Masquerade 是访问物理网段的常见折中。
   ZeroTier 和 SoftEther 都有类似思路：当远端客户端需要访问一个不能安装客户端的物理设备时，可以让网关端作为路由器或 NAT 节点。对于你的场景，如果目标设备没有回程路由，网关端 NAT 可能比“让调试端虚拟 IP 直接出现在目标网段里”更容易落地。

5. 工业设备调试可以先做应用层代理。
   在真实虚拟网卡之前，可以先做 TCP 端口代理或会话转发。例如目标设备默认端口是 `30003`，调试端连接本机端口，程序把 TCP 流转发到网关端，再由网关端连接目标设备。这不能覆盖所有协议，但能先验证两端发现、认证、会话、心跳、断线恢复和日志审计。

## 后续技术路线建议

第五阶段已经完成目标网段精确路由 + 网关端 NAT 的安全脚本生成路线，不直接内置驱动级虚拟网卡的路线仍然成立。后续建议按以下顺序推进：

1. 补充自动化测试：覆盖端口占用、认证失败、代理转发、目标设备不可达、心跳超时、中途断线和脚本生成。
2. 如果必须支持驱动级透明虚拟 IP，再接入 Wintun/TUN；只有必须支持二层广播时再考虑 TAP/桥接。
3. 继续补实机长稳测试、代码签名证书、安装器和生产级审计。

## 第二阶段进展

第二阶段 MVP 已完成，核心是安全的“连接会话 Session”模块。该模块用于模拟调试端与网关端之间的隧道握手、配置校验、共享密钥检查和会话状态变化。

本次继续补充了发现页与连接会话的联动：

- `设备发现 Discovery` 页面可以选择一个在线网关端设备。
- 点击 `连接选中网关` 后，会把该设备的 LAN IP 写入本机配置中的 `GatewayLanIp`。
- 随后会启动模拟连接会话，并把选中的设备作为当前对端。
- 已连接一台对端后，本机不能再连接其他设备。
- 如果对端也广播自己已经连接其他设备，本机也会拒绝连接它。
- 只有手动点击 `停止会话`，或者发现服务刷新后确认原对端已经离线/搜索不到，才会释放当前会话锁定。

第二阶段不会执行真实系统网络修改：

- 不创建真实虚拟网卡。
- 不修改系统路由。
- 不执行 NAT 或桥接。
- 第二阶段本身不转发真实目标设备流量；第三阶段已经通过 TCP 代理完成应用层转发。
- 不执行管理员权限命令。

新增页面：

- `连接会话 Session`：可启动或停止模拟会话，显示会话 ID、当前角色、网关端 LAN IP、目标设备 IP、虚拟 IP、最后更新时间和握手步骤列表。

新增服务与模型：

- `Services/Tunnel/ITunnelSessionService.cs`
- `Services/Tunnel/SimulatedTunnelSessionService.cs`
- `Models/SessionStatus.cs`
- `Models/TunnelSessionInfo.cs`
- `Models/HandshakeStepInfo.cs`

第二阶段发现结果与连接会话联动已经完成，第四阶段安全与稳定性、第五阶段虚拟网段脚本生成也已经完成，后续重点转入第六阶段生产发布。

`RobotNet.Windows.Wpf` 是一个 Windows 优先的 WPF 桌面工具，用于“虚拟网段远程调试”场景。

这里的“目标设备”是通用概念，可以是机械臂、PLC、工控机、相机控制器、传感器控制器或其他只在独立网段内可访问的设备。机械臂只是一个示例，不是程序限定的设备类型。

当前项目已经完成第六阶段及之前的 MVP：可运行的软件骨架、配置、网卡识别、设备发现、诊断、日志、操作教程、连接会话、安全应用层 TCP 代理、认证、签名、加密、心跳、审计日志、虚拟网段计划、路由/NAT 脚本生成、GitHub Releases 更新检查、崩溃报告、持久化日志和发布脚本已经具备。当前仍不会在程序内静默创建虚拟网卡，也不会静默修改系统路由、NAT、桥接、防火墙或其他系统网络配置。

## 使用场景

两台 Windows 电脑处于同一个 WiFi 或局域网内。

电脑 A 作为网关端，连接目标设备所在网段：

- WiFi/LAN IP 示例：`192.168.31.20`
- 目标设备网卡 IP 示例：`192.168.1.100`
- 目标设备 IP 示例：`192.168.1.10`

电脑 B 作为调试端，运行调试代码：

- WiFi/LAN IP 示例：`192.168.31.30`
- 后续计划创建虚拟网卡 IP：`192.168.1.101/24`

长期目标是让电脑 B 上的本地调试代码，可以通过电脑 A 访问目标设备所在网段。

## 当前已实现功能

- WPF 主窗口，左侧导航栏，右侧内容页。
- 程序内完整操作教程页面。
- MVVM 结构：`Models`、`ViewModels`、`Views`、`Services` 分层。
- 本机网卡枚举，基于 `System.Net.NetworkInformation`。
- 显示本机网卡的 IPv4、掩码、网关、DNS、MAC、状态、类型。
- 判断网卡是否可能是虚拟网卡。
- 判断网卡是否可能是目标设备网段候选网卡。
- 判断网卡是否可能是局域网候选网卡。
- 本地 JSON 配置保存。
- 自动生成并保存设备 ID。
- UDP 局域网设备发现。
- 内存日志服务并绑定到 UI。
- 基础诊断服务。
- 按角色区分调试端和网关端诊断逻辑。
- 真实 Ping 测试。
- 真实 TCP 端口测试。
- TCP 代理页面：调试端本机监听、网关端控制监听、目标设备连接和双向字节流转发。
- TCP 代理运行状态统计：连接数、发送/接收字节数、最后错误和最后更新时间。
- TCP 代理安全能力：SharedKey 强制认证、HMAC-SHA256 消息签名、AES-GCM 数据帧加密、会话令牌和 nonce 防重放。
- TCP 代理稳定性能力：心跳、空闲超时、连接重试、关闭帧、断线清理和审计日志。
- 网络共存诊断：识别默认上网链路、WiFi、VPN/Tunnel/虚拟网卡和目标设备网段冲突风险。
- 虚拟网段页面：按角色生成目标网段精确路由、网关端 NAT、操作预览和回滚脚本。
- 发布与更新页面：GitHub Releases 检查、更新包下载、发布检查项和日志/崩溃/更新目录入口。
- 崩溃报告：全局异常捕获并写入 `%AppData%\RobotNet.Windows.Wpf\crashes`。
- 持久化日志：运行日志写入 `%AppData%\RobotNet.Windows.Wpf\logs`。
- 发布工程：`scripts/publish-release.ps1` 生成发布包和 SHA256 校验文件。
- GitHub Actions：推送 `v*` 标签后自动创建 GitHub Release。
- 平台能力接口：权限、虚拟网卡、路由、隧道、虚拟网段计划、端口检测、GitHub 更新。
- Windows 平台安全实现：生成可审阅脚本，不在程序内静默执行真实系统修改。

## 当前未实现功能

- 不内置驱动级虚拟网卡。
- 不自动执行真实隧道驱动。
- 不在程序内静默执行 NAT。
- 不在程序内静默执行桥接。
- 不在程序内静默修改系统路由。
- 不修改防火墙规则。
- 不自动执行管理员权限命令。
- 不静默替换正在运行的程序；更新包下载后需要用户关闭程序并手动运行或替换。
- 不把代码签名证书放入仓库；签名证书需要发布者自行配置。
- 暂未实现 Linux/macOS 平台服务。

这些能力会在后续阶段通过平台服务接口封装，不能直接写在 UI 或 ViewModel 中。

## 页面说明

### 首页 Dashboard

显示当前设备名、设备 ID、当前角色、本机局域网 IP、目标设备 IP、虚拟 IP 和当前状态。

### 操作教程 Tutorial

内置完整使用步骤，包含：

- 两台电脑角色说明
- 电脑 A 网关端配置步骤
- 电脑 B 调试端配置步骤
- UDP 设备发现使用步骤
- 安全 TCP 代理使用步骤
- 第五阶段虚拟网段脚本生成步骤
- 真实系统修改的安全边界
- 推荐检查顺序

### 网卡管理 Adapters

显示本机所有网卡列表，并支持刷新。每个网卡会显示：

- 名称
- IPv4 地址
- 子网掩码
- 网关
- MAC 地址
- 状态
- 类型
- 是否虚拟网卡
- 是否目标网段候选
- 是否局域网候选
- DNS
- 描述

### 设备发现 Discovery

使用 UDP 广播发现同一局域网内运行本程序的设备。

当前 UDP 发现参数：

- 默认端口：`47831`
- 可在设置页修改 UDP 发现端口
- 协议标识：`RobotNetDiscoveryV1`
- 数据格式：JSON
- 数据内容：`DeviceId`、`ComputerName`、`LanIp`、`Role`、`Timestamp`

首次使用时，Windows 防火墙可能会提示是否允许局域网通信。若需要两台电脑互相发现，请允许专用网络访问。

### TCP 代理 Proxy

第三、四阶段新增页面，用于启动或停止安全应用层 TCP 代理。

调试端：

- 监听 `127.0.0.1:LocalListenPort`。
- 本机调试代码连接这个端口。
- 程序使用 SharedKey、HMAC-SHA256 和 AES-GCM 把本地 TCP 连接转发到网关端 `GatewayLanIp:ProxyControlPort`。

网关端：

- 监听 `0.0.0.0:ProxyControlPort`。
- 验证调试端签名后的 `OpenConnection` 请求。
- 连接 `TargetDeviceIp:TargetDevicePort`。
- 在调试端连接和目标设备连接之间做加密签名帧转发。

当前 TCP 代理只处理 TCP 端口级流量，不提供完整虚拟 IP、UDP、ICMP、广播、组播或透明网段能力。

### 虚拟网段 Virtual Subnet

第五阶段新增页面，用于生成真实目标网段访问的操作计划和脚本。

调试端：

- 生成目标设备网段的精确路由脚本。
- 只把 `TargetDeviceIp/VirtualSubnetMask` 对应网段指向网关端 LAN IP。
- 不修改 `0.0.0.0/0` 默认路由，不修改 DNS，不接管 VPN。

网关端：

- 生成 LAN 网卡和目标设备网卡的转发启用脚本。
- 生成 `New-NetNat` 网关端 NAT 脚本，避免目标设备必须配置回程路由。
- 生成对应回滚脚本，用于删除 NAT 和恢复转发设置。

脚本生成后保存在：

```text
%AppData%\RobotNet.Windows.Wpf\stage5
```

脚本默认不会直接生效，需要用管理员 PowerShell 打开，审阅后把确认变量改为 `true` 才能执行。

### 发布与更新 Release

第六阶段新增页面，用于检查 GitHub Releases、下载发布包和查看发布检查项。

- 当前版本来自程序集版本，当前为 `0.6.0`。
- 默认仓库为 `LONG10222/RobotVirtualSubnetDebugger`。
- 点击“检查更新”会请求 GitHub latest release。
- 点击“下载更新”会下载优先匹配的 Windows 发布包。
- 更新包保存到 `%AppData%\RobotNet.Windows.Wpf\updates`。
- 页面提供日志目录、崩溃目录和更新目录入口。
- 当前不会静默替换正在运行的程序，需要用户手动运行下载后的发布包。

### 配置 Settings

可以配置：

- 当前角色：未知、调试端、网关端
- 目标设备 IP
- 目标设备端口
- UDP 发现端口
- 虚拟 IP
- 虚拟子网掩码
- 网关端 LAN IP
- 目标设备网卡 IP
- SharedKey 共享密钥
- TCP 代理心跳间隔
- TCP 代理空闲超时
- TCP 代理重试次数
- GitHub 更新仓库 Owner
- GitHub 更新仓库 Repo
- 是否启动时检查更新

### 诊断 Diagnostics

提供一键诊断。

公共诊断包括：

- 当前角色检查
- 本机网卡检查
- 目标设备 IP 格式检查
- 虚拟 IP 格式检查
- 虚拟掩码格式检查
- 目标设备端口检查
- UDP 发现端口检查

调试端诊断包括：

- 网关端 LAN IP 是否已配置
- 调试端到网关端 Ping 测试
- 虚拟 IP 是否与目标设备 IP 规划在同一网段
- 调试端局域网网卡检查

网关端诊断包括：

- 目标设备网段候选网卡检查
- 目标设备网卡 IP 检查
- 网关端到目标设备 Ping 测试
- 网关端到目标设备 TCP 端口测试

诊断页还会显示后续平台能力边界：

- 网络共存策略检查
- 默认上网链路检查
- WiFi 共存检查
- VPN/Tunnel 共存检查
- 目标网段冲突检查
- 真实虚拟网段计划检查
- 管理员权限检查
- 虚拟网卡能力检查
- 路由能力检查
- 隧道能力检查
- NAT/桥接能力检查

驱动级虚拟网卡和驱动级隧道目前仍是安全占位；目标网段精确路由和网关端 NAT 已通过第五阶段脚本生成方式落地。

### 日志 Logs

显示程序运行期间的内存日志，同时日志会持久化到：

```text
%AppData%\RobotNet.Windows.Wpf\logs
```

## 项目结构

```text
RobotVirtualSubnetDebugger/
  Commands/
  Models/
  Services/
    Configuration/
    CrashReporting/
    Diagnostics/
    Discovery/
    Logging/
    Platform/
    Proxy/
    Tunnel/
    Updates/
  Utils/
  ViewModels/
  Views/
```

关键平台接口：

```text
Services/Platform/IPrivilegeService.cs
Services/Platform/IVirtualAdapterService.cs
Services/Platform/IRouteService.cs
Services/Platform/ITunnelService.cs
Services/Platform/IVirtualSubnetService.cs
Services/Platform/IPortAvailabilityService.cs
Services/Updates/IUpdateService.cs
Services/CrashReporting/ICrashReportService.cs
```

当前 Windows 实现：

```text
Services/Platform/WindowsPrivilegeService.cs
Services/Platform/WindowsVirtualAdapterService.cs
Services/Platform/WindowsRouteService.cs
Services/Platform/WindowsTunnelService.cs
Services/Platform/WindowsVirtualSubnetService.cs
Services/Platform/WindowsPortAvailabilityService.cs
Services/Updates/GitHubReleaseUpdateService.cs
Services/CrashReporting/FileCrashReportService.cs
```

其中 `WindowsVirtualSubnetService` 会生成第五阶段目标网段精确路由、网关端 NAT 和回滚脚本；其他驱动级虚拟网卡/隧道能力仍保持安全占位。

## 配置文件

配置保存位置：

```text
%AppData%\RobotNet.Windows.Wpf\config.json
```

设备 ID 保存位置：

```text
%AppData%\RobotNet.Windows.Wpf\device.id
```

默认配置：

| 配置项 | 默认值 |
| --- | --- |
| Role | `Unknown` |
| TargetDeviceIp | `192.168.1.10` |
| TargetDevicePort | `30003` |
| DiscoveryPort | `47831` |
| LocalListenPort | `30003` |
| ProxyControlPort | `47832` |
| SharedKey | 空，需要用户生成或填写 |
| ProxyHeartbeatIntervalSeconds | `5` |
| ProxyIdleTimeoutSeconds | `20` |
| ProxyReconnectAttempts | `2` |
| VirtualIp | `192.168.1.101` |
| VirtualSubnetMask | `255.255.255.0` |
| GitHubRepositoryOwner | `LONG10222` |
| GitHubRepositoryName | `RobotVirtualSubnetDebugger` |
| EnableUpdateCheckOnStartup | `true` |

旧版配置兼容：

- 旧字段 `RobotIp` 会自动迁移到 `TargetDeviceIp`。
- 旧字段 `RobotPort` 会自动迁移到 `TargetDevicePort`。
- 旧字段 `RobotAdapterIp` 会自动迁移到 `TargetDeviceAdapterIp`。
- 迁移后重新保存配置时，会写入新的 `TargetDevice*` 字段。

## 开发环境

- Windows
- .NET SDK 8 或更高版本
- WPF 支持

当前没有引入第三方 NuGet 包。

## 运行方式

进入 WPF 项目目录：

```powershell
cd "c:\Users\51426\Documents\python项目\Visual studio\RobotVirtualSubnetDebugger\RobotVirtualSubnetDebugger"
```

运行：

```powershell
dotnet run
```

仅构建：

```powershell
dotnet build
```

## 安全边界

程序运行时不会静默执行真实系统网络修改。第四阶段 TCP 代理会打开用户态 TCP 监听，并转发带认证、签名和加密封装的应用层字节流；第五阶段会生成目标网段精确路由、网关端 NAT 和回滚 PowerShell 脚本，但应用本身不会直接创建虚拟网卡、不会直接修改路由、不会直接执行 NAT/桥接、防火墙或管理员权限命令。代码中涉及系统能力的地方应继续遵守以下原则：

- UI 只负责显示和绑定。
- ViewModel 不直接执行系统命令。
- 路由、虚拟网卡、隧道、NAT、桥接、防火墙、管理员权限等能力必须封装到平台服务接口。
- Windows 优先实现，但接口设计要给 Linux/macOS 留出扩展空间。
- 真正需要修改系统网络配置时，必须先给出操作预览、生成回滚脚本，并要求用户在管理员 PowerShell 中显式确认。

## WiFi、VPN 与正常上网共存原则

这个工具必须允许用户同时使用 WiFi、VPN 和正常互联网访问。当前 TCP 代理不改默认路由和 DNS，所以不会抢占系统上网路径。第五阶段脚本也只围绕目标设备网段生成精确路由和网关端 NAT，不生成接管默认上网路径的命令。

第五阶段真实虚拟网段脚本生成已按以下约束实现：

1. 不允许修改或删除系统默认路由 `0.0.0.0/0`。
2. 不允许接管系统 DNS，不改 VPN DNS。
3. 只允许添加目标设备网段的精确路由，例如 `192.168.1.0/24`，不能把所有流量导入本工具。
4. 检测到 VPN/Tunnel 网卡时，默认不把目标设备路由下发到 VPN 网卡。
5. 检测到目标设备网段与 WiFi/VPN 网段重叠时，必须提示风险，并优先建议继续使用 TCP 代理模式。
6. 所有真实路由、NAT、桥接和虚拟网卡操作都必须有操作预览、用户确认和回滚策略。

## 后续建议

1. 增加配置、网段判断、诊断逻辑、端口检测和 TCP 代理的单元测试/集成测试。
2. 将 UDP/TCP 绑定地址加入配置项，避免多网卡环境下绑定不明确。
3. 为配置迁移增加单元测试，覆盖旧版 `Robot*` 字段到新版 `TargetDevice*` 字段的迁移。
4. 准备代码签名证书，并把证书通过本机环境变量或 GitHub Secrets 接入发布流程。
5. 如果必须让调试端拥有驱动级透明虚拟 IP，再单独接入 Wintun/TUN；只有必须支持二层广播时再评估 TAP/桥接。

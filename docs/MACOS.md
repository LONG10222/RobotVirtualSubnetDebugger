# RobotNet for macOS

macOS 版提供 Windows 版已有的主机 A / B 双角色、UDP 发现与自动配对、加密 TCP 代理、目标网段路由、网口配置与 NAT、诊断、日志和发布检查。Windows 界面继续使用 WPF。macOS 程序复用同一套发现和代理实现，使用 Avalonia 界面，支持 macOS 14 及以上的 Apple Silicon 和 Intel 电脑。

## 安装与连接

从 ZIP 解压 `RobotNet.app` 后打开。按 Mac 的要求允许局域网访问。开发构建采用临时签名，分发给其他电脑时需要发布者的 Developer ID 签名和公证。安装包已包含 .NET 运行时。

1. 两台电脑连到同一 Wi-Fi/LAN。主机 A 再用另一块网卡接设备网线。
2. 主机 A 打开「简易模式」，选择「这台电脑连接硬件设备」。填写设备 IPv4、TCP 端口、设备网口 IPv4 和掩码。设备地址和网口地址必须在同一设备网段。自动识别到多块网卡时，在「网卡管理」查看名称，再填写设备网口，例如 `en5`。
3. 如果控制程序要直接连接设备的原 IP，主机 A 和主机 B 都启用「目标网段路由」。连接网关的局域网和目标设备网段需要使用不同的地址范围。
4. 主机 A 点击「配置网口并等待连接」。主机 B 选择「这台电脑运行控制代码」，刷新并选择主机 A，确认设备地址和端口后启动调试通道。应用或回滚系统网络配置时，macOS 会请求管理员授权。
5. 路由模式下在主机 B 使用设备原 IP 和端口。仅代理模式下使用界面显示的 `127.0.0.1:本地监听端口`。仅代理模式要求主机 A 已能直接访问设备。
6. 调试结束后，两端在简易模式点击「恢复网络配置」。此操作会停止发现和代理，并撤回本机应用的系统网络操作。仅退出程序时，已应用的系统配置可留到手动回滚或重启。再次应用新配置前，先回滚上一份。

同一个 LAN 上的发现服务需使用相同的 UDP 发现端口（默认 `47831`）。Mac 和 Windows 可以互相担任 A / B。Windows `v0.7.2` 使用默认控制端口 `47832`，如果旧版 A 使用了非默认端口，在新版 B 的高级设置里填实际端口。旧版 A 不会自动填设备地址或子网，请在 B 确认这些字段。简易自动配对面向可信局域网。高级模式可以手动配置共享密钥。

## 网络行为

- 主机 B 只添加精确指向设备网段的 IPv4 路由，并校验设备 IP 的有效下一跳。主机 A 按需添加网口 IPv4 别名，开启 macOS 系统级 IPv4 转发，并在独立 PF NAT 锚点里转换指定来源网段到目标网段的流量。
- 路由/NAT 模式支持直达 IP。加密发生在 TCP 代理链路。TCP 代理覆盖 TCP 设备协议，UDP 广播、组播和二层发现需要其他网络方案。
- 应用与回滚保留默认路由、DNS 和原有 PF 主规则。配置使用系统预置的 `com.apple/*` NAT 锚点。若管理员自定义 PF 主规则并移除了该锚点，NAT 会报错，可使用已有设备回程路由或 TCP 代理。
- GUI 的配置、日志、更新包及操作记录位于 `~/Library/Application Support/RobotNet/`。正在使用的系统网络状态记录在 root 可读的 `/var/run/com.long10222.robotnet/`。导出的系统配置脚本使用配套的回滚脚本恢复。
- 当前平台与 Windows 已发布版本一样使用路由/NAT 实现虚拟网段。驱动级 TUN、二层桥接和设备上机验证属于独立验证项目。

## 构建与验证

```sh
dotnet run --project RobotNet.Mac
dotnet run --project tests/RobotNet.Mac.Integration -c Release
scripts/publish-macos.sh osx-arm64
scripts/publish-macos.sh osx-x64
```

发布脚本需要 .NET 10 SDK，输出在 `artifacts/macos/`。

## 应用内更新

1. 从「应用程序」打开 RobotNet，进入「发布与更新」。
2. 点击「检查更新」和「下载更新」。应用会按版本和处理器架构匹配 ZIP，验证同一发布附带的 `checksums-macos.sha256`。
3. 停止代理并回滚正在使用的网络配置。点击「安装并重启」并确认，应用会检查包内版本、包标识和签名完整性，再替换已安装的应用。配置、日志和下载包保存在应用数据目录。

安装仅接受官方仓库的 `macos-v*` 发布，目标为「应用程序」中的 `RobotNet.app`。安装失败可在「更新目录」找到已校验的 ZIP，退出应用后手动替换。安装进程的日志位于 `logs/update-install.log`。

发布维护者修改 `RobotNet.Mac/RobotNet.Mac.csproj` 的 `Version` 后，将更改推送到 `macos-support`。Windows 和 macOS 检查通过后，GitHub Actions 会为尚未发布的版本创建 `macos-v<版本>` 预览发布，包含两种架构的 ZIP 和校验文件。

集成测试覆盖了 macOS 网卡枚举、发现消息、配对、TCP 端口检测、加密数据双向传输、错误认证和发布包选择。管理员层的 `scripts/test-macos-network.sh` 只在隔离测试机运行，使用临时虚拟网卡验证内核路由、PF 锚点、别名与回滚。设备验收使用两台电脑和实际设备，分别测试 Mac→Windows、Windows→Mac 与 Mac→Mac，在路由及代理两种模式下访问实际端口。完成后核对默认路由、设备网口和 PF 状态。

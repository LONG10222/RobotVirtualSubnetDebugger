# 第六阶段：生产发布与 GitHub 更新

第六阶段目标是把项目从“可运行 MVP”推进到“可以通过 GitHub Releases 分发的 Windows 预览版”。

## 已完成范围

- 应用版本号固定为 `0.6.0`。
- 新增 `发布与更新 Release` 页面。
- 新增 GitHub Releases 更新检查。
- 默认更新仓库：`LONG10222/RobotVirtualSubnetDebugger`。
- 支持下载 latest release 中的 Windows 发布包。
- 支持启动时后台检查更新。
- 新增崩溃报告目录和全局异常捕获。
- 日志从内存扩展为内存 UI + 本地 UTF-8 日志文件。
- 新增发布脚本 `scripts/publish-release.ps1`。
- 发布脚本生成框架依赖版、自包含单文件版、zip 包和 SHA256 校验文件。
- 发布脚本预留 SignTool 代码签名入口。
- 新增 GitHub Actions workflow：推送 `v*` 标签后自动构建并创建 GitHub Release。

## GitHub 更新逻辑

应用通过 GitHub API 请求：

```text
https://api.github.com/repos/LONG10222/RobotVirtualSubnetDebugger/releases/latest
```

检查到新版本后，更新页会显示 Release 资源列表。点击“下载更新”后，程序会把优先匹配的 Windows 发布包下载到：

```text
%AppData%\RobotNet.Windows.Wpf\updates
```

当前不会在程序运行中替换自身，也不会静默执行下载的 exe。用户需要关闭程序后手动运行或替换。这是桌面程序自更新的安全边界。

## 崩溃报告

全局异常捕获覆盖：

- `Application.DispatcherUnhandledException`
- `AppDomain.CurrentDomain.UnhandledException`
- `TaskScheduler.UnobservedTaskException`

崩溃报告写入：

```text
%AppData%\RobotNet.Windows.Wpf\crashes
```

报告内容包括时间、版本、系统、机器名、用户和异常堆栈，方便后续提交 GitHub Issue。

## 持久化日志

运行日志写入：

```text
%AppData%\RobotNet.Windows.Wpf\logs\robotnet-yyyyMMdd.log
```

UI 日志页仍显示内存日志，方便运行中查看；本地日志文件用于问题复盘。

## 发布命令

在仓库根目录执行：

```powershell
.\scripts\publish-release.ps1 -SkipSign
```

产物输出到：

```text
artifacts\release
```

主要文件：

- `RobotNet.Windows.Wpf-0.6.0-win-x64-framework.zip`
- `RobotNet.Windows.Wpf-0.6.0-win-x64-self-contained.exe`
- `checksums.sha256`

## 代码签名

发布脚本支持 SignTool，但证书不能提交到源码仓库。启用签名时配置：

```powershell
$env:SIGNTOOL_PATH = "C:\Path\to\signtool.exe"
$env:SIGN_CERT_PATH = "C:\Path\to\certificate.pfx"
$env:SIGN_CERT_PASSWORD = "证书密码"
.\scripts\publish-release.ps1
```

如果没有证书，脚本会跳过签名，仍可生成预览版发布包。

## GitHub Release 流程

推送标签即可触发 `.github/workflows/release.yml`：

```powershell
git tag v0.6.0
git push origin v0.6.0
```

GitHub Actions 会构建发布包，并创建 Release。应用内更新检查会读取该 Release。

## 当前边界

- 当前是安全下载更新包，不做静默自替换。
- 不包含私有代码签名证书。
- 不内置 MSI/MSIX 图形安装器，当前发布形态是 portable zip 和自包含 exe。
- 驱动级 Wintun/TUN 透明虚拟 IP 仍属于后续网络能力增强，不属于第六阶段发布工程化范围。

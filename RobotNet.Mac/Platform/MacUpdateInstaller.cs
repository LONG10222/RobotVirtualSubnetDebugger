using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml.Linq;
using RobotNet.Windows.Wpf.Models;
using RobotNet.Windows.Wpf.Services.Platform;
using RobotNet.Windows.Wpf.Services.Proxy;
using RobotNet.Windows.Wpf.Services.Updates;
using RobotNet.Windows.Wpf.Utils;

namespace RobotNet.Mac.Platform;

internal sealed class MacUpdateInstaller(
    ITcpProxyService proxy,
    INetworkConfigurationExecutor network,
    Action quit) : IUpdateInstaller
{
    internal const string RepositoryUrl = "https://github.com/LONG10222/RobotVirtualSubnetDebugger";
    internal const string BundleName = "RobotNet.app";
    private const string BundleId = "com.long10222.robotnet";

    public async Task PrepareAsync(UpdateCheckResult release, UpdateDownloadResult download)
    {
        if (proxy.Current.Status is TcpProxyStatus.Listening or TcpProxyStatus.Forwarding ||
            network.GetLastOperation() is { RolledBackAt: null, RollbackCommands.Count: > 0 })
            throw new InvalidOperationException("请先停止 TCP 代理并回滚正在使用的网络配置。");

        if (!release.IsUpdateAvailable || !download.Success || download.Sha256.Length != 64 ||
            !release.ReleaseUrl.Equals($"{RepositoryUrl}/releases/tag/macos-v{release.LatestVersion}", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("应用内安装需要来自 RobotNet 官方 macOS 发布的已校验更新。");

        var bundle = Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))?.Parent;
        var userApplications = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications");
        if (bundle is null || bundle.Name != BundleName || bundle.LinkTarget is not null ||
            (bundle.Parent?.FullName != "/Applications" && bundle.Parent?.FullName != userApplications))
            throw new InvalidOperationException("请将 RobotNet.app 放到「应用程序」后重试。");

        if (!OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException();
        ValidateBundle(bundle.FullName, release.CurrentVersion);
        var stage = Directory.CreateDirectory(
            Path.Combine(bundle.Parent.FullName, $".robotnet-update-{Guid.NewGuid():N}"),
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            await StageUpdateAsync(stage.FullName, download, release.LatestVersion);
            using var process = Start("/bin/sh", "-c", ReplaceScript, "robotnet-update", stage.FullName,
                bundle.FullName, Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Path.Combine(AppPaths.LogsDirectory, "update-install.log"));
        }
        catch
        {
            stage.Delete(recursive: true);
            throw;
        }
    }

    public void ExitForUpdate() => quit();

    internal static async Task StageUpdateAsync(string stage, UpdateDownloadResult download, string version)
    {
        var zipPath = Path.Combine(stage, "update.zip");
        File.Copy(download.FilePath, zipPath);
        await using (var archive = File.OpenRead(zipPath))
        {
            var hash = await SHA256.HashDataAsync(archive);
            if (!CryptographicOperations.FixedTimeEquals(hash, Convert.FromHexString(download.Sha256)))
                throw new InvalidDataException("安装包在校验后发生变化，请重新下载。");
            archive.Position = 0;
            using var zip = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true);
            foreach (var entry in zip.Entries)
            {
                if (!entry.FullName.StartsWith(BundleName + "/", StringComparison.Ordinal) ||
                    entry.FullName.Contains('\\') || entry.FullName.Split('/').Any(part => part is "." or "..") ||
                    ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000)
                    throw new InvalidDataException("安装包包含不支持的条目。");
            }
        }

        await RunAsync("/usr/bin/ditto", "-x", "-k", zipPath, stage);
        var bundle = Path.Combine(stage, BundleName);
        ValidateBundle(bundle, version);
        await RunAsync("/usr/bin/codesign", "--verify", "--deep", "--strict", bundle);
        File.Delete(zipPath);
    }

    private static void ValidateBundle(string path, string version)
    {
        using var plist = File.OpenRead(Path.Combine(path, "Contents", "Info.plist"));
        var values = XDocument.Load(plist).Root?.Element("dict")?.Elements().ToArray() ?? [];
        string? Read(string key) =>
            values.FirstOrDefault(element => element.Name == "key" && element.Value == key)?.NextNode is XElement value
                ? value.Value : null;
        if (Read("CFBundleIdentifier") != BundleId || Read("CFBundleShortVersionString") != version ||
            Read("CFBundleExecutable") != "RobotNet.Mac" ||
            !File.Exists(Path.Combine(path, "Contents", "MacOS", "RobotNet.Mac")))
            throw new InvalidDataException("安装包的应用标识或版本与 GitHub 发布不一致。");
    }

    private static Process Start(string command, params string[] arguments)
    {
        var settings = new ProcessStartInfo(command) { UseShellExecute = false };
        foreach (var argument in arguments)
            settings.ArgumentList.Add(argument);
        return Process.Start(settings) ?? throw new IOException($"无法启动 {command}。");
    }

    private static async Task RunAsync(string command, params string[] arguments)
    {
        using var process = Start(command, arguments);
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new IOException($"无法校验或解压安装包（{Path.GetFileName(command)}）。");
    }

    // The staged bundle and the installed app are siblings on the same volume.
    internal const string ReplaceScript = """
        set -eu
        stage=$1
        installed=$2
        old_pid=$3
        log=$4
        exec >>"$log" 2>&1
        echo "RobotNet update: $(/bin/date -u)"
        backup="$stage/Previous.app"
        attempt=0
        while kill -0 "$old_pid" 2>/dev/null; do
            [ "$attempt" -lt 60 ] || { echo "Timed out waiting for the current RobotNet process."; exit 1; }
            attempt=$((attempt + 1))
            /bin/sleep 1
        done
        /bin/mv "$installed" "$backup"
        if ! /bin/mv "$stage/RobotNet.app" "$installed"; then
            /bin/mv "$backup" "$installed"
            /usr/bin/open -n "$installed" || true
            exit 1
        fi
        if ! /usr/bin/open -n "$installed"; then
            /bin/mv "$installed" "$stage/RobotNet.app"
            /bin/mv "$backup" "$installed"
            /usr/bin/open -n "$installed" || true
            exit 1
        fi
        /bin/rm -r "$backup"
        /bin/rmdir "$stage"
        echo "RobotNet update installed."
        """;
}

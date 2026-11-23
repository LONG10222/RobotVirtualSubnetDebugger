using System.IO;
using System.Net.Http;
using System.Reflection;
#if ROBOTNET_MACOS
using System.Runtime.InteropServices;
using System.Security.Cryptography;
#endif
using System.Text.Json;
using System.Text.Json.Serialization;
using RobotNet.Windows.Wpf.Models;
using RobotNet.Windows.Wpf.Services.Logging;
using RobotNet.Windows.Wpf.Utils;

namespace RobotNet.Windows.Wpf.Services.Updates;

public sealed class GitHubReleaseUpdateService : IUpdateService
{
    private readonly HttpClient _httpClient;
    private readonly ILogService _logService;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public GitHubReleaseUpdateService(ILogService logService)
        : this(new HttpClient(), logService)
    {
    }

    public GitHubReleaseUpdateService(HttpClient httpClient, ILogService logService)
    {
        _httpClient = httpClient;
        _logService = logService;
    }

    public string CurrentVersion { get; } = GetCurrentVersion();

    public string UpdatesDirectory => AppPaths.UpdatesDirectory;

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        var owner = config.GitHubRepositoryOwner.Trim();
        var repository = config.GitHubRepositoryName.Trim();
        var result = new UpdateCheckResult
        {
            CurrentVersion = CurrentVersion
        };

        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repository))
        {
            result.Message = "GitHub 仓库未配置。";
            return result;
        }

        var releasesUrl = $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repository)}/releases";
#if ROBOTNET_MACOS
        var apiUrl = releasesUrl + "?per_page=30";
#else
        var apiUrl = releasesUrl + "/latest";
#endif
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            request.Headers.UserAgent.ParseAdd($"RobotNet/{CurrentVersion}");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                result.Message = "GitHub Releases 暂无发布。";
                _logService.Warning(result.Message);
                return result;
            }

            if (!response.IsSuccessStatusCode)
            {
                result.Message = $"GitHub 更新检查失败：HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
                _logService.Warning(result.Message);
                return result;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#if ROBOTNET_MACOS
            var release = (JsonSerializer.Deserialize<List<GitHubReleaseDto>>(json, _jsonOptions) ?? [])
                .Where(candidate => candidate.Assets.Any(asset => ScoreAsset(asset.Name) > 0) &&
                                    TryParseVersion(candidate.TagName, out _))
                .OrderByDescending(candidate => Version.Parse(NormalizeVersionText(candidate.TagName)))
                .FirstOrDefault();
            if (release is null)
            {
                result.Success = true;
                result.Message = "GitHub Releases 暂无适用于本机架构的 macOS 发布包。";
                result.ReleaseUrl = $"https://github.com/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repository)}/releases";
                return result;
            }
#else
            var release = JsonSerializer.Deserialize<GitHubReleaseDto>(json, _jsonOptions);
#endif
            if (release is null || string.IsNullOrWhiteSpace(release.TagName))
            {
                result.Message = "GitHub Release 返回内容无效。";
                _logService.Warning(result.Message);
                return result;
            }

            result.Success = true;
            result.LatestVersion = NormalizeVersionText(release.TagName);
            result.ReleaseName = string.IsNullOrWhiteSpace(release.Name) ? release.TagName : release.Name;
            result.ReleaseUrl = release.HtmlUrl;
            result.PublishedAt = release.PublishedAt;
            result.Assets = release.Assets
                .Select(asset => new ReleaseAssetInfo
                {
                    Name = asset.Name,
                    DownloadUrl = asset.BrowserDownloadUrl,
                    ContentType = asset.ContentType,
                    SizeBytes = asset.Size
                })
                .Where(asset => !string.IsNullOrWhiteSpace(asset.DownloadUrl))
                .ToList();
            result.IsUpdateAvailable = IsNewerVersion(result.LatestVersion, CurrentVersion);
            result.Message = result.IsUpdateAvailable
                ? $"发现新版本 {result.LatestVersion}。"
                : $"当前已是最新版本 {CurrentVersion}。";

            _logService.Info($"GitHub 更新检查完成：{result.Message}");
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            result.Message = $"GitHub 更新检查失败：{ex.Message}";
            _logService.Error("GitHub 更新检查失败。", ex);
            return result;
        }
    }

    public async Task<UpdateDownloadResult> DownloadUpdateAsync(UpdateCheckResult update, CancellationToken cancellationToken = default)
    {
        var asset = SelectBestAsset(update.Assets);
#if ROBOTNET_MACOS
        var checksums = update.Assets.FirstOrDefault(candidate => candidate.Name == "checksums-macos.sha256");
        if (asset is null || checksums is null)
#else
        if (asset is null)
#endif
        {
            return new UpdateDownloadResult
            {
                Success = false,
#if ROBOTNET_MACOS
                Message = "此发布缺少适用于本机的 macOS 发布包或 SHA256 校验文件。"
#else
                Message = "当前 Release 没有可下载的 Windows 发布包。"
#endif
            };
        }

        string? temporaryPath = null;
        try
        {
            var fileName = SanitizeFileName(asset.Name);
            var targetPath = Path.Combine(UpdatesDirectory, fileName);
            temporaryPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".part";
#if ROBOTNET_MACOS
            var checksumText = await _httpClient.GetStringAsync(checksums.DownloadUrl, cancellationToken).ConfigureAwait(false);
            var expectedHash = ReadSha256(checksumText, asset.Name);
#endif
            using var response = await _httpClient.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using (var remoteStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var fileStream = File.Create(temporaryPath))
                await remoteStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);

#if ROBOTNET_MACOS
            await using (var fileStream = File.OpenRead(temporaryPath))
            {
                var actualHash = await SHA256.HashDataAsync(fileStream, cancellationToken).ConfigureAwait(false);
                if (!CryptographicOperations.FixedTimeEquals(expectedHash, actualHash))
                    throw new InvalidDataException("macOS 发布包 SHA256 校验失败。");
            }
#endif
            File.Move(temporaryPath, targetPath, overwrite: true);

            _logService.Audit($"更新包已下载：{targetPath}");
            return new UpdateDownloadResult
            {
                Success = true,
                Message = "更新包已下载。请关闭当前程序后手动运行或替换。",
                FilePath = targetPath
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logService.Error("下载 GitHub Release 更新包失败。", ex);
            return new UpdateDownloadResult
            {
                Success = false,
                Message = $"下载失败：{ex.Message}"
            };
        }
        finally
        {
            if (temporaryPath is not null && File.Exists(temporaryPath))
            {
                try { File.Delete(temporaryPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

#if ROBOTNET_MACOS
    private static byte[] ReadSha256(string text, string fileName)
    {
        foreach (var line in text.Split('\n'))
        {
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length == 2 && fields[0].Length == 64 && fields[1].TrimStart('*') == fileName)
                return Convert.FromHexString(fields[0]);
        }

        throw new InvalidDataException($"校验文件未包含 {fileName}。");
    }
#endif

    private static ReleaseAssetInfo? SelectBestAsset(IReadOnlyList<ReleaseAssetInfo> assets)
    {
        return assets
            .OrderByDescending(asset => ScoreAsset(asset.Name))
            .FirstOrDefault(asset => ScoreAsset(asset.Name) > 0);
    }

    private static int ScoreAsset(string name)
    {
        var lower = name.ToLowerInvariant();
#if ROBOTNET_MACOS
        var architecture = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
        return lower.Contains(architecture, StringComparison.Ordinal) && lower.EndsWith(".zip", StringComparison.Ordinal) ? 1 : 0;
#else
        var score = 0;
        if (lower.Contains("win-x64", StringComparison.Ordinal))
        {
            score += 10;
        }

        if (lower.Contains("self-contained", StringComparison.Ordinal))
        {
            score += 8;
        }

        if (lower.EndsWith(".exe", StringComparison.Ordinal))
        {
            score += 6;
        }
        else if (lower.EndsWith(".zip", StringComparison.Ordinal))
        {
            score += 4;
        }

        return score;
#endif
    }

    private static string GetCurrentVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private static bool IsNewerVersion(string latest, string current)
    {
        return TryParseVersion(latest, out var latestVersion) &&
               TryParseVersion(current, out var currentVersion) &&
               latestVersion > currentVersion;
    }

    private static bool TryParseVersion(string text, out Version version)
    {
        var normalized = NormalizeVersionText(text);
        return Version.TryParse(normalized, out version!);
    }

    private static string NormalizeVersionText(string text)
    {
        var normalized = text.Trim();
        if (normalized.StartsWith("macos-", StringComparison.OrdinalIgnoreCase))
            normalized = normalized["macos-".Length..];

        if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[1..];
        }

        var dashIndex = normalized.IndexOf('-', StringComparison.Ordinal);
        return dashIndex > 0 ? normalized[..dashIndex] : normalized;
    }

    private static string SanitizeFileName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return new string(name.Select(character => invalidChars.Contains(character) ? '_' : character).ToArray());
    }

    private sealed class GitHubReleaseDto
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = string.Empty;

        [JsonPropertyName("published_at")]
        public DateTimeOffset? PublishedAt { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAssetDto> Assets { get; set; } = [];
    }

    private sealed class GitHubAssetDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = string.Empty;

        [JsonPropertyName("content_type")]
        public string ContentType { get; set; } = string.Empty;

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }
}

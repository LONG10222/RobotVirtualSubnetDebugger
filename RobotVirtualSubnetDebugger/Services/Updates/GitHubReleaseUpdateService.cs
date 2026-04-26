using System.IO;
using System.Net.Http;
using System.Reflection;
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

        var apiUrl = $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repository)}/releases/latest";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            request.Headers.UserAgent.ParseAdd($"RobotNet.Windows.Wpf/{CurrentVersion}");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                result.Message = "GitHub Releases 暂无 latest 发布。请先在仓库发布 v0.6.0 或更高版本。";
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
            var release = JsonSerializer.Deserialize<GitHubReleaseDto>(json, _jsonOptions);
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
        if (asset is null)
        {
            return new UpdateDownloadResult
            {
                Success = false,
                Message = "当前 Release 没有可下载的 Windows 发布包。"
            };
        }

        try
        {
            var fileName = SanitizeFileName(asset.Name);
            var targetPath = Path.Combine(UpdatesDirectory, fileName);
            using var response = await _httpClient.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var remoteStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var fileStream = File.Create(targetPath);
            await remoteStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);

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
    }

    private static ReleaseAssetInfo? SelectBestAsset(IReadOnlyList<ReleaseAssetInfo> assets)
    {
        return assets
            .OrderByDescending(asset => ScoreAsset(asset.Name))
            .FirstOrDefault(asset => ScoreAsset(asset.Name) > 0);
    }

    private static int ScoreAsset(string name)
    {
        var lower = name.ToLowerInvariant();
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

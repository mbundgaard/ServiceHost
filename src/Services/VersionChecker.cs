using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace ServiceHost.Services;

public class VersionChecker : IDisposable
{
    private const string GitHubRepo = "mbundgaard/ServiceHost";
    private const string ReleasesApiUrl = $"https://api.github.com/repos/{GitHubRepo}/releases/latest";
    private const string DownloadUrl = $"https://github.com/{GitHubRepo}/releases/latest/download/ServiceHost.exe";

    private readonly HttpClient _httpClient;
    private readonly TimeSpan _cacheExpiry = TimeSpan.FromMinutes(30);

    private string? _latestVersion;
    private DateTime _lastCheck = DateTime.MinValue;
    private bool _disposed;

    public string CurrentVersion { get; }

    public VersionChecker()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "ServiceHost");

        // Get version from assembly (set during build)
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        CurrentVersion = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "0.0.0";
    }

    public async Task<VersionInfo> CheckForUpdateAsync()
    {
        var info = new VersionInfo
        {
            CurrentVersion = CurrentVersion,
            DownloadUrl = DownloadUrl
        };

        // Use cached value if still valid
        if (_latestVersion != null && DateTime.UtcNow - _lastCheck < _cacheExpiry)
        {
            info.LatestVersion = _latestVersion;
            info.UpdateAvailable = IsNewerVersion(_latestVersion, CurrentVersion);
            return info;
        }

        try
        {
            var response = await _httpClient.GetAsync(ReleasesApiUrl);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var tagName = doc.RootElement.GetProperty("tag_name").GetString();

                if (tagName != null)
                {
                    _latestVersion = tagName.TrimStart('v');
                    _lastCheck = DateTime.UtcNow;

                    info.LatestVersion = _latestVersion;
                    info.UpdateAvailable = IsNewerVersion(_latestVersion, CurrentVersion);
                }
            }
        }
        catch
        {
            // Silently ignore version check failures
        }

        return info;
    }

    private static bool IsNewerVersion(string latest, string current)
    {
        var latestParts = latest.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
        var currentParts = current.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();

        for (int i = 0; i < Math.Max(latestParts.Length, currentParts.Length); i++)
        {
            var l = i < latestParts.Length ? latestParts[i] : 0;
            var c = i < currentParts.Length ? currentParts[i] : 0;

            if (l > c) return true;
            if (l < c) return false;
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _httpClient.Dispose();
    }
}

public class VersionInfo
{
    public string CurrentVersion { get; set; } = "";
    public string? LatestVersion { get; set; }
    public bool UpdateAvailable { get; set; }
    public string DownloadUrl { get; set; } = "";
    public string? UpdateMessage => UpdateAvailable
        ? $"A newer version ({LatestVersion}) is available. Download: {DownloadUrl}"
        : null;
}

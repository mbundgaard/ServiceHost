using System.IO;
using System.Text.Json;
using ServiceHost.Models;

namespace ServiceHost.Services;

public class ConfigurationService
{
    private const string ConfigFileName = "ServiceHost.json";
    private readonly string _configPath;
    private DateTime _lastModified;

    public AppConfig Config { get; private set; } = new();
    public string ConfigPath => _configPath;

    public ConfigurationService()
    {
        _configPath = Path.Combine(AppContext.BaseDirectory, ConfigFileName);
    }

    public ConfigurationService(string configPath)
    {
        _configPath = configPath;
    }

    /// <summary>
    /// Check if the config file has changed since last load.
    /// </summary>
    public bool HasConfigChanged()
    {
        if (!File.Exists(_configPath))
            return false;

        var currentModified = File.GetLastWriteTimeUtc(_configPath);
        return currentModified > _lastModified;
    }

    /// <summary>
    /// Reload the config if it has changed. Returns the list of changes.
    /// </summary>
    public async Task<ConfigChanges?> ReloadIfChangedAsync()
    {
        if (!HasConfigChanged())
            return null;

        var oldServices = Config.Services.Select(s => s.Name).ToHashSet();

        if (!await LoadAsync())
            return null;

        var newServices = Config.Services.Select(s => s.Name).ToHashSet();

        return new ConfigChanges
        {
            Added = newServices.Except(oldServices).ToList(),
            Removed = oldServices.Except(newServices).ToList()
        };
    }

    public async Task<bool> LoadAsync()
    {
        if (!File.Exists(_configPath))
        {
            return false;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_configPath);
            var config = JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            if (config != null)
            {
                Config = config;
                _lastModified = File.GetLastWriteTimeUtc(_configPath);
                return true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load config: {ex.Message}");
        }

        return false;
    }

    public string GetLogDirectory()
    {
        var logDir = Config.LogDirectory;
        if (!Path.IsPathRooted(logDir))
        {
            logDir = Path.Combine(AppContext.BaseDirectory, logDir);
        }
        return Path.GetFullPath(logDir);
    }

    public static AppConfig CreateDefaultConfig()
    {
        return new AppConfig
        {
            ApiPort = 9500,
            LogDirectory = "./logs",
            Services = new List<ServiceConfig>
            {
                new ServiceConfig
                {
                    Name = "example",
                    Command = "python",
                    Args = new List<string> { "-m", "http.server", "8080" },
                    WorkingDirectory = ".",
                    Port = 8080
                }
            }
        };
    }

    public static async Task SaveExampleConfigAsync(string path)
    {
        var config = CreateDefaultConfig();
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        await File.WriteAllTextAsync(path, json);
    }
}

public class ConfigChanges
{
    public List<string> Added { get; init; } = new();
    public List<string> Removed { get; init; } = new();
    public bool HasChanges => Added.Count > 0 || Removed.Count > 0;
}

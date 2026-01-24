using System.IO;
using System.Text.Json;
using ServiceHost.Models;

namespace ServiceHost.Services;

public class ConfigurationService
{
    private const string ConfigFileName = "ServiceHost.json";
    private readonly string _configPath;

    public AppConfig Config { get; private set; } = new();

    public ConfigurationService()
    {
        _configPath = Path.Combine(AppContext.BaseDirectory, ConfigFileName);
    }

    public ConfigurationService(string configPath)
    {
        _configPath = configPath;
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

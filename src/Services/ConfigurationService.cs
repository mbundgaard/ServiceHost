using System.IO;
using System.Text.Json;
using ServiceHost.Models;

namespace ServiceHost.Services;

public class ConfigurationService
{
    private const string ConfigFileName = "ServiceHost.json";
    private readonly string _configPath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
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

        await _fileLock.WaitAsync();
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
        finally
        {
            _fileLock.Release();
        }

        return false;
    }

    /// <summary>
    /// Save the current configuration to the JSON file.
    /// </summary>
    public async Task SaveAsync()
    {
        await _fileLock.WaitAsync();
        try
        {
            var json = JsonSerializer.Serialize(Config, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            await File.WriteAllTextAsync(_configPath, json);
            _lastModified = File.GetLastWriteTimeUtc(_configPath);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private static (bool valid, string? error) ValidateServiceName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return (false, "Service name is required");

        // Invalid filename characters
        var invalidChars = Path.GetInvalidFileNameChars();
        if (name.Any(c => invalidChars.Contains(c)))
            return (false, "Service name contains invalid characters");

        // Windows reserved names
        var reserved = new[] { "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
        if (reserved.Contains(name.ToUpperInvariant()))
            return (false, $"'{name}' is a reserved name");

        return (true, null);
    }

    /// <summary>
    /// Add a new service to the configuration and save.
    /// </summary>
    public async Task<(bool success, string? error)> AddServiceAsync(ServiceConfig service)
    {
        var (valid, validationError) = ValidateServiceName(service.Name);
        if (!valid)
        {
            return (false, validationError);
        }

        if (Config.Services.Any(s => s.Name.Equals(service.Name, StringComparison.OrdinalIgnoreCase)))
        {
            return (false, $"Service '{service.Name}' already exists");
        }

        if (string.IsNullOrWhiteSpace(service.Command))
        {
            return (false, "Service command is required");
        }

        Config.Services.Add(service);
        await SaveAsync();
        return (true, null);
    }

    /// <summary>
    /// Update an existing service in the configuration and save.
    /// </summary>
    public async Task<(bool success, string? error)> UpdateServiceAsync(string name, ServiceConfig updatedService)
    {
        var existingIndex = Config.Services.FindIndex(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existingIndex < 0)
        {
            return (false, $"Service '{name}' not found");
        }

        var (valid, validationError) = ValidateServiceName(updatedService.Name);
        if (!valid)
        {
            return (false, validationError);
        }

        if (string.IsNullOrWhiteSpace(updatedService.Command))
        {
            return (false, "Service command is required");
        }

        // If name is being changed, check for conflicts
        if (!updatedService.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
            Config.Services.Any(s => s.Name.Equals(updatedService.Name, StringComparison.OrdinalIgnoreCase)))
        {
            return (false, $"Service '{updatedService.Name}' already exists");
        }

        Config.Services[existingIndex] = updatedService;
        await SaveAsync();
        return (true, null);
    }

    /// <summary>
    /// Remove a service from the configuration and save.
    /// </summary>
    public async Task<(bool success, string? error)> RemoveServiceAsync(string name)
    {
        var service = Config.Services.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (service == null)
        {
            return (false, $"Service '{name}' not found");
        }

        Config.Services.Remove(service);
        await SaveAsync();
        return (true, null);
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
                    WorkingDirectory = "."
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

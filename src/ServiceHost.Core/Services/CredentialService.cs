using System.Text.Json;
using System.Text.RegularExpressions;
using ServiceHost.Models;

namespace ServiceHost.Services;

public sealed partial class CredentialService
{
    private readonly Dictionary<string, string> _fileCredentials = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _sessionCredentials = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ServiceCredentialStatus> _serviceStatuses = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    private DateTime _lastModified;

    public string? CredentialsPath { get; }
    public bool CredentialsLoaded { get; private set; }
    public CredentialStatus Status { get; private set; } = new();

    public CredentialService(string? credentialsPath)
    {
        CredentialsPath = string.IsNullOrWhiteSpace(credentialsPath) ? null : Path.GetFullPath(credentialsPath);
    }

    public bool HasCredentialsChanged()
    {
        if (string.IsNullOrWhiteSpace(CredentialsPath) || !File.Exists(CredentialsPath))
        {
            return CredentialsLoaded;
        }

        return File.GetLastWriteTimeUtc(CredentialsPath) > _lastModified;
    }

    public async Task LoadAsync(AppConfig config)
    {
        lock (_lock)
        {
            _fileCredentials.Clear();
            _serviceStatuses.Clear();
            CredentialsLoaded = false;
        }

        if (!string.IsNullOrWhiteSpace(CredentialsPath) && File.Exists(CredentialsPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(CredentialsPath);
                var values = JsonSerializer.Deserialize<Dictionary<string, string>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });

                if (values != null)
                {
                    foreach (var (key, value) in values)
                    {
                        if (!string.IsNullOrWhiteSpace(key))
                        {
                            lock (_lock) _fileCredentials[key] = value;
                        }
                    }
                }

                _lastModified = File.GetLastWriteTimeUtc(CredentialsPath);
                CredentialsLoaded = true;
            }
            catch
            {
                // Treat malformed/inaccessible credential files as unloaded. Never log values.
                CredentialsLoaded = false;
            }
        }

        RebuildStatus(config);
    }

    public void SetSessionCredentials(Dictionary<string, string> credentials, AppConfig config)
    {
        lock (_lock)
        {
            foreach (var (key, value) in credentials)
            {
                if (!string.IsNullOrWhiteSpace(key)) _sessionCredentials[key] = value;
            }
        }

        RebuildStatus(config);
    }

    public void ClearSessionCredentials(AppConfig config)
    {
        lock (_lock) _sessionCredentials.Clear();
        RebuildStatus(config);
    }

    public bool HasUnresolvedCredentials(string serviceName, out IReadOnlyList<string> unresolved)
    {
        if (_serviceStatuses.TryGetValue(serviceName, out var status) && status.Unresolved.Count > 0)
        {
            unresolved = status.Unresolved;
            return true;
        }

        unresolved = Array.Empty<string>();
        return false;
    }

    public ServiceConfig Resolve(ServiceConfig config)
    {
        return new ServiceConfig
        {
            Name = config.Name,
            Command = ResolveString(config.Command),
            Args = config.Args.Select(ResolveString).ToList(),
            WorkingDirectory = ResolveNullableString(config.WorkingDirectory),
            Port = config.Port,
            Url = ResolveNullableString(config.Url),
            Environment = config.Environment?.ToDictionary(kv => kv.Key, kv => ResolveString(kv.Value), StringComparer.OrdinalIgnoreCase),
            ShutdownTimeoutSeconds = config.ShutdownTimeoutSeconds
        };
    }

    private void RebuildStatus(AppConfig config)
    {
        var allRequired = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var serviceStatuses = new List<ServiceCredentialStatus>();

        foreach (var service in config.Services)
        {
            var required = FindPlaceholders(service).ToList();
            foreach (var name in required) allRequired.Add(name);

            var unresolved = required.Where(name => !HasCredential(name)).ToList();
            var serviceStatus = new ServiceCredentialStatus
            {
                Name = service.Name,
                Required = required,
                Unresolved = unresolved
            };
            serviceStatuses.Add(serviceStatus);
            _serviceStatuses[service.Name] = serviceStatus;
        }

        var requiredList = allRequired
            .Select(name => new CredentialRequirement { Name = name, Resolved = HasCredential(name) })
            .ToList();

        Status = new CredentialStatus
        {
            CredentialsPath = CredentialsPath,
            CredentialsLoaded = CredentialsLoaded,
            SessionCredentialCount = GetSessionCredentialCount(),
            Required = requiredList,
            Unresolved = requiredList.Where(r => !r.Resolved).Select(r => r.Name).ToList(),
            Services = serviceStatuses
        };
    }

    private static IEnumerable<string> FindPlaceholders(ServiceConfig service)
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        AddPlaceholders(names, service.Command);
        foreach (var arg in service.Args) AddPlaceholders(names, arg);
        AddPlaceholders(names, service.WorkingDirectory);
        AddPlaceholders(names, service.Url);
        if (service.Environment != null)
        {
            foreach (var value in service.Environment.Values) AddPlaceholders(names, value);
        }
        return names;
    }

    private static void AddPlaceholders(ISet<string> names, string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        foreach (Match match in PlaceholderRegex().Matches(value))
        {
            names.Add(match.Groups[1].Value);
        }
    }

    private string ResolveString(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return PlaceholderRegex().Replace(value, match =>
            TryGetCredential(match.Groups[1].Value, out var credential)
                ? credential
                : match.Value);
    }

    private bool HasCredential(string name) => TryGetCredential(name, out _);

    private bool TryGetCredential(string name, out string value)
    {
        lock (_lock)
        {
            if (_sessionCredentials.TryGetValue(name, out value!)) return true;
            return _fileCredentials.TryGetValue(name, out value!);
        }
    }

    private int GetSessionCredentialCount()
    {
        lock (_lock) return _sessionCredentials.Count;
    }

    private string? ResolveNullableString(string? value) => value == null ? null : ResolveString(value);

    [GeneratedRegex(@"\$\{([A-Za-z_][A-Za-z0-9_]*)\}")]
    private static partial Regex PlaceholderRegex();
}

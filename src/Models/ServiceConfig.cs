using System.Text.Json.Serialization;

namespace ServiceHost.Models;

public class AppConfig
{
    [JsonPropertyName("apiPort")]
    public int ApiPort { get; set; } = 9500;

    [JsonPropertyName("logDirectory")]
    public string LogDirectory { get; set; } = "./logs";

    [JsonPropertyName("services")]
    public List<ServiceConfig> Services { get; set; } = new();
}

public class ServiceConfig
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;

    [JsonPropertyName("args")]
    public List<string> Args { get; set; } = new();

    [JsonPropertyName("workingDirectory")]
    public string? WorkingDirectory { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("environment")]
    public Dictionary<string, string>? Environment { get; set; }

    [JsonPropertyName("shutdownTimeoutSeconds")]
    public int ShutdownTimeoutSeconds { get; set; } = 5;
}

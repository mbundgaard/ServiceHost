using System.Text.Json.Serialization;

namespace ServiceHost.Models;

public sealed class CredentialStatus
{
    [JsonPropertyName("credentialsPath")]
    public string? CredentialsPath { get; init; }

    [JsonPropertyName("credentialsLoaded")]
    public bool CredentialsLoaded { get; init; }

    [JsonPropertyName("sessionCredentialCount")]
    public int SessionCredentialCount { get; init; }

    [JsonPropertyName("allResolved")]
    public bool AllResolved => Unresolved.Count == 0;

    [JsonPropertyName("required")]
    public List<CredentialRequirement> Required { get; init; } = new();

    [JsonPropertyName("unresolved")]
    public List<string> Unresolved { get; init; } = new();

    [JsonPropertyName("services")]
    public List<ServiceCredentialStatus> Services { get; init; } = new();
}

public sealed class CredentialRequirement
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("resolved")]
    public bool Resolved { get; init; }
}

public sealed class ServiceCredentialStatus
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("allResolved")]
    public bool AllResolved => Unresolved.Count == 0;

    [JsonPropertyName("required")]
    public List<string> Required { get; init; } = new();

    [JsonPropertyName("unresolved")]
    public List<string> Unresolved { get; init; } = new();
}

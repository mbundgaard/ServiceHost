using System.IO;

namespace ServiceHost;

public sealed class ServiceHostOptions
{
    public string? ConfigPath { get; init; }
    public string? CredentialsPath { get; init; }
    public int? ApiPort { get; init; }

    public static ServiceHostOptions FromArgs(string[] args)
    {
        string? configPath = null;
        string? credentialsPath = null;
        int? apiPort = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Equals("--config", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                configPath = args[++i];
            }
            else if (arg.StartsWith("--config=", StringComparison.OrdinalIgnoreCase))
            {
                configPath = arg.Substring("--config=".Length);
            }
            else if (arg.Equals("--credentials", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                credentialsPath = args[++i];
            }
            else if (arg.StartsWith("--credentials=", StringComparison.OrdinalIgnoreCase))
            {
                credentialsPath = arg.Substring("--credentials=".Length);
            }
            else if (arg.Equals("--port", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], out var port)) apiPort = port;
            }
            else if (arg.StartsWith("--port=", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(arg.Substring("--port=".Length), out var port)) apiPort = port;
            }
        }

        configPath ??= Environment.GetEnvironmentVariable("SERVICEHOST_CONFIG");
        credentialsPath ??= Environment.GetEnvironmentVariable("SERVICEHOST_CREDENTIALS");

        if (!apiPort.HasValue
            && int.TryParse(Environment.GetEnvironmentVariable("SERVICEHOST_PORT"), out var envPort))
        {
            apiPort = envPort;
        }

        if (string.IsNullOrWhiteSpace(configPath))
        {
            var cwdConfig = Path.Combine(Environment.CurrentDirectory, "ServiceHost.json");
            if (File.Exists(cwdConfig)) configPath = cwdConfig;
        }

        if (!string.IsNullOrWhiteSpace(configPath) && !Path.IsPathRooted(configPath))
        {
            configPath = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, configPath));
        }

        if (string.IsNullOrWhiteSpace(credentialsPath) && !string.IsNullOrWhiteSpace(configPath))
        {
            var configDir = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrWhiteSpace(configDir))
            {
                var projectCredentials = Path.Combine(configDir, "ServiceHost.credentials.json");
                if (File.Exists(projectCredentials)) credentialsPath = projectCredentials;
            }
        }

        if (!string.IsNullOrWhiteSpace(credentialsPath) && !Path.IsPathRooted(credentialsPath))
        {
            credentialsPath = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, credentialsPath));
        }

        return new ServiceHostOptions
        {
            ConfigPath = configPath,
            CredentialsPath = credentialsPath,
            ApiPort = apiPort
        };
    }
}

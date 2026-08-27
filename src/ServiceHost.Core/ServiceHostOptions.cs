using System.IO;

namespace ServiceHost;

public sealed class ServiceHostOptions
{
    public string? ConfigPath { get; init; }
    public int? ApiPort { get; init; }

    public static ServiceHostOptions FromArgs(string[] args)
    {
        string? configPath = null;
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

        return new ServiceHostOptions
        {
            ConfigPath = configPath,
            ApiPort = apiPort
        };
    }
}

using System.Collections.Concurrent;
using System.IO;
using System.Text;

namespace ServiceHost.Services;

public class LogManager : IDisposable
{
    private readonly string _logDirectory;
    private readonly ConcurrentDictionary<string, StreamWriter> _writers = new();
    private readonly ConcurrentDictionary<string, StringBuilder> _buffers = new();
    private readonly object _lock = new();
    private bool _disposed;

    public event Action<string, string>? LogLineReceived;

    public LogManager(string logDirectory)
    {
        _logDirectory = logDirectory;
        Directory.CreateDirectory(_logDirectory);
    }

    /// <summary>
    /// Load existing log file content into buffer (for reconnecting to running services)
    /// </summary>
    public void LoadExistingLog(string serviceName)
    {
        lock (_lock)
        {
            var logPath = GetLogPath(serviceName);
            var buffer = new StringBuilder();

            if (File.Exists(logPath))
            {
                try
                {
                    var content = File.ReadAllText(logPath);
                    buffer.Append(content);
                }
                catch
                {
                    // Ignore read errors
                }
            }

            _buffers[serviceName] = buffer;

            // Open writer in append mode
            try
            {
                var writer = new StreamWriter(logPath, append: true, encoding: Encoding.UTF8)
                {
                    AutoFlush = true
                };
                _writers[serviceName] = writer;
            }
            catch
            {
                // Ignore if we can't open for writing
            }
        }
    }

    public void ResetLog(string serviceName)
    {
        lock (_lock)
        {
            CloseWriter(serviceName);

            var logPath = GetLogPath(serviceName);

            // Truncate the file
            File.WriteAllText(logPath, string.Empty);

            // Create new writer
            var writer = new StreamWriter(logPath, append: true, encoding: Encoding.UTF8)
            {
                AutoFlush = true
            };
            _writers[serviceName] = writer;

            // Reset buffer
            _buffers[serviceName] = new StringBuilder();
        }
    }

    public void WriteLine(string serviceName, string line)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var formattedLine = $"[{timestamp}] {line}";

        lock (_lock)
        {
            if (_writers.TryGetValue(serviceName, out var writer))
            {
                try
                {
                    writer.WriteLine(formattedLine);
                }
                catch
                {
                    // Ignore write errors
                }
            }

            if (_buffers.TryGetValue(serviceName, out var buffer))
            {
                buffer.AppendLine(formattedLine);

                // Limit buffer size to ~1MB
                if (buffer.Length > 1_000_000)
                {
                    var text = buffer.ToString();
                    buffer.Clear();
                    buffer.Append(text.Substring(text.Length / 2));
                }
            }
        }

        LogLineReceived?.Invoke(serviceName, formattedLine);
    }

    public string GetLogContent(string serviceName)
    {
        lock (_lock)
        {
            if (_buffers.TryGetValue(serviceName, out var buffer))
            {
                return buffer.ToString();
            }
        }

        // Try to read from file if buffer not available
        var logPath = GetLogPath(serviceName);
        if (File.Exists(logPath))
        {
            try
            {
                return File.ReadAllText(logPath);
            }
            catch
            {
                return string.Empty;
            }
        }

        return string.Empty;
    }

    public string GetLogPath(string serviceName)
    {
        return Path.Combine(_logDirectory, $"{serviceName}.log");
    }

    public void CloseLog(string serviceName)
    {
        lock (_lock)
        {
            CloseWriter(serviceName);
        }
    }

    private void CloseWriter(string serviceName)
    {
        if (_writers.TryRemove(serviceName, out var writer))
        {
            try
            {
                writer.Flush();
                writer.Dispose();
            }
            catch
            {
                // Ignore close errors
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_lock)
        {
            foreach (var serviceName in _writers.Keys.ToList())
            {
                CloseWriter(serviceName);
            }
        }
    }
}

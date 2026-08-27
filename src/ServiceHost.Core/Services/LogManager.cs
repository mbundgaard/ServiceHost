using System.Collections.Concurrent;
using System.IO;
using System.Text;

namespace ServiceHost.Services;

public class LogManager : IDisposable
{
    private readonly string _logDirectory;
    private readonly ConcurrentDictionary<string, LogEntry> _entries = new();
    private readonly Timer _flushTimer;
    private bool _disposed;

    public event Action<string, string>? LogLineReceived;

    private sealed class LogEntry
    {
        public StreamWriter? Writer;
        public readonly StringBuilder Buffer = new();
        public readonly object Lock = new();
    }

    public LogManager(string logDirectory)
    {
        _logDirectory = logDirectory;
        Directory.CreateDirectory(_logDirectory);
        _flushTimer = new Timer(FlushAll, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    private void FlushAll(object? state)
    {
        foreach (var entry in _entries.Values)
        {
            lock (entry.Lock)
            {
                try { entry.Writer?.Flush(); }
                catch { }
            }
        }
    }

    /// <summary>
    /// Load existing log file content into buffer (for reconnecting to running services)
    /// </summary>
    public void LoadExistingLog(string serviceName)
    {
        var entry = _entries.GetOrAdd(serviceName, _ => new LogEntry());
        lock (entry.Lock)
        {
            CloseWriter(entry);

            var logPath = GetLogPath(serviceName);
            entry.Buffer.Clear();

            if (File.Exists(logPath))
            {
                try
                {
                    var content = File.ReadAllText(logPath);
                    entry.Buffer.Append(content);
                }
                catch
                {
                    // Ignore read errors
                }
            }

            entry.Writer = TryOpenWriter(logPath);
        }
    }

    public void ResetLog(string serviceName)
    {
        var entry = _entries.GetOrAdd(serviceName, _ => new LogEntry());
        lock (entry.Lock)
        {
            CloseWriter(entry);

            var logPath = GetLogPath(serviceName);
            File.WriteAllText(logPath, string.Empty);

            entry.Writer = TryOpenWriter(logPath);
            entry.Buffer.Clear();
        }
    }

    public void WriteLine(string serviceName, string line)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var formattedLine = $"[{timestamp}] {line}";

        if (_entries.TryGetValue(serviceName, out var entry))
        {
            lock (entry.Lock)
            {
                if (entry.Writer != null)
                {
                    try { entry.Writer.WriteLine(formattedLine); }
                    catch { }
                }

                entry.Buffer.AppendLine(formattedLine);

                // Limit buffer size to ~1MB
                if (entry.Buffer.Length > 1_000_000)
                {
                    var text = entry.Buffer.ToString();
                    entry.Buffer.Clear();
                    entry.Buffer.Append(text.Substring(text.Length / 2));
                }
            }
        }

        LogLineReceived?.Invoke(serviceName, formattedLine);
    }

    public string GetLogContent(string serviceName)
    {
        if (_entries.TryGetValue(serviceName, out var entry))
        {
            lock (entry.Lock)
            {
                return entry.Buffer.ToString();
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
        if (_entries.TryGetValue(serviceName, out var entry))
        {
            lock (entry.Lock)
            {
                CloseWriter(entry);
            }
        }
    }

    private static StreamWriter? TryOpenWriter(string logPath)
    {
        try
        {
            // AutoFlush off: a background timer flushes periodically.
            // Per-line flushing was causing severe disk contention with chatty services.
            return new StreamWriter(logPath, append: true, encoding: Encoding.UTF8);
        }
        catch
        {
            return null;
        }
    }

    private static void CloseWriter(LogEntry entry)
    {
        if (entry.Writer == null) return;
        try
        {
            entry.Writer.Flush();
            entry.Writer.Dispose();
        }
        catch
        {
            // Ignore close errors
        }
        entry.Writer = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _flushTimer.Dispose();

        foreach (var entry in _entries.Values)
        {
            lock (entry.Lock)
            {
                CloseWriter(entry);
            }
        }
        _entries.Clear();
    }
}

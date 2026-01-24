using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace ServiceHost.Services;

public class ReadinessChecker
{
    private readonly int? _port;
    private readonly Regex? _pattern;
    private readonly TimeSpan _timeout;
    private readonly TaskCompletionSource<bool> _readyTcs = new();
    private bool _isReady;

    public bool IsReady => _isReady;

    public ReadinessChecker(int? port, string? readyPattern, int timeoutSeconds = 30)
    {
        _port = port;
        _pattern = !string.IsNullOrEmpty(readyPattern) ? new Regex(readyPattern, RegexOptions.Compiled) : null;
        _timeout = TimeSpan.FromSeconds(timeoutSeconds);
    }

    public void CheckLine(string line)
    {
        if (_isReady || _pattern == null) return;

        if (_pattern.IsMatch(line))
        {
            _isReady = true;
            _readyTcs.TrySetResult(true);
        }
    }

    public async Task<(bool success, string? error)> WaitForReadyAsync(CancellationToken cancellationToken = default)
    {
        // If no readiness check configured, consider it ready immediately
        if (_port == null && _pattern == null)
        {
            _isReady = true;
            return (true, null);
        }

        using var timeoutCts = new CancellationTokenSource(_timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            if (_port != null)
            {
                // Port-based readiness check
                return await WaitForPortAsync(_port.Value, linkedCts.Token);
            }
            else if (_pattern != null)
            {
                // Pattern-based readiness check - wait for CheckLine to set ready
                var readyTask = _readyTcs.Task;
                var completedTask = await Task.WhenAny(readyTask, Task.Delay(Timeout.Infinite, linkedCts.Token));

                if (completedTask == readyTask && await readyTask)
                {
                    return (true, null);
                }
            }
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            var checkType = _port != null ? $"port {_port}" : $"pattern '{_pattern}'";
            return (false, $"Timeout waiting for {checkType}");
        }
        catch (OperationCanceledException)
        {
            return (false, "Operation cancelled");
        }

        return (false, "Unknown error");
    }

    private async Task<(bool success, string? error)> WaitForPortAsync(int port, CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromMilliseconds(100);
        var maxDelay = TimeSpan.FromSeconds(2);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync("127.0.0.1", port);
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

                var completedTask = await Task.WhenAny(connectTask, timeoutTask);

                if (completedTask == connectTask && client.Connected)
                {
                    _isReady = true;
                    _readyTcs.TrySetResult(true);
                    return (true, null);
                }
            }
            catch (SocketException)
            {
                // Port not ready yet
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Ignore other errors and retry
            }

            await Task.Delay(delay, cancellationToken);

            // Exponential backoff
            delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 1.5, maxDelay.TotalMilliseconds));
        }

        return (false, $"Timeout waiting for port {port}");
    }

    public void Cancel()
    {
        _readyTcs.TrySetResult(false);
    }
}

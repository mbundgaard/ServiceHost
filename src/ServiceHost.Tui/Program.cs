using System.Text;
using ServiceHost;
using ServiceHost.Models;

if (ServiceHostRuntime.TryApplyPendingUpdate())
{
    return;
}

ServiceHostRuntime runtime;
try
{
    runtime = await ServiceHostRuntime.StartAsync(ServiceHostOptions.FromArgs(args));
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Failed to start ServiceHost: {ex.Message}");
    return;
}

Console.OutputEncoding = Encoding.UTF8;
Console.CursorVisible = false;

var services = runtime.ProcessManager.Services;
var serviceNames = services.Keys.ToList();
var selectedIndex = 0;
var version = (System.Reflection.Assembly.GetEntryAssembly() ?? System.Reflection.Assembly.GetExecutingAssembly()).GetName().Version;
var versionText = version != null ? $"v{version.Major}" : "v0";
var dirty = true;
var running = true;

runtime.LogManager.LogLineReceived += (_, _) => dirty = true;
runtime.ProcessManager.StatusChanged += (_, _) => dirty = true;
runtime.ProcessManager.ServiceAdded += (_, _) => dirty = true;
runtime.ProcessManager.ServiceRemoved += _ => dirty = true;
runtime.ShutdownRequested += () => running = false;

try
{
    while (running)
    {
        if (dirty)
        {
            Render();
            dirty = false;
        }

        if (Console.KeyAvailable)
        {
            var key = Console.ReadKey(intercept: true);
            HandleKey(key);
            dirty = true;
        }

        await Task.Delay(100);
    }
}
finally
{
    Console.ResetColor();
    Console.CursorVisible = true;
    Console.Clear();
    await runtime.StopAsync();
}

void Render()
{
    serviceNames = services.Keys.ToList();
    if (selectedIndex >= serviceNames.Count) selectedIndex = Math.Max(0, serviceNames.Count - 1);

    var width = Math.Max(40, Console.WindowWidth);
    var height = Math.Max(12, Console.WindowHeight);

    Console.SetCursorPosition(0, 0);
    Console.Write(BuildScreen(width, height));
}

string BuildScreen(int width, int height)
{
    var sb = new StringBuilder(width * height);
    var footerSeparatorY = height - 2;
    var footerY = height - 1;

    AppendLine(sb, Fit("Services", width));

    var maxServiceRows = Math.Max(1, Math.Min(serviceNames.Count, height / 3));
    var firstServiceIndex = Math.Max(0, selectedIndex - maxServiceRows + 1);
    if (firstServiceIndex + maxServiceRows > serviceNames.Count)
    {
        firstServiceIndex = Math.Max(0, serviceNames.Count - maxServiceRows);
    }

    for (var row = 0; row < maxServiceRows; row++)
    {
        var index = firstServiceIndex + row;
        if (index < serviceNames.Count)
        {
            AppendLine(sb, Fit(ServiceRow(index), width));
        }
    }

    if (serviceNames.Count > maxServiceRows)
    {
        AppendLine(sb, Fit($"  ... {serviceNames.Count - maxServiceRows} more; F2 cycles services", width));
    }

    AppendLine(sb, Rule(width));

    var usedRows = 2 + maxServiceRows + (serviceNames.Count > maxServiceRows ? 1 : 0);
    var selectedService = GetSelectedServiceName();
    var logLines = GetLogLines(selectedService).ToList();
    var logHeight = Math.Max(0, footerSeparatorY - usedRows);
    var visibleLogs = logLines.Skip(Math.Max(0, logLines.Count - logHeight)).ToList();

    for (var i = 0; i < logHeight; i++)
    {
        var line = i < visibleLogs.Count ? visibleLogs[i] : string.Empty;
        AppendLine(sb, Fit(line, width));
    }

    AppendLine(sb, Rule(width));
    sb.Append(FitFooter("F1 info  F2 next service  F3 start/stop  F4 restart  F5 start/stop all  ^L clear log  ^Q quit", versionText, width));

    return sb.ToString();
}

string ServiceRow(int index)
{
    var name = serviceNames[index];
    var state = services[name];
    var selected = index == selectedIndex ? ">" : " ";
    var port = state.Config.Port > 0 ? $" :{state.Config.Port}" : string.Empty;
    var pid = state.ProcessId.HasValue ? $" pid:{state.ProcessId}" : string.Empty;
    var url = !string.IsNullOrWhiteSpace(state.Config.Url) ? $" {state.Config.Url}" : string.Empty;
    var creds = runtime.CredentialService.HasUnresolvedCredentials(name, out var unresolved)
        ? $" creds:{unresolved.Count}!"
        : string.Empty;
    var error = state.Status == ServiceStatus.Failed && !string.IsNullOrWhiteSpace(state.LastError)
        ? $" {state.LastError}"
        : string.Empty;

    return $"{selected} {name,-18} {StatusText(state.Status),-8} {port}{pid}{url}{creds}{error}";
}

IEnumerable<string> GetLogLines(string? serviceName)
{
    if (serviceName == null)
    {
        yield return "No service selected.";
        yield break;
    }

    var content = runtime.LogManager.GetLogContent(serviceName);
    if (string.IsNullOrWhiteSpace(content)) yield break;

    foreach (var rawLine in content.Replace("\r\n", "\n").Split('\n'))
    {
        if (string.IsNullOrEmpty(rawLine)) continue;
        yield return rawLine;
    }
}

void HandleKey(ConsoleKeyInfo key)
{
    switch (key.Key)
    {
        case ConsoleKey.F1:
            ShowInfo();
            break;
        case ConsoleKey.F2:
            NextService();
            break;
        case ConsoleKey.UpArrow:
            selectedIndex = Math.Max(0, selectedIndex - 1);
            break;
        case ConsoleKey.DownArrow:
            selectedIndex = Math.Min(Math.Max(0, serviceNames.Count - 1), selectedIndex + 1);
            break;
        case ConsoleKey.F3:
            ToggleSelected();
            break;
        case ConsoleKey.F4:
            RunSelected(runtime.ProcessManager.RestartServiceAsync);
            break;
        case ConsoleKey.F5:
            ToggleAll();
            break;
        case ConsoleKey.L when key.Modifiers.HasFlag(ConsoleModifiers.Control):
            ClearSelectedLog();
            break;
        case ConsoleKey.Q when key.Modifiers.HasFlag(ConsoleModifiers.Control):
            running = false;
            break;
    }
}

void ShowInfo()
{
    serviceNames = services.Keys.ToList();
    var selectedService = GetSelectedServiceName();
    var width = Math.Max(40, Console.WindowWidth);
    var height = Math.Max(12, Console.WindowHeight);
    var lines = BuildInfoLines(selectedService).ToList();

    Console.Clear();
    for (var i = 0; i < Math.Min(height - 1, lines.Count); i++)
    {
        Console.SetCursorPosition(0, i);
        Console.Write(Fit(lines[i], width));
    }
    Console.SetCursorPosition(0, height - 1);
    Console.Write(Fit("Press any key to close", width));
    Console.ReadKey(intercept: true);
    dirty = true;
}

IEnumerable<string> BuildInfoLines(string? selectedService)
{
    var runningCount = services.Values.Count(s => s.Status == ServiceStatus.Running);
    var total = services.Count;
    var credentialStatus = runtime.CredentialService.Status;
    var credSummary = credentialStatus.Required.Count == 0
        ? "none"
        : credentialStatus.AllResolved
            ? "ok"
            : $"missing {credentialStatus.Unresolved.Count}";

    yield return $"ServiceHost {versionText}";
    yield return Rule(60);
    yield return $"API            http://localhost:{runtime.ApiPort}/";
    yield return $"Services       {runningCount}/{total} services running";
    yield return $"Config         {runtime.ConfigurationService.ConfigPath}";
    yield return $"Project        {runtime.ProjectDirectory}";
    yield return $"Credentials    {credSummary}";
    yield return $"Cred file      {(credentialStatus.CredentialsPath ?? "none")}";
    yield return $"Session creds  {credentialStatus.SessionCredentialCount}";

    if (credentialStatus.Unresolved.Count > 0)
    {
        yield return string.Empty;
        yield return "Unresolved credentials";
        foreach (var name in credentialStatus.Unresolved) yield return $"- {name}";
    }

    yield return string.Empty;
    yield return "Service groups";
    yield return $"Running        {NamesWithStatus(ServiceStatus.Running)}";
    yield return $"Stopped        {NamesWithStatus(ServiceStatus.Stopped)}";
    yield return $"Failed         {NamesWithStatus(ServiceStatus.Failed)}";

    if (selectedService != null && services.TryGetValue(selectedService, out var state))
    {
        yield return string.Empty;
        yield return "Selected service";
        yield return $"Name           {selectedService}";
        yield return $"Status         {state.Status}";
        yield return $"Port           {(state.Config.Port > 0 ? state.Config.Port.ToString() : "none")}";
        yield return $"PID            {(state.ProcessId?.ToString() ?? "none")}";
        yield return $"Command        {state.Config.Command} {string.Join(" ", state.Config.Args)}";
        yield return $"Working dir    {state.Config.WorkingDirectory ?? "none"}";
        yield return $"URL            {state.Config.Url ?? "none"}";

        var serviceCreds = credentialStatus.Services.FirstOrDefault(s => s.Name.Equals(selectedService, StringComparison.OrdinalIgnoreCase));
        yield return $"Credentials    {(serviceCreds == null || serviceCreds.Required.Count == 0 ? "none" : serviceCreds.AllResolved ? "ok" : $"missing {serviceCreds.Unresolved.Count}")}";
        if (serviceCreds?.Unresolved.Count > 0)
        {
            yield return $"Missing        {string.Join(", ", serviceCreds.Unresolved)}";
        }
    }
}

string NamesWithStatus(ServiceStatus status)
{
    var names = services.Values
        .Where(s => s.Status == status)
        .Select(s => s.Config.Name)
        .ToList();
    return names.Count == 0 ? "none" : string.Join(", ", names);
}

void NextService()
{
    if (serviceNames.Count == 0) return;
    selectedIndex = (selectedIndex + 1) % serviceNames.Count;
}

void RunSelected(Func<string, CancellationToken, Task<(bool success, string? error)>> action)
{
    var name = GetSelectedServiceName();
    if (name == null) return;

    _ = Task.Run(async () =>
    {
        await action(name, CancellationToken.None);
        dirty = true;
    });
}

void ToggleSelected()
{
    var name = GetSelectedServiceName();
    if (name == null || !services.TryGetValue(name, out var state)) return;

    Func<string, CancellationToken, Task<(bool success, string? error)>> action =
        state.Status == ServiceStatus.Running || state.Status == ServiceStatus.Starting
            ? runtime.ProcessManager.StopServiceAsync
            : runtime.ProcessManager.StartServiceAsync;
    RunSelected(action);
}

void ToggleAll()
{
    var anyRunning = services.Values.Any(s => s.Status == ServiceStatus.Running || s.Status == ServiceStatus.Starting);
    _ = Task.Run(async () =>
    {
        if (anyRunning)
        {
            await runtime.ProcessManager.StopAllServicesAsync();
        }
        else
        {
            await Task.WhenAll(services.Keys.Select(name => runtime.ProcessManager.StartServiceAsync(name)));
        }
        dirty = true;
    });
}

void ClearSelectedLog()
{
    var name = GetSelectedServiceName();
    if (name == null) return;
    runtime.LogManager.ResetLog(name);
}

string? GetSelectedServiceName()
{
    if (selectedIndex < 0 || selectedIndex >= serviceNames.Count) return null;
    return serviceNames[selectedIndex];
}

static string StatusText(ServiceStatus status) => status switch
{
    ServiceStatus.Running => "running",
    ServiceStatus.Starting => "starting",
    ServiceStatus.Stopping => "stopping",
    ServiceStatus.Failed => "failed",
    _ => "stopped"
};

static string Rule(int width) => new('─', Math.Max(0, width));

static string Fit(string text, int width)
{
    if (text.Length > width) return text.Substring(0, width);
    return text.PadRight(width);
}

static string FitFooter(string text, string version, int width)
{
    if (width <= version.Length + 1) return Fit(version, width);
    var leftWidth = width - version.Length;
    return Fit(text, leftWidth) + version;
}

static void AppendLine(StringBuilder sb, string line)
{
    sb.Append(line);
    sb.Append('\n');
}

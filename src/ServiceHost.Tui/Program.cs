using NStack;
using ServiceHost;
using ServiceHost.Models;
using Terminal.Gui;

if (ServiceHostRuntime.TryApplyPendingUpdate())
{
    return;
}

ServiceHostRuntime runtime;
try
{
    runtime = await ServiceHostRuntime.StartAsync();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Failed to start ServiceHost: {ex.Message}");
    return;
}

Application.Init();

var top = Application.Top;
var services = runtime.ProcessManager.Services;
var serviceNames = services.Keys.ToList();
var selectedServiceName = serviceNames.FirstOrDefault();

var header = new Label($" ServiceHost TUI  API: http://localhost:{runtime.ConfigurationService.Config.ApiPort}/  Config: {runtime.ConfigurationService.ConfigPath}")
{
    X = 0,
    Y = 0,
    Width = Dim.Fill(),
    Height = 1
};

var serviceList = new ListView(serviceNames)
{
    X = 0,
    Y = 1,
    Width = 34,
    Height = Dim.Fill(1)
};

var logView = new TextView
{
    X = Pos.Right(serviceList),
    Y = 1,
    Width = Dim.Fill(),
    Height = Dim.Fill(1),
    ReadOnly = true,
    WordWrap = false
};

var status = new StatusBar(new[]
{
    new StatusItem(Key.F5, "F5 Refresh", Refresh),
    new StatusItem(Key.F2, "F2 Start", () => RunSelected(runtime.ProcessManager.StartServiceAsync)),
    new StatusItem(Key.F3, "F3 Stop", () => RunSelected(runtime.ProcessManager.StopServiceAsync)),
    new StatusItem(Key.F4, "F4 Restart", () => RunSelected(runtime.ProcessManager.RestartServiceAsync)),
    new StatusItem(Key.F6, "F6 Start All", StartAll),
    new StatusItem(Key.F7, "F7 Stop All", StopAll),
    new StatusItem(Key.F8, "F8 Clear Log", ClearSelectedLog),
    new StatusItem(Key.CtrlMask | Key.Q, "^Q Quit", Quit)
});

top.Add(header, serviceList, logView, status);

serviceList.SelectedItemChanged += _ =>
{
    selectedServiceName = GetSelectedServiceName();
    RefreshLog();
};

runtime.LogManager.LogLineReceived += OnLogLineReceived;
runtime.ProcessManager.StatusChanged += (_, _) => Application.MainLoop.Invoke(Refresh);
runtime.ProcessManager.ServiceAdded += (_, _) => Application.MainLoop.Invoke(Refresh);
runtime.ProcessManager.ServiceRemoved += _ => Application.MainLoop.Invoke(Refresh);
runtime.ShutdownRequested += () => Application.MainLoop.Invoke(Quit);

Refresh();
Application.Run();
Application.Shutdown();

runtime.LogManager.LogLineReceived -= OnLogLineReceived;
await runtime.StopAsync();

void Refresh()
{
    serviceNames = services.Keys.ToList();
    if (selectedServiceName == null || !services.ContainsKey(selectedServiceName))
    {
        selectedServiceName = serviceNames.FirstOrDefault();
    }

    var rows = serviceNames.Select(name =>
    {
        var state = services[name];
        var pid = state.ProcessId.HasValue ? $" pid:{state.ProcessId}" : string.Empty;
        return $"{StatusGlyph(state.Status)} {name,-20} {state.Status}{pid}";
    }).ToList();

    serviceList.SetSource(rows);
    RefreshLog();
    Application.Refresh();
}

void RefreshLog()
{
    selectedServiceName = GetSelectedServiceName() ?? selectedServiceName;
    var text = selectedServiceName == null
        ? "No service selected."
        : runtime.LogManager.GetLogContent(selectedServiceName);

    logView.Text = ustring.Make(text);
    logView.MoveEnd();
}

string? GetSelectedServiceName()
{
    if (serviceList.SelectedItem < 0 || serviceList.SelectedItem >= serviceNames.Count)
    {
        return null;
    }

    return serviceNames[serviceList.SelectedItem];
}

void OnLogLineReceived(string serviceName, string line)
{
    if (serviceName != selectedServiceName) return;
    Application.MainLoop.Invoke(RefreshLog);
}

void RunSelected(Func<string, CancellationToken, Task<(bool success, string? error)>> action)
{
    var name = GetSelectedServiceName();
    if (name == null) return;

    _ = Task.Run(async () =>
    {
        await action(name, CancellationToken.None);
        Application.MainLoop.Invoke(Refresh);
    });
}

void StartAll()
{
    _ = Task.Run(async () =>
    {
        await Task.WhenAll(services.Keys.Select(name => runtime.ProcessManager.StartServiceAsync(name)));
        Application.MainLoop.Invoke(Refresh);
    });
}

void StopAll()
{
    _ = Task.Run(async () =>
    {
        await runtime.ProcessManager.StopAllServicesAsync();
        Application.MainLoop.Invoke(Refresh);
    });
}

void ClearSelectedLog()
{
    var name = GetSelectedServiceName();
    if (name == null) return;
    runtime.LogManager.ResetLog(name);
    RefreshLog();
}

void Quit()
{
    Application.RequestStop();
}

static string StatusGlyph(ServiceStatus status) => status switch
{
    ServiceStatus.Running => "●",
    ServiceStatus.Starting => "◐",
    ServiceStatus.Stopping => "◑",
    ServiceStatus.Failed => "✖",
    _ => "○"
};

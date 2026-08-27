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
    runtime = await ServiceHostRuntime.StartAsync(ServiceHostOptions.FromArgs(args));
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
var version = (System.Reflection.Assembly.GetEntryAssembly() ?? System.Reflection.Assembly.GetExecutingAssembly()).GetName().Version;
var versionText = version != null ? $"v{version.Major}" : "v0";

var header = new Label
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
    Width = Dim.Fill(),
    Height = 8
};

var logTitle = new Label
{
    X = 0,
    Y = Pos.Bottom(serviceList),
    Width = Dim.Fill(),
    Height = 1
};

var logView = new TextView
{
    X = 0,
    Y = Pos.Bottom(logTitle),
    Width = Dim.Fill(),
    Height = Dim.Fill(1),
    ReadOnly = true,
    WordWrap = false
};

var versionLabel = new Label(versionText)
{
    X = Pos.AnchorEnd(versionText.Length + 1),
    Y = Pos.AnchorEnd(1),
    Width = versionText.Length,
    Height = 1
};

var status = new StatusBar(new[]
{
    new StatusItem(Key.F2, "F2 Start/Stop", ToggleSelected),
    new StatusItem(Key.F3, "F3 Restart", () => RunSelected(runtime.ProcessManager.RestartServiceAsync)),
    new StatusItem(Key.F4, "F4 All Start/Stop", ToggleAll),
    new StatusItem(Key.CtrlMask | Key.L, "^L Clear Log", ClearSelectedLog),
    new StatusItem(Key.CtrlMask | Key.Q, "^Q Quit", Quit)
});

top.Add(header, serviceList, logTitle, logView, status, versionLabel);

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

    ApplyLayout();

    var selectedIndex = selectedServiceName == null ? 0 : Math.Max(0, serviceNames.IndexOf(selectedServiceName));
    var rows = serviceNames.Select(CompactServiceRow).ToList();

    header.Text = ustring.Make(BuildHeader());
    versionLabel.Text = ustring.Make(versionText);
    serviceList.SetSource(rows);
    if (rows.Count > 0)
    {
        serviceList.SelectedItem = Math.Min(selectedIndex, rows.Count - 1);
    }

    RefreshLog();
    Application.Refresh();
}

void ApplyLayout()
{
    serviceList.X = 0;
    serviceList.Y = 1;
    serviceList.Width = Dim.Fill();
    serviceList.Height = Math.Min(10, Math.Max(5, services.Count + 2));

    logTitle.X = 0;
    logTitle.Y = Pos.Bottom(serviceList);
    logTitle.Width = Dim.Fill();
    logTitle.Height = 1;

    logView.X = 0;
    logView.Y = Pos.Bottom(logTitle);
    logView.Width = Dim.Fill();
    logView.Height = Dim.Fill(1);
}

void RefreshLog()
{
    selectedServiceName = GetSelectedServiceName() ?? selectedServiceName;

    if (selectedServiceName == null)
    {
        logTitle.Text = ustring.Make(" Logs — no service selected");
        logView.Text = ustring.Make("No service selected.");
        return;
    }

    var state = services[selectedServiceName];
    logTitle.Text = ustring.Make($" Logs — {selectedServiceName} [{state.Status}]  {state.Config.Command} {string.Join(" ", state.Config.Args)}");
    logView.Text = ustring.Make(runtime.LogManager.GetLogContent(selectedServiceName));
    logView.MoveEnd();
}

string BuildHeader()
{
    var running = services.Values.Count(s => s.Status == ServiceStatus.Running);
    var total = services.Count;
    var creds = runtime.CredentialService.Status.Required.Count == 0
        ? "creds:none"
        : runtime.CredentialService.Status.AllResolved
            ? "creds:ok"
            : $"creds:missing:{runtime.CredentialService.Status.Unresolved.Count}";
    return $" ServiceHost :{runtime.ApiPort}  {running}/{total} running  {creds}  {runtime.ConfigurationService.ConfigPath}";
}

string CompactServiceRow(string name)
{
    var state = services[name];
    var pid = state.ProcessId.HasValue ? $" pid:{state.ProcessId}" : string.Empty;
    var port = state.Config.Port > 0 ? $" :{state.Config.Port}" : string.Empty;
    var creds = runtime.CredentialService.HasUnresolvedCredentials(name, out var unresolved)
        ? $" creds:{unresolved.Count}!"
        : string.Empty;
    return $"{StatusGlyph(state.Status)} {name}  {state.Status}{port}{pid}{creds}";
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
    if (anyRunning)
    {
        _ = Task.Run(async () =>
        {
            await runtime.ProcessManager.StopAllServicesAsync();
            Application.MainLoop.Invoke(Refresh);
        });
    }
    else
    {
        _ = Task.Run(async () =>
        {
            await Task.WhenAll(services.Keys.Select(name => runtime.ProcessManager.StartServiceAsync(name)));
            Application.MainLoop.Invoke(Refresh);
        });
    }
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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using OspreyRelay.Core.Config;
using OspreyRelay.Core.Logging;
using OspreyRelay.App.Service;

// Elevated reinstall/uninstall triggered by the UI via runas
if (args.Contains("--installservice"))
{
    OspreyRelay.App.Services.WindowsServiceManager.Install(Environment.ProcessPath!);
    return;
}
if (args.Contains("--uninstallservice"))
{
    OspreyRelay.App.Services.WindowsServiceManager.Uninstall();
    return;
}

bool serviceMode = WindowsServiceHelpers.IsWindowsService() || args.Contains("--service");

if (serviceMode)
{
    var host = Host.CreateDefaultBuilder(args)
        .UseWindowsService(opts => opts.ServiceName = "365Relay")
        .ConfigureServices(services =>
        {
            services.AddSingleton<ConfigManager>();
            services.AddSingleton<RelayLogger>(_ => new RelayLogger(ConfigManager.GetLogPath()));
            services.AddHostedService<RelayHostedService>();
        })
        .Build();

    await host.RunAsync();
    return;
}

// ── GUI mode — single-instance guard ─────────────────────────────────────────
// If a GUI is already open, bring it to the foreground and exit this instance.
// Named mutex survives until the process exits (or explicitly released).
// When upgrading to named-pipe IPC, send an "activate" message over the pipe here
// instead of using FindWindow.
using var mutex = new Mutex(true, @"Global\365RelayGUI", out bool isFirstInstance);
if (!isFirstInstance)
{
    var hwnd = OspreyRelay.App.NativeMethods.FindWindow(null, "365 Email Relay");
    if (hwnd != IntPtr.Zero)
    {
        OspreyRelay.App.NativeMethods.ShowWindow(hwnd, OspreyRelay.App.NativeMethods.SW_RESTORE);
        OspreyRelay.App.NativeMethods.SetForegroundWindow(hwnd);
    }
    return;
}

ApplicationConfiguration.Initialize();
Application.Run(new OspreyRelay.App.Forms.MainForm());

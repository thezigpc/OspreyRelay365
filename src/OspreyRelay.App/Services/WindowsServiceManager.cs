using System.Diagnostics;
using System.Security.Principal;
using System.ServiceProcess;

namespace OspreyRelay.App.Services;

public static class WindowsServiceManager
{
    public const string ServiceName = "OspreyRelay365";
    private const string ServiceDisplay = "Osprey Relay for M365";
    private const string ServiceDescription =
        "Routes SMTP email from local devices through Microsoft 365 via the Graph API.";

    public static bool IsInstalled() =>
        ServiceController.GetServices().Any(s => s.ServiceName == ServiceName);

    public static ServiceControllerStatus? GetStatus()
    {
        try
        {
            using var sc = new ServiceController(ServiceName);
            return sc.Status;
        }
        catch { return null; }
    }

    public static bool IsAdministrator()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>Install the relay as a Windows Service. Relaunches elevated if needed.</summary>
    public static void Install(string serviceExePath)
    {
        if (!IsAdministrator())
        {
            RelaunchElevated("--installservice");
            return;
        }

        RunSc($"create \"{ServiceName}\" binPath= \"\"{serviceExePath}\" --service\" " +
              $"start= auto DisplayName= \"{ServiceDisplay}\"");
        RunSc($"description \"{ServiceName}\" \"{ServiceDescription}\"");
    }

    /// <summary>Remove the Windows Service. Relaunches elevated if needed.</summary>
    public static void Uninstall()
    {
        if (!IsAdministrator())
        {
            RelaunchElevated("--uninstallservice");
            return;
        }

        TryStop();
        RunSc($"delete \"{ServiceName}\"");
    }

    public static void TryStart()
    {
        using var sc = new ServiceController(ServiceName);
        if (sc.Status != ServiceControllerStatus.Running &&
            sc.Status != ServiceControllerStatus.StartPending)
        {
            sc.Start();
        }
    }

    public static void TryStop()
    {
        try
        {
            using var sc = new ServiceController(ServiceName);
            if (sc.Status == ServiceControllerStatus.Running ||
                sc.Status == ServiceControllerStatus.Paused)
            {
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
            }
        }
        catch { }
    }

    private static void RunSc(string args)
    {
        using var p = Process.Start(new ProcessStartInfo("sc.exe", args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        })!;

        p.WaitForExit();

        if (p.ExitCode != 0)
        {
            var err = p.StandardError.ReadToEnd().Trim();
            var out_ = p.StandardOutput.ReadToEnd().Trim();
            throw new InvalidOperationException(
                $"sc.exe failed (exit {p.ExitCode}): {err}{out_}");
        }
    }

    private static void RelaunchElevated(string arg)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = Application.ExecutablePath,
            Arguments = arg,
            Verb = "runas",
            UseShellExecute = true
        });
    }
}

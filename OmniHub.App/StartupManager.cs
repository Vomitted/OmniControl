using System.Diagnostics;

namespace OmniHub.App;

/// <summary>Manages an auto-start-at-logon Scheduled Task for OmniHub. A plain
/// HKCU...\Run registry entry doesn't reliably auto-elevate -- OmniHub needs
/// admin for BIOS access, so a Run-key launch either shows a UAC prompt every
/// login or silently fails to start, depending on the user's UAC settings.
/// Task Scheduler's "Run with highest privileges" flag is the standard,
/// documented way to auto-start an elevated app at logon without a prompt,
/// for a user account that is itself an administrator.</summary>
public static class StartupManager
{
    private const string TaskName = "OmniHub_AutoStart";

    public static bool IsEnabled() => RunSchTasks("/Query", "/TN", TaskName);

    public static bool SetEnabled(bool enabled)
    {
        if (!enabled) return RunSchTasks("/Delete", "/TN", TaskName, "/F");

        string exePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not determine the running executable's path.");
        return RunSchTasks("/Create", "/TN", TaskName, "/TR", exePath, "/SC", "ONLOGON", "/RL", "HIGHEST", "/F");
    }

    private static bool RunSchTasks(params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);

            using var proc = Process.Start(psi);
            if (proc is null) return false;
            proc.WaitForExit();
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}

using System.Diagnostics;
using System.Globalization;

namespace OmniHub.Core.Hardware;

/// <summary>One sample of discrete-GPU state. Fields the driver will not report come back null.</summary>
public sealed record GpuReading(
    string Name,
    double? TempC,
    double? PowerWatts,
    int? ClockMhz,
    int? UtilisationPercent);

/// <summary>
/// Discrete GPU telemetry, read through nvidia-smi.
///
/// The obvious alternative is NVAPI, and it is the "proper" answer -- but it means shipping
/// P/Invoke against an unversioned vendor DLL whose entry points are looked up by numeric
/// hash, for numbers nvidia-smi already prints. nvidia-smi installs with every NVIDIA driver,
/// lives in System32, and needs no privileges. For a readout refreshed every few seconds it is
/// the same data at a fraction of the surface area.
///
/// The cost that matters is process startup, measured at about 56 ms on this machine, so
/// results are cached and a caller cannot accidentally spawn one per frame.
///
/// ponytail: process spawn per refresh. Move to NVAPI only if something needs this faster than
/// once a second, which a temperature readout does not.
/// </summary>
public static class GpuTelemetry
{
    private static readonly string ExePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe");

    private static readonly TimeSpan CacheLife = TimeSpan.FromSeconds(3);
    private static readonly object Gate = new();
    private static GpuReading? _cached;
    private static DateTime _cachedAtUtc = DateTime.MinValue;

    /// <summary>True when an NVIDIA driver is installed and its query tool is present.</summary>
    public static bool IsAvailable => File.Exists(ExePath);

    /// <summary>
    /// Latest GPU reading, or null when there is no NVIDIA GPU or the query failed.
    ///
    /// Null is a normal answer, not an error: a machine on integrated graphics has no discrete
    /// GPU to report, and the caller should say "unavailable" rather than show a zero that
    /// looks like a stone-cold card.
    /// </summary>
    public static GpuReading? Read()
    {
        lock (Gate)
        {
            if (DateTime.UtcNow - _cachedAtUtc < CacheLife) return _cached;
            _cached = Query();
            _cachedAtUtc = DateTime.UtcNow;
            return _cached;
        }
    }

    private static GpuReading? Query()
    {
        if (!IsAvailable) return null;

        try
        {
            var psi = new ProcessStartInfo(ExePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("--query-gpu=name,temperature.gpu,power.draw,clocks.sm,utilization.gpu");
            psi.ArgumentList.Add("--format=csv,noheader,nounits");

            using var proc = Process.Start(psi);
            if (proc is null) return null;

            string output = proc.StandardOutput.ReadToEnd();

            // Bounded wait: a hung query must not stall the caller's poll loop forever.
            if (!proc.WaitForExit(3000)) { try { proc.Kill(true); } catch { } return null; }
            if (proc.ExitCode != 0) return null;

            // First line only. A machine with two NVIDIA GPUs reports both, and this readout
            // describes the one doing the work rather than trying to merge them.
            string? line = output.Split('\n').FirstOrDefault(l => l.Trim().Length > 0);
            if (line is null) return null;

            var parts = line.Split(',').Select(p => p.Trim()).ToArray();
            if (parts.Length < 5) return null;

            return new GpuReading(
                parts[0],
                Number(parts[1]),
                Number(parts[2]),
                (int?)Number(parts[3]),
                (int?)Number(parts[4]));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Parses a field, treating nvidia-smi's "[N/A]" placeholders as absent.</summary>
    private static double? Number(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : null;
}

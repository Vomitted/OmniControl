using System.Management;

namespace OmniHub.Core.Hardware;

public sealed record SystemPerf(double CpuClockGHz, double CpuLoadPercent, double MemoryUsedGB, double MemoryTotalGB);

/// <summary>
/// Real CPU/RAM telemetry via standard Windows WMI classes -- same "documented,
/// not reverse-engineered" principle as the ACPI thermal-zone and battery readers.
/// No GPU load/VRAM/clock here deliberately: that needs vendor-specific APIs
/// (NVML for NVIDIA) this app doesn't have wired up, and a plausible-looking
/// fabricated number is worse than admitting the data isn't available.
///
/// CpuClockGHz caveat: Win32_Processor.CurrentClockSpeed is not guaranteed to
/// track the CPU's real-time dynamic (Turbo Boost) frequency -- on many
/// systems/drivers it reports a static nominal value instead, and there is no
/// portable WMI counter that reliably distinguishes the two. This app reports
/// whatever the OS itself returns for this field, unmodified; it is not
/// independently cross-checked against a second source the way the temperature
/// reading is against ACPI thermal zones.
/// </summary>
public static class SystemPerfReader
{
    public static SystemPerf? Read()
    {
        try
        {
            double clockMHz = 0, loadPercent = 0;
            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PerfFormattedData_PerfOS_Processor WHERE Name='_Total'"))
                foreach (ManagementObject mo in searcher.Get())
                    using (mo) loadPercent = Convert.ToDouble(mo["PercentProcessorTime"] ?? 0.0);

            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor"))
                foreach (ManagementObject mo in searcher.Get())
                    using (mo) clockMHz = Convert.ToDouble(mo["CurrentClockSpeed"] ?? 0.0);

            double totalKB = 0, freeKB = 0;
            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem"))
            {
                foreach (ManagementObject mo in searcher.Get())
                {
                    using var _ = mo;
                    totalKB = Convert.ToDouble(mo["TotalVisibleMemorySize"] ?? 0.0);
                    freeKB = Convert.ToDouble(mo["FreePhysicalMemory"] ?? 0.0);
                }
            }

            double totalGB = totalKB / 1024.0 / 1024.0;
            double usedGB = (totalKB - freeKB) / 1024.0 / 1024.0;

            return new SystemPerf(clockMHz / 1000.0, loadPercent, usedGB, totalGB);
        }
        catch { return null; }
    }
}

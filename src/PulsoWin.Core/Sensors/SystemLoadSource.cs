// PulsoWin · MIT · Co-designed with AI coding assistants (Claude & Gemini)
using System.Runtime.InteropServices;

namespace PulsoWin.Core.Sensors;

/// <summary>Uso de CPU y RAM con APIs de Win32 (sin administrador). El cálculo de CPU es el de <see cref="Metrics.CpuSampler"/>.</summary>
public sealed class SystemLoadSource : ISensorSource
{
    private readonly Metrics.CpuSampler _cpu = new();

    public string Name => Localization.L.T("Sensor_System");

    public IReadOnlyList<SensorReading> Read()
    {
        var cpu = _cpu.Sample();
        var ram = MemoryInfo.Current();
        return
        [
            new SensorReading(Name, Localization.L.F("Sensor_Usage", "CPU"), cpu, "%",
                cpu is null ? Localization.L.T("Sensor_CpuSecondReading") : null),
            new SensorReading(Name, Localization.L.T("Sensor_RamInUse"), ram?.LoadPercent, "%",
                ram is { } m ? Localization.L.F("Common_XofY", Common.Bytes.Format((long)(m.TotalBytes - m.AvailableBytes)), Common.Bytes.Format((long)m.TotalBytes)) : null),
        ];
    }

}

public readonly record struct MemoryInfo(ulong TotalBytes, ulong AvailableBytes, double LoadPercent)
{
    public static MemoryInfo? Current()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref status) || status.ullTotalPhys == 0) return null;
        var load = 100.0 * (status.ullTotalPhys - status.ullAvailPhys) / status.ullTotalPhys;
        return new MemoryInfo(status.ullTotalPhys, status.ullAvailPhys, Math.Round(load, 1));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);
}

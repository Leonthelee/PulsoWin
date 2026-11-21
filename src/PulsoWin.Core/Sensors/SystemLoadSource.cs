using System.Runtime.InteropServices;

namespace PulsoWin.Core.Sensors;

/// <summary>Uso de CPU y RAM con APIs de Win32 (sin administrador).</summary>
public sealed class SystemLoadSource : ISensorSource
{
    private readonly object _gate = new();
    private long _prevIdle, _prevKernel, _prevUser;
    private bool _hasPrevious;

    public string Name => "Sistema";

    public IReadOnlyList<SensorReading> Read()
    {
        var cpu = CpuPercent();
        var ram = MemoryInfo.Current();
        return
        [
            new SensorReading(Name, "CPU · uso", cpu, "%",
                cpu is null ? "se calcula a partir de la segunda lectura" : null),
            new SensorReading(Name, "RAM · en uso", ram?.LoadPercent, "%",
                ram is { } m ? $"{Common.Bytes.Format((long)(m.TotalBytes - m.AvailableBytes))} de {Common.Bytes.Format((long)m.TotalBytes)}" : null),
        ];
    }

    private double? CpuPercent()
    {
        lock (_gate)
        {
            if (!GetSystemTimes(out var idle, out var kernel, out var user)) return null;
            double? result = null;
            if (_hasPrevious)
            {
                var total = (kernel - _prevKernel) + (user - _prevUser); // kernel incluye el tiempo inactivo
                if (total > 0)
                    result = Math.Round(Math.Clamp(100.0 * (total - (idle - _prevIdle)) / total, 0, 100), 1);
            }
            (_prevIdle, _prevKernel, _prevUser, _hasPrevious) = (idle, kernel, user, true);
            return result;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);
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

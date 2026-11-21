using System.Runtime.InteropServices;
using System.Text;

namespace PulsoWin.Core.Sensors;

/// <summary>
/// GPU NVIDIA mediante NVML (nvml.dll, viene con el driver de NVIDIA). No necesita driver propio.
/// NVIDIA documenta soporte limitado en GPU que no son de data center; si una llamada falla, se muestra «no disponible».
/// El ventilador se informa en porcentaje, no en RPM.
/// </summary>
public sealed class NvmlSource : ISensorSource, IDisposable
{
    private const int NVML_SUCCESS = 0;
    private const int NVML_TEMPERATURE_GPU = 0;
    private readonly object _gate = new();
    private bool? _initialized;

    public string Name => "GPU NVIDIA (NVML)";

    public IReadOnlyList<SensorReading> Read()
    {
        lock (_gate)
        {
            if (!EnsureInitialized()) return [];

            var list = new List<SensorReading>();
            if (Native.nvmlDeviceGetCount_v2(out var count) != NVML_SUCCESS) return list;

            for (uint i = 0; i < count; i++)
            {
                if (Native.nvmlDeviceGetHandleByIndex_v2(i, out var device) != NVML_SUCCESS) continue;

                var buffer = new byte[96];
                var name = Native.nvmlDeviceGetName(device, buffer, (uint)buffer.Length) == NVML_SUCCESS
                    ? Encoding.ASCII.GetString(buffer).TrimEnd('\0')
                    : $"GPU {i}";

                double? temperature = Native.nvmlDeviceGetTemperature(device, NVML_TEMPERATURE_GPU, out var t) == NVML_SUCCESS
                    ? t : null;
                list.Add(new SensorReading(Name, $"{name} · temperatura", temperature, "°C"));

                double? fan = Native.nvmlDeviceGetFanSpeed(device, out var f) == NVML_SUCCESS ? f : null;
                list.Add(new SensorReading(Name, $"{name} · ventilador", fan, "%",
                    fan is null ? "sin ventilador o no expuesto" : null));

                double? usage = Native.nvmlDeviceGetUtilizationRates(device, out var u) == NVML_SUCCESS ? u.Gpu : null;
                list.Add(new SensorReading(Name, $"{name} · uso", usage, "%"));
            }
            return list;
        }
    }

    private bool EnsureInitialized()
    {
        if (_initialized is { } done) return done;
        try
        {
            _initialized = Native.nvmlInit_v2() == NVML_SUCCESS;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            _initialized = false; // Sin GPU NVIDIA o sin driver: la fuente simplemente no aporta lecturas.
        }
        return _initialized.Value;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_initialized == true) Native.nvmlShutdown();
            _initialized = false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlUtilization
    {
        public uint Gpu;
        public uint Memory;
    }

    // La búsqueda de DLL se limita a System32 para todo el ensamblado (ver AssemblyInfo.cs).
    private static class Native
    {
        private const string Lib = "nvml.dll";

        [DllImport(Lib)] public static extern int nvmlInit_v2();
        [DllImport(Lib)] public static extern int nvmlShutdown();
        [DllImport(Lib)] public static extern int nvmlDeviceGetCount_v2(out uint count);
        [DllImport(Lib)] public static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);
        [DllImport(Lib)] public static extern int nvmlDeviceGetName(IntPtr device, byte[] name, uint length);
        [DllImport(Lib)] public static extern int nvmlDeviceGetTemperature(IntPtr device, int sensorType, out uint temperature);
        [DllImport(Lib)] public static extern int nvmlDeviceGetFanSpeed(IntPtr device, out uint speed);
        [DllImport(Lib)] public static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out NvmlUtilization utilization);
    }
}

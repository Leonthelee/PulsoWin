using PulsoWin.Core.Sensors;

namespace PulsoWin.Core.Metrics;

/// <summary>Uso y temperatura de la GPU para los diales. Cualquier valor puede ser null («no disponible»).</summary>
public sealed record GpuStatus(string? Name, double? UsagePercent, double? TemperatureC, string Source);

/// <summary>
/// GPU sin drivers de kernel:
/// - NVIDIA: NVML (viene con el driver normal de NVIDIA) da uso y temperatura.
/// - Cualquier otra: el uso sale de los contadores "GPU Engine" de Windows; la temperatura queda «no disponible»
///   (leerla sin NVML exigiría un driver de kernel, que puede chocar con los antitrampas).
/// </summary>
public sealed class GpuSampler : IDisposable
{
    private readonly NvmlSource _nvml = new();
    private readonly GpuEngineCounters _counters = new();

    public GpuStatus Sample()
    {
        var nvidia = _nvml.ReadPrimary();
        if (nvidia is not null && nvidia.UsagePercent is not null)
            return new GpuStatus(nvidia.Name, nvidia.UsagePercent, nvidia.TemperatureC, "NVML");

        var usage = GpuEngineCounters.Total(_counters.Sample()) ?? (_counters.IsAvailable ? 0 : null);
        return new GpuStatus(nvidia?.Name, usage, nvidia?.TemperatureC, "Contadores de Windows");
    }

    public void Dispose() => _nvml.Dispose();
}

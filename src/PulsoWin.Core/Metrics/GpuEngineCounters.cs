using System.Diagnostics;
using System.Text.RegularExpressions;

namespace PulsoWin.Core.Metrics;

/// <summary>Uso de un motor de la GPU por un proceso en el último intervalo (en %).</summary>
public readonly record struct GpuEngineUsage(int Pid, string Engine, double Percent);

/// <summary>
/// Lee los contadores "GPU Engine" de Windows (los mismos del Administrador de tareas), con cualquier GPU
/// y sin administrador. Se usa para el top de GPU por programa y para el uso total cuando no hay NVML.
/// Cada lectura compara con la anterior: la primera solo fija la referencia.
/// </summary>
public sealed partial class GpuEngineCounters
{
    private readonly object _gate = new();
    private Dictionary<string, CounterSample> _previous = [];

    [GeneratedRegex(@"^pid_(\d+)_.*engtype_(.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex InstanceRegex();

    /// <summary>false si el equipo no tiene estos contadores (o están deshabilitados).</summary>
    public bool IsAvailable { get; private set; } = true;

    /// <summary>Uso por proceso y motor desde la lectura anterior; vacío en la primera o si no hay contadores.</summary>
    public IReadOnlyList<GpuEngineUsage> Sample()
    {
        lock (_gate)
        {
            if (!IsAvailable) return [];
            InstanceDataCollection data;
            try
            {
                data = new PerformanceCounterCategory("GPU Engine").ReadCategory()["Utilization Percentage"];
            }
            catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException
                                           or System.ComponentModel.Win32Exception or NullReferenceException)
            {
                IsAvailable = false;
                return [];
            }

            var current = new Dictionary<string, CounterSample>(data.Count);
            var result = new List<GpuEngineUsage>();
            foreach (InstanceData instance in data.Values)
            {
                current[instance.InstanceName] = instance.Sample;
                if (!_previous.TryGetValue(instance.InstanceName, out var before)) continue;
                var match = InstanceRegex().Match(instance.InstanceName);
                if (!match.Success || !int.TryParse(match.Groups[1].Value, out var pid)) continue;
                result.Add(new GpuEngineUsage(pid, match.Groups[2].Value, CounterSample.Calculate(before, instance.Sample)));
            }
            _previous = current;
            return result;
        }
    }

    /// <summary>
    /// Uso total de la GPU como lo calcula el Administrador de tareas: se suma el uso de todos los procesos
    /// en cada tipo de motor (3D, Copy, VideoDecode…) y se toma el motor más ocupado.
    /// </summary>
    public static double? Total(IReadOnlyList<GpuEngineUsage> usage) =>
        usage.Count == 0
            ? null
            : Math.Min(100, usage.GroupBy(u => u.Engine, StringComparer.OrdinalIgnoreCase).Max(g => g.Sum(u => u.Percent)));
}

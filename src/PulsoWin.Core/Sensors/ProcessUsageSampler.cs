using System.Diagnostics;
using System.Text.RegularExpressions;
using PulsoWin.Core.Common;

namespace PulsoWin.Core.Sensors;

/// <summary>Un programa en el top de consumo. Agrupa todos sus procesos (por ejemplo, las 25 pestañas de chrome).</summary>
public sealed record UsageItem(string Name, double Value, string Display, int Processes);

public sealed record UsageTop(
    IReadOnlyList<UsageItem> Cpu,
    IReadOnlyList<UsageItem> Ram,
    IReadOnlyList<UsageItem> Gpu,
    bool GpuAvailable)
{
    public static UsageTop Empty { get; } = new([], [], [], true);
}

/// <summary>
/// Top de consumo por programa, como el Administrador de tareas, sin administrador:
/// - CPU: diferencia de tiempo de procesador entre dos muestras, repartida entre todos los núcleos.
/// - RAM: memoria en uso (working set) sumada por programa; es aproximada porque las páginas compartidas cuentan dos veces.
/// - GPU: contadores "GPU Engine" de Windows (el motor más ocupado de cada proceso).
/// La primera muestra solo prepara la referencia: CPU y GPU aparecen desde la segunda.
/// Los procesos protegidos que no dejan leer su tiempo de CPU se omiten en CPU, pero sí cuentan en RAM.
/// </summary>
public sealed partial class ProcessUsageSampler(int top = 3, TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly object _gate = new();
    private Dictionary<int, TimeSpan> _prevCpu = [];
    private DateTimeOffset _prevAt;
    private readonly Metrics.GpuEngineCounters _gpu = new(); // mismos contadores que el uso total de GPU

    public UsageTop Sample()
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            var elapsedMs = _prevAt == default ? 0 : (now - _prevAt).TotalMilliseconds;
            var names = new Dictionary<int, string>();
            var cpuNow = new Dictionary<int, TimeSpan>();
            var ram = new Dictionary<string, (long Bytes, int Count)>(StringComparer.OrdinalIgnoreCase);
            var cpu = new Dictionary<string, (double Pct, int Count)>(StringComparer.OrdinalIgnoreCase);

            foreach (var p in Process.GetProcesses())
            {
                using (p)
                {
                    if (p.Id == 0) continue; // "Idle": no es un programa
                    string name;
                    try { name = p.ProcessName; }
                    catch (InvalidOperationException) { continue; } // terminó entre medio
                    names[p.Id] = name;

                    try
                    {
                        var bytes = p.WorkingSet64;
                        var r = ram.GetValueOrDefault(name);
                        ram[name] = (r.Bytes + bytes, r.Count + 1);
                    }
                    catch (InvalidOperationException) { }

                    try
                    {
                        var total = p.TotalProcessorTime;
                        cpuNow[p.Id] = total;
                        if (elapsedMs > 0 && _prevCpu.TryGetValue(p.Id, out var before) && total >= before)
                        {
                            var pct = (total - before).TotalMilliseconds / (elapsedMs * Environment.ProcessorCount) * 100;
                            var c = cpu.GetValueOrDefault(name);
                            cpu[name] = (c.Pct + pct, c.Count + 1);
                        }
                    }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
                    {
                        // Proceso protegido o del sistema: no deja leer su tiempo de CPU.
                    }
                }
            }

            _prevCpu = cpuNow;
            _prevAt = now;

            return new UsageTop(
                Top(cpu.Select(kv => (kv.Key, Math.Min(kv.Value.Pct, 100), kv.Value.Count)), v => $"{v:0.#} %", minimum: 0.1),
                Top(ram.Select(kv => (kv.Key, (double)kv.Value.Bytes, kv.Value.Count)), v => Bytes.Format((long)v), minimum: 1),
                Top(SampleGpu(names), v => $"{v:0} %", minimum: 0.5),
                _gpu.IsAvailable);
        }
    }

    private IEnumerable<(string Name, double Value, int Count)> SampleGpu(Dictionary<int, string> names) =>
        _gpu.Sample()
            .GroupBy(u => u.Pid)
            .Select(g => (Pid: g.Key, Percent: g.Max(u => u.Percent)))   // motor más ocupado del proceso
            .Where(p => names.ContainsKey(p.Pid))
            .GroupBy(p => names[p.Pid], StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.Key, Math.Min(g.Sum(p => p.Percent), 100), g.Count()));

    private IReadOnlyList<UsageItem> Top(IEnumerable<(string Name, double Value, int Count)> items, Func<double, string> format, double minimum) =>
        items
            .Where(i => i.Value >= minimum)
            .OrderByDescending(i => i.Value)
            .Take(top)
            .Select(i => new UsageItem(i.Name, i.Value, format(i.Value), i.Count))
            .ToList();
}

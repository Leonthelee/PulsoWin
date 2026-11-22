// PulsoWin · MIT · Co-designed with AI coding assistants (Claude & Gemini)
using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

namespace PulsoWin.Core.Network;

/// <summary>
/// Modo administrador: escucha los eventos de red del kernel (ETW) y suma bytes exactos por PID.
/// - Sesión con nombre propio por proceso y StopOnDispose = true: al cerrar la app no queda una sesión
///   del kernel huérfana consumiendo recursos.
/// - Los eventos llegan en un hilo de fondo y se suman con Interlocked (sin locks).
/// Requiere administrador: Start lanza si no se puede crear la sesión.
/// </summary>
public sealed class EtwTrafficSource : ITrafficSource
{
    private readonly ConcurrentDictionary<int, PidTraffic> _traffic = new();
    private readonly ProcessNameCache _names = new();
    private TraceEventSession? _session;
    private Thread? _pump;

    public NetworkMonitorMode Mode => NetworkMonitorMode.PerProcess;

    public static string SessionName => $"PulsoWin-Red-{Environment.ProcessId}";

    public void Start()
    {
        _session = new TraceEventSession(SessionName) { StopOnDispose = true };
        _session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);

        var kernel = _session.Source.Kernel;
        kernel.TcpIpRecv += e => Add(e.ProcessID, e.size, 0);
        kernel.TcpIpSend += e => Add(e.ProcessID, 0, e.size);
        kernel.TcpIpRecvIPV6 += e => Add(e.ProcessID, e.size, 0);
        kernel.TcpIpSendIPV6 += e => Add(e.ProcessID, 0, e.size);
        kernel.UdpIpRecv += e => Add(e.ProcessID, e.size, 0);
        kernel.UdpIpSend += e => Add(e.ProcessID, 0, e.size);
        kernel.UdpIpRecvIPV6 += e => Add(e.ProcessID, e.size, 0);
        kernel.UdpIpSendIPV6 += e => Add(e.ProcessID, 0, e.size);

        // Process() bloquea hasta que la sesión se detiene: va en un hilo de fondo propio.
        _pump = new Thread(() =>
        {
            try { _session.Source.Process(); }
            catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException) { }
        })
        { IsBackground = true, Name = "PulsoWin ETW red" };
        _pump.Start();
    }

    private void Add(int pid, int rx, int tx)
    {
        if (pid < 0) return;
        var t = _traffic.GetOrAdd(pid, static _ => new PidTraffic());
        if (rx > 0) Interlocked.Add(ref t.Rx, rx);
        if (tx > 0) Interlocked.Add(ref t.Tx, tx);
    }

    public IReadOnlyList<TrafficCounter> ReadCounters() =>
        _traffic.Select(kv => new TrafficCounter(
                $"pid:{kv.Key}", kv.Key, _names.Get(kv.Key),
                Interlocked.Read(ref kv.Value.Rx), Interlocked.Read(ref kv.Value.Tx)))
            .ToList();

    public void Dispose()
    {
        _session?.Dispose();          // StopOnDispose: detiene la sesión del kernel y libera Process()
        _pump?.Join(TimeSpan.FromSeconds(2));
        _session = null;
    }

    private sealed class PidTraffic
    {
        public long Rx;
        public long Tx;
    }
}

/// <summary>Caché PID → nombre de proceso (30 s), para no consultar al sistema en cada refresco.</summary>
public sealed class ProcessNameCache(TimeProvider? time = null)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly ConcurrentDictionary<int, (string Name, DateTimeOffset At)> _cache = new();

    public string Get(int pid)
    {
        var now = _time.GetUtcNow();
        if (_cache.TryGetValue(pid, out var hit) && now - hit.At < Ttl) return hit.Name;

        var name = pid switch
        {
            0 => "Inactivo del sistema",
            4 => "System",
            _ => Lookup(pid),
        };
        _cache[pid] = (name, now);
        return name;
    }

    private static string Lookup(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return "(proceso terminado)";
        }
    }
}

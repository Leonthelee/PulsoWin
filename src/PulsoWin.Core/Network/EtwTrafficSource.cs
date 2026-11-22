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
/// - También escucha el inicio de procesos: si Windows reutiliza un PID, el proceso nuevo empieza una generación
///   nueva y no hereda los bytes ni el nombre del anterior.
/// Requiere administrador: Start lanza si no se puede crear la sesión.
/// </summary>
public sealed class EtwTrafficSource : ITrafficSource
{
    private readonly ConcurrentDictionary<(int Pid, int Generation), PidTraffic> _traffic = new();
    private readonly ConcurrentDictionary<int, int> _generation = new();
    /// <summary>Procesos terminados que se siguen mostrando con su total; los más antiguos se descartan.</summary>
    public const int MaxExited = 256;
    private readonly ConcurrentQueue<(int Pid, int Generation)> _exited = new();
    private readonly Func<int, string> _lookupName;
    private TraceEventSession? _session;

    /// <param name="lookupName">Nombre de un PID vivo (pruebas). Por defecto, el del sistema.</param>
    public EtwTrafficSource(Func<int, string>? lookupName = null) => _lookupName = lookupName ?? ProcessNameCache.Lookup;
    private Thread? _pump;

    public NetworkMonitorMode Mode => NetworkMonitorMode.PerProcess;

    public static string SessionName => $"PulsoWin-Red-{Environment.ProcessId}";

    public void Start()
    {
        _session = new TraceEventSession(SessionName) { StopOnDispose = true };
        _session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP | KernelTraceEventParser.Keywords.Process);

        var kernel = _session.Source.Kernel;
        kernel.ProcessStart += e => _generation.AddOrUpdate(e.ProcessID, 1, static (_, g) => g + 1);
        kernel.ProcessStop += e => Exited(e.ProcessID);
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

    /// <summary>Suma bytes a la generación actual del PID. El nombre se toma con el primer byte, con el proceso vivo.</summary>
    public void Add(int pid, int rx, int tx)
    {
        if (pid < 0) return;
        var t = _traffic.GetOrAdd((pid, _generation.GetValueOrDefault(pid)), static (key, self) => new PidTraffic(self.NameOf(key.Pid)), this);
        if (rx > 0) Interlocked.Add(ref t.Rx, rx);
        if (tx > 0) Interlocked.Add(ref t.Tx, tx);
    }

    /// <summary>
    /// El proceso terminó: su fila queda con su total, pero solo las <see cref="MaxExited"/> más recientes. Así una
    /// sesión larga no crece sin límite. El total de la sesión no baja: el calculador acumula lo ya contado.
    /// </summary>
    public void Exited(int pid)
    {
        var key = (pid, _generation.GetValueOrDefault(pid));
        if (!_traffic.ContainsKey(key)) return;
        _exited.Enqueue(key);
        while (_exited.Count > MaxExited && _exited.TryDequeue(out var oldest))
            _traffic.TryRemove(oldest, out _);
    }

    public IReadOnlyList<TrafficCounter> ReadCounters() =>
        _traffic.Select(kv => new TrafficCounter(
                $"pid:{kv.Key.Pid}:{kv.Key.Generation}", kv.Key.Pid, kv.Value.Name,
                Interlocked.Read(ref kv.Value.Rx), Interlocked.Read(ref kv.Value.Tx), kv.Key.Generation))
            .ToList();

    private string NameOf(int pid) => pid switch
    {
        0 => Localization.L.T("Net_SystemIdle"),
        4 => "System",
        _ => _lookupName(pid),
    };

    public void Dispose()
    {
        _session?.Dispose();          // StopOnDispose: detiene la sesión del kernel y libera Process()
        _pump?.Join(TimeSpan.FromSeconds(2));
        _session = null;
    }

    private sealed class PidTraffic(string name)
    {
        public readonly string Name = name;
        public long Rx;
        public long Tx;
    }
}

/// <summary>
/// Caché (PID, generación) → nombre de proceso, para no consultar al sistema en cada refresco.
/// El nombre se guarda mientras la fila siga llegando: un PID reutilizado llega con otra generación y se vuelve a
/// consultar, y un proceso terminado conserva su nombre en vez de tomar el del proceso nuevo con su PID.
/// </summary>
public sealed class ProcessNameCache
{
    private readonly ConcurrentDictionary<(int Pid, int Generation), string> _cache = new();

    public int Count => _cache.Count;

    /// <summary>Olvida las filas que ya no llegan. Nunca vacía las que siguen: no se re-consultan por un PID ajeno.</summary>
    public void Retain(IEnumerable<(int Pid, int Generation)> keys)
    {
        var keep = keys.ToHashSet();
        if (_cache.Count <= keep.Count) return;
        foreach (var key in _cache.Keys)
            if (!keep.Contains(key)) _cache.TryRemove(key, out _);
    }

    public string Get(int pid, int generation = 0)
    {
        if (_cache.TryGetValue((pid, generation), out var hit)) return hit;

        var name = pid switch
        {
            0 => Localization.L.T("Net_SystemIdle"),
            4 => "System",
            _ => Lookup(pid),
        };
        _cache[(pid, generation)] = name;
        return name;
    }

    public static string Lookup(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Localization.L.T("Net_ProcessExited");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return Localization.L.T("Net_ProcessHidden");   // existe, pero esta cuenta no puede leer su nombre
        }
    }
}

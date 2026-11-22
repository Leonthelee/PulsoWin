// PulsoWin · MIT · Co-designed with AI coding assistants (Claude & Gemini)
using PulsoWin.Core.Common;

namespace PulsoWin.Core.Network;

public enum NetworkMonitorMode
{
    /// <summary>Administrador: ETW del kernel, bytes exactos por proceso (PID).</summary>
    PerProcess,
    /// <summary>Usuario estándar: contadores de cada interfaz de red activa (sin detalle por proceso).</summary>
    PerInterface,
}

/// <summary>Una fila del top de tráfico: un proceso (modo administrador) o una interfaz (modo usuario).</summary>
public sealed record NetworkUsageItem(
    int? Pid,
    string Process,
    double RxBytesPerSecond,
    double TxBytesPerSecond,
    long SessionTotalBytes)
{
    public double CurrentBytesPerSecond => RxBytesPerSecond + TxBytesPerSecond;
    public string PidText => Pid?.ToString() ?? "—";
    public string RxText => Rates.Format(RxBytesPerSecond);
    public string TxText => Rates.Format(TxBytesPerSecond);
    public string SessionTotalText => Bytes.Format(SessionTotalBytes);
}


/// <summary>Bytes acumulados desde que empezó la sesión de monitoreo, por proceso o por interfaz.</summary>
public readonly record struct TrafficCounter(string Key, int? Pid, string Name, long RxTotal, long TxTotal);

/// <summary>Fuente de contadores acumulados (ETW por proceso o estadísticas de interfaz).</summary>
public interface ITrafficSource : IDisposable
{
    NetworkMonitorMode Mode { get; }

    /// <summary>Empieza a medir. Puede lanzar si la fuente no está disponible (por ejemplo, ETW sin permisos).</summary>
    void Start();

    /// <summary>Contadores acumulados desde Start. Debe ser barato: se llama cada 2 segundos.</summary>
    IReadOnlyList<TrafficCounter> ReadCounters();
}

/// <summary>Velocidades legibles: KB/s, o MB/s desde 1024 KB/s (base binaria).</summary>
public static class Rates
{
    public static string Format(double bytesPerSecond)
    {
        var kb = Math.Max(0, bytesPerSecond) / 1024.0;
        return kb >= 1024 ? $"{kb / 1024:0.0} MB/s" : $"{kb:0.0} KB/s";
    }
}

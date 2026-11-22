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


/// <summary>
/// Bytes acumulados desde que empezó la sesión de monitoreo, por proceso o por interfaz.
/// <paramref name="Generation"/> sube cada vez que Windows reutiliza un PID: dos procesos distintos con el mismo PID
/// no mezclan sus bytes ni su nombre.
/// </summary>
public readonly record struct TrafficCounter(string Key, int? Pid, string Name, long RxTotal, long TxTotal, int Generation = 0);

/// <summary>Fuente de contadores acumulados (ETW por proceso o estadísticas de interfaz).</summary>
public interface ITrafficSource : IDisposable
{
    NetworkMonitorMode Mode { get; }

    /// <summary>Empieza a medir. Puede lanzar si la fuente no está disponible (por ejemplo, ETW sin permisos).</summary>
    void Start();

    /// <summary>Contadores acumulados desde Start. Debe ser barato: se llama cada segundo.</summary>
    IReadOnlyList<TrafficCounter> ReadCounters();

    /// <summary>true si la fuente dejó de medir después de Start (por ejemplo, se cerró el auxiliar elevado).</summary>
    bool IsFaulted => false;
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

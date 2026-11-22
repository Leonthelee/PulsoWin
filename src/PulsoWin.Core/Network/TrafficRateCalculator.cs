// PulsoWin · MIT · Co-designed with AI coding assistants (Claude & Gemini)
namespace PulsoWin.Core.Network;

/// <summary>Resultado de un intervalo: el top y los totales de TODO el tráfico (no solo del top).</summary>
public sealed record TrafficResult(
    IReadOnlyList<NetworkUsageItem> Top,
    double TotalRxBytesPerSecond,
    double TotalTxBytesPerSecond,
    long TotalSessionBytes);

/// <summary>
/// Convierte contadores acumulados en velocidad actual: (acumulado actual − acumulado anterior) / intervalo.
/// Ordena de mayor a menor consumo actual (RX + TX) y devuelve como máximo <c>top</c> filas, más los totales.
/// Lógica pura y sin estado externo: se prueba sin red ni administrador.
/// </summary>
public sealed class TrafficRateCalculator(int top = 10)
{
    private Dictionary<string, (long Rx, long Tx)> _previous = [];

    public TrafficResult Next(IReadOnlyList<TrafficCounter> counters, TimeSpan elapsed)
    {
        var seconds = Math.Max(elapsed.TotalSeconds, 0.001);
        var current = new Dictionary<string, (long Rx, long Tx)>(counters.Count);
        var items = new List<NetworkUsageItem>(counters.Count);

        foreach (var c in counters)
        {
            current[c.Key] = (c.RxTotal, c.TxTotal);
            double rx = 0, tx = 0;
            if (_previous.TryGetValue(c.Key, out var before))
            {
                // Un contador que retrocede (interfaz reiniciada, PID reutilizado) no produce velocidad negativa.
                rx = Math.Max(0, c.RxTotal - before.Rx) / seconds;
                tx = Math.Max(0, c.TxTotal - before.Tx) / seconds;
            }
            items.Add(new NetworkUsageItem(c.Pid, c.Name, rx, tx, Math.Max(0, c.RxTotal) + Math.Max(0, c.TxTotal)));
        }

        _previous = current;
        var topItems = items
            .OrderByDescending(i => i.CurrentBytesPerSecond)
            .ThenByDescending(i => i.SessionTotalBytes)
            .ThenBy(i => i.Process, StringComparer.OrdinalIgnoreCase)
            .Take(top)
            .ToList();
        return new TrafficResult(topItems, items.Sum(i => i.RxBytesPerSecond), items.Sum(i => i.TxBytesPerSecond),
            items.Sum(i => i.SessionTotalBytes));
    }
}

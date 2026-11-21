using System.Net.NetworkInformation;

namespace PulsoWin.Core.Network;

/// <summary>
/// Modo usuario estándar: tráfico real por interfaz activa (Up, sin Loopback) con GetIPStatistics.
/// No necesita permisos ni lanza UnauthorizedAccessException. Los contadores de Windows cuentan desde que la
/// interfaz se conectó; aquí se resta la lectura inicial para mostrar el total de ESTA sesión.
/// </summary>
public sealed class InterfaceTrafficSource : ITrafficSource
{
    private readonly Dictionary<string, (long Rx, long Tx)> _baseline = [];

    public NetworkMonitorMode Mode => NetworkMonitorMode.PerInterface;

    public void Start() => ReadCounters(); // fija la línea base

    public IReadOnlyList<TrafficCounter> ReadCounters()
    {
        var result = new List<TrafficCounter>();
        NetworkInterface[] interfaces;
        try
        {
            interfaces = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException)
        {
            return result;
        }

        var active = interfaces
            .Where(ni => ni.OperationalStatus == OperationalStatus.Up && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .ToList();
        var realNames = WithoutFilterCopies(active.Select(ni => ni.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var ni in active)
        {
            if (!realNames.Contains(ni.Name)) continue;
            try
            {
                var stats = ni.GetIPStatistics();
                var (rx, tx) = (stats.BytesReceived, stats.BytesSent);
                if (!_baseline.TryGetValue(ni.Id, out var start))
                    _baseline[ni.Id] = start = (rx, tx);
                result.Add(new TrafficCounter(ni.Id, null, ni.Name, rx - start.Rx, tx - start.Tx));
            }
            catch (Exception ex) when (ex is NetworkInformationException or PlatformNotSupportedException)
            {
                // Interfaz que desaparece entre la lista y la lectura: se omite.
            }
        }
        return result;
    }

    /// <summary>
    /// Windows también lista los filtros del driver de cada adaptador como si fueran interfaces
    /// ("Ethernet-QoS Packet Scheduler-0000", "Ethernet-WFP Native MAC Layer LightWeight Filter-0000"),
    /// con los mismos contadores que "Ethernet". Se descarta todo nombre que sea otro nombre de la lista + "-…".
    /// </summary>
    public static IEnumerable<string> WithoutFilterCopies(IEnumerable<string> names)
    {
        var list = names.ToList();
        return list.Where(name => !list.Any(other =>
            !string.Equals(other, name, StringComparison.OrdinalIgnoreCase)
            && name.StartsWith(other + "-", StringComparison.OrdinalIgnoreCase)));
    }

    public void Dispose() { }
}

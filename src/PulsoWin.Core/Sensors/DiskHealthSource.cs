using System.Management;

namespace PulsoWin.Core.Sensors;

/// <summary>
/// Salud y temperatura de discos con la API de almacenamiento de Windows
/// (MSFT_PhysicalDisk y MSFT_StorageReliabilityCounter, lo mismo que Get-StorageReliabilityCounter).
/// Los contadores de fiabilidad suelen requerir administrador; sin él la temperatura queda «no disponible».
/// Estas consultas son lentas, así que el resultado se guarda 30 segundos.
/// </summary>
public sealed class DiskHealthSource(TimeProvider? time = null) : ISensorSource
{
    private const string Scope = @"root\Microsoft\Windows\Storage";
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly object _gate = new();
    private IReadOnlyList<SensorReading> _cached = [];
    private DateTimeOffset _cachedAt = DateTimeOffset.MinValue;

    public string Name => "Discos";

    public IReadOnlyList<SensorReading> Read()
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            if (now - _cachedAt < CacheFor) return _cached;
            _cached = Query();
            _cachedAt = now;
            return _cached;
        }
    }

    private IReadOnlyList<SensorReading> Query()
    {
        var temps = new Dictionary<string, double>();
        string? tempNote = null;
        try
        {
            using var s = new ManagementObjectSearcher(Scope, "SELECT DeviceId, Temperature FROM MSFT_StorageReliabilityCounter");
            foreach (var mo in s.Get())
            {
                using (mo)
                {
                    var id = mo["DeviceId"]?.ToString();
                    var t = mo["Temperature"] is { } raw ? Convert.ToDouble(raw) : 0;
                    if (id is not null && t > 0) temps[id] = t;
                }
            }
            if (temps.Count == 0) tempNote = "el disco no informa temperatura";
        }
        catch (Exception ex) when (Wmi.IsFailure(ex))
        {
            tempNote = "temperatura: requiere administrador";
        }

        var list = new List<SensorReading>();
        try
        {
            using var s = new ManagementObjectSearcher(Scope, "SELECT DeviceId, FriendlyName, HealthStatus FROM MSFT_PhysicalDisk");
            foreach (var mo in s.Get())
            {
                using (mo)
                {
                    var id = mo["DeviceId"]?.ToString() ?? "?";
                    var name = mo["FriendlyName"]?.ToString() ?? $"Disco {id}";
                    var health = (mo["HealthStatus"] is { } h ? Convert.ToInt32(h) : -1) switch
                    {
                        0 => "Saludable",
                        1 => "Advertencia",
                        2 => "En mal estado",
                        _ => "Desconocido",
                    };
                    list.Add(new SensorReading(Name, $"{name} · salud", null, "", null, health));

                    double? temperature = temps.TryGetValue(id, out var t) ? t : null;
                    list.Add(new SensorReading(Name, $"{name} · temperatura", temperature, "°C",
                        temperature is null ? tempNote : null));
                }
            }
        }
        catch (Exception ex) when (Wmi.IsFailure(ex))
        {
            list.Add(new SensorReading(Name, "Discos", null, "", "la API de almacenamiento no respondió"));
        }
        return list;
    }
}

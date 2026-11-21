namespace PulsoWin.Core.Sensors;

/// <summary>Foto del equipo para comparar antes y después de limpiar.</summary>
public sealed record Snapshot(DateTimeOffset At, string Drive, long DriveFreeBytes, double? RamUsedPercent, double? MaxTempC)
{
    public static Snapshot Take(IEnumerable<ISensorSource> sources)
    {
        var root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        var free = new DriveInfo(root).AvailableFreeSpace;
        var maxTemp = sources
            .SelectMany(s => Safe(s))
            .Where(r => r.Unit == "°C" && r.Value is not null)
            .Select(r => r.Value!.Value)
            .DefaultIfEmpty(double.NaN)
            .Max();
        return new Snapshot(DateTimeOffset.Now, root, free, MemoryInfo.Current()?.LoadPercent,
            double.IsNaN(maxTemp) ? null : maxTemp);
    }

    private static IReadOnlyList<SensorReading> Safe(ISensorSource source)
    {
        try { return source.Read(); }
        catch (Exception) { return []; } // un sensor que falla no debe impedir la foto
    }
}

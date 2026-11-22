// PulsoWin · MIT · Co-designed with AI coding assistants (Claude & Gemini)
namespace PulsoWin.Core.Sensors;

/// <summary>
/// Lectura de un sensor. Value null significa «no disponible», nunca cero:
/// la interfaz no debe inventar datos que el equipo no expone.
/// </summary>
public sealed record SensorReading(
    string Source,
    string Label,
    double? Value,
    string Unit,
    string? Note = null,
    string? Text = null)
{
    public string Display => Text ?? (Value is { } v ? $"{v:0.#} {Unit}".TrimEnd() : "no disponible");
}

public interface ISensorSource
{
    string Name { get; }
    IReadOnlyList<SensorReading> Read();
}

internal static class Wmi
{
    public static bool IsFailure(Exception ex) =>
        ex is System.Management.ManagementException
            or UnauthorizedAccessException
            or System.Runtime.InteropServices.COMException;
}

/// <summary>Lectura rápida de la primera GPU NVIDIA (nombre, temperatura en °C y uso en %).</summary>
public sealed record NvmlStatus(string Name, double? TemperatureC, double? UsagePercent);

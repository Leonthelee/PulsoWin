using System.Management;

namespace PulsoWin.Core.Sensors;

/// <summary>
/// Zonas térmicas ACPI vía WMI (root\WMI). Muchos equipos de consumo no las exponen,
/// y cuando lo hacen no suelen reflejar la temperatura real de la CPU.
/// No se usa Win32_TemperatureProbe: Microsoft documenta que no rellena CurrentReading.
/// </summary>
public sealed class AcpiThermalSource : ISensorSource
{
    public string Name => "ACPI (WMI)";

    public IReadOnlyList<SensorReading> Read()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI",
                "SELECT InstanceName, CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
            var list = new List<SensorReading>();
            foreach (var mo in searcher.Get())
            {
                using (mo)
                {
                    // Décimas de kelvin.
                    var celsius = Convert.ToDouble(mo["CurrentTemperature"]) / 10.0 - 273.15;
                    double? value = celsius is > -30 and < 150 ? Math.Round(celsius, 1) : null;
                    list.Add(new SensorReading(Name, $"Zona térmica {ShortName(mo["InstanceName"]?.ToString())}",
                        value, "°C", "ACPI: no equivale a la temperatura de la CPU"));
                }
            }
            if (list.Count == 0)
                list.Add(new SensorReading(Name, "Zonas térmicas", null, "°C", "el equipo no las expone"));
            return list;
        }
        catch (Exception ex) when (Wmi.IsFailure(ex))
        {
            var note = ex is ManagementException { ErrorCode: ManagementStatus.AccessDenied } or UnauthorizedAccessException
                ? "requiere administrador"
                : "el equipo no las expone";
            return [new SensorReading(Name, "Zonas térmicas", null, "°C", note)];
        }
    }

    private static string ShortName(string? instance)
    {
        if (string.IsNullOrEmpty(instance)) return "?";
        var cut = instance.LastIndexOf('\\');
        return (cut >= 0 ? instance[(cut + 1)..] : instance).Replace("_0", "");
    }
}

using System.Management;
using PulsoWin.Core.Common;

namespace PulsoWin.Core.Sensors;

public sealed record SummaryRow(string Section, string Value);

/// <summary>Ficha del equipo (lo que muestra Speccy en su resumen), con WMI estándar y sin administrador.</summary>
public static class SystemSummary
{
    public static IReadOnlyList<SummaryRow> Collect()
    {
        var rows = new List<SummaryRow>();

        Query(rows, "Sistema operativo", "SELECT Caption, Version, OSArchitecture FROM Win32_OperatingSystem",
            mo => $"{mo["Caption"]} {mo["Version"]} ({mo["OSArchitecture"]})");

        Query(rows, "CPU", "SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor",
            mo => $"{mo["Name"]?.ToString()?.Trim()} · {mo["NumberOfCores"]} núcleos / {mo["NumberOfLogicalProcessors"]} hilos");

        if (MemoryInfo.Current() is { } ram)
            rows.Add(new SummaryRow("RAM", $"{Bytes.Format((long)ram.TotalBytes)} en total"));
        Query(rows, "RAM", "SELECT Manufacturer, Capacity, Speed, DeviceLocator FROM Win32_PhysicalMemory",
            mo => $"{mo["DeviceLocator"]}: {FormatBytes(mo["Capacity"])} · {mo["Speed"]} MT/s · {mo["Manufacturer"]?.ToString()?.Trim()}");

        Query(rows, "Placa base", "SELECT Manufacturer, Product FROM Win32_BaseBoard",
            mo => $"{mo["Manufacturer"]} {mo["Product"]}");

        Query(rows, "BIOS", "SELECT Manufacturer, SMBIOSBIOSVersion FROM Win32_BIOS",
            mo => $"{mo["Manufacturer"]} {mo["SMBIOSBIOSVersion"]}");

        // AdapterRAM de WMI es de 32 bits y se satura en 4 GB, por eso no se muestra.
        Query(rows, "GPU", "SELECT Name, DriverVersion FROM Win32_VideoController",
            mo => $"{mo["Name"]} (driver {mo["DriverVersion"]})");

        Query(rows, "Almacenamiento", "SELECT Model, Size, InterfaceType FROM Win32_DiskDrive",
            mo => $"{mo["Model"]} · {FormatBytes(mo["Size"])} · {mo["InterfaceType"]}");

        return rows;
    }

    private static void Query(List<SummaryRow> rows, string section, string wql, Func<ManagementBaseObject, string> format)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\cimv2", wql);
            foreach (var mo in searcher.Get())
            {
                using (mo) rows.Add(new SummaryRow(section, format(mo)));
            }
        }
        catch (Exception ex) when (Wmi.IsFailure(ex))
        {
            rows.Add(new SummaryRow(section, "no disponible"));
        }
    }

    private static string FormatBytes(object? value) =>
        value is null ? "?" : Bytes.Format(Convert.ToInt64(value));
}

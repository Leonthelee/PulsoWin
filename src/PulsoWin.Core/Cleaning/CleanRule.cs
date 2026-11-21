namespace PulsoWin.Core.Cleaning;

/// <summary>
/// Regla declarativa de limpieza. Formato propio (no se reutilizan reglas de CCleaner ni winapp2.ini).
/// </summary>
public sealed record CleanRule
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }

    /// <summary>Carpeta base; admite variables de entorno como %LOCALAPPDATA%.</summary>
    public required string Base { get; init; }

    /// <summary>null: la base es la carpeta. "chromium" o "firefox": se recorre cada perfil dentro de la base.</summary>
    public string? Profiles { get; init; }

    /// <summary>Subcarpetas relativas (dentro de la base o de cada perfil). "" es la propia carpeta.</summary>
    public string[] SubPaths { get; init; } = [""];

    public string Pattern { get; init; } = "*";
    public bool Recursive { get; init; } = true;

    /// <summary>Solo se borran archivos sin modificar en este número de horas (protege instaladores en curso).</summary>
    public double MinAgeHours { get; init; }

    /// <summary>Procesos que deben estar cerrados (sin .exe), por ejemplo "chrome".</summary>
    public string[] BlockingProcesses { get; init; } = [];

    public bool DefaultOn { get; init; } = true;
    public string? Note { get; init; }
}

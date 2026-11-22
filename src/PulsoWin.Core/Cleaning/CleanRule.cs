// PulsoWin · MIT · Co-designed with AI coding assistants (Claude & Gemini)
namespace PulsoWin.Core.Cleaning;

/// <summary>
/// Regla declarativa de limpieza. Formato propio (no se reutilizan reglas de CCleaner ni winapp2.ini).
/// Las propiedades opcionales usan set (no init) a propósito: el serializador generado asigna
/// todas las propiedades init, y las ausentes del JSON quedarían en null/false en vez de su valor por defecto.
/// </summary>
public sealed record CleanRule
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }

    /// <summary>Carpeta base; admite variables de entorno como %LOCALAPPDATA%.</summary>
    public required string Base { get; init; }

    /// <summary>null: la base es la carpeta. "chromium" o "firefox": se recorre cada perfil dentro de la base.</summary>
    public string? Profiles { get; set; }

    /// <summary>Subcarpetas relativas (dentro de la base o de cada perfil). "" es la propia carpeta.</summary>
    public string[] SubPaths { get; set; } = [""];

    public string Pattern { get; set; } = "*";
    public bool Recursive { get; set; } = true;

    /// <summary>Solo se borran archivos sin modificar en este número de horas (protege instaladores en curso).</summary>
    public double MinAgeHours { get; set; }

    /// <summary>Procesos que deben estar cerrados (sin .exe), por ejemplo "chrome".</summary>
    public string[] BlockingProcesses { get; set; } = [];

    public bool DefaultOn { get; set; } = true;
    public string? Note { get; set; }
}

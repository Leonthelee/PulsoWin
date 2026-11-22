// PulsoWin · MIT · Co-designed with AI coding assistants (Claude & Gemini)
using CommunityToolkit.Mvvm.ComponentModel;
using PulsoWin.Core.Cleaning;
using PulsoWin.Core.Localization;

namespace PulsoWin.Presentation.Services;

/// <summary>Diálogos que necesita la lógica de pantalla. La vista los implementa con ventanas reales.</summary>
public interface IDialogService
{
    /// <summary>Pide confirmación. Devuelve true solo si la persona acepta explícitamente.</summary>
    bool Confirm(string title, string message);
}

/// <summary>Papelera de reciclaje, abstraída para que las pruebas nunca vacíen la papelera real.</summary>
public interface IRecycleBin
{
    (long Bytes, long Items) Query();
    bool Empty();
}

public sealed class WindowsRecycleBin : IRecycleBin
{
    public (long Bytes, long Items) Query() => RecycleBin.Query();
    public bool Empty() => RecycleBin.Empty();
}

/// <summary>Barra de estado compartida por todas las pestañas.</summary>
public sealed partial class StatusBar : ObservableObject
{
    [ObservableProperty]
    private string _message = L.T("Status_Ready");
}

/// <summary>
/// Idioma elegido por la persona ("es" o "en"), guardado como texto en %LOCALAPPDATA%\PulsoWin\idioma.txt.
/// Sin elección guardada se usa el idioma de Windows (español si Windows está en español; si no, inglés).
/// </summary>
public static class LanguagePreference
{
    public static string DefaultPath => Path.Combine(AppPaths.DataDir, "idioma.txt");

    public static string? Load(string? path = null)
    {
        try
        {
            var file = path ?? DefaultPath;
            return File.Exists(file) ? File.ReadAllText(file).Trim() : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Guarda la elección. Devuelve false si no se pudo escribir (la app sigue en el idioma actual).</summary>
    public static bool Save(string code, string? path = null)
    {
        try
        {
            var file = path ?? DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, L.Pick(code).TwoLetterISOLanguageName);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Idioma con que arranca la app: el guardado o, si no hay, el de Windows.</summary>
    public static System.Globalization.CultureInfo Resolve(string? saved, System.Globalization.CultureInfo windowsUi) =>
        L.Pick(string.IsNullOrWhiteSpace(saved) ? windowsUi.TwoLetterISOLanguageName : saved);

    /// <summary>El otro idioma disponible (lo que ofrece el botón de idioma).</summary>
    public static string Next(System.Globalization.CultureInfo current)
    {
        var i = L.Supported.ToList().IndexOf(current.TwoLetterISOLanguageName);
        return L.Supported[(i + 1) % L.Supported.Count];
    }
}

/// <summary>
/// Al abrir, PulsoWin pide permiso de administrador para medir la red por proceso (ETW).
/// Si la persona lo rechaza, la app abre igual en modo estándar (red por interfaz).
/// </summary>
public static class ElevationPolicy
{
    /// <summary>Saltarse la petición (por ejemplo, en equipos donde no se quiere el aviso cada vez).</summary>
    public const string SkipSwitch = "--sin-admin";
    /// <summary>Marca del relanzamiento elevado: evita pedirlo dos veces si Windows no concedió el permiso.</summary>
    public const string RelaunchedSwitch = "--elevado";

    public static bool ShouldRequestAdmin(bool isAdministrator, IReadOnlyCollection<string> args) =>
        !isAdministrator
        && !args.Any(a => a.Equals(SkipSwitch, StringComparison.OrdinalIgnoreCase)
                          || a.Equals(RelaunchedSwitch, StringComparison.OrdinalIgnoreCase)
                          || a.StartsWith("--wu-install", StringComparison.OrdinalIgnoreCase));
}

public static class AppPaths
{
    public static string DataDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PulsoWin");
}

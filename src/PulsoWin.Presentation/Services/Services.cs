using CommunityToolkit.Mvvm.ComponentModel;
using PulsoWin.Core.Cleaning;

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
    private string _message = "Listo. Pulsa START para ver qué se puede liberar.";
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

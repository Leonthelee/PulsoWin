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

public static class AppPaths
{
    public static string DataDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PulsoWin");
}

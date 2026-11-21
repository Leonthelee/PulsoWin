using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PulsoWin.Core.Network;

namespace PulsoWin.Presentation.ViewModels;

/// <summary>
/// Pestaña Red: top 10 de tráfico en vivo. Arranca con la ventana (no hace falta abrir la pestaña)
/// y se detiene al cancelar el token del ciclo de vida de la ventana.
/// </summary>
public sealed partial class NetworkViewModel(NetworkMonitorService service) : ObservableObject
{
    public const string Header = "Aquí te muestro tu tráfico de red actual (los primeros 10)";

    public ObservableCollection<NetworkUsageItem> Items { get; } = [];

    [ObservableProperty] private string _notice = "Iniciando el monitoreo de red…";
    [ObservableProperty] private bool _isPerProcess;
    [ObservableProperty] private string _modeText = "";

    /// <summary>Se llama desde el hilo de la interfaz; los snapshots llegan en ese mismo hilo.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        service.SnapshotUpdated += OnSnapshot;
        try
        {
            await service.RunAsync(cancellationToken);
        }
        finally
        {
            service.SnapshotUpdated -= OnSnapshot;
        }
    }

    private void OnSnapshot(object? sender, NetworkSnapshot snapshot)
    {
        Notice = snapshot.Notice;
        IsPerProcess = snapshot.Mode == NetworkMonitorMode.PerProcess;
        ModeText = IsPerProcess ? "POR PROCESO · ETW" : "POR INTERFAZ";

        // Fila por fila (como los sensores): la tabla no parpadea ni pierde el scroll cada 2 s.
        var items = snapshot.Items;
        for (var i = 0; i < items.Count; i++)
        {
            if (i < Items.Count)
            {
                if (!Equals(Items[i], items[i])) Items[i] = items[i];
            }
            else Items.Add(items[i]);
        }
        while (Items.Count > items.Count) Items.RemoveAt(Items.Count - 1);
    }
}

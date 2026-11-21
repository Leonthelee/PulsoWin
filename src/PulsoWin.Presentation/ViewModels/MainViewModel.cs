using System.ComponentModel;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PulsoWin.Core.Cleaning;
using PulsoWin.Core.Common;
using PulsoWin.Core.Sensors;
using PulsoWin.Core.Updates;
using PulsoWin.Presentation.Services;

namespace PulsoWin.Presentation.ViewModels;

/// <summary>Raíz de la pantalla: compone las tres pestañas y el estado de la cabecera.</summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IReadOnlyList<ISensorSource> _sources;

    public const int CleanTab = 0;

    [ObservableProperty] private int _selectedTab;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _heroCaption = "PULSA START";

    public MainViewModel(
        CleanViewModel clean,
        HardwareViewModel hardware,
        UpdatesViewModel updates,
        StatusBar status,
        IReadOnlyList<ISensorSource> sources)
    {
        Clean = clean;
        Hardware = hardware;
        Updates = updates;
        Status = status;
        _sources = sources;
        Clean.PropertyChanged += OnChildChanged;
        Updates.PropertyChanged += OnChildChanged;
    }

    /// <summary>Arma la pantalla con las dependencias reales del equipo.</summary>
    /// <param name="installWindowsUpdates">
    /// Instala actualizaciones de Windows por UpdateID. Lo provee la vista porque necesita lanzar el
    /// proceso auxiliar elevado (un permiso de administrador), cosa que esta capa no debe hacer.
    /// </param>
    public static MainViewModel Create(IDialogService dialogs,
        Func<IReadOnlyList<string>, Task<WindowsUpdateInstallResult>> installWindowsUpdates)
    {
        IReadOnlyList<ISensorSource> sources =
            [new SystemLoadSource(), new NvmlSource(), new DiskHealthSource(), new AcpiThermalSource()];
        var status = new StatusBar();
        var probe = new ProcessProbe();
        var dataDir = AppPaths.DataDir;
        var clean = new CleanViewModel(RuleCatalog.LoadBuiltIn(), probe, dialogs, new WindowsRecycleBin(), status, dataDir, sources);
        var updates = new UpdatesViewModel(new WingetClient(), () => new WindowsUpdateClient().QueryPending(),
            installWindowsUpdates, dialogs, status, dataDir);
        return new MainViewModel(clean, new HardwareViewModel(sources), updates, status, sources);
    }

    public CleanViewModel Clean { get; }
    public HardwareViewModel Hardware { get; }
    public UpdatesViewModel Updates { get; }
    public StatusBar Status { get; }

    public string VersionText { get; } =
        "v" + (typeof(MainViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0] ?? "?");

    /// <summary>El botón START de la cabecera: lleva a la pestaña Limpiar y lanza el análisis.</summary>
    [RelayCommand]
    private async Task StartAsync()
    {
        SelectedTab = CleanTab;
        if (Clean.ScanCommand.CanExecute(null))
            await Clean.ScanCommand.ExecuteAsync(null);
    }

    private void OnChildChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(CleanViewModel.IsBusy) or nameof(CleanViewModel.BusyLabel))) return;
        IsBusy = Clean.IsBusy || Updates.IsBusy;
        HeroCaption = Clean.IsBusy ? Clean.BusyLabel
            : Updates.IsBusy ? Updates.BusyLabel
            : "PULSA START";
    }

    public void Dispose()
    {
        Clean.PropertyChanged -= OnChildChanged;
        Updates.PropertyChanged -= OnChildChanged;
        foreach (var disposable in _sources.OfType<IDisposable>()) disposable.Dispose();
    }
}

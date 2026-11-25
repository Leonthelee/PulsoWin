// PulsoWin · MIT · Co-designed with AI coding assistants (Claude & Gemini)
using System.ComponentModel;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PulsoWin.Core.Cleaning;
using PulsoWin.Core.Common;
using PulsoWin.Core.Metrics;
using PulsoWin.Core.Network;
using PulsoWin.Core.Sensors;
using PulsoWin.Core.Updates;
using PulsoWin.Presentation.Services;
using PulsoWin.Core.Localization;

namespace PulsoWin.Presentation.ViewModels;

/// <summary>Raíz de la pantalla: compone las tres pestañas y el estado de la cabecera.</summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IReadOnlyList<ISensorSource> _sources;

    public const int CleanTab = 0;
    public const int HardwareTab = 1;
    public const int UpdatesTab = 2;
    public const int NetworkTab = 3;

    /// <summary>START solo sirve para limpiar: en la pestaña Actualizar se desvanece y no responde.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStartAvailable))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private int _selectedTab;

    /// <summary>START sirve en Limpiar (analiza) y Hardware (lleva a Limpiar); en Actualizar y Red no aporta nada.</summary>
    public bool IsStartAvailable => SelectedTab is CleanTab or HardwareTab;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _heroCaption = L.T("Hero_PressStart");

    // ------------------------------------------------------------------ huevo de pascua
    private readonly KonamiCode _konami = new();

    /// <summary>Firma de autoría, leída de los metadatos del ensamblado de la app.</summary>
    public AuthorSignature Signature { get; private set; } = AuthorSignature.FromAssembly(typeof(MainViewModel).Assembly);

    [ObservableProperty] private bool _isEasterEggOpen;
    [ObservableProperty] private string _easterEggTitle = KonamiTitle;

    /// <summary>El huevo de pascua solo existe cuando la app ya se usó más de estas veces.</summary>
    public const int ThanksAfterUses = 20;
    public static string ThanksMessage => L.T("Egg_Thanks");
    private static string KonamiTitle => L.T("Egg_KonamiTitle");

    /// <summary>Texto del agradecimiento para la vista.</summary>
    public string ThanksText => ThanksMessage;

    /// <summary>Veces que se abrió la app, contando esta (0 si no se pudo leer el contador).</summary>
    public int Uses { get; private set; }
    public bool IsEasterEggUnlocked => Uses > ThanksAfterUses;

    /// <summary>
    /// La vista avisa una vez al abrir cuántos usos lleva. En el uso 21 el huevo aparece solo, una única vez,
    /// con el agradecimiento; desde ahí también se puede volver a abrir con el código Konami.
    /// </summary>
    public void RegisterLaunch(int uses)
    {
        Uses = uses;
        if (uses != ThanksAfterUses + 1) return;
        EasterEggTitle = L.F("Egg_UsesTitle", uses);
        IsEasterEggOpen = true;
    }

    /// <summary>La vista indica qué ensamblado firma (el .exe), para leer sus metadatos.</summary>
    public void UseSignatureFrom(Assembly assembly) => Signature = AuthorSignature.FromAssembly(assembly);

    /// <summary>Recibe cada tecla ("Up", "B", …). Devuelve true si con ella se completó el código Konami (y el huevo ya está desbloqueado).</summary>
    public bool OnKey(string key)
    {
        if (!_konami.Push(key) || !IsEasterEggUnlocked) return false;
        EasterEggTitle = KonamiTitle;
        IsEasterEggOpen = true;
        return true;
    }

    [RelayCommand]
    private void CloseEasterEgg() => IsEasterEggOpen = false;

    public MainViewModel(
        CleanViewModel clean,
        HardwareViewModel hardware,
        UpdatesViewModel updates,
        StatusBar status,
        IReadOnlyList<ISensorSource> sources,
        MetricsViewModel? metrics = null)
    {
        Clean = clean;
        Hardware = hardware;
        Updates = updates;
        Metrics = metrics ?? new MetricsViewModel(new SystemMetricsService());
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
        var clean = new CleanViewModel(RuleCatalog.LoadBuiltIn(), probe, dialogs, new WindowsRecycleBin(), status, dataDir, sources, new BrowserCloser());
        var updates = new UpdatesViewModel(new WingetClient(), () => new WindowsUpdateClient().QueryPending(),
            installWindowsUpdates, dialogs, status, dataDir);
        // Red por proceso: la pide la persona en la pestaña RED; la mide un auxiliar elevado, no la app.
        var metrics = new MetricsViewModel(new SystemMetricsService(perProcessFactory: () => new ElevatedTrafficSource()));
        return new MainViewModel(clean, new HardwareViewModel(sources), updates, status, sources, metrics);
    }

    public CleanViewModel Clean { get; }
    public HardwareViewModel Hardware { get; }
    public UpdatesViewModel Updates { get; }
    /// <summary>Motor único de métricas en vivo (HUD, pestaña Red y Modo Gadget).</summary>
    public MetricsViewModel Metrics { get; }
    public StatusBar Status { get; }

    public string VersionText { get; } =
        "v" + (typeof(MainViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0] ?? "?");

    /// <summary>El botón START de la cabecera: lleva a la pestaña Limpiar y lanza el análisis.</summary>
    [RelayCommand(CanExecute = nameof(IsStartAvailable))]
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
            : L.T("Hero_PressStart");
    }

    public void Dispose()
    {
        Clean.PropertyChanged -= OnChildChanged;
        Updates.PropertyChanged -= OnChildChanged;
        foreach (var disposable in _sources.OfType<IDisposable>()) disposable.Dispose();
    }
}

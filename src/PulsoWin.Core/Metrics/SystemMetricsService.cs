// PulsoWin · MIT · Co-designed with AI coding assistants (Claude & Gemini)
using System.Security.Principal;
using PulsoWin.Core.Network;
using PulsoWin.Core.Sensors;

namespace PulsoWin.Core.Metrics;

/// <summary>Foto inmutable que el motor publica cada intervalo. La consumen todas las vistas.</summary>
public sealed record SystemMetricsSnapshot(
    DateTimeOffset At,
    double CpuPercent,
    double RamPercent,
    double RamUsedGB,
    double RamTotalGB,
    double NetRxKBps,
    double NetTxKBps,
    long NetSessionTotalBytes,
    IReadOnlyList<NetworkUsageItem> TopNetworkProcesses,
    bool IsAdminMode,
    NetworkMonitorMode NetworkMode,
    string NetworkNotice,
    double? GpuPercent = null,
    double? GpuTempC = null,
    string? GpuName = null);

/// <summary>
/// Motor único en segundo plano: CPU (GetSystemTimes), RAM (GlobalMemoryStatusEx) y red (ETW con administrador,
/// interfaces sin él). Sin drivers de kernel ni temperaturas: no choca con anti-cheats.
/// Las vistas (app completa y gadget) solo se suscriben a <see cref="SnapshotUpdated"/>: cambiar de vista
/// no reinicia ni duplica el monitoreo. Al cancelar el token libera la fuente de red (y la sesión ETW).
/// </summary>
public sealed class SystemMetricsService(
    Func<bool>? isAdministrator = null,
    Func<ITrafficSource>? etwFactory = null,
    Func<ITrafficSource>? interfaceFactory = null,
    TimeSpan? interval = null,
    int top = 10,
    TimeProvider? time = null,
    Func<double?>? cpu = null,
    Func<MemoryInfo?>? memory = null,
    Func<GpuStatus>? gpu = null,
    Func<ITrafficSource>? perProcessFactory = null)
{
    /// <summary>Cada cuánto se publica una foto: 1 s para que las agujas se vean casi en vivo.</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(1);

    public static string StandardUserNotice => Localization.L.T("Net_StandardNotice");
    public static string AdministratorNotice => Localization.L.T("Net_AdminNotice");
    public static string HelperNotice => Localization.L.T("Net_HelperNotice");

    /// <summary>
    /// La red por proceso se puede pedir sin reiniciar: hay un auxiliar elevado disponible y la app no es administrador.
    /// </summary>
    public bool CanRequestPerProcess => perProcessFactory is not null && !_isAdministrator();

    // Estado compartido entre la interfaz (pide) y el bucle (atiende). Todo bajo _gate.
    private readonly object _gate = new();
    private bool _running;
    private bool _perProcessActive;
    private TaskCompletionSource<bool>? _request;   // sigue en pie hasta que el cambio termina: un segundo clic la comparte

    /// <summary>
    /// Pide la red por proceso con el auxiliar elevado (Windows muestra el aviso de administrador).
    /// true si quedó activa (o ya lo estaba); false si se rechazó, falló, no está disponible o el motor no corre.
    /// Mientras se espera el aviso, las métricas siguen llegando con la fuente anterior, y pedirlo otra vez no lanza
    /// un segundo auxiliar: devuelve la misma espera.
    /// </summary>
    public Task<bool> RequestPerProcessAsync()
    {
        if (!CanRequestPerProcess) return Task.FromResult(false);
        lock (_gate)
        {
            if (!_running) return Task.FromResult(false);
            if (_perProcessActive) return Task.FromResult(true);
            _request ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            return _request.Task;
        }
    }

    private readonly Func<bool> _isAdministrator = isAdministrator ?? IsRunningAsAdministrator;
    private readonly Func<ITrafficSource> _etwFactory = etwFactory ?? (() => new EtwTrafficSource());
    private readonly Func<ITrafficSource> _interfaceFactory = interfaceFactory ?? (() => new InterfaceTrafficSource());
    private readonly TimeSpan _interval = interval ?? DefaultInterval;
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Func<double?> _cpu = cpu ?? new CpuSampler().Sample;
    private readonly Func<MemoryInfo?> _memory = memory ?? MemoryInfo.Current;

    public event EventHandler<SystemMetricsSnapshot>? SnapshotUpdated;

    /// <summary>Última foto publicada (para una vista que se abre a mitad de camino).</summary>
    public SystemMetricsSnapshot? Latest { get; private set; }

    public static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Corre hasta que se cancele. Los eventos se publican en el contexto desde el que se llamó
    /// (el hilo de la interfaz si se llama desde ella); las lecturas van a segundo plano.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var isAdmin = _isAdministrator();
        ITrafficSource source;
        string? notice;
        try
        {
            (source, notice) = await Task.Run(() => OpenNetworkSource(isAdmin), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;   // se cerró antes de abrir la red: no hay nada que liberar
        }
        if (!isAdmin && perProcessFactory is not null) notice = Localization.L.T("Net_StandardNoticeAsk");
        using var gpuSampler = gpu is null ? new GpuSampler() : null;   // NVML se libera al terminar
        var readGpu = gpu ?? gpuSampler!.Sample;
        Task<(ITrafficSource? Source, string? Error)>? switching = null;
        long sessionBase = 0, lastSessionTotal = 0;   // el total de la sesión no retrocede al cambiar de fuente
        lock (_gate) _running = true;
        try
        {
            var calculator = new TrafficRateCalculator(top);
            var last = _time.GetTimestamp();
            await Task.Run(() => { _cpu(); readGpu(); calculator.Next(source.ReadCounters(), _interval); }, cancellationToken); // referencias

            using var timer = new PeriodicTimer(_interval, _time);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(true))
            {
                // ¿Se cayó el auxiliar? Sus cifras ya no son actuales: se vuelve a la vista por interfaz.
                if (source.IsFaulted)
                {
                    // La última foto que alcanzó a llegar se cuenta antes de soltar la fuente.
                    lastSessionTotal = calculator.Next(source.ReadCounters(), _time.GetElapsedTime(last)).TotalSessionBytes;
                    sessionBase += lastSessionTotal;
                    lastSessionTotal = 0;
                    source.Dispose();
                    source = StartInterfaces();
                    notice = Localization.L.T("Net_HelperLost");
                    calculator = new TrafficRateCalculator(top);
                    calculator.Next(source.ReadCounters(), _interval);
                    lock (_gate) _perProcessActive = false;
                }

                // ¿Se pidió la red por proceso? El aviso de administrador se espera aparte, sin frenar las métricas.
                if (switching is null)
                {
                    bool wanted;
                    lock (_gate) wanted = _request is not null;
                    if (wanted) switching = Task.Run(() => TryStart(perProcessFactory!));
                }
                if (switching is { IsCompleted: true })
                {
                    var (elevated, error) = await switching.ConfigureAwait(true);
                    switching = null;
                    if (elevated is not null)
                    {
                        // Lo que pasó por la fuente vieja desde el último segundo también cuenta.
                        lastSessionTotal = calculator.Next(source.ReadCounters(), _time.GetElapsedTime(last)).TotalSessionBytes;
                        sessionBase += lastSessionTotal;
                        lastSessionTotal = 0;
                        source.Dispose();
                        source = elevated;
                        notice = HelperNotice;
                        calculator = new TrafficRateCalculator(top);
                        calculator.Next(source.ReadCounters(), _interval);
                    }
                    else
                    {
                        notice = Localization.L.F("Net_HelperFailed", error ?? "");
                    }
                    TaskCompletionSource<bool>? answered;
                    lock (_gate)
                    {
                        answered = _request;
                        _request = null;
                        _perProcessActive = elevated is not null;
                    }
                    answered?.TrySetResult(elevated is not null);
                }

                var (cpuNow, mem, counters, gpuNow) = await Task.Run(() => (_cpu(), _memory(), source.ReadCounters(), readGpu()),
                        cancellationToken)
                    .ConfigureAwait(true);
                var now = _time.GetTimestamp();
                var net = calculator.Next(counters, _time.GetElapsedTime(last, now));
                last = now;
                lastSessionTotal = net.TotalSessionBytes;

                const double GB = 1024d * 1024 * 1024;
                var snapshot = new SystemMetricsSnapshot(
                    _time.GetLocalNow(),
                    cpuNow ?? Latest?.CpuPercent ?? 0,
                    mem?.LoadPercent ?? 0,
                    mem is { } m ? (m.TotalBytes - m.AvailableBytes) / GB : 0,
                    mem is { } t ? t.TotalBytes / GB : 0,
                    net.TotalRxBytesPerSecond / 1024,
                    net.TotalTxBytesPerSecond / 1024,
                    sessionBase + net.TotalSessionBytes,
                    net.Top,
                    isAdmin,
                    source.Mode,
                    notice,
                    gpuNow.UsagePercent,
                    gpuNow.TemperatureC,
                    gpuNow.Name);
                Latest = snapshot;
                SnapshotUpdated?.Invoke(this, snapshot);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cierre normal, también si llega durante la primera lectura: el finally libera la fuente de red
            // (y detiene la sesión ETW o el auxiliar).
        }
        finally
        {
            source.Dispose();
            TaskCompletionSource<bool>? unanswered;
            lock (_gate)
            {
                _running = false;
                _perProcessActive = false;
                unanswered = _request;
                _request = null;
            }
            unanswered?.TrySetResult(false);   // nadie queda esperando un motor que ya no corre
            // Si el aviso seguía abierto al cerrar, el auxiliar que llegue tarde se libera igual.
            _ = switching?.ContinueWith(t => t.Result.Source?.Dispose(), TaskScheduler.Default);
        }
    }

    private static (ITrafficSource? Source, string? Error) TryStart(Func<ITrafficSource> factory)
    {
        ITrafficSource? source = null;
        try
        {
            source = factory();
            source.Start();
            return (source, null);
        }
        catch (Exception ex)
        {
            source?.Dispose();
            return (null, ex.Message);
        }
    }

    private (ITrafficSource Source, string Notice) OpenNetworkSource(bool isAdmin)
    {
        if (isAdmin)
        {
            ITrafficSource? etw = null;
            try
            {
                etw = _etwFactory();
                etw.Start();
                return (etw, AdministratorNotice);
            }
            catch (Exception ex)
            {
                etw?.Dispose();
                return (StartInterfaces(),
                    Localization.L.F("Net_EtwFailed", ex.Message));
            }
        }
        return (StartInterfaces(), StandardUserNotice);
    }

    private ITrafficSource StartInterfaces()
    {
        var source = _interfaceFactory();
        source.Start();
        return source;
    }
}

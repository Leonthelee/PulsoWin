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
    Func<GpuStatus>? gpu = null)
{
    /// <summary>Cada cuánto se publica una foto: 1 s para que las agujas se vean casi en vivo.</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(1);

    public const string StandardUserNotice =
        "Modo usuario estándar: para ver el consumo exacto en MB por proceso, ejecuta la app como Administrador.";
    public const string AdministratorNotice = "Modo administrador: consumo exacto por proceso (ETW del kernel).";

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
        var (source, notice) = await Task.Run(() => OpenNetworkSource(isAdmin), cancellationToken);
        using var gpuSampler = gpu is null ? new GpuSampler() : null;   // NVML se libera al terminar
        var readGpu = gpu ?? gpuSampler!.Sample;
        using (source)
        {
            var calculator = new TrafficRateCalculator(top);
            var last = _time.GetTimestamp();
            await Task.Run(() => { _cpu(); readGpu(); calculator.Next(source.ReadCounters(), _interval); }, cancellationToken); // referencias

            using var timer = new PeriodicTimer(_interval, _time);
            try
            {
                while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(true))
                {
                    var (cpuNow, mem, counters, gpuNow) = await Task.Run(() => (_cpu(), _memory(), source.ReadCounters(), readGpu()),
                            cancellationToken)
                        .ConfigureAwait(true);
                    var now = _time.GetTimestamp();
                    var net = calculator.Next(counters, _time.GetElapsedTime(last, now));
                    last = now;

                    const double GB = 1024d * 1024 * 1024;
                    var snapshot = new SystemMetricsSnapshot(
                        _time.GetLocalNow(),
                        cpuNow ?? Latest?.CpuPercent ?? 0,
                        mem?.LoadPercent ?? 0,
                        mem is { } m ? (m.TotalBytes - m.AvailableBytes) / GB : 0,
                        mem is { } t ? t.TotalBytes / GB : 0,
                        net.TotalRxBytesPerSecond / 1024,
                        net.TotalTxBytesPerSecond / 1024,
                        net.TotalSessionBytes,
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
                // Cierre normal: el using libera la fuente de red (y detiene la sesión ETW).
            }
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
                    $"No se pudo iniciar el monitoreo por proceso ({ex.Message}). Mostrando el tráfico por interfaz.");
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

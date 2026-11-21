using System.Security.Principal;

namespace PulsoWin.Core.Network;

/// <summary>
/// Monitor de red híbrido. Al arrancar detecta los privilegios:
/// - Administrador → ETW del kernel, exacto por proceso.
/// - Usuario estándar (o si ETW falla) → estadísticas por interfaz, sin errores.
/// Publica cada <c>interval</c> (2 s) un <see cref="NetworkSnapshot"/> con el top 10 por el evento
/// <see cref="SnapshotUpdated"/>, en el mismo contexto en que se llamó a <see cref="RunAsync"/>
/// (el hilo de la interfaz, si se llama desde ella). Al cancelar, libera la fuente y la sesión ETW.
/// </summary>
public sealed class NetworkMonitorService(
    Func<bool>? isAdministrator = null,
    Func<ITrafficSource>? etwFactory = null,
    Func<ITrafficSource>? interfaceFactory = null,
    TimeSpan? interval = null,
    int top = 10,
    TimeProvider? time = null)
{
    public const string StandardUserNotice =
        "Modo usuario estándar: para ver el consumo exacto en MB por proceso, ejecuta la app como Administrador.";
    public const string AdministratorNotice = "Modo administrador: consumo exacto por proceso (ETW del kernel).";

    private readonly Func<bool> _isAdministrator = isAdministrator ?? IsRunningAsAdministrator;
    private readonly Func<ITrafficSource> _etwFactory = etwFactory ?? (() => new EtwTrafficSource());
    private readonly Func<ITrafficSource> _interfaceFactory = interfaceFactory ?? (() => new InterfaceTrafficSource());
    private readonly TimeSpan _interval = interval ?? TimeSpan.FromSeconds(2);
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public event EventHandler<NetworkSnapshot>? SnapshotUpdated;

    public static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var (source, notice) = await Task.Run(OpenSource, cancellationToken);
        using (source)
        {
            var calculator = new TrafficRateCalculator(top);
            var last = _time.GetTimestamp();
            calculator.Next(await Task.Run(source.ReadCounters, cancellationToken), _interval); // referencia inicial
            SnapshotUpdated?.Invoke(this, new NetworkSnapshot(source.Mode, [], notice));

            using var timer = new PeriodicTimer(_interval, _time);
            try
            {
                while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(true))
                {
                    var counters = await Task.Run(source.ReadCounters, cancellationToken).ConfigureAwait(true);
                    var now = _time.GetTimestamp();
                    var items = calculator.Next(counters, _time.GetElapsedTime(last, now));
                    last = now;
                    SnapshotUpdated?.Invoke(this, new NetworkSnapshot(source.Mode, items, notice));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Cierre normal: el using libera la fuente (y detiene la sesión ETW).
            }
        }
    }

    private (ITrafficSource Source, string Notice) OpenSource()
    {
        if (_isAdministrator())
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
                var fallback = StartInterfaces();
                return (fallback, $"No se pudo iniciar el monitoreo por proceso ({ex.Message}). Mostrando el tráfico por interfaz.");
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

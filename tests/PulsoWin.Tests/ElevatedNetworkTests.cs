// PulsoWin · MIT · Co-designed with AI coding assistants (Claude & Gemini)
using System.Diagnostics;
using System.IO.Pipes;
using PulsoWin.Core.Metrics;
using PulsoWin.Core.Network;
using PulsoWin.Core.Sensors;
using PulsoWin.Presentation.ViewModels;

namespace PulsoWin.Tests;

/// <summary>
/// Red por proceso sin elevar la app (requisito 9.2): la mide un auxiliar elevado que solo envía cifras por una tubería.
/// </summary>
public class ElevatedNetworkTests
{
    private const string ValidPipe = "PulsoWin-Red-0123456789abcdef0123456789abcdef";
    private static readonly MemoryInfo FakeRam = new(16UL * 1024 * 1024 * 1024, 4UL * 1024 * 1024 * 1024, 75);

    private static SystemMetricsService Service(bool admin, ITrafficSource interfaces, Func<ITrafficSource>? perProcess) =>
        new(() => admin,
            () => throw new InvalidOperationException("ETW directo no debería usarse"),
            () => interfaces,
            TimeSpan.FromMilliseconds(40),
            cpu: () => 10,
            memory: () => FakeRam,
            gpu: () => new GpuStatus(null, null, null, "—"),
            perProcessFactory: perProcess);

    // ------------------------------------------------------------------ formato de la tubería

    [Fact]
    public void Wire_frames_round_trip_and_ignore_anything_that_is_not_four_numbers()
    {
        var text = new StringWriter();
        TrafficWire.WriteFrame(text, [new("a", 10, "chrome", 1000, 200), new("b", 4, "System", 50, 0, 2), new("c", null, "Wi-Fi", 9, 9)]);
        TrafficWire.WriteFrame(text, [new("a", 10, "chrome", 3000, 400)]);
        var written = text.ToString();
        var wire = written + "1 2 3\n-5 0 1 1\n7 0 x 1\n8 0 1 1 1\n9 1 9 9\n\n";   // de estas, solo «9 1 9 9» es válida

        var frames = TrafficWire.ReadFrames(new StringReader(wire)).ToList();

        Assert.Equal(3, frames.Count);
        Assert.Equal([new WireRow(10, 0, 1000, 200), new WireRow(4, 2, 50, 0)], frames[0]);   // una fila sin PID (interfaz) no viaja
        Assert.Equal([new WireRow(10, 0, 3000, 400)], frames[1]);
        Assert.Equal([new WireRow(9, 1, 9, 9)], frames[2]);
        Assert.DoesNotContain("chrome", written);                           // solo números, nunca nombres
    }

    [Theory]
    [InlineData(ValidPipe, true)]
    [InlineData("PulsoWin-Red-0123456789abcdef0123456789abcde", false)]    // GUID incompleto
    [InlineData("PulsoWin-Red-..\\..\\pipe\\otra", false)]
    [InlineData("OtraApp-0123456789abcdef0123456789abcdef", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Helper_only_accepts_its_own_random_pipe_names(string? name, bool valid)
    {
        Assert.Equal(valid, ElevatedTrafficSource.IsValidPipeName(name));
    }

    public static TheoryData<string[]> BadHelperArgs() => new(
    [
        [],
        ["--net-helper"],
        ["--net-helper", "PulsoWin-Red-xyz", "123"],
        ["--net-helper", ValidPipe, "no-es-pid"],
        ["--net-helper", ValidPipe, "1", "extra"],
        ["--otra-cosa", ValidPipe, "1"],
    ]);

    [Theory]
    [MemberData(nameof(BadHelperArgs))]
    public void Helper_refuses_bad_arguments_before_touching_ETW(string[] args)
    {
        Assert.Equal(2, NetworkHelper.Run(args, () => throw new InvalidOperationException("no debió abrir ETW")));
    }

    /// <summary>Un proceso legible y de vida corta, con otro ejecutable (cmd.exe).</summary>
    private static Process OtherProcess(string command = "ping -n 6 127.0.0.1 >nul") =>
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c {command}") { CreateNoWindow = true, UseShellExecute = false })!;

    [Fact]
    public void Helper_refuses_a_parent_that_is_not_PulsoWin()
    {
        // cmd.exe se puede leer (mismo usuario) y es otro ejecutable: se ejercita la comparación de rutas.
        using var other = OtherProcess();
        try
        {
            Assert.Equal(3, NetworkHelper.Run(["--net-helper", ValidPipe, other.Id.ToString()],
                () => throw new InvalidOperationException("no debió abrir ETW")));
        }
        finally { other.Kill(); }
    }

    [Fact]
    public async Task Helper_only_writes_to_a_pipe_held_by_the_app_named_in_its_arguments()
    {
        // La tubería la abre ESTE proceso, pero el auxiliar dice trabajar para otro: no le manda nada.
        var name = "PulsoWin-Red-" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var waiting = server.WaitForConnectionAsync();
        using var other = OtherProcess();
        try
        {
            var code = await Task.Run(() => NetworkHelper.Run(["--net-helper", name, other.Id.ToString()],
                () => throw new InvalidOperationException("no debió abrir ETW"), isSameExecutable: _ => true));
            Assert.Equal(5, code);
        }
        finally { other.Kill(); }
        await waiting;
    }

    // ------------------------------------------------------------------ tubería real, auxiliar simulado

    [Fact]
    public async Task Source_reads_what_the_helper_sends_through_a_private_pipe()
    {
        Task? helper = null;
        var source = new ElevatedTrafficSource(pipe =>
        {
            helper = Task.Run(() =>
            {
                using var client = new NamedPipeClientStream(".", pipe, PipeDirection.Out);
                client.Connect(5000);
                using var writer = new StreamWriter(client) { NewLine = "\n" };
                TrafficWire.WriteFrame(writer, [new("x", Environment.ProcessId, "", 4096, 1024, 3)]);
                Thread.Sleep(300);
            });
            return Process.GetCurrentProcess();   // el «auxiliar» es este proceso: el PID del cliente coincide
        }, TimeSpan.FromSeconds(5));

        source.Start();
        IReadOnlyList<TrafficCounter> counters = [];
        for (var i = 0; i < 50 && counters.Count == 0; i++)
        {
            await Task.Delay(20);
            counters = source.ReadCounters();
        }
        source.Dispose();
        await helper!;

        var row = Assert.Single(counters);
        Assert.Equal(Environment.ProcessId, row.Pid);
        Assert.Equal(4096, row.RxTotal);
        Assert.Equal(1024, row.TxTotal);
        Assert.Equal(3, row.Generation);
        Assert.Equal($"pid:{Environment.ProcessId}:3", row.Key);                    // PID reutilizado = otra fila
        Assert.Equal(Process.GetCurrentProcess().ProcessName, row.Name);   // el nombre se pone de este lado
        Assert.Equal(NetworkMonitorMode.PerProcess, source.Mode);
    }

    [Fact]
    public void Declining_the_administrator_prompt_fails_cleanly()
    {
        using var source = new ElevatedTrafficSource(_ => null, TimeSpan.FromSeconds(1));
        var ex = Assert.Throws<UnauthorizedAccessException>(source.Start);
        Assert.Equal("no se concedió el permiso de administrador", ex.Message);
    }

    [Fact]
    public void A_helper_that_never_connects_times_out()
    {
        using var source = new ElevatedTrafficSource(_ => Process.GetCurrentProcess(), TimeSpan.FromMilliseconds(200));
        Assert.Throws<TimeoutException>(source.Start);
    }

    [Fact]
    public void A_helper_that_dies_before_connecting_is_reported_at_once_not_after_the_timeout()
    {
        using var source = new ElevatedTrafficSource(_ => OtherProcess("exit 7"), TimeSpan.FromSeconds(30));
        var watch = Stopwatch.StartNew();
        var ex = Assert.Throws<InvalidOperationException>(source.Start);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"esperó {watch.Elapsed}");
        Assert.Equal("el proceso auxiliar terminó antes de conectarse (código 7)", ex.Message);
    }

    [Fact]
    public async Task Only_the_launched_helper_may_feed_the_pipe()
    {
        // Lo «lanzado» es cmd.exe, pero quien se conecta es este proceso: la app rechaza esas cifras.
        // (La fuente se queda con el Process y lo libera; para cerrar cmd.exe se usa otro objeto con su PID.)
        var launched = OtherProcess();
        var launchedId = launched.Id;
        Task? intruder = null;
        try
        {
            using var source = new ElevatedTrafficSource(pipe =>
            {
                intruder = Task.Run(() =>
                {
                    using var client = new NamedPipeClientStream(".", pipe, PipeDirection.Out);
                    client.Connect(5000);
                    Thread.Sleep(200);
                });
                return launched;
            }, TimeSpan.FromSeconds(5));
            var ex = Assert.Throws<UnauthorizedAccessException>(source.Start);
            Assert.Equal("se conectó un proceso que no es el auxiliar lanzado por PulsoWin", ex.Message);
        }
        finally
        {
            try { using var cmd = Process.GetProcessById(launchedId); cmd.Kill(); }
            catch (ArgumentException) { /* ya terminó */ }
        }
        await intruder!.ContinueWith(_ => { });
    }

    [Fact]
    public async Task When_the_helper_goes_away_the_source_faults_and_forgets_old_figures()
    {
        using var hold = new ManualResetEventSlim();
        Task? helper = null;
        using var source = new ElevatedTrafficSource(pipe =>
        {
            helper = Task.Run(() =>
            {
                using var client = new NamedPipeClientStream(".", pipe, PipeDirection.Out);
                client.Connect(5000);
                using var writer = new StreamWriter(client) { NewLine = "\n" };
                TrafficWire.WriteFrame(writer, [new("x", 4, "", 10, 10)]);
                hold.Wait(5000);
            });
            return Process.GetCurrentProcess();
        }, TimeSpan.FromSeconds(5));
        source.Start();
        for (var i = 0; i < 50 && source.ReadCounters().Count == 0; i++) await Task.Delay(20);
        Assert.False(source.IsFaulted);

        hold.Set();                                   // el auxiliar se cierra
        await helper!;
        for (var i = 0; i < 50 && !source.IsFaulted; i++) await Task.Delay(20);

        Assert.True(source.IsFaulted);
        Assert.Empty(source.ReadCounters());          // nada de cifras viejas haciéndose pasar por actuales
    }

    // ------------------------------------------------------------------ motor: cambio en vivo

    [Fact]
    public async Task Asking_for_per_process_switches_the_live_engine_without_elevating_the_app()
    {
        var interfaces = new FakeTrafficSource(NetworkMonitorMode.PerInterface);
        var helper = new FakeTrafficSource(NetworkMonitorMode.PerProcess);
        var service = Service(admin: false, interfaces, () => helper);
        var snapshots = new List<SystemMetricsSnapshot>();
        service.SnapshotUpdated += (_, s) => { lock (snapshots) snapshots.Add(s); };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var run = service.RunAsync(cts.Token);

        await Task.Delay(120);
        Assert.True(service.CanRequestPerProcess);
        Assert.True(await service.RequestPerProcessAsync());
        await Task.Delay(120);
        cts.Cancel();
        await run;

        Assert.Equal(NetworkMonitorMode.PerInterface, snapshots[0].NetworkMode);
        Assert.StartsWith("Modo usuario estándar: tráfico por interfaz. Pulsa «Ver por proceso»", snapshots[0].NetworkNotice);
        var last = snapshots[^1];
        Assert.Equal(NetworkMonitorMode.PerProcess, last.NetworkMode);
        Assert.Equal(SystemMetricsService.HelperNotice, last.NetworkNotice);
        Assert.False(last.IsAdminMode);                                     // la app nunca se elevó
        Assert.Equal("steam", last.TopNetworkProcesses[0].Process);
        Assert.True(interfaces.Disposed);                                   // la fuente anterior se liberó al cambiar
        Assert.True(helper.Disposed);                                       // y el auxiliar, al cerrar
    }

    [Fact]
    public async Task A_refused_prompt_keeps_the_interface_view_and_says_why()
    {
        var interfaces = new FakeTrafficSource(NetworkMonitorMode.PerInterface);
        var service = Service(admin: false, interfaces,
            () => new FakeTrafficSource(NetworkMonitorMode.PerProcess, failOnStart: true));
        SystemMetricsSnapshot? last = null;
        service.SnapshotUpdated += (_, s) => last = s;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var run = service.RunAsync(cts.Token);

        await Task.Delay(100);
        Assert.False(await service.RequestPerProcessAsync());
        await Task.Delay(100);
        cts.Cancel();
        await run;

        Assert.Equal(NetworkMonitorMode.PerInterface, last!.NetworkMode);
        Assert.Equal("No se pudo activar la red por proceso (sin permisos para ETW). Sigue el tráfico por interfaz.", last.NetworkNotice);
        Assert.True(interfaces.Disposed);                                   // liberada al cerrar
    }

    [Fact]
    public void An_app_that_is_already_administrator_does_not_offer_the_helper()
    {
        static ITrafficSource Unused() => throw new InvalidOperationException();
        Assert.True(Service(admin: false, new FakeTrafficSource(NetworkMonitorMode.PerInterface), Unused).CanRequestPerProcess);
        Assert.False(Service(admin: true, new FakeTrafficSource(NetworkMonitorMode.PerInterface), Unused).CanRequestPerProcess);
        Assert.False(Service(admin: false, new FakeTrafficSource(NetworkMonitorMode.PerInterface), null).CanRequestPerProcess);
    }

    [Fact]
    public async Task The_button_shows_only_while_the_view_is_per_interface()
    {
        var helper = new FakeTrafficSource(NetworkMonitorMode.PerProcess);
        var service = Service(admin: false, new FakeTrafficSource(NetworkMonitorMode.PerInterface), () => helper);
        var vm = new MetricsViewModel(service);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var run = vm.RunAsync(cts.Token);

        await Task.Delay(120);
        Assert.True(vm.ShowPerProcessButton);
        Assert.True(vm.RequestPerProcessCommand.CanExecute(null));
        await vm.RequestPerProcessCommand.ExecuteAsync(null);
        await Task.Delay(120);
        Assert.True(vm.IsPerProcess);
        Assert.False(vm.ShowPerProcessButton);
        Assert.False(vm.RequestPerProcessCommand.CanExecute(null));

        cts.Cancel();
        await run;
    }

    [Fact]
    public async Task If_the_helper_dies_the_engine_goes_back_to_interfaces_and_the_session_total_never_drops()
    {
        var helper = new SwitchableSource(NetworkMonitorMode.PerProcess);
        var service = Service(admin: false, new SwitchableSource(NetworkMonitorMode.PerInterface),
            () => helper);
        var snapshots = new List<SystemMetricsSnapshot>();
        service.SnapshotUpdated += (_, s) => { lock (snapshots) snapshots.Add(s); };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var run = service.RunAsync(cts.Token);

        await Task.Delay(200);
        Assert.True(await service.RequestPerProcessAsync());
        await Task.Delay(200);
        helper.Faulted = true;                                              // el auxiliar se cerró
        await Task.Delay(200);
        cts.Cancel();
        await run;

        Assert.Contains(snapshots, s => s.NetworkMode == NetworkMonitorMode.PerProcess);
        var last = snapshots[^1];
        Assert.Equal(NetworkMonitorMode.PerInterface, last.NetworkMode);
        Assert.Equal("Se cortó la medición por proceso (el proceso auxiliar se cerró). Volviendo al tráfico por interfaz.",
            last.NetworkNotice);
        Assert.True(helper.Disposed);
        for (var i = 1; i < snapshots.Count; i++)                           // ni al pasar a por proceso ni al volver
            Assert.True(snapshots[i].NetSessionTotalBytes >= snapshots[i - 1].NetSessionTotalBytes,
                $"el total bajó de {snapshots[i - 1].NetSessionTotalBytes} a {snapshots[i].NetSessionTotalBytes}");
        Assert.True(service.CanRequestPerProcess);                          // se puede volver a pedir
    }

    [Fact]
    public async Task Clicking_again_while_the_prompt_is_open_does_not_launch_a_second_helper()
    {
        var starts = 0;
        var service = Service(admin: false, new SwitchableSource(NetworkMonitorMode.PerInterface), () =>
        {
            Interlocked.Increment(ref starts);
            return new SwitchableSource(NetworkMonitorMode.PerProcess) { StartDelay = TimeSpan.FromMilliseconds(300) };
        });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var run = service.RunAsync(cts.Token);
        await Task.Delay(100);

        var first = service.RequestPerProcessAsync();
        await Task.Delay(150);                                              // el «aviso» sigue abierto
        var second = service.RequestPerProcessAsync();
        Assert.Same(first, second);
        Assert.True(await first);
        Assert.True(await service.RequestPerProcessAsync());                // ya activo: responde sin lanzar otro
        cts.Cancel();
        await run;

        Assert.Equal(1, starts);
    }

    [Fact]
    public async Task Closing_while_the_engine_is_still_starting_ends_quietly()
    {
        var service = Service(admin: false, new SwitchableSource(NetworkMonitorMode.PerInterface),
            () => new SwitchableSource(NetworkMonitorMode.PerProcess));
        using var cts = new CancellationTokenSource();
        cts.Cancel();   // la ventana se cierra antes de la primera lectura (pasaba con el equipo cargado)

        await service.RunAsync(cts.Token);   // no debe lanzar OperationCanceledException

        Assert.False(await service.RequestPerProcessAsync().WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task Asking_when_the_engine_is_not_running_answers_no_instead_of_waiting_forever()
    {
        var service = Service(admin: false, new SwitchableSource(NetworkMonitorMode.PerInterface),
            () => new SwitchableSource(NetworkMonitorMode.PerProcess));
        Assert.False(await service.RequestPerProcessAsync().WaitAsync(TimeSpan.FromSeconds(2)));   // antes de arrancar

        using var cts = new CancellationTokenSource();
        var run = service.RunAsync(cts.Token);
        await Task.Delay(60);
        cts.Cancel();
        await run;
        Assert.False(await service.RequestPerProcessAsync().WaitAsync(TimeSpan.FromSeconds(2)));   // después de cerrar
    }
}

/// <summary>Fuente falsa con tráfico creciente, arranque lento opcional y falla a pedido.</summary>
internal sealed class SwitchableSource(NetworkMonitorMode mode) : ITrafficSource
{
    private long _reads;
    public TimeSpan StartDelay { get; init; }
    public volatile bool Faulted;
    public bool Disposed { get; private set; }
    public NetworkMonitorMode Mode => mode;
    public bool IsFaulted => Faulted;
    public void Start() => Thread.Sleep(StartDelay);

    public IReadOnlyList<TrafficCounter> ReadCounters()
    {
        var n = Interlocked.Increment(ref _reads);
        return [new TrafficCounter("a", 10, "steam", n * 1000, n * 10)];
    }

    public void Dispose() => Disposed = true;
}

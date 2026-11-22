// PulsoWin · MIT · Co-designed with AI coding assistants (Claude & Gemini)
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
    public void Wire_frames_round_trip_and_ignore_anything_that_is_not_three_numbers()
    {
        var text = new StringWriter();
        TrafficWire.WriteFrame(text, [new("a", 10, "chrome", 1000, 200), new("b", 4, "System", 50, 0), new("c", null, "Wi-Fi", 9, 9)]);
        TrafficWire.WriteFrame(text, [new("a", 10, "chrome", 3000, 400)]);
        var written = text.ToString();
        var wire = written + "1 2\n-5 1 1\n7 x 1\n8 1 1 1\n9 9 9\n\n";   // de estas, solo «9 9 9» es válida

        var frames = TrafficWire.ReadFrames(new StringReader(wire)).ToList();

        Assert.Equal(3, frames.Count);
        Assert.Equal([(10, 1000L, 200L), (4, 50L, 0L)], frames[0]);          // una fila sin PID (interfaz) no viaja
        Assert.Equal([(10, 3000L, 400L)], frames[1]);
        Assert.Equal([(9, 9L, 9L)], frames[2]);
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

    [Fact]
    public void Helper_refuses_a_parent_that_is_not_PulsoWin()
    {
        // El PID 4 es «System»: no es este ejecutable, así que el auxiliar no mide nada para él.
        Assert.Equal(3, NetworkHelper.Run(["--net-helper", ValidPipe, "4"],
            () => throw new InvalidOperationException("no debió abrir ETW")));
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
                TrafficWire.WriteFrame(writer, [new("x", Environment.ProcessId, "", 4096, 1024)]);
                Thread.Sleep(300);
            });
            return true;
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
        Assert.Equal(System.Diagnostics.Process.GetCurrentProcess().ProcessName, row.Name);   // el nombre se pone de este lado
        Assert.Equal(NetworkMonitorMode.PerProcess, source.Mode);
    }

    [Fact]
    public void Declining_the_administrator_prompt_fails_cleanly()
    {
        using var source = new ElevatedTrafficSource(_ => false, TimeSpan.FromSeconds(1));
        var ex = Assert.Throws<UnauthorizedAccessException>(source.Start);
        Assert.Equal("no se concedió el permiso de administrador", ex.Message);
    }

    [Fact]
    public void A_helper_that_never_connects_times_out()
    {
        using var source = new ElevatedTrafficSource(_ => true, TimeSpan.FromMilliseconds(200));
        Assert.Throws<TimeoutException>(source.Start);
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
}

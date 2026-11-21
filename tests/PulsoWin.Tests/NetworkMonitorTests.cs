using PulsoWin.Core.Network;
using PulsoWin.Presentation.ViewModels;

namespace PulsoWin.Tests;

/// <summary>Fuente falsa: cada lectura suma tráfico; registra si se inició y si se liberó.</summary>
internal sealed class FakeTrafficSource(NetworkMonitorMode mode, bool failOnStart = false) : ITrafficSource
{
    private long _reads;
    public bool Started { get; private set; }
    public bool Disposed { get; private set; }
    public NetworkMonitorMode Mode => mode;

    public void Start()
    {
        if (failOnStart) throw new UnauthorizedAccessException("sin permisos para ETW");
        Started = true;
    }

    public IReadOnlyList<TrafficCounter> ReadCounters()
    {
        var n = Interlocked.Increment(ref _reads);
        return [new TrafficCounter("a", 10, "chrome", n * 1000, n * 100), new TrafficCounter("b", 20, "steam", n * 5000, 0)];
    }

    public void Dispose() => Disposed = true;
}

public class NetworkMonitorTests
{
    [Theory]
    [InlineData(0, "0.0 KB/s")]
    [InlineData(1536, "1.5 KB/s")]
    [InlineData(1023 * 1024, "1023.0 KB/s")]
    [InlineData(1024 * 1024, "1.0 MB/s")]
    [InlineData(5 * 1024 * 1024 + 512 * 1024, "5.5 MB/s")]
    [InlineData(-10, "0.0 KB/s")]
    public void Rates_switch_to_MB_per_second_from_1024_KB(double bytesPerSecond, string expected)
    {
        Assert.Equal(expected, Rates.Format(bytesPerSecond).Replace(',', '.'));
    }

    [Fact]
    public void Calculator_computes_rate_from_delta_and_sorts_by_current_usage()
    {
        var calc = new TrafficRateCalculator();
        var first = calc.Next([new("a", 1, "lento", 1000, 0), new("b", 2, "rapido", 0, 0)], TimeSpan.FromSeconds(2));
        Assert.All(first, i => Assert.Equal(0, i.CurrentBytesPerSecond)); // primera lectura: solo referencia

        var second = calc.Next([new("a", 1, "lento", 3000, 0), new("b", 2, "rapido", 8000, 2000)], TimeSpan.FromSeconds(2));

        Assert.Equal("rapido", second[0].Process);
        Assert.Equal(4000, second[0].RxBytesPerSecond);   // (8000 − 0) / 2 s
        Assert.Equal(1000, second[0].TxBytesPerSecond);
        Assert.Equal(1000, second[1].RxBytesPerSecond);   // (3000 − 1000) / 2 s
        Assert.Equal(10000, second[0].SessionTotalBytes); // RX + TX acumulados en la sesión
    }

    [Fact]
    public void Calculator_returns_strictly_the_top_10()
    {
        var calc = new TrafficRateCalculator();
        var counters = Enumerable.Range(1, 15).Select(i => new TrafficCounter($"k{i}", i, $"p{i}", 0, 0)).ToList();
        calc.Next(counters, TimeSpan.FromSeconds(2));
        var next = Enumerable.Range(1, 15).Select(i => new TrafficCounter($"k{i}", i, $"p{i}", i * 1000L, 0)).ToList();

        var top = calc.Next(next, TimeSpan.FromSeconds(1));

        Assert.Equal(10, top.Count);
        Assert.Equal("p15", top[0].Process);
        Assert.Equal("p6", top[^1].Process);
        Assert.True(top.Zip(top.Skip(1)).All(p => p.First.CurrentBytesPerSecond >= p.Second.CurrentBytesPerSecond));
    }

    [Fact]
    public void Calculator_never_reports_negative_speed_when_a_counter_goes_back()
    {
        var calc = new TrafficRateCalculator();
        calc.Next([new("a", 7, "x", 5000, 5000)], TimeSpan.FromSeconds(2));
        var after = calc.Next([new("a", 7, "x", 100, 100)], TimeSpan.FromSeconds(2)); // PID reutilizado / interfaz reiniciada
        Assert.Equal(0, after[0].CurrentBytesPerSecond);
    }

    private static async Task<List<NetworkSnapshot>> RunUntil(NetworkMonitorService service, int snapshots)
    {
        var received = new List<NetworkSnapshot>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        service.SnapshotUpdated += (_, s) =>
        {
            lock (received) received.Add(s);
            if (received.Count >= snapshots) cts.Cancel();
        };
        await service.RunAsync(cts.Token);
        return received;
    }

    [Fact]
    public async Task Administrator_uses_ETW_per_process_and_releases_it_on_close()
    {
        var etw = new FakeTrafficSource(NetworkMonitorMode.PerProcess);
        var service = new NetworkMonitorService(() => true, () => etw,
            () => throw new InvalidOperationException("no debería usarse"), TimeSpan.FromMilliseconds(40));

        var snapshots = await RunUntil(service, 3);

        Assert.All(snapshots, s => Assert.Equal(NetworkMonitorMode.PerProcess, s.Mode));
        Assert.Equal(NetworkMonitorService.AdministratorNotice, snapshots[0].Notice);
        Assert.Equal("steam", snapshots[^1].Items[0].Process);   // 5000 B por lectura > 1100 B de chrome
        Assert.True(etw.Started);
        Assert.True(etw.Disposed);                                 // sesión ETW cerrada al cancelar
    }

    [Fact]
    public async Task Standard_user_falls_back_to_interfaces_with_the_exact_notice()
    {
        var interfaces = new FakeTrafficSource(NetworkMonitorMode.PerInterface);
        var service = new NetworkMonitorService(() => false,
            () => throw new InvalidOperationException("ETW no debe intentarse sin administrador"),
            () => interfaces, TimeSpan.FromMilliseconds(40));

        var snapshots = await RunUntil(service, 2);

        Assert.All(snapshots, s => Assert.Equal(NetworkMonitorMode.PerInterface, s.Mode));
        Assert.Equal("Modo usuario estándar: para ver el consumo exacto en MB por proceso, ejecuta la app como Administrador.",
            snapshots[0].Notice);
        Assert.True(interfaces.Disposed);
    }

    [Fact]
    public async Task If_ETW_fails_even_as_admin_it_falls_back_without_crashing()
    {
        var etw = new FakeTrafficSource(NetworkMonitorMode.PerProcess, failOnStart: true);
        var interfaces = new FakeTrafficSource(NetworkMonitorMode.PerInterface);
        var service = new NetworkMonitorService(() => true, () => etw, () => interfaces, TimeSpan.FromMilliseconds(40));

        var snapshots = await RunUntil(service, 2);

        Assert.Equal(NetworkMonitorMode.PerInterface, snapshots[0].Mode);
        Assert.StartsWith("No se pudo iniciar el monitoreo por proceso", snapshots[0].Notice);
        Assert.True(etw.Disposed);        // la sesión fallida también se libera
        Assert.True(interfaces.Disposed);
    }

    [Fact]
    public void Driver_filter_copies_of_an_interface_are_not_listed_twice()
    {
        // Lista real observada en Windows 11 (sin administrador).
        string[] names =
        [
            "Ethernet",
            "Ethernet-QoS Packet Scheduler-0000",
            "Ethernet-WFP 802.3 MAC Layer LightWeight Filter-0000",
            "Ethernet-WFP Native MAC Layer LightWeight Filter-0000",
            "vEthernet (Default Switch)",
            "vEthernet (Default Switch)-QoS Packet Scheduler-0000",
            "vEthernet (WSL (Hyper-V firewall))",
            "Wi-Fi",                                   // el guion propio del nombre no la descarta
        ];

        Assert.Equal(new[] { "Ethernet", "vEthernet (Default Switch)", "vEthernet (WSL (Hyper-V firewall))", "Wi-Fi" },
            InterfaceTrafficSource.WithoutFilterCopies(names));
    }

    [Fact]
    public void Real_interface_source_reads_without_admin_and_without_throwing()
    {
        using var source = new InterfaceTrafficSource();
        source.Start();
        var counters = source.ReadCounters();
        Assert.All(counters, c =>
        {
            Assert.Null(c.Pid);
            Assert.True(c.RxTotal >= 0 && c.TxTotal >= 0);
        });
    }

    [Fact]
    public async Task View_model_keeps_at_most_10_rows_and_shows_the_mode()
    {
        var interfaces = new FakeTrafficSource(NetworkMonitorMode.PerInterface);
        var vm = new NetworkViewModel(new NetworkMonitorService(() => false, null, () => interfaces, TimeSpan.FromMilliseconds(40)));
        using var cts = new CancellationTokenSource();
        var run = vm.RunAsync(cts.Token);
        await Task.Delay(300);
        await cts.CancelAsync();
        await run;

        Assert.InRange(vm.Items.Count, 1, 10);
        Assert.False(vm.IsPerProcess);
        Assert.Equal("POR INTERFAZ", vm.ModeText);
        Assert.Equal(NetworkMonitorService.StandardUserNotice, vm.Notice);
    }
}

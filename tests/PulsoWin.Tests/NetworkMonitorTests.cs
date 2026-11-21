using PulsoWin.Core.Metrics;
using PulsoWin.Core.Network;
using PulsoWin.Core.Sensors;
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
    private static readonly MemoryInfo FakeRam = new(16UL * 1024 * 1024 * 1024, 4UL * 1024 * 1024 * 1024, 75);

    private static SystemMetricsService Service(bool admin, ITrafficSource? etw, ITrafficSource interfaces, double? cpu = 42.5) =>
        new(() => admin,
            () => etw ?? throw new InvalidOperationException("ETW no debería usarse"),
            () => interfaces,
            TimeSpan.FromMilliseconds(40),
            cpu: () => cpu,
            memory: () => FakeRam);

    private static async Task<List<SystemMetricsSnapshot>> RunUntil(SystemMetricsService service, int snapshots)
    {
        var received = new List<SystemMetricsSnapshot>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        service.SnapshotUpdated += (_, s) =>
        {
            lock (received) received.Add(s);
            if (received.Count >= snapshots) cts.Cancel();
        };
        await service.RunAsync(cts.Token);
        return received;
    }

    // ------------------------------------------------------------------ formato y cálculo

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
    public void Calculator_computes_rate_from_delta_sorts_and_totals_everything()
    {
        var calc = new TrafficRateCalculator(top: 1);
        var first = calc.Next([new("a", 1, "lento", 1000, 0), new("b", 2, "rapido", 0, 0)], TimeSpan.FromSeconds(2));
        Assert.All(first.Top, i => Assert.Equal(0, i.CurrentBytesPerSecond)); // primera lectura: solo referencia

        var second = calc.Next([new("a", 1, "lento", 3000, 0), new("b", 2, "rapido", 8000, 2000)], TimeSpan.FromSeconds(2));

        var only = Assert.Single(second.Top);
        Assert.Equal("rapido", only.Process);
        Assert.Equal(4000, only.RxBytesPerSecond);           // (8000 − 0) / 2 s
        Assert.Equal(1000, only.TxBytesPerSecond);
        Assert.Equal(5000, second.TotalRxBytesPerSecond);    // los totales incluyen lo que quedó fuera del top
        Assert.Equal(13000, second.TotalSessionBytes);
    }

    [Fact]
    public void Calculator_returns_strictly_the_top_10()
    {
        var calc = new TrafficRateCalculator();
        calc.Next(Enumerable.Range(1, 15).Select(i => new TrafficCounter($"k{i}", i, $"p{i}", 0, 0)).ToList(), TimeSpan.FromSeconds(2));
        var top = calc.Next(Enumerable.Range(1, 15).Select(i => new TrafficCounter($"k{i}", i, $"p{i}", i * 1000L, 0)).ToList(),
            TimeSpan.FromSeconds(1)).Top;

        Assert.Equal(10, top.Count);
        Assert.Equal("p15", top[0].Process);
        Assert.Equal("p6", top[^1].Process);
    }

    [Fact]
    public void Calculator_never_reports_negative_speed_when_a_counter_goes_back()
    {
        var calc = new TrafficRateCalculator();
        calc.Next([new("a", 7, "x", 5000, 5000)], TimeSpan.FromSeconds(2));
        Assert.Equal(0, calc.Next([new("a", 7, "x", 100, 100)], TimeSpan.FromSeconds(2)).Top[0].CurrentBytesPerSecond);
    }

    [Fact]
    public void Cpu_is_the_busy_share_of_kernel_plus_user_time()
    {
        var cpu = new CpuSampler();
        Assert.Null(cpu.Next(idle: 100, kernel: 300, user: 100));          // primera lectura: referencia
        // +100 de kernel (incluye 50 inactivos) y +100 de usuario → 150 de 200 ocupados = 75 %.
        Assert.Equal(75, cpu.Next(idle: 150, kernel: 400, user: 200));
        Assert.Equal(0, cpu.Next(idle: 250, kernel: 500, user: 200));        // todo inactivo
    }

    // ------------------------------------------------------------------ motor único

    [Fact]
    public async Task Administrator_uses_ETW_per_process_and_releases_it_on_close()
    {
        var etw = new FakeTrafficSource(NetworkMonitorMode.PerProcess);
        var snapshots = await RunUntil(Service(admin: true, etw, new FakeTrafficSource(NetworkMonitorMode.PerInterface)), 3);

        var last = snapshots[^1];
        Assert.True(last.IsAdminMode);
        Assert.Equal(NetworkMonitorMode.PerProcess, last.NetworkMode);
        Assert.Equal(SystemMetricsService.AdministratorNotice, last.NetworkNotice);
        Assert.Equal("steam", last.TopNetworkProcesses[0].Process);
        Assert.Equal(42.5, last.CpuPercent);
        Assert.Equal(75, last.RamPercent);
        Assert.Equal(12, last.RamUsedGB, 3);
        Assert.Equal(16, last.RamTotalGB, 3);
        Assert.True(last.NetRxKBps > 0);
        Assert.True(etw.Disposed);                                 // sesión ETW cerrada al cancelar
    }

    [Fact]
    public async Task Standard_user_falls_back_to_interfaces_with_the_exact_notice()
    {
        var interfaces = new FakeTrafficSource(NetworkMonitorMode.PerInterface);
        var snapshots = await RunUntil(Service(admin: false, etw: null, interfaces), 2);

        Assert.All(snapshots, s => Assert.False(s.IsAdminMode));
        Assert.Equal("Modo usuario estándar: para ver el consumo exacto en MB por proceso, ejecuta la app como Administrador.",
            snapshots[0].NetworkNotice);
        Assert.True(interfaces.Disposed);
    }

    [Fact]
    public async Task If_ETW_fails_even_as_admin_it_falls_back_without_crashing()
    {
        var etw = new FakeTrafficSource(NetworkMonitorMode.PerProcess, failOnStart: true);
        var interfaces = new FakeTrafficSource(NetworkMonitorMode.PerInterface);
        var snapshots = await RunUntil(Service(admin: true, etw, interfaces), 2);

        Assert.True(snapshots[0].IsAdminMode);
        Assert.Equal(NetworkMonitorMode.PerInterface, snapshots[0].NetworkMode);
        Assert.StartsWith("No se pudo iniciar el monitoreo por proceso", snapshots[0].NetworkNotice);
        Assert.True(etw.Disposed && interfaces.Disposed);
    }

    [Fact]
    public async Task Two_views_share_one_engine_without_restarting_it()
    {
        var interfaces = new FakeTrafficSource(NetworkMonitorMode.PerInterface);
        var service = Service(admin: false, etw: null, interfaces);
        var fullApp = new MetricsViewModel(service);
        using var cts = new CancellationTokenSource();
        var run = fullApp.RunAsync(cts.Token);
        await Task.Delay(250);

        // El gadget se conecta a la MISMA fuente: Latest ya tiene datos sin reiniciar nada.
        Assert.NotNull(service.Latest);
        var gadget = new MetricsViewModel(service);
        gadget.Apply(service.Latest!);
        Assert.Equal(fullApp.CpuText, gadget.CpuText);
        Assert.Equal(1, interfaces.Started ? 1 : 0);

        await cts.CancelAsync();
        await run;
        Assert.True(interfaces.Disposed);
    }

    [Fact]
    public void Real_interface_source_reads_without_admin_and_without_throwing()
    {
        using var source = new InterfaceTrafficSource();
        source.Start();
        Assert.All(source.ReadCounters(), c =>
        {
            Assert.Null(c.Pid);
            Assert.True(c.RxTotal >= 0 && c.TxTotal >= 0);
        });
    }

    [Fact]
    public void Driver_filter_copies_of_an_interface_are_not_listed_twice()
    {
        string[] names =
        [
            "Ethernet", "Ethernet-QoS Packet Scheduler-0000", "Ethernet-WFP 802.3 MAC Layer LightWeight Filter-0000",
            "vEthernet (Default Switch)", "vEthernet (Default Switch)-QoS Packet Scheduler-0000", "Wi-Fi",
        ];
        Assert.Equal(new[] { "Ethernet", "vEthernet (Default Switch)", "Wi-Fi" }, InterfaceTrafficSource.WithoutFilterCopies(names));
    }

    // ------------------------------------------------------------------ vista: diales y escala

    [Theory]
    [InlineData(0, 64d * 1024)]
    [InlineData(50d * 1024, 64d * 1024)]
    [InlineData(300d * 1024, 1d * 1024 * 1024)]
    [InlineData(1.3 * 1024 * 1024, 4d * 1024 * 1024)]
    [InlineData(5000d * 1024 * 1024, 1024d * 1024 * 1024)]   // por encima del último escalón: se queda en el máximo
    public void Network_scale_picks_the_smallest_round_step_that_fits(double bytesPerSecond, double expected)
    {
        Assert.Equal(expected, new NetworkScale().Next(bytesPerSecond));
    }

    [Fact]
    public void Network_scale_goes_up_at_once_and_down_only_after_the_peak_is_forgotten()
    {
        var scale = new NetworkScale(window: 3);
        Assert.Equal(4d * 1024 * 1024, scale.Next(2d * 1024 * 1024)); // ráfaga de 2 MB/s
        Assert.Equal(4d * 1024 * 1024, scale.Next(10 * 1024));         // sigue recordando el pico
        Assert.Equal(4d * 1024 * 1024, scale.Next(10 * 1024));
        Assert.Equal(64d * 1024, scale.Next(10 * 1024));               // pico olvidado: baja
    }

    [Fact]
    public void Metrics_view_model_formats_everything_for_the_dials()
    {
        var vm = new MetricsViewModel(new SystemMetricsService());
        vm.Apply(new SystemMetricsSnapshot(DateTimeOffset.Now, 91.2, 75, 12, 16, 1024, 256, 6L * 1024 * 1024 * 1024,
            [new NetworkUsageItem(10, "steam", 1024 * 1024, 256 * 1024, 123)], true, NetworkMonitorMode.PerProcess,
            SystemMetricsService.AdministratorNotice));

        Assert.Equal("91.2 %", vm.CpuText.Replace(',', '.'));
        Assert.Equal("12.0 / 16.0 GB", vm.RamGbText.Replace(',', '.'));
        Assert.Equal("1.3 MB/s", vm.NetRateText.Replace(',', '.'));    // 1024 + 256 KB/s
        Assert.Equal("Sesión 6.0 GB", vm.NetSessionText.Replace(',', '.'));
        Assert.Equal(4d * 1024 * 1024, vm.NetScaleMax);
        Assert.InRange(vm.NetNeedle, 0.31, 0.32);                      // 1,25 MB/s de 4 MB/s
        Assert.Equal("POR PROCESO · ETW", vm.ModeText);
        Assert.Single(vm.TopNetwork);
    }
}

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PulsoWin.Core.Sensors;

namespace PulsoWin.Presentation.ViewModels;

/// <summary>Pestaña Hardware: ficha del equipo y sensores en vivo cada 2 segundos.</summary>
public sealed partial class HardwareViewModel(IReadOnlyList<ISensorSource> sources, ProcessUsageSampler? usage = null) : ObservableObject
{
    private readonly ProcessUsageSampler _usage = usage ?? new ProcessUsageSampler();
    private bool _reading;

    // Top 3 de programas que más consumen, bajo cada marcador del HUD.
    [ObservableProperty] private IReadOnlyList<UsageItem> _topCpu = [];
    [ObservableProperty] private IReadOnlyList<UsageItem> _topRam = [];
    [ObservableProperty] private IReadOnlyList<UsageItem> _topGpu = [];
    [ObservableProperty] private string _gpuTopNote = "midiendo…";

    public ObservableCollection<SummaryRow> Summary { get; } = [];
    public ObservableCollection<SensorReading> Sensors { get; } = [];

    // Marcadores de la cabecera ("HUD"): lo que un jugador mira primero.
    [ObservableProperty] private string _gpuTempText = "—";

    /// <summary>
    /// Carga la ficha y refresca los sensores hasta que se cancele. Se llama desde el hilo de la interfaz:
    /// las colecciones se actualizan en ese hilo y la lectura de sensores corre en segundo plano.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var summary = await Task.Run(SystemSummary.Collect, cancellationToken);
        foreach (var row in summary) Summary.Add(row);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        do
        {
            await RefreshAsync();
        }
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(true));
    }

    public async Task RefreshAsync()
    {
        if (_reading) return;
        _reading = true;
        try
        {
            var (readings, top) = await Task.Run(() => (sources.SelectMany(ReadSafe).ToList(), SampleUsageSafe()));
            TopCpu = top.Cpu;
            TopRam = top.Ram;
            TopGpu = top.Gpu;
            GpuTopNote = !top.GpuAvailable ? "sin contadores de GPU" : top.Gpu.Count == 0 ? "GPU en reposo" : "";
            // Actualiza fila por fila en vez de vaciar y rellenar: la tabla no salta ni pierde el scroll cada 2 s.
            if (Sensors.Count == readings.Count)
            {
                for (var i = 0; i < readings.Count; i++)
                    if (!Equals(Sensors[i], readings[i])) Sensors[i] = readings[i];
            }
            else
            {
                Sensors.Clear();
                foreach (var reading in readings) Sensors.Add(reading);
            }

            GpuTempText = Find(readings, r => r.Unit == "°C" && r.Label.EndsWith("· temperatura") && r.Source.StartsWith("GPU"));
        }
        finally
        {
            _reading = false;
        }
    }

    private UsageTop SampleUsageSafe()
    {
        try
        {
            return _usage.Sample();
        }
        catch (Exception)
        {
            return UsageTop.Empty; // el top es informativo: si falla, no debe frenar los sensores
        }
    }

    private static string Find(List<SensorReading> readings, Func<SensorReading, bool> match) =>
        readings.FirstOrDefault(r => match(r) && r.Value is not null)?.Display ?? "—";

    private static IReadOnlyList<SensorReading> ReadSafe(ISensorSource source)
    {
        try
        {
            return source.Read();
        }
        catch (Exception ex)
        {
            return [new SensorReading(source.Name, source.Name, null, "", "error: " + ex.Message)];
        }
    }
}

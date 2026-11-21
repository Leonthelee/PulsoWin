using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PulsoWin.Core.Sensors;

namespace PulsoWin.Presentation.ViewModels;

/// <summary>Pestaña Hardware: ficha del equipo y sensores en vivo cada 2 segundos.</summary>
public sealed partial class HardwareViewModel(IReadOnlyList<ISensorSource> sources) : ObservableObject
{
    private bool _reading;

    public ObservableCollection<SummaryRow> Summary { get; } = [];
    public ObservableCollection<SensorReading> Sensors { get; } = [];

    // Marcadores de la cabecera ("HUD"): lo que un jugador mira primero.
    [ObservableProperty] private string _cpuText = "—";
    [ObservableProperty] private string _ramText = "—";
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
            var readings = await Task.Run(() => sources.SelectMany(ReadSafe).ToList());
            Sensors.Clear();
            foreach (var reading in readings) Sensors.Add(reading);

            CpuText = Find(readings, r => r.Label == "CPU · uso");
            RamText = Find(readings, r => r.Label == "RAM · en uso");
            GpuTempText = Find(readings, r => r.Unit == "°C" && r.Label.EndsWith("· temperatura") && r.Source.StartsWith("GPU"));
        }
        finally
        {
            _reading = false;
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

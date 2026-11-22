// PulsoWin · MIT · Co-designed with AI coding assistants (Claude & Gemini)
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PulsoWin.Core.Common;
using PulsoWin.Core.Metrics;
using PulsoWin.Core.Network;
using PulsoWin.Core.Localization;

namespace PulsoWin.Presentation.ViewModels;

/// <summary>
/// Estado en vivo que comparten la app completa (HUD y pestaña Red) y el Modo Gadget.
/// Hay UNA sola instancia conectada a UN solo <see cref="SystemMetricsService"/>: abrir o cerrar el gadget
/// no reinicia el monitoreo, solo cambia qué ventana lo muestra.
/// </summary>
public sealed partial class MetricsViewModel(SystemMetricsService service) : ObservableObject
{
    public static string NetworkHeader => L.T("Net_Header");

    private readonly NetworkScale _scale = new();

    public ObservableCollection<NetworkUsageItem> TopNetwork { get; } = [];

    // CPU
    [ObservableProperty] private double _cpuPercent;
    [ObservableProperty] private string _cpuText = "—";

    // RAM
    [ObservableProperty] private double _ramPercent;
    [ObservableProperty] private string _ramPercentText = "—";
    [ObservableProperty] private string _ramGbText = "";

    // GPU (uso y temperatura; «no disponible» cuando el equipo no los expone)
    [ObservableProperty] private double _gpuPercent;
    [ObservableProperty] private string _gpuText = "—";
    [ObservableProperty] private string _gpuSubText = "";
    [ObservableProperty] private double _gpuTempC;
    [ObservableProperty] private string _gpuTempText = "—";
    [ObservableProperty] private string _gpuTempSubText = "";

    // Red
    [ObservableProperty] private double _netBytesPerSecond;
    [ObservableProperty] private string _netRateText = "—";
    [ObservableProperty] private string _netSessionText = "";
    [ObservableProperty] private double _netScaleMax = NetworkScale.Steps[0];
    [ObservableProperty] private string _netScaleText = "";

    // Modo
    [ObservableProperty] private bool _isAdminMode;
    [ObservableProperty] private bool _isPerProcess;
    [ObservableProperty] private string _modeText = "";
    [ObservableProperty] private string _notice = L.T("Net_Starting");

    /// <summary>Aguja del dial de red, 0..1 sobre la escala dinámica actual.</summary>
    public double NetNeedle => NetScaleMax <= 0 ? 0 : Math.Clamp(NetBytesPerSecond / NetScaleMax, 0, 1);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        service.SnapshotUpdated += OnSnapshot;
        try
        {
            if (service.Latest is { } latest) Apply(latest);
            await service.RunAsync(cancellationToken);
        }
        finally
        {
            service.SnapshotUpdated -= OnSnapshot;
        }
    }

    private void OnSnapshot(object? sender, SystemMetricsSnapshot s) => Apply(s);

    /// <summary>Aplica una foto (público para pruebas).</summary>
    public void Apply(SystemMetricsSnapshot s)
    {
        CpuPercent = s.CpuPercent;
        CpuText = $"{s.CpuPercent:0.#} %";
        RamPercent = s.RamPercent;
        RamPercentText = $"{s.RamPercent:0.#} %";
        RamGbText = $"{s.RamUsedGB:0.0} / {s.RamTotalGB:0.0} GB";

        GpuPercent = s.GpuPercent ?? 0;
        GpuText = s.GpuPercent is { } g ? $"{g:0} %" : "—";
        GpuSubText = s.GpuPercent is null ? L.T("Common_NotAvailable") : Shorten(s.GpuName) ?? L.T("Gauge_TotalUse");
        GpuTempC = s.GpuTempC ?? 0;
        GpuTempText = s.GpuTempC is { } t ? $"{t:0} °C" : "—";
        GpuTempSubText = s.GpuTempC is null ? L.T("Common_NotAvailable") : "GPU";

        var bytesPerSecond = (s.NetRxKBps + s.NetTxKBps) * 1024;
        NetScaleMax = _scale.Next(bytesPerSecond);
        NetBytesPerSecond = bytesPerSecond;
        NetRateText = Rates.Format(bytesPerSecond);
        NetSessionText = L.F("Net_Session", Bytes.Format(s.NetSessionTotalBytes));
        NetScaleText = L.F("Net_Scale", Rates.Format(NetScaleMax).Replace(",0 ", " ").Replace(".0 ", " "));
        OnPropertyChanged(nameof(NetNeedle));

        IsAdminMode = s.IsAdminMode;
        IsPerProcess = s.NetworkMode == NetworkMonitorMode.PerProcess;
        ModeText = IsPerProcess ? L.T("Net_ModePerProcess") : L.T("Net_ModeInterface");
        Notice = s.NetworkNotice;

        // Fila por fila: la tabla no parpadea ni pierde el scroll cada 2 s.
        var items = s.TopNetworkProcesses;
        for (var i = 0; i < items.Count; i++)
        {
            if (i < TopNetwork.Count)
            {
                if (!Equals(TopNetwork[i], items[i])) TopNetwork[i] = items[i];
            }
            else TopNetwork.Add(items[i]);
        }
        while (TopNetwork.Count > items.Count) TopNetwork.RemoveAt(TopNetwork.Count - 1);
    }

    /// <summary>"NVIDIA Quadro P1000" → "Quadro P1000": el nombre cabe bajo el dial.</summary>
    private static string? Shorten(string? gpuName)
    {
        if (string.IsNullOrWhiteSpace(gpuName)) return null;
        var name = gpuName.Replace("NVIDIA ", "", StringComparison.OrdinalIgnoreCase).Replace("GeForce ", "", StringComparison.OrdinalIgnoreCase);
        return name.Length <= 22 ? name : name[..21] + "…";
    }
}

/// <summary>
/// Escala dinámica del dial de red: el máximo es el escalón redondo más chico que cubre el pico
/// de las últimas <c>window</c> lecturas (30 lecturas de 1 s ≈ 30 s). Sube de inmediato y baja solo cuando el pico se olvida,
/// así la aguja no salta de escala con cada ráfaga corta.
/// </summary>
public sealed class NetworkScale(int window = 30)
{
    public static readonly double[] Steps =
    [
        64d * 1024, 256d * 1024,
        1d * 1024 * 1024, 4d * 1024 * 1024, 16d * 1024 * 1024, 64d * 1024 * 1024, 256d * 1024 * 1024,
        1024d * 1024 * 1024,
    ];

    private readonly Queue<double> _recent = new();

    public double Next(double bytesPerSecond)
    {
        _recent.Enqueue(Math.Max(0, bytesPerSecond));
        while (_recent.Count > window) _recent.Dequeue();
        var peak = _recent.Max();
        return Steps.FirstOrDefault(s => s >= peak, Steps[^1]);
    }
}

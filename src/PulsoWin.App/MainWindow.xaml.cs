using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using PulsoWin.Core.Cleaning;
using PulsoWin.Core.Common;
using PulsoWin.Core.Sensors;
using PulsoWin.Core.Updates;

namespace PulsoWin.App;

public partial class MainWindow : Window
{
    private static readonly string DataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PulsoWin");
    private static readonly string KeepFile = Path.Combine(DataDir, "cookies-conservar.txt");

    private readonly ObservableCollection<RuleRow> _rows = [];
    private readonly ObservableCollection<SensorReading> _sensors = [];
    private readonly ObservableCollection<PackageRow> _packages = [];
    private readonly ObservableCollection<WindowsUpdateItem> _winUpdates = [];
    private readonly IProcessProbe _probe = new ProcessProbe();
    private readonly WingetClient _winget = new();
    private readonly WindowsUpdateClient _windowsUpdate = new();
    private readonly List<ISensorSource> _sources;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private List<CookiePlan> _cookiePlans = [];
    private KeepList _cookieKeep = KeepList.Parse(""); // la misma lista con la que se calcularon los planes confirmados
    private bool _readingSensors;

    public MainWindow()
    {
        InitializeComponent();

        _sources = [new SystemLoadSource(), new NvmlSource(), new DiskHealthSource(), new AcpiThermalSource()];
        foreach (var rule in RuleCatalog.LoadBuiltIn()) _rows.Add(new RuleRow(rule));
        _rows.Add(RuleRow.CreateRecycleBin());

        RulesGrid.ItemsSource = _rows;
        SensorsGrid.ItemsSource = _sensors;
        PackagesGrid.ItemsSource = _packages;
        WindowsUpdatesGrid.ItemsSource = _winUpdates;
        KeepBox.Text = LoadKeepList();

        Loaded += async (_, _) =>
        {
            SummaryGrid.ItemsSource = await Task.Run(() => SystemSummary.Collect());
            await RefreshSensorsAsync();
            _timer.Start();
        };
        _timer.Tick += async (_, _) => await RefreshSensorsAsync();
        Closed += (_, _) =>
        {
            _timer.Stop();
            foreach (var disposable in _sources.OfType<IDisposable>()) disposable.Dispose();
        };
    }

    // ------------------------------------------------------------------ sensores

    private async Task RefreshSensorsAsync()
    {
        if (_readingSensors) return;
        _readingSensors = true;
        try
        {
            var readings = await Task.Run(() => _sources.SelectMany(s => ReadSafe(s)).ToList());
            _sensors.Clear();
            foreach (var reading in readings) _sensors.Add(reading);
        }
        finally
        {
            _readingSensors = false;
        }
    }

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

    // ------------------------------------------------------------------ análisis

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true, "Analizando… (no se borra nada todavía)");
        try
        {
            var selected = _rows.Where(r => r.IsSelected).ToList();
            foreach (var row in _rows.Except(selected)) row.Reset();

            var wantCookies = CookiesCheck.IsChecked == true;
            var keep = KeepList.Parse(KeepBox.Text);

            var (scans, recycle, plans) = await Task.Run(() =>
            {
                var scanner = new Scanner(_probe);
                var scanResults = selected.Where(r => !r.IsRecycleBin).Select(r => (Row: r, Item: scanner.Scan(r.Rule!))).ToList();
                var bin = selected.Any(r => r.IsRecycleBin) ? RecycleBin.Query() : ((long, long)?)null;
                var cookiePlans = wantCookies
                    ? CookieStores.Discover().Select(s => new CookieCleaner(_probe).Plan(s, keep)).ToList()
                    : [];
                return (scanResults, bin, cookiePlans);
            });

            foreach (var (row, item) in scans) row.ApplyScan(item);
            if (recycle is { } r)
                foreach (var row in selected.Where(x => x.IsRecycleBin)) row.ApplyRecycleBin(r.Item1, r.Item2);

            _cookiePlans = plans;
            _cookieKeep = keep;
            CookieInfo.Text = wantCookies ? DescribeCookiePlans(plans) : "";

            var totalBytes = selected.Sum(x => x.PendingBytes);
            var cookies = plans.Where(p => p.BlockedBy is null && p.Error is null).Sum(p => p.ToDelete);
            CleanButton.IsEnabled = selected.Any(x => x.HasWork) || cookies > 0;
            StatusText.Text = $"Análisis listo: {Bytes.Format(totalBytes)} se pueden liberar" +
                              (wantCookies ? $" y {cookies:N0} cookies se pueden borrar." : ".");
        }
        catch (Exception ex)
        {
            StatusText.Text = "El análisis falló: " + ex.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static string DescribeCookiePlans(List<CookiePlan> plans)
    {
        if (plans.Count == 0) return "No se encontraron perfiles de Chrome, Edge ni Firefox.";
        return string.Join(Environment.NewLine, plans.Select(p =>
            p.BlockedBy is not null ? $"{p.Store.Browser} ({p.Store.Profile}): ciérralo para borrar sus cookies."
            : p.Error is not null ? $"{p.Store.Browser} ({p.Store.Profile}): no se pudo leer ({p.Error})."
            : $"{p.Store.Browser} ({p.Store.Profile}): {p.ToDelete:N0} a borrar, {p.Kept:N0} se conservan."));
    }

    // ------------------------------------------------------------------ limpieza

    private async void Clean_Click(object sender, RoutedEventArgs e)
    {
        var work = _rows.Where(r => r.IsSelected && r.HasWork).ToList();
        var cookiePlans = CookiesCheck.IsChecked == true
            ? _cookiePlans.Where(p => p.BlockedBy is null && p.Error is null && p.ToDelete > 0).ToList()
            : [];
        if (work.Count == 0 && cookiePlans.Count == 0)
        {
            StatusText.Text = "No hay nada que limpiar. Pulsa «Analizar» primero.";
            return;
        }

        var files = work.Sum(r => r.PendingFiles);
        var bytes = work.Sum(r => r.PendingBytes);
        var cookies = cookiePlans.Sum(p => p.ToDelete);
        var message = $"Se borrarán {files:N0} archivos ({Bytes.Format(bytes)})"
                      + (work.Any(r => r.IsRecycleBin) ? " y se vaciará la papelera" : "")
                      + (cookies > 0 ? $", además de {cookies:N0} cookies (con copia de respaldo)" : "")
                      + ".\n\nLos archivos en uso se omiten. ¿Continuar?";
        if (MessageBox.Show(this, message, "Confirmar limpieza", MessageBoxButton.YesNo, MessageBoxImage.Warning)
            != MessageBoxResult.Yes)
            return;

        SetBusy(true, "Limpiando…");
        try
        {
            // Se usa la lista con la que se analizó (y que el usuario confirmó), no lo que haya en el cuadro ahora.
            var keep = _cookieKeep;
            SaveKeepList(string.Join(Environment.NewLine, keep.Domains));
            var run = await Task.Run(() => RunCleaning(work, cookiePlans, keep));

            foreach (var (row, status) in run.RowStatus) row.ApplyCleaned(status);
            _cookiePlans = [];
            CookieInfo.Text = run.CookieSummary;
            CleanButton.IsEnabled = false;
            BeforeAfterText.Text = DescribeBeforeAfter(run);
            StatusText.Text = $"Limpieza terminada. Registro: {run.LogPath}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "La limpieza falló: " + ex.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private sealed record CleaningRun(
        Snapshot Before,
        Snapshot After,
        long BytesFreed,
        int FilesDeleted,
        int CookiesDeleted,
        List<(RuleRow Row, string Status)> RowStatus,
        string CookieSummary,
        string LogPath);

    private CleaningRun RunCleaning(List<RuleRow> work, List<CookiePlan> cookiePlans, KeepList keep)
    {
        var before = Snapshot.Take(_sources);
        var log = new List<string> { $"PulsoWin · limpieza del {DateTime.Now.ToString("F", CultureInfo.CurrentCulture)}", "" };
        var statuses = new List<(RuleRow, string)>();
        var cleaner = new Cleaner(_probe);
        long freed = 0;
        var deletedFiles = 0;

        foreach (var row in work)
        {
            if (row.IsRecycleBin)
            {
                var emptied = RecycleBin.Empty();
                if (emptied) freed += row.RecycleBytes;
                statuses.Add((row, emptied ? "Papelera vaciada" : "No se pudo vaciar la papelera"));
                log.Add($"[papelera] {(emptied ? "vaciada" : "error al vaciar")} · {Bytes.Format(row.RecycleBytes)}");
                continue;
            }

            var result = cleaner.Clean(row.LastScan!);
            freed += result.BytesFreed;
            deletedFiles += result.Deleted.Count;
            statuses.Add((row, $"{result.Deleted.Count:N0} borrados · {Bytes.Format(result.BytesFreed)}"
                               + (result.Skipped > 0 ? $" · {result.Skipped:N0} en uso, omitidos" : "")));
            log.Add($"[{result.RuleId}] {result.Deleted.Count} borrados, {result.Skipped} omitidos, {Bytes.Format(result.BytesFreed)}");
            log.AddRange(result.Deleted.Select(p => "  - " + p));
        }

        var cookieCleaner = new CookieCleaner(_probe);
        var backupRoot = Path.Combine(DataDir, "respaldos-cookies");
        var cookieLines = new List<string>();
        var cookiesDeleted = 0;
        foreach (var plan in cookiePlans)
        {
            var r = cookieCleaner.Delete(plan.Store, keep, backupRoot);
            cookiesDeleted += r.Deleted;
            var line = r.Error is null
                ? $"{plan.Store.Browser} ({plan.Store.Profile}): {r.Deleted:N0} cookies borradas. Respaldo en {r.BackupPath}"
                : $"{plan.Store.Browser} ({plan.Store.Profile}): no se borró nada ({r.Error}).";
            cookieLines.Add(line);
            log.Add("[cookies] " + line);
        }

        var after = Snapshot.Take(_sources);
        string logPath;
        try
        {
            logPath = CleanLog.Write(DataDir, log);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logPath = "(no se pudo escribir el registro: " + ex.Message + ")";
        }
        return new CleaningRun(before, after, freed, deletedFiles, cookiesDeleted, statuses,
            string.Join(Environment.NewLine, cookieLines), logPath);
    }

    private static string DescribeBeforeAfter(CleaningRun run)
    {
        var (b, a) = (run.Before, run.After);
        var lines = new List<string>
        {
            $"Borrado: {Bytes.Format(run.BytesFreed)} en {run.FilesDeleted:N0} archivos" +
            (run.CookiesDeleted > 0 ? $" y {run.CookiesDeleted:N0} cookies." : "."),
            $"Espacio libre en {b.Drive}: {Bytes.Format(b.DriveFreeBytes)} → {Bytes.Format(a.DriveFreeBytes)} " +
            $"({Signed(a.DriveFreeBytes - b.DriveFreeBytes)})",
            $"RAM en uso: {Percent(b.RamUsedPercent)} → {Percent(a.RamUsedPercent)}",
            $"Temperatura máxima leída: {Celsius(b.MaxTempC)} → {Celsius(a.MaxTempC)}",
        };
        return string.Join(Environment.NewLine, lines);

        static string Signed(long v) => (v >= 0 ? "+" : "") + Bytes.Format(v);
        static string Percent(double? v) => v is { } x ? $"{x:0.#} %" : "no disponible";
        static string Celsius(double? v) => v is { } x ? $"{x:0.#} °C" : "no disponible";
    }

    // ------------------------------------------------------------------ actualizaciones

    private async void ScanUpdates_Click(object sender, RoutedEventArgs e)
    {
        SetUpdatesBusy(true, "Buscando actualizaciones en winget y Windows Update… (solo lectura)");
        try
        {
            var wingetTask = _winget.ListUpgradesAsync();
            var wuTask = Task.Run(() => _windowsUpdate.QueryPending());
            await Task.WhenAll(wingetTask, wuTask);

            var wingetScan = wingetTask.Result;
            var wuScan = wuTask.Result;

            _packages.Clear();
            foreach (var pkg in wingetScan.Packages) _packages.Add(new PackageRow(pkg));

            _winUpdates.Clear();
            foreach (var item in wuScan.Updates) _winUpdates.Add(item);

            WindowsUpdateStatus.Text = !wuScan.IsAvailable
                ? $"Windows Update: {wuScan.Error}"
                : wuScan.Updates.Count == 0
                    ? "Windows Update: no hay actualizaciones pendientes."
                    : $"Windows Update: {wuScan.Updates.Count} actualización(es) pendiente(s). Usa «Abrir Windows Update» para instalarlas.";

            UpgradeButton.IsEnabled = _packages.Count > 0;
            StatusText.Text = !wingetScan.IsAvailable
                ? $"winget no disponible: {wingetScan.Error}"
                : $"Búsqueda lista: {_packages.Count} paquete(s) en winget y {_winUpdates.Count} en Windows Update.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "La búsqueda de actualizaciones falló: " + ex.Message;
        }
        finally
        {
            SetUpdatesBusy(false);
        }
    }

    private async void UpgradeSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = _packages.Where(p => p.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusText.Text = "Selecciona al menos un paquete de winget para actualizar.";
            return;
        }

        var preview = string.Join(Environment.NewLine, selected.Take(10).Select(p => $"• {p.Name} ({p.Id}): {p.InstalledVersion} → {p.AvailableVersion}"));
        if (selected.Count > 10) preview += $"{Environment.NewLine}… y {selected.Count - 10} más.";
        var confirm = $"Se actualizarán {selected.Count} paquete(s) vía winget:\n\n{preview}\n\nAlgunos instaladores pueden pedir confirmación UAC. ¿Continuar?";
        if (MessageBox.Show(this, confirm, "Confirmar actualización", MessageBoxButton.YesNo, MessageBoxImage.Question)
            != MessageBoxResult.Yes)
            return;

        SetUpdatesBusy(true, $"Actualizando {selected.Count} paquete(s)…");
        var log = new List<string> { $"PulsoWin · actualización winget del {DateTime.Now.ToString("F", CultureInfo.CurrentCulture)}", "" };
        var ok = 0;
        var failed = 0;
        try
        {
            foreach (var row in selected)
            {
                row.Status = "Actualizando…";
                StatusText.Text = $"Actualizando {row.Name} ({row.Id})…";
                var res = await _winget.UpgradeAsync(row.Id);
                if (res.Succeeded)
                {
                    ok++;
                    row.Status = "Actualizado";
                    row.IsSelected = false;
                }
                else
                {
                    failed++;
                    row.Status = "Error: " + res.Summary;
                }
                log.Add($"[{row.Id}] {(res.Succeeded ? "OK" : "FAIL")} (exit {res.ExitCode}) · {res.Summary}");
            }

            string logPath;
            try { logPath = CleanLog.Write(DataDir, log); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logPath = "(no se pudo escribir el registro: " + ex.Message + ")";
            }

            StatusText.Text = $"Actualización terminada: {ok} OK, {failed} con error. Registro: {logPath}";
        }
        finally
        {
            SetUpdatesBusy(false);
            UpgradeButton.IsEnabled = _packages.Any(p => p.IsSelected);
        }
    }

    private void OpenWindowsUpdate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            WindowsUpdateClient.OpenWindowsUpdateSettings();
        }
        catch (Exception ex)
        {
            StatusText.Text = "No se pudo abrir Windows Update: " + ex.Message;
        }
    }

    private void SetUpdatesBusy(bool busy, string? status = null)
    {
        ScanUpdatesButton.IsEnabled = !busy;
        if (busy) UpgradeButton.IsEnabled = false;
        PackagesGrid.IsEnabled = !busy;
        Cursor = busy ? System.Windows.Input.Cursors.Wait : null;
        if (status is not null) StatusText.Text = status;
    }

    // ------------------------------------------------------------------ utilidades

    private void SetBusy(bool busy, string? status = null)
    {
        ScanButton.IsEnabled = !busy;
        if (busy) CleanButton.IsEnabled = false;
        RulesGrid.IsEnabled = !busy;
        Cursor = busy ? System.Windows.Input.Cursors.Wait : null;
        if (status is not null) StatusText.Text = status;
    }

    private static string LoadKeepList()
    {
        try
        {
            return File.Exists(KeepFile) ? File.ReadAllText(KeepFile) : "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    private static void SaveKeepList(string text)
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(KeepFile, text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // No es crítico: la lista se usa igual en esta limpieza.
        }
    }
}

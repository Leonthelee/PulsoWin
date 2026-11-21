using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PulsoWin.Core.Cleaning;
using PulsoWin.Core.Common;
using PulsoWin.Core.Sensors;
using PulsoWin.Presentation.Services;

namespace PulsoWin.Presentation.ViewModels;

/// <summary>Pestaña Limpiar: analizar (solo lectura) → confirmar → borrar.</summary>
public sealed partial class CleanViewModel : ObservableObject
{
    private readonly IProcessProbe _probe;
    private readonly IDialogService _dialogs;
    private readonly IRecycleBin _recycleBin;
    private readonly StatusBar _status;
    private readonly string _dataDir;
    private readonly IReadOnlyList<ISensorSource> _sources;
    private List<CookiePlan> _cookiePlans = [];
    private KeepList _cookieKeep = KeepList.Parse(""); // la lista con la que se calcularon los planes que se confirman

    [ObservableProperty] private bool _deleteCookies;
    [ObservableProperty] private string _keepText = "";
    [ObservableProperty] private string _cookieInfo = "";
    [ObservableProperty] private string _beforeAfter = "Todavía no has limpiado en esta sesión.";

    /// <summary>Total liberable del último análisis, para el marcador grande de la cabecera.</summary>
    [ObservableProperty] private string _freeableText = "—";

    /// <summary>Las 3 categorías que más espacio liberan, bajo el marcador LIBERABLE.</summary>
    [ObservableProperty] private IReadOnlyList<UsageItem> _topFreeable = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand), nameof(CleanCommand))]
    private bool _isBusy;

    [ObservableProperty] private string _busyLabel = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CleanCommand))]
    private bool _hasPendingWork;

    public CleanViewModel(
        IEnumerable<CleanRule> rules,
        IProcessProbe probe,
        IDialogService dialogs,
        IRecycleBin recycleBin,
        StatusBar status,
        string dataDir,
        IReadOnlyList<ISensorSource> sources)
    {
        _probe = probe;
        _dialogs = dialogs;
        _recycleBin = recycleBin;
        _status = status;
        _dataDir = dataDir;
        _sources = sources;
        foreach (var rule in rules) Rules.Add(new RuleRow(rule));
        Rules.Add(RuleRow.CreateRecycleBin());
        _keepText = LoadKeepList();
        _selectAll = new SelectAllState<RuleRow>(Rules, () => OnPropertyChanged(nameof(AllSelected)));
    }

    private readonly SelectAllState<RuleRow> _selectAll;

    public ObservableCollection<RuleRow> Rules { get; } = [];

    /// <summary>Casilla «seleccionar todo» de la cabecera: true, false o null (algunas marcadas).</summary>
    public bool? AllSelected
    {
        get => _selectAll.Value;
        set => _selectAll.Value = value;
    }

    private string KeepFile => Path.Combine(_dataDir, "cookies-conservar.txt");

    // ------------------------------------------------------------------ análisis

    private bool CanScan() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        IsBusy = true;
        BusyLabel = "ANALIZANDO…";
        _status.Message = "Analizando… (no se borra nada todavía)";
        try
        {
            var selected = Rules.Where(r => r.IsSelected).ToList();
            foreach (var row in Rules.Except(selected)) row.Reset();

            var wantCookies = DeleteCookies;
            var keep = KeepList.Parse(KeepText);

            var (scans, recycle, plans) = await Task.Run(() =>
            {
                var scanner = new Scanner(_probe);
                var scanResults = selected.Where(r => !r.IsRecycleBin)
                    .Select(r => (Row: r, Item: scanner.Scan(r.Rule!))).ToList();
                var bin = selected.Any(r => r.IsRecycleBin) ? _recycleBin.Query() : ((long, long)?)null;
                var cookiePlans = wantCookies
                    ? CookieStores.Discover().Select(s => new CookieCleaner(_probe).Plan(s, keep)).ToList()
                    : [];
                return (scanResults, bin, cookiePlans);
            });

            foreach (var (row, item) in scans) row.ApplyScan(item);
            if (recycle is { } bin)
                foreach (var row in selected.Where(x => x.IsRecycleBin)) row.ApplyRecycleBin(bin.Item1, bin.Item2);

            _cookiePlans = plans;
            _cookieKeep = keep;
            CookieInfo = wantCookies ? DescribeCookiePlans(plans) : "";

            var totalBytes = selected.Sum(x => x.PendingBytes);
            var cookies = plans.Where(p => p.BlockedBy is null && p.Error is null).Sum(p => p.ToDelete);
            FreeableText = Bytes.Format(totalBytes);
            TopFreeable = selected.Where(r => r.PendingBytes > 0)
                .OrderByDescending(r => r.PendingBytes).Take(3)
                .Select(r => new UsageItem(r.Name, r.PendingBytes, Bytes.Format(r.PendingBytes), r.PendingFiles))
                .ToList();
            HasPendingWork = selected.Any(x => x.HasWork) || cookies > 0;
            _status.Message = $"Análisis listo: {Bytes.Format(totalBytes)} se pueden liberar" +
                              (wantCookies ? $" y {cookies:N0} cookies se pueden borrar." : ".");
        }
        catch (Exception ex)
        {
            _status.Message = "El análisis falló: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
            BusyLabel = "";
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

    private bool CanClean() => !IsBusy && HasPendingWork;

    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task CleanAsync()
    {
        var work = Rules.Where(r => r.IsSelected && r.HasWork).ToList();
        var cookiePlans = DeleteCookies
            ? _cookiePlans.Where(p => p.BlockedBy is null && p.Error is null && p.ToDelete > 0).ToList()
            : [];
        if (work.Count == 0 && cookiePlans.Count == 0)
        {
            _status.Message = "No hay nada que limpiar. Pulsa START para analizar primero.";
            return;
        }

        var files = work.Sum(r => r.PendingFiles);
        var bytes = work.Sum(r => r.PendingBytes);
        var cookies = cookiePlans.Sum(p => p.ToDelete);
        var message = $"Se borrarán {files:N0} archivos ({Bytes.Format(bytes)})"
                      + (work.Any(r => r.IsRecycleBin) ? " y se vaciará la papelera" : "")
                      + (cookies > 0 ? $", además de {cookies:N0} cookies (con copia de respaldo)" : "")
                      + ".\n\nLos archivos en uso se omiten. ¿Continuar?";
        if (!_dialogs.Confirm("Confirmar limpieza", message))
        {
            _status.Message = "Limpieza cancelada. No se borró nada.";
            return;
        }

        IsBusy = true;
        BusyLabel = "LIMPIANDO…";
        _status.Message = "Limpiando…";
        try
        {
            // Se usa la lista con la que se analizó (y que se confirmó), no lo que haya en el cuadro ahora.
            var keep = _cookieKeep;
            SaveKeepList(string.Join(Environment.NewLine, keep.Domains));
            var run = await Task.Run(() => RunCleaning(work, cookiePlans, keep));

            foreach (var (row, status) in run.RowStatus) row.ApplyCleaned(status);
            _cookiePlans = [];
            CookieInfo = run.CookieSummary;
            HasPendingWork = false;
            FreeableText = "+" + Bytes.Format(run.BytesFreed);
            TopFreeable = [];
            BeforeAfter = DescribeBeforeAfter(run);
            _status.Message = $"Limpieza terminada. Registro: {run.LogPath}";
        }
        catch (Exception ex)
        {
            _status.Message = "La limpieza falló: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
            BusyLabel = "";
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
        var statuses = new List<(RuleRow Row, string Status)>();
        var cleaner = new Cleaner(_probe);
        long freed = 0;
        var deletedFiles = 0;

        foreach (var row in work)
        {
            if (row.IsRecycleBin)
            {
                var emptied = _recycleBin.Empty();
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
        var backupRoot = Path.Combine(_dataDir, "respaldos-cookies");
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
            logPath = CleanLog.Write(_dataDir, log);
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
        return string.Join(Environment.NewLine,
            $"Borrado: {Bytes.Format(run.BytesFreed)} en {run.FilesDeleted:N0} archivos" +
            (run.CookiesDeleted > 0 ? $" y {run.CookiesDeleted:N0} cookies." : "."),
            $"Espacio libre en {b.Drive}: {Bytes.Format(b.DriveFreeBytes)} → {Bytes.Format(a.DriveFreeBytes)} " +
            $"({Signed(a.DriveFreeBytes - b.DriveFreeBytes)})",
            $"RAM en uso: {Percent(b.RamUsedPercent)} → {Percent(a.RamUsedPercent)}",
            $"Temperatura máxima leída: {Celsius(b.MaxTempC)} → {Celsius(a.MaxTempC)}");

        static string Signed(long v) => (v >= 0 ? "+" : "") + Bytes.Format(v);
        static string Percent(double? v) => v is { } x ? $"{x:0.#} %" : "no disponible";
        static string Celsius(double? v) => v is { } x ? $"{x:0.#} °C" : "no disponible";
    }

    // ------------------------------------------------------------------ lista de cookies a conservar

    private string LoadKeepList()
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

    private void SaveKeepList(string text)
    {
        try
        {
            Directory.CreateDirectory(_dataDir);
            File.WriteAllText(KeepFile, text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // No es crítico: la lista se usa igual en esta limpieza.
        }
    }
}

// PulsoWin · MIT · Co-designed with AI coding assistants (Claude & Gemini)
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PulsoWin.Core.Cleaning;
using PulsoWin.Core.Common;
using PulsoWin.Core.Sensors;
using PulsoWin.Presentation.Services;
using PulsoWin.Core.Localization;

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
    [ObservableProperty] private string _beforeAfter = L.T("Clean_NotYet");

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
        BusyLabel = L.T("Busy_Analyzing");
        _status.Message = L.T("Clean_Analyzing");
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
            _status.Message = wantCookies
                ? L.F("Clean_ScanDoneCookies", Bytes.Format(totalBytes), cookies)
                : L.F("Clean_ScanDone", Bytes.Format(totalBytes));
        }
        catch (Exception ex)
        {
            _status.Message = L.F("Clean_ScanFailed", ex.Message);
        }
        finally
        {
            IsBusy = false;
            BusyLabel = "";
        }
    }

    private static string DescribeCookiePlans(List<CookiePlan> plans)
    {
        if (plans.Count == 0) return L.T("Cookies_NoProfiles");
        return string.Join(Environment.NewLine, plans.Select(p =>
            p.BlockedBy is not null ? L.F("Cookies_CloseBrowser", p.Store.Browser, p.Store.Profile)
            : p.Error is not null ? L.F("Cookies_ReadError", p.Store.Browser, p.Store.Profile, p.Error)
            : L.F("Cookies_Plan", p.Store.Browser, p.Store.Profile, p.ToDelete, p.Kept)));
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
            _status.Message = L.T("Clean_NothingToClean");
            return;
        }

        var files = work.Sum(r => r.PendingFiles);
        var bytes = work.Sum(r => r.PendingBytes);
        var cookies = cookiePlans.Sum(p => p.ToDelete);
        var message = L.F("Clean_Confirm_Files", files, Bytes.Format(bytes))
                      + (work.Any(r => r.IsRecycleBin) ? L.T("Clean_Confirm_Recycle") : "")
                      + (cookies > 0 ? L.F("Clean_Confirm_Cookies", cookies) : "")
                      + L.T("Clean_Confirm_End");
        if (!_dialogs.Confirm(L.T("Clean_ConfirmTitle"), message))
        {
            _status.Message = L.T("Clean_Cancelled");
            return;
        }

        IsBusy = true;
        BusyLabel = L.T("Busy_Cleaning");
        _status.Message = L.T("Clean_Cleaning");
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
            _status.Message = L.F("Clean_Done", run.LogPath);
        }
        catch (Exception ex)
        {
            _status.Message = L.F("Clean_Failed", ex.Message);
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
        var log = new List<string> { L.F("Log_CleanHeader", DateTime.Now.ToString("F", CultureInfo.CurrentCulture)), "" };
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
                statuses.Add((row, emptied ? L.T("Recycle_Emptied") : L.T("Recycle_EmptyFailed")));
                log.Add($"[papelera] {(emptied ? "vaciada" : "error al vaciar")} · {Bytes.Format(row.RecycleBytes)}");
                continue;
            }

            var result = cleaner.Clean(row.LastScan!);
            freed += result.BytesFreed;
            deletedFiles += result.Deleted.Count;
            statuses.Add((row, L.F("Clean_RowDone", result.Deleted.Count, Bytes.Format(result.BytesFreed))
                               + (result.Skipped > 0 ? L.F("Clean_RowSkipped", result.Skipped) : "")));
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
                ? L.F("Cookies_Deleted", plan.Store.Browser, plan.Store.Profile, r.Deleted, r.BackupPath)
                : L.F("Cookies_NotDeleted", plan.Store.Browser, plan.Store.Profile, r.Error);
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
            logPath = L.F("Log_WriteFailed", ex.Message);
        }
        return new CleaningRun(before, after, freed, deletedFiles, cookiesDeleted, statuses,
            string.Join(Environment.NewLine, cookieLines), logPath);
    }

    private static string DescribeBeforeAfter(CleaningRun run)
    {
        var (b, a) = (run.Before, run.After);
        return string.Join(Environment.NewLine,
            run.CookiesDeleted > 0
                ? L.F("BA_DeletedCookies", Bytes.Format(run.BytesFreed), run.FilesDeleted, run.CookiesDeleted)
                : L.F("BA_Deleted", Bytes.Format(run.BytesFreed), run.FilesDeleted),
            L.F("BA_FreeSpace", b.Drive, Bytes.Format(b.DriveFreeBytes), Bytes.Format(a.DriveFreeBytes), Signed(a.DriveFreeBytes - b.DriveFreeBytes)),
            L.F("BA_Ram", Percent(b.RamUsedPercent), Percent(a.RamUsedPercent)),
            L.F("BA_MaxTemp", Celsius(b.MaxTempC), Celsius(a.MaxTempC)));

        static string Signed(long v) => (v >= 0 ? "+" : "") + Bytes.Format(v);
        static string Percent(double? v) => v is { } x ? $"{x:0.#} %" : L.T("Common_NotAvailable");
        static string Celsius(double? v) => v is { } x ? $"{x:0.#} °C" : L.T("Common_NotAvailable");
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

// PulsoWin · MIT · Co-designed with AI coding assistants (Claude & Gemini)
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PulsoWin.Core.Cleaning;
using PulsoWin.Core.Updates;
using PulsoWin.Presentation.Services;
using PulsoWin.Core.Localization;

namespace PulsoWin.Presentation.ViewModels;

/// <summary>
/// Pestaña Actualizar, todo dentro de PulsoWin:
/// 1) «Buscar» consulta winget y Windows Update en segundo plano, sin abrir otras pantallas.
/// 2) Al terminar muestra en una sola lista todo lo pendiente.
/// 3) «Proceder» instala lo marcado: programas con winget y Windows Update con un único permiso de administrador.
/// </summary>
public sealed partial class UpdatesViewModel(
    WingetClient winget,
    Func<WindowsUpdateScanResult> queryWindowsUpdate,
    Func<IReadOnlyList<string>, Task<WindowsUpdateInstallResult>> installWindowsUpdates,
    IDialogService dialogs,
    StatusBar status,
    string dataDir) : ObservableObject
{
    public ObservableCollection<UpdateRow> Items { get; } = [];

    private SelectAllState<UpdateRow>? _selectAll;
    private SelectAllState<UpdateRow> SelectAllTracker =>
        _selectAll ??= new SelectAllState<UpdateRow>(Items, () => OnPropertyChanged(nameof(AllSelected)));

    /// <summary>Casilla «seleccionar todo» de la cabecera: true, false o null (algunas marcadas).</summary>
    public bool? AllSelected
    {
        get => SelectAllTracker.Value;
        set => SelectAllTracker.Value = value;
    }

    [ObservableProperty]
    private string _summary = L.T("Upd_Intro");

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand), nameof(ProceedCommand), nameof(SelectAllCommand))]
    private bool _isBusy;

    [ObservableProperty] private string _busyLabel = "";

    // ------------------------------------------------------------------ buscar

    private bool CanSearch() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        IsBusy = true;
        BusyLabel = L.T("Busy_Searching");
        status.Message = L.T("Upd_Searching");
        try
        {
            var wingetTask = winget.ListUpgradesAsync();
            var wuTask = Task.Run(queryWindowsUpdate);
            await Task.WhenAll(wingetTask, wuTask);
            var programs = wingetTask.Result;
            var windows = wuTask.Result;

            _ = SelectAllTracker; // se suscribe a la lista antes de llenarla
            Items.Clear();
            foreach (var u in windows.Updates.Where(u => WindowsUpdateInstaller.IsValidUpdateId(u.UpdateId)))
                Items.Add(UpdateRow.FromWindows(u));
            foreach (var p in programs.Packages) Items.Add(UpdateRow.FromWinget(p));

            var programCount = Items.Count(i => i.Kind == UpdateKind.Program);
            var windowsCount = Items.Count(i => i.Kind == UpdateKind.Windows);
            Summary = Items.Count == 0
                ? L.T("Upd_AllUpToDate")
                : L.F("Upd_Found", windowsCount, programCount);
            var notes = new List<string>();
            if (!programs.IsAvailable) notes.Add("winget: " + programs.Error);
            if (!windows.IsAvailable) notes.Add("Windows Update: " + windows.Error);
            if (notes.Count > 0) Summary += "  (" + string.Join(" · ", notes) + ")";
            status.Message = L.T("Upd_SearchDone");
        }
        catch (Exception ex)
        {
            status.Message = L.F("Upd_SearchFailed", ex.Message);
        }
        finally
        {
            IsBusy = false;
            BusyLabel = "";
            ProceedCommand.NotifyCanExecuteChanged();
            SelectAllCommand.NotifyCanExecuteChanged();
        }
    }

    private bool HasItems() => !IsBusy && Items.Count > 0;

    [RelayCommand(CanExecute = nameof(HasItems))]
    private void SelectAll()
    {
        var mark = Items.Any(i => !i.IsSelected);
        foreach (var item in Items) item.IsSelected = mark;
    }

    // ------------------------------------------------------------------ proceder

    [RelayCommand(CanExecute = nameof(HasItems))]
    private async Task ProceedAsync()
    {
        var selected = Items.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0)
        {
            status.Message = L.T("Upd_SelectOne");
            return;
        }

        var windows = selected.Where(i => i.Kind == UpdateKind.Windows).ToList();
        var programs = selected.Where(i => i.Kind == UpdateKind.Program).ToList();
        var preview = string.Join(Environment.NewLine, selected.Take(10).Select(i =>
            i.Kind == UpdateKind.Windows ? $"• Windows: {i.Name}" : $"• {i.Name}: {i.CurrentVersion} → {i.NewVersion}"));
        if (selected.Count > 10) preview += Environment.NewLine + L.F("Upd_AndMore", selected.Count - 10);
        var message = L.F("Upd_Confirm_Head", selected.Count, preview)
                      + (windows.Count > 0 ? L.T("Upd_Confirm_Windows") : "")
                      + (programs.Count > 0 ? L.T("Upd_Confirm_Programs") : "")
                      + L.T("Upd_Confirm_End");
        if (!dialogs.Confirm(L.T("Upd_ConfirmTitle"), message))
        {
            status.Message = L.T("Upd_Cancelled");
            return;
        }

        IsBusy = true;
        BusyLabel = L.T("Busy_Updating");
        var log = new List<string> { L.F("Log_UpdatesHeader", DateTime.Now.ToString("F", CultureInfo.CurrentCulture)), "" };
        var ok = 0;
        var failed = 0;
        var reboot = false;
        try
        {
            if (windows.Count > 0)
            {
                foreach (var row in windows) row.Status = L.T("Upd_RowInstalling");
                status.Message = L.F("Upd_InstallingWindows", windows.Count);
                var result = await installWindowsUpdates(windows.Select(w => w.Id).ToList());
                if (result.Error is not null)
                {
                    foreach (var row in windows) row.Status = result.Cancelled ? L.T("Upd_RowCancelled") : L.F("Common_Error", result.Error);
                    failed += windows.Count;
                    log.Add($"[windows] {(result.Cancelled ? "cancelado" : "error")}: {result.Error}");
                }
                else
                {
                    reboot = result.RebootRequired;
                    foreach (var row in windows)
                    {
                        var item = result.Items.FirstOrDefault(r => string.Equals(r.UpdateId, row.Id, StringComparison.OrdinalIgnoreCase));
                        var success = item?.Succeeded == true;
                        row.Status = success
                            ? (reboot ? L.T("Upd_RowInstalledReboot") : L.T("Upd_RowInstalled"))
                            : L.F("Upd_RowNotInstalled", item?.ResultCode ?? -1);
                        if (success) { ok++; row.IsSelected = false; } else failed++;
                        log.Add($"[windows {row.NewVersion}] {(success ? "OK" : "FAIL")} · {row.Name}");
                    }
                }
            }

            foreach (var row in programs)
            {
                row.Status = L.T("Upd_RowUpdating");
                status.Message = L.F("Upd_Updating", row.Name);
                var res = await winget.UpgradeAsync(row.Id);
                if (res.Succeeded) { ok++; row.Status = L.T("Upd_RowUpdated"); row.IsSelected = false; }
                else { failed++; row.Status = L.F("Common_Error", res.Summary); }
                log.Add($"[{row.Id}] {(res.Succeeded ? "OK" : "FAIL")} (exit {res.ExitCode}) · {res.Summary}");
            }

            string logPath;
            try { logPath = CleanLog.Write(dataDir, log); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logPath = L.F("Log_WriteFailed", ex.Message);
            }
            Summary = L.F("Upd_Done", ok, failed) + (reboot ? L.T("Upd_DoneReboot") : "");
            status.Message = L.F("Upd_Finished", logPath);
        }
        catch (Exception ex)
        {
            foreach (var row in selected.Where(r => r.Status.EndsWith('…'))) row.Status = L.T("Upd_RowInterrupted");
            status.Message = L.F("Upd_Interrupted", ok, failed, ex.Message);
        }
        finally
        {
            IsBusy = false;
            BusyLabel = "";
        }
    }
}

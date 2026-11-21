using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PulsoWin.Core.Cleaning;
using PulsoWin.Core.Updates;
using PulsoWin.Presentation.Services;

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

    [ObservableProperty]
    private string _summary = "Pulsa «Buscar actualizaciones». PulsoWin revisa programas y Windows en segundo plano, sin instalar nada todavía.";

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
        BusyLabel = "BUSCANDO…";
        status.Message = "Buscando actualizaciones de programas y de Windows… (solo lectura; puede tardar un minuto)";
        try
        {
            var wingetTask = winget.ListUpgradesAsync();
            var wuTask = Task.Run(queryWindowsUpdate);
            await Task.WhenAll(wingetTask, wuTask);
            var programs = wingetTask.Result;
            var windows = wuTask.Result;

            Items.Clear();
            foreach (var u in windows.Updates.Where(u => WindowsUpdateInstaller.IsValidUpdateId(u.UpdateId)))
                Items.Add(UpdateRow.FromWindows(u));
            foreach (var p in programs.Packages) Items.Add(UpdateRow.FromWinget(p));

            var programCount = Items.Count(i => i.Kind == UpdateKind.Program);
            var windowsCount = Items.Count(i => i.Kind == UpdateKind.Windows);
            Summary = Items.Count == 0
                ? "Todo al día: no hay actualizaciones pendientes."
                : $"{windowsCount} de Windows y {programCount} de programas pendientes. Marca las que quieras y pulsa «Proceder».";
            var notes = new List<string>();
            if (!programs.IsAvailable) notes.Add("winget: " + programs.Error);
            if (!windows.IsAvailable) notes.Add("Windows Update: " + windows.Error);
            if (notes.Count > 0) Summary += "  (" + string.Join(" · ", notes) + ")";
            status.Message = "Búsqueda terminada.";
        }
        catch (Exception ex)
        {
            status.Message = "La búsqueda de actualizaciones falló: " + ex.Message;
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
            status.Message = "Marca al menos una actualización (o usa «Marcar todo»).";
            return;
        }

        var windows = selected.Where(i => i.Kind == UpdateKind.Windows).ToList();
        var programs = selected.Where(i => i.Kind == UpdateKind.Program).ToList();
        var preview = string.Join(Environment.NewLine, selected.Take(10).Select(i =>
            i.Kind == UpdateKind.Windows ? $"• Windows: {i.Name}" : $"• {i.Name}: {i.CurrentVersion} → {i.NewVersion}"));
        if (selected.Count > 10) preview += $"{Environment.NewLine}… y {selected.Count - 10} más.";
        var message = $"Se instalarán {selected.Count} actualización(es), aceptando sus términos de licencia:\n\n{preview}\n\n"
                      + (windows.Count > 0 ? "Windows pedirá UNA vez permiso de administrador para las de Windows.\n" : "")
                      + (programs.Count > 0 ? "Algunos programas pueden pedir su propio permiso.\n" : "")
                      + "Puede tardar varios minutos. ¿Continuar?";
        if (!dialogs.Confirm("Confirmar actualizaciones", message))
        {
            status.Message = "Actualización cancelada. No se instaló nada.";
            return;
        }

        IsBusy = true;
        BusyLabel = "ACTUALIZANDO…";
        var log = new List<string> { $"PulsoWin · actualizaciones del {DateTime.Now.ToString("F", CultureInfo.CurrentCulture)}", "" };
        var ok = 0;
        var failed = 0;
        var reboot = false;
        try
        {
            if (windows.Count > 0)
            {
                foreach (var row in windows) row.Status = "Descargando e instalando…";
                status.Message = $"Instalando {windows.Count} actualización(es) de Windows… acepta el permiso de administrador si aparece.";
                var result = await installWindowsUpdates(windows.Select(w => w.Id).ToList());
                if (result.Error is not null)
                {
                    foreach (var row in windows) row.Status = result.Cancelled ? "Cancelado (sin permiso)" : "Error: " + result.Error;
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
                        row.Status = success ? (reboot ? "Instalada · requiere reiniciar" : "Instalada") : $"No se instaló (código {item?.ResultCode ?? -1})";
                        if (success) { ok++; row.IsSelected = false; } else failed++;
                        log.Add($"[windows {row.NewVersion}] {(success ? "OK" : "FAIL")} · {row.Name}");
                    }
                }
            }

            foreach (var row in programs)
            {
                row.Status = "Actualizando…";
                status.Message = $"Actualizando {row.Name}…";
                var res = await winget.UpgradeAsync(row.Id);
                if (res.Succeeded) { ok++; row.Status = "Actualizado"; row.IsSelected = false; }
                else { failed++; row.Status = "Error: " + res.Summary; }
                log.Add($"[{row.Id}] {(res.Succeeded ? "OK" : "FAIL")} (exit {res.ExitCode}) · {res.Summary}");
            }

            string logPath;
            try { logPath = CleanLog.Write(dataDir, log); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logPath = "(no se pudo escribir el registro: " + ex.Message + ")";
            }
            Summary = $"Listo: {ok} instalada(s), {failed} con error." + (reboot ? " Reinicia el equipo para terminar de aplicar las de Windows." : "");
            status.Message = $"Actualización terminada. Registro: {logPath}";
        }
        catch (Exception ex)
        {
            foreach (var row in selected.Where(r => r.Status.EndsWith('…'))) row.Status = "Interrumpido";
            status.Message = $"La actualización se interrumpió ({ok} OK y {failed} con error hasta ese momento): {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            BusyLabel = "";
        }
    }
}

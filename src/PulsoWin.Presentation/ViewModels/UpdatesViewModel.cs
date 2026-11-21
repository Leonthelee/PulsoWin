using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PulsoWin.Core.Cleaning;
using PulsoWin.Core.Updates;
using PulsoWin.Presentation.Services;

namespace PulsoWin.Presentation.ViewModels;

/// <summary>Pestaña Actualizar: winget (elegir y actualizar) y Windows Update (solo lectura).</summary>
public sealed partial class UpdatesViewModel(
    WingetClient winget,
    Func<WindowsUpdateScanResult> queryWindowsUpdate,
    Action openWindowsUpdate,
    IDialogService dialogs,
    StatusBar status,
    string dataDir) : ObservableObject
{
    public ObservableCollection<PackageRow> Packages { get; } = [];
    public ObservableCollection<WindowsUpdateItem> WindowsUpdates { get; } = [];

    [ObservableProperty]
    private string _windowsUpdateStatus =
        "Pulsa «Buscar actualizaciones» para consultar winget y Windows Update sin instalar nada todavía.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand), nameof(UpgradeSelectedCommand))]
    private bool _isBusy;

    [ObservableProperty] private string _busyLabel = "";

    private bool CanSearch() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        IsBusy = true;
        BusyLabel = "BUSCANDO…";
        status.Message = "Buscando actualizaciones en winget y Windows Update… (solo lectura)";
        try
        {
            var wingetTask = winget.ListUpgradesAsync();
            var wuTask = Task.Run(queryWindowsUpdate);
            await Task.WhenAll(wingetTask, wuTask);
            var wingetScan = wingetTask.Result;
            var wuScan = wuTask.Result;

            Packages.Clear();
            foreach (var pkg in wingetScan.Packages) Packages.Add(new PackageRow(pkg));
            WindowsUpdates.Clear();
            foreach (var item in wuScan.Updates) WindowsUpdates.Add(item);

            WindowsUpdateStatus = !wuScan.IsAvailable
                ? $"Windows Update: {wuScan.Error}"
                : wuScan.Updates.Count == 0
                    ? "Windows Update: no hay actualizaciones pendientes."
                    : $"Windows Update: {wuScan.Updates.Count} actualización(es) pendiente(s). Usa «Abrir Windows Update» para instalarlas.";

            status.Message = !wingetScan.IsAvailable
                ? $"winget no disponible: {wingetScan.Error}"
                : $"Búsqueda lista: {Packages.Count} paquete(s) en winget y {WindowsUpdates.Count} en Windows Update. Marca los que quieras actualizar.";
        }
        catch (Exception ex)
        {
            status.Message = "La búsqueda de actualizaciones falló: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
            BusyLabel = "";
            UpgradeSelectedCommand.NotifyCanExecuteChanged();
        }
    }

    // Con los paquetes desmarcados por defecto, el botón se habilita en cuanto hay resultados y avisa si no hay nada marcado.
    private bool CanUpgrade() => !IsBusy && Packages.Count > 0;

    [RelayCommand(CanExecute = nameof(CanUpgrade))]
    private async Task UpgradeSelectedAsync()
    {
        var selected = Packages.Where(p => p.IsSelected).ToList();
        if (selected.Count == 0)
        {
            status.Message = "Marca al menos un paquete de winget para actualizar.";
            return;
        }

        var preview = string.Join(Environment.NewLine,
            selected.Take(10).Select(p => $"• {p.Name} ({p.Id}): {p.InstalledVersion} → {p.AvailableVersion}"));
        if (selected.Count > 10) preview += $"{Environment.NewLine}… y {selected.Count - 10} más.";
        var confirm = $"Se actualizarán {selected.Count} paquete(s) vía winget, en silencio y aceptando sus licencias:\n\n{preview}\n\nAlgunos instaladores pueden pedir confirmación UAC. ¿Continuar?";
        if (!dialogs.Confirm("Confirmar actualización", confirm))
        {
            status.Message = "Actualización cancelada. No se instaló nada.";
            return;
        }

        IsBusy = true;
        BusyLabel = "ACTUALIZANDO…";
        var log = new List<string> { $"PulsoWin · actualización winget del {DateTime.Now.ToString("F", CultureInfo.CurrentCulture)}", "" };
        var ok = 0;
        var failed = 0;
        try
        {
            foreach (var row in selected)
            {
                row.Status = "Actualizando…";
                status.Message = $"Actualizando {row.Name} ({row.Id})…";
                var res = await winget.UpgradeAsync(row.Id);
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
            try { logPath = CleanLog.Write(dataDir, log); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logPath = "(no se pudo escribir el registro: " + ex.Message + ")";
            }
            status.Message = $"Actualización terminada: {ok} OK, {failed} con error. Registro: {logPath}";
        }
        catch (Exception ex)
        {
            foreach (var row in selected.Where(r => r.Status == "Actualizando…")) row.Status = "Interrumpido";
            status.Message = $"La actualización se interrumpió ({ok} OK y {failed} con error hasta ese momento): {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            BusyLabel = "";
        }
    }

    [RelayCommand]
    private void OpenWindowsUpdate()
    {
        try
        {
            openWindowsUpdate();
        }
        catch (Exception ex)
        {
            status.Message = "No se pudo abrir Windows Update: " + ex.Message;
        }
    }
}

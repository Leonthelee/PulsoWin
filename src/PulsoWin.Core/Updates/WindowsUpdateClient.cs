using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PulsoWin.Core.Updates;

public sealed record WindowsUpdateItem(
    string Title,
    string KbId,
    bool IsMandatory,
    bool IsDownloaded,
    long SizeBytes);

public sealed record WindowsUpdateScanResult(
    IReadOnlyList<WindowsUpdateItem> Updates,
    string? Error = null)
{
    public bool IsAvailable => Error is null;
}

/// <summary>
/// Consulta de solo lectura de actualizaciones pendientes de Windows vía Windows Update Agent (COM)
/// sin requerir elevación. La instalación se delega a Configuración de Windows (<c>ms-settings:windowsupdate</c>)
/// para mantener <c>PulsoWin.App</c> como <c>asInvoker</c>.
/// </summary>
public sealed class WindowsUpdateClient
{
    public WindowsUpdateScanResult QueryPending()
    {
        if (!OperatingSystem.IsWindows())
            return new WindowsUpdateScanResult([], "Solo disponible en Windows.");

        object? session = null;
        object? searcher = null;
        object? searchResult = null;
        try
        {
            var sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session");
            if (sessionType is null)
                return new WindowsUpdateScanResult([], "Windows Update Agent no disponible.");

            session = Activator.CreateInstance(sessionType);
            if (session is null)
                return new WindowsUpdateScanResult([], "No se pudo iniciar la sesión de Windows Update.");

            dynamic dynSession = session;
            searcher = dynSession.CreateUpdateSearcher();
            dynamic dynSearcher = searcher;
            searchResult = dynSearcher.Search("IsInstalled=0 and Type='Software' and IsHidden=0");
            dynamic dynResult = searchResult;

            var items = new List<WindowsUpdateItem>();
            int count = dynResult.Updates.Count;
            for (var i = 0; i < count; i++)
            {
                dynamic u = dynResult.Updates.Item(i);
                string title = u.Title ?? "(sin título)";
                var kb = "";
                try
                {
                    if (u.KBArticleIDs.Count > 0) kb = "KB" + (string)u.KBArticleIDs.Item(0);
                }
                catch
                {
                    // Algunas actualizaciones de definiciones o drivers no exponen KB.
                }

                bool mandatory = false;
                bool downloaded = false;
                long size = 0;
                try { mandatory = (bool)u.IsMandatory; } catch { }
                try { downloaded = (bool)u.IsDownloaded; } catch { }
                try { size = Convert.ToInt64(u.MaxDownloadSize); } catch { }

                items.Add(new WindowsUpdateItem(title, kb, mandatory, downloaded, size));
            }

            return new WindowsUpdateScanResult(items);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            return new WindowsUpdateScanResult([], $"No disponible ({ex.Message})");
        }
        finally
        {
            ReleaseCom(searchResult);
            ReleaseCom(searcher);
            ReleaseCom(session);
        }
    }

    public static void OpenWindowsUpdateSettings()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "ms-settings:windowsupdate",
            UseShellExecute = true,
        });
    }

    private static void ReleaseCom(object? obj)
    {
        if (obj is not null && Marshal.IsComObject(obj))
        {
            try { Marshal.FinalReleaseComObject(obj); }
            catch { }
        }
    }
}

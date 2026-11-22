// PulsoWin · MIT · Co-designed with AI coding assistants (Claude & Gemini)
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PulsoWin.Core.Updates;

public sealed record WindowsUpdateInstallItem(string UpdateId, string Title, int ResultCode)
{
    public bool Succeeded => ResultCode is 2 or 3; // orcSucceeded / orcSucceededWithErrors
}

public sealed record WindowsUpdateInstallResult(
    IReadOnlyList<WindowsUpdateInstallItem> Items,
    bool RebootRequired,
    string? Error = null,
    bool Cancelled = false)
{
    public static WindowsUpdateInstallResult Fail(string error) => new([], false, error);
    public static WindowsUpdateInstallResult UserCancelled() => new([], false, "No se dio permiso de administrador.", Cancelled: true);
}

[JsonSerializable(typeof(WindowsUpdateInstallResult))]
internal sealed partial class WindowsUpdateJsonContext : JsonSerializerContext;

/// <summary>
/// Descarga e instala actualizaciones de Windows con el Windows Update Agent (COM).
/// REQUIERE administrador: se ejecuta solo dentro del proceso auxiliar elevado de PulsoWin,
/// y únicamente instala los UpdateID que la persona marcó y confirmó.
/// </summary>
public static class WindowsUpdateInstaller
{
    public static bool IsValidUpdateId(string id) => Guid.TryParseExact(id, "D", out _);

    public static WindowsUpdateInstallResult Install(IReadOnlyCollection<string> updateIds)
    {
        var wanted = new HashSet<string>(updateIds.Where(IsValidUpdateId), StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0) return WindowsUpdateInstallResult.Fail("No hay actualizaciones válidas que instalar.");

        try
        {
            var sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session");
            var collType = Type.GetTypeFromProgID("Microsoft.Update.UpdateColl");
            if (sessionType is null || collType is null)
                return WindowsUpdateInstallResult.Fail("Windows Update Agent no está disponible.");

            dynamic session = Activator.CreateInstance(sessionType)!;
            session.ClientApplicationID = "PulsoWin";
            dynamic result = session.CreateUpdateSearcher().Search("IsInstalled=0 and Type='Software' and IsHidden=0");

            dynamic toInstall = Activator.CreateInstance(collType)!;
            var titles = new List<(string Id, string Title)>();
            int count = result.Updates.Count;
            for (var i = 0; i < count; i++)
            {
                dynamic u = result.Updates.Item(i);
                string id = u.Identity.UpdateID;
                if (!wanted.Contains(id)) continue;
                // La persona confirmó en PulsoWin que se aceptan los términos de las actualizaciones marcadas.
                if (!(bool)u.EulaAccepted) u.AcceptEula();
                toInstall.Add(u);
                titles.Add((id, (string)u.Title));
            }

            if (titles.Count == 0)
                return WindowsUpdateInstallResult.Fail("Las actualizaciones marcadas ya no están pendientes.");

            dynamic downloader = session.CreateUpdateDownloader();
            downloader.Updates = toInstall;
            downloader.Download();

            dynamic installer = session.CreateUpdateInstaller();
            installer.Updates = toInstall;
            dynamic installResult = installer.Install();

            var items = new List<WindowsUpdateInstallItem>();
            for (var i = 0; i < titles.Count; i++)
                items.Add(new WindowsUpdateInstallItem(titles[i].Id, titles[i].Title, (int)installResult.GetUpdateResult(i).ResultCode));

            return new WindowsUpdateInstallResult(items, (bool)installResult.RebootRequired);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException
                                       or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            return WindowsUpdateInstallResult.Fail(ex.Message);
        }
    }

    public static string Serialize(WindowsUpdateInstallResult result) =>
        JsonSerializer.Serialize(result, WindowsUpdateJsonContext.Default.WindowsUpdateInstallResult);

    public static WindowsUpdateInstallResult? Deserialize(string json) =>
        JsonSerializer.Deserialize(json, WindowsUpdateJsonContext.Default.WindowsUpdateInstallResult);
}

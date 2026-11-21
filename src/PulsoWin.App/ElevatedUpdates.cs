using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using PulsoWin.Core.Updates;
using PulsoWin.Presentation.Services;

namespace PulsoWin.App;

/// <summary>
/// Instalación de Windows Update con un único permiso de administrador.
///
/// La ventana de PulsoWin nunca corre como administrador. Para instalar, relanza su propio .exe con
/// <c>--wu-install &lt;token&gt; &lt;UpdateID&gt;…</c> y el verbo "runas" (Windows muestra el aviso UAC).
/// Ese proceso auxiliar no abre ventanas: instala solo los UpdateID recibidos (validados como GUID),
/// escribe el resultado en %LOCALAPPDATA%\PulsoWin\wu-result-&lt;token&gt;.json y termina.
/// La ruta la arma el propio auxiliar a partir de un token GUID: nunca escribe donde le indiquen.
/// </summary>
public static class ElevatedUpdates
{
    public const string Switch = "--wu-install";

    // ------------------------------------------------------------------ lado de la ventana (sin privilegios)

    public static async Task<WindowsUpdateInstallResult> InstallAsync(IReadOnlyList<string> updateIds)
    {
        var ids = updateIds.Where(WindowsUpdateInstaller.IsValidUpdateId).ToList();
        if (ids.Count == 0) return WindowsUpdateInstallResult.Fail("No hay actualizaciones válidas que instalar.");

        var token = Guid.NewGuid().ToString("D");
        var resultFile = ResultPath(token);
        var psi = new ProcessStartInfo(Environment.ProcessPath ?? throw new InvalidOperationException("Ruta del ejecutable desconocida."))
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
            Arguments = $"{Switch} {token} {string.Join(' ', ids)}", // solo GUIDs: no hay nada que escapar
        };

        Process? helper;
        try
        {
            helper = Process.Start(psi);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED: se rechazó el UAC
        {
            return WindowsUpdateInstallResult.UserCancelled();
        }
        if (helper is null) return WindowsUpdateInstallResult.Fail("No se pudo iniciar el instalador.");

        using (helper) await helper.WaitForExitAsync();

        try
        {
            if (!File.Exists(resultFile))
                return WindowsUpdateInstallResult.Fail($"El instalador terminó sin informar resultado (código {helper.ExitCode}).");
            return WindowsUpdateInstaller.Deserialize(await File.ReadAllTextAsync(resultFile))
                   ?? WindowsUpdateInstallResult.Fail("Resultado ilegible del instalador.");
        }
        finally
        {
            try { File.Delete(resultFile); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    // ------------------------------------------------------------------ lado del auxiliar (elevado, sin ventana)

    /// <summary>Corre el auxiliar. Devuelve el código de salida del proceso.</summary>
    public static int RunHelper(string[] args)
    {
        if (args.Length < 3 || !Guid.TryParseExact(args[1], "D", out var token)) return 2;
        var ids = args.Skip(2).Where(WindowsUpdateInstaller.IsValidUpdateId).ToList();
        if (ids.Count != args.Length - 2) return 2; // algún argumento no era un UpdateID válido: no se instala nada

        var result = WindowsUpdateInstaller.Install(ids);
        Directory.CreateDirectory(AppPaths.DataDir);
        File.WriteAllText(ResultPath(token.ToString("D")), WindowsUpdateInstaller.Serialize(result));
        return result.Error is null ? 0 : 1;
    }

    private static string ResultPath(string token) => Path.Combine(AppPaths.DataDir, $"wu-result-{token}.json");
}

// PulsoWin · MIT · Co-designed with AI coding assistants (Claude & Gemini)
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using PulsoWin.Core.Localization;
using PulsoWin.Core.Network;
using PulsoWin.Core.Updates;

namespace PulsoWin.App;

/// <summary>
/// Instalación de Windows Update con un único permiso de administrador.
///
/// La ventana de PulsoWin nunca corre como administrador. Para instalar, relanza su propio .exe con
/// <c>--wu-install &lt;tubería&gt; &lt;PID de la app&gt; &lt;UpdateID&gt;…</c> y el verbo "runas" (Windows muestra el aviso UAC).
/// Ese proceso auxiliar no abre ventanas: instala solo los UpdateID recibidos (validados como GUID) y devuelve el
/// resultado por una tubería privada (<see cref="PrivatePipe"/>), con las dos puntas verificadas por PID.
/// No usa archivos: si el aviso se aprueba con otra cuenta de administrador, su perfil no es el de la persona.
/// </summary>
public static class ElevatedUpdates
{
    public const string Switch = "--wu-install";
    private const string PipePrefix = "PulsoWin-WU-";
    private const int MaxResultChars = 1 << 20;   // un resultado anómalo no puede llenar la memoria

    // ------------------------------------------------------------------ lado de la ventana (sin privilegios)

    public static async Task<WindowsUpdateInstallResult> InstallAsync(IReadOnlyList<string> updateIds)
    {
        var ids = updateIds.Where(WindowsUpdateInstaller.IsValidUpdateId).ToList();
        if (ids.Count == 0) return WindowsUpdateInstallResult.Fail(L.T("Wu_NoValid"));

        var name = PrivatePipe.NewName(PipePrefix);
        using var pipe = PrivatePipe.CreateServer(name);
        var psi = new ProcessStartInfo(Environment.ProcessPath ?? throw new InvalidOperationException("Ruta del ejecutable desconocida."))
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
            // solo un nombre validado, un número y GUIDs: no hay nada que escapar
            Arguments = $"{Switch} {name} {Environment.ProcessId.ToString(CultureInfo.InvariantCulture)} {string.Join(' ', ids)}",
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
        if (helper is null) return WindowsUpdateInstallResult.Fail(L.T("Wu_HelperFailed"));

        using (helper)
        {
            using var stop = new CancellationTokenSource();
            var connect = pipe.WaitForConnectionAsync(stop.Token);
            await Task.WhenAny(connect, helper.WaitForExitAsync());
            if (!connect.IsCompleted) await Task.WhenAny(connect, Task.Delay(500));   // se conectó justo antes de salir
            if (!connect.IsCompletedSuccessfully)
            {
                stop.Cancel();
                await helper.WaitForExitAsync();
                return WindowsUpdateInstallResult.Fail(L.F("Wu_HelperNoResult", helper.ExitCode));
            }

            // El resultado solo se acepta del auxiliar que lanzó esta app.
            if (!PipePeers.TryGetClientProcessId(pipe, out var clientPid) || clientPid != helper.Id)
                return WindowsUpdateInstallResult.Fail(L.T("Wu_HelperBadResult"));

            var text = await ReadLimitedAsync(pipe);
            await helper.WaitForExitAsync();
            return (text is null ? null : WindowsUpdateInstaller.Deserialize(text))
                   ?? WindowsUpdateInstallResult.Fail(L.F("Wu_HelperNoResult", helper.ExitCode));
        }
    }

    private static async Task<string?> ReadLimitedAsync(Stream pipe)
    {
        using var reader = new StreamReader(pipe);
        var buffer = new char[4096];
        var text = new System.Text.StringBuilder();
        int read;
        while ((read = await reader.ReadAsync(buffer)) > 0)
        {
            text.Append(buffer, 0, read);
            if (text.Length > MaxResultChars) return null;
        }
        return text.Length == 0 ? null : text.ToString();
    }

    // ------------------------------------------------------------------ lado del auxiliar (elevado, sin ventana)

    /// <summary>
    /// Corre el auxiliar. Devuelve el código de salida del proceso: 0 instalado, 1 error al instalar, 2 argumentos,
    /// 3 la app no es este ejecutable, 4 la app ya no esperaba, 5 la tubería es de otro proceso.
    /// </summary>
    public static int RunHelper(string[] args)
    {
        if (args.Length < 4 || args[0] != Switch || !PrivatePipe.IsValidName(args[1], PipePrefix)
            || !int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out var parentPid))
            return 2;
        var ids = args.Skip(3).Where(WindowsUpdateInstaller.IsValidUpdateId).ToList();
        if (ids.Count != args.Length - 3) return 2; // algún argumento no era un UpdateID válido: no se instala nada

        try
        {
            using var parent = Process.GetProcessById(parentPid);
            if (!PrivatePipe.ParentIsSameExecutable(parent)) return 3;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return 3;
        }

        try
        {
            using var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.Out);
            pipe.Connect(10_000);
            // Se verifica antes de instalar: el resultado solo va a la app que pidió la instalación.
            if (!PipePeers.TryGetServerProcessId(pipe, out var serverPid) || serverPid != parentPid) return 5;

            var result = WindowsUpdateInstaller.Install(ids);
            using var writer = new StreamWriter(pipe);
            writer.Write(WindowsUpdateInstaller.Serialize(result));
            writer.Flush();
            return result.Error is null ? 0 : 1;
        }
        catch (TimeoutException)
        {
            return 4;
        }
        catch (IOException)
        {
            return 4;   // la app cerró la tubería (se cerró la ventana)
        }
    }
}

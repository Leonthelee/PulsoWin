// PulsoWin · MIT · Co-designed with AI coding assistants (Claude & Gemini)
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using PulsoWin.Presentation.Services;
using PulsoWin.Core.Localization;

namespace PulsoWin.App;

/// <summary>
/// La app solo se cierra cuando la persona cierra la ventana. Un error inesperado se registra en
/// %LOCALAPPDATA%\PulsoWin\logs\errores.log y se muestra, pero no tumba la aplicación.
/// </summary>
public partial class App : Application
{
    private static readonly string ErrorLog = Path.Combine(AppPaths.DataDir, "logs", "errores.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        // Idioma antes de cualquier texto (también en el instalador elevado, cuyos mensajes llegan a la app).
        L.Culture = LanguagePreference.Resolve(LanguagePreference.Load(), CultureInfo.CurrentUICulture);
        CultureInfo.DefaultThreadCurrentUICulture = L.Culture;
        Thread.CurrentThread.CurrentUICulture = L.Culture;

        // Modo auxiliar elevado (instalar Windows Update): sin ventana, en un hilo MTA para COM, y termina.
        if (e.Args.Length > 0 && e.Args[0] == ElevatedUpdates.Switch)
        {
            int code;
            try
            {
                code = Task.Run(() => ElevatedUpdates.RunHelper(e.Args)).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log("instalador de Windows Update", ex);
                code = 1;
            }
            Shutdown(code);
            return;
        }

        // Pide administrador al abrir (para ver la red por proceso). Si se acepta, esta instancia se cierra y sigue
        // la elevada; si se rechaza, la app abre igual en modo estándar. "--sin-admin" se salta la petición.
        if (ElevationPolicy.ShouldRequestAdmin(Core.Metrics.SystemMetricsService.IsRunningAsAdministrator(), e.Args)
            && TryRelaunchElevated(e.Args))
        {
            Shutdown(0);
            return;
        }

        // Errores en el hilo de la interfaz (incluye comandos async): se registran y la app sigue viva.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        // Tareas en segundo plano cuyo error nadie esperó: se registran y se marcan como observadas.
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log("tarea en segundo plano", args.Exception);
            args.SetObserved();
        };
        // Último recurso (hilos ajenos a WPF): .NET no permite seguir, pero al menos queda registro.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) Log("error fatal", ex);
        };
        base.OnStartup(e);
        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        Log("interfaz", e.Exception);
        MessageBox.Show(
            L.F("Err_Unexpected", e.Exception.Message, ErrorLog),
            "PulsoWin", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    /// <summary>Relanza este mismo .exe con el aviso de administrador. false si se rechazó o no se pudo.</summary>
    private static bool TryRelaunchElevated(string[] args)
    {
        try
        {
            var arguments = string.Join(' ', args.Append(ElevationPolicy.RelaunchedSwitch).Select(a => $"\"{a.Replace("\"", "")}\""));
            using var elevated = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = arguments,
            });
            return elevated is not null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false; // 1223 = la persona rechazó el aviso: se sigue sin administrador
        }
    }

    private static void Log(string origin, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ErrorLog)!);
            File.AppendAllText(ErrorLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ({origin}) {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception logEx) when (logEx is IOException or UnauthorizedAccessException)
        {
            // Si ni siquiera se puede escribir el registro, no hay nada más que hacer sin cerrar la app.
        }
    }
}

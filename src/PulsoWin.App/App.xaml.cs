using System.IO;
using System.Windows;
using System.Windows.Threading;
using PulsoWin.Presentation.Services;

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
            $"Algo salió mal, pero PulsoWin sigue abierto y no se borró nada a medias sin avisar.\n\n{e.Exception.Message}\n\nDetalle en: {ErrorLog}",
            "PulsoWin", MessageBoxButton.OK, MessageBoxImage.Warning);
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

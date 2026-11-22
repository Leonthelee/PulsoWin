// PulsoWin · MIT · Co-designed with AI coding assistants (Claude & Gemini)
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using PulsoWin.Presentation.ViewModels;

namespace PulsoWin.App;

/// <summary>
/// Vista principal. Solo conecta la ventana con su ViewModel y gestiona su ciclo de vida:
/// la lógica vive en PulsoWin.Presentation.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly CancellationTokenSource _lifetime = new();

    public MainWindow()
    {
        InitializeComponent();
        _vm = MainViewModel.Create(new WpfDialogService(this), ElevatedUpdates.InstallAsync);
        _vm.UseSignatureFrom(typeof(App).Assembly);
        DataContext = _vm;

        // Huevo de pascua: PreviewKeyDown ve las flechas aunque una tabla tenga el foco (y no las consume).
        PreviewKeyDown += (_, e) =>
        {
            if (_vm.IsEasterEggOpen && e.Key == System.Windows.Input.Key.Escape)
            {
                _vm.CloseEasterEggCommand.Execute(null);
                e.Handled = true;
                return;
            }
            _vm.OnKey(e.Key.ToString());
        };

        SourceInitialized += (_, _) => UseDarkTitleBar();

        // Motor único de métricas (CPU, RAM, red): arranca con la ventana, en segundo plano, sin bloquear la interfaz.
        // Ocultar esta ventana para el Modo Gadget NO lo detiene (Loaded no se repite al mostrarla de nuevo).
        // Al cerrar, _lifetime se cancela y el servicio libera la fuente de red (y detiene la sesión ETW).
        _gadget = new GadgetMode(this, _vm.Metrics);
        Loaded += async (_, _) =>
        {
            try
            {
                await _vm.Metrics.RunAsync(_lifetime.Token);
            }
            catch (OperationCanceledException)
            {
                // Cierre normal de la ventana.
            }
        };

        Loaded += async (_, _) =>
        {
            try
            {
                await _vm.Hardware.RunAsync(_lifetime.Token);
            }
            catch (OperationCanceledException)
            {
                // Cierre normal de la ventana.
            }
        };
        Closed += (_, _) =>
        {
            _lifetime.Cancel();
            _gadget.Dispose();   // cierra el gadget y quita el ícono de la bandeja
            _vm.Dispose();
        };
    }

    private readonly GadgetMode _gadget;

    private void GadgetMode_Click(object sender, RoutedEventArgs e) => _gadget.Enter();

    private void ProfileLink_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.Signature.HasSafeProfileLink) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_vm.Signature.Profile) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Sin navegador predeterminado: no pasa nada, el enlace queda visible para copiarlo.
        }
    }

    /// <summary>Barra de título oscura (Windows 10 20H1+ y 11), a juego con el tema.</summary>
    private void UseDarkTitleBar()
    {
        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        var enabled = 1;
        var hwnd = new WindowInteropHelper(this).Handle;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}

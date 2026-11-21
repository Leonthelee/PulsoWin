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
        DataContext = _vm;

        SourceInitialized += (_, _) => UseDarkTitleBar();
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
            _vm.Dispose();
        };
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

// PulsoWin · MIT · Co-designed with AI coding assistants (Claude & Gemini)
using System.Windows;
using PulsoWin.Presentation.ViewModels;
using Forms = System.Windows.Forms;
using PulsoWin.Core.Localization;

namespace PulsoWin.App;

/// <summary>
/// Cambia entre la app completa y el Modo Gadget sin tocar el motor de métricas:
/// solo oculta una ventana y muestra la otra. Mientras el gadget está visible hay un ícono en la bandeja
/// del sistema (la ventana flotante no aparece en la barra de tareas).
/// </summary>
public sealed class GadgetMode : IDisposable
{
    private readonly Window _main;
    private readonly MetricsViewModel _metrics;
    private GadgetWindow? _gadget;
    private Forms.NotifyIcon? _tray;
    private Forms.ToolStripMenuItem? _pinItem;
    private bool _placed;

    public GadgetMode(Window main, MetricsViewModel metrics)
    {
        _main = main;
        _metrics = metrics;
    }

    public bool IsActive => _gadget?.IsVisible == true;

    public void Enter()
    {
        _gadget ??= CreateGadget();
        _gadget.Show();
        if (!_placed)
        {
            _gadget.PlaceBottomRight();
            _placed = true;
        }
        ShowTray();
        _main.Hide();
    }

    public void Exit()
    {
        _main.Show();
        if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
        _main.Activate();
        _gadget?.Hide();
        if (_tray is not null) _tray.Visible = false;
    }

    private GadgetWindow CreateGadget()
    {
        var gadget = new GadgetWindow { DataContext = _metrics };
        gadget.ReturnRequested += (_, _) => Exit();
        gadget.Closed += (_, _) => _gadget = null;
        return gadget;
    }

    private void ShowTray()
    {
        if (_tray is null)
        {
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add(L.T("Gadget_Back"), null, (_, _) => Exit());
            _pinItem = new Forms.ToolStripMenuItem(L.T("Gadget_Pin")) { CheckOnClick = true, Checked = true };
            _pinItem.CheckedChanged += (_, _) => { if (_gadget is not null) _gadget.Topmost = _pinItem.Checked; };
            menu.Items.Add(_pinItem);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(L.T("Gadget_Exit"), null, (_, _) => _main.Close());
            menu.Opening += (_, _) => { if (_gadget is not null && _pinItem is not null) _pinItem.Checked = _gadget.Topmost; };

            _tray = new Forms.NotifyIcon
            {
                Icon = LoadIcon(),
                Text = L.T("Gadget_Title"),
                ContextMenuStrip = menu,
            };
            _tray.DoubleClick += (_, _) => Exit();
        }
        _tray.Visible = true;
    }

    private static System.Drawing.Icon LoadIcon()
    {
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/pulsowin.ico"));
        using var stream = resource!.Stream;
        return new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
    }

    public void Dispose()
    {
        if (_tray is not null)
        {
            _tray.Visible = false;   // sin esto el ícono queda "fantasma" en la bandeja hasta pasar el mouse
            _tray.Dispose();
            _tray = null;
        }
        _gadget?.Close();
    }
}

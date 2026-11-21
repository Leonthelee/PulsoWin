using System.Windows;
using System.Windows.Input;

namespace PulsoWin.App;

/// <summary>Ventana del Modo Gadget. Solo muestra; el monitoreo sigue corriendo en el motor único.</summary>
public partial class GadgetWindow : Window
{
    public GadgetWindow()
    {
        InitializeComponent();
    }

    /// <summary>La persona pidió volver a la app completa (doble clic o botón ⤢).</summary>
    public event EventHandler? ReturnRequested;

    private void Shell_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ReturnRequested?.Invoke(this, EventArgs.Empty);
            return;
        }
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Return_Click(object sender, RoutedEventArgs e) => ReturnRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Primera vez: esquina inferior derecha del área de trabajo.</summary>
    public void PlaceBottomRight()
    {
        UpdateLayout();
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 8;
        Top = area.Bottom - ActualHeight - 8;
    }
}

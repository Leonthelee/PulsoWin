// PulsoWin · MIT · Co-designed with AI coding assistants (Claude & Gemini)
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using PulsoWin.Presentation.Services;

namespace PulsoWin.App;

/// <summary>Diálogos reales de WPF para la capa de presentación.</summary>
public sealed class WpfDialogService(Window owner) : IDialogService
{
    public bool Confirm(string title, string message) =>
        MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
        == MessageBoxResult.Yes;
}

/// <summary>true → false. Sirve para deshabilitar tablas mientras se trabaja.</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}

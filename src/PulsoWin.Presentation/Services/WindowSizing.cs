// PulsoWin · MIT · Co-designed with AI coding assistants (Claude & Gemini)
namespace PulsoWin.Presentation.Services;

/// <summary>
/// Geometría calculada para la ventana principal en unidades lógicas (DIPs):
/// tamaño inicial, mínimos seguros y posición centrada dentro del área útil del monitor.
/// </summary>
public readonly record struct WindowBounds(
    double Width,
    double Height,
    double MinWidth,
    double MinHeight,
    double Left,
    double Top);

/// <summary>
/// Adapta automáticamente el tamaño y la posición de la ventana principal a cualquier
/// resolución o escala DPI de Windows, evitando que se abra gigante o se corte fuera de pantalla.
/// </summary>
public static class WindowSizing
{
    /// <summary>Tamaño cómodo en pantallas estándar o grandes (1080p, 1440p, 4K).</summary>
    public const double PreferredWidth = 1240;
    public const double PreferredHeight = 860;

    /// <summary>Mínimos deseados; se reducen automáticamente si el monitor es más chico.</summary>
    public const double PreferredMinWidth = 980;
    public const double PreferredMinHeight = 680;

    /// <summary>Proporción máxima del área útil que ocupa al abrirse, dejando margen visual.</summary>
    public const double MaxWorkAreaWidthRatio = 0.92;
    public const double MaxWorkAreaHeightRatio = 0.90;

    public static WindowBounds FitToWorkArea(
        double workAreaLeft,
        double workAreaTop,
        double workAreaWidth,
        double workAreaHeight)
    {
        if (!double.IsFinite(workAreaWidth) || !double.IsFinite(workAreaHeight) ||
            workAreaWidth <= 0 || workAreaHeight <= 0)
        {
            return new WindowBounds(
                PreferredWidth,
                PreferredHeight,
                PreferredMinWidth,
                PreferredMinHeight,
                0,
                0);
        }

        double safeLeft = double.IsFinite(workAreaLeft) ? workAreaLeft : 0;
        double safeTop = double.IsFinite(workAreaTop) ? workAreaTop : 0;

        double maxWidth = Math.Floor(workAreaWidth * MaxWorkAreaWidthRatio);
        double maxHeight = Math.Floor(workAreaHeight * MaxWorkAreaHeightRatio);

        double width = Math.Clamp(Math.Min(PreferredWidth, maxWidth), Math.Min(320, workAreaWidth), workAreaWidth);
        double height = Math.Clamp(Math.Min(PreferredHeight, maxHeight), Math.Min(240, workAreaHeight), workAreaHeight);

        double minWidth = Math.Min(PreferredMinWidth, width);
        double minHeight = Math.Min(PreferredMinHeight, height);

        double left = safeLeft + Math.Floor(Math.Max(0, (workAreaWidth - width) / 2.0));
        double top = safeTop + Math.Floor(Math.Max(0, (workAreaHeight - height) / 2.0));

        return new WindowBounds(width, height, minWidth, minHeight, left, top);
    }
}

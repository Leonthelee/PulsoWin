// PulsoWin · MIT · Co-designed with AI coding assistants (Claude & Gemini)
using PulsoWin.Presentation.Services;

namespace PulsoWin.Tests;

public class WindowSizingTests
{
    [Theory]
    [InlineData(2560, 1400, 1240, 860, 980, 680)] // 1440p al 100%: tope cómodo, nunca gigante
    [InlineData(1920, 1040, 1240, 860, 980, 680)] // 1080p al 100%: equilibrado y centrado
    [InlineData(1536, 816, 1240, 734, 980, 680)]  // 1080p al 125%: respeta barra de tareas
    [InlineData(1366, 728, 1240, 655, 980, 655)]  // 1366x768 al 100%: notebook clásico
    [InlineData(1280, 672, 1177, 604, 980, 604)]  // 1080p al 150%: notebook 14" sin cortes
    [InlineData(800, 560, 736, 504, 736, 504)]    // 800x600: ambos mínimos se adaptan
    public void FitToWorkArea_adapts_to_any_screen_without_overflow(
        double areaW, double areaH,
        double expectedW, double expectedH,
        double expectedMinW, double expectedMinH)
    {
        var b = WindowSizing.FitToWorkArea(0, 0, areaW, areaH);

        Assert.Equal(expectedW, b.Width);
        Assert.Equal(expectedH, b.Height);
        Assert.Equal(expectedMinW, b.MinWidth);
        Assert.Equal(expectedMinH, b.MinHeight);
        Assert.True(b.MinWidth <= b.Width && b.Width <= areaW);
        Assert.True(b.MinHeight <= b.Height && b.Height <= areaH);
        Assert.True(b.Left >= 0 && b.Left + b.Width <= areaW);
        Assert.True(b.Top >= 0 && b.Top + b.Height <= areaH);
    }

    [Fact]
    public void FitToWorkArea_respects_non_zero_work_area_origin()
    {
        // Barra de tareas arriba (48 DIPs) o izquierda (64 DIPs)
        var b = WindowSizing.FitToWorkArea(64, 48, 1500, 800);

        Assert.Equal(1240, b.Width);
        Assert.Equal(720, b.Height);
        Assert.Equal(64 + 130, b.Left);
        Assert.Equal(48 + 40, b.Top);
        Assert.True(b.Left + b.Width <= 64 + 1500);
        Assert.True(b.Top + b.Height <= 48 + 800);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-100, 800)]
    [InlineData(double.NaN, 800)]
    [InlineData(1920, double.PositiveInfinity)]
    public void FitToWorkArea_falls_back_cleanly_on_invalid_area(double areaW, double areaH)
    {
        var b = WindowSizing.FitToWorkArea(0, 0, areaW, areaH);

        Assert.Equal(WindowSizing.PreferredWidth, b.Width);
        Assert.Equal(WindowSizing.PreferredHeight, b.Height);
        Assert.Equal(WindowSizing.PreferredMinWidth, b.MinWidth);
        Assert.Equal(WindowSizing.PreferredMinHeight, b.MinHeight);
    }
}

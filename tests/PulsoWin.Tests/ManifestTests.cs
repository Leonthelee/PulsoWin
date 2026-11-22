// PulsoWin · MIT · Co-designed with AI coding assistants (Claude & Gemini)
using System.Xml.Linq;

namespace PulsoWin.Tests;

/// <summary>
/// El manifiesto de la app: si su XML falla, Windows no abre el .exe («configuración en paralelo no es correcta»).
/// Además fija los requisitos de Windows 1.2 (compatibilidad), 1.3 (DPI) y 9.1/9.2 (sin administrador).
/// </summary>
public class ManifestTests
{
    private static XDocument Manifest()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PulsoWin.slnx"))) dir = dir.Parent;
        return XDocument.Load(Path.Combine(dir!.FullName, "src", "PulsoWin.App", "app.manifest"));   // lanza si el XML es inválido
    }

    [Fact]
    public void App_runs_as_invoker_never_asking_for_administrator_at_startup()
    {
        var level = Manifest().Descendants().Single(e => e.Name.LocalName == "requestedExecutionLevel");
        Assert.Equal("asInvoker", level.Attribute("level")!.Value);
        Assert.Equal("false", level.Attribute("uiAccess")!.Value);
    }

    [Fact]
    public void Dpi_awareness_is_declared_per_monitor_in_the_manifest()
    {
        var awareness = Manifest().Descendants().Single(e => e.Name.LocalName == "dpiAwareness");
        Assert.Equal("PerMonitorV2", awareness.Value);
        Assert.Equal("true/pm", Manifest().Descendants().Single(e => e.Name.LocalName == "dpiAware").Value);  // Windows 10 antiguos
    }

    [Fact]
    public void Windows_10_and_11_are_declared_as_supported()
    {
        Assert.Contains(Manifest().Descendants().Where(e => e.Name.LocalName == "supportedOS"),
            e => e.Attribute("Id")!.Value == "{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}");
    }
}

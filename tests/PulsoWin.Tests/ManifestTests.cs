// PulsoWin · MIT · Co-designed with AI coding assistants (Claude & Gemini)
using System.Xml.Linq;

namespace PulsoWin.Tests;

/// <summary>
/// El manifiesto de la app: si su XML falla, Windows no abre el .exe («configuración en paralelo no es correcta»).
/// Además fija los requisitos de Windows 1.2 (compatibilidad), 1.3 (DPI) y 9.1/9.2 (sin administrador).
/// </summary>
public class ManifestTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PulsoWin.slnx"))) dir = dir.Parent;
        return dir!.FullName;
    }

    private static XDocument Manifest() =>
        XDocument.Load(Path.Combine(Root(), "src", "PulsoWin.App", "app.manifest"));   // lanza si el XML es inválido

    private static XDocument StorePackage() =>
        XDocument.Load(Path.Combine(Root(), "packaging", "msix", "AppxManifest.xml"));

    [Fact]
    public void Store_package_runs_the_app_exe_with_only_the_justified_capabilities()
    {
        var pkg = StorePackage();
        var app = pkg.Descendants().Single(e => e.Name.LocalName == "Application");
        Assert.Equal("PulsoWin.exe", app.Attribute("Executable")!.Value);
        Assert.Equal("Windows.FullTrustApplication", app.Attribute("EntryPoint")!.Value);

        var caps = pkg.Descendants().Where(e => e.Name.LocalName == "Capability").Select(e => e.Attribute("Name")!.Value);
        Assert.Equal(["allowElevation", "runFullTrust", "unvirtualizedResources"], caps.Order());

        // Sin esto el limpiador borraría sobre una copia privada del paquete y no liberaría nada.
        Assert.Equal("disabled", pkg.Descendants().Single(e => e.Name.LocalName == "FileSystemWriteVirtualization").Value);
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

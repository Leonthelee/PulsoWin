using System.ComponentModel;
using PulsoWin.Core.Updates;
using Xunit;

namespace PulsoWin.Tests;

public sealed class WingetClientTests
{
    private const string FakeWinget = @"C:\Users\prueba\AppData\Local\Microsoft\WindowsApps\winget.exe";

    private sealed class FakeRunner : IProcessRunner
    {
        public List<(string FileName, IReadOnlyList<string> Args)> Calls { get; } = [];
        public Func<string, IReadOnlyList<string>, ProcessResult>? Handler { get; set; }

        public Task<ProcessResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((fileName, arguments));
            var handler = Handler ?? ((_, _) => new ProcessResult(0, "", ""));
            return Task.FromResult(handler(fileName, arguments));
        }
    }

    [Fact]
    public void ParseUpgradeOutput_ParsesSpanishTableWithSpinnersAndFooter()
    {
        const string sample =
            "   - \r   \\ \r   | \rNombre                        Id                        Versión   Disponible Origen\r\n" +
            "-------------------------------------------------------------------------------------------\r\n" +
            "GitHub CLI                    GitHub.cli                2.103.3   2.105.0    winget\r\n" +
            "Node.js                       OpenJS.NodeJS.LTS         26.13.0   26.13.1    winget\r\n" +
            "2 actualizaciones disponibles.\r\n";

        var packages = WingetParser.ParseUpgradeOutput(sample);

        Assert.Equal(2, packages.Count);
        Assert.Equal("GitHub.cli", packages[0].Id);
        Assert.Equal("2.103.3", packages[0].InstalledVersion);
        Assert.Equal("2.105.0", packages[0].AvailableVersion);
        Assert.Equal("winget", packages[0].Source);
        Assert.Equal("OpenJS.NodeJS.LTS", packages[1].Id);
    }

    [Fact]
    public void ParseUpgradeOutput_ParsesEnglishTableAndStopsAtAdditionalNotice()
    {
        const string sample =
            "Name                          Id                        Version   Available  Source\n" +
            "-----------------------------------------------------------------------------------\n" +
            "Tailscale                     Tailscale.Tailscale       1.104.5   1.104.7    winget\n" +
            "The following packages have an upgrade available, but require explicit targeting:\n" +
            "Ignored App                   Ignored.App               1.0       2.0        winget\n";

        var packages = WingetParser.ParseUpgradeOutput(sample);

        var pkg = Assert.Single(packages);
        Assert.Equal("Tailscale.Tailscale", pkg.Id);
        Assert.Equal("1.104.7", pkg.AvailableVersion);
    }

    [Theory]
    [InlineData("GitHub.cli", true)]
    [InlineData("Microsoft.VCRedist.2015+.x64", true)]
    [InlineData("XP8K4RGX25G3GM", true)]
    [InlineData("", false)]
    [InlineData("foo; calc.exe", false)]
    [InlineData("foo && del *", false)]
    [InlineData("../evil", false)]
    [InlineData("has space", false)]
    public void IsValidPackageId_ValidatesStrictly(string id, bool expected)
    {
        Assert.Equal(expected, WingetParser.IsValidPackageId(id));
    }

    [Fact]
    public async Task ListUpgradesAsync_PassesExpectedArgsAndHandlesMissingWinget()
    {
        var runner = new FakeRunner
        {
            Handler = (_, _) => throw new Win32Exception("not found")
        };
        var client = new WingetClient(runner, wingetPath: FakeWinget);

        var result = await client.ListUpgradesAsync();

        Assert.False(result.IsAvailable);
        Assert.Empty(result.Packages);
        var call = Assert.Single(runner.Calls);
        Assert.Equal(FakeWinget, call.FileName);
        Assert.Equal(["upgrade", "--accept-source-agreements"], call.Args);
    }

    [Fact]
    public async Task UpgradeAsync_RejectsInvalidIdWithoutRunningProcess()
    {
        var runner = new FakeRunner();
        var client = new WingetClient(runner, wingetPath: FakeWinget);

        var result = await client.UpgradeAsync("bad;id");

        Assert.False(result.Succeeded);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task UpgradeAsync_UsesExactSilentArguments()
    {
        var runner = new FakeRunner
        {
            Handler = (_, _) => new ProcessResult(0, "Instalado correctamente", "")
        };
        var client = new WingetClient(runner, wingetPath: FakeWinget);

        var result = await client.UpgradeAsync("GitHub.cli");

        Assert.True(result.Succeeded);
        var call = Assert.Single(runner.Calls);
        Assert.Equal(FakeWinget, call.FileName);
        Assert.Equal(
            [
                "upgrade",
                "--id", "GitHub.cli",
                "--exact",
                "--accept-source-agreements",
                "--accept-package-agreements",
                "--silent"
            ],
            call.Args);
    }

    [Fact]
    public async Task Never_runs_winget_by_bare_name()
    {
        var runner = new FakeRunner { Handler = (_, _) => new ProcessResult(0, "", "") };
        var client = new WingetClient(runner);

        await client.ListUpgradesAsync();

        // Con App Installer: ruta completa al alias. Sin él: no se ejecuta nada.
        Assert.All(runner.Calls, c => Assert.True(Path.IsPathFullyQualified(c.FileName), c.FileName));
    }

    [Fact]
    public void Default_path_points_to_the_user_WindowsApps_alias()
    {
        var path = WingetClient.DefaultWingetPath();
        if (path is not null)
            Assert.EndsWith(Path.Combine("Microsoft", "WindowsApps", "winget.exe"), path, StringComparison.OrdinalIgnoreCase);
    }
}

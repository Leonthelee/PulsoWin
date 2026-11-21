using PulsoWin.Core.Cleaning;
using PulsoWin.Core.Sensors;
using PulsoWin.Core.Updates;
using PulsoWin.Presentation.Services;
using PulsoWin.Presentation.ViewModels;

namespace PulsoWin.Tests;

internal sealed class FakeDialogs(bool answer) : IDialogService
{
    public int Asked { get; private set; }
    public bool Confirm(string title, string message)
    {
        Asked++;
        return answer;
    }
}

internal sealed class FakeRecycleBin(long bytes = 0, long items = 0) : IRecycleBin
{
    public int Emptied { get; private set; }
    public (long Bytes, long Items) Query() => (bytes, items);
    public bool Empty()
    {
        Emptied++;
        return true;
    }
}

/// <summary>
/// La capa de presentación se prueba sin abrir ventanas. Nunca toca la papelera real
/// (FakeRecycleBin) y solo borra dentro de carpetas temporales propias de cada prueba.
/// </summary>
public class ViewModelTests
{
    private static CleanViewModel CreateClean(TempDir dir, IDialogService dialogs, IRecycleBin? bin = null) =>
        new(
            [new CleanRule { Id = "tmp", Name = "Temporales de prueba", Category = "Prueba", Base = dir.Path }],
            new FakeProbe(),
            dialogs,
            bin ?? new FakeRecycleBin(),
            new StatusBar(),
            Path.Combine(dir.Path, "_datos"),
            Array.Empty<ISensorSource>());

    [Fact]
    public async Task Clean_is_disabled_until_a_scan_finds_something()
    {
        using var dir = new TempDir();
        dir.File("a.tmp", 100);
        var vm = CreateClean(dir, new FakeDialogs(true));

        Assert.False(vm.CleanCommand.CanExecute(null));
        await vm.ScanCommand.ExecuteAsync(null);
        Assert.True(vm.CleanCommand.CanExecute(null));
        Assert.Equal("100 B", vm.FreeableText);
    }

    [Fact]
    public async Task Cancelling_the_confirmation_deletes_nothing()
    {
        using var dir = new TempDir();
        var file = dir.File("a.tmp", 100);
        var bin = new FakeRecycleBin(bytes: 500, items: 3);
        var dialogs = new FakeDialogs(false);
        var vm = CreateClean(dir, dialogs, bin);

        await vm.ScanCommand.ExecuteAsync(null);
        await vm.CleanCommand.ExecuteAsync(null);

        Assert.Equal(1, dialogs.Asked);
        Assert.True(File.Exists(file));
        Assert.Equal(0, bin.Emptied);
    }

    [Fact]
    public async Task Confirming_deletes_scanned_files_and_reports_before_after()
    {
        using var dir = new TempDir();
        var file = dir.File("a.tmp", 100);
        var bin = new FakeRecycleBin(bytes: 500, items: 3);
        var vm = CreateClean(dir, new FakeDialogs(true), bin);

        await vm.ScanCommand.ExecuteAsync(null);
        await vm.CleanCommand.ExecuteAsync(null);

        Assert.False(File.Exists(file));
        Assert.Equal(1, bin.Emptied);
        Assert.StartsWith("Borrado:", vm.BeforeAfter);
        Assert.False(vm.CleanCommand.CanExecute(null)); // hay que volver a analizar antes de otra limpieza
    }

    private static UpdatesViewModel CreateUpdates(FakeRunnerForVm runner, IDialogService dialogs) =>
        new(new WingetClient(runner, wingetPath: @"C:\fake\winget.exe"),
            () => new WindowsUpdateScanResult([]), () => { }, dialogs, new StatusBar(),
            Path.Combine(Path.GetTempPath(), "pulsowin-tests"));

    private const string WingetTable = """
        Nombre      Id            Versión  Disponible Origen
        ---------------------------------------------------------
        GitHub CLI  GitHub.cli    2.40.0   2.41.0     winget
        1 actualizaciones disponibles.
        """;

    [Fact]
    public async Task Packages_arrive_unselected_and_nothing_installs_without_a_choice()
    {
        var runner = new FakeRunnerForVm(WingetTable);
        var dialogs = new FakeDialogs(true);
        var vm = CreateUpdates(runner, dialogs);

        await vm.SearchCommand.ExecuteAsync(null);
        var package = Assert.Single(vm.Packages);
        Assert.False(package.IsSelected);

        await vm.UpgradeSelectedCommand.ExecuteAsync(null);
        Assert.Equal(0, dialogs.Asked);
        Assert.DoesNotContain(runner.Calls, c => c.Contains("--id"));
    }

    [Fact]
    public async Task Upgrade_runs_only_after_confirmation()
    {
        var runner = new FakeRunnerForVm(WingetTable);
        var vm = CreateUpdates(runner, new FakeDialogs(false));
        await vm.SearchCommand.ExecuteAsync(null);
        vm.Packages[0].IsSelected = true;

        await vm.UpgradeSelectedCommand.ExecuteAsync(null);
        Assert.DoesNotContain(runner.Calls, c => c.Contains("--id"));

        var confirmed = CreateUpdates(runner, new FakeDialogs(true));
        await confirmed.SearchCommand.ExecuteAsync(null);
        confirmed.Packages[0].IsSelected = true;
        await confirmed.UpgradeSelectedCommand.ExecuteAsync(null);
        Assert.Contains(runner.Calls, c => c.Contains("GitHub.cli"));
    }

    [Fact]
    public async Task Start_goes_to_the_clean_tab_and_scans()
    {
        using var dir = new TempDir();
        dir.File("a.tmp", 100);
        var status = new StatusBar();
        var clean = CreateClean(dir, new FakeDialogs(false));
        var vm = new MainViewModel(clean, new HardwareViewModel([]),
            CreateUpdates(new FakeRunnerForVm(""), new FakeDialogs(false)), status, []);
        vm.SelectedTab = 2;

        await vm.StartCommand.ExecuteAsync(null);

        Assert.Equal(MainViewModel.CleanTab, vm.SelectedTab);
        Assert.Equal("100 B", clean.FreeableText);
        Assert.Equal("PULSA START", vm.HeroCaption);
    }
}

internal sealed class FakeRunnerForVm(string listOutput) : IProcessRunner
{
    public List<string> Calls { get; } = [];

    public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        Calls.Add(string.Join(' ', arguments));
        return Task.FromResult(arguments.Contains("--id")
            ? new ProcessResult(0, "ok", "")
            : new ProcessResult(0, listOutput, ""));
    }
}

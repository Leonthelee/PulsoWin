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

    private const string KbA = "11111111-1111-1111-1111-111111111111";
    private const string KbB = "22222222-2222-2222-2222-222222222222";

    private static readonly WindowsUpdateScanResult TwoWindowsUpdates = new(
    [
        new WindowsUpdateItem("Actualización de seguridad A", "KB5000001", true, false, 1024, KbA),
        new WindowsUpdateItem("Definiciones B", "KB5000002", false, true, 2048, KbB),
    ]);

    /// <summary>Instalador falso de Windows Update: registra qué UpdateID le pidieron instalar.</summary>
    private sealed class FakeWindowsInstaller(WindowsUpdateInstallResult? forced = null)
    {
        public List<string> Requested { get; } = [];

        public Task<WindowsUpdateInstallResult> Install(IReadOnlyList<string> ids)
        {
            Requested.AddRange(ids);
            return Task.FromResult(forced ?? new WindowsUpdateInstallResult(
                ids.Select(id => new WindowsUpdateInstallItem(id, "t", 2)).ToList(), RebootRequired: true));
        }
    }

    private static UpdatesViewModel CreateUpdates(FakeRunnerForVm runner, IDialogService dialogs,
        WindowsUpdateScanResult? windows = null, FakeWindowsInstaller? installer = null) =>
        new(new WingetClient(runner, wingetPath: @"C:\fake\winget.exe"),
            () => windows ?? new WindowsUpdateScanResult([]),
            (installer ?? new FakeWindowsInstaller()).Install,
            dialogs, new StatusBar(),
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
        var package = Assert.Single(vm.Items);
        Assert.False(package.IsSelected);

        await vm.ProceedCommand.ExecuteAsync(null);
        Assert.Equal(0, dialogs.Asked);
        Assert.DoesNotContain(runner.Calls, c => c.Contains("--id"));
    }

    [Fact]
    public async Task Upgrade_runs_only_after_confirmation()
    {
        var runner = new FakeRunnerForVm(WingetTable);
        var vm = CreateUpdates(runner, new FakeDialogs(false));
        await vm.SearchCommand.ExecuteAsync(null);
        vm.Items[0].IsSelected = true;

        await vm.ProceedCommand.ExecuteAsync(null);
        Assert.DoesNotContain(runner.Calls, c => c.Contains("--id"));

        var confirmed = CreateUpdates(runner, new FakeDialogs(true));
        await confirmed.SearchCommand.ExecuteAsync(null);
        confirmed.Items[0].IsSelected = true;
        await confirmed.ProceedCommand.ExecuteAsync(null);
        Assert.Contains(runner.Calls, c => c.Contains("GitHub.cli"));
    }

    [Fact]
    public async Task Search_lists_windows_and_programs_together_in_one_list()
    {
        var vm = CreateUpdates(new FakeRunnerForVm(WingetTable), new FakeDialogs(true), TwoWindowsUpdates);

        await vm.SearchCommand.ExecuteAsync(null);

        Assert.Equal(3, vm.Items.Count);
        Assert.Equal(2, vm.Items.Count(i => i.Kind == UpdateKind.Windows));
        Assert.Contains("2 de Windows y 1 de programas", vm.Summary);
        Assert.All(vm.Items, i => Assert.False(i.IsSelected));
    }

    [Fact]
    public async Task Proceed_installs_only_the_marked_windows_updates()
    {
        var installer = new FakeWindowsInstaller();
        var vm = CreateUpdates(new FakeRunnerForVm(WingetTable), new FakeDialogs(true), TwoWindowsUpdates, installer);
        await vm.SearchCommand.ExecuteAsync(null);
        vm.Items.Single(i => i.Id == KbA).IsSelected = true;

        await vm.ProceedCommand.ExecuteAsync(null);

        Assert.Equal(new[] { KbA }, installer.Requested);
        Assert.Equal("Instalada · requiere reiniciar", vm.Items.Single(i => i.Id == KbA).Status);
        Assert.Contains("Reinicia el equipo", vm.Summary);
    }

    [Fact]
    public async Task Refusing_the_admin_prompt_installs_nothing_and_says_so()
    {
        var installer = new FakeWindowsInstaller(WindowsUpdateInstallResult.UserCancelled());
        var vm = CreateUpdates(new FakeRunnerForVm(""), new FakeDialogs(true), TwoWindowsUpdates, installer);
        await vm.SearchCommand.ExecuteAsync(null);
        vm.SelectAllCommand.Execute(null);

        await vm.ProceedCommand.ExecuteAsync(null);

        Assert.All(vm.Items, i => Assert.Equal("Cancelado (sin permiso)", i.Status));
    }

    [Theory]
    [InlineData("11111111-1111-1111-1111-111111111111", true)]
    [InlineData("11111111-1111-1111-1111-11111111111", false)]
    [InlineData("KB5000001", false)]
    [InlineData("--wu-install", false)]
    [InlineData("11111111-1111-1111-1111-111111111111 & calc", false)]
    public void Only_real_update_ids_reach_the_elevated_installer(string id, bool valid)
    {
        Assert.Equal(valid, WindowsUpdateInstaller.IsValidUpdateId(id));
    }

    [Theory]
    // Caso real del KB5132061: Windows Update informó mínimo 0 y máximo 92.712 MB (≈ 90,5 GB).
    [InlineData(97_214_000_000L, 0L, "variable")]
    [InlineData(800L * 1024 * 1024, 0L, "hasta 800,0 MB")]
    [InlineData(120L * 1024 * 1024, 110L * 1024 * 1024, "120,0 MB")]
    [InlineData(0L, 0L, "")]
    public void Windows_update_size_never_shows_the_theoretical_maximum_as_real(long max, long min, string expected)
    {
        var item = new WindowsUpdateItem("t", "KB1", false, false, max, KbA, min);
        Assert.Equal(expected, item.SizeText.Replace('.', ','));
    }

    [Fact]
    public void Select_all_checkbox_tracks_rows_both_ways()
    {
        using var dir = new TempDir();
        var vm = new CleanViewModel(
            [
                new CleanRule { Id = "a", Name = "A", Category = "x", Base = dir.Path },
                new CleanRule { Id = "b", Name = "B", Category = "x", Base = dir.Path, DefaultOn = false },
            ],
            new FakeProbe(), new FakeDialogs(true), new FakeRecycleBin(), new StatusBar(), dir.Path, Array.Empty<ISensorSource>());
        var raised = 0;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(CleanViewModel.AllSelected)) raised++; };

        Assert.Null(vm.AllSelected);                 // algunas marcadas: casilla a medias
        vm.AllSelected = true;
        Assert.All(vm.Rules, r => Assert.True(r.IsSelected));
        vm.AllSelected = false;
        Assert.All(vm.Rules, r => Assert.False(r.IsSelected));
        vm.Rules[0].IsSelected = true;               // marcar una fila a mano actualiza la cabecera
        Assert.Null(vm.AllSelected);
        Assert.True(raised > 0);
    }

    [Fact]
    public async Task Select_all_works_on_the_updates_list_after_a_search()
    {
        var vm = CreateUpdates(new FakeRunnerForVm(WingetTable), new FakeDialogs(true), TwoWindowsUpdates);
        await vm.SearchCommand.ExecuteAsync(null);

        Assert.False(vm.AllSelected);
        vm.AllSelected = true;
        Assert.All(vm.Items, i => Assert.True(i.IsSelected));
    }

    [Fact]
    public void Usage_sampler_reports_ram_at_once_and_cpu_from_the_second_sample()
    {
        var sampler = new ProcessUsageSampler(top: 3);

        var first = sampler.Sample();
        Thread.Sleep(300);
        var second = sampler.Sample();

        Assert.Empty(first.Cpu);                     // la primera muestra solo fija la referencia
        Assert.InRange(second.Ram.Count, 1, 3);       // siempre hay procesos usando memoria
        Assert.True(second.Ram.SequenceEqual(second.Ram.OrderByDescending(r => r.Value)));
        Assert.All(second.Cpu, c => Assert.InRange(c.Value, 0, 100));
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

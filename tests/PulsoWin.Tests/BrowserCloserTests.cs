// PulsoWin · MIT · Co-designed with AI coding assistants (Claude & Gemini)
using PulsoWin.Core.Cleaning;
using PulsoWin.Core.Common;
using PulsoWin.Core.Sensors;
using PulsoWin.Presentation.Services;
using PulsoWin.Presentation.ViewModels;

namespace PulsoWin.Tests;

public class BrowserCloserTests
{
    private sealed class SimProcess(string name, int windows = 0, bool refusesClose = false, Action? onWindowClosed = null) : IBrowserProcess
    {
        public string Name { get; } = name;
        public int Windows { get; private set; } = windows;
        public bool Alive { get; private set; } = true;
        public int CloseCalls { get; private set; }
        public int KillCalls { get; private set; }

        public bool HasMainWindow => Alive && Windows > 0;

        public void CloseMainWindow()
        {
            CloseCalls++;
            if (!Alive || refusesClose || Windows <= 0) return;
            Windows--;
            if (Windows == 0) onWindowClosed?.Invoke();
        }

        public void Kill()
        {
            KillCalls++;
            Windows = 0;
            Alive = false;
        }

        public void Dispose() { }
    }

    private sealed class FakeCloser(bool closes = true, params string[] running) : IBrowserCloser
    {
        private readonly HashSet<string> _running = new(running, StringComparer.OrdinalIgnoreCase);
        public bool VisibleWindows { get; set; } = true;
        public int CloseRequests { get; private set; }
        public int HeadlessCloseRequests { get; private set; }

        public IReadOnlyList<string> Running(IEnumerable<string> processNames) =>
            processNames.Where(_running.Contains).ToList();

        public bool HasVisibleWindow(string processName) =>
            VisibleWindows && _running.Contains(processName);

        public Task<IReadOnlyList<string>> CloseAsync(IEnumerable<string> processNames, TimeSpan timeout)
        {
            CloseRequests++;
            if (closes) _running.Clear();
            return Task.FromResult(Running(processNames));
        }

        public Task CloseHeadlessAsync(IEnumerable<string> processNames, TimeSpan timeout)
        {
            HeadlessCloseRequests++;
            if (!VisibleWindows && closes)
            {
                foreach (var n in processNames) _running.Remove(n);
            }
            return Task.CompletedTask;
        }
    }

    private sealed class DynamicFakeProbe(Func<bool> isEdgeRunning) : IProcessProbe
    {
        public bool IsRunning(string processName) =>
            string.Equals(processName, "msedge", StringComparison.OrdinalIgnoreCase) && isEdgeRunning();
    }

    private static BrowserCloser CreateCloser(List<SimProcess> procs) =>
        new(
            name => procs.Where(p => p.Alive && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).Cast<IBrowserProcess>().ToList(),
            pollDelay: TimeSpan.FromMilliseconds(5),
            settleDelay: TimeSpan.FromMilliseconds(5));

    [Fact]
    public async Task BrowserCloser_terminates_headless_edge_startup_boost_processes()
    {
        var procs = new List<SimProcess>
        {
            new("msedge", windows: 0),
            new("msedge", windows: 0),
            new("msedge", windows: 0),
        };
        var closer = CreateCloser(procs);

        Assert.Equal(["msedge"], closer.Running(["chrome", "msedge"]));
        var still = await closer.CloseAsync(["msedge"], TimeSpan.FromMilliseconds(200));

        Assert.Empty(still);
        Assert.All(procs, p =>
        {
            Assert.Equal(0, p.CloseCalls);
            Assert.True(p.KillCalls >= 1);
            Assert.False(p.Alive);
        });
    }

    [Fact]
    public async Task BrowserCloser_closes_visible_windows_then_cleans_up_startup_boost_background_processes()
    {
        var procs = new List<SimProcess>();
        procs.Add(new SimProcess("msedge", windows: 2, onWindowClosed: () => procs.Add(new SimProcess("msedge", windows: 0))));
        var closer = CreateCloser(procs);

        var still = await closer.CloseAsync(["msedge"], TimeSpan.FromMilliseconds(300));

        Assert.Empty(still);
        Assert.Equal(2, procs[0].CloseCalls);
        Assert.All(procs, p => Assert.False(p.Alive));
    }

    [Fact]
    public async Task BrowserCloser_never_kills_a_visible_window_that_refuses_to_close()
    {
        var edgeBlocked = new SimProcess("msedge", windows: 1, refusesClose: true);
        var chromeHeadless = new SimProcess("chrome", windows: 0);
        var procs = new List<SimProcess> { edgeBlocked, chromeHeadless };
        var closer = CreateCloser(procs);

        var still = await closer.CloseAsync(["chrome", "msedge"], TimeSpan.FromMilliseconds(60));

        Assert.Equal(["msedge"], still);
        Assert.Equal(0, edgeBlocked.KillCalls);
        Assert.True(edgeBlocked.Alive);
        Assert.False(chromeHeadless.Alive);
    }

    [Fact]
    public async Task CleanViewModel_cleans_edge_cache_when_edge_is_only_running_headless_in_background()
    {
        using var dir = new TempDir();
        var cacheFile = dir.File(@"EdgeCache\f_001", 512);
        var edgeRule = new CleanRule
        {
            Id = "edge-cache", Name = "Edge · caché", Category = "Navegadores",
            Base = Path.Combine(dir.Path, "EdgeCache"), BlockingProcesses = ["msedge"],
        };
        var closer = new FakeCloser(closes: true, "msedge") { VisibleWindows = false };
        var probe = new DynamicFakeProbe(() => closer.Running(["msedge"]).Count > 0);
        var vm = new CleanViewModel([edgeRule], probe, new FakeDialogs(true), new FakeRecycleBin(),
            new StatusBar(), Path.Combine(dir.Path, "_datos"), [], closer);

        await vm.ScanCommand.ExecuteAsync(null);
        Assert.Equal("512 B", vm.FreeableText);

        await vm.CleanCommand.ExecuteAsync(null);
        Assert.Equal(1, closer.HeadlessCloseRequests);
        Assert.False(File.Exists(cacheFile));
    }
}

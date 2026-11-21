using PulsoWin.Core.Cleaning;

namespace PulsoWin.Tests;

public class ScannerCleanerTests
{
    private static CleanRule Rule(string baseDir, string? profiles = null, string[]? subPaths = null,
        double minAgeHours = 0, string[]? blocking = null, string pattern = "*") => new()
    {
        Id = "test",
        Name = "test",
        Category = "test",
        Base = baseDir,
        Profiles = profiles,
        SubPaths = subPaths ?? [""],
        MinAgeHours = minAgeHours,
        BlockingProcesses = blocking ?? [],
        Pattern = pattern,
    };

    [Fact]
    public void Scan_is_read_only_and_counts_bytes()
    {
        using var dir = new TempDir();
        var a = dir.File("a.tmp", 100);
        var b = dir.File("sub\\b.tmp", 50);

        var item = new Scanner(new FakeProbe()).Scan(Rule(dir.Path));

        Assert.Equal(150, item.Bytes);
        Assert.Equal(2, item.Files.Count);
        Assert.True(File.Exists(a) && File.Exists(b));
    }

    [Fact]
    public void Recent_files_are_protected_by_min_age()
    {
        using var dir = new TempDir();
        dir.File("viejo.tmp", 10, DateTime.UtcNow.AddDays(-2));
        dir.File("nuevo.tmp", 10, DateTime.UtcNow.AddMinutes(-5));

        var item = new Scanner(new FakeProbe()).Scan(Rule(dir.Path, minAgeHours: 24));

        Assert.Single(item.Files);
        Assert.EndsWith("viejo.tmp", item.Files[0]);
    }

    [Fact]
    public void Pattern_and_non_recursive_are_respected()
    {
        using var dir = new TempDir();
        dir.File("thumbcache_32.db");
        dir.File("otro.db");
        dir.File("sub\\thumbcache_96.db");

        var rule = Rule(dir.Path, pattern: "thumbcache_*.db") with { Recursive = false };
        var item = new Scanner(new FakeProbe()).Scan(rule);

        Assert.Single(item.Files);
    }

    [Fact]
    public void Chromium_profiles_are_all_enumerated()
    {
        using var dir = new TempDir();
        dir.File("Default\\Preferences");
        dir.File("Default\\Cache\\Cache_Data\\f_0001", 20);
        dir.File("Profile 2\\Preferences");
        dir.File("Profile 2\\Cache\\Cache_Data\\f_0002", 30);
        dir.File("NoEsPerfil\\Cache\\Cache_Data\\f_0003", 40); // sin Preferences: se ignora

        var item = new Scanner(new FakeProbe()).Scan(Rule(dir.Path, "chromium", ["Cache\\Cache_Data"]));

        Assert.Equal(50, item.Bytes);
    }

    [Fact]
    public void Running_browser_blocks_cleaning()
    {
        using var dir = new TempDir();
        var file = dir.File("x.tmp");

        var item = new Scanner(new FakeProbe("chrome")).Scan(Rule(dir.Path, blocking: ["chrome"]));
        var result = new Cleaner().Clean(item);

        Assert.Equal("chrome", item.BlockedBy);
        Assert.Empty(result.Deleted);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void Clean_deletes_only_scanned_files()
    {
        using var dir = new TempDir();
        dir.File("a.tmp", 100);
        var item = new Scanner(new FakeProbe()).Scan(Rule(dir.Path));
        var later = dir.File("creado-despues.tmp", 10);

        var result = new Cleaner().Clean(item);

        Assert.Equal(100, result.BytesFreed);
        Assert.Single(result.Deleted);
        Assert.True(File.Exists(later));
    }

    [Fact]
    public void Clean_skips_paths_outside_the_rule_roots()
    {
        using var dir = new TempDir();
        using var outside = new TempDir();
        var victim = outside.File("importante.docx");
        var rule = Rule(dir.Path);
        var forged = new ScanItem(rule, 10, [victim], null);

        var result = new Cleaner().Clean(forged);

        Assert.Empty(result.Deleted);
        Assert.Equal(1, result.Skipped);
        Assert.True(File.Exists(victim));
    }

    [Fact]
    public void Locked_files_are_skipped_not_fatal()
    {
        using var dir = new TempDir();
        var locked = dir.File("abierto.tmp");
        dir.File("libre.tmp");
        var item = new Scanner(new FakeProbe()).Scan(Rule(dir.Path));

        CleanResult result;
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            result = new Cleaner().Clean(item);

        Assert.Single(result.Deleted);
        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public void Browser_opened_after_scan_blocks_cleaning()
    {
        using var dir = new TempDir();
        var file = dir.File("f_0001");
        var item = new Scanner(new FakeProbe()).Scan(Rule(dir.Path, blocking: ["chrome"]));

        var result = new Cleaner(new FakeProbe("chrome")).Clean(item); // Chrome se abrió entre Analizar y Limpiar

        Assert.Empty(result.Deleted);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void File_modified_after_scan_is_kept()
    {
        using var dir = new TempDir();
        var file = dir.File("instalador.tmp", 10, DateTime.UtcNow.AddDays(-2));
        var item = new Scanner(new FakeProbe()).Scan(Rule(dir.Path, minAgeHours: 24));
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow); // un instalador lo reescribió

        var result = new Cleaner(new FakeProbe()).Clean(item);

        Assert.Empty(result.Deleted);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void Forged_item_cannot_bypass_pattern_or_recursion()
    {
        using var dir = new TempDir();
        var other = dir.File("otro.db");
        var nested = dir.File("sub\\thumbcache_96.db");
        var rule = Rule(dir.Path, pattern: "thumbcache_*.db") with { Recursive = false };

        var result = new Cleaner(new FakeProbe()).Clean(new ScanItem(rule, 20, [other, nested], null));

        Assert.Empty(result.Deleted);
        Assert.True(File.Exists(other) && File.Exists(nested));
    }

    [Fact]
    public void Parents_of_protected_folders_are_protected_too()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.True(RuleRoots.IsProtected(Path.GetDirectoryName(profile)!));                // C:\Users
        Assert.True(RuleRoots.IsProtected(Path.Combine(profile, "AppData")));               // padre de Local y Roaming
        Assert.False(RuleRoots.IsProtected(Path.Combine(Path.GetTempPath(), "pulsowin-x"))); // una carpeta normal
    }

    [Fact]
    public void Protected_folders_are_never_roots()
    {
        Assert.True(RuleRoots.IsProtected(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
        Assert.True(RuleRoots.IsProtected(Environment.GetFolderPath(Environment.SpecialFolder.Windows)));
        Assert.True(RuleRoots.IsProtected("C:\\"));
        Assert.Empty(RuleRoots.Resolve(Rule("%USERPROFILE%")));
        Assert.Empty(RuleRoots.Resolve(Rule("%VARIABLE_QUE_NO_EXISTE%\\x")));
    }
}

using Microsoft.Data.Sqlite;
using PulsoWin.Core.Cleaning;

namespace PulsoWin.Tests;

public class CookieCleanerTests
{
    /// <summary>Base mínima con el mismo nombre de tabla y columna que usa Chromium.</summary>
    private static CookieStore CreateChromiumLikeStore(TempDir dir, params string[] hosts)
    {
        var db = Path.Combine(dir.Path, "Cookies");
        using (var conn = new SqliteConnection($"Data Source={db};Pooling=False"))
        {
            conn.Open();
            using var create = conn.CreateCommand();
            create.CommandText = "CREATE TABLE cookies (host_key TEXT NOT NULL, name TEXT NOT NULL, encrypted_value BLOB)";
            create.ExecuteNonQuery();
            foreach (var host in hosts)
            {
                using var insert = conn.CreateCommand();
                insert.CommandText = "INSERT INTO cookies VALUES ($h, 'sid', x'00')";
                insert.Parameters.AddWithValue("$h", host);
                insert.ExecuteNonQuery();
            }
        }
        return new CookieStore("Chrome", "Default", db, "cookies", "host_key", "chrome");
    }

    private static List<string> Hosts(CookieStore store)
    {
        using var conn = new SqliteConnection($"Data Source={store.DbPath};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT host_key FROM cookies ORDER BY host_key";
        using var reader = cmd.ExecuteReader();
        var list = new List<string>();
        while (reader.Read()) list.Add(reader.GetString(0));
        return list;
    }

    [Fact]
    public void Plan_counts_without_modifying()
    {
        using var dir = new TempDir();
        var store = CreateChromiumLikeStore(dir, ".google.com", "mail.google.com", ".tracker.net", "ads.example");

        var plan = new CookieCleaner(new FakeProbe()).Plan(store, KeepList.Parse("google.com"));

        Assert.Equal(2, plan.ToDelete);
        Assert.Equal(2, plan.Kept);
        Assert.Equal(4, Hosts(store).Count);
    }

    [Fact]
    public void Delete_keeps_listed_domains_and_makes_a_backup()
    {
        using var dir = new TempDir();
        var store = CreateChromiumLikeStore(dir, ".google.com", "mail.google.com", ".tracker.net", "ads.example");
        var backupRoot = Path.Combine(dir.Path, "respaldos");

        var result = new CookieCleaner(new FakeProbe()).Delete(store, KeepList.Parse("google.com"), backupRoot);

        Assert.Null(result.Error);
        Assert.Equal(2, result.Deleted);
        Assert.Equal(new[] { ".google.com", "mail.google.com" }, Hosts(store));
        Assert.True(File.Exists(Path.Combine(result.BackupPath!, "Cookies")));
    }

    [Fact]
    public void Open_browser_blocks_plan_and_delete()
    {
        using var dir = new TempDir();
        var store = CreateChromiumLikeStore(dir, ".tracker.net");
        var cleaner = new CookieCleaner(new FakeProbe("chrome"));

        Assert.Equal("chrome", cleaner.Plan(store, KeepList.Parse("")).BlockedBy);
        var result = cleaner.Delete(store, KeepList.Parse(""), Path.Combine(dir.Path, "respaldos"));

        Assert.NotNull(result.Error);
        Assert.Single(Hosts(store));
    }

    [Fact]
    public void Corrupt_database_reports_error_instead_of_throwing()
    {
        using var dir = new TempDir();
        var path = dir.File("Cookies", 64);
        var store = new CookieStore("Chrome", "Default", path, "cookies", "host_key", "chrome");

        var plan = new CookieCleaner(new FakeProbe()).Plan(store, KeepList.Parse(""));

        Assert.NotNull(plan.Error);
    }
}

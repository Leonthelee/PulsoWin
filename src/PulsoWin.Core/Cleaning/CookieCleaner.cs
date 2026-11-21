using Microsoft.Data.Sqlite;
using PulsoWin.Core.Common;

namespace PulsoWin.Core.Cleaning;

public sealed record CookieStore(string Browser, string Profile, string DbPath, string Table, string HostColumn, string Process);

public sealed record CookiePlan(CookieStore Store, int ToDelete, int Kept, string? BlockedBy, string? Error);

public sealed record CookieDeleteResult(int Deleted, string? BackupPath, string? Error);

public static class CookieStores
{
    public static IEnumerable<CookieStore> Discover()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var stores = new List<CookieStore>();

        (string Browser, string Base, string Process)[] chromium =
        [
            ("Chrome", Path.Combine(local, "Google", "Chrome", "User Data"), "chrome"),
            ("Edge", Path.Combine(local, "Microsoft", "Edge", "User Data"), "msedge"),
        ];
        foreach (var (browser, baseDir, process) in chromium)
        {
            foreach (var profile in ProfileLocator.Expand(baseDir, "chromium"))
            {
                var db = Path.Combine(profile, "Network", "Cookies");
                if (!File.Exists(db)) db = Path.Combine(profile, "Cookies"); // versiones antiguas
                if (File.Exists(db))
                    stores.Add(new CookieStore(browser, Path.GetFileName(profile), db, "cookies", "host_key", process));
            }
        }

        foreach (var profile in ProfileLocator.Expand(Path.Combine(roaming, "Mozilla", "Firefox", "Profiles"), "firefox"))
        {
            var db = Path.Combine(profile, "cookies.sqlite");
            if (File.Exists(db))
                stores.Add(new CookieStore("Firefox", Path.GetFileName(profile), db, "moz_cookies", "host", "firefox"));
        }

        return stores;
    }
}

/// <summary>
/// Borra filas de cookies por dominio. No descifra valores: el dominio se guarda en claro
/// (Chromium: host_key; Firefox: host). Solo actúa con el navegador cerrado y siempre deja copia.
/// Ojo: borrar cookies no borra localStorage ni IndexedDB, donde algunos sitios guardan la sesión.
/// </summary>
public sealed class CookieCleaner(IProcessProbe probe, TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public CookiePlan Plan(CookieStore store, KeepList keep)
    {
        if (probe.IsRunning(store.Process))
            return new CookiePlan(store, 0, 0, store.Process, null);
        try
        {
            using var conn = Open(store.DbPath, SqliteOpenMode.ReadOnly);
            var rows = CountByHost(conn, store);
            return new CookiePlan(store,
                rows.Where(r => !keep.Keeps(r.Host)).Sum(r => r.Count),
                rows.Where(r => keep.Keeps(r.Host)).Sum(r => r.Count),
                null, null);
        }
        catch (SqliteException ex)
        {
            return new CookiePlan(store, 0, 0, null, ex.Message);
        }
    }

    public CookieDeleteResult Delete(CookieStore store, KeepList keep, string backupRoot)
    {
        if (probe.IsRunning(store.Process))
            return new CookieDeleteResult(0, null, $"«{store.Process}» está abierto");

        string backup;
        try
        {
            backup = Backup(store, backupRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new CookieDeleteResult(0, null, "no se pudo crear la copia de respaldo: " + ex.Message);
        }

        try
        {
            using var conn = Open(store.DbPath, SqliteOpenMode.ReadWrite);
            using var tx = conn.BeginTransaction();
            var hosts = CountByHost(conn, store).Where(r => !keep.Keeps(r.Host)).Select(r => r.Host).ToList();
            var deleted = 0;
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            // Tabla y columna salen de constantes propias, nunca de la entrada del usuario.
            cmd.CommandText = $"DELETE FROM {store.Table} WHERE {store.HostColumn} = $host";
            var parameter = cmd.Parameters.Add("$host", SqliteType.Text);
            foreach (var host in hosts)
            {
                parameter.Value = host;
                deleted += cmd.ExecuteNonQuery();
            }
            tx.Commit();
            return new CookieDeleteResult(deleted, backup, null);
        }
        catch (SqliteException ex)
        {
            return new CookieDeleteResult(0, backup, ex.Message);
        }
    }

    private static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = path, Mode = mode, Pooling = false }.ToString();
        var conn = new SqliteConnection(cs);
        conn.Open();
        return conn;
    }

    private static List<(string Host, int Count)> CountByHost(SqliteConnection conn, CookieStore store)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {store.HostColumn}, COUNT(*) FROM {store.Table} GROUP BY {store.HostColumn}";
        using var reader = cmd.ExecuteReader();
        var rows = new List<(string Host, int Count)>();
        while (reader.Read())
            rows.Add((reader.IsDBNull(0) ? "" : reader.GetString(0), reader.GetInt32(1)));
        return rows;
    }

    private string Backup(CookieStore store, string backupRoot)
    {
        var stamp = _time.GetLocalNow().ToString("yyyyMMdd-HHmmss");
        var dir = Path.Combine(backupRoot, $"{store.Browser}-{Sanitize(store.Profile)}-{stamp}");
        Directory.CreateDirectory(dir);
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
        {
            var src = store.DbPath + suffix;
            if (File.Exists(src))
                File.Copy(src, Path.Combine(dir, Path.GetFileName(src)), overwrite: true);
        }
        return dir;
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) || c == ' ' ? '_' : c));
    }
}

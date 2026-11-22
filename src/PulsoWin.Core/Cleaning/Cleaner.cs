// PulsoWin · MIT · Co-designed with AI coding assistants (Claude & Gemini)
using System.IO.Enumeration;
using PulsoWin.Core.Common;

namespace PulsoWin.Core.Cleaning;

public sealed record CleanResult(string RuleId, long BytesFreed, IReadOnlyList<string> Deleted, int Skipped);

/// <summary>
/// Fase 3: borra solo lo que el análisis encontró. Entre «Analizar» y «Limpiar» puede pasar
/// cualquier cantidad de tiempo, así que todas las condiciones de la regla se vuelven a comprobar aquí.
/// </summary>
public sealed class Cleaner(IProcessProbe? probe = null, TimeProvider? time = null)
{
    private readonly IProcessProbe _probe = probe ?? new ProcessProbe();
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public CleanResult Clean(ScanItem item)
    {
        var rule = item.Rule;
        if (item.BlockedBy is not null || rule.BlockingProcesses.Any(p => _probe.IsRunning(p)))
            return new CleanResult(rule.Id, 0, [], item.Files.Count); // el navegador se abrió después del análisis

        var cutoff = _time.GetUtcNow().UtcDateTime - TimeSpan.FromHours(rule.MinAgeHours);
        var roots = RuleRoots.Resolve(rule).ToList();
        var deleted = new List<string>();
        long freed = 0;
        var skipped = 0;

        foreach (var path in item.Files)
        {
            FileInfo? info = null;
            var wasReadOnly = false;
            try
            {
                info = new FileInfo(path);
                if (!info.Exists) continue;

                var root = roots.FirstOrDefault(r => RuleRoots.IsUnder(path, r));
                var parent = Path.GetDirectoryName(path);
                if (root is null || parent is null
                    || info.Attributes.HasFlag(FileAttributes.ReparsePoint)
                    || info.Attributes.HasFlag(FileAttributes.System)
                    || RuleRoots.HasLinkBetween(parent, root)                       // unión creada después del análisis
                    || info.LastWriteTimeUtc > cutoff                                // modificado desde el análisis
                    || !FileSystemName.MatchesSimpleExpression(rule.Pattern, info.Name)
                    || (!rule.Recursive && !string.Equals(Path.TrimEndingDirectorySeparator(parent), root, StringComparison.OrdinalIgnoreCase)))
                {
                    skipped++;
                    continue;
                }

                var length = info.Length;
                wasReadOnly = info.IsReadOnly;
                if (wasReadOnly) info.IsReadOnly = false;
                info.Delete();
                freed += length;
                deleted.Add(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped++; // en uso o sin permiso: se deja intacto
                if (wasReadOnly && info is not null)
                {
                    try { info.IsReadOnly = true; }
                    catch (Exception restore) when (restore is IOException or UnauthorizedAccessException) { }
                }
            }
        }

        return new CleanResult(rule.Id, freed, deleted, skipped);
    }
}

// PulsoWin · MIT · Co-designed with AI coding assistants (Claude & Gemini)
using PulsoWin.Core.Common;

namespace PulsoWin.Core.Cleaning;

public sealed record ScanItem(CleanRule Rule, long Bytes, IReadOnlyList<string> Files, string? BlockedBy);

/// <summary>Fase 1: solo lectura. Calcula qué se borraría sin tocar nada.</summary>
public sealed class Scanner(IProcessProbe probe, TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public ScanItem Scan(CleanRule rule)
    {
        var blockedBy = rule.BlockingProcesses.FirstOrDefault(p => probe.IsRunning(p));
        var cutoff = _time.GetUtcNow().UtcDateTime - TimeSpan.FromHours(rule.MinAgeHours);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = rule.Recursive,
            IgnoreInaccessible = true,
            // No seguir uniones ni enlaces simbólicos, y no tocar archivos de sistema.
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
        };

        var files = new List<string>();
        long bytes = 0;
        foreach (var root in RuleRoots.Resolve(rule))
        {
            try
            {
                foreach (var path in Directory.EnumerateFiles(root, rule.Pattern, options))
                {
                    long length;
                    try
                    {
                        var info = new FileInfo(path);
                        if (info.LastWriteTimeUtc > cutoff) continue;
                        length = info.Length;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        continue;
                    }
                    bytes += length;
                    files.Add(path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // La carpeta desapareció o cambió durante el recorrido: se omite.
            }
        }

        return new ScanItem(rule, bytes, files, blockedBy);
    }
}

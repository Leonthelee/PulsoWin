// PulsoWin · MIT · Co-designed with AI coding assistants (Claude & Gemini)
using System.Runtime.InteropServices;

namespace PulsoWin.Core.Cleaning;

/// <summary>Resuelve las carpetas reales de una regla y aplica las barreras de seguridad.</summary>
public static class RuleRoots
{
    public static IEnumerable<string> Resolve(CleanRule rule)
    {
        var expanded = Environment.ExpandEnvironmentVariables(rule.Base);
        if (expanded.Contains('%') || !Path.IsPathFullyQualified(expanded) || expanded.StartsWith(@"\\"))
            yield break;

        // Forma larga: %TEMP% puede venir como C:\Users\NOMBRE~1\..., y las comparaciones son por texto.
        var baseDir = Normalize(expanded);
        if (IsProtected(baseDir) || !Directory.Exists(baseDir) || IsLink(baseDir))
            yield break;

        foreach (var profile in ProfileLocator.Expand(baseDir, rule.Profiles))
        {
            foreach (var sub in rule.SubPaths)
            {
                var root = Normalize(Path.Combine(profile, sub));
                if (IsUnder(root, baseDir) && !IsProtected(root) && Directory.Exists(root) && !HasLinkBetween(root, baseDir))
                    yield return root;
            }
        }
    }

    public static bool IsUnder(string path, string parent)
    {
        var p = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent)) + Path.DirectorySeparatorChar;
        var x = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) + Path.DirectorySeparatorChar;
        return x.StartsWith(p, StringComparison.OrdinalIgnoreCase);
    }

    private static readonly Environment.SpecialFolder[] Guarded =
    [
        Environment.SpecialFolder.UserProfile,
        Environment.SpecialFolder.Windows,
        Environment.SpecialFolder.System,
        Environment.SpecialFolder.ProgramFiles,
        Environment.SpecialFolder.ProgramFilesX86,
        Environment.SpecialFolder.CommonApplicationData,
        Environment.SpecialFolder.LocalApplicationData,
        Environment.SpecialFolder.ApplicationData,
        Environment.SpecialFolder.MyDocuments,
        Environment.SpecialFolder.Desktop,
    ];

    /// <summary>
    /// Una carpeta protegida, o cualquier carpeta que la contenga (C:\, C:\Users, ...AppData),
    /// nunca es raíz de limpieza, aunque una regla mal escrita apunte a ella.
    /// </summary>
    public static bool IsProtected(string path)
    {
        var target = Normalize(path);
        if (Path.GetPathRoot(target) is { Length: > 0 } driveRoot &&
            string.Equals(Path.TrimEndingDirectorySeparator(driveRoot), Path.TrimEndingDirectorySeparator(target), StringComparison.OrdinalIgnoreCase))
            return true;

        return Guarded
            .Select(Environment.GetFolderPath)
            .Where(f => f.Length > 0)
            .Any(f => IsUnder(Normalize(f), target));
    }

    /// <summary>Ruta completa, en forma larga y sin separador final.</summary>
    public static string Normalize(string path)
    {
        var full = Path.GetFullPath(path);
        var buffer = new char[32768];
        var length = GetLongPathNameW(full, buffer, (uint)buffer.Length);
        var longPath = length > 0 && length < buffer.Length ? new string(buffer, 0, (int)length) : full;
        return Path.TrimEndingDirectorySeparator(longPath);
    }

    /// <summary>¿Hay una unión o enlace simbólico en algún nivel entre la carpeta y la raíz (inclusive)?</summary>
    public static bool HasLinkBetween(string directory, string stopAt)
    {
        for (var d = directory; d is not null && IsUnder(d, stopAt); d = Path.GetDirectoryName(d))
        {
            if (IsLink(d)) return true;
        }
        return false;
    }

    private static bool IsLink(string directory)
    {
        try
        {
            var info = new DirectoryInfo(directory);
            return info.Exists && info.Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true; // si no se puede comprobar, se trata como peligroso
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetLongPathNameW(string lpszShortPath, char[] lpszLongPath, uint cchBuffer);
}

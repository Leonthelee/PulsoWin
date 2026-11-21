using System.Text.RegularExpressions;

namespace PulsoWin.Core.Updates;

public sealed record WingetPackage(
    string Name,
    string Id,
    string InstalledVersion,
    string AvailableVersion,
    string Source);

public sealed record WingetScanResult(
    IReadOnlyList<WingetPackage> Packages,
    string? Error = null)
{
    public bool IsAvailable => Error is null;
}

public sealed record WingetUpgradeResult(
    string PackageId,
    bool Succeeded,
    int ExitCode,
    string Summary);

/// <summary>Analizador tolerante a idioma (ES/EN) para la salida tabular de <c>winget upgrade</c>.</summary>
public static partial class WingetParser
{
    // IDs válidos de winget / msstore: letras, dígitos, puntos, guiones, guiones bajos o '+'.
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._+\-]{1,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidIdRegex();

    public static bool IsValidPackageId(string? id) =>
        !string.IsNullOrWhiteSpace(id) && ValidIdRegex().IsMatch(id);

    public static IReadOnlyList<WingetPackage> ParseUpgradeOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return [];

        var lines = output
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(NormalizeLine)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        // Buscar la línea separadora de guiones ("-----...") que sigue al encabezado.
        var sepIndex = -1;
        for (var i = 1; i < lines.Count; i++)
        {
            if (IsSeparatorLine(lines[i]))
            {
                sepIndex = i;
                break;
            }
        }

        if (sepIndex < 1) return [];

        var header = lines[sepIndex - 1];
        var cols = DetectColumnStarts(header);
        if (cols is null) return [];

        var results = new List<WingetPackage>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = sepIndex + 1; i < lines.Count; i++)
        {
            var line = lines[i];
            if (IsFooterLine(line)) break;

            var pkg = ParseRow(line, cols.Value);
            if (pkg is not null && seen.Add(pkg.Id)) results.Add(pkg);
        }

        return results;
    }

    private static string NormalizeLine(string raw)
    {
        // winget imprime spinners con '\r'; nos quedamos con el último segmento tras '\r'.
        var lastCr = raw.LastIndexOf('\r');
        var segment = lastCr >= 0 ? raw[(lastCr + 1)..] : raw;
        return segment.TrimEnd();
    }

    private static bool IsSeparatorLine(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length >= 20 && trimmed.All(c => c == '-');
    }

    private static bool IsFooterLine(string line)
    {
        var trimmed = line.Trim();
        // Pie típico: "26 actualizaciones disponibles." / "26 upgrades available." / "The following packages..."
        return Regex.IsMatch(trimmed, @"^\d+\s+(actualizaci|upgrade)", RegexOptions.IgnoreCase)
               || trimmed.StartsWith("Los siguientes paquetes", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("The following packages", StringComparison.OrdinalIgnoreCase);
    }

    private readonly record struct Columns(int Name, int Id, int Version, int Available, int Source);

    private static Columns? DetectColumnStarts(string header)
    {
        var idIdx = IndexOfColumn(header, "Id");
        var verIdx = IndexOfAnyColumn(header, "Versión", "Version");
        var availIdx = IndexOfAnyColumn(header, "Disponible", "Available");
        var srcIdx = IndexOfAnyColumn(header, "Origen", "Source");

        if (idIdx <= 0 || verIdx <= idIdx || availIdx <= verIdx) return null;
        return new Columns(0, idIdx, verIdx, availIdx, srcIdx);
    }

    private static int IndexOfAnyColumn(string header, params string[] names)
    {
        foreach (var name in names)
        {
            var idx = IndexOfColumn(header, name);
            if (idx >= 0) return idx;
        }
        return -1;
    }

    private static int IndexOfColumn(string header, string name)
    {
        var start = 0;
        while (start < header.Length)
        {
            var idx = header.IndexOf(name, start, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return -1;
            var leftOk = idx == 0 || char.IsWhiteSpace(header[idx - 1]);
            var end = idx + name.Length;
            var rightOk = end == header.Length || char.IsWhiteSpace(header[end]);
            if (leftOk && rightOk) return idx;
            start = idx + 1;
        }
        return -1;
    }

    private static WingetPackage? ParseRow(string line, Columns cols)
    {
        // Si los caracteres no-ASCII desplazan ligeramente el ancho de columna,
        // hacemos primero slice por columna y, si el ID no valida, fallback por 2+ espacios.
        var name = Slice(line, cols.Name, cols.Id);
        var id = Slice(line, cols.Id, cols.Version);
        var installed = Slice(line, cols.Version, cols.Available);
        var available = cols.Source > cols.Available
            ? Slice(line, cols.Available, cols.Source)
            : Slice(line, cols.Available, line.Length);
        var source = cols.Source > cols.Available
            ? Slice(line, cols.Source, line.Length)
            : "";

        if (!IsValidPackageId(id))
        {
            var tokens = Regex.Split(line.Trim(), @"\s{2,}");
            if (tokens.Length < 4) return null;
            name = tokens[0].Trim();
            id = tokens[1].Trim();
            installed = tokens[2].Trim();
            available = tokens[3].Trim();
            source = tokens.Length >= 5 ? tokens[4].Trim() : "";
            if (!IsValidPackageId(id)) return null;
        }

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(available)) return null;
        return new WingetPackage(name, id, installed, available, source);
    }

    private static string Slice(string line, int start, int end)
    {
        if (start >= line.Length) return "";
        var clampedEnd = Math.Min(end, line.Length);
        if (clampedEnd <= start) return "";
        return line[start..clampedEnd].Trim();
    }
}

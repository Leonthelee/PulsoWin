// PulsoWin · MIT · Co-designed with AI coding assistants (Claude & Gemini)
namespace PulsoWin.Core.Cleaning;

/// <summary>Dominios cuyas cookies se conservan. "google.com" conserva también "mail.google.com".</summary>
public sealed class KeepList
{
    private readonly string[] _domains;

    public KeepList(IEnumerable<string> domains) =>
        _domains = domains.Select(Normalize).Where(d => d.Length > 0).Distinct().ToArray();

    public IReadOnlyList<string> Domains => _domains;

    public static KeepList Parse(string? text) =>
        new((text ?? "").Split(['\r', '\n', ',', ';', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries));

    public bool Keeps(string host)
    {
        var h = Normalize(host);
        return h.Length > 0 && _domains.Any(d => h == d || h.EndsWith("." + d, StringComparison.Ordinal));
    }

    private static string Normalize(string value)
    {
        var d = value.Trim().ToLowerInvariant();
        var scheme = d.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) d = d[(scheme + 3)..];
        var cut = d.IndexOfAny(['/', '?', '#']);
        if (cut >= 0) d = d[..cut];
        var port = d.LastIndexOf(':');
        if (port >= 0) d = d[..port];
        if (d.StartsWith("*.", StringComparison.Ordinal)) d = d[2..];
        return d.Trim('.');
    }
}

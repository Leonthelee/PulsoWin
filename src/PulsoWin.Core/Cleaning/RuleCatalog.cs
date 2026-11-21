using System.Text.Json;

namespace PulsoWin.Core.Cleaning;

public static class RuleCatalog
{
    private const string ResourceName = "PulsoWin.rules.json";
    private static readonly string[] KnownProfiles = ["chromium", "firefox"];

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static IReadOnlyList<CleanRule> LoadBuiltIn()
    {
        using var stream = typeof(RuleCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("Falta el catálogo de reglas embebido.");
        return Parse(stream);
    }

    public static IReadOnlyList<CleanRule> Parse(Stream json)
    {
        var rules = JsonSerializer.Deserialize<List<CleanRule>>(json, Json) ?? [];
        Validate(rules);
        return rules;
    }

    private static void Validate(List<CleanRule> rules)
    {
        var duplicated = rules.GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicated is not null)
            throw new InvalidDataException($"Regla duplicada: {duplicated.Key}");

        foreach (var rule in rules)
        {
            if (rule.Profiles is not null && !KnownProfiles.Contains(rule.Profiles))
                throw new InvalidDataException($"[{rule.Id}] tipo de perfil desconocido: {rule.Profiles}");
            if (rule.Base.Split('\\', '/').Contains("..") || rule.Base.StartsWith(@"\\") || rule.Base.StartsWith("//"))
                throw new InvalidDataException($"[{rule.Id}] la carpeta base no puede subir de nivel ni ser de red.");
            if (rule.Pattern.IndexOfAny(['\\', '/']) >= 0 || rule.Pattern.Contains(".."))
                throw new InvalidDataException($"[{rule.Id}] el patrón no puede contener rutas.");
            foreach (var sub in rule.SubPaths)
            {
                if (Path.IsPathRooted(sub) || sub.Split('\\', '/').Contains(".."))
                    throw new InvalidDataException($"[{rule.Id}] subruta no permitida: {sub}");
            }
        }
    }
}

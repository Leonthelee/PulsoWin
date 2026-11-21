using System.Text.Json;
using System.Text.Json.Serialization;

namespace PulsoWin.Core.Cleaning;

/// <summary>
/// Serialización generada en compilación: no depende de reflexión, así que el catálogo
/// también carga si la app se publica recortada (trimming) o con AOT nativo.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(List<CleanRule>))]
internal sealed partial class RuleJsonContext : JsonSerializerContext;

public static class RuleCatalog
{
    private const string ResourceName = "PulsoWin.rules.json";
    private static readonly string[] KnownProfiles = ["chromium", "firefox"];

    public static IReadOnlyList<CleanRule> LoadBuiltIn()
    {
        using var stream = typeof(RuleCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("Falta el catálogo de reglas embebido.");
        return Parse(stream);
    }

    public static IReadOnlyList<CleanRule> Parse(Stream json)
    {
        var rules = JsonSerializer.Deserialize(json, RuleJsonContext.Default.ListCleanRule) ?? [];
        Validate(rules);
        return rules;
    }

    private static void Validate(List<CleanRule> rules)
    {
        if (rules.Any(r => r is null || r.Id is null))
            throw new InvalidDataException("El catálogo contiene una regla nula o sin id.");

        var duplicated = rules.GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicated is not null)
            throw new InvalidDataException($"Regla duplicada: {duplicated.Key}");

        foreach (var rule in rules)
        {
            // Un null explícito en el JSON ("pattern": null) se rechaza en vez de fallar más adelante.
            if (rule is null || rule.Id is null || rule.Base is null || rule.Pattern is null
                || rule.SubPaths is null || rule.SubPaths.Any(s => s is null) || rule.BlockingProcesses is null)
                throw new InvalidDataException($"[{rule?.Id ?? "?"}] la regla tiene campos nulos.");
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

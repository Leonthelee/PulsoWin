// PulsoWin · MIT · Co-designed with AI coding assistants (Claude & Gemini)
using System.Collections;
using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using PulsoWin.Core.Cleaning;
using PulsoWin.Core.Localization;
using PulsoWin.Presentation.Services;

namespace PulsoWin.Tests;

/// <summary>Las pruebas comparan textos en español: se fija el idioma una vez, sin depender del Windows que las corre.</summary>
internal static class TestCulture
{
    [ModuleInitializer]
    internal static void UseSpanish() => L.Culture = CultureInfo.GetCultureInfo("es");
}

public sealed partial class LocalizationTests
{
    [GeneratedRegex(@"\{(\d+)(?::[^}]*)?\}")]
    private static partial Regex Placeholder();

    // L.T("Clave"), L.F("Clave", …), L.T("Clave", cultura) y {l:Tr Clave} en XAML
    [GeneratedRegex(@"\bL\.[TF]\(""([A-Za-z0-9_\-]+)""|\{l:Tr ([A-Za-z0-9_\-]+)\}")]
    private static partial Regex KeyUse();

    private static Dictionary<string, string> Entries(string culture)
    {
        var set = L.Resources.GetResourceSet(
            culture.Length == 0 ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(culture),
            createIfNotExists: true, tryParents: false)!;
        return set.Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PulsoWin.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
    }

    public static TheoryData<string> OtherLanguages() => new(L.Supported.Where(c => c != "es"));

    [Theory]
    [MemberData(nameof(OtherLanguages))]
    public void Every_language_has_every_text_with_the_same_placeholders(string culture)
    {
        var spanish = Entries("");
        var other = Entries(culture);

        Assert.Empty(spanish.Keys.Except(other.Keys));                       // nada sin traducir
        Assert.Empty(other.Keys.Except(spanish.Keys));                       // nada sobrante
        foreach (var (key, es) in spanish)
        {
            Assert.False(string.IsNullOrWhiteSpace(other[key]), $"{culture}: «{key}» vacío");
            var expected = Placeholder().Matches(es).Select(m => m.Groups[1].Value).Order();
            var actual = Placeholder().Matches(other[key]).Select(m => m.Groups[1].Value).Order();
            Assert.True(expected.SequenceEqual(actual), $"{culture}: «{key}» cambia los marcadores {{0}}");
        }
    }

    [Fact]
    public void Every_key_used_in_code_and_xaml_exists()
    {
        var spanish = Entries("");
        var src = Path.Combine(RepoRoot(), "src");
        var missing = Directory.EnumerateFiles(src, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".cs") || f.EndsWith(".xaml"))
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(f => KeyUse().Matches(File.ReadAllText(f))
                .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
                .Where(key => !spanish.ContainsKey(key))
                .Select(key => $"{Path.GetFileName(f)}: {key}"))
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void Every_cleaning_rule_has_its_translations()
    {
        var spanish = Entries("");
        foreach (var rule in RuleCatalog.LoadBuiltIn())
        {
            Assert.Equal(rule.Name, spanish[$"Rule_{rule.Id}_Name"]);      // el español es el mismo del catálogo
            Assert.True(spanish.ContainsKey($"Category_{rule.Category}"), rule.Category);
            if (rule.Note is not null) Assert.Equal(rule.Note, spanish[$"Rule_{rule.Id}_Note"]);
        }
    }

    [Theory]
    [InlineData("es-CL", "es")]
    [InlineData("es", "es")]
    [InlineData("EN-us", "en")]
    [InlineData("fr-FR", "en")]   // idioma sin traducción: inglés
    [InlineData("", "en")]
    [InlineData(null, "en")]
    public void Unknown_languages_fall_back_to_english(string? code, string expected)
    {
        Assert.Equal(expected, L.Pick(code).TwoLetterISOLanguageName);
    }

    [Fact]
    public void English_texts_are_really_english()
    {
        var en = CultureInfo.GetCultureInfo("en");
        Assert.Equal("CLEAN", L.T("Tab_Clean", en));
        Assert.Equal("LIMPIAR", L.T("Tab_Clean", CultureInfo.GetCultureInfo("es")));
        Assert.Equal("Thanks for using the APP, I hope it's useful and helpful. =)", L.T("Egg_Thanks", en));
    }

    [Fact]
    public void Language_preference_is_saved_loaded_and_toggled()
    {
        var file = Path.Combine(Path.GetTempPath(), "pulsowin-idioma-" + Guid.NewGuid().ToString("N"), "idioma.txt");
        try
        {
            var windowsSpanish = CultureInfo.GetCultureInfo("es-CL");
            Assert.Null(LanguagePreference.Load(file));
            Assert.Equal("es", LanguagePreference.Resolve(null, windowsSpanish).TwoLetterISOLanguageName);      // sin elección: Windows

            Assert.True(LanguagePreference.Save("en-US", file));
            Assert.Equal("en", LanguagePreference.Load(file));                                                  // se guarda normalizado
            Assert.Equal("en", LanguagePreference.Resolve(LanguagePreference.Load(file), windowsSpanish).TwoLetterISOLanguageName);

            Assert.Equal("en", LanguagePreference.Next(CultureInfo.GetCultureInfo("es")));
            Assert.Equal("es", LanguagePreference.Next(CultureInfo.GetCultureInfo("en")));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(file)!, recursive: true);
        }
    }
}

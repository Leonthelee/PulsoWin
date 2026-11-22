// PulsoWin · MIT · Co-designed with AI coding assistants (Claude & Gemini)
using System.Globalization;
using System.Resources;

namespace PulsoWin.Core.Localization;

/// <summary>
/// Textos visibles de PulsoWin en el idioma elegido. El español es el idioma base (Texts.resx) y el inglés
/// vive en Texts.en.resx. Para sumar un idioma basta otro Texts.xx.resx con las mismas claves y agregarlo a
/// <see cref="Supported"/>; una prueba revisa que no falte ninguna clave ni cambien los marcadores {0}.
/// </summary>
public static class L
{
    /// <summary>Idiomas disponibles (código ISO de dos letras), en el orden en que se ofrecen.</summary>
    public static readonly IReadOnlyList<string> Supported = ["es", "en"];

    public static ResourceManager Resources { get; } = new("PulsoWin.Core.Localization.Texts", typeof(L).Assembly);

    /// <summary>Idioma de los textos. Lo fija la app al arrancar (preferencia guardada o idioma de Windows).</summary>
    public static CultureInfo Culture { get; set; } = Pick(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);

    /// <summary>
    /// Elige el idioma: el pedido si existe; si no, inglés (es el más entendido fuera del español).
    /// </summary>
    public static CultureInfo Pick(string? code)
    {
        var two = code?.Trim().ToLowerInvariant() is { Length: >= 2 } c ? c[..2] : "";
        return CultureInfo.GetCultureInfo(Supported.Contains(two) ? two : "en");
    }

    /// <summary>Texto de la clave en el idioma actual (la clave misma si faltara, para que se note).</summary>
    public static string T(string key) => T(key, Culture);

    public static string T(string key, CultureInfo culture) => Resources.GetString(key, culture) ?? key;

    /// <summary>Texto con valores. Los números y fechas siguen la configuración regional de Windows.</summary>
    public static string F(string key, params object?[] args) => string.Format(CultureInfo.CurrentCulture, T(key), args);

    /// <summary>Texto de la clave, o <paramref name="fallback"/> si no existe (por ejemplo, una regla nueva sin traducir).</summary>
    public static string TOr(string key, string fallback) => Resources.GetString(key, Culture) ?? fallback;
}

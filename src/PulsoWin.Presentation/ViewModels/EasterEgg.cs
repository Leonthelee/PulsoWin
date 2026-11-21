using System.Reflection;

namespace PulsoWin.Presentation.ViewModels;

/// <summary>
/// Detecta el código Konami (↑ ↑ ↓ ↓ ← → ← → B A). Recibe nombres de tecla neutros
/// ("Up", "Down", "Left", "Right", "B", "A") para no depender de WPF y poder probarse.
/// Una tecla equivocada reinicia la secuencia, pero si esa tecla es el inicio válido, cuenta.
/// </summary>
public sealed class KonamiCode
{
    private static readonly string[] Sequence = ["Up", "Up", "Down", "Down", "Left", "Right", "Left", "Right", "B", "A"];
    private int _position;

    /// <summary>Registra una tecla. Devuelve true justo cuando se completa el código.</summary>
    public bool Push(string key)
    {
        if (string.Equals(key, Sequence[_position], StringComparison.OrdinalIgnoreCase))
        {
            _position++;
            if (_position < Sequence.Length) return false;
            _position = 0;
            return true;
        }

        // "Up Up Up Down…" sigue siendo válido: los dos últimos "Up" cuentan como inicio.
        _position = _position == 2 && string.Equals(key, "Up", StringComparison.OrdinalIgnoreCase) ? 2
                  : string.Equals(key, Sequence[0], StringComparison.OrdinalIgnoreCase) ? 1
                  : 0;
        return false;
    }
}

/// <summary>
/// Firma de autoría que muestra el huevo de pascua. Se lee de los AssemblyMetadata del ensamblado
/// (definidos una sola vez en Directory.Build.props), así el texto visible y el oculto en el binario
/// nunca se desincronizan.
/// </summary>
public sealed record AuthorSignature(string Author, string Profile, string HelloDev)
{
    public static AuthorSignature FromAssembly(Assembly assembly)
    {
        var meta = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(a => a.Value is not null)
            .GroupBy(a => a.Key)
            .ToDictionary(g => g.Key, g => g.First().Value!);
        return new AuthorSignature(
            meta.GetValueOrDefault("Author", "PulsoWin"),
            meta.GetValueOrDefault("Profile", "https://github.com/Leonthelee/PulsoWin"),
            meta.GetValueOrDefault("HelloDev", "Gracias por usar PulsoWin."));
    }

    /// <summary>Solo se abren enlaces https: el texto viene del binario, pero no se confía a ciegas.</summary>
    public bool HasSafeProfileLink =>
        Uri.TryCreate(Profile, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}

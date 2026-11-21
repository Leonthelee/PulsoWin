namespace PulsoWin.Core.Cleaning;

/// <summary>Enumera perfiles de navegador. Se recorren todos, no solo "Default".</summary>
public static class ProfileLocator
{
    public static IEnumerable<string> Expand(string baseDir, string? profiles)
    {
        if (!Directory.Exists(baseDir))
            return [];

        if (profiles is null)
            return [baseDir];

        try
        {
            // Chromium: cada perfil (Default, Profile 1, ...) tiene un archivo Preferences.
            // Firefox: cada subcarpeta de Profiles es un perfil.
            return Directory.EnumerateDirectories(baseDir)
                .Where(dir => profiles == "firefox" || File.Exists(Path.Combine(dir, "Preferences")))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}

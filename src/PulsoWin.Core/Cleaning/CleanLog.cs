// PulsoWin · MIT · Co-designed with AI coding assistants (Claude & Gemini)
namespace PulsoWin.Core.Cleaning;

public static class CleanLog
{
    public static string Write(string dataDir, IEnumerable<string> lines)
    {
        var dir = Path.Combine(dataDir, "logs");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"limpieza-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        File.WriteAllLines(path, lines);
        return path;
    }
}

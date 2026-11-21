using PulsoWin.Core.Common;

namespace PulsoWin.Tests;

internal sealed class FakeProbe(params string[] running) : IProcessProbe
{
    public bool IsRunning(string processName) =>
        running.Contains(processName, StringComparer.OrdinalIgnoreCase);
}

/// <summary>Carpeta temporal propia de cada prueba; se borra al terminar.</summary>
internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pulsowin-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string relative, int bytes = 10, DateTime? lastWriteUtc = null)
    {
        var full = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllBytes(full, new byte[bytes]);
        System.IO.File.SetLastWriteTimeUtc(full, lastWriteUtc ?? DateTime.UtcNow.AddDays(-3));
        return full;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { }
    }
}

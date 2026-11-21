using System.Diagnostics;

namespace PulsoWin.Core.Common;

/// <summary>Permite saber si un proceso está corriendo (inyectable para pruebas).</summary>
public interface IProcessProbe
{
    bool IsRunning(string processName);
}

public sealed class ProcessProbe : IProcessProbe
{
    public bool IsRunning(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var p in processes) p.Dispose();
        }
    }
}

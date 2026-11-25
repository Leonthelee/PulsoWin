// PulsoWin · MIT · Co-designed with AI coding assistants (Claude & Gemini)
using System.ComponentModel;
using System.Diagnostics;

namespace PulsoWin.Core.Common;

/// <summary>Permite saber si un proceso está corriendo en la sesión del usuario actual (inyectable para pruebas).</summary>
public interface IProcessProbe
{
    bool IsRunning(string processName);
}

public sealed class ProcessProbe : IProcessProbe
{
    public static readonly int CurrentSessionId = GetCurrentSessionId();

    private static int GetCurrentSessionId()
    {
        using var self = Process.GetCurrentProcess();
        return self.SessionId;
    }

    public bool IsRunning(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        try
        {
            foreach (var p in processes)
            {
                try
                {
                    if (p.SessionId == CurrentSessionId)
                        return true;
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                {
                    // El proceso terminó entre la enumeración y la lectura.
                }
            }
            return false;
        }
        finally
        {
            foreach (var p in processes) p.Dispose();
        }
    }
}

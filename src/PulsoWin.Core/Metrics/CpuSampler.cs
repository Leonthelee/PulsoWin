using System.Runtime.InteropServices;

namespace PulsoWin.Core.Metrics;

/// <summary>
/// Uso total de CPU con la API Win32 GetSystemTimes: delta de (inactivo, kernel, usuario) entre dos llamadas.
/// Sin administrador y sin PerformanceCounter (cuyos nombres cambian según el idioma de Windows).
/// La primera llamada solo fija la referencia y devuelve null.
/// </summary>
public sealed class CpuSampler
{
    private readonly object _gate = new();
    private long _prevIdle, _prevKernel, _prevUser;
    private bool _hasPrevious;

    public double? Sample()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return null;
        return Next(idle, kernel, user);
    }

    /// <summary>Cálculo puro a partir de tiempos acumulados (se prueba sin llamar a Windows).</summary>
    public double? Next(long idle, long kernel, long user)
    {
        lock (_gate)
        {
            double? result = null;
            if (_hasPrevious)
            {
                var total = (kernel - _prevKernel) + (user - _prevUser); // el tiempo de kernel incluye el inactivo
                if (total > 0)
                    result = Math.Round(Math.Clamp(100.0 * (total - (idle - _prevIdle)) / total, 0, 100), 1);
            }
            (_prevIdle, _prevKernel, _prevUser, _hasPrevious) = (idle, kernel, user, true);
            return result;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);
}

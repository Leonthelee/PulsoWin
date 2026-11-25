// PulsoWin · MIT · Co-designed with AI coding assistants (Claude & Gemini)
using System.ComponentModel;
using System.Diagnostics;

namespace PulsoWin.Core.Common;

/// <summary>
/// Cierra navegadores Chromium en 2 fases ordenadas (ventanas visibles primero;
/// procesos en segundo plano sin ventana después) para liberar bloqueos de caché/cookies.
/// </summary>
public interface IBrowserCloser
{
    IReadOnlyList<string> Running(IEnumerable<string> processNames);

    bool HasVisibleWindow(string processName);

    Task<IReadOnlyList<string>> CloseAsync(IEnumerable<string> processNames, TimeSpan timeout);

    /// <summary>
    /// Cierra únicamente procesos en segundo plano sin ventana (ej. Edge Startup Boost) de los navegadores
    /// que NO tengan ninguna ventana visible abierta. Nunca cierra ventanas del usuario.
    /// </summary>
    Task CloseHeadlessAsync(IEnumerable<string> processNames, TimeSpan timeout);
}

internal interface IBrowserProcess : IDisposable
{
    bool HasMainWindow { get; }
    void CloseMainWindow();
    void Kill();
}

public sealed class BrowserCloser : IBrowserCloser
{
    public static readonly IReadOnlyList<string> ChromiumBrowsers = ["chrome", "msedge"];

    private readonly Func<string, IReadOnlyList<IBrowserProcess>> _enumerate;
    private readonly TimeSpan _pollDelay;
    private readonly TimeSpan _settleDelay;

    public BrowserCloser()
        : this(EnumerateCurrentSessionProcesses, TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(400))
    {
    }

    internal BrowserCloser(
        Func<string, IReadOnlyList<IBrowserProcess>> enumerate,
        TimeSpan pollDelay,
        TimeSpan settleDelay)
    {
        _enumerate = enumerate;
        _pollDelay = pollDelay;
        _settleDelay = settleDelay;
    }

    public IReadOnlyList<string> Running(IEnumerable<string> processNames) =>
        processNames.Where(IsRunning).ToList();

    public async Task<IReadOnlyList<string>> CloseAsync(IEnumerable<string> processNames, TimeSpan timeout)
    {
        var names = processNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (names.Count == 0) return [];

        var totalTimeout = timeout > TimeSpan.Zero ? timeout : TimeSpan.FromMilliseconds(50);
        var windowBudget = TimeSpan.FromMilliseconds(Math.Max(totalTimeout.TotalMilliseconds * 0.75, _pollDelay.TotalMilliseconds));
        var windowDeadline = DateTime.UtcNow + windowBudget;
        var finalDeadline = DateTime.UtcNow + totalTimeout;

        // Fase 1: cerrar de forma ordenada todas las ventanas visibles (incluidas ventanas múltiples).
        while (true)
        {
            bool anyVisibleWindow = false;
            foreach (var name in names)
            {
                foreach (var p in _enumerate(name))
                {
                    using (p)
                    {
                        try
                        {
                            if (p.HasMainWindow)
                            {
                                anyVisibleWindow = true;
                                p.CloseMainWindow();
                            }
                        }
                        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                        {
                            // El proceso terminó entre la consulta y el cierre.
                        }
                    }
                }
            }

            if (!anyVisibleWindow || DateTime.UtcNow >= windowDeadline)
                break;

            await Task.Delay(_pollDelay).ConfigureAwait(false);
        }

        // Identificar qué navegadores ya NO tienen ninguna ventana visible (seguro terminar su modo segundo plano).
        var safeForBackgroundCleanup = names.Where(n => !HasVisibleWindow(n)).ToList();
        await CloseHeadlessCoreAsync(safeForBackgroundCleanup, finalDeadline).ConfigureAwait(false);

        return Running(names);
    }

    public Task CloseHeadlessAsync(IEnumerable<string> processNames, TimeSpan timeout)
    {
        var names = processNames
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(n => IsRunning(n) && !HasVisibleWindow(n))
            .ToList();
        if (names.Count == 0) return Task.CompletedTask;

        var totalTimeout = timeout > TimeSpan.Zero ? timeout : TimeSpan.FromMilliseconds(50);
        return CloseHeadlessCoreAsync(names, DateTime.UtcNow + totalTimeout);
    }

    private async Task CloseHeadlessCoreAsync(IReadOnlyList<string> safeNames, DateTime finalDeadline)
    {
        while (safeNames.Count > 0)
        {
            // Revalidar en cada ciclo que el usuario no acaba de abrir una ventana visible.
            var stillHeadlessOnly = safeNames.Where(n => !HasVisibleWindow(n)).ToList();
            if (stillHeadlessOnly.Count == 0) break;

            KillHeadlessProcesses(stillHeadlessOnly);

            if (Running(stillHeadlessOnly).Count == 0)
            {
                // Pausa breve para que Windows libere bloqueos NTFS (Cookies / Cache_Data) y por si
                // Startup Boost intenta relanzar un proceso sin ventana inmediatamente.
                if (_settleDelay > TimeSpan.Zero)
                    await Task.Delay(_settleDelay).ConfigureAwait(false);

                stillHeadlessOnly = safeNames.Where(n => !HasVisibleWindow(n)).ToList();
                if (Running(stillHeadlessOnly).Count == 0)
                    break;

                KillHeadlessProcesses(stillHeadlessOnly);
            }

            if (DateTime.UtcNow >= finalDeadline)
                break;

            await Task.Delay(_pollDelay).ConfigureAwait(false);
        }
    }

    private void KillHeadlessProcesses(IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            foreach (var p in _enumerate(name))
            {
                using (p)
                {
                    try
                    {
                        if (!p.HasMainWindow)
                            p.Kill();
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
                    {
                        // Ya terminó junto con el proceso padre o está saliendo.
                    }
                }
            }
        }
    }

    public bool HasVisibleWindow(string name)
    {
        var list = _enumerate(name);
        try
        {
            foreach (var p in list)
            {
                try
                {
                    if (p.HasMainWindow) return true;
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                {
                }
            }
            return false;
        }
        finally
        {
            foreach (var p in list) p.Dispose();
        }
    }

    private bool IsRunning(string name)
    {
        var list = _enumerate(name);
        foreach (var p in list) p.Dispose();
        return list.Count > 0;
    }

    private static IReadOnlyList<IBrowserProcess> EnumerateCurrentSessionProcesses(string name)
    {
        var raw = Process.GetProcessesByName(name);
        var result = new List<IBrowserProcess>(raw.Length);
        foreach (var p in raw)
        {
            bool sameSession = false;
            try
            {
                sameSession = p.SessionId == ProcessProbe.CurrentSessionId;
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
            }

            if (sameSession)
                result.Add(new SystemBrowserProcess(p));
            else
                p.Dispose();
        }
        return result;
    }

    private sealed class SystemBrowserProcess(Process inner) : IBrowserProcess
    {
        public bool HasMainWindow => inner.MainWindowHandle != IntPtr.Zero;
        public void CloseMainWindow() => inner.CloseMainWindow();
        public void Kill() => inner.Kill();
        public void Dispose() => inner.Dispose();
    }
}

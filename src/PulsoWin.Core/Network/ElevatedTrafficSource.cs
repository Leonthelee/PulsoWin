// PulsoWin · MIT · Co-designed with AI coding assistants (Claude & Gemini)
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using PulsoWin.Core.Localization;

namespace PulsoWin.Core.Network;

/// <summary>
/// Red por proceso SIN elevar la app: un proceso auxiliar elevado (<c>PulsoWin.exe --net-helper</c>) abre la sesión
/// ETW y solo envía números (PID, generación, bytes recibidos, bytes enviados) por una tubería con nombre privada.
/// - La app (sin administrador) crea la tubería con un nombre aleatorio. Pueden abrirla el usuario actual y los
///   administradores (el aviso UAC puede aprobarse con otra cuenta de administrador).
/// - Las dos puntas se autentican por PID: la app solo acepta al auxiliar que ella lanzó, y el auxiliar solo escribe
///   si el servidor de la tubería es la app que indica su línea de comandos.
/// - El auxiliar nunca lee nada de la app: no recibe órdenes, así que no hay nada que inyectarle.
/// - Si el auxiliar se cierra, la fuente queda en falla (<see cref="IsFaulted"/>) y el motor vuelve a la vista por interfaz.
/// Los nombres de proceso se resuelven aquí, del lado sin privilegios.
/// </summary>
public sealed class ElevatedTrafficSource(Func<string, Process?>? launchHelper = null, TimeSpan? connectTimeout = null) : ITrafficSource
{
    public const string Switch = "--net-helper";
    private const string PipePrefix = "PulsoWin-Red-";

    private readonly Func<string, Process?> _launch = launchHelper ?? LaunchElevatedHelper;
    private readonly TimeSpan _connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(60);
    private readonly ProcessNameCache _names = new();
    private NamedPipeServerStream? _pipe;
    private Process? _helper;
    private Thread? _reader;
    private volatile IReadOnlyList<WireRow> _latest = [];
    private volatile bool _faulted;

    public NetworkMonitorMode Mode => NetworkMonitorMode.PerProcess;

    /// <summary>true cuando el auxiliar dejó de enviar (se cerró o cortó la tubería): sus cifras ya no son actuales.</summary>
    public bool IsFaulted => _faulted;

    /// <summary>Nombre aceptado por el auxiliar: el prefijo y un GUID sin guiones, nada más.</summary>
    public static bool IsValidPipeName(string? name) => PrivatePipe.IsValidName(name, PipePrefix);

    public void Start()
    {
        var name = PrivatePipe.NewName(PipePrefix);
        _pipe = PrivatePipe.CreateServer(name);

        _helper = _launch(name) ?? throw new UnauthorizedAccessException(L.T("Net_HelperDeclined"));

        using var timeout = new CancellationTokenSource(_connectTimeout);
        var connect = _pipe.WaitForConnectionAsync(timeout.Token);
        var exited = _helper.WaitForExitAsync(timeout.Token);
        Task.WhenAny(connect, exited).GetAwaiter().GetResult();

        if (!connect.IsCompletedSuccessfully)
        {
            timeout.Cancel();   // libera la espera que no ganó
            throw HelperGone();
        }

        // Solo se aceptan cifras del auxiliar que lanzó ESTA app, no de cualquier proceso que abra la tubería.
        if (!PipePeers.TryGetClientProcessId(_pipe, out var clientPid) || clientPid != _helper.Id)
        {
            timeout.Cancel();
            throw new UnauthorizedAccessException(L.T("Net_HelperWrongPeer"));
        }

        // Conectarse no basta: el auxiliar envía su primera foto solo después de abrir ETW. Si ETW falla, se cierra
        // sin enviarla y el cambio se informa como fallido, en vez de mostrar un segundo la vista por proceso.
        var pipe = _pipe;
        _reader = new Thread(() => ReadFrames(pipe)) { IsBackground = true, Name = "PulsoWin red elevada" };
        _reader.Start();
        try
        {
            if (_firstFrame.Task.WaitAsync(timeout.Token).GetAwaiter().GetResult()) return;
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException(L.T("Net_HelperTimeout"));
        }
        throw HelperGone();
    }

    private readonly TaskCompletionSource<bool> _firstFrame = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Exception HelperGone() =>
        _helper is not null && WaitExit(_helper) && HasExited(_helper, out var code)
            ? new InvalidOperationException(L.F("Net_HelperExited", code))
            : new TimeoutException(L.T("Net_HelperTimeout"));

    private void ReadFrames(Stream pipe)
    {
        try
        {
            using var reader = new StreamReader(pipe);
            foreach (var frame in TrafficWire.ReadFrames(reader))
            {
                _latest = frame;
                _firstFrame.TrySetResult(true);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // La tubería se cortó: se trata igual que el fin de la transmisión.
        }
        // La última foto se conserva: el motor la cuenta antes de volver a la vista por interfaz.
        _faulted = true;
        _firstFrame.TrySetResult(false);
    }

    public IReadOnlyList<TrafficCounter> ReadCounters()
    {
        var rows = _latest;
        _names.Retain(rows.Select(t => (t.Pid, t.Generation)));
        return rows.Select(t => new TrafficCounter(
                $"pid:{t.Pid}:{t.Generation}", t.Pid, _names.Get(t.Pid, t.Generation), t.Rx, t.Tx, t.Generation))
            .ToList();
    }

    public void Dispose()
    {
        _pipe?.Dispose();            // corta la tubería: el auxiliar detecta el corte y cierra su sesión ETW
        _reader?.Join(TimeSpan.FromSeconds(2));
        _helper?.Dispose();
        _pipe = null;
        _helper = null;
    }

    /// <summary>Si la tubería se cortó, el auxiliar suele estar terminando: se le da un momento para leer su código.</summary>
    private static bool WaitExit(Process process)
    {
        try { process.WaitForExit(2000); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        return true;
    }

    private static bool HasExited(Process process, out string code)
    {
        code = "?";
        try
        {
            if (!process.HasExited) return false;
            code = process.ExitCode.ToString(CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    /// <summary>Relanza este mismo .exe en modo auxiliar con el aviso de administrador. null si se rechazó.</summary>
    private static Process? LaunchElevatedHelper(string pipeName)
    {
        try
        {
            return Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = $"{Switch} {pipeName} {Environment.ProcessId.ToString(CultureInfo.InvariantCulture)}",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null; // 1223: la persona rechazó el aviso
        }
    }
}

/// <summary>Una fila de la tubería: PID, generación del PID (sube si Windows lo reutiliza) y bytes acumulados.</summary>
public readonly record struct WireRow(int Pid, int Generation, long Rx, long Tx);

/// <summary>
/// Formato de la tubería: una línea «pid generación rx tx» por proceso y una línea vacía al final de cada foto.
/// Solo números enteros no negativos; cualquier línea que no calce se ignora.
/// </summary>
public static class TrafficWire
{
    /// <summary>Tope de filas por foto: una foto anómala no puede llenar la memoria.</summary>
    public const int MaxRowsPerFrame = 4096;

    public static void WriteFrame(TextWriter writer, IEnumerable<TrafficCounter> counters)
    {
        // Si alguna vez hay más filas que el tope, quedan fuera las de menos tráfico, no unas al azar. El total de la
        // sesión no baja por eso: el calculador acumula lo ya contado (ver TrafficRateCalculator).
        foreach (var c in counters.Where(c => c.Pid is >= 0).OrderByDescending(c => c.RxTotal + c.TxTotal).Take(MaxRowsPerFrame))
            writer.Write(string.Create(CultureInfo.InvariantCulture, $"{c.Pid} {c.Generation} {c.RxTotal} {c.TxTotal}\n"));
        writer.Write('\n');
        writer.Flush();
    }

    public static IEnumerable<IReadOnlyList<WireRow>> ReadFrames(TextReader reader)
    {
        var frame = new List<WireRow>();
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0)
            {
                yield return frame;
                frame = [];
                continue;
            }
            if (frame.Count < MaxRowsPerFrame && TryParse(line, out var row)) frame.Add(row);
        }
    }

    private static bool TryParse(string line, out WireRow row)
    {
        row = default;
        var parts = line.Split(' ');
        if (parts.Length != 4) return false;
        var ok = int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var pid)
                 & int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var gen)
                 & long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var rx)
                 & long.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var tx);
        if (ok) row = new WireRow(pid, gen, rx, tx);
        return ok;
    }
}

/// <summary>
/// Tubería privada entre la app (sin administrador) y un auxiliar elevado que ella lanzó.
/// Nombre: prefijo + GUID sin guiones. Acceso: usuario actual (control total) y administradores (lectura/escritura),
/// porque el aviso UAC puede aprobarse con otra cuenta de administrador. Cada punta verifica además el PID de la otra.
/// </summary>
public static class PrivatePipe
{
    public static string NewName(string prefix) => prefix + Guid.NewGuid().ToString("N");

    public static bool IsValidName(string? name, string prefix) =>
        name is not null && name.StartsWith(prefix, StringComparison.Ordinal)
        && Guid.TryParseExact(name[prefix.Length..], "N", out _);

    public static NamedPipeServerStream CreateServer(string name) =>
        NamedPipeServerStreamAcl.Create(name, PipeDirection.In, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous, 0, 0, Access());

    private static PipeSecurity Access()
    {
        var security = new PipeSecurity();
        using var me = WindowsIdentity.GetCurrent();
        security.AddAccessRule(new PipeAccessRule(me.User!, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.ReadWrite, AccessControlType.Allow));
        return security;
    }

    /// <summary>
    /// Lado elevado: el proceso que lo lanzó debe ser este mismo ejecutable. Si no se puede leer, se rechaza.
    /// </summary>
    public static bool ParentIsSameExecutable(Process parent)
    {
        var own = Environment.ProcessPath;
        var theirs = parent.MainModule?.FileName;
        return own is not null && theirs is not null
               && string.Equals(Path.GetFullPath(own), Path.GetFullPath(theirs), StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>PID del otro extremo de una tubería con nombre (kernel32).</summary>
public static class PipePeers
{
    public static bool TryGetClientProcessId(PipeStream pipe, out int pid) =>
        Query(GetNamedPipeClientProcessId, pipe, out pid);

    public static bool TryGetServerProcessId(PipeStream pipe, out int pid) =>
        Query(GetNamedPipeServerProcessId, pipe, out pid);

    private delegate bool PeerQuery(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint pid);

    private static bool Query(PeerQuery query, PipeStream pipe, out int pid)
    {
        pid = 0;
        if (!query(pipe.SafePipeHandle, out var raw)) return false;
        pid = unchecked((int)raw);
        return true;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool GetNamedPipeClientProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint clientProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint serverProcessId);
}

/// <summary>
/// Lado elevado: abre ETW y escribe una foto por segundo en la tubería de la app que lo lanzó.
/// Acepta solo «--net-helper &lt;tubería válida&gt; &lt;PID de la app&gt;»: la app debe ser este mismo ejecutable
/// y debe ser ella quien tiene abierta la tubería. Códigos: 2 argumentos, 3 app distinta, 5 tubería ajena.
/// </summary>
public static class NetworkHelper
{
    public static int Run(IReadOnlyList<string> args, Func<ITrafficSource>? etwFactory = null, TimeSpan? interval = null,
        Func<Process, bool>? isSameExecutable = null)
    {
        if (args.Count != 3 || args[0] != ElevatedTrafficSource.Switch
            || !ElevatedTrafficSource.IsValidPipeName(args[1])
            || !int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out var parentPid))
            return 2;

        Process parent;
        try
        {
            parent = Process.GetProcessById(parentPid);
            if (!(isSameExecutable ?? PrivatePipe.ParentIsSameExecutable)(parent)) { parent.Dispose(); return 3; }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return 3;
        }

        try
        {
            using (parent)
            using (var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.Out))
            {
                pipe.Connect(10_000);
                // Las cifras elevadas solo van a la app indicada: otra instancia o un proceso ajeno no las reciben.
                if (!PipePeers.TryGetServerProcessId(pipe, out var serverPid) || serverPid != parentPid) return 5;

                using var etw = (etwFactory ?? (() => new EtwTrafficSource()))();
                etw.Start();
                using var writer = new StreamWriter(pipe) { NewLine = "\n" };
                var wait = interval ?? TimeSpan.FromSeconds(1);
                while (!parent.HasExited)
                {
                    TrafficWire.WriteFrame(writer, etw.ReadCounters());
                    Thread.Sleep(wait);
                }
            }
        }
        catch (IOException)
        {
            // La app cerró la tubería: se termina y los using detienen la sesión ETW.
        }
        catch (TimeoutException)
        {
            return 4;   // la app ya no esperaba la conexión
        }
        return 0;
    }
}

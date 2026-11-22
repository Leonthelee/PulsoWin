// PulsoWin · MIT · Co-designed with AI coding assistants (Claude & Gemini)
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using PulsoWin.Core.Localization;

namespace PulsoWin.Core.Network;

/// <summary>
/// Red por proceso SIN elevar la app: un proceso auxiliar elevado (<c>PulsoWin.exe --net-helper</c>) abre la sesión
/// ETW y solo envía números (PID, bytes recibidos, bytes enviados) por una tubería con nombre privada.
/// - La app (sin administrador) crea la tubería con un nombre aleatorio y acceso solo para el usuario actual.
/// - El auxiliar nunca lee nada de la app: no recibe órdenes, así que no hay nada que inyectarle.
/// - El auxiliar termina solo cuando la app se cierra o la tubería se corta.
/// Los nombres de proceso se resuelven aquí, del lado sin privilegios.
/// </summary>
public sealed class ElevatedTrafficSource(Func<string, bool>? launchHelper = null, TimeSpan? connectTimeout = null) : ITrafficSource
{
    public const string Switch = "--net-helper";
    private const string PipePrefix = "PulsoWin-Red-";

    private readonly Func<string, bool> _launch = launchHelper ?? LaunchElevatedHelper;
    private readonly TimeSpan _connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(60);
    private readonly ProcessNameCache _names = new();
    private NamedPipeServerStream? _pipe;
    private Thread? _reader;
    private volatile IReadOnlyList<(int Pid, long Rx, long Tx)> _latest = [];

    public NetworkMonitorMode Mode => NetworkMonitorMode.PerProcess;

    /// <summary>Nombre aceptado por el auxiliar: el prefijo y un GUID sin guiones, nada más.</summary>
    public static bool IsValidPipeName(string? name) =>
        name is not null && name.StartsWith(PipePrefix, StringComparison.Ordinal)
        && Guid.TryParseExact(name[PipePrefix.Length..], "N", out _);

    public void Start()
    {
        var name = PipePrefix + Guid.NewGuid().ToString("N");
        _pipe = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        if (!_launch(name))
            throw new UnauthorizedAccessException(L.T("Net_HelperDeclined"));

        using (var timeout = new CancellationTokenSource(_connectTimeout))
        {
            try
            {
                _pipe.WaitForConnectionAsync(timeout.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException(L.T("Net_HelperTimeout"));
            }
        }

        var pipe = _pipe;
        _reader = new Thread(() => ReadFrames(pipe)) { IsBackground = true, Name = "PulsoWin red elevada" };
        _reader.Start();
    }

    private void ReadFrames(Stream pipe)
    {
        try
        {
            using var reader = new StreamReader(pipe);
            foreach (var frame in TrafficWire.ReadFrames(reader))
                _latest = frame;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // La tubería se cerró (la app se cierra o el auxiliar terminó): se dejan de recibir cifras.
        }
    }

    public IReadOnlyList<TrafficCounter> ReadCounters() =>
        _latest.Select(t => new TrafficCounter($"pid:{t.Pid}", t.Pid, _names.Get(t.Pid), t.Rx, t.Tx)).ToList();

    public void Dispose()
    {
        _pipe?.Dispose();            // corta la tubería: el auxiliar detecta el corte y cierra su sesión ETW
        _reader?.Join(TimeSpan.FromSeconds(2));
        _pipe = null;
    }

    /// <summary>Relanza este mismo .exe en modo auxiliar con el aviso de administrador. false si se rechazó.</summary>
    private static bool LaunchElevatedHelper(string pipeName)
    {
        try
        {
            using var helper = Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = $"{Switch} {pipeName} {Environment.ProcessId.ToString(CultureInfo.InvariantCulture)}",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            return helper is not null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false; // 1223: la persona rechazó el aviso
        }
    }
}

/// <summary>
/// Formato de la tubería: una línea «pid rx tx» por proceso y una línea vacía al final de cada foto.
/// Solo números enteros; cualquier línea que no calce se ignora.
/// </summary>
public static class TrafficWire
{
    /// <summary>Tope de filas por foto: una foto anómala no puede llenar la memoria.</summary>
    public const int MaxRowsPerFrame = 4096;

    public static void WriteFrame(TextWriter writer, IEnumerable<TrafficCounter> counters)
    {
        foreach (var c in counters.Where(c => c.Pid is >= 0).Take(MaxRowsPerFrame))
            writer.Write(string.Create(CultureInfo.InvariantCulture, $"{c.Pid} {c.RxTotal} {c.TxTotal}\n"));
        writer.Write('\n');
        writer.Flush();
    }

    public static IEnumerable<IReadOnlyList<(int Pid, long Rx, long Tx)>> ReadFrames(TextReader reader)
    {
        var frame = new List<(int, long, long)>();
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

    private static bool TryParse(string line, out (int Pid, long Rx, long Tx) row)
    {
        row = default;
        var parts = line.Split(' ');
        if (parts.Length != 3) return false;
        var ok = int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var pid)
                 & long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var rx)
                 & long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var tx);
        if (ok) row = (pid, rx, tx);
        return ok;
    }
}

/// <summary>
/// Lado elevado: abre ETW y escribe una foto por segundo en la tubería de la app que lo lanzó.
/// Acepta solo «--net-helper &lt;tubería válida&gt; &lt;PID de la app&gt;», y la app debe ser este mismo ejecutable.
/// </summary>
public static class NetworkHelper
{
    public static int Run(IReadOnlyList<string> args, Func<ITrafficSource>? etwFactory = null, TimeSpan? interval = null)
    {
        if (args.Count != 3 || args[0] != ElevatedTrafficSource.Switch
            || !ElevatedTrafficSource.IsValidPipeName(args[1])
            || !int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out var parentPid))
            return 2;

        Process parent;
        try
        {
            parent = Process.GetProcessById(parentPid);
            if (!IsSameExecutable(parent)) { parent.Dispose(); return 3; }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return 3;
        }

        try
        {
            using (parent)
            using (var etw = (etwFactory ?? (() => new EtwTrafficSource()))())
            using (var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.Out))
            {
                etw.Start();
                pipe.Connect(10_000);
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

    private static bool IsSameExecutable(Process parent)
    {
        var own = Environment.ProcessPath;
        var theirs = parent.MainModule?.FileName;
        return own is not null && theirs is not null && string.Equals(Path.GetFullPath(own), Path.GetFullPath(theirs), StringComparison.OrdinalIgnoreCase);
    }
}

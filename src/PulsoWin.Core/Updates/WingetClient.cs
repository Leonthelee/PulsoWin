using System.ComponentModel;

namespace PulsoWin.Core.Updates;

/// <summary>Cliente de solo lectura y actualización controlada sobre <c>winget</c>.</summary>
public sealed class WingetClient
{
    private readonly IProcessRunner _runner;
    private readonly TimeSpan _scanTimeout;
    private readonly TimeSpan _upgradeTimeout;

    public WingetClient(IProcessRunner? runner = null, TimeSpan? scanTimeout = null, TimeSpan? upgradeTimeout = null)
    {
        _runner = runner ?? new ProcessRunner();
        _scanTimeout = scanTimeout ?? TimeSpan.FromSeconds(45);
        _upgradeTimeout = upgradeTimeout ?? TimeSpan.FromMinutes(10);
    }

    public async Task<WingetScanResult> ListUpgradesAsync(CancellationToken cancellationToken = default)
    {
        ProcessResult result;
        try
        {
            result = await _runner.RunAsync(
                "winget",
                ["upgrade", "--accept-source-agreements"],
                _scanTimeout,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Win32Exception)
        {
            return new WingetScanResult([], "winget no está instalado o no está en el PATH.");
        }

        if (result.TimedOut)
            return new WingetScanResult([], "winget tardó demasiado al consultar actualizaciones.");

        var packages = WingetParser.ParseUpgradeOutput(result.StdOut);
        if (packages.Count == 0 && result.ExitCode != 0 && !string.IsNullOrWhiteSpace(result.StdErr))
            return new WingetScanResult([], result.StdErr.Trim());

        return new WingetScanResult(packages);
    }

    public async Task<WingetUpgradeResult> UpgradeAsync(string packageId, CancellationToken cancellationToken = default)
    {
        if (!WingetParser.IsValidPackageId(packageId))
            return new WingetUpgradeResult(packageId, false, -1, "ID de paquete rechazado por validación.");

        ProcessResult result;
        try
        {
            result = await _runner.RunAsync(
                "winget",
                [
                    "upgrade",
                    "--id", packageId,
                    "--exact",
                    "--accept-source-agreements",
                    "--accept-package-agreements",
                    "--silent"
                ],
                _upgradeTimeout,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Win32Exception)
        {
            return new WingetUpgradeResult(packageId, false, -1, "winget no está disponible.");
        }

        if (result.TimedOut)
            return new WingetUpgradeResult(packageId, false, -1, "Tiempo de espera agotado.");

        if (result.ExitCode == 0)
            return new WingetUpgradeResult(packageId, true, 0, "Actualizado correctamente.");

        var detail = ExtractLastLine(result.StdErr) ?? ExtractLastLine(result.StdOut) ?? $"código {result.ExitCode}";
        return new WingetUpgradeResult(packageId, false, result.ExitCode, detail);
    }

    private static string? ExtractLastLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return text
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(l => l.Contains('\r') ? l[(l.LastIndexOf('\r') + 1)..] : l)
            .Select(l => l.Trim())
            .LastOrDefault(l => !string.IsNullOrWhiteSpace(l) && !l.All(c => c is '-' or '\\' or '|' or '/'));
    }
}

// PulsoWin · MIT · Co-designed with AI coding assistants (Claude & Gemini)
using System.ComponentModel;

namespace PulsoWin.Core.Updates;

/// <summary>Cliente de solo lectura y actualización controlada sobre <c>winget</c>.</summary>
public sealed class WingetClient
{
    private readonly IProcessRunner _runner;
    private readonly TimeSpan _scanTimeout;
    private readonly TimeSpan _upgradeTimeout;
    private readonly string? _wingetPath;

    /// <param name="wingetPath">
    /// Ruta completa de winget.exe. Por defecto, el alias que instala App Installer en
    /// %LOCALAPPDATA%\Microsoft\WindowsApps. Nunca se ejecuta "winget" a secas: Windows buscaría
    /// primero en la carpeta de la app y en la carpeta actual, donde alguien podría dejar un ejecutable falso.
    /// </param>
    public WingetClient(IProcessRunner? runner = null, TimeSpan? scanTimeout = null, TimeSpan? upgradeTimeout = null,
        string? wingetPath = null)
    {
        _runner = runner ?? new ProcessRunner();
        _scanTimeout = scanTimeout ?? TimeSpan.FromSeconds(45);
        _upgradeTimeout = upgradeTimeout ?? TimeSpan.FromMinutes(10);
        _wingetPath = wingetPath ?? DefaultWingetPath();
    }

    /// <summary>Ruta del alias de winget del usuario actual, o null si App Installer no está instalado.</summary>
    public static string? DefaultWingetPath()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (local.Length == 0) return null;
        var path = Path.Combine(local, "Microsoft", "WindowsApps", "winget.exe");
        return File.Exists(path) ? path : null;
    }

    public async Task<WingetScanResult> ListUpgradesAsync(CancellationToken cancellationToken = default)
    {
        if (_wingetPath is null)
            return new WingetScanResult([], Localization.L.T("Wg_NotInstalledStore"));

        ProcessResult result;
        try
        {
            result = await _runner.RunAsync(
                _wingetPath,
                ["upgrade", "--accept-source-agreements"],
                _scanTimeout,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Win32Exception)
        {
            return new WingetScanResult([], Localization.L.T("Wg_RunFailed"));
        }

        if (result.TimedOut)
            return new WingetScanResult([], Localization.L.T("Wg_ScanTimeout"));

        var packages = WingetParser.ParseUpgradeOutput(result.StdOut);
        if (packages.Count == 0 && result.ExitCode != 0 && !string.IsNullOrWhiteSpace(result.StdErr))
            return new WingetScanResult([], result.StdErr.Trim());

        return new WingetScanResult(packages);
    }

    public async Task<WingetUpgradeResult> UpgradeAsync(string packageId, CancellationToken cancellationToken = default)
    {
        if (!WingetParser.IsValidPackageId(packageId))
            return new WingetUpgradeResult(packageId, false, -1, Localization.L.T("Wg_BadId"));

        if (_wingetPath is null)
            return new WingetUpgradeResult(packageId, false, -1, Localization.L.T("Wg_NotInstalled"));

        ProcessResult result;
        try
        {
            result = await _runner.RunAsync(
                _wingetPath,
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
            return new WingetUpgradeResult(packageId, false, -1, Localization.L.T("Wg_Unavailable"));
        }

        if (result.TimedOut)
            return new WingetUpgradeResult(packageId, false, -1, Localization.L.T("Common_Timeout"));

        if (result.ExitCode == 0)
            return new WingetUpgradeResult(packageId, true, 0, Localization.L.T("Wg_Updated"));

        var detail = ExtractLastLine(result.StdErr) ?? ExtractLastLine(result.StdOut) ?? Localization.L.F("Common_Code", result.ExitCode);
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

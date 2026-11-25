<#
  PulsoWin · MIT · Co-designed with AI coding assistants (Claude & Gemini)
  Construye y valida el instalador MSI de PulsoWin.
  Uso (desde la raíz del repositorio, en PowerShell, SIN administrador):
      .\tools\build-msi.ps1
  Resultado: artifacts\msi\PulsoWin-<versión>-x64.msi
#>
[CmdletBinding()]
param(
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $root 'artifacts\publish'
$out = Join-Path $root 'artifacts\msi'

function Invoke-Step([string]$name, [scriptblock]$action) {
    Write-Host "==> $name" -ForegroundColor Cyan
    & $action
    if ($LASTEXITCODE -ne 0) { throw "Falló: $name (código $LASTEXITCODE)" }
}

Push-Location $root
try {
    $version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version |
        Where-Object { $_ } | Select-Object -First 1
    if ($version -notmatch '^\d+\.\d+\.\d+$') {
        throw "Versión inválida en Directory.Build.props: '$version'"
    }

    if (-not $SkipTests) {
        Invoke-Step 'Pruebas' { dotnet test PulsoWin.slnx -c Release --nologo }
    }

    if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
    Invoke-Step 'Publicar la app (self-contained, win-x64)' {
        dotnet publish src\PulsoWin.App\PulsoWin.App.csproj -c Release -r win-x64 --self-contained true `
            -p:PublishSingleFile=false -p:DebugType=none -warnaserror -o $publish --nologo
    }

    $pdbs = @(Get-ChildItem $publish -Filter '*.pdb' -Recurse)
    if ($pdbs.Count -gt 0) {
        throw "Se detectaron archivos .pdb en la publicación: $($pdbs.Name -join ', ')"
    }

    # El runtime publicado viaja dentro del MSI: nunca se empaqueta uno con vulnerabilidades conocidas.
    $MinRuntime = [version]'10.0.14'   # CVE-2026-111497 (Brotli) afecta a < 10.0.14
    $coreclr = Join-Path $publish 'coreclr.dll'
    if (-not (Test-Path $coreclr)) { throw 'La publicación no trae coreclr.dll: no es self-contained.' }
    $fv = (Get-Item $coreclr).VersionInfo
    $runtime = [version]::new($fv.FileMajorPart, $fv.FileMinorPart, [math]::Floor($fv.FileBuildPart / 100))
    if ($runtime -lt $MinRuntime) { throw "El runtime publicado es $runtime y el mínimo seguro es $MinRuntime." }
    Write-Host "    Runtime .NET empaquetado: $runtime (mínimo $MinRuntime)"

    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    Invoke-Step 'Armar el MSI (WiX v5)' {
        dotnet build installer\PulsoWin.Installer.wixproj -c Release `
            "-p:Version=$version" "-p:PublishDir=$publish/" "-p:OutputPath=$out/" --nologo
    }

    $msi = Get-ChildItem $out -Filter '*.msi' -Recurse | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($null -eq $msi) { throw 'No se generó ningún MSI.' }

    $published = (Get-ChildItem $publish -File -Recurse).Count
    $inspect = & (Join-Path $PSScriptRoot 'inspect-msi.ps1') -Path $msi.FullName
    $inspect | ForEach-Object { Write-Host "    $_" }
    $inMsi = [int](($inspect | Select-String '^Archivos\s+(\d+)').Matches[0].Groups[1].Value)
    if ($inMsi -ne $published) { throw "El MSI trae $inMsi archivos y la publicación tiene $published." }
    if ($inspect -match 'PRESENTE|FALTA') { throw 'El MSI trae algo no permitido o le falta PulsoWin.exe (ver arriba).' }
    Write-Host ""
    Write-Host "MSI listo: $($msi.FullName) ($([math]::Round($msi.Length / 1MB, 1)) MB)" -ForegroundColor Green
}
finally {
    Pop-Location
}

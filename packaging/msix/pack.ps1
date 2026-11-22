# PulsoWin · MIT
# Arma el paquete MSIX (sin firmar) para subirlo a la Microsoft Store, que lo firma al publicarlo.
#
#   .\packaging\msix\pack.ps1 -PublishDir artifacts\publish -Version 0.4.0 -Out artifacts\msix
#
# Para la Store, pasa los tres valores de Partner Center → Identidad del producto:
#   -Name "12345Leonthelee.PulsoWin" -Publisher "CN=XXXXXXXX-XXXX-..." -PublisherDisplayName "Leonthelee"
# Sin ellos usa una identidad de prueba: sirve para validar el paquete, no para subirlo.
param(
    [Parameter(Mandatory)] [string] $PublishDir,
    [Parameter(Mandatory)] [string] $Version,
    [string] $Out = "artifacts\msix",
    [string] $Name = "PulsoWin.Prueba",
    [string] $Publisher = "CN=PulsoWin Prueba",
    [string] $PublisherDisplayName = "PulsoWin (prueba)"
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Versión inválida: '$Version' (se espera X.Y.Z)." }
$exe = Join-Path $PublishDir "PulsoWin.exe"
if (-not (Test-Path $exe)) { throw "No está ${exe}: publica primero la app (dotnet publish)." }

# MakeAppx viene con el SDK de Windows; se toma la versión más nueva instalada.
$makeappx = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\makeappx.exe" -ErrorAction SilentlyContinue |
    Sort-Object { [version]$_.Directory.Parent.Name } | Select-Object -Last 1
if (-not $makeappx) { throw "No se encontró makeappx.exe (instala el SDK de Windows 10/11)." }

$root = Split-Path -Parent $PSScriptRoot | Split-Path -Parent
$stage = Join-Path $Out "stage"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage, (Join-Path $stage "Assets") | Out-Null
Copy-Item (Join-Path $PublishDir "*") $stage -Recurse

# Logos del paquete a partir del ícono de la app (mismo diseño, sin binarios extra en el repositorio).
$ico = Join-Path $root "src\PulsoWin.App\Assets\pulsowin.ico"
# Se toma el cuadro más grande del .ico. Si viene en PNG (lo normal en 256 px), se lee directo:
# System.Drawing.Icon de .NET Framework no sabe decodificarlo.
$bytes = [IO.File]::ReadAllBytes($ico)
$best = 0; $bestSize = -1
for ($i = 0; $i -lt [BitConverter]::ToUInt16($bytes, 4); $i++) {
    $w = $bytes[6 + 16 * $i]; if ($w -eq 0) { $w = 256 }
    if ($w -gt $bestSize) { $bestSize = $w; $best = $i }
}
$len = [BitConverter]::ToInt32($bytes, 6 + 16 * $best + 8)
$off = [BitConverter]::ToInt32($bytes, 6 + 16 * $best + 12)
if ($bytes[$off] -eq 0x89) {
    $big = New-Object System.Drawing.Bitmap((New-Object IO.MemoryStream($bytes, $off, $len)))
} else {
    $big = (New-Object System.Drawing.Icon($ico, $bestSize, $bestSize)).ToBitmap()
}
foreach ($logo in @(@{ File = "StoreLogo.png"; Size = 50 }, @{ File = "Square44x44Logo.png"; Size = 44 }, @{ File = "Square150x150Logo.png"; Size = 150 })) {
    $bmp = New-Object System.Drawing.Bitmap($logo.Size, $logo.Size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.DrawImage($big, 0, 0, $logo.Size, $logo.Size)
    $g.Dispose()
    $bmp.Save((Join-Path $stage "Assets\$($logo.File)"), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}
$big.Dispose()

# El manifiesto: la Store exige versión de 4 partes con la última en 0.
$esc = { param($s) [System.Security.SecurityElement]::Escape($s) }
$manifest = (Get-Content (Join-Path $PSScriptRoot "AppxManifest.xml") -Raw -Encoding UTF8).
    Replace("{{NAME}}", (& $esc $Name)).
    Replace("{{PUBLISHER}}", (& $esc $Publisher)).
    Replace("{{PUBLISHER_DISPLAY}}", (& $esc $PublisherDisplayName)).
    Replace("{{VERSION}}", "$Version.0")
[xml]$manifest | Out-Null   # falla aquí si quedó XML inválido
[System.IO.File]::WriteAllText((Join-Path $stage "AppxManifest.xml"), $manifest, (New-Object System.Text.UTF8Encoding($false)))

$msix = Join-Path $Out "PulsoWin-$Version-x64.msix"
if (Test-Path $msix) { Remove-Item $msix -Force }
& $makeappx.FullName pack /d $stage /p $msix /o
if ($LASTEXITCODE -ne 0) { throw "makeappx falló (código $LASTEXITCODE)." }
Remove-Item $stage -Recurse -Force
Write-Host "Listo: $msix (sin firmar; la Store lo firma al publicarlo)"

<#
  PulsoWin · MIT · Co-designed with AI coding assistants (Claude & Gemini)
  Revisa un MSI sin instalarlo (solo lectura, sin administrador): propiedades, carpeta de destino,
  archivos, accesos directos y que no traiga servicios, tareas ni claves de inicio automático.
  Uso: .\tools\inspect-msi.ps1 artifacts\msi\PulsoWin-0.4.0-x64.msi
#>
param([Parameter(Mandatory)][string]$Path)
$ErrorActionPreference = 'Stop'
$full = (Resolve-Path $Path).Path
$installer = New-Object -ComObject WindowsInstaller.Installer
$db = $installer.GetType().InvokeMember('OpenDatabase', 'InvokeMethod', $null, $installer, @($full, 0))

function Get-Rows([string]$sql) {
    $view = $db.GetType().InvokeMember('OpenView', 'InvokeMethod', $null, $db, @($sql))
    $view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, $null) | Out-Null
    $rows = @()
    while ($true) {
        $rec = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null)
        if ($null -eq $rec) { break }
        $count = $rec.GetType().InvokeMember('FieldCount', 'GetProperty', $null, $rec, $null)
        $fields = @()
        for ($i = 1; $i -le [int]$count; $i++) {
            $fields += $rec.GetType().InvokeMember('StringData', 'GetProperty', $null, $rec, [object[]]@([int]$i))
        }
        $rows += ,$fields
    }
    $view.GetType().InvokeMember('Close', 'InvokeMethod', $null, $view, $null) | Out-Null
    return ,$rows
}

function Test-Table([string]$name) {
    (Get-Rows "SELECT ``Name`` FROM ``_Tables`` WHERE ``Name`` = '$name'").Count -gt 0
}

foreach ($p in 'ProductName', 'Manufacturer', 'ProductVersion', 'UpgradeCode', 'ALLUSERS', 'ProductLanguage') {
    $v = Get-Rows "SELECT ``Value`` FROM ``Property`` WHERE ``Property`` = '$p'"
    "{0,-16} {1}" -f $p, ($(if ($v.Count) { $v[0][0] } else { '(no definido)' }))
}
"{0,-16} {1}" -f 'Archivos', (Get-Rows 'SELECT `File` FROM `File`').Count
$files = Get-Rows 'SELECT `FileName` FROM `File`'
$exe = @($files | Where-Object { ($_[0] -split '\|')[-1] -eq 'PulsoWin.exe' })
"{0,-16} {1}" -f 'PulsoWin.exe', $(if ($exe.Count) { 'incluido' } else { 'FALTA' })
foreach ($s in (Get-Rows 'SELECT `Name`, `Target` FROM `Shortcut`')) { "{0,-16} {1} -> {2}" -f 'Acceso directo', $s[0], $s[1] }
foreach ($t in 'ServiceInstall', 'ServiceControl') {
    "{0,-16} {1}" -f $t, $(if ((Test-Table $t) -and (Get-Rows "SELECT * FROM ``$t``").Count) { 'PRESENTE (revisar)' } else { 'ninguno' })
}
$regs = Get-Rows 'SELECT `Key` FROM `Registry`'
$run = @($regs | Where-Object { $_[0] -match 'CurrentVersion\\Run' })
"{0,-16} {1}" -f 'Inicio automático', $(if ($run.Count) { 'PRESENTE (revisar)' } else { 'ninguno' })

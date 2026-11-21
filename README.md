# PulsoWin

Limpiador (tipo CCleaner) y visor de hardware (tipo Speccy) en una sola app para Windows 10/11.
Diseño propio: no reutiliza código, reglas ni recursos de CCleaner o Speccy.

Estado: **MVP inicial**. Compila sin advertencias con el SDK 10.0.404 y pasan las 35 pruebas del motor. Falta probarlo en hardware real (con y sin GPU NVIDIA, NVMe y SATA).

## Compilar y probar

Requisito: [SDK de .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0) (LTS). .NET 8 dejó de tener soporte en noviembre de 2026.

```powershell
dotnet test
dotnet run --project src/PulsoWin.App
```

## Qué hace hoy

**Limpiar** (sin administrador):
- Temporales del usuario (solo archivos con más de 24 h), caché de miniaturas, volcados de errores, papelera.
- Caché de Chrome, Edge y Firefox, en **todos** los perfiles. Si el navegador está abierto, la regla se bloquea y avisa.
- Cookies con lista de dominios a conservar (`google.com` conserva también `mail.google.com`). Solo con el navegador cerrado y siempre con copia de respaldo previa en `%LOCALAPPDATA%\PulsoWin\respaldos-cookies`.
- Flujo en tres fases: **analizar** (solo lectura) → **confirmar** (muestra archivos y tamaño) → **borrar**. Cada limpieza deja un registro en `%LOCALAPPDATA%\PulsoWin\logs`.
- **Antes / después**: espacio libre, RAM en uso y temperatura máxima leída.

**Hardware** (sin administrador ni driver):
- Resumen: SO, CPU, RAM por módulo, placa, BIOS, GPU y discos (WMI).
- Sensores en vivo cada 2 s: uso de CPU y RAM, GPU NVIDIA (temperatura, ventilador en %, uso) vía NVML, salud y temperatura de discos (API de almacenamiento de Windows), zonas térmicas ACPI.
- Lo que el equipo no expone aparece como **«no disponible»**, nunca como cero.

**Actualizar** (`winget` + Windows Update en modo `asInvoker`):
- Flujo en tres fases igual que Limpiar: **buscar** (solo lectura) → **confirmar** (selección explícita de paquetes) → **actualizar** (`winget upgrade --id <id> --exact --silent` con validación estricta de ID y sin shell).
- Consulta de actualizaciones pendientes de Windows vía Windows Update Agent COM (`Microsoft.Update.Session`, solo lectura sin elevación) y acceso directo a `ms-settings:windowsupdate` para instalar parches del sistema.
- Cada lote de actualización deja registro en `%LOCALAPPDATA%\PulsoWin\logs`.

## Decisiones de diseño

| Decisión | Por qué |
|---|---|
| La app corre como usuario (`asInvoker`) | Patrón recomendado por Microsoft; la elevación irá en un proceso aparte y acotado. |
| Sin driver de kernel en el MVP | WinRing0 está en la lista de drivers vulnerables (Defender lo detecta, CVE-2020-14979) y Windows 11 la aplica por defecto. |
| Cookies: borrar filas por dominio, sin descifrar | El dominio se guarda en claro (`host_key` en Chromium, `host` en Firefox); el cifrado app-bound de Chrome no impide borrar. |
| No escribir en la base de un navegador abierto | Evita corrupción y bloqueos de SQLite. |
| Reglas declarativas en JSON propio (`rules.json`) | Añadir limpiezas sin tocar código; validación que rechaza rutas absolutas, `..` y patrones con rutas. |
| Barreras de seguridad en el borrado | Nunca usa como raíz la unidad, el perfil, Windows, Program Files, AppData, Documentos ni el Escritorio; no sigue enlaces ni uniones; vuelve a comprobar cada archivo antes de borrar. |
| Fuera de alcance | Limpiador de registro y «optimizador de RAM»: no hay evidencia independiente de beneficio y sí de riesgo. |
| DLL nativas solo desde System32 | Evita secuestro de DLL (`nvml.dll`, `shell32`, `kernel32`). |

## Estructura

```
src/PulsoWin.Core     motor: reglas, escaneo, borrado, cookies, sensores, foto antes/después
src/PulsoWin.App      interfaz WPF (pestañas Limpiar y Hardware)
tests/PulsoWin.Tests  pruebas xUnit del motor (sin tocar archivos reales del usuario)
```

## Próximos pasos

1. Probar en equipos reales (con y sin GPU NVIDIA, NVMe y SATA).
2. GPU AMD con ADLX.
3. Proceso elevado acotado para caché de Windows Update (detener `bits`, `wuauserv` y `cryptsvc`) y `cleanmgr /sagerun`.
4. Extensión con `chrome.browsingData` para limpiar con el navegador abierto (desde Chrome 136 el DevTools Protocol ya no sirve sobre el perfil normal).
5. Sensores de placa como módulo opcional (LibreHardwareMonitorLib con PawnIO), solo tras probarlo con Memory Integrity y la lista de bloqueo activas.
6. Distribución: firma de código y build protegido. En 2017 CCleaner se distribuyó con malware y firma válida porque comprometieron su entorno de build.

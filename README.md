<div align="center">

# 🎮 PulsoWin
### *Limpiador seguro, visor de hardware en vivo y gestor unificado de actualizaciones para Windows 10/11*

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET 10.0 LTS](https://img.shields.io/badge/.NET-10.0%20LTS-512BD4.svg?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![WPF · MVVM](https://img.shields.io/badge/UI-WPF%20%C2%B7%20MVVM%20Ne%C3%B3n%20Retro-00E5FF.svg)](#-arquitectura-y-flujos-de-ejecución)
[![Tests 168/168](https://img.shields.io/badge/Tests-168%2F168%20Passing-brightgreen.svg)](#-compilar-probar-y-verificación)
[![Zero Kernel Drivers](https://img.shields.io/badge/Drivers-Zero%20Kernel%20Drivers-success.svg)](#-decisiones-de-diseño)
[![Deterministic & AOT-Ready](https://img.shields.io/badge/Build-Deterministic%20%C2%B7%20AOT--Safe-orange.svg)](#-estándares-de-ingeniería-y-trazabilidad)

<p align="center">
  <b>Gratis, transparente y de código abierto (MIT).</b><br>
  <i>Muestra exactamente qué va a tocar antes de ejecutar, corre sin privilegios por defecto, no toca el registro y no instala drivers de kernel vulnerables.</i>
</p>

[Qué hace hoy](#-qué-hace-hoy) • [Arquitectura y Flujos](#-arquitectura-y-flujos-de-ejecución) • [Estándares y Trazabilidad](#-estándares-de-ingeniería-y-trazabilidad) • [Decisiones de Diseño](#-decisiones-de-diseño) • [Compilar y Probar](#-compilar-probar-y-verificación) • [Créditos](#-créditos-y-desarrollo)

</div>

---

## 🎯 Qué es PulsoWin (y qué NO hace a propósito)

Limpiador y visor de hardware en una sola app para Windows 10/11, pensado para quien quiere mantener su PC (por ejemplo, para jugar) sin instalar herramientas que prometen milagros ni cargar servicios opacos en segundo plano.

- **Estado actual:** **`v0.4.0`**. Compila sin advertencias con el SDK de .NET 10 y pasan las **168 pruebas** (motor y ViewModels). Probado en un equipo (Intel + NVIDIA Quadro + NVMe/SATA), en sesión normal y elevada; se agradecen reportes de otros equipos.

> 🔐 **PulsoWin abre siempre sin administrador.** La red se ve **por interfaz** (Ethernet, Wi-Fi…).
> Para ver el consumo exacto **por proceso** (PID, programa, descarga, subida y total de la sesión), pulsa
> **«🔐 Ver por proceso»** en la pestaña **RED**. Windows pide permiso de administrador **solo para un proceso auxiliar**
> que mide la red con ETW y le pasa las cifras a la app. PulsoWin sigue sin administrador.
- **Qué no hace (a propósito):** no promete más FPS, no «optimiza» la RAM y no limpia el registro. Borrar cachés libera espacio, pero no acelera un PC sano; la caché de shaders, de hecho, conviene dejarla (viene desactivada por defecto).

---

## ⚙️ Qué hace hoy

### 1. Limpiar (sin administrador)
- **Temporales y sistema:** archivos temporales del usuario (solo con más de 24 h), caché de miniaturas, volcados de errores y papelera de reciclaje.
- **Navegadores (multi-perfil):** caché de Chrome, Edge y Firefox en **todos** los perfiles. Si el navegador está abierto, la regla se bloquea y avisa sin tocar archivos.
- **Cookies selectivas con respaldo:** conservan los dominios configurados (`google.com` conserva también `mail.google.com`). Solo opera con el navegador cerrado y siempre guarda copia previa en `%LOCALAPPDATA%\PulsoWin\respaldos-cookies`.
- **Flujo estricto en tres fases:** **Analizar** (solo lectura) → **Confirmar** (muestra archivos y tamaño exacto) → **Borrar**. Cada lote deja bitácora en `%LOCALAPPDATA%\PulsoWin\logs`.
- **Métrica Antes / Después:** compara espacio libre en disco, % de RAM en uso y temperatura máxima leída.

### 2. HUD Neón Retro (cabecera siempre visible)
- **Telemetría instantánea + Top 3:** uso de CPU, uso de RAM, temperatura de GPU y espacio liberable, cada uno con los 3 procesos/programas que más consumen (agrupados como en el Administrador de tareas) y las 3 categorías que más espacio liberan.
- **Botón `▶ START`:** salta a **Limpiar** e inicia el análisis; el mando retro de la cabecera se anima mientras el motor trabaja.

### 3. Hardware (sin administrador ni driver de kernel)
- **Resumen del equipo:** SO, CPU, RAM por módulo, placa base, BIOS, GPU y discos vía WMI.
- **Sensores en vivo cada 2 s:** CPU y RAM (`SystemLoadSource`), GPU NVIDIA (temperatura, ventilador en % y uso vía `System32\nvml.dll`), salud y temperatura de discos (API de almacenamiento de Windows) y zonas térmicas ACPI.
- **Honestidad de telemetría:** lo que el hardware o Windows no expone aparece como **«no disponible»**, nunca inventado ni en cero.

### 4. Actualizar (`winget` + Windows Update dentro de PulsoWin)
- **Buscar (solo lectura):** consulta `winget` y Windows Update Agent (COM) en segundo plano y consolida todo lo pendiente en **una sola lista** con tamaño honesto (lee `MinDownloadSize` y `MaxDownloadSize`).
- **Proceder:** nada viene marcado por defecto (selección explícita o casilla de tres estados en cabecera).
  - Programas: `winget.exe upgrade --id <id> --exact --silent` con ruta absoluta del ejecutable, validación estricta de ID y sin shell.
  - Parches de Windows: proceso auxiliar acotado (`ElevatedUpdates` / `WindowsUpdateInstaller`) que solicita **un** permiso UAC y solo acepta identificadores con formato GUID. Avisa si requiere reinicio; nunca reinicia solo.
- Cada lote de actualización queda auditado en `%LOCALAPPDATA%\PulsoWin\logs`.

### 5. Red (tráfico en vivo, Top 10) — por proceso, con un auxiliar elevado
- **«Aquí te muestro tu tráfico de red actual (los primeros 10)»:** se refresca **cada 1 s**, ordenado por descarga + subida, con PID, proceso, descarga y subida (KB/s o MB/s) y total de la sesión (B/KB/MB/GB, base 1024).
- **Al abrir:** tráfico real **por interfaz** activa (sin loopback ni las copias de filtros del driver). No pide permisos.
- **«🔐 Ver por proceso»:** lanza `PulsoWin.exe --net-helper`, un proceso auxiliar elevado sin ventana.
  - Abre ETW del kernel (`TraceEvent`, sesión propia con `StopOnDispose`) y mide los bytes exactos **por proceso**.
  - Solo envía números (PID y bytes) por una tubería con nombre aleatorio, abierta solo para tu usuario y los administradores del equipo (el aviso puede aprobarse con otra cuenta de administrador). Cada punta verifica el PID de la otra. No recibe nada de la app.
  - Solo trabaja para su propio `PulsoWin.exe`, y termina al cerrar la app: no quedan sesiones del kernel huérfanas.
- **Si rechazas el aviso:** sigue la vista por interfaz y un aviso lo explica. Nunca falla por falta de permisos.
- Si abres PulsoWin como administrador por tu cuenta, mide por proceso directamente, sin auxiliar.

### 6. Modo Gadget (tablero flotante)
- Botón **«◔ Modo Gadget»** en la cabecera: oculta la app completa y muestra una ventana flotante **sin detener el monitoreo**.
- **5 diales con aguja animada** (suavizada, se refrescan cada 1 s): **Red** (velocidad con escala automática + total de la sesión), **CPU**, **RAM** (con `X.X / Y.Y GB`), **GPU** (uso) y **Temp GPU** (°C). Arco verde → amarillo (&gt;70) → rojo (&gt;85).
- Sin bordes, translúcida, fuera de la barra de tareas, arrastrable, **siempre visible** (📌) e ícono en la bandeja del sistema. Doble clic o **⤢** vuelve a la app completa.
- **Sin drivers de kernel ni temperatura de CPU** (no choca con antitrampas como Vanguard): CPU con `GetSystemTimes`, RAM con `GlobalMemoryStatusEx`, GPU con NVML (driver normal de NVIDIA) o los contadores de Windows.

### 7. Idiomas: español e inglés 🌐
- Al abrir, PulsoWin usa el **idioma de Windows**: español si Windows está en español; inglés en cualquier otro caso.
- El botón **«🌐 EN» / «🌐 ES»** de la cabecera cambia al otro idioma y ofrece reiniciar la app para aplicarlo. La elección se recuerda.
- **¿Quieres sumar tu idioma?** Copia `src/PulsoWin.Core/Localization/Texts.en.resx` como `Texts.xx.resx` (por ejemplo `Texts.pt.resx`), traduce los valores sin tocar las claves ni los marcadores `{0}`, y agrega el código a `L.Supported`. Las pruebas avisan si falta un texto o cambia un marcador.

---

## 🧰 Drivers y BIOS: usa siempre los sitios oficiales de tu marca

PulsoWin **no instala drivers ni actualiza la BIOS**. Para eso, te aconsejamos usar únicamente el soporte oficial del
fabricante de tu equipo: ahí identificas tu modelo y descargas drivers, BIOS y herramientas verificados.

| Marca | Soporte oficial |
|---|---|
| **Dell** | [dell.com/support/home/es-cl](https://www.dell.com/support/home/es-cl) |
| **Lenovo** | [pcsupport.lenovo.com/es/es](https://pcsupport.lenovo.com/es/es) |
| **HP** | [HP Support Assistant](https://support.hp.com/cl-es/help/hp-support-assistant) |
| **ASUS** | [asus.com/latin/support/download-center](https://www.asus.com/latin/support/download-center/) |

> ⚠️ Evita los sitios de «descarga de drivers» de terceros y los «actualizadores automáticos» que no sean del fabricante:
> son una vía habitual de malware y de drivers incorrectos. Ante la duda, entra a la web de tu marca escribiendo la
> dirección tú mismo.

---

## 🏛️ Arquitectura y Flujos de Ejecución

PulsoWin separa de forma estricta **motor determinista** (`PulsoWin.Core`), **lógica de pantalla testeable** (`PulsoWin.Presentation`) y **capa visual** (`PulsoWin.App`), manteniendo un flujo de dependencias unidireccional (`Core → Presentation → App`).

```mermaid
flowchart TD
    U["Usuario · Interfaz WPF (PulsoWin.App · Tema NeonRetro)"] -->|"Comandos y Binding MVVM"| VM["ViewModels y Contratos (PulsoWin.Presentation)"]

    subgraph CAPA_PRES ["Capa de Presentación (Sin dependencia WPF)"]
        VM --> VM1["CleanViewModel (3 Fases)"]
        VM --> VM2["HardwareViewModel (Polling 2 s)"]
        VM --> VM3["UpdatesViewModel (Lista Unificada)"]
    end

    VM1 --> L1
    VM2 --> H1
    VM3 --> P1

    subgraph LIMPIEZA ["Pipeline 1: Motor de Limpieza (Solo Lectura -> Confirmar -> Borrar)"]
        L1["RuleCatalog (rules.json AOT-Safe) + CookieStores"] -->|"Solo lectura"| L2["Scanner + CookieCleaner.Plan + RecycleBin"]
        L2 -->|"Confirmación explícita + KeepList"| L3["Cleaner (Barreras Root/Symlink) + Respaldo Cookies + CleanLog"]
    end

    subgraph HARDWARE ["Pipeline 2: Telemetría y HUD (asInvoker, sin driver)"]
        H1["SystemSummary (WMI) + Snapshot Antes/Después"] --> H2["SystemLoad + NvmlSource (System32) + DiskHealth + AcpiThermal"]
        H2 --> H3["ProcessUsageSampler (Top 3 CPU/RAM por aplicación)"]
    end

    subgraph UPDATES ["Pipeline 3: Actualizaciones (winget + Windows Update)"]
        P1["WingetClient (ruta absoluta, ArgumentList, sin shell)"] --> P3["Lista Unificada (Desmarcada por defecto)"]
        P2["WindowsUpdateClient (COM Solo Lectura)"] --> P3
        P3 -->|"Selección explícita"| P4["winget upgrade --id --exact --silent"]
        P3 -->|"Proceso auxiliar UAC por GUID"| P5["WindowsUpdateInstaller + CleanLog"]
    end
```

---

## 📐 Estándares de Ingeniería y Trazabilidad

| # / Patrón o Estándar | Descripción Técnica | Archivos Clave | Commit |
| :--- | :--- | :--- | :--- |
| **1. Arquitectura Orientada a Dominios y Flujo Unidireccional (`Core → Presentation → App`)** | Adaptación a .NET MVVM del patrón `alan2207/bulletproof-react`: separación por dominios (`Cleaning`, `Sensors`, `Updates`, `Common`) con flujo unidireccional hacia los ViewModels, permitiendo testear pantallas sin instanciar WPF. | `src/PulsoWin.Core/*`<br>`src/PulsoWin.Presentation/ViewModels/*`<br>`src/PulsoWin.App/*` | [`45d4b2e`](https://github.com/Leonthelee/PulsoWin/commit/45d4b2e) |
| **2. Estándar Frontend Web / Cockpit (`alan2207/bulletproof-react` + `vitejs/vite-plugin-react`)** | Contrato de referencia para futura interfaz web/cockpit: arquitectura por dominios (`shared → features → app` en `frontend/src/features/`), `App.tsx` / `provider.tsx` / `dashboard.tsx`, `api-client.ts` centralizado, `GlobalErrorBoundary` y toolchain `@vitejs/plugin-react` (JSX Runtime automático, Fast Refresh HMR `:5173`, alias `@/*` → `./src/*`, proxy `/api` → `:8090`, build `frontend/dist`). | Estándar web del ecosistema (`df899e6`: `App.tsx`, `provider.tsx`, `api-client.ts`, `vite.config.ts`, `package.json`, `tsconfig.json`) | [`45d4b2e`](https://github.com/Leonthelee/PulsoWin/commit/45d4b2e) *(equivalente MVVM)* |
| **3. Motor de Limpieza en 3 Fases y Barreras Anti-Borrado** | Escaneo en solo lectura, bloqueo automático si el navegador está abierto, rechazo de raíces críticas (`C:\`, perfil, Windows, Program Files, AppData, Documentos, Escritorio), ignorar `ReparsePoint`/symlinks, re-validación por archivo y respaldo SQLite de cookies antes de borrar filas por dominio. | `src/PulsoWin.Core/Cleaning/Scanner.cs`<br>`src/PulsoWin.Core/Cleaning/Cleaner.cs`<br>`src/PulsoWin.Core/Cleaning/CookieCleaner.cs`<br>`src/PulsoWin.Core/Cleaning/RuleRoots.cs` | [`057c1a9`](https://github.com/Leonthelee/PulsoWin/commit/057c1a9) |
| **4. Catálogo Declarativo AOT-Safe (`System.Text.Json` Source Generator)** | Reglas declarativas en JSON embebido deserializadas con `JsonSerializerContext` (cero reflexión en runtime, compatible con `PublishAot` y trimming) y validación contra `..` o rutas absolutas. | `src/PulsoWin.Core/Cleaning/RuleCatalog.cs`<br>`src/PulsoWin.Core/Cleaning/CleanRule.cs`<br>`src/PulsoWin.Core/Cleaning/rules.json` | [`ea77ad3`](https://github.com/Leonthelee/PulsoWin/commit/ea77ad3) |
| **5. Actualizador Unificado Seguro (`winget` + Windows Update COM)** | `ProcessRunner` con `UseShellExecute = false` y `ArgumentList` (cero inyección de shell), resolución segura de `winget.exe`, regex estricto de ID, consulta WUA en solo lectura y auxiliar elevado acotado exclusivamente a GUIDs. | `src/PulsoWin.Core/Updates/ProcessRunner.cs`<br>`src/PulsoWin.Core/Updates/WingetClient.cs`<br>`src/PulsoWin.Core/Updates/WindowsUpdateClient.cs`<br>`src/PulsoWin.Core/Updates/WindowsUpdateInstaller.cs`<br>`src/PulsoWin.App/ElevatedUpdates.cs` | [`a746c69`](https://github.com/Leonthelee/PulsoWin/commit/a746c69)<br>[`630017d`](https://github.com/Leonthelee/PulsoWin/commit/630017d)<br>[`c9a862c`](https://github.com/Leonthelee/PulsoWin/commit/c9a862c) |
| **6. HUD Neón Retro, Ícono Reproducible y Top 3 por Proceso** | Tema visual WPF propio sin librerías de terceros, cabecera estable sin saltos de layout, ícono multi-resolución generado por código (`tools/icon.cs`) y muestreo diferencial de CPU/RAM agrupado por aplicación. | `src/PulsoWin.App/Themes/NeonRetro.xaml`<br>`src/PulsoWin.App/Controls/RetroPadHero.xaml`<br>`src/PulsoWin.Core/Sensors/ProcessUsageSampler.cs`<br>`tools/icon.cs` | [`45d4b2e`](https://github.com/Leonthelee/PulsoWin/commit/45d4b2e)<br>[`c9a862c`](https://github.com/Leonthelee/PulsoWin/commit/c9a862c)<br>[`2d5813b`](https://github.com/Leonthelee/PulsoWin/commit/2d5813b)<br>[`4cc6d0d`](https://github.com/Leonthelee/PulsoWin/commit/4cc6d0d) |
| **7. Build Determinista, Higiene de Binarios (`REA`) y Firma de Autoría** | Compilación reproducible con `Deterministic=true`, `PathMap`, supresión de rutas locales del `.pdb` en el encabezado PE CodeView (`RSDS`) en `Release` (`ContinuousIntegrationBuild=true`, `DebugType=none`) e incrustación de firma de autoría en `AssemblyMetadata` (`Author`, `Profile`, `HelloDev`), auditado con REA (`0` fugas locales). | `Directory.Build.props`<br>`global.json` | [`88eb5b8`](https://github.com/Leonthelee/PulsoWin/commit/88eb5b8)<br>[`857b739`](https://github.com/Leonthelee/PulsoWin/commit/857b739) |

---

## 🛡️ Decisiones de diseño

| Decisión | Por qué |
|---|---|
| Nunca se eleva la app entera | Manifiesto `asInvoker` (requisito 9.2 de Windows). Lo que necesita administrador va en procesos auxiliares acotados, que se piden solo al usar esa función: instalar actualizaciones de Windows y medir la red por proceso. |
| DPI declarado en el manifiesto | `PerMonitorV2` (requisito 1.3): nítido en pantallas con escalado distinto, sin llamar a `SetProcessDPIAware`. |
| Front y back separados (MVVM) | `Core` (lógica) ← `Presentation` (estado y comandos, testeable sin ventanas) ← `App` (vistas WPF). |
| Sin driver de kernel en el MVP | WinRing0 está en la lista de drivers vulnerables (Defender lo detecta, CVE-2020-14979) y Windows 11 la aplica por defecto. |
| Cookies: borrar filas por dominio, sin descifrar | El dominio se guarda en claro (`host_key` en Chromium, `host` en Firefox); el cifrado app-bound de Chrome no impide borrar. |
| No escribir en la base de un navegador abierto | Evita corrupción y bloqueos de SQLite. |
| Reglas declarativas en JSON propio (`rules.json`) | Añadir limpiezas sin tocar código; validación que rechaza rutas absolutas, `..` y patrones con rutas. |
| Barreras de seguridad en el borrado | Nunca usa como raíz la unidad, el perfil, Windows, Program Files, AppData, Documentos ni el Escritorio; no sigue enlaces ni uniones; vuelve a comprobar cada archivo antes de borrar. |
| Fuera de alcance | Limpiador de registro y «optimizador de RAM»: no hay evidencia independiente de beneficio y sí de riesgo. |
| DLL nativas solo desde System32 | Evita secuestro de DLL (`nvml.dll`, `shell32`, `kernel32`). |

---

## 🗂️ Estructura

```text
PulsoWin.slnx
Directory.Build.props             versión, builds reproducibles, strip PDB en Release y firma AssemblyMetadata
global.json                       SDK .NET 10.0.100 (rollForward: latestFeature)
src/
├── PulsoWin.Core/                motor: Cleaning, Sensors, Updates, Common, Localization (textos es/en; 0 dependencia de UI)
├── PulsoWin.Presentation/        ViewModels MVVM y servicios desacoplados de WPF
└── PulsoWin.App/                 front WPF, tema Neón retro, Assets/pulsowin.ico y auxiliar elevado
tests/
└── PulsoWin.Tests/               suite xUnit (168 pruebas: motor, ViewModels e idiomas)
tools/
└── icon.cs                       generador reproducible del ícono multi-resolución
installer/
├── PulsoWin.Installer.wixproj    instalador MSI (WiX Toolset 5, MS-RL)
└── Package.wxs                   Program Files, acceso en el menú Inicio y actualización mayor
```

---

## 🧪 Compilar, Probar y Verificación

Requisito: [SDK de .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0) (LTS). .NET 8 dejó de tener soporte en noviembre de 2026.

```powershell
# 1. Ejecutar toda la suite de pruebas (168/168 en verde)
dotnet test --nologo

# 2. Compilar en Release tratando advertencias como errores (sin rutas locales en RSDS)
dotnet build -c Release --no-incremental -warnaserror --nologo

# 3. Ejecutar la aplicación de escritorio
dotnet run --project src/PulsoWin.App

# 4. (Opcional) Publicar y generar el instalador MSI
dotnet publish src/PulsoWin.App -c Release -r win-x64 --self-contained -p:DebugType=none -o artifacts\publish\win-x64
dotnet build installer\PulsoWin.Installer.wixproj -c Release -p:Version=0.4.0 "-p:PublishDir=$PWD\artifacts\publish\win-x64" -o artifacts\msi
```

---

## 🚀 Próximos pasos

1. Probar en equipos reales (con y sin GPU NVIDIA, NVMe y SATA).
2. GPU AMD con ADLX.
3. Proceso elevado acotado para caché de Windows Update (detener `bits`, `wuauserv` y `cryptsvc`) y `cleanmgr /sagerun`.
4. Extensión con `chrome.browsingData` para limpiar con el navegador abierto (desde Chrome 136 el DevTools Protocol ya no sirve sobre el perfil normal).
5. Sensores de placa como módulo opcional (LibreHardwareMonitorLib con PawnIO), solo tras probarlo con Memory Integrity y la lista de bloqueo activas.
6. Distribución: firma de código (Authenticode) y build protegido. En 2017 CCleaner se distribuyó con malware y firma válida porque comprometieron su entorno de build.

---

<a id="code-signing-policy"></a>
## 🔏 Firma de código

> **PulsoWin no tiene firma digital.** Ninguna versión publicada está firmada y, por ahora, no hay un certificado de firma de código en camino.

Qué significa al descargarlo:
- **«Windows protegió su PC»:** pulsa **Más información → Ejecutar de todas formas**.
- **Smart App Control activado:** Windows puede bloquear la app sin ofrecer esa opción (error `0x800711C7`). En ese caso, compílala tú mismo con los comandos de arriba.
- **Cómo saber que el archivo es el original:**
  - Cada versión se compila en GitHub Actions desde este repositorio ([workflow](.github/workflows/release.yml)), no en un equipo personal.
  - Cada archivo publicado trae su `.sha256`: compara el resultado de `Get-FileHash <archivo> -Algorithm SHA256` con ese valor.
- **Revisión:** todo cambio propuesto por otra persona (pull request) lo revisa [@Leonthelee](https://github.com/Leonthelee) antes de integrarse.

<a id="privacidad"></a>
## 🔒 Privacidad

This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it.

PulsoWin **no tiene telemetría** ni envía datos de uso, del equipo o de la red a ningún servidor. Solo se conecta a internet cuando tú lo pides:
- **Buscar o instalar actualizaciones:** usa `winget` y el Agente de Windows Update, que hablan con los servidores de Microsoft según la política de privacidad de Microsoft.
- **Perfil del autor en el huevo de pascua:** solo se abre si haces clic en él.

El monitor de red y el visor de hardware leen datos del propio equipo y no los guardan ni los envían. PulsoWin solo guarda localmente cuántas veces se abrió (un número en `%LOCALAPPDATA%\PulsoWin\usos.txt`), para su huevo de pascua, y el idioma elegido (`idioma.txt`, en la misma carpeta).

---

## 👥 Créditos y desarrollo

- **Autoría y dirección:** Daniel Elías Díaz Lepe ([@Leonthelee](https://github.com/Leonthelee))
- **Co-desarrollo y revisión cruzada:** Claude (Opus 5.5 · Anthropic) y Antigravity Orquestador (Gemini 4 · Google)

> *Co-designed with AI coding assistants (Claude & Gemini).* Ver [AUTHORS.md](AUTHORS.md).

---

## 📄 Licencia y avisos

[MIT](LICENSE). Puedes usarlo, modificarlo y redistribuirlo libremente, sin garantía de ningún tipo: revisa qué vas a borrar antes de confirmar.

PulsoWin es un proyecto independiente, escrito desde cero. No está afiliado, patrocinado ni respaldado por Gen Digital, Piriform, CCleaner ni Speccy, que son marcas de sus respectivos dueños y se mencionan solo como referencia.

Dependencias: `CommunityToolkit.Mvvm`, `Microsoft.Diagnostics.Tracing.TraceEvent`, `Microsoft.Data.Sqlite`, `System.Diagnostics.PerformanceCounter` y `System.Management` (MIT), `SQLitePCLRaw` (Apache-2.0); pruebas con `xUnit` (Apache-2.0); instalador con WiX Toolset 5 (MS-RL).

Historial de versiones y cómo volver atrás: [CHANGELOG.md](CHANGELOG.md).

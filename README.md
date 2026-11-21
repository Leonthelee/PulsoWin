# PulsoWin

Limpiador y visor de hardware en una sola app para Windows 10/11, **gratis y de código abierto**.
Pensado para quien quiere mantener su PC (por ejemplo, para jugar) sin instalar herramientas que prometen milagros:
muestra qué va a borrar antes de hacerlo, no toca el registro y no usa drivers de kernel.

Estado: **MVP inicial**. Compila sin advertencias con el SDK de .NET 10 y pasan las 76 pruebas (motor y pantallas). Probado en un solo equipo (Intel + NVIDIA Quadro + NVMe/SATA); se agradecen reportes de otros equipos.

> **Qué no hace (a propósito):** no promete más FPS, no «optimiza» la RAM y no limpia el registro. Borrar cachés libera espacio, pero no acelera un PC sano; la caché de shaders, de hecho, conviene dejarla (viene desactivada).

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

**HUD** (cabecera, siempre visible):
- CPU, RAM, temperatura de GPU y espacio liberable, cada uno con su **top 3**: los programas que más consumen
  (agrupando sus procesos, como el Administrador de tareas) y las categorías que más liberan.
- Botón **▶ START**: va a Limpiar y analiza. El mando de la cabecera se anima mientras la app trabaja.

**Hardware** (sin administrador ni driver):
- Resumen: SO, CPU, RAM por módulo, placa, BIOS, GPU y discos (WMI).
- Sensores en vivo cada 2 s: uso de CPU y RAM, GPU NVIDIA (temperatura, ventilador en %, uso) vía NVML, salud y temperatura de discos (API de almacenamiento de Windows), zonas térmicas ACPI.
- Lo que el equipo no expone aparece como **«no disponible»**, nunca como cero.

**Actualizar** (todo dentro de PulsoWin):
- **Buscar** consulta winget y Windows Update en segundo plano (solo lectura) y muestra todo lo pendiente en **una sola lista**.
- **Proceder** instala lo marcado: programas con `winget upgrade --id <id> --exact --silent` (ID validado, sin shell);
  actualizaciones de Windows con un proceso auxiliar que pide **un** permiso de administrador (UAC) y solo acepta
  identificadores con formato GUID. Avisa si hay que reiniciar; nunca reinicia solo.
- Nada viene marcado: cada actualización se elige a propósito (o con la casilla «seleccionar todo»).
- Cada lote deja registro en `%LOCALAPPDATA%\PulsoWin\logs`.

## Decisiones de diseño

| Decisión | Por qué |
|---|---|
| La app corre como usuario (`asInvoker`) | Patrón recomendado por Microsoft; lo único que necesita administrador (instalar Windows Update) va en un proceso auxiliar aparte y acotado. |
| Front y back separados (MVVM) | `Core` (lógica) ← `Presentation` (estado y comandos, testeable sin ventanas) ← `App` (vistas WPF). |
| Sin driver de kernel en el MVP | WinRing0 está en la lista de drivers vulnerables (Defender lo detecta, CVE-2020-14979) y Windows 11 la aplica por defecto. |
| Cookies: borrar filas por dominio, sin descifrar | El dominio se guarda en claro (`host_key` en Chromium, `host` en Firefox); el cifrado app-bound de Chrome no impide borrar. |
| No escribir en la base de un navegador abierto | Evita corrupción y bloqueos de SQLite. |
| Reglas declarativas en JSON propio (`rules.json`) | Añadir limpiezas sin tocar código; validación que rechaza rutas absolutas, `..` y patrones con rutas. |
| Barreras de seguridad en el borrado | Nunca usa como raíz la unidad, el perfil, Windows, Program Files, AppData, Documentos ni el Escritorio; no sigue enlaces ni uniones; vuelve a comprobar cada archivo antes de borrar. |
| Fuera de alcance | Limpiador de registro y «optimizador de RAM»: no hay evidencia independiente de beneficio y sí de riesgo. |
| DLL nativas solo desde System32 | Evita secuestro de DLL (`nvml.dll`, `shell32`, `kernel32`). |

## Estructura

```
src/PulsoWin.Core          back: reglas, escaneo, borrado, cookies, sensores, top de consumo, winget y Windows Update
src/PulsoWin.Presentation  ViewModels (MVVM): estado de pantalla y comandos, sin WPF
src/PulsoWin.App           front WPF: vistas, tema «Neón retro», cabecera animada y auxiliar elevado
tests/PulsoWin.Tests  pruebas xUnit del motor (sin tocar archivos reales del usuario)
```

## Próximos pasos

1. Probar en equipos reales (con y sin GPU NVIDIA, NVMe y SATA).
2. GPU AMD con ADLX.
3. Proceso elevado acotado para caché de Windows Update (detener `bits`, `wuauserv` y `cryptsvc`) y `cleanmgr /sagerun`.
4. Extensión con `chrome.browsingData` para limpiar con el navegador abierto (desde Chrome 136 el DevTools Protocol ya no sirve sobre el perfil normal).
5. Sensores de placa como módulo opcional (LibreHardwareMonitorLib con PawnIO), solo tras probarlo con Memory Integrity y la lista de bloqueo activas.
6. Distribución: firma de código y build protegido. En 2017 CCleaner se distribuyó con malware y firma válida porque comprometieron su entorno de build.

## Licencia y avisos

[MIT](LICENSE). Puedes usarlo, modificarlo y redistribuirlo libremente, sin garantía de ningún tipo: revisa qué vas a borrar antes de confirmar.

PulsoWin es un proyecto independiente, escrito desde cero. No está afiliado, patrocinado ni respaldado por Gen Digital, Piriform, CCleaner ni Speccy, que son marcas de sus respectivos dueños y se mencionan solo como referencia.

Dependencias: Microsoft.Data.Sqlite y System.Management (MIT), SQLitePCLRaw (Apache-2.0); pruebas con xUnit (Apache-2.0).

Historial de versiones y cómo volver atrás: [CHANGELOG.md](CHANGELOG.md).

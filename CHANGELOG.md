# Historial de cambios

Formato basado en [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/); versiones con [SemVer](https://semver.org/lang/es/).
Cada versión publicada tiene su etiqueta git (`vX.Y.Z`) y su release en GitHub: son los puntos de retorno.

## [Sin publicar]

### Agregado
- **Firma de co-diseño** «Co-designed with AI coding assistants (Claude & Gemini)»:
  - **oculta:** `AssemblyMetadata` «CoDesign» incrustado en `PulsoWin.exe`, `PulsoWin.Core.dll` y `PulsoWin.Presentation.dll`; el huevo de pascua la lee de ahí;
  - **visible:** encabezado en cada archivo `.cs` de `src/`, [AUTHORS.md](AUTHORS.md) y créditos del README.
- README: secciones «Code signing policy» y «Privacidad».
- **Idiomas: español e inglés.** Todos los textos de la app (vistas, mensajes, sensores, reglas de limpieza y errores
  visibles) salen de `Texts.resx` / `Texts.en.resx` en `PulsoWin.Core/Localization`. Arranca en el idioma de Windows
  (español o, si no, inglés) y el botón «🌐 EN / ES» lo cambia y reinicia la app conservando el modo administrador.
  Pruebas que exigen las mismas claves y marcadores en cada idioma.

### Cambiado
- **Huevo de pascua:** ahora solo existe cuando la app se usó **más de 20 veces**. En el uso 21 aparece solo, una vez,
  con «Gracias por el uso de la APP, espero sea de aporte y ayuda. =)»; desde ahí el código Konami lo vuelve a abrir.
  El conteo es un número local en `%LOCALAPPDATA%\PulsoWin\usos.txt` (no sale del equipo).

## [0.3.0] — 2026-11-21

> 🔐 **Para ver la red por proceso, PulsoWin debe ejecutarse como Administrador.** Desde esta versión la app
> lo pide al abrir; si se rechaza, abre igual en modo estándar (red por interfaz). `--sin-admin` se salta el aviso.

### Agregado
- **Pestaña RED:** «Aquí te muestro tu tráfico de red actual (los primeros 10)». Top 10 en vivo con PID, proceso,
  descarga, subida y total de la sesión. Con administrador: ETW por proceso (`Microsoft.Diagnostics.Tracing.TraceEvent`,
  MIT; sesión propia con `StopOnDispose`). Sin administrador: tráfico por interfaz y aviso.
- **Motor único `SystemMetricsService`:** CPU (`GetSystemTimes`), RAM (`GlobalMemoryStatusEx`), GPU (NVML o contadores
  de Windows) y red en una sola foto inmutable **cada 1 s**. HUD, pestaña RED y Modo Gadget leen del mismo motor.
- **Modo Gadget:** ventana flotante sin bordes, translúcida, arrastrable, siempre visible e ícono en la bandeja, con
  **5 diales** de aguja suavizada: Red (escala automática), CPU, RAM, GPU y Temp GPU. Ocupa un 10 % menos de pantalla.
- **Pedido de administrador al abrir** (relanzamiento elevado; si se rechaza, sigue en modo estándar).
- Huevo de pascua: el código Konami muestra la firma de autoría. Selección múltiple en las tablas (Ctrl/Shift + clic,
  barra espaciadora, clic derecho). Ícono propio de la app.
- Pruebas: 119 (de 76 en la 0.2.0).

### Cambiado
- START se desvanece en las pestañas donde no hace nada (Actualizar y Red).
- El top de GPU por programa y el uso total de GPU comparten la misma lectura de contadores (`GpuEngineCounters`).

### Seguridad
- Sin drivers de kernel ni temperatura de CPU: el monitoreo no choca con antitrampas (probado junto a Vanguard).
- La sesión ETW se cierra al salir (verificado: 1 sesión mientras mide, 0 al cerrar).

## [0.2.0] — 2026-11-21

Ver la [release v0.2.0](https://github.com/Leonthelee/PulsoWin/releases/tag/v0.2.0).

### Agregado
- Diseño «Neón retro» para jugadores: tema oscuro con acentos magenta y cian, tipografía Bahnschrift,
  tablas, pestañas, botones y barras de desplazamiento propias, y barra de título oscura.
- Cabecera animada con un mando retro original apuntando a un notebook: «PULSA START» en reposo,
  y rayo y barras de progreso animados mientras se analiza, limpia o actualiza.
  Respeta la opción de Windows «Mostrar animaciones».
- HUD en la cabecera (CPU, RAM, temperatura de GPU y espacio liberable) y botón START que lleva a Limpiar y analiza.
- Proyecto `PulsoWin.Presentation` (MVVM con CommunityToolkit.Mvvm): ViewModels testeables, sin WPF.
- 6 pruebas de ViewModels; entre ellas, que cancelar la confirmación no borra nada (61 pruebas en total).
- **Top de consumo** bajo cada marcador del HUD: los 3 programas que más usan CPU, RAM y GPU
  (agrupando sus procesos, como el Administrador de tareas, sin administrador), y las 3 categorías que más liberan.
- **Actualizar sin salir de PulsoWin:** «Buscar» revisa winget y Windows Update en segundo plano y muestra todo
  lo pendiente en una sola lista; «Proceder» instala lo marcado. Las de Windows se instalan con un proceso
  auxiliar que pide **un** permiso de administrador (UAC), acepta solo identificadores válidos (GUID) y
  escribe su resultado únicamente en la carpeta de datos de PulsoWin. Se informa si hace falta reiniciar.
- 9 pruebas más (70 en total): lista única, instala solo los UpdateID marcados, rechazo del UAC, validación de
  identificadores y medición de consumo.
- Casilla «seleccionar todo» en la cabecera de las tablas de Limpiar y Actualizar (todo / nada / algunas).
- 6 pruebas más (76 en total): tamaño de Windows Update y casilla «seleccionar todo».

### Quitado
- El botón «Abrir Windows Update» (abría Configuración de Windows): ahora todo ocurre dentro de la app.
- El botón «Marcar todo»: lo reemplaza la casilla de la cabecera.

### Cambiado
- Front y back separados en tres capas: `Core` (lógica) ← `Presentation` (estado y comandos) ← `App` (vistas).
  `MainWindow.xaml.cs` pasó de 445 líneas a ~50.

### Corregido
- La app ya no se cierra ante un error inesperado: se registra en `%LOCALAPPDATA%\PulsoWin\logs\errores.log`,
  se muestra un aviso y la ventana sigue abierta.
- La pantalla ya no salta cada 2 segundos: tarjetas del HUD de alto fijo y sensores actualizados fila por fila.
- El tamaño de las actualizaciones de Windows ya no muestra el máximo teórico de Windows Update como real
  (una acumulativa figuraba con 90,5 GB): ahora dice «variable» o «hasta X».

## [0.1.0] — 2026-11-21

Primera versión pública (preliminar): limpiador, visor de hardware, pestaña Actualizar (winget + Windows Update),
licencia MIT. Ver la [release v0.1.0](https://github.com/Leonthelee/PulsoWin/releases/tag/v0.1.0).

---

## Cómo volver atrás (rollback)

**Como usuario:** descarga una versión anterior desde
[Releases](https://github.com/Leonthelee/PulsoWin/releases); cada una es un zip independiente.

**En el código:**

```powershell
git tag                              # versiones disponibles
git switch --detach v0.1.0           # mirar o compilar una versión anterior sin tocar nada
git switch main                      # volver
git revert <commit>                  # deshacer un cambio ya publicado creando un commit inverso (seguro)
```

Las ramas `feat/*` se fusionan en `main` solo cuando compilan sin advertencias y pasan todas las pruebas,
así `main` siempre queda en un estado al que se puede volver.

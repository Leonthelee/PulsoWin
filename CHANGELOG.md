# Historial de cambios

Formato basado en [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/); versiones con [SemVer](https://semver.org/lang/es/).
Cada versión publicada tiene su etiqueta git (`vX.Y.Z`) y su release en GitHub: son los puntos de retorno.

## [Sin publicar] — rama `feat/ui-gamer`

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

### Quitado
- El botón «Abrir Windows Update» (abría Configuración de Windows): ahora todo ocurre dentro de la app.

### Cambiado
- Front y back separados en tres capas: `Core` (lógica) ← `Presentation` (estado y comandos) ← `App` (vistas).
  `MainWindow.xaml.cs` pasó de 445 líneas a ~50.

### Corregido
- La app ya no se cierra ante un error inesperado: se registra en `%LOCALAPPDATA%\PulsoWin\logs\errores.log`,
  se muestra un aviso y la ventana sigue abierta.

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

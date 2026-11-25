# Autores

**PulsoWin** — Daniel Elías Díaz Lepe ([@Leonthelee](https://github.com/Leonthelee)): autoría, dirección y decisiones de diseño.

*Co-designed with AI coding assistants (Claude & Gemini).*

| Modelo / Entorno | Proveedor | Contribución principal |
| :--- | :--- | :--- |
| **Claude Opus 5.5** *(Claude Code)* | Anthropic | Motor de limpieza, monitor de red ETW, Modo Gadget, localización `es`/`en`, MSI WiX v5, MSIX Store y pipeline `release.yml`. |
| **Gemini Pro** *(Antigravity · Planificación)* | Google DeepMind | Hardening de binarios y CI (`CVE-2026-111497`, `SECURITY.md`), diseño de `BrowserCloser` (*Startup Boost*) y `WindowSizing`, revisión cruzada. |
| **Gemini Flash** *(Antigravity · Ejecución)* | Google DeepMind | Implementación de `BrowserCloser`, `WindowSizing`, `build-msi.ps1` / `inspect-msi.ps1`, pruebas unitarias, auditoría NuGet/MSI en CI y capturas UI sintéticas. |

Cada cambio hecho con un asistente queda registrado en el historial de git con su línea `Co-Authored-By`.
La misma firma va incrustada en los binarios (`AssemblyMetadata` «CoDesign» en `Directory.Build.props`)
y se ve en el huevo de pascua de la app, que se desbloquea al pasar los 20 usos.

¿Vas a contribuir? Bienvenido: agrega tu nombre aquí en tu pull request.

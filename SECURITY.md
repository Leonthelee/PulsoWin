# Política de Seguridad · Security Policy

*Última actualización · Last updated: 2026-11-24*

[Español](#español) · [English](#english)

---

## Español

### Versiones soportadas

| Serie | Runtime .NET | Estado |
| :--- | :--- | :--- |
| `0.4.x` (`main`) | `.NET 10.0 LTS` (`>= 10.0.15` empaquetado, mitiga `CVE-2026-111497`) | Soportada |
| `< 0.4.0` | — | Sin soporte |

### Modelo de privilegios y arquitectura segura

- **Sin elevación permanente (`asInvoker`):** la aplicación principal abre siempre como usuario estándar.
- **Auxiliares UAC acotados:**
  - `--wu-install`: instala únicamente identificadores de Windows Update validados por formato `GUID` estricto.
  - `--net-helper`: ejecuta captura ETW de tráfico por proceso y transmite solo contadores numéricos por tubería local autenticada por PID y ACL de usuario/administradores. Termina al cerrar la ventana principal sin dejar sesiones huérfanas.
- **Cero drivers de kernel:** no utiliza `WinRing0` ni drivers vulnerables en bloqueos de Windows 11 (`CVE-2020-14979`).
- **Carga segura de bibliotecas nativas:** `[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]` impide secuestro de DLL (`nvml.dll`, `dwmapi.dll`, `shell32.dll`).
- **Motor de limpieza con barreras físicas:** flujos en tres fases (solo lectura → confirmación → borrado), rechazo de raíces críticas del sistema y `ReparsePoint`/symlinks, y respaldo previo SQLite al eliminar cookies por dominio sin descifrar su contenido.

### Cadena de suministro y reproducibilidad

- Compilación determinista (`Deterministic=true`, `ContinuousIntegrationBuild=true`, `DebugType=none`) sin rutas de usuario ni símbolos `.pdb` filtrados en paquetes `Release`.
- Flujo de CI/CD en GitHub Actions endurecido con acciones fijadas por hash SHA-1 completo, permisos mínimos de lectura y verificación automática de vulnerabilidades NuGet y estructura de MSI (`tools/inspect-msi.ps1`).
- Integridad verificable mediante archivos `.sha256` publicados junto a cada lanzamiento.

### Reportar una vulnerabilidad

Por favor, **no abras un issue público** para reportar fallas de seguridad.

1. Utiliza **GitHub Private Vulnerability Reporting**: [Report a vulnerability](https://github.com/Leonthelee/PulsoWin/security/advisories/new).
2. Incluye versión afectada, pasos mínimos de reproducción o prueba de concepto y el impacto observado.
3. Tiempo objetivo de acuse de recibo inicial: **72 horas**.

---

## English

### Supported Versions

| Release series | .NET Runtime | Status |
| :--- | :--- | :--- |
| `0.4.x` (`main`) | `.NET 10.0 LTS` (self-contained `>= 10.0.15`, mitigates `CVE-2026-111497`) | Supported |
| `< 0.4.0` | — | Unsupported |

### Privilege Model & Hardening

- **Standard-user execution (`asInvoker`):** the desktop UI never elevates the entire process.
- **Scoped UAC helpers:** `--wu-install` strictly validates Windows Update GUIDs; `--net-helper` isolates kernel ETW network sampling behind a PID-verified local named pipe and stops on app exit.
- **Zero kernel drivers:** does not bundle `WinRing0` (`CVE-2020-14979`) or custom kernel drivers.
- **DLL hijacking prevention:** `[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]` enforces System32 resolution for native DLLs.
- **Safe deletion guardrails:** 3-phase flow (read-only scan → confirmation → clean), critical directory/symlink rejection, and SQLite domain-only cookie deletion with pre-delete backup.

### Supply Chain & Build Hygiene

- Deterministic Release builds (`ContinuousIntegrationBuild=true`, `DebugType=none`) with zero local path or `.pdb` leaks.
- SHA-pinned GitHub Actions workflow with least-privilege permissions, NuGet vulnerability audit, `.NET` runtime floor enforcement (`>= 10.0.14`), and read-only MSI validation (`tools/inspect-msi.ps1`).

### Reporting a Vulnerability

Please **do not open a public issue** for security reports. Submit a private report via [GitHub Security Advisories](https://github.com/Leonthelee/PulsoWin/security/advisories/new) with reproduction steps and impact details.

# Política de privacidad · Privacy policy

*Última actualización · Last updated: 2026-11-22*

[Español](#español) · [English](#english)

---

## Español

PulsoWin es una aplicación de escritorio para Windows, gratuita y de código abierto (licencia MIT). Este documento
explica qué datos toca y qué hace con ellos.

### Resumen
- **No recopila ni envía datos personales.** No tiene telemetría, analítica, anuncios, cuentas ni compras.
- **Todo ocurre en tu equipo.** PulsoWin no tiene servidores propios y no envía nada a internet.

### Qué información lee, solo en tu equipo
- **Limpieza:** lee los archivos temporales y las cachés que eliges limpiar para calcular cuánto espacio liberan.
  Si eliges limpiar **cookies**, lee las bases de datos de cookies de tus navegadores para borrar las de los dominios
  que no quieres conservar. Lee solo el dominio de cada cookie, nunca descifra su contenido.
- **Monitor:** lee el uso de CPU, RAM, GPU, discos y red, y los nombres de los procesos que más consumen. Estas cifras
  solo se muestran en pantalla y no se guardan.
- **Red por proceso:** si la activas, un proceso auxiliar con permiso de administrador cuenta los bytes de red por
  proceso. Esas cifras van al programa por una conexión local del propio equipo; nunca salen del equipo.

### Qué guarda en tu equipo
En `%LOCALAPPDATA%\PulsoWin`:
- el idioma que elegiste (`idioma.txt`);
- un contador de aperturas de la app (`usos.txt`);
- registros en `logs`: errores, y qué se limpió o actualizó en cada lote. Pueden incluir rutas de archivos de tu equipo;
- una **copia de respaldo de las cookies** antes de borrarlas (`respaldos-cookies`), para que puedas recuperarlas.

Nada de eso sale de tu equipo. Puedes borrar esa carpeta cuando quieras; desinstalar la app no la borra, para no perder
el respaldo de cookies.

### Servicios de Microsoft
La pestaña de actualizaciones usa herramientas del propio Windows: **winget** (Administrador de paquetes de Windows)
y **Windows Update**. Al buscar o instalar actualizaciones, esas herramientas se conectan con los servidores de
Microsoft y de los proveedores de cada programa, según sus propias políticas de privacidad. PulsoWin no recibe ni
guarda esos datos.

### Menores de edad
PulsoWin no recopila datos de nadie, tampoco de menores de edad.

### Cambios y contacto
Los cambios a esta política quedan en el historial de este archivo en GitHub. Para preguntas, abre un *issue* en
<https://github.com/Leonthelee/PulsoWin/issues>.

---

## English

PulsoWin is a free, open-source (MIT) Windows desktop app. This document explains what data it touches and what it
does with it.

### Summary
- **It does not collect or send personal data.** No telemetry, analytics, ads, accounts or purchases.
- **Everything happens on your computer.** PulsoWin has no servers of its own and sends nothing over the internet.

### What it reads, only on your computer
- **Cleaning:** it reads the temporary files and caches you choose to clean, to work out how much space they free.
  If you choose to clean **cookies**, it reads your browsers' cookie databases to delete the cookies of domains you do
  not want to keep. It reads only each cookie's domain and never decrypts cookie contents.
- **Monitor:** it reads CPU, RAM, GPU, disk and network usage, and the names of the processes that use the most.
  These figures are only shown on screen and are not stored.
- **Per-process network:** if you turn it on, a helper process with administrator rights counts network bytes per
  process. Those figures reach the app over a local connection on the same computer and never leave it.

### What it stores on your computer
In `%LOCALAPPDATA%\PulsoWin`:
- the language you chose (`idioma.txt`);
- a count of app launches (`usos.txt`);
- logs in `logs`: errors, and what was cleaned or updated in each batch. They may include file paths on your computer;
- a **backup copy of your cookies** before they are deleted (`respaldos-cookies`), so you can restore them.

None of it leaves your computer. You can delete that folder at any time; uninstalling the app does not delete it, so
the cookie backup is not lost.

### Microsoft services
The updates tab uses tools built into Windows: **winget** (Windows Package Manager) and **Windows Update**. When
they look for or install updates, those tools connect to the servers of Microsoft and of each program's publisher,
under their own privacy policies. PulsoWin does not receive or store that data.

### Children
PulsoWin collects no data from anyone, including children.

### Changes and contact
Changes to this policy are kept in this file's history on GitHub. For questions, open an issue at
<https://github.com/Leonthelee/PulsoWin/issues>.

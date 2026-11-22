# Publicar PulsoWin en la Microsoft Store

En la Store, la firma la pone Microsoft: el paquete MSIX se sube **sin firmar** y la Store lo firma con su
certificado al publicarlo. Así Windows (y Smart App Control) confían en la app sin que el proyecto compre
un certificado.

> El MSI y el zip de GitHub siguen igual (sin firma). El MSIX solo sirve para la Store: fuera de ella
> Windows no instala un paquete sin firmar.

## 1. Cuenta y nombre (se hace una sola vez, en el navegador)

1. Crear la cuenta de desarrollador individual en <https://storedeveloper.microsoft.com>.
   Se verifica la identidad con un documento.
2. En Partner Center → **Aplicaciones y juegos** → **Nueva aplicación**, reservar el nombre **PulsoWin**.
3. En el producto → **Administración de productos** → **Identidad del producto**, copiar los tres valores:

   | Partner Center | Variable del repositorio |
   |---|---|
   | `Package/Identity/Name` | `STORE_NAME` |
   | `Package/Identity/Publisher` (empieza con `CN=`) | `STORE_PUBLISHER` |
   | `Package/Properties/PublisherDisplayName` | `STORE_PUBLISHER_DISPLAY` |

4. Guardarlos en GitHub → Settings → Secrets and variables → Actions → **Variables**. No son secretos:
   aparecen en el paquete publicado. Van las tres o ninguna: con una identidad a medias, el workflow falla
   con un aviso en vez de armar un paquete que la Store rechazaría.

## 2. Armar el paquete

En cada build, el workflow `Build y release` sube el artefacto `PulsoWin-<versión>-store` con el `.msix`.
Si las variables no están, el paquete usa una identidad de prueba: sirve para validarlo, pero no para subirlo.

En local:

```powershell
dotnet publish src\PulsoWin.App\PulsoWin.App.csproj -c Release -r win-x64 --self-contained true -o artifacts\msix-publish
.\packaging\msix\pack.ps1 -PublishDir artifacts\msix-publish -Version 0.4.0 -Out artifacts\msix `
  -Name "<STORE_NAME>" -Publisher "<STORE_PUBLISHER>" -PublisherDisplayName "<STORE_PUBLISHER_DISPLAY>"
```

## 3. Validar antes de subir

Desde una consola **como administrador** (el kit lo exige):

```powershell
& "C:\Program Files (x86)\Windows Kits\10\App Certification Kit\appcert.exe" test -appxpackagepath "<ruta>\PulsoWin-0.4.0-x64.msix" -reportoutputpath "$env:TEMP\pulsowin-wack.xml"
```

## 4. Envío

En el envío, en **Paquetes**, se sube el `.msix`. En **Propiedades**, el formulario pide justificar las
capacidades restringidas. Texto sugerido:

**allowElevation**

> PulsoWin opens as a standard user and never elevates at startup. Administrator rights are requested
> on demand, with the standard UAC prompt, for two user-initiated actions only: (1) "Per process" in the
> Network tab starts a separate helper process that reads the Windows kernel network ETW provider, which
> requires administrator rights, and sends only per-process byte counters back to the app over a local
> named pipe restricted to the current user and administrators, after checking the helper's process ID; (2) installing a Windows Update the user selected, through
> the Windows Update Agent API. If the user declines the prompt, the app keeps working without those features.

**unvirtualizedResources**

> PulsoWin is a disk cleaner. It deletes temporary files and browser caches that the user selects in
> %LOCALAPPDATA% and %APPDATA%. With file system write virtualization those deletions would only affect
> the package's private copy and would free no space, so FileSystemWriteVirtualization is disabled.
> The app does not write to the registry.

**runFullTrust**

> PulsoWin is a WPF desktop application (Win32, full trust).

Microsoft pide escribir a <reportapp@microsoft.com> **antes** de enviar una app con `allowElevation`.
En ese correo va la misma justificación.

## Si rechazan allowElevation

Hay que quitar la línea `allowElevation` de `packaging/msix/AppxManifest.xml` y ocultar, en la versión
empaquetada, «Ver por proceso» y la instalación de actualizaciones de Windows. Con eso la red se ve por
interfaz y la limpieza sigue completa.

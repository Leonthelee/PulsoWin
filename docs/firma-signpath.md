# Firma de código con SignPath Foundation

PulsoWin se compila y firma en GitHub Actions ([`.github/workflows/release.yml`](../.github/workflows/release.yml)).
Mientras no exista el token de SignPath, el workflow igual compila, prueba y deja el zip y el MSI **sin firma**.

## Por qué se firma en dos pasos

Smart App Control bloquea cualquier DLL sin firma que Windows no conozca (error `0x800711C7`), aunque el MSI que la
instaló sí esté firmado. Por eso el orden es:

1. **artefacto `app`:** se firman `PulsoWin.exe` y las DLL propias;
2. **artefacto `msi`:** el MSI se arma con esos binarios ya firmados y después se firma el MSI;
3. los `.sha256` se calculan al final (firmar cambia el hash).

Solo se firman archivos de este proyecto. Las DLL de .NET ya vienen firmadas por Microsoft.

## Configuración, una vez aprobado el proyecto

**En GitHub** (Settings → Secrets and variables → Actions):

| Tipo | Nombre | Valor |
|---|---|---|
| Secret | `SIGNPATH_API_TOKEN` | token de API del usuario CI en SignPath |
| Variable | `SIGNPATH_ORGANIZATION_ID` | ID de la organización en SignPath |
| Variable (opcional) | `SIGNPATH_PROJECT_SLUG` | por defecto `PulsoWin` |
| Variable (opcional) | `SIGNPATH_POLICY_SLUG` | por defecto `release-signing` |

**En SignPath**, el proyecto necesita dos *artifact configurations* con estos slugs. Los artefactos de GitHub llegan
como zip, por eso la raíz es `<zip-file>`.

`app`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<artifact-configuration xmlns="http://signpath.io/artifact-configuration/v1">
  <zip-file>
    <pe-file-set>
      <include path="PulsoWin.exe" />
      <include path="PulsoWin.dll" />
      <include path="PulsoWin.Core.dll" />
      <include path="PulsoWin.Presentation.dll" />
      <include path="en/PulsoWin.Core.resources.dll" />
      <for-each>
        <authenticode-sign />
      </for-each>
    </pe-file-set>
  </zip-file>
</artifact-configuration>
```

`msi`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<artifact-configuration xmlns="http://signpath.io/artifact-configuration/v1">
  <zip-file>
    <msi-file path="*.msi">
      <authenticode-sign />
    </msi-file>
  </zip-file>
</artifact-configuration>
```

Si se agrega un idioma, su `xx/PulsoWin.Core.resources.dll` debe sumarse a `app`. Si falta, el workflow se detiene:
revisa que cada `PulsoWin.exe` y `PulsoWin*.dll` tenga una firma válida antes de armar el MSI.

## Publicar una versión

1. Sube `<Version>` en `Directory.Build.props` y completa el `CHANGELOG.md`.
2. Crea y sube la etiqueta: `git tag -a vX.Y.Z -m "PulsoWin X.Y.Z"` y `git push origin vX.Y.Z`.
   El workflow rechaza la etiqueta si no coincide con `<Version>`.
3. Con firma activa, SignPath puede pedir la aprobación manual de cada solicitud (la del `app` y la del `msi`).
4. El workflow deja un **borrador** de release con los archivos y sus hashes verificados. Completa las notas y
   publícalo a mano.

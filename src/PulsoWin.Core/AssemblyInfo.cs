using System.Runtime.InteropServices;

// Las DLL nativas (nvml.dll, shell32, kernel32) solo se cargan desde System32,
// nunca desde la carpeta de la app ni del directorio actual (evita secuestro de DLL).
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

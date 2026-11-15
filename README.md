# Generador de etiquetas .NET

Port a .NET 8 de "Etiquetador de cuadros": lee esquemas eléctricos (PDF, DXF, DWG, Excel/CSV) y genera el Excel para GeniusPro de Cembre.

## Estructura
- `src/Etiquetador.Core`: lectores, extracción, Excel, sesión (sin UI). Usa PdfPig.
- `src/Etiquetador.Cli`: línea de comandos (`--prueba`, `--json`, `--tabla`, `--xlsx`).
- `src/Etiquetador.App`: ventana Avalonia 11 (todo en C#, sin AXAML).

## Compilar y ejecutar
1. Instalar el SDK de .NET 8.
2. `dotnet restore`
3. `dotnet run --project src/Etiquetador.App` (con `-- --demo` para cargar datos de ejemplo, `-- --prueba` para autotest)
4. CLI: `dotnet run --project src/Etiquetador.Cli -- --prueba`

La configuración se guarda en `%APPDATA%\EtiquetadorCuadros\config.json` (mismo formato que la versión Python).

## Crear el .exe con doble clic
Ejecuta `construir.bat`: compila el programa en `publish\EtiquetadorCuadros.exe` (funciona sin tener .NET instalado) y crea un acceso directo en el escritorio. Para compilarlo hace falta el SDK de .NET 8.

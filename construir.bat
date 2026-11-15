@echo off
rem Crea publish\EtiquetadorCuadros.exe y un acceso directo en el escritorio.
rem Hace falta tener instalado el SDK de .NET 8 (o superior).
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo No se encuentra dotnet. Instala el SDK de .NET 8 desde https://dotnet.microsoft.com/download
  pause
  exit /b 1
)

echo Compilando... (la primera vez descarga paquetes y tarda un poco)
dotnet publish src\Etiquetador.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
if errorlevel 1 (
  echo.
  echo Ha fallado la compilacion. Copia el error de arriba.
  pause
  exit /b 1
)

powershell -NoProfile -Command "$ws = New-Object -ComObject WScript.Shell; $d = [Environment]::GetFolderPath('Desktop'); $l = $ws.CreateShortcut((Join-Path $d 'Etiquetador de cuadros.lnk')); $l.TargetPath = (Resolve-Path '.\publish\EtiquetadorCuadros.exe').Path; $l.WorkingDirectory = (Resolve-Path '.\publish').Path; $l.Save()"

echo.
echo Listo. Programa: %~dp0publish\EtiquetadorCuadros.exe
echo Acceso directo creado en el escritorio: Etiquetador de cuadros
pause

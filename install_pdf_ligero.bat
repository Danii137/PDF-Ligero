@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "PDF_APP="
set "INSTALL_SCRIPT="

for /d %%D in ("%SCRIPT_DIR%firma*") do (
    if exist "%%~fD\build\output\PDFLigero.exe" (
        set "PDF_APP=%%~fD\build\output\PDFLigero.exe"
        set "INSTALL_SCRIPT=%%~fD\install-context-menu.ps1"
    )
)

if not defined PDF_APP (
    echo [ERROR] No se encontro PDFLigero.exe.
    echo Ejecuta primero el script build.ps1 de la carpeta de firma.
    pause
    exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%INSTALL_SCRIPT%"
if errorlevel 1 (
    echo [ERROR] No se pudo instalar la integracion de PDF Ligero.
    pause
    exit /b 1
)

echo.
echo PDF Ligero esta integrado con el Explorador.
echo Para usarlo con doble clic, elige Abrir con - PDF Ligero - Siempre.
echo.
pause

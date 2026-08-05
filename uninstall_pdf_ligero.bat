@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "UNINSTALL_SCRIPT="

for /d %%D in ("%SCRIPT_DIR%firma*") do (
    if exist "%%~fD\unregister-context-menu.ps1" (
        set "UNINSTALL_SCRIPT=%%~fD\unregister-context-menu.ps1"
    )
)

if not defined UNINSTALL_SCRIPT (
    echo [ERROR] No se encontro el desinstalador de PDF Ligero.
    pause
    exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%UNINSTALL_SCRIPT%"
if errorlevel 1 (
    echo [ERROR] No se pudo eliminar la integracion de PDF Ligero.
    pause
    exit /b 1
)

echo.
echo Integracion de PDF Ligero eliminada.
echo.
pause

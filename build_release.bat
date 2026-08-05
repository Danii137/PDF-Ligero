@echo off
setlocal

set "ROOT_DIR=%~dp0.."

echo Compilando Word2PDF.exe...
py -3 -m PyInstaller --noconfirm "%ROOT_DIR%\Word2PDF.spec"
if %errorlevel% neq 0 (
    echo [ERROR] Fallo la compilacion.
    pause
    exit /b 1
)

copy /Y "%ROOT_DIR%\dist\Word2PDF.exe" "%~dp0Word2PDF.exe" >nul
if %errorlevel% neq 0 (
    echo [ERROR] No se pudo copiar el ejecutable compilado al instalador.
    pause
    exit /b 1
)

echo Listo. Ejecutable actualizado en:
echo %~dp0Word2PDF.exe
pause

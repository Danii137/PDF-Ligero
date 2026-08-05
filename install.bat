@echo off
REM Se conserva por compatibilidad: ahora hay un instalador unico que registra
REM tanto Word2PDF como PDF Ligero. Este archivo solo lo lanza.
setlocal

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0instalar.ps1"
set "CODIGO=%ERRORLEVEL%"

echo.
pause
exit /b %CODIGO%

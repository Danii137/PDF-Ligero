@echo off
setlocal

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0instalar.ps1" %*
set "CODIGO=%ERRORLEVEL%"

echo.
pause
exit /b %CODIGO%

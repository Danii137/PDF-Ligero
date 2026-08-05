@echo off
REM Se conserva por compatibilidad. Creaba un entorno virtual propio y compilaba
REM sin icono, con rutas relativas a una carpeta padre que ya no existe.
REM Ahora lanza compilar-word2pdf.ps1, que usa el Python del sistema, incrusta el
REM icono del platano rojo y prueba una conversion real antes de sustituir.
setlocal

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0compilar-word2pdf.ps1" %*
set "CODIGO=%ERRORLEVEL%"

echo.
pause
exit /b %CODIGO%

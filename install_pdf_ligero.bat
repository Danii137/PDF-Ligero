@echo off
REM Instala solo PDF Ligero. Para registrar tambien Word2PDF usa instalar.bat.
setlocal

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0instalar.ps1" -SoloPdfLigero
set "CODIGO=%ERRORLEVEL%"

echo.
pause
exit /b %CODIGO%

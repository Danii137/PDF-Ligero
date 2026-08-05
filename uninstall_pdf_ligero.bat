@echo off
REM Quita solo PDF Ligero. Para retirar tambien Word2PDF usa desinstalar.bat.
setlocal

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0desinstalar.ps1" -SoloPdfLigero
set "CODIGO=%ERRORLEVEL%"

echo.
pause
exit /b %CODIGO%

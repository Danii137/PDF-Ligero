@echo off
REM Se conserva por compatibilidad. Apuntaba a una carpeta padre que ya no
REM existe y a un Word2PDF.spec que nunca estuvo aqui, asi que no funcionaba.
REM Ahora lanza compilar-word2pdf.ps1, que ademas incrusta el icono del platano
REM rojo y comprueba una conversion real antes de sustituir el ejecutable.
setlocal

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0compilar-word2pdf.ps1" %*
set "CODIGO=%ERRORLEVEL%"

echo.
pause
exit /b %CODIGO%

@echo off
setlocal

echo ==========================================
echo DESINSTALADOR DE WORD2PDF
echo ==========================================
echo.

reg delete "HKCU\Software\Classes\SystemFileAssociations\.docx\shell\Word2PDF" /f >nul 2>&1
reg delete "HKCU\Software\Classes\SystemFileAssociations\.doc\shell\Word2PDF" /f >nul 2>&1
reg delete "HKCU\Software\Classes\SystemFileAssociations\.rtf\shell\Word2PDF" /f >nul 2>&1

echo Menu contextual eliminado para el usuario actual.
echo.
pause

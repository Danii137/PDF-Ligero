@echo off
setlocal

echo ==========================================
echo INSTALADOR DE WORD2PDF
echo ==========================================
echo.

set "APP_DIR=%~dp0"
set "APP_EXE=%APP_DIR%Word2PDF.exe"

if not exist "%APP_EXE%" (
    echo [ERROR] No se encontro Word2PDF.exe en:
    echo %APP_EXE%
    echo.
    echo Copia la carpeta completa antes de instalar.
    pause
    exit /b 1
)

echo [INFO] Registrando menu contextual para el usuario actual...

reg add "HKCU\Software\Classes\SystemFileAssociations\.docx\shell\Word2PDF" /ve /d "Convertir a PDF" /f >nul
reg add "HKCU\Software\Classes\SystemFileAssociations\.docx\shell\Word2PDF" /v "Icon" /d "%APP_EXE%" /f >nul
reg add "HKCU\Software\Classes\SystemFileAssociations\.docx\shell\Word2PDF" /v "MultiSelectModel" /d "Player" /f >nul
reg add "HKCU\Software\Classes\SystemFileAssociations\.docx\shell\Word2PDF\command" /ve /d "\"%APP_EXE%\" \"%%1\"" /f >nul

reg add "HKCU\Software\Classes\SystemFileAssociations\.doc\shell\Word2PDF" /ve /d "Convertir a PDF" /f >nul
reg add "HKCU\Software\Classes\SystemFileAssociations\.doc\shell\Word2PDF" /v "Icon" /d "%APP_EXE%" /f >nul
reg add "HKCU\Software\Classes\SystemFileAssociations\.doc\shell\Word2PDF" /v "MultiSelectModel" /d "Player" /f >nul
reg add "HKCU\Software\Classes\SystemFileAssociations\.doc\shell\Word2PDF\command" /ve /d "\"%APP_EXE%\" \"%%1\"" /f >nul

reg add "HKCU\Software\Classes\SystemFileAssociations\.rtf\shell\Word2PDF" /ve /d "Convertir a PDF" /f >nul
reg add "HKCU\Software\Classes\SystemFileAssociations\.rtf\shell\Word2PDF" /v "Icon" /d "%APP_EXE%" /f >nul
reg add "HKCU\Software\Classes\SystemFileAssociations\.rtf\shell\Word2PDF" /v "MultiSelectModel" /d "Player" /f >nul
reg add "HKCU\Software\Classes\SystemFileAssociations\.rtf\shell\Word2PDF\command" /ve /d "\"%APP_EXE%\" \"%%1\"" /f >nul

echo.
echo INSTALACION COMPLETADA
echo.
echo Ya puedes hacer clic derecho en archivos .doc, .docx y .rtf para convertirlos.
echo Importante: Microsoft Word debe estar instalado en cada ordenador.
echo No muevas esta carpeta despues de instalar.
echo.
pause

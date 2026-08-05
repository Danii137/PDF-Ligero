@echo off
setlocal

cd /d "%~dp0.."

if not exist ".word2pdf_buildenv\Scripts\python.exe" (
    echo [INFO] Creando entorno limpio de compilacion...
    python -m venv .word2pdf_buildenv
    if errorlevel 1 (
        echo [ERROR] No se pudo crear el entorno virtual.
        pause
        exit /b 1
    )
)

echo [INFO] Instalando dependencias de compilacion...
".word2pdf_buildenv\Scripts\python.exe" -m pip install --upgrade pip pyinstaller PyQt5 docx2pdf
if errorlevel 1 (
    echo [ERROR] No se pudieron instalar las dependencias.
    pause
    exit /b 1
)

echo [INFO] Generando EXE...
".word2pdf_buildenv\Scripts\pyinstaller.exe" --clean --noconfirm --onefile --name Word2PDF --paths . --workpath .word2pdf_pyi_build --distpath .word2pdf_pyi_dist Word2PDF.py
if errorlevel 1 (
    echo [ERROR] Error al compilar Word2PDF.exe
    pause
    exit /b 1
)

copy /Y ".word2pdf_pyi_dist\Word2PDF.exe" "Word2PDF_Installer\Word2PDF.exe" >nul

echo.
echo Compilacion completada.
echo EXE actualizado en Word2PDF_Installer\Word2PDF.exe
echo.
pause

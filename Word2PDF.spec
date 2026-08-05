# Receta de empaquetado de Word2PDF.
#
# Existe para poder quitar binarios concretos del paquete, cosa que la linea de
# comandos de PyInstaller no permite: --exclude-module solo afecta a modulos de
# Python, y el hook de Qt copia sus DLL en bloque.
#
# Importa porque todo lo que va dentro se descomprime en %TEMP% en CADA
# ejecucion, y el Explorador lanza un proceso por archivo seleccionado.
#
# Se compila con compilar-word2pdf.ps1, que genera antes los iconos.

import os

raiz = os.path.abspath(SPECPATH)
trabajo = os.path.join(raiz, ".word2pdf-build")
icono_app = os.path.join(trabajo, "PDFLigero.ico")
icono_consola = os.path.join(trabajo, "homer.ico")

datos = [(icono_app, ".")]
if os.path.exists(icono_consola):
    datos.append((icono_consola, "."))

# Modulos que Word2PDF.py no importa en ninguna rama.
modulos_fuera = [
    "PyQt5.QtQuick",
    "PyQt5.QtQml",
    "PyQt5.QtQuickWidgets",
    "PyQt5.QtWebEngineWidgets",
    "PyQt5.QtMultimedia",
    "PyQt5.QtBluetooth",
    "PyQt5.QtNetwork",
    "PyQt5.QtSql",
    "PyQt5.QtTest",
    "PyQt5.QtDBus",
    "win32ui",
    "win32uiole",
    "pythonwin",
    "tkinter",
    "unittest",
    "pydoc",
    "doctest",
]

# Binarios que Qt solo carga para renderizar con OpenGL o QML. Esta aplicacion
# dibuja con QPainter sobre una superficie raster y no usa ninguno de los dos.
# Se comparan por nombre de archivo, en minusculas.
binarios_fuera = {
    "opengl32sw.dll",        # OpenGL por software, 7,4 MiB
    "d3dcompiler_47.dll",    # compilador de shaders de ANGLE
    "libglesv2.dll",         # ANGLE
    "libegl.dll",            # ANGLE
    "qt5quick.dll",
    "qt5qml.dll",
    "qt5qmlmodels.dll",
}


a = Analysis(
    [os.path.join(raiz, "Word2PDF.py")],
    pathex=[raiz],
    binaries=[],
    datas=datos,
    hiddenimports=[],
    hookspath=[],
    runtime_hooks=[],
    excludes=modulos_fuera,
    noarchive=False,
)

a.binaries = TOC(
    (nombre, ruta, tipo)
    for nombre, ruta, tipo in a.binaries
    if os.path.basename(nombre).lower() not in binarios_fuera
)

pyz = PYZ(a.pure, a.zipped_data)

exe = EXE(
    pyz,
    a.scripts,
    a.binaries,
    a.zipfiles,
    a.datas,
    name="Word2PDF",
    debug=False,
    strip=False,
    upx=False,
    console=True,
    icon=icono_app,
)

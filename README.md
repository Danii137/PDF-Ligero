# PDF Ligero y Word2PDF

Dos herramientas de escritorio para Windows que se instalan juntas desde esta
carpeta.

**PDF Ligero** es un visor y editor de PDF que cubre lo que se usa a diario sin
la complejidad de Acrobat Pro, funcionando por completo en local: nada sale del
equipo. **Word2PDF** convierte documentos de Word y RTF a PDF desde el menú
contextual del Explorador.

## PDF Ligero

- Visor rápido con varios documentos en pestañas y carga perezosa.
- Miniaturas virtualizadas, marcadores y búsqueda que solo se ejecuta al pulsar
  `Enter`.
- Combinar PDFs, insertar páginas arrastrando entre miniaturas, y quitar, girar
  o reordenar páginas.
- OCR local en español e inglés, con orientación automática y enderezado.
- Editor de marcadores con destinos y acciones avanzadas.
- Comparación de revisiones de planos: superposición, rojo/cian, cortinilla y
  alineación automática.
- Medición calibrada de distancias, perímetros y áreas.
- Edición visual de texto y rellenado de formularios AcroForm sin aplanarlos.
- Firma digital con certificado, con apariencia distinta por certificado.
- PDFs protegidos con contraseña: se piden en español y se abren en modo de solo
  lectura, explicando qué herramientas quedan desactivadas y por qué.

El original nunca se sobrescribe: cada operación crea una revisión recuperable
con `Ctrl+Z` y `Ctrl+Y`, y la aplicación sobrevive a un cierre inesperado.

## Word2PDF

Selecciona uno o varios `.doc`, `.docx` o `.rtf`, clic derecho y **Convertir a
PDF**. Usa Microsoft Word por COM, así que requiere tenerlo instalado.

## Instalación

```powershell
# 1. Compilar PDF Ligero
cd "firma automática"
.\build.ps1

# 2. Registrar las dos herramientas en el menú contextual
cd ..
.\instalar.bat
```

`desinstalar.bat` deshace el registro. No se borra ningún archivo.

La aplicación no necesita permisos de administrador: escribe solo en `HKCU`.

## Compilación

PDF Ligero es C# sobre .NET Framework y WinForms, compilado con `csc` mediante
`firma automática\build.ps1`. No hay `.csproj`: el script recoge todos los `.cs`
de esa carpeta. Word2PDF es Python con PyQt5.

La carpeta `firma automática\build\validation-*` contiene la batería de pruebas
automatizadas: motores aislados, interfaces y smoke sobre el visor real.

## Nota sobre la firma manuscrita

`firma automática\build\output\firma_limpia.png` **no forma parte de este
repositorio**: es la rúbrica de una persona concreta y publicarla permitiría
estamparla en cualquier documento. Cada instalación aporta la suya en esa ruta;
sin ella, la firma visible usa su representación de reserva.

## Licencia

AGPL v3. Ver [`LICENSE`](LICENSE).

El programa incorpora iTextSharp 5, que es AGPL, y por eso el conjunto lo es
también. El análisis completo está en [`LICENCIAS.md`](LICENCIAS.md) y la
atribución de terceros en
[`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).

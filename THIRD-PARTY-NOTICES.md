# Avisos de terceros

PDF Ligero y Word2PDF incorporan software de terceros. Este archivo recoge la
atribución que debe acompañar a cualquier copia distribuida. El análisis de las
obligaciones que implican estas licencias está en [LICENCIAS.md](LICENCIAS.md).

---

## PDF Ligero

### iTextSharp 5.5.13.3 — GNU AGPL v3

Copyright © 2000-2022 iText Group NV.

Se usa para leer y escribir la estructura de los PDF: combinar, organizar
páginas, marcadores, formularios AcroForm, edición de texto y firma digital.

Es la dependencia que determina la licencia del conjunto. El texto completo está
en `firma automática/packages/iTextSharp.5.5.13.3/gnu-agpl-v3.0.md`.

En cumplimiento de la sección 7(b) de la AGPL, la aplicación conserva la línea
`Producer` de iText en todo PDF que crea o manipula. El valor concreto es:

```text
iTextSharp™ 5.5.13.3 ©2000-2022 iText Group NV (AGPL-version)
```

La comprobación está implementada, no es solo una intención: ver
`IsExpectedProducerTransition` en `firma automática/PdfTextEditService.cs`.

### Bouncy Castle 1.8.9 — licencia MIT adaptada

Copyright © 2000-2021 The Legion of the Bouncy Castle Inc.

Criptografía para la firma digital de PDF. https://www.bouncycastle.org/licence.html

### PdfiumViewer 2.13.0 — Apache License 2.0

Copyright © Pieter van Ginkel.

Envoltorio .NET de PDFium. Es el que renderiza en pantalla, busca texto e
imprime. https://github.com/pvginkel/PdfiumViewer

### PDFium (PdfiumViewer.Native.x86_64.v8-xfa 2018.4.8.256) — BSD 3-Clause

Copyright © 2014 The PDFium Authors. Copyright © 2014 Google Inc.

Motor de renderizado, derivado de Foxit Software. Se distribuye como
`pdfium.dll`. El empaquetado NuGet es de Pieter van Ginkel (Apache 2.0).

### Tesseract OCR 5.5 y su runtime — Apache License 2.0 y otras

Copyright © Google Inc. y contribuyentes de Tesseract.

Se distribuye en `firma automática/runtime/ocr/` junto a las bibliotecas que
necesita para funcionar sin instalación global, y los modelos de idioma `spa`,
`eng` y `osd`. El OCR es completamente local: no envía documentos a ningún
servicio.

Ese directorio contiene además una compilación MSYS2/mingw-w64 de estas
bibliotecas, cada una con la licencia de su proyecto. Las familias presentes
son:

| Familia | Componentes | Licencia |
|---|---|---|
| Tesseract | `libtesseract-5`, `tesseract.exe` | Apache 2.0 |
| Leptonica | `libleptonica-6` | BSD 2-Clause |
| ICU | `libicudt75`, `libicuin75`, `libicuuc75` | Unicode License |
| GLib / GIO / GObject | `libglib-2.0-0`, `libgio-2.0-0`, `libgobject-2.0-0`, `libgmodule-2.0-0` | LGPL 2.1+ |
| Pango y Cairo | `libpango*`, `libcairo-2`, `libpixman-1-0` | LGPL 2.1+ / MPL |
| Texto y tipografía | `libharfbuzz-0`, `libfreetype-6`, `libfontconfig-1`, `libfribidi-0`, `libgraphite2`, `libthai-0`, `libdatrie-1` | MIT, FTL, LGPL según componente |
| Imagen | `libpng16-16`, `libjpeg-8`, `libtiff-6`, `libwebp-7`, `libwebpmux-3`, `libsharpyuv-0`, `libgif-7`, `libopenjp2-7`, `libjbig-0`, `libLerc` | permisivas (BSD/MIT/zlib) |
| Compresión | `zlib1`, `libdeflate`, `libzstd`, `liblz4`, `liblzma-5`, `libbz2-1`, `libbrotli*`, `libarchive-13` | permisivas (zlib/BSD/MIT) |
| Red y cifrado | `libcurl-4`, `libcrypto-3-x64`, `libssh2-1`, `libpsl-5`, `libidn2-0` | curl, Apache 2.0, BSD, LGPL |
| Internacionalización | `libiconv-2`, `libintl-8`, `libunistring-5` | LGPL |
| Runtime de GCC | `libstdc++-6`, `libgcc_s_seh-1`, `libwinpthread-1` | GPL 3 con excepción de runtime |
| Varios | `libexpat-1`, `libpcre2-8-0`, `libffi-8`, `libb2-1` | MIT / BSD |

Las bibliotecas LGPL se distribuyen como DLL independientes y sustituibles, que
es la forma en que esa licencia permite enlazarlas desde un programa con otra
licencia. El runtime de GCC lleva la excepción que permite su uso sin
contaminar la licencia del programa.

### Tipografías

La interfaz usa Bahnschrift y Segoe UI Variable Text, que forman parte de
Windows. No se redistribuyen: si faltan, la aplicación recurre a Segoe UI.

---

## Word2PDF

### PyQt5 — GPL v3

Interfaz de la ventana del conversor. Riverbank Computing la ofrece bajo GPL v3
o licencia comercial.

### pywin32 — licencia PSF

Automatización de Microsoft Word por COM para realizar la conversión.

Word2PDF **no incluye ni redistribuye Microsoft Word**: lo automatiza si está
instalado en el equipo. La licencia de Word corre por cuenta de quien lo use.

---

## Recursos propios

El icono del plátano rojo (`icono.png`, `firma automática/assets/PDFLigero.png`)
es obra propia de este proyecto.

La imagen de firma manuscrita `firma_limpia.png` **no se distribuye** y no forma
parte del repositorio: es la rúbrica de una persona concreta. Cada instalación
aporta la suya.

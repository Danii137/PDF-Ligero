# Motor OCR local de PDF Ligero

## Contrato de integración

- `PdfOcrService.GetAvailability()`
- `PdfOcrService.Analyze(path, settings, progress, token)`
- `PdfOcrService.CreateDefaultInstructions(analysis)`
- `PdfOcrService.RenderPreviewPng(path, instruction, dpi, token)`
- `PdfOcrService.Process(path, output, analysis, instructions, settings, progress, token)`

`PdfOcrPageInstruction` es el plan editable por la interfaz:

- `PageNumber`
- `Process`
- `ClockwiseRotationDegrees` (múltiplos de 90)
- `ApplyDeskew`
- `DeskewDegrees` (entre -5 y 5; positivo = horario en pantalla)

## Estrategia

- PDFium renderiza una sola página cada vez, con límite de 16 millones de
  píxeles por defecto.
- Tesseract se ejecuta localmente y fuera del proceso principal.
- Se omiten por defecto las páginas que ya tienen texto utilizable.
- El giro de 90/180/270 grados modifica la rotación de página sin rasterizar.
- El enderezado pequeño envuelve el contenido original en una matriz PDF:
  tampoco rasteriza ni recomprime imágenes.
- La capa OCR invisible se coloca palabra a palabra sobre el contenido ya
  corregido.
- El proceso acepta cancelación y entrega progreso por página.

## Seguridad de archivo

- El PDF original se abre solo para lectura.
- Se comprueba su huella antes y después del proceso.
- La salida se escribe en un temporal del volumen de destino, se reabre con
  iTextSharp y PDFium y finalmente se mueve de forma atómica.
- XFA se bloquea porque no puede conservarse de forma fiable.
- Las firmas digitales se detectan y el resultado informa de su invalidación.
- El enderezado automático no se aplica a páginas con enlaces, anotaciones o
  campos porque sus rectángulos no deben quedar desalineados.

## Runtime distribuido

`runtime/ocr` contiene Tesseract 5.5, sus DLL y solo los modelos `spa`, `eng` y
`osd`. `build.ps1` exige esos archivos y los copia a `build/output/ocr`.
El servicio busca primero ese runtime junto al ejecutable, por lo que no
depende de una instalación global ni de servicios en la nube.

Tamaño del runtime: aproximadamente 84,7 MiB en disco. No se carga durante el
arranque normal; Tesseract solo se inicia al analizar o procesar OCR.

## Validación

`compile-and-run.ps1` prueba deliberadamente el runtime distribuido y una ruta
con espacios. El fixture incluye acentos, una página inclinada 2 grados, otra
girada 90 grados y una tercera con texto vectorial que debe omitirse.

Resultado de referencia:

- 3 páginas conservadas;
- 2 páginas procesadas;
- 774 palabras reconocidas;
- orientación automática: corrección de 270 grados;
- enderezado automático: corrección de -2 grados;
- original idéntico por SHA-256;
- salida abierta y renderizada de nuevo con PDFium;
- duración aproximada: 8-10 segundos a 220 dpi en esta máquina.

# Contexto de continuidad — PDF Ligero

> Documento de relevo para Claude Code, Codex u otro agente.
>
> **Última auditoría:** 4 de agosto de 2026  
> **Estado funcional estable:** fases 1–8 terminadas, incluida la edición
> visual controlada de texto y el rellenado de AcroForm.  
> **Siguiente prioridad:** fase 9, endurecimiento transversal y distribución.  
> **Regla de lectura:** cuando este archivo y una suposición entren en conflicto,
> comprobar primero el código y los informes de QA. Las casillas pendientes de
> la sección 12 son el protocolo para trabajos futuros, no tareas abiertas de
> la fase 8.

## 1. Qué es el producto

PDF Ligero es una aplicación de escritorio para Windows que busca cubrir las
operaciones de PDF que se usan a diario sin la complejidad de Adobe Acrobat
Pro. La prioridad del producto es:

1. abrir y navegar PDFs con rapidez;
2. ofrecer pocas herramientas, pero bien integradas;
3. conservar siempre los originales;
4. trabajar localmente, sin subir documentos a servicios externos;
5. mantener una interfaz sobria, técnica y compacta;
6. no bloquear la interfaz durante trabajos pesados.

El repositorio también contiene `Word2PDF.py` y sus scripts de empaquetado.
Esa es otra herramienta del mismo instalador. El código principal de PDF
Ligero está en `firma automática\`; no confundir `build_release.bat` o
`build_portable.bat`, orientados a Word2PDF, con la compilación de PDF Ligero.

## 2. Rutas, construcción y ejecución

### Raíz auditada

```text
D:\desarrollos\Word2PDF_Installer
```

La ruta contiene un espacio y una carpeta con tilde. En PowerShell conviene
usar comillas y `-LiteralPath`.

### Fuente principal

```text
firma automática\
```

No hay `.csproj` ni solución. `build.ps1` recopila todos los `*.cs` situados
directamente en esa carpeta y los compila con:

```text
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
```

Compilación normal, restaurando paquetes si faltan:

```powershell
Set-Location -LiteralPath 'D:\desarrollos\Word2PDF_Installer\firma automática'
.\build.ps1
```

Compilación rápida cuando `packages\` ya está completo:

```powershell
Set-Location -LiteralPath 'D:\desarrollos\Word2PDF_Installer\firma automática'
.\build.ps1 -SkipRestore
```

Salida principal:

```text
firma automática\build\output\PDFLigero.exe
```

También se genera `FirmaAutomatica.exe` como copia compatible con integraciones
antiguas. Junto al ejecutable deben quedar:

- `PdfiumViewer.dll`;
- `pdfium.dll` nativa x64;
- `itextsharp.dll`;
- `BouncyCastle.Crypto.dll`;
- `PDFLigero.ico` y `PDFLigero.png`;
- el directorio `ocr\` completo.

Ejecución de desarrollo:

```powershell
& '.\build\output\PDFLigero.exe'
& '.\build\output\PDFLigero.exe' --open '.\build\output\sample.pdf'
```

Integración con el Explorador, después de compilar:

```powershell
Set-Location -LiteralPath 'D:\desarrollos\Word2PDF_Installer'
.\install_pdf_ligero.bat
```

Ese `.bat` localiza la carpeta `firma*` y ejecuta
`firma automática\install-context-menu.ps1`. No hace falta reinstalar la
integración para cada recompilación si la ruta de salida no cambia.

### Estado del binario actual

```text
PDFLigero.exe
Fecha: 04/08/2026 18:54:24  
Tamaño: 693760 bytes  
SHA-256: EE2C5DC80783805EB7C7C9E8C7C91650F7038CDA9F807D3A141AC3465B85C4DA
```

La raíz auditada no contiene metadatos `.git`. Antes de una refactorización
grande hay que crear una copia o iniciar control de versiones; no existe un
`git restore` fiable en el estado actual.

## 3. Restricciones técnicas

### C# 5 y WinForms clásico

El código debe mantenerse compatible con C# 5 y .NET Framework:

- no usar interpolación `$"..."`;
- no usar `?.`, `??=`, pattern matching, records, tuplas modernas, funciones
  locales, `using var`, switch expressions ni miembros con `=>`;
- preferir bloques `using (...)`, propiedades clásicas y `delegate`/`Action`;
- toda UI se construye por código; no hay diseñador ni `.resx`;
- mantener las clases en el namespace `FirmaAutomatica`;
- un nuevo `*.cs` en la raíz de `firma automática\` entra automáticamente en
  el build general;
- los harnesses de QA que enumeran fuentes de forma explícita sí deben
  actualizar su lista.

### Dependencias

- iTextSharp `5.5.13.3`;
- BouncyCastle `1.8.9`;
- PdfiumViewer `2.13.0`;
- PdfiumViewer Native x86_64 v8-xfa `2018.4.8.256`;
- Tesseract OCR local `5.5`, con `spa`, `eng` y `osd`.

iTextSharp 5 se distribuye bajo AGPL. La estrategia de licencia debe revisarse
antes de distribuir el programa fuera de un uso propio o interno.

## 4. Arquitectura y archivos clave

### Arranque e integración del sistema

- `Program.cs`
  - punto de entrada `[STAThread]`;
  - decide entre visor (`--open`/`--view`), combinación (`--merge`) y firma
    (`--sign` o argumentos normales);
  - limita lotes a 50 PDFs;
  - contiene parseo CLI, coordinación de instancias y logging.
- `ViewerInstanceBroker.cs`
  - mantiene una sola instancia del visor;
  - reenvía nuevas aperturas del Explorador a la ventana existente.
- `install-context-menu.ps1`, `unregister-context-menu.ps1`,
  `uninstall-legacy-context-menu.ps1`
  - registran o limpian las acciones del Explorador.
- `AppBranding.cs`, `assets\PDFLigero.png`, `generate-icon.ps1`
  - identidad visual del plátano rojo.

### Ventana principal y navegación

- `PdfViewerForm.cs`
  - orquestador central de pestañas, visor, búsqueda, herramientas, workers,
    historial y cierre;
  - contiene la clase privada `PdfWorkspace`;
  - cada workspace mantiene por separado `Path`, `ContentPath`, documento
    PDFium, visor, navegación, búsqueda e historial;
  - carga cada PDF de forma perezosa al seleccionar su pestaña;
  - integra actualmente comparación, medición, OCR, marcadores, organización,
    inserción, impresión, firma, edición de texto/AcroForm y zoom por
    rectángulo.
- `ClosablePdfTabControl.cs`
  - pestañas superiores con cierre individual.
- `PdfThumbnailList.cs`
  - miniaturas virtualizadas;
  - renderiza una página por tick en el hilo de UI;
  - solo encola páginas visibles o cercanas;
  - caché LRU acotada, actualmente 12 miniaturas por documento.
- `PdfRectangleZoomController.cs`
  - gesto neutro de selección rectangular;
  - usa `PdfRenderer.PointToPdf`, `BoundsToPdf` y `BoundsFromPdf`;
  - implementa `IMessageFilter` y un `IPdfMarker` ligero;
  - es una referencia útil para el controlador de medición.
- `PdfMeasurementModel.cs`
  - modelo inmutable de puntos y geometrías;
  - calibración, unidades, distancia, perímetro y área con doble precisión.
- `PdfMeasurementController.cs`
  - se crea solo al abrir la herramienta;
  - barra flotante plegable, captura en coordenadas PDF y `IPdfMarker`;
  - escalas por página y snapshot de calibración por cota;
  - estado únicamente en memoria, límites 200/100 y `Dispose` completo.
- `PdfPageSizeInfo.cs`
  - tamaño físico de la página y formato en mm/cm.
- `PdfPrintPreviewForm.cs`
  - vista previa de impresión con hoja y cotas visibles.

### Edición segura, historial y recuperación

- `PdfEditSession.cs`
  - revisiones inmutables en disco;
  - Undo/Redo y manifiesto de recuperación;
  - máximo de 8 revisiones propias;
  - objetivo máximo de 768 MiB por sesión/documento;
  - límite global de recuperación de 2 GiB;
  - raíz predeterminada:
    `%LOCALAPPDATA%\PDFLigero\Recovery`;
  - en QA puede cambiarse mediante `PDFLIGERO_RECOVERY_ROOT`.
- `PdfAtomicFileService.cs`
  - guardado mediante temporal junto al destino;
  - vaciado durable, validación y `File.Replace`/`File.Move`;
  - huella rápida de cinco muestras y SHA-256 completo.
- `PdfBackgroundOperationForm.cs`
  - diálogo común para operaciones que no deben bloquear el hilo de UI.
- `PdfTextEditService.cs`
  - reemplazo visual incremental, texto Unicode embebido y validación de la
    revisión;
  - transforma correctamente coordenadas PDFium con `CropBox` desplazado y
    giros 0/90/180/270; `/UserUnit` distinto de 1 aún no está verificado;
  - conserva páginas, formularios, metadatos descriptivos, propiedades XMP y
    firmas incrustadas; `Producer`/fechas técnicas pueden reflejar la revisión.
- `PdfTextEditSelectionController.cs`, `PdfTextEditDialog.cs`
  - selector bajo demanda con cruceta y confirmación central;
  - previsualización, autoajuste, fuente, alineación y colores;
  - se dispone al cambiar revisión o cerrar la pestaña.
- `PdfAcroFormService.cs`, `PdfAcroFormFillForm.cs`
  - analiza y rellena campos AcroForm sin aplanarlos;
  - crea un único editor dinámico para el campo seleccionado y guarda solo
    diferencias;
  - mantiene apariencias, valores canónicos, widgets y contenido de página.
- `PdfPageInsertService.cs`
  - inserción de PDFs entre páginas.
- `PdfPageOrganizerService.cs`
  - quitar, girar y reordenar páginas.
- `PdfMergeService.cs`, `PdfMergeForm.cs`
  - combinación con conservación de estructura, marcadores y formularios.

### OCR y marcadores

- `PdfOcrService.cs`
  - análisis, giro, deskew y capa de texto;
  - máximo predeterminado de 16 millones de píxeles por página.
- `PdfOcrOptionsForm.cs`, `PdfOcrReviewForm.cs`,
  `PdfOcrProgressForm.cs`
  - configuración, revisión visual y progreso cancelable.
- `PdfBookmarkService.cs`
  - conserva destinos y acciones avanzadas sin simplificarlos.
- `PdfBookmarkEditorForm.cs`
  - editor jerárquico de marcadores.

### Comparación de planos — terminada

- `PdfPlanComparisonService.cs`
  - sesión PDFium propia y perezosa;
  - renderiza solo una pareja de páginas;
  - normaliza tamaños físicos;
  - modos A, B, superposición, rojo/cian y cortinilla;
  - alineación física, automática y ajuste manual;
  - límite predeterminado de 4 millones de píxeles por página y 128 MiB de
    memoria de trabajo.
- `PdfPlanComparisonSurface.cs`
  - superficie completa dentro de la pestaña;
  - worker cancelable;
  - selectores A/B y página;
  - controles plegables y responsive;
  - al cerrarse cancela, libera bitmaps y dispone su sesión.

### Firma

- `CertificateDialog.cs`
  - certificado del almacén de Windows o PFX/P12;
  - recuerda selección sin guardar contraseñas.
- `SigningFlowController.cs`
  - flujo de firma;
  - usa append mode para no reescribir firmas previas.
- `PdfPlacementForm.cs`, `SignatureSelectionBox.cs`
  - colocación visual y selección de campos existentes.
- `SignatureAppearanceRenderer.cs`
  - apariencia visible y fallback seguro.
- `UserPreferences.cs`
  - preferencias e imagen por huella de certificado.

## 5. Funcionalidad terminada

La siguiente lista describe comportamiento estable que no debe degradarse:

- apertura rápida mediante botón, `Ctrl+O`, CLI y arrastrar/soltar;
- varias pestañas en una sola ventana y carga perezosa;
- página editable, total, tamaño de papel y navegación;
- miniaturas y marcadores en panel izquierdo plegable;
- búsqueda `Ctrl+F` que **no comienza al escribir**: solo busca al pulsar
  `Enter`; `Mayús+Enter` retrocede;
- zoom por rectángulo cuando no hay herramienta activa;
- impresión con preview, hoja y dimensiones visibles;
- combinación de varios PDFs y menú contextual del Explorador;
- inserción exacta de PDFs entre miniaturas;
- quitar, girar y reordenar páginas;
- historial `Ctrl+Z`/`Ctrl+Y`, autoguardado de revisiones y recuperación;
- OCR local español/inglés, orientación automática y deskew;
- creación y edición avanzada de marcadores;
- sustitución visual controlada de texto mediante `T`/`Ctrl+E`, con
  previsualización, Unicode, formato básico y revisión recuperable;
- rellenado de formularios AcroForm de texto, casilla, opción, combo y lista
  sin aplanar los campos;
- firma digital y apariencia distinta por certificado;
- comparación de revisiones con `Ctrl+Mayús+C`, superposición, cambios
  rojo/cian, alternancia y cortinilla;
- alineación automática/manual de la comparación;
- medición calibrada con `Ctrl+Mayús+M`, distancia, perímetro, área,
  escalas rápidas/calibración conocida y unidades mm/cm/m;
- interfaz compacta y adaptada a 900×620 y escalas 125/150 %.

El detalle histórico y los criterios ya superados están en:

- `ROADMAP_PDF_LIGERO.md`;
- `firma automática\README.md`;
- `firma automática\HANDOFF.md`.

## 6. Invariantes de seguridad y rendimiento

Estas reglas forman parte del producto, no son recomendaciones opcionales.

### Originales y guardado

1. Nunca escribir directamente en el PDF de origen.
2. Una operación estructural crea primero una revisión completa y validada en
   Recovery.
3. Publicar al destino solo mediante temporal, validación y sustitución
   atómica.
4. Cancelar o fallar no puede dejar un PDF parcial ni temporales huérfanos.
5. Antes de reutilizar o sobrescribir un destino, comprobar si cambió fuera de
   la aplicación.
6. La comparación y la primera entrega de medición son de solo lectura y no
   deben crear revisiones de Recovery.
7. Texto y AcroForm deben aplicar una revisión validada mediante
   `PdfEditSession.RevisionCommit`; cancelar el diálogo no reserva ni publica
   una revisión.

### Firmas y formularios

1. Avisar antes de cualquier modificación posterior a una firma digital.
2. No prometer que una edición conserva la validez criptográfica de la firma.
3. Bloquear operaciones estructurales sobre XFA cuando no se pueda garantizar
   un resultado correcto.
4. El firmado propiamente dicho debe conservar append mode y reutilizar campos
   `/Sig` vacíos compatibles.
5. La edición visual de texto no es redacción: debe avisar siempre de que el
   contenido anterior puede seguir dentro del archivo.
6. El rellenado AcroForm no aplana campos. XFA, firmas/certificaciones,
   restricciones FieldMDP/DocMDP y derechos de uso Adobe se bloquean en esta
   primera entrega cuando no puede garantizarse un resultado seguro.

### Rendimiento

1. No cargar todas las pestañas al abrirlas.
2. No renderizar todas las páginas ni todas las miniaturas por adelantado.
3. No conservar PDFs completos duplicados en RAM; el historial vive en disco.
4. El OCR solo carga Tesseract al invocarlo.
5. La comparación solo abre su sesión al invocarla y conserva dos bitmaps
   normalizados.
6. La medición debe operar sobre el `PdfRenderer` ya visible, sin abrir otro
   PDF ni rasterizar páginas.
7. Todo overlay debe invalidar únicamente lo necesario y no crear bitmaps de
   página completa por cada movimiento del ratón.
8. Mantener límites explícitos y pruebas de documentos grandes.
9. Texto y formularios se cargan únicamente al invocarlos. El análisis y el
   guardado se ejecutan fuera del hilo de UI y cada comprobación de identidad
   realiza como máximo un hash completo bajo un bloqueo de lectura.

## 7. Gotchas que han causado o pueden causar errores

### `Path` no es `ContentPath`

En `PdfWorkspace`:

- `Path` identifica el PDF original con el que se abrió la pestaña;
- `ContentPath` apunta al contenido que el usuario está viendo ahora;
- después de insertar, organizar, aplicar OCR, editar marcadores, deshacer,
  rehacer o recuperar, `ContentPath` puede ser una revisión dentro de Recovery.

Para leer o procesar la revisión visible debe usarse `ContentPath`. Usar
`Path` silenciosamente devolvería al original y perdería cambios del flujo.
Para deduplicar pestañas o explicar el origen sí puede ser correcto usar
`Path`. No borrar una revisión señalada por `ContentPath` mientras el visor,
una comparación o un worker aún la utiliza.

### Recovery y cambio de revisión

- Reservar salidas con `PdfEditSession.ReserveRevisionPath`.
- Usar `BeginRevisionCommit` y completar solo después de que el nuevo
  documento esté activo en el visor.
- En fallo: rollback o `PreserveForRecovery`, según el punto exacto.
- No limpiar Recovery por conveniencia: puede ser la única copia de cambios
  no guardados.
- La medición es estado efímero. Si cambia `ContentPath` por una nueva
  revisión, Undo/Redo o recuperación, invalidar las mediciones de ese
  workspace para no mostrar cotas sobre una geometría distinta.

### PDFium no se trata como thread-safe

- El `PdfiumDocument` del viewer pertenece al flujo normal del visor.
- Miniaturas: render en UI, una por tick.
- Workers que necesiten PDFium deben abrir su propio documento.
- Comparación serializa `Compare`, consultas y `Dispose` con el mismo lock.
- Nunca renderizar y disponer el mismo handle PDFium en paralelo.
- Las conversiones `PointToPdf`, `BoundsFromPdf` y la colección de markers del
  renderer deben tocarse desde el hilo de UI.

### `Dispose` es parte de la corrección

Liberar siempre:

- `PdfiumDocument`;
- `Bitmap`, `Image` y composiciones temporales;
- `FileStream` y leases de verificación;
- `CancellationTokenSource`;
- `Timer`, `Font`, `ToolTip`, formularios y controles temporales;
- sesiones y resultados de comparación.

Si un resultado de worker llega después de cerrar la superficie o cambiar de
generación, descartarlo **y disponerlo**. Antes de `BeginInvoke`, comprobar
`IsDisposed`, `Disposing` y que el handle sigue existiendo. Al cerrar una
pestaña, detener primero controladores, timers y workers que dependan de ella.

El cierre total tiene un orden deliberado y no debe convertirse otra vez en
un `TabPage.Dispose()` por workspace: preparar todos los workspaces, disponer
una sola vez `documentTabs` y después liberar cada documento, Recovery y
lease. Los `PdfDocument` deben seguir vivos hasta que desaparezcan todos los
renderers. En PdfiumViewer 2.13, asignar `Viewer.Document = null` no descarga
el documento conservado por el renderer. `closingAll` se activa antes de
ocultar la ventana para bloquear callbacks tardíos de la instancia única.

### Workers y cancelación

- WinForms solo se modifica desde el hilo de UI.
- Cada petición larga necesita token/generación para ignorar respuestas
  obsoletas.
- Cancelar debe ser cooperativo y comprobado dentro de bucles costosos.
- Serializar la finalización del worker y el `Dispose` de los recursos que usa.
- No considerar el diálogo cerrado como prueba de que el worker terminó.
- No ejecutar simultáneamente varios harnesses que escriban el mismo EXE o
  directorio de salida: Windows puede bloquear el archivo y producir un falso
  fallo `CS0016`.

### Sin control de versiones

No hay `.git` en la raíz auditada. Conservar los cambios ajenos, editar con
parches pequeños y revisar siempre los archivos recién modificados. Evitar
reescrituras mecánicas de `PdfViewerForm.cs`: es grande y concentra muchas
integraciones.

## 8. Convenciones de interfaz

El lenguaje visual imita una lámina arquitectónica:

- fondo ventana `RGB(234,233,230)`;
- superficies cálidas casi blancas `RGB(250,249,247)`;
- grafito para títulos `RGB(31,31,29)`;
- divisores finos `RGB(211,209,204)`;
- único acento bermellón `RGB(238,91,61)`;
- Bahnschrift Light/SemiCondensed para títulos, pestañas, cifras y rótulos
  técnicos;
- Segoe UI Variable Text para controles y lectura, con fallback seguro;
- sin sombras pesadas, ribbons, paneles permanentes grandes ni animaciones
  costosas.

Patrón de layout:

- pestañas arriba;
- header compacto con documento, página y papel;
- miniaturas/marcadores plegables a la izquierda;
- rail de herramientas de 48 px a la derecha;
- funciones menos frecuentes dentro de `Más`;
- superficies temporales, como comparación, cubren la pestaña sin reducir el
  área normal del PDF;
- tooltips y `AccessibleName` en iconos no textuales;
- probar siempre ancho mínimo 900×620 y escalado 125/150 %.

Atajos que no deben romperse:

- `Ctrl+O`: abrir;
- `Ctrl+W`: cerrar pestaña;
- `Ctrl+F`: buscar;
- `Ctrl+E`: seleccionar una zona para reemplazo visual de texto;
- `Ctrl+S`: guardar copia;
- `Ctrl+P`: imprimir;
- `Ctrl+Z` / `Ctrl+Y`: deshacer / rehacer;
- `Ctrl+Mayús+S`: firmar;
- `Ctrl+Mayús+B`: editar marcadores;
- `Ctrl+Mayús+C`: comparar revisiones;
- `Ctrl+Mayús+M`: medir planos;
- `Esc`: cancela primero la interacción temporal activa.

Una nueva herramienta debe integrarse en la exclusión mutua de
`RefreshToolAvailability`, `CanUseRectangleZoom`, teclado, cambio/cierre de
pestaña y `FormClosing`.

## 9. Fase 7: medición calibrada — **COMPLETADA**

### Alcance cerrado para la primera entrega

- distancia;
- perímetro;
- área;
- escalas rápidas `1:1`, `1:20`, `1:50`, `1:100` y `1:200`;
- escala manual positiva en formatos `75`, `1:75`, `75,5` o `75.5`;
- calibración por dos puntos indicando una longitud conocida;
- unidades `mm`, `cm` y `m`;
- geometrías y etiquetas guardadas solo en memoria y separadas por página;
- controlador bajo demanda sobre el `PdfRenderer` ya existente;
- barra flotante compacta y plegable;
- cruceta nativa `+` con hotspot central exacto mientras se mide;
- atajo implementado `Ctrl+Mayús+M`;
- sin persistencia en el PDF, exportación ni escritura de archivos en esta
  primera entrega.

### Archivos y API reales

- `PdfMeasurementModel.cs`
  - `PdfMeasurementCalibration.FromScale(...)`;
  - `PdfMeasurementCalibration.FromKnownDistance(...)`;
  - `PdfPageMeasurement` para distancia, perímetro y área;
  - `PdfMeasurementFormatter` y unidades mm/cm/m;
  - modelo inmutable, validaciones de finitud/rango y matemáticas sin WinForms.
- `PdfMeasurementController.cs`
  - constructor sobre el `PdfRenderer` ya visible y predicado de activación;
  - `Activate`, `Deactivate`, `Clear`, `NotifyActivePage` y `Dispose`;
  - `IsActive`, `HasMeasurements`, `ActiveStateChanged` y `StatusChanged`;
  - barra flotante/plegable, escalas rápidas y manuales, calibración,
    unidades y markers;
  - parser manual invariante con coma/punto decimal y confirmación por
    `Enter` o al salir del campo;
  - entrada incorrecta visible y pendiente: consume el clic del lienzo para
    impedir medir accidentalmente con la escala anterior;
  - cruceta mediante `SetCursor` y refuerzo ligero en `MouseMove`, sin bitmap,
    repintado adicional ni modificar el cursor de la barra;
  - `Enter`, doble clic, Retroceso y Escape sin secuestrar otros controles;
  - máximo de 200 mediciones y 100 vértices.
- `PdfViewerForm.cs`
  - botón `↔`, menú `Más -> Medir plano…` y `Ctrl+Mayús+M`;
  - un controlador lazy por `PdfWorkspace`;
  - estado mostrado dentro del encabezado existente;
  - exclusión con comparación, edición, OCR y zoom rectangular;
  - invalidación al cambiar `ContentPath` y limpieza al cerrar.

### Modelo y reglas implementadas

1. Guardar puntos en coordenadas PDF, no en píxeles de pantalla.
2. Mantener calibración por página, porque un mismo documento puede mezclar
   escalas.
3. Interpretar `1:N` como:
   `longitud real = longitud física de la hoja × N`.
4. En calibración de dos puntos:
   `factor = longitud real conocida / distancia física observada`.
5. Calcular polígonos con doble precisión; usar suma de segmentos para
   perímetro y fórmula del cordón para área.
6. Convertir unidades solo para presentar; mantener una unidad base estable,
   preferiblemente milímetros reales.
7. Recalcular etiquetas al cambiar unidad sin alterar la geometría.
8. Tratar zoom, scroll y resize como cambios de proyección, no de medición.
9. Al cambiar de página, conservar las geometrías terminadas de esa página,
   cancelar únicamente el trazado incompleto.
10. Al cambiar la revisión visible (`ContentPath`), limpiar todas las
    mediciones del workspace o pedir confirmación; no reutilizar coordenadas
    sobre contenido distinto.

### Interacción final

- abrir/cerrar medición con `Ctrl+Mayús+M`;
- modos compactos de distancia, perímetro, área y calibración;
- escribir una escala libre y confirmarla con `Enter`; `Esc` recupera la
  selección anterior si hay una entrada pendiente;
- clics añaden vértices;
- usar el centro de la cruceta como punto exacto de captura;
- `Enter` o doble clic termina perímetro/área;
- `Esc` cancela solo el trazado actual; un segundo `Esc` puede cerrar la
  herramienta si no hay trazado;
- deshacer último punto, borrar la última cota de la página y limpiar todas
  desde la barra;
- plegar controles sin ocultar las cotas;
- etiquetas legibles, con fondo cálido mínimo y trazo bermellón;
- el pan y la rueda deben seguir funcionando cuando no se está capturando un
  punto;
- el zoom por rectángulo debe estar desactivado mientras medición está activa.

El primer `Esc` cancela un trazado; sin trazado, cierra la herramienta. Las
teclas de la medición solo se interceptan cuando el canvas tiene el foco, de
modo que la caja de página y los desplegables conservan su comportamiento.

### Orden concreto de trabajo

- [x] Implementar y probar `PdfMeasurementModel.cs` de forma aislada.
- [x] Implementar `PdfMeasurementController.cs` bajo demanda.
- [x] Dibujar geometrías y etiquetas mediante markers/overlay ligero.
- [x] Añadir escalas rápidas y calibración por dos puntos.
- [x] Añadir escala manual con Enter/salida, decimales y memoria por página.
- [x] Sustituir la mano por una cruceta precisa solo durante la medición.
- [x] Añadir selector de mm/cm/m.
- [x] Mantener estado en memoria por workspace y página.
- [x] Integrar botón/menú/`Ctrl+Mayús+M` en `PdfViewerForm.cs`.
- [x] Integrar cancelación con `Esc`, cambio de página y cambio/cierre de
      pestaña.
- [x] Integrar exclusión con comparación, OCR, operaciones estructurales y
      zoom por rectángulo.
- [x] Disponer controlador y estado al recargar una revisión o cerrar.
- [x] Crear harness matemático con casos conocidos.
- [x] Crear harness UI con zoom, scroll, cambio de unidad, plegado y rotación.
- [x] Ejecutar regresiones de comparación, organizador, OCR y bookmarks.
- [x] Inspeccionar visualmente capturas ancha y mínima con página rotada.
- [x] Actualizar `README.md`, `HANDOFF.md`, `ROADMAP_PDF_LIGERO.md` y este
      documento.

### Criterios mínimos para marcarla terminada

- error matemático menor que la tolerancia fijada en fixtures conocidos;
- resultados idénticos al medir antes y después de zoom/scroll;
- calibración de dos puntos reproducible;
- escala manual decimal reproducible, independiente por página y conservada
  como snapshot de cada cota;
- una escala manual inválida no permite capturar con la calibración anterior;
- cruceta persistente sobre texto/enlaces, zoom y giro, con restauración al
  desactivar o disponer;
- distancia, perímetro y área correctos en las tres unidades;
- estado independiente por página y por pestaña;
- cero escritura sobre original, Recovery o temporales;
- cero carga de PDFium adicional;
- `Dispose` y cierre dejan cero barras, filtros y markers residuales;
- `Esc`, cambio de pestaña y cierre durante una geometría dejan cero
  controladores activos;
- la aplicación sigue abriendo PDFs con la misma ruta rápida cuando medición
  nunca se invoca.

## 10. Fase 8: edición de texto y AcroForm — **COMPLETADA**

### Edición visual controlada

- `T` abre un menú mínimo; `Ctrl+E` inicia directamente la selección de texto.
- El selector usa cruceta, solo acepta una página y muestra una `T` central para
  confirmar. `Esc`, cambio de página/pestaña/revisión y cierre lo limpian.
- Antes de mostrar el diálogo, el servicio analiza el PDF, transforma el
  rectángulo PDFium y extrae únicamente el texto de esa zona bajo un solo guard
  de lectura.
- El diálogo permite texto Unicode, sans/serif/mono, tamaño manual o autoajuste,
  alineación izquierda/centro/derecha, color de texto y color/cubierta de fondo.
- Guardar añade una cubierta y texto nuevo en modo incremental. No modifica el
  stream antiguo ni constituye redacción segura.
- `CropBox` con origen desplazado y `/Rotate` 0/90/180/270 quedan normalizados
  por `PdfTextPageTransform`. `/UserUnit` distinto de 1 no se promete hasta
  añadir un fixture específico.
- La revisión conserva páginas, AcroForm, metadatos descriptivos, propiedades
  XMP y firmas incrustadas; `Producer`, `ModDate` y fechas técnicas XMP pueden
  actualizarse para identificar la nueva revisión;
  una firma anterior puede seguir verificando su revisión original, pero la
  edición es una modificación posterior y se avisa **antes** del diálogo.
- XFA y PDFs sin permisos completos se bloquean. Un fallo deja el original y
  Recovery en estado coherente y muestra un solo diálogo de error.

### Rellenado AcroForm

- `T -> Rellenar formulario PDF…` analiza campos interactivos en segundo plano.
- La ventana lista y busca campos por orden de página/posición, pero materializa
  solo un editor cada vez.
- Admite texto, multilínea, contraseña enmascarada en la interfaz, checkbox con export value
  real, radio, combo y listas simples/múltiples.
- Firma, botón, solo lectura, rich text y file select son informativos o se
  bloquean; los scripts/cálculos se conservan pero no se ejecutan.
- Solo se escriben valores realmente cambiados, en append mode, con apariencias
  `/AP` regeneradas y sin aplanar el formulario.
- XFA, PDFs firmados/certificados, restricciones DocMDP/FieldMDP y derechos de
  uso ampliados Adobe se bloquean conservadoramente.
- El enmascarado de un campo de contraseña solo evita mostrar el valor en la
  ventana: no cifra ese valor dentro del PDF. Los scripts y cálculos se
  conservan, pero PDF Ligero no los ejecuta.

### Integración, seguridad y rendimiento

- Ambos flujos reservan una revisión mediante `PdfEditSession`, la validan con
  PDFium antes de activarla y participan en Recovery/Undo/Redo.
- Si llega otro PDF desde el Explorador durante un diálogo, se crea su pestaña
  pero su activación se difiere hasta terminar la operación; la edición conserva
  el workspace de origen.
- La apertura de archivos, medición, comparación, OCR, organización, búsqueda y
  zoom rectangular son mutuamente excluyentes con el selector cuando procede.
- Servicios, controladores, fuentes embebidas y formularios se crean solo al
  invocar la herramienta. No hay coste de render ni de PDFium en el arranque
  normal por esta fase.
- El análisis previo y el guardado hacen cada uno un único hash completo del
  origen bajo `FileShare.Read`; no se repite el hash dentro del mismo guard.

Limitación deliberada: el reemplazo visual puede dejar texto y datos debajo de
la cubierta. No usarlo para secretos ni datos personales que deban eliminarse.
La edición directa de objetos PDF y la redacción/saneado verificable no forman
parte de esta entrega. El texto Unicode requiere que Windows disponga de una
fuente incrustable que cubra todos los caracteres solicitados; si no existe,
la operación se bloquea en lugar de publicar glifos vacíos.

## 11. Pruebas existentes y comandos

Ejecutar desde `firma automática\`. Cerrar previamente todas las ventanas de
PDF Ligero y lanzar los scripts de forma secuencial.

### Build completo

```powershell
.\build.ps1 -SkipRestore
```

### Edición de texto y AcroForm

```powershell
.\build\validation-content-edit-engine\compile-and-run.ps1
.\build\validation-content-edit-ui\compile-and-run.ps1
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass `
  -File .\build\validation-content-edit-viewer\run-smoke.ps1
.\build\validation-acroform\compile-and-run.ps1
.\build\validation-acroform-ui\compile-and-run.ps1
```

Últimos resultados auditados el 4 de agosto de 2026:

- texto/motor: `build\validation-content-edit-engine\output\run-20260804-193136-711`,
  `RESULTADO=PASS`; Unicode real, fuente embebida, cuatro giros, `CropBox`,
  append prefix, AcroForm/metadatos/XMP, firma previa, XFA, temporales e
  identidad del original;
- texto/UI: `build\validation-content-edit-ui\run-20260804-194227-3cd8bc5b`,
  `RESULTADO=PASS`; selector, confirmación central, `Esc`, modelo, colores y
  layout 100/125/150 %;
- texto/visor real:
  `build\validation-content-edit-viewer\run-20260804-194944-f62ba4fd`,
  `RESULTADO GLOBAL: PASS`; botón/menús lazy, `Ctrl+E`, exclusión de
  herramientas, ciclo selector/T central, activación de revisión,
  Recovery/Undo/Redo, recreación, cierre y originales intactos;
- AcroForm/motor: `build\validation-acroform\output\run-20260804-174733`,
  `RESULTADO=PASS`; valores Unicode/canónicos, `/AP`, prefijo incremental,
  cambios diferenciales, Recovery/Undo/Redo y bloqueos;
- AcroForm/UI: `build\validation-acroform-ui\run-20260804-194845-7587ce6d`,
  `RESULTADO=PASS`; fixture de 2 páginas, 11 campos/8 editables, todos los
  editores y layout sin solapes a 100/125/150 %, además de una revisión
  rellenable conservada y renderizada con PDFium.

Capturas inspeccionadas: `dialog-100.png`, `selection-100.png` y las tres
`acroform-completo-*.png`. Los cuatro renders de rotación del motor de texto se
inspeccionaron también visualmente y mantienen orientación, caja y Unicode. Los
renders `revision-rellenada-pagina-1.png` y `revision-rellenada-pagina-2.png`
confirman visualmente apariencias, acentos, listas y CJK (`東京`); el resolvedor
solo acepta una fuente si cubre todos los codepoints requeridos.

### Medición de planos

```powershell
.\build\validation-measurement-engine\compile-and-run.ps1
.\build\validation-measurement-ui\compile-and-run.ps1
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass `
  -File .\build\validation-measurement-viewer\run-smoke.ps1
```

Últimos informes auditados el 4 de agosto de 2026:

- motor:
  `build\validation-measurement-engine\run-20260804-171957-a19bd5a7`,
  `RESULTADO=PASS`;
- UI:
  `build\validation-measurement-ui\run-20260804-171957-da2b455b`,
  `RESULTADO=PASS`;
- capturas inspeccionadas:
  `01-medicion-ancha.png`, `02-medicion-900x620-rotada.png` y
  `03-escala-manual-75-5-cruceta.png`;
- visor real:
  `build\validation-measurement-viewer\run-20260804-172004-2d5f33be`,
  `RESULTADO GLOBAL: PASS`;
- captura integrada inspeccionada:
  `01-medicion-integrada.png`.

El motor cubre la matriz de tres geometrías × tres unidades. El harness UI
cubre el parser manual, commit por `Enter`/salida, entrada inválida, escala
decimal por página, snapshot por cota, ciclo y restauración de la cruceta,
calibración, secuencia real de doble clic, borrar última, zoom, scroll,
rotación, límites 200/100, limpieza de `Dispose` e identidad intacta del PDF.

### Rendimiento de apertura y memoria

Auditoría reproducible del 4 de agosto de 2026:

```powershell
.\build\validation-performance\run-benchmark.ps1
```

Informe consolidado:
`build\validation-performance\PERFORMANCE_REPORT.md`.

Series finales sobre el ejecutable de la fase 8:

- ventana vacía/vectorial, un calentamiento y cinco procesos medidos:
  `build\validation-performance\run-20260804-193401-a0987ea2`;
- documento grande, escaneado y multipestaña, un calentamiento y tres procesos
  medidos: `build\validation-performance\run-20260804-193459-309bbbe8`;
- ambas con `RESULTADO_GLOBAL=PASS`;
- fixtures intactos y cero procesos residuales.

Medianas observadas en el equipo auditado:

- ventana vacía lista en `307,0 ms`, `27,8 MiB` privados;
- PDF vectorial de dos páginas listo en `364,5 ms`, `42,0 MiB` privados;
- PDF vectorial de 81 páginas listo en `333,7 ms`, `43,4 MiB` privados;
- PDF escaneado de 16 páginas y 33,33 MiB listo en `407,6 ms`,
  `184,0 MiB` privados;
- cuatro pestañas iniciales listas en `267,3 ms`, `43,3 MiB` privados: solo
  se carga la activa.

Hubo cero pausas mayores de 500 ms, cero cierres forzados, cero procesos
residuales y todos los fixtures conservaron su SHA-256. Las cifras son
calientes, no un arranque tras reinicio, y corresponden al i9-12900K/63,7 GiB
del equipo auditado sobre SSD SATA.

El cierre multipestaña quedó optimizado el mismo día. Antes, cuatro pestañas
tardaban `1,981 s` en perder la ventana y `3,438 s` en terminar el proceso. La
ruta final prepara todos los workspaces, destruye una sola vez el contenedor de
pestañas y solo después libera documentos, Recovery y leases. El resultado es
`720,5 ms` hasta perder la ventana y `2,083 s` hasta terminar: mejoras del
`63,6 %` y `39,4 %`, respectivamente. No se omite ningún `Dispose`, no se usa
`Environment.Exit` y el cierre individual conserva su ruta propia.

El binario auditado final se identifica en la sección 2; recalcular su SHA-256
después de cualquier recompilación.

### Comparación de planos

```powershell
.\build\validation-plan-comparison-engine\compile-and-run.ps1
.\build\validation-plan-comparison\compile-and-run.ps1
.\build\validation-plan-comparison-ui\compile-and-run.ps1
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass `
  -File .\build\validation-plan-comparison-viewer\run-smoke.ps1
```

Últimos informes auditados:

- engine: PASS, alineación fiable, 3.997.418 píxeles, 61 MiB estimados;
- fixture independiente: PASS, originales idénticos y pico de 57,6 MiB;
- UI: PASS en cuatro modos, responsive, plegado y tres cancelaciones;
- visor real: PASS en apertura/cierre, cobertura completa, exclusión de
  herramientas y originales intactos.

### Regresiones relevantes

```powershell
.\build\validation-organizer-ui\compile-and-run.ps1
.\build\validation-ocr\compile-and-run.ps1
.\build\validation-ocr-ui\compile-and-run.ps1
.\build\validation-ocr-stress\compile-and-run.ps1
.\build\validation-bookmarks\compile-and-run.ps1
.\build\validation-bookmarks-engine\compile-and-run.ps1
.\build\validation-bookmarks-integration\compile-and-run.ps1
.\build\validation-bookmarks-ui\compile-and-run.ps1
.\build\validation-bookmarks-viewer\compile-and-run.ps1
```

Otros bancos ya presentes:

- `build\validation-phase1\`: historial, recuperación y guardado atómico;
- `build\validation-phase1-large\`: Undo/Redo con PDF grande;
- `build\validation-rectangle-zoom\`: gesto y encuadre;
- `build\validation-paper-preview\`: tamaño y preview de impresión;
- `build\validation-architectural-ui\`: layout y escalado;
- `build\validation-insert-ui\`: inserción desde miniaturas.

Los QA de medición viven en `build\validation-measurement-engine`,
`build\validation-measurement-ui` y `build\validation-measurement-viewer`.
Crean fixtures propios; no dependen de PDFs personales ni escriben archivos
en el directorio de producción.

### Backlog confirmado tras la optimización

1. Endurecer de forma transversal el diagnóstico y la recuperación de PDFs
   protegidos, cifrados o dañados; texto y AcroForm ya aplican la política
   conservadora de la fase 8.
2. Reutilizar opcionalmente la posición de firma en lotes, normalizando el
   rectángulo por tamaño y rotación de página y permitiendo confirmar cada PDF.
3. Evaluar edición directa de objetos de texto solo para PDFs compatibles; la
   redacción real exige demostrar la eliminación de contenido oculto.
4. Hibernar visores mediante LRU solo si el uso real supera habitualmente
   8-12 documentos grandes ya visitados; con cuatro pestañas lazy la memoria
   medida ya es 43,3 MiB y no justifica ese refactor.
5. Antes de distribución externa: repositorio Git, proyecto/solución y tests,
   revisión de licencias, instalador/desinstalador único y firma Authenticode.

## 12. Checklist para la próxima fase o modificación

### Antes de tocar código

- [ ] Leer este archivo completo.
- [ ] Leer `ROADMAP_PDF_LIGERO.md`, `firma automática\README.md` y
      `firma automática\HANDOFF.md`.
- [ ] Identificar la fase, los archivos y los harnesses afectados; no asumir
      que una tarea del cierre anterior sigue pendiente.
- [ ] Revisar los cambios recientes y no sobrescribir trabajo concurrente.
- [ ] Confirmar que no hay procesos `PDFLigero` ni harnesses bloqueando salidas.
- [ ] Ejecutar `.\build.ps1 -SkipRestore` para tener una línea base.

### Durante la implementación

- [ ] Mantener C# 5.
- [ ] Usar `ContentPath` para la revisión visible.
- [ ] Conservar el original; toda mutación debe crear una revisión segura y
      recuperable antes de publicarse.
- [ ] No abrir otro `PdfiumDocument` para overlays que puedan reutilizar el
      renderer visible.
- [ ] Mantener toda interacción con `PdfRenderer` en UI.
- [ ] Cancelar y disponer correctamente al cambiar/cerrar.
- [ ] Probar la lógica de motor fuera de WinForms siempre que sea posible.
- [ ] Hacer parches pequeños en `PdfViewerForm.cs`.
- [ ] No degradar búsqueda Enter-only ni zoom neutro.

### Antes de entregar

- [ ] Build completo sin warnings nuevos relevantes.
- [ ] QA del motor afectado.
- [ ] QA de controlador/UI y smoke del visor real cuando haya integración.
- [ ] Regresiones indicadas.
- [ ] Inspección visual de capturas reales.
- [ ] Confirmar cero procesos, locks y temporales residuales.
- [ ] Confirmar que PDFs fixture conservan SHA-256, longitud y fecha.
- [ ] Actualizar documentación y marcar únicamente lo realmente probado.
- [ ] Informar la ruta exacta de `build\output\PDFLigero.exe`.

## 13. Cierre de las fases 7 y 8

La fase quedó cerrada el 30 de julio de 2026 y su mejora de escala
manual/cruceta se auditó el 4 de agosto. La medición no abre otro
`PdfiumDocument`, no crea bitmaps de página y no escribe Recovery ni el PDF.
El coste al abrir documentos sigue siendo cero porque el controlador se crea
solo al pulsar `↔`/`Ctrl+Mayús+M`; el cambio de cursor no inicia ningún
render ni temporizador.

### Registro final de medición

```text
Estado: COMPLETADA
Motor matemático: PASS
Controlador/overlay: PASS
Integración PdfViewerForm: PASS
QA motor: PASS
QA UI: PASS
QA visor real: PASS
Escala manual y bloqueo de valores inválidos: PASS
Cruceta precisa y restauración de cursor: PASS
Regresiones comparación/organizador/OCR/marcadores: PASS
Regresión zoom por rectángulo: PASS
Documentación final: ACTUALIZADA
Límites: 200 mediciones/documento; 100 vértices/medición
Persistencia: solo memoria; se descarta al recargar/cerrar
Siguiente fase en aquel cierre: edición controlada de texto (completada después)
```

### Registro final de texto y formularios

```text
Estado: COMPLETADA
Motor de reemplazo visual: PASS
Selector y diálogo: PASS 100/125/150 %
Motor AcroForm: PASS
Formulario AcroForm: PASS 100/125/150 %
Integración Recovery/Undo/Redo: PASS
Unicode, rotación y CropBox: PASS
Firmas/XFA/protección/original intacto: PASS
Regresiones medición/marcadores/organizador/zoom: PASS
Rendimiento de apertura y multipestaña: PASS
Limitación: cubierta visual, no redacción segura
Siguiente prioridad: fase 9, endurecimiento transversal y distribución
```

Consultar los informes indicados en la sección 11. Si se modifica un motor,
repetir primero su harness aislado, después su QA UI/integración y por último
las regresiones y el benchmark proporcionales al cambio.

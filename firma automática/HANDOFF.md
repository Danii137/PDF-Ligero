# Handoff

Para retomar el proyecto con Claude Code, Codex u otro agente, leer primero
`..\CONTEXTO_PDF_LIGERO.md`: contiene arquitectura, invariantes, QA, binario
auditado y el estado exacto de la siguiente fase.

## Resumen
Este proyecto implementa `PDF Ligero`, un visor y herramienta PDF para Windows construido sobre la firma automatica original.

Flujo actual:
- Abrir uno o varios PDF desde la app, arrastrarlos o usar `Abrir con PDF Ligero`
- Navegar entre documentos mediante pestanas superiores con cierre individual
- Recorrer paginas desde miniaturas virtualizadas en un panel izquierdo plegable
- Seleccionar paginas con Ctrl/Mayus, reordenarlas por arrastre, girarlas o eliminarlas
- Insertar uno o varios PDF arrastrandolos exactamente entre miniaturas; una linea roja marca la posicion
- Mantener la edicion en la misma pestana, saltar al punto insertado y marcarla con `•`
- Deshacer/rehacer por pestana con `Ctrl+Z` y `Ctrl+Y`, y recuperar tras un cierre inesperado
- Usar la barra lateral derecha compacta para abrir, buscar, ejecutar OCR, firmar, combinar y acceder a `Mas`
- Buscar palabras con `Ctrl+F`; escribir no busca hasta pulsar `Enter`
- Crear un zoom por rectangulo en modo neutro y encuadrarlo desde su centro
- Aplicar OCR local `spa+eng`, giro automatico y enderezado con revision por pagina
- Ver `pagina actual de total` y escribir un numero para saltar a esa pagina
- Visualizar marcadores existentes, imprimir o guardar una copia
- Crear, renombrar, borrar, ordenar y cambiar de nivel los marcadores
- Capturar la vista actual como destino y aplicar con Undo/Redo sin tocar el original
- Comparar revisiones de planos con `Ctrl+Mayus+C`: A/B, superposicion,
  rojo/cian, alternancia, cortinilla y alineacion fisica/automatica/manual
- Medir planos con `Ctrl+Mayus+M`: distancia, perimetro y area con escala
  impresa, escala manual o calibracion por dos puntos y cruceta precisa
- Cubrir y reemplazar texto con `T` o `Ctrl+E`, previsualizacion y formato
  basico, como revision recuperable sin tocar el original
- Rellenar campos AcroForm compatibles sin aplanarlos y deshacer/rehacer el
  resultado
- Seleccionar varios PDFs y usar `Combinar con PDF Ligero`
- Ordenar, agregar o quitar documentos y crear una copia combinada
- Seleccionar uno o varios PDFs
- Clic derecho -> `Firmar PDFs`
- Elegir certificado desde `Windows Personal` o archivo `.pfx/.p12`
- Abrir los PDFs uno a uno para colocar la zona de firma
- Hacer clic en un campo de firma detectado o dibujar un rectangulo manual
- Firmar y guardar en la misma carpeta con sufijo `_f`

Objetivo principal ya conseguido:
- Flujo visual sencillo
- Firma visible tipo Acrobat basada en PNG limpio
- Soporte de lotes de hasta `50 PDFs`
- Menu contextual funcional
- Selector de certificados mejorado
- Respeto de firmas previas ya existentes en el PDF
- Deteccion de campos de firma vacios del propio PDF
- Firma visual diferente por certificado
- Icono del platano rojo en ejecutables, ventanas y Explorador
- Combinacion segura con marcadores, orden visual y temporales
- Campos de formulario y destinos internos conservados al combinar
- Estado de pagina, zoom y busqueda independiente para cada pestana
- Carga perezosa de documentos y miniaturas para no penalizar selecciones multiples
- Cierre multipestana por lote: libera controles, documentos y Recovery de
  forma determinista sin destruir cada `TabPage` por separado
- Instancia persistente del visor: nuevas aperturas llegan a la ventana existente
- Insercion segura de PDF entre paginas, sin modificar ningun original
- Organizacion estructural de paginas con multiseleccion, giro, borrado, reordenado y Undo/Redo
- OCR transaccional con original intacto, progreso/cancelacion, preview LRU y Undo/Redo
- Runtime OCR distribuido junto a la app, sin nube y sin coste en el arranque normal
- Editor de marcadores transaccional con destinos exactos y preservacion de acciones PDF avanzadas
- Comparacion de planos de solo lectura, cargada bajo demanda y limitada a una
  pareja de paginas sin modificar los originales

## Estado actual
La app esta usable, rapida y bastante pulida.

Puntos ya resueltos:
- Menu contextual `Firmar PDFs`
- Menus contextuales `Abrir con PDF Ligero` y `Combinar con PDF Ligero`
- Coordinacion de una sola instancia con agregacion de multiples archivos
- Visor multipestana con recepcion persistente de nuevas aperturas
- Panel plegable de miniaturas y marcadores
- Barra de herramientas vertical compacta
- Certificados de `Windows Personal`
- Ocultacion de certificados caducados
- Recuerdo de la ultima eleccion de certificado
- Firma visible personalizada usando `firma_limpia.png`
- Icono del platano rojo generado en varios tamanos
- Progreso durante la firma en lote
- Confirmacion antes de sobrescribir archivos `_f.pdf`
- UI de colocacion de firma bastante mejorada respecto al inicio
- Firma en modo append para no invalidar firmas anteriores
- Seleccion directa de campos de firma detectados en el PDF
- Preview blindado para que no falle con recuadros muy grandes o raros
- Insercion de uno o varios PDF desde las miniaturas, con indicador visual de posicion
- Revision temporal validada en la misma pestana, salto al punto de insercion y original intacto
- Trabajo de insercion en segundo plano, aviso de firmas digitales y bloqueo preventivo de XFA
- Historial en disco limitado, autoguardado por operacion, cierre seguro y recuperacion
- Organizador en segundo plano que conserva enlaces, destinos, formularios, etiquetas y outlines sin rasterizar
- Zoom por rectangulo aislado de enlaces, rueda, busqueda y operaciones estructurales
- OCR de una pagina cada vez, maximo de 16 Mpx, capa invisible alineada,
  omision de texto existente, correccion manual y bloqueo de XFA
- Editor compacto de marcadores con jerarquia, destino por pagina/posicion,
  captura de la vista actual, aviso de firmas y Undo/Redo
- Superficie integrada de comparacion con selector A/B y de paginas,
  superposicion, rojo/cian, alternancia, cortinilla, opacidad y controles
  plegables; alineacion automatica y ajuste manual X/Y
- Medicion de distancias, perimetros y areas con escalas rapidas, entrada
  manual (`75`, `1:75` o decimal) o calibracion por dos puntos, unidades
  mm/cm/m, cruceta precisa y estado independiente por pagina
- Barra de medicion flotante y plegable, creada solo al invocarla, con cotas
  en memoria, borrar ultima/todas y originales intactos
- Editor visual de texto bajo demanda con Unicode embebido, autoajuste,
  alineacion/colores, cuatro rotaciones y `CropBox` desplazado
- Rellenado AcroForm bajo demanda para texto, casillas, opciones, combos y
  listas; apariencias regeneradas, campos interactivos y cambios diferenciales
- Ambos flujos usan Recovery/Undo/Redo, un unico aviso de error y mantienen el
  original intacto; XFA/protecciones incompatibles se bloquean
- Cierre de cuatro pestanas optimizado: ventana fuera en `720,5 ms` y proceso
  terminado en `2,083 s`, frente a `1,981/3,438 s` antes del cambio

## Siguiente fase

El endurecimiento transversal de la fase 9 quedó **completado el 5 de agosto de
2026**: diagnóstico único de PDFs protegidos, cifrados o dañados, mensajes
homogéneos en español, diálogo propio de contraseña y modo protegido de solo
lectura en el visor.

Lo siguiente es la parte de distribución: instalador único para Word2PDF y PDF
Ligero, actualización del motor PDFium, revisión de licencias y firma
Authenticode. Después puede añadirse la reutilización opcional de posición de
firma en lotes. La redacción/saneado real seguirá siendo un módulo opcional
hasta demostrar que elimina también contenido oculto.

El proyecto ya tiene control de versiones: `https://github.com/Danii137/PDF-Ligero`.
`firma_limpia.png` queda deliberadamente fuera del repositorio por ser una firma
manuscrita real.

## Scripts importantes
- `build.ps1`
  compila la app y copia dependencias
- `install-context-menu.ps1`
  registra el menu contextual en `HKCU`
- `generate-icon.ps1`
  genera `PDFLigero.ico` desde `assets/PDFLigero.png`
- `uninstall-legacy-context-menu.ps1`
  elimina la entrada antigua `Firmar PDF digitalmente` desde `HKLM` si se ejecuta como administrador

Comandos habituales:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1 -SkipRestore
```

```powershell
powershell -ExecutionPolicy Bypass -File .\install-context-menu.ps1
```

## Ficheros clave
- `Program.cs`
  arranque del visor, combinador o firma, control de instancia por modo, limite de 50 PDFs, logging y agregacion de invocaciones
- `ViewerInstanceBroker.cs`
  mutex y tuberia persistentes del visor; envia nuevas rutas a la ventana ya abierta sin demorar el arranque
- `PdfViewerForm.cs`
  coordinador multipestana, estado por documento, paneles laterales, pagina editable y busqueda bajo demanda con `Ctrl+F`
- `ClosablePdfTabControl.cs`
  pestanas compactas owner-drawn, cierre con `x`, truncado y desbordamiento
- `PdfThumbnailList.cs`
  miniaturas virtualizadas, render lazy, cache LRU e indicador de insercion por arrastre entre paginas
- `PdfPageInsertService.cs`
  insercion de paginas sin recomprimir, traslado de marcadores y destinos, validacion y publicacion atomica
- `PdfEditSession.cs`
  historial transaccional en disco, Undo/Redo, limites de espacio, manifiesto durable y recuperacion segura
- `PdfAtomicFileService.cs`
  guardado de copias mediante temporal junto al destino sin tocar el original
- `PdfRectangleZoomController.cs`
  seleccion ligera sobre el renderer, confirmacion central, encuadre y cancelaciones
- `PdfTextEditService.cs`
  analisis y reemplazo visual incremental, fuente Unicode embebida, coordenadas
  rotadas/CropBox, control de identidad y validacion estructural/visual
- `PdfTextEditSelectionController.cs`
  selector de una pagina con cruceta, marker propio, confirmacion central y
  limpieza completa al cancelar/cambiar revision/cerrar
- `PdfTextEditDialog.cs`
  editor y preview de texto, fuente, tamano/autoajuste, alineacion y colores
- `PdfAcroFormService.cs`
  analisis, politica conservadora, escritura append sin aplanar, valores
  canonicos, apariencias y validacion de campos/widgets; conserva metadatos
  descriptivos y XMP, admitiendo cambios tecnicos esperables de la revision
- `PdfAcroFormFillForm.cs`
  buscador/lista accesible y un unico editor dinamico para no cargar formularios
  grandes en memoria visual
- `PdfOcrService.cs`
  analisis, giro, deskew vectorial, Tesseract TSV, capa invisible y salida atomica
- `PdfOcrOptionsForm.cs`
  ambito y opciones ligeras antes de analizar
- `PdfOcrReviewForm.cs`
  revision por pagina, giro manual y preview asincrono con cache LRU de tres imagenes
- `PdfOcrProgressForm.cs`
  progreso modeless y cancelacion accesible sin bloquear navegacion ni busqueda
- `runtime/ocr`
  Tesseract 5.5 y modelos `spa`, `eng` y `osd`
- `PdfMergeForm.cs`
  ventana para agregar, quitar y ordenar PDFs, ver sus paginas, elegir salida y seguir el progreso
- `PdfMergeService.cs`
  copia de paginas, formularios, destinos y arboles raw de marcadores,
  incluidas acciones avanzadas, con validacion final y publicacion segura
- `PdfBookmarkService.cs`
  lectura y escritura incremental del arbol raw, destinos exactos, acciones
  avanzadas, control de identidad, validacion y publicacion atomica
- `PdfBookmarkEditorForm.cs`
  editor compacto de jerarquia, orden, nivel y destino de marcadores
- `PdfPlanComparisonService.cs`
  sesion PDFium bajo demanda, normalizacion fisica de una pareja de paginas,
  limites de 4 Mpx/128 MiB, alineacion y composicion A/B, overlay, rojo/cian y
  cortinilla sin escribir en los PDF
- `PdfPlanComparisonSurface.cs`
  superficie integrada con worker cancelable, selectores de fuente/pagina,
  modos visuales, opacidad, alineacion automatica/manual y controles plegables
- `PdfMeasurementModel.cs`
  modelo inmutable, calibracion, unidades y calculos estables de distancia,
  perimetro y area sin dependencia visual
- `PdfMeasurementController.cs`
  barra flotante, captura en coordenadas PDF, markers ligeros, escalas por
  pagina, entrada manual validada, cruceta precisa, cotas en memoria y ciclo
  de vida/teclado de la medicion
- `AppBranding.cs`
  aplica el icono incrustado a todas las ventanas
- `CertificateDialog.cs`
  modal de eleccion de certificado
- `UserPreferences.cs`
  persistencia simple de la ultima eleccion de certificado
- `SigningFlowController.cs`
  flujo principal, control de conflictos `_f.pdf`, progreso de firma, append mode y firmado sobre campos existentes
- `SigningProgressForm.cs`
  ventana de progreso durante el firmado
- `PdfPlacementForm.cs`
  UI para visualizar paginas, detectar campos de firma y colocar la firma
- `SignatureSelectionBox.cs`
  control de seleccion por arrastre, clic rapido y clic sobre campos detectados
- `SignatureAppearanceRenderer.cs`
  composicion de la firma visible, layout del bloque derecho y fallback seguro del preview
- `SignaturePlacement.cs`
  datos finales de la colocacion de firma, incluyendo `ExistingFieldName` cuando se reutiliza un campo nativo
- `DetectedSignatureField.cs`
  modelo interno para campos de firma vacios detectados en el PDF
- `build/output/firma_limpia.png`
  PNG base de la firma manuscrita
- `assets/PDFLigero.png`
  fuente transparente del icono del platano rojo
- `build/output/PDFLigero.ico`
  icono multitamano generado para app, ventanas y menu contextual

## Decisiones importantes que ya estan tomadas

### Firma visible
La firma visible ya no depende como fuente principal del recorte automatico de Acrobat.

Orden actual:
1. Si existe `build/output/firma_limpia.png`, se usa esa grafica
2. Si no existe, se intenta extraer la apariencia desde `appearances.acrodata`

La firma visible:
- usa el PNG limpio
- construye una tarjeta legible con bloque derecho maquetado
- ajusta jerarquia usando tipografia y negritas
- intenta adaptarse al rectangulo disponible

### Certificados
El modal:
- muestra certificados con clave privada
- oculta certificados caducados
- recuerda la ultima seleccion
- para archivo `.pfx/.p12` recuerda la ruta, no la contrasena

Persistencia:
- se guarda en `%LOCALAPPDATA%\FirmaAutomatica\preferences.ini`

### Lotes
La app limita el lote a `50 PDFs`.

Motivos:
- mantener estabilidad
- evitar tiempos excesivos
- evitar sensacion de cuelgue en flujos muy grandes

### Combinacion
El modo `--merge` usa nombres de mutex y tuberia distintos al modo `--sign`, de modo que las invocaciones repetidas del Explorador se agregan sin interferir con una firma en curso.

La salida:
- se escribe en un temporal dentro de la carpeta elegida;
- se reabre y comprueba por numero de paginas;
- se mueve o reemplaza solo despues de validar;
- nunca usa uno de los originales como destino;
- conserva los marcadores y destinos desplazando sus numeros de pagina;
- mantiene los campos AcroForm y evita colisiones de nombres entre documentos.

Combinar crea un documento nuevo. Las firmas digitales que contengan los originales no validan el archivo resultante, aunque su apariencia siga visible.

### Insercion entre miniaturas
El panel de miniaturas distingue entre dos gestos:
- soltar PDF sobre el area general de la ventana los abre como pestanas;
- soltar uno o varios PDF en el panel de miniaturas los inserta en la posicion que marca la linea roja.

Durante el segundo gesto aparece una linea roja entre las miniaturas. La
posicion puede ser antes de la primera pagina, entre dos paginas o despues de
la ultima. Los PDF arrastrados se insertan en su orden y el trabajo pesado se
hace en segundo plano para no bloquear la interfaz.

La salida:
- se construye en Recovery mediante un temporal y se valida antes de publicarla;
- nunca modifica el documento base ni ninguno de los PDF arrastrados;
- sustituye la vista de la misma pestana y muestra la primera pagina insertada;
- queda marcada como cambio sin guardar y puede deshacerse con `Ctrl+Z`.

Cada revision terminada ya funciona como autoguardado. El manifiesto se publica
con escritura durable y las revisiones completas permanecen solo en disco, no
duplicadas en RAM. Hay un maximo de 8 revisiones y un objetivo de 768 MB por
documento, conservando siempre la revision activa y su predecesora inmediata;
el limite global real es de 2 GB, con reserva adicional de espacio libre. Al
cerrar se puede guardar, descartar o cancelar; un cierre inesperado conserva la
sesion para el siguiente arranque.

El guardado visible se hace fuera del hilo de interfaz, en un temporal situado
junto al destino, con vaciado durable, comprobacion y sustitucion atomica. La
sesion temporal solo se elimina tras una decision de cierre segura. Si el
archivo guardado desaparece o cambia fuera de la aplicacion, se vuelve a pedir
guardar o descartar expresamente. La comprobacion combina metadatos con una
huella rapida de cinco muestras repartidas por el PDF y un SHA-256 completo
calculado en segundo plano antes de usar el destino o borrar la recuperacion.
Una revision owned ya guardada conserva el manifiesto de emergencia hasta un
cierre normal verificado, por lo que tambien sobrevive a un crash posterior.
Si hay varias recuperaciones del mismo PDF, solo se ofrece la mas reciente y
las anteriores no se sustituyen ni se borran.

La insercion cambia estructuralmente el documento. Si se detectan firmas
digitales, la interfaz avisa antes de empezar porque esas firmas no mantienen
su validez criptografica en la copia y sera necesario firmarla de nuevo. Los
PDF con formularios XFA se rechazan expresamente: es preferible bloquear la
operacion a producir silenciosamente un documento dañado.

### Sobrescritura
Antes de abrir los visores, si ya existe `x_f.pdf`:
- `Si` -> sustituir
- `No` -> omitir esos PDFs y seguir con el resto
- `Cancelar` -> abortar

Esto evita perder tiempo colocando firmas para archivos que luego no se quieren sobrescribir.

### Logging
`AppLog` se hizo tolerante a concurrencia entre procesos para evitar el error de acceso a `FirmaAutomatica.log`.

### PDFs ya firmados y campos de firma existentes
El firmado ahora usa `append mode` en iTextSharp.

Consecuencias:
- las firmas previas del documento no deben invalidarse por reescritura completa
- si el PDF trae un campo de firma vacio compatible, la app lo reutiliza
- si no hay campo compatible, sigue funcionando el flujo manual por rectangulo

Implementacion actual:
- `SigningFlowController.cs` usa `PdfStamper.CreateSignature(..., true)`
- si `SignaturePlacement.ExistingFieldName` viene informado, se firma con `appearance.SetVisibleSignature(fieldName)`
- la deteccion de campos se hace con `AcroFields.GetBlankSignatureNames()` y `GetFieldPositions()`

## UI actual
El visor general y la pantalla de colocacion de firma son las vistas mas retocadas.

Estado visual actual:
- pestanas de documentos arriba;
- header muy compacto con nombre y pagina editable;
- miniaturas y marcadores a la izquierda, con panel plegable y linea roja de insercion al arrastrar PDF;
- seis herramientas compactas a la derecha y un desplegable para las menos frecuentes;
- toolbar y panel de marcadores propios de PdfiumViewer ocultos;
- lenguaje visual de plano arquitectonico: blancos calidos, grafito, Bahnschrift
  Light/SemiCondensed para la jerarquia tecnica, Segoe UI Variable Text para
  controles y lectura, fallbacks seguros, lineas finas y un unico acento bermellon;
- iconos monocromos, pestana activa y pagina seleccionada identificadas con
  reglas finas, sin sombras pesadas ni componentes que afecten al rendimiento;
- cada pestana conserva posicion, zoom y resultados de busqueda;
- la colocacion de firma mantiene tarjetas centradas y campos detectados con overlay ligero.

Si se retoma el diseno en el futuro, no haria una reescritura completa.
Lo razonable seria seguir con mejoras pequenas de:
- espaciado
- pesos tipograficos
- colores
- densidad del footer

## Cosas a vigilar si se sigue

### 1. Apariencia visible de la firma
Aunque esta bastante afinada, sigue siendo la parte mas sensible del proyecto.

Si se toca:
- probar tamanos pequenos y medianos
- revisar que el texto siga cabiendo
- no reintroducir bordes o fondos raros
- no perder el PNG limpio como fuente principal

### 2. Rendimiento del visor
Al abrir varios archivos, se crean sus pestanas inmediatamente y cada documento
se carga solo al entrar en el. Cada pestana conserva un `PdfViewer` propio para
que cambiar entre documentos sea instantaneo.

Las miniaturas:
- se dibujan en un unico control virtualizado;
- renderizan una pagina por tick solo en la zona visible y cercana;
- no acceden al documento desde un hilo paralelo;
- mantienen una cache LRU de 12 imagenes por documento.

Si en el futuro se usan habitualmente mas de 8-12 documentos grandes ya
visitados, conviene anadir hibernacion LRU de visores inactivos.

El cierre total sigue un orden obligatorio: preparar todos los workspaces,
disponer una sola vez el `TabControl` padre y solo entonces liberar los
`PdfDocument`, Recovery y leases. No volver al `TabPage.Dispose()` secuencial
ni usar `Viewer.Document = null` como descarga: PdfiumViewer 2.13 mantiene la
referencia interna del renderer.

### 3. Preview de firma
Se ha blindado el preview para evitar excepciones cuando el recuadro es desproporcionadamente grande.

Comportamiento actual:
- intenta renderizar la firma completa normal
- si el layout del bloque derecho falla en algun caso raro, degrada a un fallback simple
- si aun asi el preview falla en `OnPaint`, se muestra una previsualizacion segura y se registra en log

Importante:
- esto no deberia afectar al PDF final en casos normales
- se introdujo como proteccion de consistencia de UI, no como cambio de apariencia principal

### 4. Certificados especiales
Con certificados de token, DNIe o almacen protegido puede seguir apareciendo PIN o prompts de Windows.
Eso es normal y no conviene intentar saltarselo.

### 5. Menu contextual
Actualmente la entrada activa es la nueva:
- `HKCU:\Software\Classes\SystemFileAssociations\.pdf\shell\FirmarPDFs`
- `HKCU:\Software\Classes\SystemFileAssociations\.pdf\shell\PDFLigero.Merge`

La entrada antigua `Firmar PDF digitalmente` ya no se necesita y se puede eliminar con:
- `uninstall-legacy-context-menu.ps1`

## Posibles mejoras futuras
- Edicion directa de objetos de texto solo para PDFs compatibles; mantener la
  redaccion real verificable como modulo posterior separado
- Opcion para reutilizar la misma posicion de firma en varios PDFs sin volver a marcarla en todos
- Preferencias visuales simples
  por ejemplo: tamano de firma por clic o mostrar/ocultar ciertos datos del bloque derecho
- Desinstalador completo de la herramienta
- Mejor gestion transversal de PDF protegidos, cifrados o danados
- Hibernacion LRU solo si el uso real supera habitualmente 8-12 documentos ya
  visitados; no penalizar ahora la apertura medida
- Mejor deteccion de PDFs raros donde Acrobat pinta cajas de firma pero no sean campos `/Sig` estandar

## Dependencias
Se compila con:
- `iTextSharp 5.5.13.3`
- `BouncyCastle 1.8.9`
- `PdfiumViewer 2.13.0`
- `PdfiumViewer.Native.x86_64.v8-xfa`
- `Tesseract OCR 5.5` local con modelos `spa`, `eng` y `osd`

No es un proyecto `.csproj`; se compila por script con `csc`.

## Validacion automatizada acumulada

- `build/validation-phase1/Phase1EditSessionHarness.cs`: Commit, Undo/Redo,
  ramificacion, poda protegida, recuperacion, descarte y limpieza.
- `build/validation-phase1/Phase1UiHarness.cs`: insercion real en la misma
  pestana, cinco ciclos Undo/Redo, guardado, cierre, lease del destino y prueba
  de que el dialogo de background no puede devolver antes que su worker.
- `build/validation-phase1/PdfAtomicSaveHarness.cs`: copia nueva y reemplazo,
  prefijo tolerado, SHA-256 completo y deteccion de cambios con igual tamano y
  fecha.
- `build/validation-architectural-ui/ArchitecturalUiQa.cs`: estados vacio,
  normal, compacto y escalas 125/150 %, sin solapes.
- `build/validation-phase1-large/`: PDF escaneado realista de 33,33 MiB. En
  cinco ciclos con visor y miniaturas activos, las medianas fueron 38,2 ms para
  Undo y 54,7 ms para Redo; no hubo crecimiento acumulativo y el incremento
  temporal maximo quedo alrededor de 45 MiB.
- `build/validation-rectangle-zoom/`: seleccion, control central, encuadre,
  centrado, Escape, cambio de pagina y bloqueo con herramienta activa.
- `build/validation-ocr/`: ruta con espacios y acentos, texto existente,
  giro de 270 grados, deskew de -2 grados, SHA-256 intacto y reapertura PDFium.
- `build/validation-ocr-ui/`: opciones, analisis, revision, aplicacion,
  busqueda real sobre la capa OCR y Undo/Redo.
- `build/validation-ocr-stress/`: cinco ciclos de cancelacion durante analisis
  y proceso, limpieza de temporales/procesos, SHA-256 intacto y prueba A0 bajo
  el limite de 16 Mpx.
- `build/validation-bookmarks/`: fixture independiente con jerarquia cerrada,
  destinos homonimos Name/String, GoTo con Next, SetOCGState, JavaScript,
  AcroForm, enlaces y firma RSA real.
- `build/validation-bookmarks-engine/`: crear, renombrar, borrar, ordenar,
  cambiar nivel y destino; modos Fit/XYZ, render identico, cancelacion,
  identidad SHA-256 y firma incremental.
- `build/validation-bookmarks-integration/`: preservacion raw, formularios,
  enlaces, metadata, firma verificable, Undo/Redo, cambio externo y combinacion.
- `build/validation-bookmarks-ui/` y `build/validation-bookmarks-viewer/`:
  editor compacto y recorrido real abrir -> aplicar -> Ctrl+Z/Ctrl+Y -> navegar.
- `build/validation-plan-comparison-engine/`: fixtures vectoriales, lienzo
  comun, alineacion, A/B, overlay, rojo/cian, cortinilla, cancelacion,
  Dispose, limite de 4 Mpx/128 MiB y originales intactos.
- `build/validation-plan-comparison/`: banco independiente A3/A4 con cajas y
  desplazamientos conocidos, cambios localizados, cancelacion durante render,
  SHA-256/fecha/locks intactos y pico real cercano a 80 MiB.
- `build/validation-plan-comparison-viewer/run-smoke.ps1`: abre el visor real
  con dos pestañas, comprueba el botón Delta, la cobertura total sobre
  miniaturas, el bloqueo de herramientas, el cierre por atajo/cambio/cierre de
  pestaña y la identidad intacta de ambos originales.
- `build/validation-plan-comparison-ui/`: flujo bajo demanda, cuatro modos,
  paginas vinculadas, intercambio A/B, alineacion auto/manual, responsive,
  plegado, cierre durante render y capturas ancha/compacta inspeccionadas.
- `build/validation-measurement-engine/`: distancia 3-4-5, perimetro y area,
  matriz 3 geometrías x 3 unidades, escalas, calibracion conocida,
  degenerados, formato y rangos no finitos.
- `build/validation-measurement-ui/`: barra ancha/minima, escala manual por
  `Enter`/salida de campo, validacion invalida, memoria por pagina, cruceta,
  zoom, scroll, rotacion, snapshot por cota, doble clic, borrar ultima,
  limites 200/100, `Dispose` y PDF original intacto.
- `build/validation-measurement-viewer/`: activacion lazy dentro del visor
  real, estados de herramientas, cabecera, navegacion, pestanas y cierre.
- `build/validation-content-edit-engine/`: texto Unicode real, fuente embebida,
  rotaciones 0/90/180/270, `CropBox`, append prefix, formularios, metadata/XMP,
  firma RSA, XFA, temporales e identidad del original.
- `build/validation-content-edit-ui/`: selector/confirmacion, `Esc`, preview,
  colores y layout 100/125/150 %.
- `build/validation-content-edit-viewer/`: smoke del visor real con botón/menús
  lazy, `Ctrl+E`, exclusión de herramientas, selector/T central, aplicación,
  Recovery, Undo/Redo, recreación, cierre y originales intactos.
- `build/validation-acroform/`: tipos de campo, valores canonicos, Unicode,
  apariencias, append prefix, cambios diferenciales, Recovery/Undo/Redo,
  proteccion, solo lectura y cambio externo.
- `build/validation-acroform-ui/`: fixture de 2 paginas y 11 campos, todos los
  editores y layout sin recortes/solapes a 100/125/150 %.
- `build/validation-performance/`: benchmark externo del ejecutable real con
  ventana vacia, PDF vectorial de 2/81 paginas, escaneado de 33,33 MiB y cuatro
  pestanas lazy. Informe consolidado en `PERFORMANCE_REPORT.md`; apertura,
  memoria, respuesta, cierre, SHA-256 y procesos residuales: PASS. El SHA-256
  vigente del binario se mantiene en `..\CONTEXTO_PDF_LIGERO.md`.
- `build/validation-close-performance/`: perfil por etapas que aisló la
  destrucción WinForms y justifica el cierre por lote.
- `build/validation-hardening/`: clasificación de PDFs con contraseña, permisos
  restringidos, cifrado no admitido, dañado, truncado, cifrado+truncado, PNG
  renombrado a `.pdf`, inexistente y bloqueado por otro proceso; apertura real de
  PDFs cifrados con contraseña correcta, incorrecta y de solo propietario;
  ausencia de texto inglés de PDFium o iText en lo que ve el usuario; y la
  comprobación de que tras cada fallo el archivo se puede borrar, que es la
  prueba de que no queda ningún handle abierto.
- `build/validation-hardening-viewer/`: visor real con diálogo propio de
  contraseña, aviso de contraseña incorrecta, apertura al acertar, modo protegido
  con la lista exacta de herramientas apagadas y encendidas, atajos que no tocan
  Recovery, alternancia entre pestaña protegida y normal, cancelación sin diálogo
  de error con el archivo liberado al instante y capturas a tamaño normal y
  900×620.

## Pruebas manuales recomendadas al retomar
1. Firmar un solo PDF desde clic derecho
2. Firmar 2 o 3 PDFs a la vez
3. Repetir sobre un PDF ya firmado para probar el conflicto `_f.pdf`
4. Probar certificado de `Windows Personal`
5. Probar archivo `.pfx/.p12`
6. Abrir un PDF grande para revisar tiempos de carga
7. Confirmar que el icono del menu contextual sigue viendose bien
8. Probar un PDF con firmas previas ya existentes y verificar en Acrobat que siguen validas
9. Probar un PDF con campos de firma vacios y confirmar que se pueden clicar directamente
10. Probar un recuadro de firma exageradamente grande y confirmar que el preview no rompe la UI
11. Combinar PDFs con formularios y marcadores, y comprobar orden, campos y destinos
12. Escribir una busqueda comun y confirmar que no se ejecuta hasta pulsar `Enter`
13. Abrir tres PDF a la vez y confirmar que aparecen tres pestanas pero solo se carga la activa
14. Cambiar de pestana y verificar que pagina, zoom y busqueda se conservan
15. Plegar el panel izquierdo, seleccionar miniaturas y alternar a marcadores
16. Abrir otro PDF desde el Explorador con el visor abierto y confirmar que llega como nueva pestana
17. Arrastrar un PDF antes de la primera pagina, entre dos miniaturas y despues de la ultima
18. Arrastrar varios PDF entre miniaturas y comprobar orden, linea roja, misma pestana, marca `•` y salto
19. Repetir la insercion con un PDF firmado y confirmar que se avisa sin modificar el original
20. Intentar insertar un PDF con formulario XFA y confirmar que la operacion se bloquea sin dejar una salida parcial
21. Tras insertar, comprobar `Ctrl+Z`, `Ctrl+Y`, original intacto y estado independiente por pestana
22. Simular cierre inesperado y comprobar Recuperar, Descartar y Conservar para mas tarde
23. Abrir marcadores, crear uno con la vista actual, cambiar su nivel y destino,
    aplicar, deshacer y rehacer; repetir con un PDF firmado
24. Abrir dos revisiones con `Ctrl+Mayus+C`, cambiar paginas y comprobar
    superposicion, rojo/cian, alternancia y cortinilla
25. Probar alineacion automatica, retocar X/Y manualmente, plegar los controles
    y cerrar con `Esc`; comprobar que ninguno de los originales cambia
26. Abrir medicion con `Ctrl+Mayus+M`, medir distancia/perimetro/area y cambiar
    de unidad, zoom, giro y pagina; comprobar escalas independientes y cierre
    limpio con `Esc`
27. Escribir `75`, `1:75` y `1:75,5` en el selector, pulsar `Enter` y comprobar
    que el centro de la cruceta coincide con cada punto marcado; probar tambien
    una escala incorrecta y confirmar que no deja medir hasta corregirla
28. Pulsar `Ctrl+E`, seleccionar texto en paginas normales y giradas, cambiar
    Unicode/formato/colores, aplicar y comprobar `Ctrl+Z`/`Ctrl+Y`
29. Confirmar que cancelar el editor no crea revision y que un PDF firmado
    avisa antes de abrirlo; recordar que la cubierta no es redaccion segura
30. Rellenar un AcroForm mixto, reabrirlo en otro visor y comprobar texto,
    casillas, opciones, combos/listas, apariencias y campos aun interactivos
31. Intentar texto/formulario sobre XFA, protegido y firmado/certificado y
    comprobar el bloqueo o aviso sin temporales ni cambios en el original
32. Abrir un PDF con contraseña de apertura: comprobar que el diálogo está en
    español, que una contraseña incorrecta avisa dentro de la propia ventana y
    que al acertar la cabecera muestra `DOCUMENTO PROTEGIDO`
33. Con esa pestaña activa, confirmar que texto, OCR, marcadores, organizar,
    comparar y firmar están en gris, y que buscar, medir, imprimir y guardar
    copia siguen funcionando; alternar a una pestaña normal y ver que todo
    vuelve
34. Cancelar el diálogo de contraseña y comprobar que no sale ningún error, que
    la pestaña queda con `!` y que **el archivo se puede renombrar desde el
    Explorador inmediatamente**; volver a abrirlo debe preguntar otra vez
35. Abrir un PDF dañado o truncado y comprobar que no pide contraseña y que el
    mensaje explica que el archivo no es válido o está incompleto
36. Revisar `%TEMP%\FirmaAutomatica.log` y confirmar que no aparece ninguna
    contraseña

## Ruta de salida principal
El ejecutable final esta en:
- `build/output/PDFLigero.exe`

`build/output/FirmaAutomatica.exe` se conserva como copia compatible con integraciones antiguas.

## Nota final
El proyecto ya esta en un punto muy decente.
Si se retoma en el futuro, lo importante es no perder tres cosas que costaron bastante:
- la limpieza visual de la firma manuscrita
- la simplicidad del flujo para el usuario final
- la compatibilidad con PDFs ya firmados y con campos de firma existentes

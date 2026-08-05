# Hoja de ruta de PDF Ligero

## Principios

- Apertura y navegación rápidas.
- Operaciones sencillas y visibles.
- Mantener siempre el original intacto y publicar los cambios solo en una copia.
- No modificar silenciosamente un PDF firmado: cualquier edición de contenido puede invalidar firmas digitales.
- Mantener la experiencia actual de firma mientras se moderniza el resto de la aplicación.

## Fase 0 - Base técnica

Estado: base operativa completada; modernización pendiente en fase 9.

Hecho:

- auditoría de las dos aplicaciones existentes;
- elección de C# y PDFium como base del programa PDF;
- unificación del visor y la firma en `PDFLigero.exe`;
- conservación de `FirmaAutomatica.exe` para no romper instalaciones antiguas;
- build verificado;
- batería amplia de harnesses automatizados de motor, UI, visor real,
  recuperación, regresión y rendimiento.

Pendiente:

- iniciar control de versiones;
- crear una solución/proyecto moderno que integre los harnesses existentes;
- decidir la estrategia de licencias antes de distribuir el programa fuera de un uso propio o interno;
- sustituir gradualmente los componentes antiguos sin perder la firma en modo incremental.

## Fase 1 - Visor rápido y firma por certificado

Estado: completada.

Incluye:

- ventana general de PDF;
- apertura mediante botón, argumento `--open` y arrastrar y soltar;
- visor bajo demanda con PDFium;
- búsqueda con `Ctrl+F` que solo se ejecuta al pulsar `Enter`, con resaltado y navegación anterior/siguiente;
- página actual y total visibles, con salto a una página escribiendo su número;
- marcadores existentes visibles;
- imprimir, guardar copia y zoom;
- botón para firmar el PDF abierto;
- comandos de Explorador `Abrir con PDF Ligero` y `Firmar PDFs`;
- imagen de firma asociada por huella digital de certificado;
- identidad visual del plátano rojo en ejecutables, ventanas y Explorador;
- fallback automático a la firma predeterminada si la imagen personalizada falta o está dañada.

Criterios comprobados:

- compilación completa sin errores;
- apertura y render de `sample.pdf`;
- búsqueda real `1 de 2 -> 2 de 2 -> 1 de 2` y limpieza de marcadores al cerrar;
- revisión del icono transparente en tamaños de 16 a 256 píxeles;
- ventana publicada en una mediana aproximada de 0,35 segundos en tres ejecuciones locales con el PDF de muestra;
- asociación, carga y restablecimiento de una firma PNG por huella digital.

## Fase 2 - Combinar PDFs

Estado: completada.

Incluye:

- seleccionar varios PDF;
- clic derecho -> `Combinar con PDF Ligero`;
- mostrar el orden antes de guardar;
- permitir reordenar archivos con arrastrar y soltar;
- abrir el combinador desde la barra lateral con los PDF abiertos ya añadidos;
- agregar o quitar archivos y mostrar sus paginas;
- generar un archivo mediante escritura temporal y reemplazo seguro;
- conservar los marcadores existentes con sus nuevas paginas;
- abrir automáticamente el resultado.

Criterios de aceptación:

- conserva todas las páginas en el orden elegido;
- no sobrescribe sin confirmación;
- admite nombres y rutas con espacios o caracteres españoles;
- un error en un PDF no destruye los originales;
- funciona con una selección múltiple del Explorador.

## Fase 3 - Organizador de páginas

Estado: completada.

Incluye:

- documentos múltiples en pestañas superiores con cierre individual;
- una sola instancia persistente del visor recibe nuevas aperturas del Explorador;
- carga perezosa de cada PDF al seleccionar su pestaña;
- página, zoom y búsqueda independientes por documento;
- panel izquierdo plegable;
- miniaturas virtualizadas, renderizadas bajo demanda y con caché LRU limitada;
- selector compacto entre miniaturas y marcadores;
- barra lateral derecha mínima para herramientas presentes y futuras;
- arrastrar varios PDF abre pestañas; la combinación queda como acción explícita.
- insertar uno o varios PDF exactamente entre dos miniaturas, con una línea roja que indica la posición;
- realizar la inserción en segundo plano como revisión recuperable, sin modificar los originales;
- mantener el resultado en la misma pestaña y saltar directamente al punto de inserción;
- avisar antes de editar documentos con firmas digitales y bloquear formularios XFA para evitar resultados dañados.
- deshacer y rehacer con `Ctrl+Z` y `Ctrl+Y`;
- autoguardar cada operación terminada, recuperar tras fallo y confirmar al cerrar;
- limitar el historial tanto por revisiones como por espacio en disco.
- quitar, girar y reordenar páginas;
- deshacer y rehacer mientras la ventana siga abierta.
- zoom por rectángulo en el modo neutro: arrastrar sobre la hoja, confirmar
  desde el centro y encuadrar la selección sin ocupar espacio de interfaz.

Criterios comprobados:

- documentos largos no cargan todas las miniaturas a resolución completa;
- el orden visual coincide exactamente con el PDF guardado;
- se conservan tamaños y rotaciones de página;
- las páginas no seleccionadas no se recomprimen innecesariamente.
- el zoom por rectángulo no intercepta la rueda, los enlaces ni los clics
  normales, y se cancela con `Esc`, cambio de página o cambio de pestaña.

## Fase 4 - OCR, giro automático y enderezado

Estado: completada.

Incluye:

- OCR local en español e inglés;
- detección de páginas que ya tienen texto para no reprocesarlas;
- orientación automática de 90, 180 o 270 grados;
- corrección de inclinación pequeña;
- vista previa por página antes de aplicar;
- capa de texto invisible alineada con la imagen;
- opción de procesar solo páginas seleccionadas.

Criterios comprobados:

- texto buscable y copiable;
- la capa OCR coincide visualmente con la página;
- no reduce de forma innecesaria la resolución;
- permite corregir manualmente una orientación equivocada;
- mantiene siempre el original.
- el motor español/inglés y el modelo de orientación se distribuyen dentro de
  la aplicación, funcionan sin nube y solo se cargan al usar OCR;
- las páginas con texto útil se omiten por defecto;
- PDFium renderiza una sola página cada vez y limita cada imagen OCR a
  16 millones de píxeles;
- el giro de 90/180/270 grados y el enderezado leve transforman el contenido
  PDF existente sin convertir el documento entero en imágenes;
- la revisión previa mantiene como máximo tres imágenes en caché;
- cancelar elimina los temporales y la revisión terminada entra en el mismo
  historial recuperable de `Ctrl+Z` / `Ctrl+Y`;
- prueba real: tres páginas, dos escaneadas y una vectorial, 774-795 palabras
  reconocidas, giro de 270 grados, corrección de -2 grados, ruta con espacios y
  acentos, original idéntico por SHA-256 y resultado reabierto con PDFium.
- cinco ciclos consecutivos de análisis, OCR y cancelación dejaron cero
  procesos, carpetas nuevas o salidas parciales; una lámina A0 se mantuvo bajo
  el límite de 16 millones de píxeles y conservó exactamente su apariencia.

## Fase 5 - Marcadores

Estado: completada.

Incluye:

- crear, renombrar y borrar marcadores;
- cambiar su nivel y reordenarlos;
- apuntarlos a una página y posición vertical exactas;
- capturar la página y posición que se están viendo;
- conservar el estado abierto o cerrado de cada rama;
- navegar desde el panel izquierdo respetando los destinos del PDF;
- aplicar cada edición como una revisión recuperable, con `Ctrl+Z` y
  `Ctrl+Y`, sin sobrescribir el original;
- conservar acciones avanzadas, destinos nombrados, formularios, enlaces,
  metadatos y contenido de página;
- trasladar árboles completos y acciones avanzadas al combinar PDFs.

Criterios comprobados:

- árbol anidado con creación, renombrado, borrado, orden y cambio de nivel;
- destinos de página y posición comprobados tras guardar y reabrir;
- modos nativos `XYZ`, ajustar página, ancho, alto y rectángulo conservados,
  incluidas sus coordenadas opcionales;
- acciones `GoTo`, `SetOCGState` y JavaScript conservadas sin convertirlas a
  un modelo simplificado;
- formularios AcroForm, enlaces, metadatos y render vectorial sin cambios;
- edición incremental de un PDF firmado: la firma previa sigue incrustada y
  verificable, y se avisa de que la edición es posterior;
- cancelación sin salida parcial ni temporales residuales;
- cambio externo del archivo detectado por SHA-256 completo antes de guardar,
  incluso con el mismo tamaño y fecha;
- flujo real del visor con aplicar, deshacer, rehacer y navegar desde el árbol.

## Fase 6 - Comparación y superposición de planos

Estado: completada.

Incluye:

- abrir la comparación desde `Más -> Comparar revisiones…` o
  `Ctrl+Mayús+C`;
- elegir de forma explícita el PDF y la página A y B;
- ver solo A, solo B, alternarlas, superponerlas con opacidad regulable,
  compararlas en rojo/cian o mover una cortinilla;
- normalizar ambas páginas en un mismo lienzo físico, incluso si sus cajas o
  tamaños de papel son distintos;
- alineación por caja de página, sugerencia automática de desplazamiento y
  ajuste manual ligero de X e Y;
- mantener la comparación dentro de la ventana principal y permitir plegar
  sus controles para dedicar el espacio al plano;
- cargar PDFium y renderizar únicamente la pareja de páginas solicitada;
- no escribir nunca sobre ninguno de los PDF comparados.

Criterios comprobados:

- máximo predeterminado de cuatro millones de píxeles por página y 128 MiB de
  memoria de trabajo estimada;
- en los modos normales solo se conservan los dos bitmaps normalizados; la
  composición rojo/cian se crea al entrar en Diferencias y se libera al salir,
  sin volver a abrir ni renderizar los PDF;
- las llamadas de render y `Dispose` se serializan para no usar en paralelo
  los mismos manejadores de PDFium;
- cancelación antes de abrir y durante una comparación grande sin salida
  parcial;
- alineación automática y ajustes manuales comprobados con planos vectoriales
  A3/A4, cajas distintas y desplazamientos conocidos;
- originales idénticos por SHA-256, longitud y fecha, y desbloqueados después
  de cerrar la sesión;
- pico real del proceso de prueba alrededor de 80 MiB.

## Fase 7 - Medición calibrada de planos

Estado: completada.

Incluye:

- abrir la herramienta desde la barra compacta, `Más -> Medir plano…` o
  `Ctrl+Mayús+M`;
- medir distancias, perímetros y áreas directamente sobre la vista PDF;
- usar escalas rápidas `1:1`, `1:20`, `1:50`, `1:100` y `1:200`, o calibrar
  con dos puntos y una distancia real conocida;
- escribir cualquier escala positiva directamente como `75`, `1:75`,
  `75,5` o `75.5`, confirmándola con `Enter`;
- mostrar resultados en `mm`, `cm` o `m`;
- mantener calibración independiente por página y conservar la escala con la
  que se creó cada medición;
- borrar la última medición de la página o limpiar todas;
- plegar la barra flotante sin ocultar cotas;
- marcar con una cruceta de centro exacto mientras la herramienta está activa
  y restaurar el cursor normal al cerrarla;
- conservar zoom, desplazamiento, rotación y navegación entre páginas;
- guardar geometrías solo en memoria, sin escribir ni rasterizar el PDF.

Criterios comprobados:

- cálculo aislado de distancia, perímetro y área con dobles y casos límite;
- escalas y calibración conocida reproducibles;
- doble clic sin vértices ni mediciones fantasma;
- coordenadas estables al rotar, ampliar y desplazar la página;
- máximo de 200 mediciones por documento y 100 vértices por geometría;
- controlador creado únicamente al invocar la herramienta, sin coste en la
  apertura normal;
- limpieza de barra, filtros y marcadores verificada al cerrar pestañas y la
  ventana;
- invalidación al recargar una revisión auditada en la ruta
  `ApplyRevisionToWorkspace`;
- originales idénticos por SHA-256, longitud y fecha.

## Fase 8 - Edición de texto y formularios — completada

La edición arbitraria del contenido interno de cualquier PDF no es equivalente
a editar un documento Word. La primera entrega, cerrada el 4 de agosto de 2026,
implementa una edición visual controlada:

- herramienta compacta `T` y acceso por `Ctrl+E`;
- selección precisa de una zona en la página visible y confirmación central;
- precarga del texto estático encontrado dentro de la selección;
- cubierta opcional del contenido anterior y texto Unicode nuevo;
- fuente sans/serif/mono, tamaño manual o autoajuste, alineación y colores;
- previsualización antes de aplicar;
- coordenadas correctas con `CropBox` desplazado y giros 0/90/180/270 grados;
- revisión recuperable en la misma pestaña, con `Ctrl+Z`/`Ctrl+Y` y original
  intacto;
- aviso previo para documentos firmados y bloqueo claro de XFA o permisos
  incompatibles.

También se añadió rellenado de formularios AcroForm sin aplanarlos: texto,
multilínea, contraseña enmascarada, casillas, botones de opción, combos y listas
simples o múltiples. Los campos de firma, botones, solo lectura y tipos no
compatibles se muestran como informativos. XFA, documentos firmados/certificados
y derechos de uso ampliados de Adobe se bloquean conservadoramente en este
flujo.

La cubierta visual **no es una redacción segura**: el contenido anterior puede
seguir existiendo dentro del PDF. La eliminación verificable de contenido y la
edición directa de objetos de texto quedan como módulos futuros, solo para PDFs
compatibles y después de demostrar que no dejan datos ocultos.

## Fase 9 - Endurecimiento y distribución

Estado: endurecimiento transversal **completado** el 5 de agosto de 2026. Queda
la parte de distribución.

Hecho:

- recuperación tras cierre inesperado;
- **tratamiento homogéneo de PDFs protegidos, cifrados o dañados en toda la
  aplicación**;
- diagnóstico único que traduce cualquier fallo de PDFium o de iText a una causa
  y un texto en español, con un consejo de qué hacer;
- diálogo propio de contraseña de apertura, en lugar del formulario en inglés de
  la librería;
- modo protegido de solo lectura: el documento se ve, se busca, se mide, se
  imprime y se guarda copia, pero las herramientas que editan quedan apagadas con
  una explicación, en vez de fallar una a una;
- apertura de PDFium centralizada, que además dejó de bloquear el archivo cuando
  la carga falla;
- fin de los descartes en silencio: el CLI y la ventana de combinar explican qué
  archivo no pudieron abrir y por qué;
- XFA bloqueado también al editar marcadores, que era el último flujo estructural
  donde podía generarse una copia dañada sin avisar;
- control de versiones iniciado y publicado en GitHub;
- pruebas de regresión mantenidas: 16 harnesses de motor y UI, cuatro smoke del
  visor real, OCR completo y benchmark de rendimiento;
- herramientas pesadas siguen bajo demanda; la memoria medida es idéntica a la
  de la fase 8 en los cinco escenarios del benchmark.

Distribución, cerrada el 5 de agosto de 2026:

- **instalador único** para Word2PDF y PDF Ligero: `instalar.bat` registra las
  dos herramientas, instala lo que encuentra y avisa de lo que falta en vez de
  abortar. `desinstalar.bat` hace lo contrario. Los cuatro `.bat` anteriores se
  conservan como lanzadores para no romper accesos directos;
- **revisión de licencias** completa en `LICENCIAS.md`, con la atribución que
  debe acompañar a una copia en `THIRD-PARTY-NOTICES.md`;
- el ejecutable ya se identifica: producto, empresa, versión 1.0.0 y el aviso de
  copyright que pide la AGPL (`firma automática/AssemblyInfo.cs`);
- **firma Authenticode** preparada y probada en `firmar-ejecutables.ps1`, con
  sellado de tiempo y verificación.

Pendiente, con el motivo:

- **actualizar PDFium: bloqueado, no aplazado.** El paquete nativo que usa el
  proyecto (`PdfiumViewer.Native.x86_64.v8-xfa` 2018.4.8.256) es el último que
  existe, y `PdfiumViewer` 2.13.0 también está abandonado. Una compilación
  moderna de PDFium no exporta dos funciones que el wrapper sí llama
  (`FPDF_Release` y `FPDFDest_GetPageIndex`, esta última renombrada upstream a
  `FPDFDest_GetDestPageIndex`), así que romperían la descarga de la librería, la
  navegación por marcadores y los enlaces de página. Adoptarla exige bifurcar
  PdfiumViewer, que es Apache 2.0. Para evaluar cualquier candidato hay una
  herramienta: `firma automática/build/validation-pdfium-compat/`;
- **ejecutar la firma Authenticode**: hace falta un certificado de firma de
  código, que hay que comprar. El del equipo es de firma de documentos y no
  sirve;
- **elegir la licencia del repositorio**: es la decisión que queda abierta en
  `LICENCIAS.md`.

## Siguiente entrega

De la fase 9 solo queda lo que depende de una compra o una decisión: el
certificado de firma de código y la licencia del repositorio. La actualización
de PDFium es un proyecto aparte, con el análisis ya hecho.

Como mejora funcional independiente queda reutilizar opcionalmente una posición
normalizada de firma en lotes, confirmando cada PDF antes de firmarlo.

La redacción real y el saneado continúan siendo opcionales. No se publicarán
hasta demostrar que eliminan contenido y datos ocultos en lugar de limitarse a
taparlos visualmente.

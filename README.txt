GUIA DE INSTALACION - WORD2PDF
================================

PDF LIGERO
----------
La carpeta "firma automática" contiene ahora la aplicación PDF Ligero.

Funciones disponibles:
- Visor PDF rápido.
- Varios PDF en una misma ventana, cada uno en su pestaña.
- Apertura múltiple por botón o arrastrar y soltar.
- Miniaturas de todas las páginas en un panel izquierdo plegable.
- Editor de marcadores: crear, renombrar, borrar, ordenar, cambiar de nivel y
  asignar página/posición; cada cambio admite Ctrl+Z y Ctrl+Y.
- Inserción de uno o varios PDF arrastrándolos exactamente entre dos miniaturas.
- Barra lateral mínima de herramientas con iconos y ayudas.
- Página actual editable y estado de página, zoom y búsqueda conservado por pestaña.
- Búsqueda con Ctrl+F; solo empieza al pulsar Enter.
- Edición visual controlada de texto con T o Ctrl+E: selección rectangular,
  previsualización, fuente, tamaño, alineación y colores, con Ctrl+Z/Ctrl+Y.
- Rellenado de formularios PDF AcroForm sin aplanarlos; los formularios XFA o
  protegidos incompatibles se bloquean de forma segura.
- OCR local español + inglés con orientación automática, enderezado leve y
  revisión por página; el original permanece intacto.
- Zoom por rectángulo sin activar herramientas: arrastra sobre la hoja y pulsa
  dentro del marco para encuadrarlo.
- Combinación rápida de varios PDF, con orden antes de guardar.
- Comparación de revisiones con el botón Delta o Ctrl+Mayús+C: superposición,
  rojo/cian, alternancia A/B, cortinilla y alineación automática o manual.
- Medición de distancia, perímetro y área con Ctrl+Mayús+M, escalas rápidas,
  escala escrita a mano o calibración por dos puntos y unidades mm/cm/m; una
  cruceta precisa marca el punto y las cotas solo viven en memoria.
- Apertura, combinación y firma desde el menú contextual.
- Las nuevas invocaciones de "Abrir con PDF Ligero" llegan a la ventana ya abierta.
- Firma visual diferente para cada certificado.
- Icono del plátano rojo en la aplicación y la integración de Windows.

Para insertar páginas en el PDF abierto, arrastra uno o varios archivos PDF
desde el Explorador hasta el espacio exacto entre dos miniaturas. Una línea
roja muestra dónde quedarán las páginas. La operación se realiza en segundo
plano, crea una revisión recuperable en la misma pestaña y salta al punto de
inserción; los archivos originales no se modifican.
Si se detectan firmas digitales, el programa avisa antes de continuar porque
la copia editada no conserva su validez. La inserción de formularios XFA se
bloquea para evitar generar un PDF dañado.

La edición de texto cubre visualmente la zona anterior, pero no elimina datos
ocultos y no sirve como redacción confidencial. Tanto esta edición como el
rellenado compatible de AcroForm crean una revisión recuperable y mantienen el
original intacto.

Ejecutable:
"firma automática\build\output\PDFLigero.exe"

Para registrar "Abrir con PDF Ligero", "Combinar con PDF Ligero" y "Firmar PDFs":
1. Ejecuta "install_pdf_ligero.bat".
2. Si quieres abrir siempre los PDF con esta aplicación, usa una vez:
   Abrir con -> PDF Ligero -> Siempre.

Las fases siguientes están descritas en "ROADMAP_PDF_LIGERO.md".
Para continuar el desarrollo con otro agente, leer primero
"CONTEXTO_PDF_LIGERO.md".

REQUISITOS
----------
- PDF Ligero: Windows; no necesita Microsoft Word.
- Word2PDF: Windows y Microsoft Word instalado.

INSTALACION
-----------
1. Copia la carpeta completa "Word2PDF_Installer" a una ubicacion fija.
   Tambien puede estar en una carpeta compartida del servidor si todos tienen acceso de lectura.
2. Comprueba que dentro esta el archivo "Word2PDF.exe".
3. Ejecuta "install.bat".
4. No muevas ni renombres la carpeta despues de instalar.
   Si lo instalas desde el servidor, usa siempre la misma ruta compartida.

USO
---
1. Selecciona uno o varios archivos .doc, .docx o .rtf.
2. Haz clic derecho.
3. Pulsa "Convertir a PDF".
4. El PDF se genera en la misma carpeta que el Word.

NOTAS
-----
- El instalador registra el menu contextual para .doc, .docx y .rtf.
- Si seleccionas varios archivos, Windows lanza varias llamadas y la app las agrupa automaticamente.
- Si un archivo falla, revisa el log en el Escritorio: "word2pdf_log.txt".
- La conversion usa Microsoft Word directamente, por eso sirve tambien para RTF.
- El lote maximo por ejecucion es 50 archivos. Se convierten de uno en uno para no saturar el equipo.
- Al lanzar una conversion por clic derecho solo se muestra una consola de progreso y resumen.

DESINSTALACION
--------------
1. Ejecuta "uninstall.bat".

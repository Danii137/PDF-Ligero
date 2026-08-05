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
- PDFs con contraseña: se pide en español y el documento se abre en modo
  protegido, de solo lectura.
- Cualquier problema al abrir un PDF (cifrado, dañado, en uso o desaparecido) se
  explica en español y con qué hacer.

PDFS PROTEGIDOS
---------------
Si un PDF pide contraseña de apertura, PDF Ligero la solicita en su propia
ventana. No la guarda ni la envía a ningún sitio.

Al acertarla el documento se abre en modo protegido: se puede ver, buscar,
medir, imprimir y guardar una copia, pero las herramientas que modifican el PDF
quedan desactivadas con una explicación. Es deliberado: esas herramientas abren
el documento por su cuenta y no conocen la contraseña, así que antes fallaban
una a una con mensajes en inglés.

Cancelar no es un error. La pestaña queda marcada con "!" y basta volver a abrir
el archivo para que pregunte otra vez.

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

El código está en https://github.com/Danii137/PDF-Ligero. La imagen de firma
manuscrita "firma_limpia.png" no se publica: cada instalación aporta la suya en
"firma automática\build\output\".

REQUISITOS
----------
- PDF Ligero: Windows; no necesita Microsoft Word.
- Word2PDF: Windows y Microsoft Word instalado.

INSTALACION
-----------
1. Copia la carpeta completa "Word2PDF_Installer" a una ubicacion fija.
   Tambien puede estar en una carpeta compartida del servidor si todos tienen acceso de lectura.
2. Ejecuta "instalar.bat".
   Registra las dos herramientas de una vez: Word2PDF y PDF Ligero. Instala lo
   que encuentre y avisa de lo que falte, en vez de detenerse.
3. No muevas ni renombres la carpeta despues de instalar.
   Si lo instalas desde el servidor, usa siempre la misma ruta compartida.

Si solo quieres una de las dos:
   "install_pdf_ligero.bat"  registra solo PDF Ligero.
   "instalar.bat" y "install.bat" registran ambas.

PDF Ligero necesita estar compilado antes ("firma automática\build.ps1").
Word2PDF necesita Microsoft Word instalado en cada equipo.

USO
---
1. Selecciona uno o varios archivos .doc, .docx o .rtf.
2. Haz clic derecho.
3. Pulsa "Convertir a PDF con PDF Ligero" (con el icono del platano rojo).
4. El PDF se genera en la misma carpeta que el Word.

Las dos herramientas comparten icono y nomenclatura a proposito: en un PDF veras
"Abrir con PDF Ligero", "Combinar con PDF Ligero" y "Firmar PDFs", y en un Word
"Convertir a PDF con PDF Ligero". Es la misma familia.

Si alguna vez recompilas Word2PDF, usa "compilar-word2pdf.ps1": genera el icono
desde el mismo PNG que PDF Ligero, lo incrusta en el ejecutable y comprueba una
conversion real antes de sustituir el anterior, del que deja copia .bak.

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
1. Ejecuta "desinstalar.bat" (retira las dos herramientas).
   "uninstall_pdf_ligero.bat" retira solo PDF Ligero.
   No se borra ningun archivo: solo se deshace el registro del menu contextual.

LICENCIA
--------
PDF Ligero y Word2PDF se distribuyen bajo la Licencia Publica General Affero de
GNU, version 3 (AGPL v3). El texto completo esta en "LICENSE".

El motivo es que el programa incorpora iTextSharp, que es AGPL. El codigo fuente
esta publicado en https://github.com/Danii137/PDF-Ligero, que es lo que la
licencia exige a cambio de poder distribuir el programa libremente.

El detalle esta en "LICENCIAS.md" y la atribucion de terceros en
"THIRD-PARTY-NOTICES.md".

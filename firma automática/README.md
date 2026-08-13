# PDF Ligero

Aplicación de escritorio para Windows construida a partir de la herramienta de firma automática.

Se distribuye bajo **AGPL v3**: ver `..\LICENSE`, `..\LICENCIAS.md` y
`..\THIRD-PARTY-NOTICES.md`.

La aplicación ya permite:

- abrir uno o varios PDF rápidamente en pestañas dentro de la misma ventana;
- conservar la página, el zoom y la búsqueda de cada pestaña al cambiar de documento;
- recibir en la ventana existente los PDF abiertos después desde el Explorador;
- abrir varios PDF a la vez con el botón o arrastrándolos a la ventana;
- recorrer miniaturas virtualizadas desde un panel izquierdo plegable;
- seleccionar varias páginas y reordenarlas, girarlas o eliminarlas desde las
  miniaturas;
- insertar uno o varios PDF arrastrándolos exactamente entre dos miniaturas;
- deshacer y rehacer ediciones con `Ctrl+Z` y `Ctrl+Y`;
- recuperar automáticamente una edición tras un cierre inesperado;
- alternar entre miniaturas y los marcadores existentes;
- crear, renombrar, borrar, ordenar y cambiar de nivel los marcadores;
- llevar un marcador a una página y posición exactas o capturar la vista
  actual, con `Ctrl+Z` y `Ctrl+Y` para deshacer o rehacer;
- comparar dos revisiones de planos en la misma ventana mediante
  superposición, rojo/cian, alternancia o cortinilla;
- medir distancias, perímetros y áreas con escala impresa, escala escrita a
  mano o calibración por dos puntos, sin modificar el PDF;
- usar una barra lateral derecha mínima con iconos y ayudas;
- ejecutar OCR local en español e inglés, con orientación automática,
  enderezado leve y revisión visual por página;
- omitir automáticamente las páginas que ya contienen texto buscable;
- aplicar OCR solo a la página actual, a la selección de miniaturas o al
  documento completo;
- cancelar el OCR con `Esc`, desde su ventana de progreso o pulsando de nuevo
  el botón OCR;
- ampliar una zona sin activar herramientas: arrastra un rectángulo sobre la
  hoja y pulsa dentro o en su control central;
- trabajar con una interfaz sobria de inspiración arquitectónica: papel cálido,
  grafito, líneas técnicas y acento bermellón;
- imprimir, guardar una copia, ajustar el ancho, ampliar y girar la vista desde `Más`;
- buscar texto con `Ctrl+F`, navegar con `Enter` y `Mayús+Enter` y cerrar con `Esc`;
- cubrir y reemplazar visualmente texto con `T` o `Ctrl+E`, con fuente,
  tamaño, alineación, colores y previsualización;
- rellenar formularios PDF AcroForm sin aplanarlos;
- lanzar la firma del PDF abierto;
- combinar varios PDFs en el orden elegido, sin modificar los originales;
- firmar uno o varios PDF desde el menú contextual;
- asociar una imagen de firma diferente a cada certificado;
- conservar el flujo visual y el guardado con sufijo `_f`;
- abrir PDFs protegidos con contraseña y explicar en español, siempre, qué pasa
  cuando un documento está cifrado, dañado, en uso o ya no está.

## PDFs protegidos, cifrados o dañados

Si un PDF pide contraseña de apertura, PDF Ligero la solicita en una ventana
propia. La contraseña no se guarda ni se envía a ningún sitio.

Al acertarla, el documento se abre en **modo protegido**: la cabecera muestra
`DOCUMENTO PROTEGIDO` y se puede ver, navegar, buscar, medir, imprimir y guardar
una copia con normalidad. Las herramientas que modifican el PDF —texto y
formularios, OCR, organizar páginas, marcadores, comparar y firmar— aparecen
desactivadas con una explicación, porque cada una abre el documento por su
cuenta y no conoce esa contraseña.

Cancelar la ventana de contraseña no es un error: la pestaña queda marcada con
`!` y basta volver a abrir el archivo para que se pregunte otra vez.

Cuando un PDF no se puede abrir, el motivo se explica siempre en español —
cifrado no admitido, archivo dañado o incompleto, en uso por otro programa, sin
permisos o ya no está en su sitio— junto con qué se puede hacer al respecto.

Un PDF que solo tiene contraseña de propietario se abre sin preguntar nada, como
en cualquier visor. Si sus permisos impiden una edición segura, la herramienta
correspondiente lo dirá al invocarla.

## Construcción

```powershell
.\build.ps1
```

La salida principal es:

```text
build\output\PDFLigero.exe
```

`firma_limpia.png` no está en el repositorio: es una firma manuscrita real y
cada instalación debe aportar la suya en `build\output\`. Sin ella, la firma
visible usa su camino de reserva.

También se genera `FirmaAutomatica.exe` como copia compatible con instalaciones anteriores.

El plátano rojo de `assets\PDFLigero.png` es el icono del ejecutable, las ventanas, la barra de tareas, `Abrir con` y los comandos del Explorador.

## Búsqueda

Con un PDF abierto:

- `Ctrl+F`: abrir y enfocar la búsqueda;
- escribir no inicia ninguna búsqueda;
- `Enter`: ejecutar la búsqueda; si el texto no ha cambiado, ir a la coincidencia siguiente;
- `Mayús+Enter`: coincidencia anterior;
- `Esc`: cerrar la búsqueda y retirar los resaltados.

La barra muestra la coincidencia actual y el total encontrado.

Cada pestaña conserva su propia consulta, coincidencia activa y posición. Al
escribir, la aplicación no busca ni recorre el documento: el trabajo empieza
únicamente al pulsar `Enter`.

## Editar el texto directamente sobre la página

Pulsa el botón `Editar` de la barra derecha o `Ctrl+Mayús+E`. Las líneas de texto
se recuadran y basta pinchar una para escribir encima, como en cualquier editor
de PDF conocido.

Al pinchar aparece el texto de esa línea listo para cambiarlo y una barra
flotante con **fuente, tamaño, negrita, cursiva y color**. `Enter` o `Aplicar`
guardan el cambio; `Esc` o `Cancelar` lo dejan como estaba. Pinchar fuera
también aplica, como en un procesador de textos.

El texto anterior se elimina del documento, no se tapa. Si solo cambias las
palabras y la fuente incrustada las cubre, se sustituye conservando esa misma
fuente. Si cambias el tamaño, el color o la tipografía, o escribes caracteres
que la fuente incrustada no trae, se reescribe con esa fuente instalada en
Windows.

Cada cambio crea una revisión, así que `Ctrl+Z` deshace. El original no se toca.

Se trabaja **línea a línea** a propósito: una línea se corresponde con un grupo
concreto de instrucciones del PDF, que es lo que se puede sustituir sin riesgo.
Un párrafo entero obligaría a recomponer el reparto entre líneas, y el formato
PDF no lo hace solo.

## Editar texto y rellenar formularios

Pulsa `T` en la barra derecha para elegir una de las dos acciones. `Ctrl+E`
inicia directamente la selección para reemplazar texto.

Para reemplazar texto visualmente:

1. elige `Cubrir y reemplazar texto…`;
2. arrastra una zona sobre una sola página y pulsa la `T` de su centro;
3. revisa el texto detectado y la tipografía que se ha reconocido;
4. pulsa `Aplicar` para crear una revisión en la misma pestaña.

**La tipografía del original se reconoce sola.** Al seleccionar una zona se lee
la fuente que tiene realmente ese texto, con su tamaño, su color y si va en
negrita o cursiva, y el diálogo llega con todo eso puesto: no hay que acertarlo
a ojo. Se indica encima del desplegable, por ejemplo `Detectado: Calibri 11 pt`.
Se puede cambiar a mano si interesa.

La fuente incrustada en el PDF no sirve para escribir, porque los documentos de
Word la incrustan en subconjuntos que solo traen las letras ya usadas y una
letra nueva saldría en blanco. Por eso se busca esa misma fuente instalada en
Windows; si no está o no cubre algún carácter, se recurre a una genérica y el
texto se escribe igualmente.

**Sustituir el texto de verdad.** La casilla `Sustituir el texto original`
elimina el texto anterior del documento en vez de taparlo: deja de poder
extraerse, copiarse o buscarse. Solo aparece disponible cuando la zona lo
admite; si no, el consejo de la casilla explica por qué. Cuando está activa, el
aviso del pie cambia para decir lo que va a pasar de verdad.

Hay dos formas de hacerlo, y se elige sola:

- si la fuente incrustada en el PDF trae todos los caracteres nuevos, se
  sustituye la cadena tal cual y no cambia nada más;
- si le falta alguno —los documentos de Word incrustan subconjuntos, y es
  frecuente que no tengan los dígitos que no usabas—, se borra el texto anterior
  y el nuevo se escribe con esa misma fuente instalada en Windows, en el mismo
  sitio y tamaño.

Al sustituir, el documento se reescribe entero en lugar de añadir una revisión
incremental, porque es la única forma de que el cambio del contenido llegue al
archivo. El original sigue sin tocarse y `Ctrl+Z` funciona igual.

Sustituir no es lo mismo que un saneado completo: quita ese texto, pero no
audita el resto del documento en busca de otros rastros.

La operación entiende páginas giradas y cajas de página desplazadas, incrusta
la fuente necesaria para texto Unicode y entra en el historial normal de
`Ctrl+Z`/`Ctrl+Y`. El original permanece intacto. La cubierta es solo visual:
no elimina el texto ni otros datos que puedan existir debajo y, por tanto, no
debe usarse para ocultar información confidencial. Si ninguna fuente
incrustable de Windows cubre todos los caracteres solicitados, la operación se
bloquea en vez de guardar símbolos vacíos.

`Rellenar formulario PDF…` abre los campos interactivos AcroForm en una lista
buscable. Admite texto, multilínea, contraseña enmascarada, casillas, opciones,
combos y listas; guarda solo los cambios y mantiene los campos editables, sin
aplanarlos. Los botones, firmas y campos de solo lectura se muestran como
informativos. Los formularios XFA, los PDF ya firmados o certificados y los
documentos cuya protección no permite una modificación segura se bloquean con
una explicación. Un campo de
contraseña se oculta mientras se escribe, pero su valor no queda cifrado por ese
hecho dentro del PDF. Los scripts y cálculos se conservan, pero PDF Ligero no
los ejecuta.

## Anotar: rotulador, subrayador y notas

Pulsa el rotulador de la barra derecha o `Ctrl+Mayús+A`. Aparece una barra
flotante sobre la página con las tres herramientas, seis colores y tres
grosores.

- **Rotulador**: arrastra para dibujar a mano alzada sobre la página, como si
  rodearas una zona de un plano con un boli.
- **Subrayador**: arrastra sobre un texto para marcarlo en color translúcido,
  que deja leer lo de debajo.
- **Nota**: haz clic donde quieras anclarla y escribe el comentario.

Las marcas se acumulan mientras trabajas y se escriben todas de una vez al
pulsar `Guardar marcas`; el botón indica cuántas hay. `Deshacer` quita la
última. Si intentas cerrar la herramienta con marcas sin guardar, se avisa antes
de perderlas. Al guardar se crea una revisión normal, así que `Ctrl+Z` también
funciona.

Se guardan como **anotaciones PDF estándar**, no dibujadas sobre el contenido:
se ven en Acrobat, en el móvil y en cualquier otro visor, el contenido original
no se toca y quien reciba el documento puede borrarlas. Los PDF cifrados y los
que llevan formularios XFA se bloquean, y si el documento está firmado
digitalmente se avisa de que la firma dejará de ser válida.

Dentro de PDF Ligero las marcas las dibuja la propia aplicación, no el motor de
renderizado. El motivo está medido y anotado en
`build/validation-annotations/README.md`: la versión de PDFium que usa el
proyecto no dibuja anotaciones, y está congelada porque es la última compatible
con el envoltorio. El efecto práctico es bueno: se ven exactamente igual
mientras se dibujan y después de guardar.

## Zoom por rectángulo

Cuando no hay ninguna herramienta activa y la búsqueda está cerrada:

1. arrastra sobre la zona de la página que quieres revisar;
2. suelta para conservar el marco;
3. pulsa dentro del marco o en el pequeño control de su centro.

El visor amplía y centra esa zona. `Esc`, un clic fuera, cambiar de página,
cambiar de pestaña o recargar una revisión cancelan el marco. La rueda, los
enlaces y los clics normales conservan su comportamiento.

## Marcadores

Abre los marcadores con la estrella del panel izquierdo. El pequeño lápiz de
esa misma cabecera abre el editor. También puedes usar
`Más -> Editar marcadores…` o `Ctrl+Mayús+B`.

- `Insert` o `Ctrl+N`: crear;
- doble clic o `F2`: renombrar;
- `Supr`: borrar;
- `Alt+↑` / `Alt+↓`: reordenar;
- `Alt+←` / `Alt+→`: cambiar de nivel;
- `Usar vista actual`: asignar la página y posición visibles;
- `Aplicar`: crear una revisión segura en la misma pestaña.

El editor también permite escribir la página y una posición vertical exacta.
Cancelar no modifica nada. Aplicar conserva el original y entra en el historial
normal: `Ctrl+Z` deshace y `Ctrl+Y` rehace.

Las páginas no se rasterizan ni se recomprimen. El motor conserva acciones
avanzadas de marcadores, destinos nombrados y los modos `XYZ`, ajustar página,
ancho, alto o rectángulo, además de formularios, enlaces y metadatos.
En un PDF firmado se avisa antes de continuar: la firma anterior permanece
incrustada, pero la edición consta como una modificación posterior.

## Comparar revisiones de planos

Abre `Más -> Comparar revisiones…` o pulsa `Ctrl+Mayús+C`. La página visible
queda como revisión A; puedes elegir otra pestaña, abrir otro PDF o arrastrarlo
como revisión B. Cada lado tiene su selector de página.

La superficie ofrece:

- `Superponer`, con opacidad regulable;
- `Cambios`, donde lo exclusivo de A aparece rojo y lo nuevo de B, cian;
- `Alternar`, manualmente o con reproducción;
- `Cortinilla`, para desplazar la separación entre A y B;
- alineación física por cajas de página;
- alineación automática de desplazamientos pequeños;
- corrección manual de X/Y en milímetros, también arrastrando el plano.

La alineación automática calcula y aplica el desplazamiento cuando encuentra
una coincidencia fiable; siempre puede restablecerse o corregirse manualmente.
Las teclas `1` a `4` cambian de modo y `Espacio` alterna A/B cuando está activo
ese modo.
Los controles se pliegan para dejar prácticamente toda la ventana al plano y
`Esc` vuelve al visor normal.

La comparación es de solo lectura. Los PDF originales no se guardan,
reescriben ni rasterizan. El motor se carga únicamente al abrir esta
herramienta, renderiza una sola pareja de páginas y limita cada página a cuatro
millones de píxeles con un presupuesto predeterminado de 128 MiB. Cambiar entre
superposición, alternancia y cortinilla reutiliza los dos renders ya cargados,
por lo que no penaliza el arranque ni la navegación normal.

## Medir planos

Abre la herramienta con el icono `↔`, desde
`Más -> Medir plano…` o con `Ctrl+Mayús+M`.

1. Elige la escala impresa `1:1`, `1:20`, `1:50`, `1:100` o `1:200`, o
   escríbela directamente en el selector: por ejemplo `75`, `1:75` o
   `1:75,5`, y pulsa `Enter`. Si la escala impresa no es fiable, selecciona
   `Calibrar…`, marca dos puntos y escribe su distancia real.
2. Elige distancia `↔`, perímetro o área y la unidad `mm`, `cm` o `m`.
3. Marca los puntos con el centro exacto de la cruceta `+`. La distancia
   termina al segundo punto; perímetros y áreas terminan con `Enter` o doble
   clic.

`Retroceso` elimina el último vértice y `Esc` cancela el trazado actual; con
otro `Esc` se cierra la herramienta. El botón `↶` borra la última medición de
la página y `⌫` limpia todas las del documento. La barra puede plegarse.

No se presupone una escala al abrir: hay que escribirla, elegirla o calibrarla.
Una escala manual incorrecta se marca y bloquea la captura hasta corregirla o
cancelarla con `Esc`. Cada página recuerda su calibración y cada cota conserva
la escala con la que se creó.
Las geometrías se guardan únicamente en memoria; al recargar una revisión o
cerrar la pestaña se descartan. El controlador se crea bajo demanda sobre el
visor existente, sin abrir otro PDF ni penalizar el arranque normal.

## OCR, orientación y enderezado

Pulsa `OCR` en la barra derecha o abre `Más -> OCR y enderezado…`.

- Elige página actual, páginas seleccionadas o todo el documento.
- Por defecto se omiten páginas que ya contienen texto.
- El análisis propone giros de 90/180/270 grados y un enderezado pequeño.
- La revisión muestra cada página antes de aplicar. Puedes excluirla, girarla
  manualmente o desactivar su enderezado.
- El trabajo se realiza página a página. Puedes seguir navegando, buscando,
  cambiando de pestaña y usando el zoom.
- Durante el procesamiento, `Esc` o el botón cuadrado de la barra derecha
  cancelan y limpian los temporales.
- Al terminar, el resultado sustituye la vista de la misma pestaña como una
  revisión recuperable: `Ctrl+Z` deshace y `Ctrl+Y` rehace.

El original nunca se sobrescribe. Los PDF con XFA se bloquean; si existen
firmas digitales, se avisa porque la nueva revisión no conserva su validez
criptográfica. Tesseract, los idiomas `spa+eng` y la orientación `osd` se
incluyen en `build\output\ocr`: no se utiliza ningún servicio en la nube ni se
envía el documento fuera del equipo. El motor solo arranca al pulsar OCR, por
lo que no penaliza la apertura normal de archivos.

## Pestañas, miniaturas y herramientas

- La tipografía y la jerarquía visual siguen el lenguaje de una lámina técnica,
  con Bahnschrift Light/SemiCondensed en títulos, pestañas y numeración, y
  Segoe UI Variable Text en controles; sin sombras pesadas ni elementos
  decorativos que ralenticen el visor.
- `Ctrl+O`: abre uno o varios PDF como pestañas.
- `Ctrl+W`: cierra la pestaña activa.
- El botón `×` de cada pestaña cierra solo ese documento.
- El panel izquierdo muestra páginas en miniatura y se pliega con `‹`.
- Los botones pequeños de la cabecera izquierda alternan páginas y marcadores.
- La barra derecha reúne Abrir, Buscar, Firmar, Combinar y `Más`.
- Soltar varios PDF abre varias pestañas; combinar es siempre una acción
  explícita para evitar crear un archivo por accidente.

Los documentos que todavía no se han visitado se cargan al seleccionar su
pestaña. Las miniaturas se generan solo para las páginas visibles y usan una
caché limitada.

## Insertar PDF entre páginas

Con un documento abierto, arrastra uno o varios PDF desde el Explorador hasta
el espacio exacto entre dos miniaturas del panel izquierdo. Una línea roja
indica la posición final antes de soltar. También puedes soltar antes de la
primera página o después de la última.

La inserción conserva el orden de los archivos arrastrados y se ejecuta en
segundo plano. El resultado sustituye la vista de la misma pestaña, salta a la
primera página insertada y queda marcado con `•` como cambio sin guardar. El
PDF original y los documentos arrastrados permanecen intactos.

Si se detectan firmas digitales, se muestra un aviso antes de continuar: su
apariencia puede seguir visible, pero la copia editada no mantiene la validez
criptográfica y deberá firmarse de nuevo. Los documentos con formularios XFA
se bloquean en este flujo para evitar crear una copia dañada.

## Organizar páginas

- `Ctrl+clic` selecciona páginas sueltas y `Mayús+clic` selecciona un intervalo.
- `Ctrl+A`, con el panel de miniaturas activo, selecciona todas las páginas.
- Arrastrar la selección dentro del panel cambia su posición.
- `Supr` elimina las páginas seleccionadas; el menú contextual permite además
  girarlas 90 grados a izquierda o derecha.
- Cada operación se ejecuta en segundo plano, mantiene intacto el PDF original
  y se puede deshacer o rehacer.

El motor conserva páginas, formularios, destinos, enlaces, etiquetas y
marcadores sin rasterizarlos. Cuando un PDF contiene una acción avanzada que no
se puede reconstruir de forma segura tras borrar páginas, la operación se
bloquea antes de crear la copia; reordenar y girar siguen disponibles.

## Deshacer, autoguardado y recuperación

- `Ctrl+Z`: vuelve a la revisión anterior del PDF activo.
- `Ctrl+Y` o `Ctrl+Mayús+Z`: rehace la revisión.
- El historial pertenece a cada pestaña; cambiar de PDF no mezcla estados.
- Cada operación terminada ya es su propio autoguardado en disco. No se conserva
  otro PDF completo en memoria ni se ejecuta ningún guardado periódico.
- Al cerrar una pestaña modificada se puede guardar una copia, descartar o
  cancelar.
- La copia se escribe en segundo plano mediante un temporal junto al destino y
  solo se publica cuando se ha vaciado y comprobado correctamente.
- Si la copia guardada se mueve, se elimina o cambia fuera de PDF Ligero antes
  de cerrar, una comprobación rápida y un SHA-256 completo lo detectan aunque
  conserve tamaño y fecha; la revisión temporal no se borra y se pide guardarla
  de nuevo o descartarla expresamente. La comprobación completa se hace en
  segundo plano y solo antes de usar o eliminar la recuperación.
- Tras un cierre inesperado, el siguiente inicio permite recuperar, descartar o
  conservar la edición para más tarde.
- Si existen varias recuperaciones del mismo PDF, se ofrece primero únicamente
  la más reciente y las anteriores permanecen intactas.

Las revisiones temporales se guardan bajo
`%LOCALAPPDATA%\PDFLigero\Recovery`, con un máximo de 8 revisiones y 768 MB por
documento como objetivo; la revisión activa y la inmediatamente anterior se
conservan siempre. El límite global sí es de 2 GB y se mantiene además una
reserva de espacio libre. Los archivos se crean únicamente al editar: abrir y
leer PDFs sigue usando la carga perezosa de siempre. Los originales nunca se
sobrescriben. Incluso una revisión ya guardada conserva su pequeño manifiesto
de emergencia hasta completar un cierre normal y comprobado.

## Combinar PDFs

Selecciona dos o mas PDFs en el Explorador, pulsa el boton derecho y elige:

```text
Combinar con PDF Ligero
```

La ventana de combinacion permite agregar o quitar archivos, ver el numero de paginas, reordenar con `Subir`/`Bajar` o arrastrando las filas y elegir el archivo de salida. El programa escribe primero un temporal en la carpeta de destino, comprueba el numero de paginas y solo entonces publica el resultado. Los PDF de origen no se modifican y los marcadores existentes se trasladan al combinado.

Un combinado es un documento nuevo. La apariencia de una firma digital anterior puede seguir visible, pero esa firma deja de validar el nuevo archivo.

## Integración con Windows

```powershell
.\install-context-menu.ps1
```

Esto registra:

- `Abrir con PDF Ligero`;
- `Combinar con PDF Ligero`;
- `Firmar PDFs`;
- `PDF Ligero` dentro de la lista `Abrir con`.

Para usar el programa con doble clic, elige una vez:

```text
Abrir con -> PDF Ligero -> Siempre
```

## Firma distinta para cada certificado

Al entrar en `Firmar PDF`, selecciona el certificado y usa `Elegir imagen...` dentro de `Firma visual asociada al certificado`.

La imagen se convierte a PNG y se copia a:

```text
%LOCALAPPDATA%\FirmaAutomatica\signatures
```

La asociación se hace mediante la huella digital del certificado. `Predeterminada` elimina esa asociación y vuelve a usar `firma_limpia.png`. Nunca se guarda la contraseña de un PFX/P12.

## Desinstalación de la integración

```powershell
.\unregister-context-menu.ps1
```

Consulta `..\ROADMAP_PDF_LIGERO.md` para el endurecimiento y las mejoras
siguientes.

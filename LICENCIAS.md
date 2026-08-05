# Revisión de licencias

Auditoría del 5 de agosto de 2026, dentro de la fase 9. La atribución que debe
acompañar a una copia distribuida está en
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md); este documento analiza qué
obliga cada licencia y qué queda por decidir.

## Lo esencial en un párrafo

PDF Ligero enlaza **iTextSharp 5, que es AGPL v3**. Eso significa que, en cuanto
el programa se distribuya fuera de la organización, el conjunto debe publicarse
bajo AGPL v3 con su código fuente, o bien hay que comprar una licencia comercial
de iText. No hay una tercera vía: no existe una versión de iTextSharp 5 con
licencia permisiva.

**Uso interno en AGOIN: sin obligaciones.** La AGPL se activa al distribuir el
programa a terceros o al ofrecerlo a través de una red. Usarlo en los equipos de
la propia organización no dispara nada.

## Qué exige exactamente la AGPL, y cómo está el proyecto

| Requisito | Estado |
|---|---|
| Publicar el código fuente del conjunto al distribuirlo | El repositorio es público: `https://github.com/Danii137/PDF-Ligero` |
| Declarar la licencia del conjunto | **Pendiente**: el repositorio no tiene archivo `LICENSE`. Ver más abajo |
| §7(b): conservar la línea `Producer` de iText en cada PDF creado o manipulado | **Cumplido y verificado en código**: `IsExpectedProducerTransition` en `firma automática/PdfTextEditService.cs` valida que el `Producer` se conserve o pase a `original + "; modified using " + versión de iText` |
| §5: mostrar los avisos legales en la interfaz de versiones modificadas | **Cumplido parcialmente**: desde esta entrega el ejecutable declara producto, empresa y copiado con la mención a la AGPL y a iText (`firma automática/AssemblyInfo.cs`). Falta un «Acerca de» visible en la propia ventana |
| Conservar los avisos de copyright de terceros | Cumplido con `THIRD-PARTY-NOTICES.md` |

## La decisión que queda pendiente

El repositorio es público y contiene una obra derivada de software AGPL, pero no
declara licencia. Sin un archivo `LICENSE`, por defecto se entiende «todos los
derechos reservados», lo que es incoherente con haberlo publicado. Hay que
elegir:

**Opción A — Publicar bajo AGPL v3.** Es la coherente con lo que ya se ha hecho.
Coste: cero. Consecuencia: cualquiera puede usar, modificar y redistribuir el
código, siempre que mantenga la AGPL. Para una herramienta interna de estudio no
suele ser un problema.

**Opción B — Comprar una licencia comercial de iText.** Permite distribuir sin
publicar el código y sin la AGPL. Coste: la tarifa de iText Group NV
(`sales@itextpdf.com`). Tiene sentido si el programa se va a vender o entregar a
clientes como producto cerrado.

**Opción C — Dejarlo en uso estrictamente interno.** No distribuir el binario
fuera de la organización y hacer el repositorio privado. No hace falta hacer
nada más, pero cierra la puerta a compartirlo.

Mientras no se decida, lo prudente es no entregar el ejecutable a terceros.

## El resto de dependencias no plantea problemas

- **Bouncy Castle** (MIT adaptada), **PdfiumViewer** (Apache 2.0), **PDFium**
  (BSD 3-Clause) y **Tesseract** (Apache 2.0) son permisivas: basta con
  conservar su atribución, que es lo que hace `THIRD-PARTY-NOTICES.md`.
- El **runtime OCR** incluye bibliotecas LGPL. Se distribuyen como DLL
  independientes y sustituibles, que es la forma en que la LGPL permite
  enlazarlas desde un programa con otra licencia. El runtime de GCC lleva la
  excepción que evita que su GPL contamine el programa.
- Si alguna vez se distribuye externamente, conviene una auditoría DLL a DLL del
  runtime OCR. Aquí se han identificado las familias de licencia, no cada uno de
  los 55 archivos.

## Word2PDF

`Word2PDF.py` usa **PyQt5**, que es GPL v3 o comercial. La misma lógica que con
iText: distribuir el conversor obliga a publicarlo bajo GPL o a licenciar PyQt5.

Como PDF Ligero ya arrastra la AGPL, publicar el conjunto bajo AGPL v3 resuelve
también este caso: la AGPL v3 es compatible con la GPL v3.

Word2PDF automatiza Microsoft Word por COM pero no lo incluye. La licencia de
Word corre por cuenta de quien lo use.

## Antes de distribuir, revisar

- [ ] Elegir entre las opciones A, B y C y añadir el archivo `LICENSE` que
      corresponda.
- [ ] Añadir un «Acerca de» en la ventana con la licencia y los avisos, para
      cerrar del todo la sección 5 de la AGPL.
- [ ] Firmar los ejecutables con Authenticode: ver `firmar-ejecutables.ps1`.
- [ ] Auditoría DLL a DLL del runtime OCR si la distribución es externa.

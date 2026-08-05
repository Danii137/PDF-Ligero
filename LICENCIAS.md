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
| Declarar la licencia del conjunto | **Cumplido**: [`LICENSE`](LICENSE) con la AGPL v3 |
| §7(b): conservar la línea `Producer` de iText en cada PDF creado o manipulado | **Cumplido y verificado en código**: `IsExpectedProducerTransition` en `firma automática/PdfTextEditService.cs` valida que el `Producer` se conserve o pase a `original + "; modified using " + versión de iText` |
| §5: mostrar los avisos legales en la interfaz de versiones modificadas | **Cumplido parcialmente**: desde esta entrega el ejecutable declara producto, empresa y copiado con la mención a la AGPL y a iText (`firma automática/AssemblyInfo.cs`). Falta un «Acerca de» visible en la propia ventana |
| Conservar los avisos de copyright de terceros | Cumplido con `THIRD-PARTY-NOTICES.md` |

## La decisión tomada

**El proyecto se publica bajo AGPL v3**, decidido el 5 de agosto de 2026. El
archivo [`LICENSE`](LICENSE) contiene el texto oficial de la licencia precedido
de la cabecera de copyright.

Es la opción coherente con tener el repositorio público: el código fuente ya
está disponible, que es justo lo que la AGPL exige a cambio de poder distribuir
el programa.

Qué implica en la práctica:

- se puede entregar el ejecutable a quien sea, dentro y fuera de la
  organización, sin pagar nada a iText;
- quien reciba el programa tiene derecho a su código fuente, que está publicado;
- quien lo modifique y lo distribuya debe mantener la AGPL y publicar sus
  cambios;
- si en el futuro se quisiera vender como producto cerrado, haría falta una
  licencia comercial de iText Group NV (`sales@itextpdf.com`) y rehacer esta
  decisión.

Las alternativas que se descartaron fueron comprar la licencia comercial de
iText, que solo tiene sentido para distribuir el programa cerrado, y dejarlo en
uso estrictamente interno con el repositorio privado.

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

- [x] Elegir la licencia del conjunto y añadir el archivo `LICENSE`.
- [ ] Añadir un «Acerca de» en la ventana con la licencia y los avisos, para
      cerrar del todo la sección 5 de la AGPL. El ejecutable ya lleva el aviso
      en sus metadatos, pero no se ve desde la interfaz.
- [ ] Firmar los ejecutables con Authenticode: ver `firmar-ejecutables.ps1`.
      Hace falta comprar un certificado de firma de código.
- [ ] Auditoría DLL a DLL del runtime OCR si la distribución es externa.

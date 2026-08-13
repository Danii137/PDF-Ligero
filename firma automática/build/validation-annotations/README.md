# QA de la capa de anotación

Comprueba que el rotulador, el subrayador y las notas se guardan como
anotaciones PDF de verdad y que la aplicación puede recuperarlas.

```powershell
.\comprobar.ps1
```

Devuelve 0 si todo pasa.

## Qué cubre

1. Las marcas se acumulan en memoria y se escriben de una sola vez.
2. El PDF de origen no se toca.
3. La página declara las tres anotaciones, con los subtipos correctos
   (`/Ink`, `/Highlight`, `/Text`), autor, fecha y texto.
4. Al leerlas de vuelta se recuperan íntegras: puntos del trazo, color y texto
   de la nota.

## PDFium no dibuja anotaciones

Este es el hallazgo que condiciona el diseño, y conviene no olvidarlo.

El motor con el que la aplicación pinta las páginas —PDFium, a través de
PdfiumViewer— **no dibuja las anotaciones**, aunque se le pase
`PdfRenderFlags.Annotations`. Se comprobó a fondo:

- la bandera llega bien: `FlagsToFPDFFlags` solo limpia dos bits propios de la
  librería (`Transparent` y `CorrectFromDpi`), y deja pasar `Annotations`;
- las anotaciones se escriben con su apariencia (`/AP`) y con la bandera de
  impresión (`/F 4`), verificado leyendo el archivo resultante;
- se probaron las dos convenciones de caja de apariencia, con `BBox` en
  coordenadas de página y con `BBox [0 0 w h]`;
- se probó escribiéndolas en una revisión incremental y reescribiendo el
  documento entero.

En los cuatro casos el resultado renderizado es idéntico al original: cero
píxeles de diferencia. Y esa versión de PDFium está congelada a propósito (ver
`ROADMAP_PDF_LIGERO.md`): la última compilación compatible con el envoltorio es
de 2018.

Por eso el diseño es el que es: las marcas se guardan como **anotaciones
estándar**, de modo que se ven en Acrobat, en el móvil y en cualquier otro
visor, y quien reciba el PDF puede borrarlas; pero **dentro de PDF Ligero las
dibuja la propia aplicación**, leyéndolas del documento y pintándolas sobre la
página. Como efecto secundario bueno, se ven exactamente igual mientras se
dibujan y después de guardar.

Si algún día se actualiza PDFium, esta comprobación es el sitio donde volver a
medirlo: si empezara a dibujarlas, habría que dejar de pintarlas dos veces.

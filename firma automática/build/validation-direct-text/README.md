# QA de la edición directa de texto

Comprueba lo único que distingue de verdad esta función de cubrir y escribir
encima: que **el texto antiguo desaparece del documento**, no que quede tapado.

```powershell
# hace falta el PDF de muestra de la QA de tipografía
..\validation-text-style-probe\crear-muestras.ps1
.\comprobar.ps1
```

Devuelve 0 si todo pasa. Va por `PdfTextEditService.Save`, el camino real, para
que se ejecute también la validación posterior que protege el resto del
documento.

## Qué cubre

1. El análisis acierta el modo según lo que se quiera escribir.
2. Con la fuente incrustada del propio PDF: el texto nuevo aparece y el antiguo
   ya no se puede extraer.
3. Con caracteres que esa fuente no trae: se usa la del sistema y el texto
   antiguo también desaparece.
4. Las demás páginas conservan su texto exacto y el PDF de origen no se toca.

## Los dos modos, y por qué hay dos

Las fuentes que incrusta Word son **subconjuntos**: solo llevan los glifos que
el documento ya usaba. En el PDF de muestra, la fuente del cuerpo de texto no
tiene los dígitos `0` ni `2` a `9`. Escribir con ella «47.900» dejaría huecos en
blanco.

| Modo | Cuándo | Qué hace |
|---|---|---|
| `InPlace` | La fuente incrustada cubre todo el texto nuevo | Sustituye la cadena en el flujo. No cambia nada más |
| `RewriteWithSystemFont` | Falta algún glifo | Borra el texto del flujo y lo reescribe con la misma fuente instalada en Windows, en el mismo sitio y tamaño |

Los dos borran el texto anterior de verdad, que es lo que se buscaba.

## Cómo se localiza el texto que hay que tocar

El flujo de contenido se recorre dos veces: una con el procesador de iText, que
dice qué texto dibuja cada fragmento y dónde, y otra troceando el flujo en
operadores. Ambos recorridos visitan los fragmentos en el mismo orden, así que
se casan por índice.

`viabilidad.ps1` y `ViabilidadQa.cs` son las dos pruebas que se hicieron antes de
escribir nada, y siguen valiendo para comprobar los supuestos:

- `viabilidad.ps1` — enseña, fuente a fuente, qué glifos trae y si se puede
  volver a codificar texto con ella. Sirve para entender por qué un documento
  concreto no admite la sustitución en el sitio.
- `ViabilidadQa.cs` — comprueba que los dos recorridos cuentan los mismos
  fragmentos. **Si esto dejara de cumplirse, el emparejamiento por índice no
  sería fiable** y habría que revisar el reescritor.

En tiempo de ejecución ese mismo recuento se rehace y, si no coincide —porque el
texto venga dentro de un objeto reutilizado, por ejemplo—, la operación se
rechaza con una explicación en vez de arriesgarse a dañar el documento.

## Por qué se reescribe el documento entero

El resto de la aplicación escribe revisiones incrementales, que son menos
invasivas. Aquí no se puede: se comprobó que iText **no recoge el cambio del
flujo de contenido en modo incremental** —el archivo sale con el texto viejo—,
así que la sustitución reescribe el documento completo. Es lo que ya hacía
`PdfPageOrganizerService` por un motivo parecido.

Cubrir y escribir encima sigue yendo en incremental; solo cambia el modo cuando
se pide sustituir.

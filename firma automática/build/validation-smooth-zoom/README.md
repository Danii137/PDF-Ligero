# Zoom con la rueda sin tirones

Mide, sobre un PDF real que se pasa como argumento, lo que se siente al hacer
Ctrl+rueda: cuánto tarda en responder cada muesca, cuánto dura el peor
fotograma de la animación, cuánto cuesta el render nítido al soltar y si lo que
había bajo el puntero sigue ahí.

## Cómo funciona el zoom suave

Mientras se gira la rueda **no se rasteriza nada**. Se toma una foto de lo que
hay en pantalla y se reescala alrededor del puntero, animada. Cuando la rueda
para (220 ms), se aplica el aumento de verdad una sola vez y se cambia la foto
por las hojas nítidas. Durante el gesto el visor no se toca: eso permite, al
terminar, preguntarle qué punto del PDF había bajo el puntero y llevarlo al
sitio exacto donde lo dejó la animación.

## Qué comprueba

- Cada muesca responde en **menos de 16 ms** (un fotograma a 60 Hz).
- **Ningún fotograma** de la animación pasa de 50 ms.
- Al soltar, el punto bajo el puntero se ha movido **3 px como mucho**.
- La escala final es la pedida (×1,2 por muesca).
- Alejar las mismas muescas **vuelve a la escala de partida**, también anclado.
- La foto que toma el gesto no sale en blanco.
- La rueda que PdfiumViewer manda **directa a la ventana del visor** acaba en
  el zoom suave, no en el suyo.
- Sobre la hoja se ve la **flecha**, no la mano.

## Trampas que costaron encontrar

1. **La capa, dentro del visor, lo desplazaba.** El visor es un control con
   desplazamiento: al meterle una hija en (0,0) se movía solo para dejarla a la
   vista. El zoom aterrizaba lejos del puntero y el visor repintaba por debajo
   con un render de 300 ms. La capa va en el contenedor, como hermana.
2. **El zoom se aplicaba dos veces.** Aplicarlo hace aparecer una barra de
   desplazamiento, eso cambia el tamaño del visor y volvía a pedir que se
   terminara el gesto dentro del propio cierre: 106 % en vez de 88 %.
3. **GDI+ era demasiado lento para animar.** Reescalar la ventana con
   `DrawImage` costaba 66 ms por fotograma; los fotogramas se amontonaban y la
   rueda dejaba de atenderse. `StretchBlt` con HALFTONE hace lo mismo en 2,5 ms.
4. **Tras aplicar, la vista se corría 13 px** al aparecer la barra horizontal.
   El anclaje hace una segunda pasada.
5. **Con el ratón de verdad, el zoom suave no se ejecutaba nunca.** PdfiumViewer
   instala su propio filtro de rueda al crear cada visor —antes que el
   nuestro, así que va primero—: mira qué hay bajo el ratón real y, si es el
   visor, le manda la rueda directamente y se la come. Hacía zoom el de
   PdfiumViewer, que no mira el puntero: «el zoom se va al centro». Las pruebas
   no lo veían porque mandaban la rueda con el ratón fuera de la ventana.
   Ahora se escucha también en la ventana del visor.
6. **`PointToPdf` se equivoca unos 2,4 px** (`PointFromPdf` es exacto). El
   anclaje tomaba el punto con uno y lo recolocaba con el otro: cada gesto
   corría el plano; seis muescas sueltas, 37 px. Se invierte `PointFromPdf`
   con tres esquinas de la hoja, y entre gestos seguidos se conserva el mismo
   punto del PDF para no perder la fracción de píxel.
7. **Parar y arrancar un `Timer` de WinForms costaba 21 ms** (destruye y crea
   su ventana interna), y se hacía en cada muesca. Un solo temporizador dura
   todo el gesto y mira cuánto lleva quieta la rueda.
8. **El visor no deja la hoja donde se quiera.** Si la hoja cabe de ancho, la
   centra; si cabe de alto, la pega arriba; y nunca desplaza más allá del
   borde de la hoja. La animación ya respeta esos límites, para no prometer un
   sitio que al soltar no se puede dar, y el punto al que se apuntaba se
   recuerda: en cuanto la hoja crece lo bastante, vuelve bajo el puntero.

## Lo que no se puede quitar

El render nítido al soltar (~0,5-1 s con planos A1 muy ampliados). El visor
pinta cada hoja entera en una imagen y no acepta una más pequeña para estirarla
—se probó—, y reescalar a esos tamaños cuesta lo mismo que rasterizar. Se hace
una sola vez, con la imagen final ya en pantalla, así que no hay saltos.

## Ejecución

```powershell
Set-Location -LiteralPath '...\firma automática\build\validation-smooth-zoom'
.\compile-and-run.ps1 -PdfReal 'D:\ruta\a\unos planos.pdf'
```

## Resultado de referencia

```text
5 muescas de Ctrl+rueda:
  ANTES  cada muesca congelaba 199 ms   total 995 ms
  AHORA  cada muesca responde en 0.0 ms   peor fotograma 27 ms   render final 766 ms
  Foto inicial del visor: 25 ms   escala final 88 %   punto bajo el puntero: error 1 px
  Alejar las mismas muescas: escala 35.5 % (partida 35.5 %)   error bajo el puntero 1 px
  Rueda enviada directa al visor (como hace PdfiumViewer): la atiende el zoom suave
  Cursor sobre la hoja: flecha normal
PASS: el zoom con la rueda es fluido y aterriza donde debe.
```

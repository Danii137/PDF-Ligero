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

## Cuatro trampas que costaron encontrar

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
  ANTES  cada muesca congelaba 172 ms   total 859 ms
  AHORA  cada muesca responde en 10.5 ms   peor fotograma 24 ms   render final 645 ms
  Foto inicial del visor: 21 ms   escala final 88 %   punto bajo el puntero: error 0 px
  Alejar las mismas muescas: escala 35.5 % (partida 35.5 %)   error bajo el puntero 2 px
PASS: el zoom con la rueda es fluido y aterriza donde debe.
```

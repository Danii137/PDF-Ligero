# Fluidez y navegación del visor

Mide lo que cuesta un repintado del visor real al navegar y al hacer zoom, con
y sin la capa que guarda las páginas ya rasterizadas. Sin números, «va más
fluido» es una opinión.

## De dónde sale el problema

Medido sobre un juego de planos real (30 hojas A1/A2/A0, 10 MB):

- rasterizar una hoja cuesta entre **220 y 350 ms**;
- `PdfRenderer` guarda la imagen de cada página, pero la tira en cuanto cambia
  el zoom o entra una página nueva, y la vuelve a pedir **en el hilo de la
  interfaz**.

De ahí venían los tirones: 300 ms cada vez que se pasaba de página y 620 ms en
un paso de zoom con dos hojas a la vista.

## Qué comprueba

- **Pasar de página** con una pausa realista de lectura: es donde entra la
  preparación de las hojas de al lado. Se exige al menos ×3.
- **Acercar y alejar**: aquí **no se promete velocidad**. A mucho aumento el
  coste es montar una imagen de varios megapíxeles y eso no se quita. Lo que se
  exige es no empeorar.
- **El anclaje del zoom**, que es el arreglo de verdad del zoom: partiendo de
  la hoja 12 y acercando cuatro pasos, sin anclar acabas en la **hoja 19**;
  anclado te quedas en la 12.

Admite un PDF real como argumento; sin él se fabrica un juego de planos
sintético con mucho trazo.

## Tres intentos fallidos, por si vuelve la tentación

1. **Guardar un maestro al doble de resolución** y servir los zooms
   escalándolo. Sale peor: rasterizar al doble cuesta cuatro veces más, y cada
   página nueva lo paga.
2. **Reescalar con bicúbica de calidad** para el apaño instantáneo. A tamaños
   grandes cuesta más que rasterizar de nuevo (455 ms medidos en un A0 muy
   ampliado). Se usa bilineal, y por encima de 2,5 Mpx no se usa apaño.
3. **Medir contando el refinado de fondo** dentro del cronómetro. Eso no es lo
   que se siente: la página ya está a la vista y nadie espera mirando.

## Ejecución

```powershell
Set-Location -LiteralPath '...\firma automática\build\validation-viewer-fluidity'
.\compile-and-run.ps1 -PdfReal 'D:\ruta\a\unos planos.pdf'
```

## Resultado de referencia

```text
SIN cache:  pagina 305 ms   acercar 383 ms   alejar 372 ms
CON cache:  pagina  44 ms   acercar 366 ms   alejar 350 ms

  Pasar de pagina              305 ms        44 ms    x7,0 mas rapido
  Acercar                      383 ms       366 ms    x1,0 mas rapido
  Alejar                       372 ms       350 ms    x1,1 mas rapido

  Anclaje del zoom: partiendo de la hoja 12 y acercando 4 pasos
      ->  sin anclar: hoja 19   anclado: hoja 12
```

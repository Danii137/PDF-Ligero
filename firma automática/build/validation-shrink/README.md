# Reducir el tamaño de un PDF

El caso real: un plano escaneado a 300 ppp que no cabe en un correo.

`PdfShrinkService` baja la resolución de las imágenes que van muy por encima
de lo que se va a ver, y aprieta la estructura del archivo. Lo vectorial —el
texto, las líneas del plano— se copia tal cual.

## Qué comprueba

- **Escaneo grande**: dos páginas A4 con imagen a 300 ppp, reducidas a 150.
  Baja el tamaño, se rehacen las imágenes, la resolución cae de verdad
  (3508 → 1753 px) y no se pierde ninguna página.
- **Documento de solo texto**: no hay imágenes que tocar. Lo que no puede
  pasar es que la copia salga más grande, ni que se deje en disco una copia
  que no mejora nada.
- **Sobrescribir el original**: rechazado.
- **El original intacto** por SHA-256.

## Sobre el fixture

Lleva **grano de escáner**: variación suave píxel a píxel por toda la hoja.
Es lo que hace que un escaneo pese megas —Flate no puede con el ruido— y a la
vez lo que un JPEG absorbe sin problema. Sin ese grano el fixture se comprime
a nada y no se parece a lo que sale de un escáner.

Durante el desarrollo, dos versiones anteriores del fixture dieron resultados
que parecían fallos del servicio y no lo eran:

- con 32 bits por píxel, iText añade una máscara de transparencia y el servicio
  deja la imagen en paz, con razón: al pasarla a JPEG se perdería;
- con una imagen limpia sin grano, Flate comprime mejor que JPEG y el servicio
  decide no tocar nada, que también es lo correcto.

## Ejecución

```powershell
Set-Location -LiteralPath '...\firma automática\build\validation-shrink'
.\compile-and-run.ps1
```

## Resultado de referencia

```text
Escaneo: 31.83 MB -> 0.46 MB  (99% menos, 2 de 2 imagenes rehechas)
Lado mayor de imagen tras reducir: 1753 px
Solo texto: 0.00 MB -> 0.00 MB (20% menos)
Sobrescribir el original: rechazado, como debe ser.
PASS: reducir el tamaño funciona.
```

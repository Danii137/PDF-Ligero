# Geometría de la capa de texto del OCR

Mide el PDF que sale del OCR con `PdfTextBlockLocator`, que es la clase que usa
el subrayador para saber por dónde pasa cada renglón. Existe porque ese camino
—OCR escribe la capa, el subrayador la lee— se rompió sin que ninguna prueba se
enterara: las marcas salían como franjas amarillas de arriba abajo de la página.

## Qué comprueba

1. **El PDF real con OCR.** Alto de renglón mediano y máximo, y cuerpo de letra
   máximo. Ningún renglón de un documento pasa de una pulgada (72 pt).
2. **El caso patológico.** Fabrica un PDF cuya capa invisible declara una letra
   de 400 puntos para una palabra corta, que es exactamente lo que producían las
   cajas disparatadas de Tesseract, y exige que el localizador lo acote.

La segunda comprobación es la que tiene dientes: sin el guardia de
`PdfTextBlockLocator` el renglón mide unos 400 pt y la prueba falla.

## Ejecución

```powershell
# Primero hay que generar un PDF con OCR:
Set-Location -LiteralPath '...\firma automática\build\validation-ocr'
.\compile-and-run.ps1

Set-Location -LiteralPath '...\firma automática\build\validation-ocr-layer'
.\compile-and-run.ps1
```

Admite otro PDF como argumento:

```powershell
.\compile-and-run.ps1 -PdfConOcr 'D:\ruta\memoria escaneada OCR.pdf'
```

## Resultado de referencia

Sobre el fixture de `validation-ocr`, con 300 ppp y filtro de confianza:

```text
Paginas: 3   alto de pagina: 842.0 pt
Renglones: 82   de ellos marcados como OCR: 81
Alto de renglon: mediana 8.20 pt   maximo 14.80 pt
Cuerpo de letra maximo: 16.00 pt
Caso patologico (letra declarada de 400 pt): renglon de 11.0 pt
```

# Marca de agua y numeración

## Qué comprueba

Leyendo el texto del PDF resultante, no mirando si el archivo existe:

- **Numeración simple**: las 8 hojas dicen «Pagina N de 8».
- **Numeración desde la hoja 2**: el caso de una memoria con portada. La
  portada no lleva número, la hoja 2 dice «1 / 7» y la última «7 / 7». Es la
  comprobación que importa, porque ahí es donde se equivocan las cuentas.
- **Marca de agua**: aparece en las 8 hojas **y** el contenido original sigue
  estando. La marca se escribe debajo del contenido justamente para no taparlo.
- **Sin marca ni números**: se rechaza en vez de crear una copia idéntica.
- **El original intacto** por SHA-256.

## Ejecución

```powershell
Set-Location -LiteralPath '...\firma automática\build\validation-stamp'
.\compile-and-run.ps1
```

## Resultado de referencia

```text
Numeracion simple: 8 de 8 hojas numeradas.
Numeracion desde la hoja 2: 7 hojas numeradas.
Marca de agua: presente en las 8 hojas, sin tapar el texto.
Sin marca ni numeros: rechazado, como debe ser.
Original intacto por SHA-256.
PASS: marca de agua y numeración funcionan.
```

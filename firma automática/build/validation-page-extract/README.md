# Extraer y dividir páginas

`PdfPageExtractService` no escribe PDFs por su cuenta: monta la lista de páginas
y se la pasa a `PdfPageOrganizerService`, que ya sabe copiar páginas sin
rasterizar, conservar los marcadores y comprobar el resultado. Esta prueba
verifica el reparto y, sobre todo, que el original no se toca.

## Qué comprueba

- **Extracción seguida** (4-6): salen esas tres páginas y ninguna otra.
- **Extracción salteada y desordenada** (9, 2, 11): salen en el orden pedido,
  que es lo que permite rehacer un documento a la carta.
- **División** de 12 páginas en partes de 5: tres archivos (5 + 5 + 2) sin
  perder ni una página.
- **El original intacto** por SHA-256 al terminar.

Cada página del fixture lleva escrito su número, así que leyendo el texto del
resultado se sabe exactamente qué páginas salieron y en qué orden.

## Ejecución

```powershell
Set-Location -LiteralPath '...\firma automática\build\validation-page-extract'
.\compile-and-run.ps1
```

## Resultado de referencia

```text
Extraccion 4-6: 3 paginas -> extracto seguido.pdf
Extraccion 9, 2, 11: 3 paginas.
Division en partes de 5: 3 archivos, 12 paginas en total.
Original intacto por SHA-256.
PASS: extraer y dividir páginas funciona.
```

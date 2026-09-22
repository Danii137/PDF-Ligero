# Archivos adjuntos

En documentación oficial es normal que el PDF lleve dentro el DWG, la hoja de
cálculo o el justificante. Hasta ahora esos archivos estaban ahí sin que el
programa dijera nada, así que se perdían.

## Qué comprueba

- **Sin adjuntos**: no se inventa ninguno.
- **Ciclo completo**: se adjuntan dos archivos —uno binario de 4 kB y un CSV con
  tildes— y luego se sacan. El binario tiene que volver **idéntico por SHA-256**:
  si el contenido no vuelve igual, el adjunto no sirve de nada. El CSV tiene que
  conservar los acentos, tanto en el contenido como en el nombre.
- **Pedir uno que no está** devuelve false en vez de reventar.
- **No se pierden páginas** al adjuntar, y **el original queda intacto**.

## Dónde busca

Los adjuntos viven en dos sitios según cómo se hicieran: el árbol
`EmbeddedFiles` del catálogo, y las anotaciones `FileAttachment` ancladas a una
página. Se recorren los dos, porque un PDF que trae el adjunto solo como
anotación es igual de real.

## Ejecución

```powershell
Set-Location -LiteralPath '...\firma automática\build\validation-attachments'
.\compile-and-run.ps1
```

## Resultado de referencia

```text
Sin adjuntos: la lista sale vacia.
Adjuntados: 2 archivos.
Sacado y comparado: identico por SHA-256.
PASS: los adjuntos funcionan.
```

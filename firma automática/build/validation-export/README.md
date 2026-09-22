# Exportar a imagen y a texto

## Qué comprueba

- **Imágenes**: exporta las páginas 1 y 3 a 150 ppp y comprueba que los PNG
  tienen el tamaño que corresponde a un A4 a esa resolución **y que no han
  salido en blanco**. Un archivo que existe pero está vacío pasaría una
  comprobación de «¿se creó el archivo?» y no sirve de nada.
- **Texto**: el texto de las cuatro páginas aparece, con acentos, y **en
  orden**: importa al pegarlo en otro sitio.
- **Escaneo sin OCR**: un PDF cuyas páginas son imágenes no tiene texto que
  sacar. No se escribe ningún .txt, en vez de dejar un archivo con solo los
  separadores de página, que es lo que parecería que ha funcionado.

## Ejecución

```powershell
Set-Location -LiteralPath '...\firma automática\build\validation-export'
.\compile-and-run.ps1
```

## Resultado de referencia

```text
Imagenes: 2 archivos.
Texto: 2002 caracteres, 4 paginas.
Escaneo sin OCR: no se escribe nada, como debe ser.
PASS: exportar funciona.
```

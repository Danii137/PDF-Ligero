# Que lo que se guarda quede bien guardado

No basta con que la operación no lance. Hay que abrir el archivo resultante y
comprobar que lo que se pidió está dentro, que el resto sigue ahí, y que el
original no se ha tocado.

## Qué comprueba

- **Marcas: guardar y releer.** Se guardan un subrayado, una nota y un trazo, y
  se vuelven a leer **con el documento ya abierto en PDFium**, que es
  exactamente lo que hace el visor nada más guardar. Vuelven las tres, con su
  tipo, y la nota conserva su texto.
- **Organizar páginas.** Quitar una, girar otra 90° y dejar el resto al revés.
  Se comprueba el orden leyendo el texto de cada hoja y que el giro está
  escrito en el PDF.
- **Marcadores**, incluidas las tildes del título: «Capítulo 1» tiene que
  volver con su acento.
- **Guardar una copia**: idéntica al original byte a byte.
- **El original intacto** por SHA-256 en cada caso.

## Lo que este banco deja claro

La secuencia que fallaba en producción —«Las marcas se guardaron, pero no se
pudo refrescar la vista»— **pasa aquí sin problemas**. Es decir: el fallo no
está en el servicio que escribe ni en el que relee, sino en el estado de la
interfaz al recargar la revisión. Eso acota dónde hay que mirar cuando se
retome.

## Ejecución

```powershell
Set-Location -LiteralPath '...\firma automática\build\validation-save-integrity'
.\compile-and-run.ps1
```

## Resultado de referencia

```text
--- Marcas: guardar y releer ---
   marcas guardadas: 3   releidas: 3
   original intacto por SHA-256
--- Organizar paginas ---
   paginas: 4   quitadas: 1
   giro de 90 grados conservado
--- Marcadores ---
   marcadores leidos: 3
   tildes conservadas: "Capítulo 1"
--- Guardar una copia ---
   copia identica por SHA-256

PASS: lo que se guarda queda bien guardado.
```

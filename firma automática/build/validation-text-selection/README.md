# Seleccionar y copiar texto

Monta el visor real fuera de pantalla y usa `PdfTextSelectionController` tal
como lo usa la aplicación.

## Qué comprueba

- `Ctrl+A` selecciona todo el texto de la página y no se deja ningún renglón.
- Los renglones salen **en orden de lectura**: copiar un párrafo y que llegue
  desordenado no sirve de nada.
- `Ctrl+C` deja en el portapapeles exactamente lo seleccionado.
- `Esc` suelta la selección.
- Copiar sin nada seleccionado no revienta ni ensucia el portapapeles.

## Una trampa del banco

El portapapeles de Windows se sirve por OLE y necesita que el hilo atienda
mensajes para completar la entrega. La aplicación real tiene su bucle de
mensajes; esta prueba no, así que bombea a mano con `Application.DoEvents()`
antes de leer. Sin eso el portapapeles sale vacío **aunque la copia haya ido
bien**, y parece un fallo del programa que no lo es.

Lo que **no** cubre es el arrastre con el ratón, que necesita la ventana en
pantalla. Para eso está el patrón de `validation-inline-edit\capturar-subrayado.ps1`,
que conduce la aplicación de verdad.

## Ejecución

```powershell
Set-Location -LiteralPath '...\firma automática\build\validation-text-selection'
.\compile-and-run.ps1
```

## Resultado de referencia

```text
Seleccion de pagina: 124 caracteres, 4 renglones.
Portapapeles: 124 caracteres, coinciden.
PASS: seleccionar y copiar texto funciona.
```

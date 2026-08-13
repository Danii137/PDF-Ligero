# QA de reconocimiento de tipografía

Comprueba que al editar texto la aplicación reconoce la fuente que realmente
tiene el documento y escribe el reemplazo con esa misma fuente, en vez de con
una genérica elegida a mano.

Se prueba contra PDFs hechos **con Word**, no generados con iTextSharp, porque
el caso que importa es el real: Word incrusta las fuentes en subconjuntos
(`ABCDEF+Calibri`), que solo contienen los glifos ya usados y por tanto no
sirven para escribir texto nuevo.

## Cómo se ejecuta

Hace falta Word instalado y `build\output\PDFLigero.exe` recién compilado.

```powershell
.\crear-muestras.ps1        # genera muestras\muestras.pdf, una fuente por página
.\comprobar.ps1             # ¿detecta la tipografía correcta?
.\comprobar-reemplazo.ps1   # ¿escribe el texto nuevo con esa misma fuente?
```

Ambos scripts devuelven código de salida 0 si todo va bien.

## Qué cubre

`comprobar.ps1` compara, página a página, la fuente, el tamaño (con 0,35 pt de
tolerancia), la negrita y la cursiva detectadas frente a lo que se le pidió a
Word. Las páginas del PDF de muestra son:

| Página | Tipografía |
|---|---|
| 1 | Calibri 11 pt |
| 2 | Calibri 16 pt negrita |
| 3 | Times New Roman 12 pt cursiva |
| 4 | Arial 9,5 pt en gris |
| 5 | Consolas 10 pt |

`comprobar-reemplazo.ps1` va más allá: prepara una selección real, escribe un
reemplazo reutilizando la tipografía detectada y comprueba que la fuente que
acabó usándose es la del original. Antes de esta fase el resultado era siempre
Segoe UI, Times o Courier, sin relación con el documento.

## Por qué también vigila los metadatos

Estos scripts destaparon que **ningún PDF de Word se podía editar**: iText
reescribe el paquete XMP al guardar y reparte las propiedades de otra forma, y
la validación posterior lo interpretaba como una modificación de los metadatos y
abortaba. Si `comprobar-reemplazo.ps1` vuelve a fallar con «Los metadatos XMP
descriptivos cambiaron», es que esa protección ha vuelto a romperse.

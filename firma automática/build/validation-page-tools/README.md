# Duplicar página, hoja en blanco y recortar márgenes

## Qué comprueba

- **Duplicar la página 2** de un documento de 4: salen 5 páginas en el orden
  1, 2, 2, 3, 4. Se comprueba el orden leyendo el texto de cada hoja, no solo
  el número de páginas.
- **Hoja en blanco antes de la 2**: la hoja nueva está vacía, **tiene el mismo
  tamaño que las demás** (no un A4 por defecto, que es el fallo típico con
  planos) y la página original 2 viene detrás.
- **Recortar al contenido**: un rectángulo de 200×300 puntos centrado en un A4.
  La caja de recorte tiene que bajar de 595×842 a unos 231×330 — ni quedarse
  casi igual ni comerse el dibujo.
- **Recorte imposible**: un margen fijo mayor que la propia página. No se puede
  dejar la hoja en nada, así que se queda como estaba en vez de destruirla.
- **El original intacto** por SHA-256.

## Nota de implementación

Duplicar se hace en dos pasos —sacar la página a un PDF suelto y volver a
meterla— porque el organizador se niega, con razón, a que una página de origen
aparezca dos veces en la misma reorganización: tendría que decidir a cuál de las
dos copias van los marcadores y los enlaces que apuntan a ella. La primera
versión de esta prueba descubrió justamente eso.

## Ejecución

```powershell
Set-Location -LiteralPath '...\firma automática\build\validation-page-tools'
.\compile-and-run.ps1
```

## Resultado de referencia

```text
Duplicar la 2: 5 paginas.
Hoja en blanco antes de la 2: 4 paginas.
Recorte al contenido: de 595x842 a 231x330 puntos.
Recorte imposible: la pagina se queda como estaba.
PASS: duplicar, hoja en blanco y recortar funcionan.
```

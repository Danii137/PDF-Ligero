# Fase de comparación de planos - contrato de validación

Esta carpeta contiene un banco de pruebas independiente de la interfaz y de
los archivos de producción. Los originales se generan de forma determinista y
se vuelven a comprobar por SHA-256 después de cada operación.

## Fixtures

`revision-A.pdf` y `revision-B.pdf` contienen dos páginas exclusivamente
vectoriales:

- página 1: plano A3 apaisado;
- página 2: sección A4 vertical;
- líneas, cotas, textos y geometría repetida suficiente para estimar el
  alineamiento;
- `revision-B.pdf` tiene un `MediaBox` distinto y un desplazamiento global
  pequeño en cada página;
- los cambios reales están limitados a regiones conocidas: muro desplazado,
  pilar añadido, notas revisadas, cubierta modificada y conducto añadido.

La transformación conocida de B respecto de A se guarda en
`fixture-manifest.txt`. Es solo el oráculo de QA: el motor no debe recibirla.

## Criterios obligatorios del motor

1. Abrir ambos documentos bajo demanda, sin modificar ninguno.
2. Emparejar páginas explícitamente. No asumir que dos PDF siempre tienen el
   mismo número, orden o tamaño de página.
3. Estimar traslación y, si el motor lo admite, una escala uniforme limitada.
   Para estos fixtures la traslación estimada debe quedar a menos de 2 puntos
   de la transformación conocida.
4. Separar el desplazamiento global de los cambios reales. Todas las regiones
   conocidas deben conservar una señal visible, el total marcado no debe
   superar el 12,5 % de la hoja y el ruido fuera de las regiones ampliadas
   20 puntos no debe superar el 2,2 % del área comparable. Este límite admite
   el borde rojo/cian de un píxel que deja el antialiasing de dos renders
   vectoriales independientes.
5. Producir vistas `A`, `B`, superposición y diferencias sin rasterizar ni
   guardar una copia de los originales.
6. Mantener un límite explícito de píxeles por página. El caso normal usa como
   máximo 4 millones de píxeles por render y el motor no debe conservar más de
   tres renders completos simultáneamente.
7. Respetar `CancellationToken` durante análisis y render. Una cancelación no
   deja resultados parciales ni directorios temporales.
8. El SHA-256, tamaño y fecha de escritura de ambos originales deben ser
   idénticos antes y después de análisis, render, cancelación y cierre.

## Salidas del harness

`compile-and-run.ps1` crea una carpeta `run-*` con:

- los dos PDF originales;
- PNG de las cuatro páginas;
- `page-1-reference-overlay.png` y
  `page-2-reference-overlay.png`, una superposición alineada por el oráculo
  (azul = solo A, rojo = solo B);
- `qa-report.txt`;
- `fixture-manifest.txt`.

La superposición de referencia permite revisar visualmente el fixture y no
pretende sustituir las pruebas del motor. Cuando la API de producción está
disponible, el mismo script compila también `PlanComparisonEngineQa.cs`.

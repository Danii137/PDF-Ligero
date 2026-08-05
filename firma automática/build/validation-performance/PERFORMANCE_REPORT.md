# PDF Ligero - validación de rendimiento

Fecha de auditoría: 4 de agosto de 2026.

## Resultado

`PASS`. El ejecutable abre con rapidez, mantiene un consumo contenido y el
cierre multipestaña ya está optimizado. En las series finales aisladas no hubo
pausas de interfaz superiores a 500 ms, cierres forzados, procesos residuales
ni cambios en los PDF de prueba.

La prueba no pretende garantizar el mismo tiempo para cualquier PDF dañado,
cifrado, alojado en red o con una estructura excepcional. Las cifras son las
observadas en el equipo auditado y con la caché normal de Windows ya caliente.

## Equipo auditado

- Windows 11 Pro 10.0.26200;
- Intel Core i9-12900K, 16 núcleos y 24 procesadores lógicos;
- 63,7 GiB de RAM;
- SSD SATA WD Blue SA510 de 1 TB para la unidad `D:`;
- `PDFLigero.exe` SHA-256:
  `EE2C5DC80783805EB7C7C9E8C7C91650F7038CDA9F807D3A141AC3465B85C4DA`.

## Metodología

- un calentamiento descartado por escenario;
- tres procesos nuevos medidos para cada caso individual y cinco para el caso
  multipestaña;
- tiempo desde crear el proceso hasta ventana visible;
- tiempo hasta documento cargado, repintado completo forzado y ventana
  respondiendo a mensajes;
- memoria estabilizada un segundo después del primer repintado;
- pings `WM_NULL` con timeout de 500 ms durante carga y estabilización;
- cierre mediante el mismo mensaje `WM_CLOSE` del botón X;
- SHA-256 antes y después de todos los fixtures;
- inspección visual separada de los cinco estados mediante `PrintWindow`.

No se vació la caché del sistema: son mediciones calientes reproducibles. Un
benchmark realmente frío solo debe etiquetarse como tal tras reiniciar, y no
se simuló copiando archivos porque eso también calienta sus páginas.

## Resultados principales

| Escenario | Ventana mediana | Listo mediana | Listo p95 | Memoria privada | Working set |
|---|---:|---:|---:|---:|---:|
| Vacío | 267,6 ms | 289,2 ms | 312,4 ms | 27,7 MiB | 43,7 MiB |
| Vectorial, 2 páginas | 230,4 ms | 438,3 ms | 508,3 ms | 41,7 MiB | 57,6 MiB |
| Vectorial, 81 páginas | 211,3 ms | 397,5 ms | 450,4 ms | 43,1 MiB | 60,6 MiB |
| Escaneado, 16 páginas y 33,33 MiB | 211,3 ms | 613,1 ms | 653,9 ms | 184,3 MiB | 197,9 MiB |
| Cuatro pestañas, carga diferida | 241,9 ms | 567,6 ms | 755,8 ms | 43,2 MiB | 59,2 MiB |

El PDF de 81 páginas solo añade 1,4 MiB privados frente al vectorial de dos
páginas. Cuatro pestañas, una de ellas correspondiente al escaneado de
33,33 MiB, permanecen en 43,3 MiB privados mientras solo se visita la primera.
Ambos datos confirman la carga diferida y la caché limitada de miniaturas.

El ping máximo de las dos series finales fue 143,3 ms y hubo cero timeouts de
500 ms.

## Optimización del cierre multipestaña

El perfil por etapas demostró que el coste estaba en destruir cada `TabPage`
por separado, no en PDFium, Recovery, OCR ni el broker. El cierre total se
divide ahora en tres pasos seguros: desconectar herramientas y miniaturas,
destruir el `TabControl` completo en un único lote y, con los renderers ya
cerrados, liberar documentos, Recovery y leases.

| Cuatro pestañas | Antes | Después | Mejora |
|---|---:|---:|---:|
| Ventana desaparecida | 1.981,0 ms | 720,5 ms | 63,6 % |
| Proceso terminado | 3.438,0 ms | 2.083,1 ms | 39,4 % |

La ventana se oculta únicamente después de confirmar todos los documentos. La
ruta individual sigue liberando su pestaña de forma determinista. También se
bloquean nuevas aperturas desde la instancia única desde el punto irreversible
del cierre y la limpieza continúa con las demás pestañas si una liberación
aislada produce una excepción.

## Evidencias

- baseline anterior a la optimización, cinco iteraciones:
  `run-20260804-174640-ca6bd698\qa-report.txt`;
- serie final multipestaña, cinco iteraciones:
  `run-20260804-185505-551f2ad4\qa-report.txt` y
  `benchmark-results.csv`;
- serie final de los cuatro escenarios restantes, tres iteraciones:
  `run-20260804-185540-9cd6b8c2\qa-report.txt` y
  `benchmark-results.csv`;
- perfil por etapas y harness reproducible:
  `..\validation-close-performance\CLOSE_PERFORMANCE_REPORT.md` y
  `run-profile.ps1`;
- smoke visual corregido:
  `run-20260804-174949-4bf0814c\captures`;
- captura final multipestaña inspeccionada:
  `run-20260804-185505-551f2ad4\captures\multitab_4_lazy.png`;
- script reproducible: `run-benchmark.ps1`.

Las cinco capturas corregidas muestran la interfaz vacía, el PDF vectorial,
las 81 páginas, el escaneado pesado y las cuatro pestañas sin defectos de
renderizado visibles.

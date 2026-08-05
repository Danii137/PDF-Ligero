# PDF Ligero - perfil del cierre multipestaña

Fecha: 4 de agosto de 2026.

## Resultado

El cierre lento no procede de OCR, mediciones, historial de edición ni del
broker de instancia. El coste está concentrado en destruir la jerarquía
WinForms/PdfiumViewer y, dentro de las pestañas, en desechar la pestaña que
tiene el documento cargado.

Cinco procesos lógicos de prueba, con los mismos cuatro PDF del benchmark de
rendimiento y carga diferida, dieron una mediana de **2.048,8 ms** para
`Form.Close()` completo. El benchmark externo anterior había observado
1.981,0 ms hasta perder la ventana; la diferencia entre ambas series está
dentro de la variación normal de destrucción de controles.

Tras aplicar el cierre por lote al contenedor de pestañas, el benchmark externo
final bajó a **720,5 ms** hasta perder la ventana y **2.083,1 ms** hasta terminar
el proceso. Frente al baseline externo de 1.981,0/3.438,0 ms, son mejoras del
63,6 % y 39,4 %, respectivamente.

## Costes medidos

| Etapa | Mediana | Máximo | Diagnóstico |
|---|---:|---:|---|
| `FormClosing + FormClosed + destrucción WinForms` real | 2.048,8 ms | 2.079,8 ms | Coste total visible |
| Destrucción base de `Form` aislada tras el handler | 1.232,7 ms | 1.493,3 ms | Principal coste global |
| `TabPage.Dispose`, pestaña vectorial cargada | 427,8 ms | 487,5 ms | Principal coste por documento |
| `TabPage.Dispose`, cada pestaña diferida | 31,5-36,9 ms | 63,5 ms | Bajo |
| `FormClosing` (confirmaciones sin cambios) | 32,3 ms | 38,5 ms | Bajo |
| Resto de `FormClosed` (timer, menú, workers) | 32,3 ms | 63,7 ms | Bajo |
| `ViewerInstanceBroker.Dispose` | 0,586 ms | 33,6 ms | No es el cuello de botella |

Las etapas siguientes tuvieron medianas inferiores a 1,2 ms por pestaña:

- `RectangleZoom.Dispose`: 0,116-0,312 ms;
- desconexión de eventos y tooltips: 0,751-1,190 ms;
- `Thumbnails.ClearDocument`: 0,100-0,230 ms;
- `Document.Dispose`: 0,126-0,782 ms;
- limpieza de `PdfEditSession`: 0,242-0,399 ms;
- lease de verificación: 0,072-0,094 ms;
- bookkeeping de la pestaña: 0,157-0,280 ms.

`Measurement.Dispose` marcó 0,089-0,123 ms en este escenario porque la
herramienta de medición es diferida y no había sido abierta. Esto confirma que
no participa en los ~2 segundos originalmente observados; el harness queda
preparado para activar la herramienta en una repetición específica.

## Ventana frente a proceso

En la serie externa original, el intervalo mediano entre desaparecer la
ventana y terminar el proceso fue **1.457,1 ms**. El broker consume solo
0,586 ms de mediana, por lo que ese intervalo corresponde al cierre del CLR,
finalizadores y descarga de componentes nativos (principalmente Pdfium), no a
trabajo funcional de `PdfViewerForm` ni al canal de instancia única.

## Optimización aplicada

La implementación final hace lo siguiente:

1. confirma primero todos los documentos y solo entonces marca el cierre como
   irreversible, suspende layout y oculta la ventana;
2. prepara todos los workspaces retirando medición, zoom, eventos, tooltips y
   caché de miniaturas;
3. mantiene vivos los `PdfDocument` mientras destruye una única vez el
   `TabControl` padre con todas sus vistas;
4. después libera documentos, Recovery y leases en un `finally`;
5. evita refrescos por pestaña durante el cierre total y bloquea solicitudes
   tardías procedentes del broker de instancia única;
6. conserva la ruta determinista anterior para cerrar una sola pestaña.

No se asigna `Viewer.Document = null`: PdfiumViewer 2.13 no descarga así el
documento que conserva el renderer. Tampoco se usa `Environment.Exit`, `Kill`,
limpieza paralela ni una tarea abandonada. La mejora conserva la liberación
determinista de HWND, GDI, documentos y recuperación.

## Reproducción

- Harness: `run-profile.ps1`.
- Serie final: `run-20260804-183631-e86e735b`.
- Datos brutos: `stage-results.csv` (SHA-256
  `03D36B6F4780AC15AE522EAD11FFE89BCEB212FD9202AB926EEEED070093FE2C`).
- Benchmark optimizado final:
  `..\validation-performance\run-20260804-185505-551f2ad4`.
- Resultado: cero procesos `PDFLigero`/`FirmaAutomatica` residuales, cero
  cierres forzados y fixtures intactos.

El perfilador solo crea instrumentación y resultados dentro de
`build/validation-close-performance`; no modifica fuentes ni binarios de
producción.

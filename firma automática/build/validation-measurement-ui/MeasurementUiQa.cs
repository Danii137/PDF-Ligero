using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using iTextSharp.text;
using iTextSharp.text.pdf;
using PdfiumViewer;

namespace FirmaAutomatica
{
    internal static class MeasurementUiQa
    {
        private const int WmKeyDown = 0x0100;
        private const int WmLButtonDown = 0x0201;
        private const int WmLButtonDoubleClick = 0x0203;
        private static readonly List<string> Report =
            new List<string>();

        [STAThread]
        private static int Main(string[] arguments)
        {
            if (arguments.Length != 1)
            {
                Console.Error.WriteLine(
                    "Uso: MeasurementUiQa <carpeta-salida>");
                return 2;
            }

            var output = Path.GetFullPath(arguments[0]);
            Directory.CreateDirectory(output);
            var fixture = Path.Combine(output, "plano-medicion-ui.pdf");

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                CreateFixture(fixture);
                var identity = FileIdentity.Capture(fixture);

                AssertManualScaleAndPrecisionCursor(output, fixture);
                AssertVisibleWorkflow(output, fixture);
                AssertCalibrationDialog(fixture);
                AssertLimitsAndDispose(fixture);

                Require(
                    identity.IsUnchanged(),
                    "El PDF de entrada conserva longitud, fecha y SHA-256.");
                Report.Add("PDF_ORIGINAL_INTACTO=PASS");
                Report.Add("RESULTADO=PASS");
                File.WriteAllLines(
                    Path.Combine(output, "qa-report.txt"),
                    Report.ToArray(),
                    Encoding.UTF8);
                Console.WriteLine(string.Join(
                    Environment.NewLine,
                    Report.ToArray()));
                return 0;
            }
            catch (Exception exception)
            {
                Report.Add("RESULTADO=FAIL");
                Report.Add(exception.ToString());
                File.WriteAllLines(
                    Path.Combine(output, "qa-report.txt"),
                    Report.ToArray(),
                    Encoding.UTF8);
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        private static void AssertManualScaleAndPrecisionCursor(
            string output,
            string fixture)
        {
            AssertScaleParses("75", 75D);
            AssertScaleParses("1:75", 75D);
            AssertScaleParses("75,5", 75.5D);
            AssertScaleParses("75.5", 75.5D);

            var invalidScales = new[]
            {
                string.Empty,
                "0",
                "-1",
                "1:0",
                "1:",
                "2:75",
                "1:75:5",
                "75,5.2",
                "NaN",
                "Infinity",
                "1e2"
            };
            foreach (var invalidScale in invalidScales)
            {
                double ignored;
                Require(
                    !PdfMeasurementController.TryParseScaleDenominator(
                        invalidScale,
                        out ignored),
                    "Parser rechaza escala inválida: '" +
                    invalidScale +
                    "'.");
            }

            Report.Add("PARSER_ESCALA_MANUAL_FORMATOS_Y_LIMITES=PASS");

            using (var document = PdfiumViewer.PdfDocument.Load(fixture))
            using (var host = CreateHost(new Size(1060, 700)))
            using (var renderer = CreateRenderer(document))
            {
                host.Controls.Add(renderer);
                host.Show();
                Pump(140);

                var canvasPoint = new Point(
                    54,
                    Math.Max(80, renderer.ClientSize.Height - 70));
                Cursor.Position = renderer.PointToScreen(canvasPoint);
                Pump(30);
                Cursor.Current = Cursors.IBeam;

                var controller = NewController(renderer);
                var disposed = false;
                try
                {
                    controller.Activate();
                    Pump(60);
                    Require(
                        controller.IsActive,
                        "Herramienta activa para escala manual y cruceta.");

                    var setCursor = new SetCursorEventArgs(
                        canvasPoint,
                        HitTest.Client)
                    {
                        Cursor = Cursors.Hand
                    };
                    InvokePrivate(
                        controller,
                        "Renderer_SetCursor",
                        renderer,
                        setCursor);
                    Require(
                        setCursor.Cursor == Cursors.Cross,
                        "SetCursor Client activo impone la cruceta precisa.");

                    var toolbarPoint = new Point(
                        controller.Toolbar.Left + 6,
                        controller.Toolbar.Top + 6);
                    var toolbarCursor = new SetCursorEventArgs(
                        toolbarPoint,
                        HitTest.Client)
                    {
                        Cursor = Cursors.Hand
                    };
                    InvokePrivate(
                        controller,
                        "Renderer_SetCursor",
                        renderer,
                        toolbarCursor);
                    Require(
                        toolbarCursor.Cursor == Cursors.Hand &&
                        controller.Toolbar.Cursor != Cursors.Cross &&
                        controller.ScaleSelector.Cursor != Cursors.Cross,
                        "La barra flotante no hereda la cruceta del lienzo.");

                    Cursor.Current = Cursors.Hand;
                    InvokeRendererMouseMove(
                        controller,
                        renderer,
                        canvasPoint);
                    Require(
                        Cursor.Current == Cursors.Cross,
                        "MouseMove recupera la cruceta aunque PDFium ponga la mano.");

                    SetPendingScaleText(controller, "75");
                    var enter = new KeyEventArgs(Keys.Enter);
                    InvokePrivate(
                        controller,
                        "ScaleSelector_KeyDown",
                        controller.ScaleSelector,
                        enter);
                    Require(
                        enter.Handled &&
                        enter.SuppressKeyPress &&
                        !controller.HasPendingScaleText &&
                        controller.Calibration != null &&
                        controller.Calibration.Description ==
                            "Escala 1:75" &&
                        controller.ScaleSelector.Text == "1:75",
                        "Enter confirma escala manual entera.");

                    SetPendingScaleText(controller, "1:75,5");
                    InvokePrivate(
                        controller,
                        "ScaleSelector_Leave",
                        controller.ScaleSelector,
                        EventArgs.Empty);
                    Require(
                        !controller.HasPendingScaleText &&
                        controller.Calibration != null &&
                        controller.Calibration.Description ==
                            "Escala 1:75,5" &&
                        controller.ScaleSelector.Text == "1:75,5",
                        "Leave confirma escala decimal con coma.");

                    controller.SelectUnit(PdfMeasurementUnit.Meter);
                    controller.SelectKind(PdfMeasurementKind.Distance);
                    Require(
                        controller.AddPointForTesting(
                            0,
                            new PointF(125f, 185f)) &&
                        controller.AddPointForTesting(
                            0,
                            new PointF(305f, 185f)),
                        "Medición creada con escala manual 1:75,5.");
                    var customMeasurement = controller.Measurements[0];
                    var customSnapshot = GetCalibrationSnapshot(
                        controller,
                        customMeasurement);
                    Require(
                        customSnapshot.Description == "Escala 1:75,5",
                        "La medición toma snapshot de la escala manual.");

                    renderer.Zoom = Math.Min(
                        renderer.ZoomMax,
                        renderer.Zoom * 1.15D);
                    renderer.PerformLayout();
                    Pump(80);
                    AssertCrossAfterHand(
                        controller,
                        renderer,
                        canvasPoint,
                        "zoom");

                    renderer.Rotation = PdfRotation.Rotate90;
                    Pump(80);
                    AssertCrossAfterHand(
                        controller,
                        renderer,
                        canvasPoint,
                        "rotación");
                    renderer.Rotation = PdfRotation.Rotate0;

                    renderer.Page = 1;
                    controller.NotifyActivePage(1);
                    Pump(60);
                    Require(
                        controller.Calibration == null,
                        "Una página nueva no hereda la escala manual.");
                    AssertCrossAfterHand(
                        controller,
                        renderer,
                        canvasPoint,
                        "cambio de página");

                    SetPendingScaleText(controller, "33,25");
                    InvokePrivate(
                        controller,
                        "ScaleSelector_Leave",
                        controller.ScaleSelector,
                        EventArgs.Empty);
                    Require(
                        controller.Calibration != null &&
                        controller.Calibration.Description ==
                            "Escala 1:33,25",
                        "Escala manual independiente guardada en página 2.");

                    renderer.Page = 0;
                    controller.NotifyActivePage(0);
                    Pump(50);
                    Require(
                        controller.Calibration != null &&
                        controller.Calibration.Description ==
                            "Escala 1:75,5" &&
                        controller.ScaleSelector.Text == "1:75,5",
                        "Volver a la página restaura su escala manual y texto.");

                    Capture(
                        host,
                        renderer,
                        controller.Toolbar,
                        Path.Combine(
                            output,
                            "03-escala-manual-75-5-cruceta.png"));

                    controller.SelectScale(100D);
                    Require(
                        controller.Calibration.Description ==
                            "Escala 1:100" &&
                        GetCalibrationSnapshot(
                            controller,
                            customMeasurement).Description ==
                            "Escala 1:75,5",
                        "Cambiar a escala rápida conserva el snapshot manual.");

                    SetPendingScaleText(controller, "1:0");
                    var draftBeforeInvalidClick =
                        controller.DraftPointCount;
                    Require(
                        SendPdfMouseMessage(
                            renderer,
                            controller,
                            WmLButtonDown,
                            0,
                            new PointF(370f, 240f)) &&
                        controller.DraftPointCount ==
                            draftBeforeInvalidClick &&
                        controller.HasPendingScaleText &&
                        controller.Calibration != null &&
                        controller.Calibration.Description ==
                            "Escala 1:100" &&
                        controller.StatusText.IndexOf(
                            "no válida",
                            StringComparison.OrdinalIgnoreCase) >= 0,
                        "Texto inválido bloquea el clic y no usa la escala anterior.");

                    SetPendingScaleText(controller, "75.5");
                    var recover = new KeyEventArgs(Keys.Enter);
                    InvokePrivate(
                        controller,
                        "ScaleSelector_KeyDown",
                        controller.ScaleSelector,
                        recover);
                    Require(
                        controller.Calibration.Description ==
                            "Escala 1:75,5" &&
                        !controller.HasPendingScaleText,
                        "Escala válida con punto recupera la captura.");

                    AssertCrossAfterHand(
                        controller,
                        renderer,
                        canvasPoint,
                        "recuperación tras editar escala");
                    controller.Deactivate();
                    Require(
                        Cursor.Current == Cursors.IBeam,
                        "Desactivar restaura el cursor previo.");

                    var inactiveCursor = new SetCursorEventArgs(
                        canvasPoint,
                        HitTest.Client)
                    {
                        Cursor = Cursors.Hand
                    };
                    InvokePrivate(
                        controller,
                        "Renderer_SetCursor",
                        renderer,
                        inactiveCursor);
                    Require(
                        inactiveCursor.Cursor == Cursors.Hand,
                        "Herramienta inactiva no modifica SetCursor.");

                    Cursor.Current = Cursors.SizeAll;
                    controller.Activate();
                    Pump(40);
                    AssertCrossAfterHand(
                        controller,
                        renderer,
                        canvasPoint,
                        "reactivación");
                    var toolbar = controller.Toolbar;
                    controller.Dispose();
                    disposed = true;
                    Require(
                        Cursor.Current == Cursors.SizeAll &&
                        toolbar.IsDisposed &&
                        !renderer.Controls.Contains(toolbar),
                        "Dispose restaura cursor y retira la barra sin herencias.");
                }
                finally
                {
                    if (!disposed)
                    {
                        controller.Dispose();
                    }
                }
            }

            Report.Add("ESCALA_MANUAL_ENTER_LEAVE_POR_PAGINA=PASS");
            Report.Add("ESCALA_INVALIDA_BLOQUEA_CAPTURA_ANTERIOR=PASS");
            Report.Add("ESCALA_RAPIDA_Y_SNAPSHOT_SIGUEN=PASS");
            Report.Add("CRUCETA_PRECISA_CICLO_COMPLETO=PASS");
            Report.Add("CAPTURA_ESCALA_1_75_5=PASS");
        }

        private static void AssertVisibleWorkflow(
            string output,
            string fixture)
        {
            using (var document = PdfiumViewer.PdfDocument.Load(fixture))
            using (var host = CreateHost(new Size(1220, 840)))
            using (var renderer = CreateRenderer(document))
            {
                host.Controls.Add(renderer);
                host.Show();
                Pump(180);

                var viewportBefore = renderer.ClientSize;
                var canMeasure = true;
                using (var controller = new PdfMeasurementController(
                    renderer,
                    delegate { return canMeasure; },
                    Color.FromArgb(238, 91, 61),
                    Color.FromArgb(185, 68, 45),
                    Color.FromArgb(250, 249, 247)))
                {
                    var activeEvents = 0;
                    var statusEvents = 0;
                    controller.ActiveStateChanged += delegate
                    {
                        activeEvents++;
                    };
                    controller.StatusChanged += delegate
                    {
                        statusEvents++;
                    };

                    controller.Activate();
                    Pump(80);
                    Require(controller.IsActive, "Activación bajo demanda.");
                    Require(
                        controller.Toolbar.Visible &&
                        controller.Toolbar.Parent == renderer,
                        "Barra flotante alojada dentro del renderer.");
                    Require(
                        renderer.ClientSize == viewportBefore,
                        "La barra no reduce el área visible del PDF.");
                    Require(
                        controller.Calibration == null &&
                        controller.StatusText.IndexOf(
                            "escala",
                            StringComparison.OrdinalIgnoreCase) >= 0,
                        "Inicio explícito sin escala asumida.");
                    Require(
                        controller.ScaleSelector.AccessibleName.IndexOf(
                            "Escala",
                            StringComparison.OrdinalIgnoreCase) >= 0 &&
                        controller.UnitSelector.AccessibleName.IndexOf(
                            "Unidad",
                            StringComparison.OrdinalIgnoreCase) >= 0,
                        "Selectores accesibles.");

                    var blockedPoint = new PdfPoint(
                        0,
                        new PointF(120f, 120f));
                    var clientPoint = renderer.PointFromPdf(blockedPoint);
                    Cursor.Position = renderer.PointToScreen(clientPoint);
                    var mouseMessage = Message.Create(
                        renderer.Handle,
                        WmLButtonDown,
                        IntPtr.Zero,
                        IntPtr.Zero);
                    Require(
                        !controller.PreFilterMessage(ref mouseMessage) &&
                        controller.DraftPointCount == 0,
                        "Sin escala no captura geometría.");

                    controller.SelectScale(100D);
                    controller.SelectUnit(PdfMeasurementUnit.Meter);
                    Require(
                        controller.Calibration != null &&
                        controller.Calibration.Description == "Escala 1:100",
                        "Escala rápida 1:100 visible y tipada.");

                    Require(
                        controller.AddPointForTesting(
                            0,
                            new PointF(110f, 150f)),
                        "Primer punto de distancia.");
                    Require(
                        controller.AddPointForTesting(
                            0,
                            new PointF(254f, 150f)),
                        "Segundo punto de distancia.");
                    Require(
                        controller.Measurements.Count == 1 &&
                        controller.Measurements[0].Kind ==
                            PdfMeasurementKind.Distance,
                        "Distancia termina automáticamente a dos puntos.");
                    var firstMeasurement = controller.Measurements[0];
                    var firstSnapshot =
                        GetCalibrationSnapshot(
                            controller,
                            firstMeasurement);
                    var firstResult = firstMeasurement.Format(
                        firstSnapshot,
                        PdfMeasurementUnit.Meter);
                    controller.SelectScale(20D);
                    var snapshotAfterScaleChange =
                        GetCalibrationSnapshot(
                            controller,
                            firstMeasurement);
                    Require(
                        firstSnapshot.Description == "Escala 1:100" &&
                        snapshotAfterScaleChange.Description ==
                            "Escala 1:100" &&
                        firstMeasurement.Format(
                            snapshotAfterScaleChange,
                            PdfMeasurementUnit.Meter) == firstResult,
                        "Una medición conserva su calibración al cambiar la escala.");
                    controller.SelectScale(100D);

                    controller.SelectKind(PdfMeasurementKind.Perimeter);
                    AddTriangle(controller, 0, 130f, 270f);
                    Require(
                        controller.FinishDraftForTesting() &&
                        controller.Measurements.Count == 2,
                        "Perímetro termina mediante Enter/controlador.");

                    controller.SelectKind(PdfMeasurementKind.Area);
                    AddRectangle(controller, 0, 330f, 210f);
                    Require(
                        controller.FinishDraftForTesting() &&
                        controller.Measurements.Count == 3 &&
                        controller.Measurements[2].Kind ==
                            PdfMeasurementKind.Area,
                        "Área cerrada y guardada solo en memoria.");
                    Require(
                        renderer.Markers.Count == 1,
                        "Un marcador agregado por página medida.");

                    controller.SelectKind(PdfMeasurementKind.Perimeter);
                    var beforeDoubleClick =
                        controller.Measurements.Count;
                    SendPdfMouseMessage(
                        renderer,
                        controller,
                        WmLButtonDown,
                        0,
                        new PointF(500f, 340f));
                    SendPdfMouseMessage(
                        renderer,
                        controller,
                        WmLButtonDown,
                        0,
                        new PointF(570f, 340f));
                    SendPdfMouseMessage(
                        renderer,
                        controller,
                        WmLButtonDown,
                        0,
                        new PointF(535f, 395f));
                    Require(
                        controller.DraftPointCount == 3,
                        "DOWN previo al doble clic añade el vértice final.");
                    SendPdfMouseMessage(
                        renderer,
                        controller,
                        WmLButtonDoubleClick,
                        0,
                        new PointF(535f, 395f));
                    var doubleClickMeasurement =
                        controller.Measurements[
                            controller.Measurements.Count - 1];
                    Require(
                        controller.Measurements.Count ==
                            beforeDoubleClick + 1 &&
                        doubleClickMeasurement.Kind ==
                            PdfMeasurementKind.Perimeter &&
                        doubleClickMeasurement.Points.Count == 3 &&
                        controller.DraftPointCount == 0,
                        "DBLCLK termina sin punto fantasma.");

                    controller.AddPointForTesting(
                        0,
                        new PointF(510f, 250f));
                    controller.AddPointForTesting(
                        0,
                        new PointF(560f, 250f));
                    var backspace = Message.Create(
                        renderer.Handle,
                        WmKeyDown,
                        (IntPtr)Keys.Back,
                        IntPtr.Zero);
                    Require(
                        controller.PreFilterMessage(ref backspace) &&
                        controller.DraftPointCount == 1,
                        "Retroceso deshace el último vértice.");

                    var escape = Message.Create(
                        renderer.Handle,
                        WmKeyDown,
                        (IntPtr)Keys.Escape,
                        IntPtr.Zero);
                    Require(
                        controller.PreFilterMessage(ref escape) &&
                        controller.IsActive &&
                        controller.DraftPointCount == 0,
                        "Primer Esc cancela el trazado sin salir.");

                    controller.AddPointForTesting(
                        0,
                        new PointF(510f, 250f));
                    controller.NotifyActivePage(1);
                    Require(
                        controller.DraftPointCount == 0 &&
                        controller.ActivePageIndex == 1 &&
                        controller.Calibration == null,
                        "Cambiar de página cancela el trazado y no hereda escala.");

                    controller.SelectScale(20D);
                    controller.SelectKind(PdfMeasurementKind.Distance);
                    controller.AddPointForTesting(
                        1,
                        new PointF(90f, 120f));
                    controller.AddPointForTesting(
                        1,
                        new PointF(190f, 120f));
                    Require(
                        controller.Measurements.Any(
                            delegate(PdfPageMeasurement value)
                            {
                                return value.PageIndex == 1;
                            }) &&
                        renderer.Markers.Count == 2,
                        "Mediciones y marcadores se separan por página.");

                    renderer.Page = 0;
                    controller.NotifyActivePage(0);
                    Require(
                        controller.Calibration != null &&
                        controller.Calibration.Description ==
                            "Escala 1:100",
                        "Volver a la página restaura su escala 1:100.");

                    var viewportProbe = new PdfPoint(
                        0,
                        new PointF(430f, 300f));
                    var probeBeforeZoom =
                        renderer.PointFromPdf(viewportProbe);
                    var stableResultBeforeViewport =
                        firstMeasurement.Format(
                            firstSnapshot,
                            PdfMeasurementUnit.Meter);
                    renderer.Zoom = Math.Min(
                        renderer.ZoomMax,
                        renderer.Zoom * 1.35D);
                    renderer.PerformLayout();
                    Pump(120);
                    var probeAfterZoom =
                        renderer.PointFromPdf(viewportProbe);
                    var pdfAfterZoom =
                        renderer.PointToPdf(probeAfterZoom);
                    Require(
                        pdfAfterZoom.IsValid &&
                        pdfAfterZoom.Page == 0 &&
                        Math.Abs(
                            pdfAfterZoom.Location.X -
                            viewportProbe.Location.X) <= 2f &&
                        Math.Abs(
                            pdfAfterZoom.Location.Y -
                            viewportProbe.Location.Y) <= 2f &&
                        probeAfterZoom != probeBeforeZoom &&
                        firstMeasurement.Format(
                            firstSnapshot,
                            PdfMeasurementUnit.Meter) ==
                            stableResultBeforeViewport,
                        "Zoom reproyecta sin alterar coordenadas ni resultado.");

                    var displayLocation =
                        renderer.DisplayRectangle.Location;
                    renderer.SetDisplayRectLocation(
                        new Point(
                            displayLocation.X - 35,
                            displayLocation.Y - 25),
                        false);
                    Pump(100);
                    var probeAfterScroll =
                        renderer.PointFromPdf(viewportProbe);
                    var pdfAfterScroll =
                        renderer.PointToPdf(probeAfterScroll);
                    Require(
                        pdfAfterScroll.IsValid &&
                        pdfAfterScroll.Page == 0 &&
                        Math.Abs(
                            pdfAfterScroll.Location.X -
                            viewportProbe.Location.X) <= 2f &&
                        Math.Abs(
                            pdfAfterScroll.Location.Y -
                            viewportProbe.Location.Y) <= 2f &&
                        probeAfterScroll != probeAfterZoom &&
                        firstMeasurement.Format(
                            firstSnapshot,
                            PdfMeasurementUnit.Meter) ==
                            stableResultBeforeViewport,
                        "Scroll reproyecta sin alterar coordenadas ni resultado.");

                    renderer.ZoomMode = PdfViewerZoomMode.FitWidth;
                    renderer.PerformLayout();
                    Pump(100);
                    Capture(
                        host,
                        renderer,
                        controller.Toolbar,
                        Path.Combine(output, "01-medicion-ancha.png"));

                    var collapse = FindButton(
                        controller.Toolbar,
                        "Plegar herramientas");
                    Require(collapse != null, "Control de plegado accesible.");
                    collapse.PerformClick();
                    Pump(40);
                    Require(
                        controller.Toolbar.Width == 94,
                        "Barra plegada ocupa solo 94 píxeles.");
                    collapse.PerformClick();
                    Require(
                        controller.Toolbar.Width == 558,
                        "Barra desplegable recupera las herramientas.");

                    host.ClientSize = new Size(900, 620);
                    renderer.Rotation = PdfRotation.Rotate90;
                    Pump(180);
                    Require(
                        controller.Toolbar.Right <=
                            renderer.ClientSize.Width - 8,
                        "Barra contenida en vista de 900 × 620.");
                    Capture(
                        host,
                        renderer,
                        controller.Toolbar,
                        Path.Combine(output, "02-medicion-900x620-rotada.png"));

                    renderer.Rotation = PdfRotation.Rotate0;
                    var pageZeroBeforeRemove = controller.Measurements.Count(
                        delegate(PdfPageMeasurement value)
                        {
                            return value.PageIndex == 0;
                        });
                    var pageOneBeforeRemove = controller.Measurements.Count(
                        delegate(PdfPageMeasurement value)
                        {
                            return value.PageIndex == 1;
                        });
                    var removeLast = FindButton(
                        controller.Toolbar,
                        "última medición");
                    Require(
                        removeLast != null,
                        "Botón Borrar última expuesto y accesible.");
                    removeLast.PerformClick();
                    Require(
                        controller.Measurements.Count(
                            delegate(PdfPageMeasurement value)
                            {
                                return value.PageIndex == 0;
                            }) == pageZeroBeforeRemove - 1 &&
                        controller.Measurements.Count(
                            delegate(PdfPageMeasurement value)
                            {
                                return value.PageIndex == 1;
                            }) == pageOneBeforeRemove &&
                        renderer.Markers.Count == 2,
                        "Borrar última afecta solo a página activa y conserva markers.");

                    canMeasure = false;
                    controller.Deactivate();
                    controller.Activate();
                    Require(
                        !controller.IsActive &&
                        controller.StatusText.IndexOf(
                            "No se puede",
                            StringComparison.OrdinalIgnoreCase) >= 0,
                        "El delegado de disponibilidad bloquea reactivación.");
                    canMeasure = true;
                    controller.Activate();
                    Require(controller.IsActive, "Reactivación restaurada.");
                    Require(
                        controller.PreFilterMessage(ref escape) &&
                        !controller.IsActive,
                        "Esc sin borrador desactiva la herramienta.");
                    Require(
                        controller.HasMeasurements,
                        "Desactivar conserva mediciones en memoria.");
                    Require(
                        activeEvents >= 4 && statusEvents >= 4,
                        "Eventos de actividad y estado publicados.");

                    controller.Clear();
                    Require(
                        !controller.HasMeasurements &&
                        controller.Measurements.Count == 0 &&
                        renderer.Markers.Count == 0,
                        "Clear elimina mediciones y todos sus markers.");
                }
            }

            Report.Add(
                "FLUJO_UI_ESTADO_PAGINAS_ROTACION_CAPTURAS=PASS");
            Report.Add("CALIBRACION_POR_PAGINA=PASS");
            Report.Add("SNAPSHOT_CALIBRACION_MEDICION=PASS");
            Report.Add("ZOOM_SCROLL_COORDENADAS_ESTABLES=PASS");
            Report.Add("DOBLE_CLIC_SIN_PUNTO_FANTASMA=PASS");
            Report.Add("BORRAR_ULTIMA_SOLO_PAGINA_ACTIVA=PASS");
        }

        private static void AssertCalibrationDialog(string fixture)
        {
            using (var document = PdfiumViewer.PdfDocument.Load(fixture))
            using (var host = CreateHost(new Size(900, 620)))
            using (var renderer = CreateRenderer(document))
            {
                host.Controls.Add(renderer);
                host.Show();
                Pump(120);

                using (var controller = NewController(renderer))
                {
                    controller.Activate();
                    controller.ScaleSelector.SelectedIndex =
                        controller.ScaleSelector.Items.Count - 1;
                    Require(
                        controller.IsCalibrating &&
                        controller.StatusText.IndexOf(
                            "dos puntos",
                            StringComparison.OrdinalIgnoreCase) >= 0,
                        "Calibración de dos puntos iniciada.");
                    controller.AddPointForTesting(
                        0,
                        new PointF(100f, 100f));

                    var dialogHandled = false;
                    using (var timer = new System.Windows.Forms.Timer())
                    {
                        timer.Interval = 60;
                        timer.Tick += delegate
                        {
                            var dialog = Application.OpenForms
                                .Cast<Form>()
                                .FirstOrDefault(
                                    delegate(Form form)
                                    {
                                        return form.Text ==
                                            "Calibrar escala";
                                    });
                            if (dialog == null)
                            {
                                return;
                            }

                            var value = FindDescendant<NumericUpDown>(
                                dialog);
                            var unit = FindDescendant<ComboBox>(dialog);
                            var apply = dialog.Controls
                                .OfType<Button>()
                                .FirstOrDefault(
                                    delegate(Button button)
                                    {
                                        return button.DialogResult ==
                                            DialogResult.OK;
                                    });
                            Require(
                                value != null && unit != null && apply != null,
                                "Diálogo de calibración completo.");
                            value.Value = 5M;
                            unit.SelectedIndex = 2;
                            dialogHandled = true;
                            timer.Stop();
                            apply.PerformClick();
                        };
                        timer.Start();
                        controller.AddPointForTesting(
                            0,
                            new PointF(244f, 100f));
                    }

                    Require(
                        dialogHandled &&
                        !controller.IsCalibrating &&
                        controller.DraftPointCount == 0,
                        "Diálogo calibrado y cerrado de forma determinista.");
                    Require(
                        controller.Calibration != null &&
                        controller.Calibration.Description.IndexOf(
                            "5,00 m",
                            StringComparison.Ordinal) >= 0 &&
                        controller.ActiveUnit ==
                            PdfMeasurementUnit.Meter,
                        "Distancia conocida aplica calibración y unidad.");

                    controller.SelectKind(PdfMeasurementKind.Distance);
                    controller.AddPointForTesting(
                        0,
                        new PointF(100f, 180f));
                    controller.AddPointForTesting(
                        0,
                        new PointF(172f, 180f));
                    Require(
                        controller.Measurements.Count == 1 &&
                        Math.Abs(
                            controller.Measurements[0].Calculate(
                                controller.Calibration,
                                PdfMeasurementUnit.Meter) -
                            2.5D) < 0.0001D,
                        "Calibración 144 pt = 5 m produce 72 pt = 2,5 m.");
                }
            }

            Report.Add("CALIBRACION_DOS_PUNTOS_DIALOGO=PASS");
        }

        private static void AssertLimitsAndDispose(string fixture)
        {
            using (var document = PdfiumViewer.PdfDocument.Load(fixture))
            using (var host = CreateHost(new Size(900, 620)))
            using (var renderer = CreateRenderer(document))
            {
                host.Controls.Add(renderer);
                host.Show();
                Pump(100);

                var controller = NewController(renderer);
                controller.Activate();
                controller.SelectScale(1D);
                controller.SelectKind(PdfMeasurementKind.Distance);
                for (var index = 0; index < 201; index++)
                {
                    controller.AddPointForTesting(
                        0,
                        new PointF(50f, 50f + index));
                    controller.AddPointForTesting(
                        0,
                        new PointF(60f, 50f + index));
                }

                Require(
                    controller.Measurements.Count == 200 &&
                    controller.StatusText.IndexOf(
                        "máximo de 200",
                        StringComparison.OrdinalIgnoreCase) >= 0,
                    "Límite duro de 200 mediciones.");

                controller.Clear();
                controller.SelectKind(PdfMeasurementKind.Perimeter);
                for (var index = 0; index < 101; index++)
                {
                    controller.AddPointForTesting(
                        0,
                        new PointF(
                            40f + index * 2f,
                            200f + (index % 2) * 4f));
                }

                Require(
                    controller.DraftPointCount == 100 &&
                    controller.StatusText.IndexOf(
                        "100 vértices",
                        StringComparison.OrdinalIgnoreCase) >= 0,
                    "Límite duro de 100 vértices por medición.");

                var toolbar = controller.Toolbar;
                var markerCountBeforeDispose = renderer.Markers.Count;
                Require(
                    markerCountBeforeDispose > 0 &&
                    renderer.Controls.Contains(toolbar),
                    "Controller posee marker y barra antes de Dispose.");
                controller.Dispose();
                Require(
                    !renderer.Controls.Contains(toolbar) &&
                    renderer.Markers.Count == 0 &&
                    toolbar.IsDisposed,
                    "Dispose retira controles y todos los markers.");

                var key = Message.Create(
                    renderer.Handle,
                    WmKeyDown,
                    (IntPtr)Keys.Enter,
                    IntPtr.Zero);
                Require(
                    !controller.PreFilterMessage(ref key),
                    "MessageFilter queda inerte después de Dispose.");
            }

            Report.Add("LIMITES_Y_DISPOSE_COMPLETO=PASS");
        }

        private static PdfMeasurementController NewController(
            PdfRenderer renderer)
        {
            return new PdfMeasurementController(
                renderer,
                delegate { return true; },
                Color.FromArgb(238, 91, 61),
                Color.FromArgb(185, 68, 45),
                Color.FromArgb(250, 249, 247));
        }

        private static Form CreateHost(Size clientSize)
        {
            return new Form
            {
                Text = "QA medición PDF Ligero",
                StartPosition = FormStartPosition.Manual,
                Location = new Point(20, 20),
                ClientSize = clientSize,
                BackColor = Color.FromArgb(234, 233, 230)
            };
        }

        private static PdfRenderer CreateRenderer(
            PdfiumViewer.PdfDocument document)
        {
            var renderer = new PdfRenderer
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(234, 233, 230),
                ZoomMode = PdfViewerZoomMode.FitWidth,
                TabStop = true
            };
            renderer.Load(document);
            return renderer;
        }

        private static void AddTriangle(
            PdfMeasurementController controller,
            int page,
            float x,
            float y)
        {
            controller.AddPointForTesting(page, new PointF(x, y));
            controller.AddPointForTesting(page, new PointF(x + 100f, y));
            controller.AddPointForTesting(
                page,
                new PointF(x + 50f, y + 80f));
        }

        private static void AddRectangle(
            PdfMeasurementController controller,
            int page,
            float x,
            float y)
        {
            controller.AddPointForTesting(page, new PointF(x, y));
            controller.AddPointForTesting(page, new PointF(x + 120f, y));
            controller.AddPointForTesting(
                page,
                new PointF(x + 120f, y + 90f));
            controller.AddPointForTesting(page, new PointF(x, y + 90f));
        }

        private static void AssertScaleParses(
            string text,
            double expected)
        {
            double actual;
            Require(
                PdfMeasurementController.TryParseScaleDenominator(
                    text,
                    out actual) &&
                Math.Abs(actual - expected) < 0.0000001D,
                "Parser acepta '" +
                text +
                "' como 1:" +
                expected.ToString(
                    "0.######",
                    System.Globalization.CultureInfo.InvariantCulture) +
                ".");
        }

        private static void SetPendingScaleText(
            PdfMeasurementController controller,
            string text)
        {
            controller.ScaleSelector.Text = text;
            InvokePrivate(
                controller,
                "ScaleSelector_TextUpdate",
                controller.ScaleSelector,
                EventArgs.Empty);
            Require(
                controller.HasPendingScaleText,
                "Editar la escala queda pendiente hasta Enter o Leave.");
        }

        private static void AssertCrossAfterHand(
            PdfMeasurementController controller,
            PdfRenderer renderer,
            Point location,
            string scenario)
        {
            Cursor.Current = Cursors.Hand;
            InvokeRendererMouseMove(
                controller,
                renderer,
                location);
            Require(
                Cursor.Current == Cursors.Cross &&
                controller.IsPrecisionCursorApplied,
                "La cruceta permanece precisa tras " + scenario + ".");
        }

        private static void InvokeRendererMouseMove(
            PdfMeasurementController controller,
            PdfRenderer renderer,
            Point location)
        {
            InvokePrivate(
                controller,
                "Renderer_MouseMove",
                renderer,
                new MouseEventArgs(
                    MouseButtons.None,
                    0,
                    location.X,
                    location.Y,
                    0));
        }

        private static void InvokePrivate(
            object target,
            string methodName,
            params object[] arguments)
        {
            var method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Require(
                method != null,
                "Método interno accesible para QA: " + methodName + ".");
            try
            {
                method.Invoke(target, arguments);
            }
            catch (TargetInvocationException exception)
            {
                if (exception.InnerException != null)
                {
                    throw exception.InnerException;
                }

                throw;
            }
        }

        private static bool SendPdfMouseMessage(
            PdfRenderer renderer,
            PdfMeasurementController controller,
            int messageId,
            int pageIndex,
            PointF pdfLocation)
        {
            var location = renderer.PointFromPdf(
                new PdfPoint(pageIndex, pdfLocation));
            Cursor.Position = renderer.PointToScreen(location);
            var message = Message.Create(
                renderer.Handle,
                messageId,
                IntPtr.Zero,
                IntPtr.Zero);
            return controller.PreFilterMessage(ref message);
        }

        private static Button FindButton(
            Control root,
            string accessibleNameFragment)
        {
            return EnumerateDescendants(root)
                .OfType<Button>()
                .FirstOrDefault(
                    delegate(Button button)
                    {
                        return (button.AccessibleName ?? string.Empty)
                            .IndexOf(
                                accessibleNameFragment,
                                StringComparison.OrdinalIgnoreCase) >= 0;
                    });
        }

        private static PdfMeasurementCalibration GetCalibrationSnapshot(
            PdfMeasurementController controller,
            PdfPageMeasurement measurement)
        {
            var field = typeof(PdfMeasurementController).GetField(
                "measurementCalibrations",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Require(
                field != null,
                "Snapshot de calibración accesible para QA.");
            var values = field.GetValue(controller) as
                IDictionary<
                    PdfPageMeasurement,
                    PdfMeasurementCalibration>;
            Require(
                values != null && values.ContainsKey(measurement),
                "La medición tiene snapshot de calibración.");
            return values[measurement];
        }

        private static T FindDescendant<T>(Control root)
            where T : Control
        {
            return EnumerateDescendants(root).OfType<T>().FirstOrDefault();
        }

        private static IEnumerable<Control> EnumerateDescendants(
            Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (var descendant in EnumerateDescendants(child))
                {
                    yield return descendant;
                }
            }
        }

        private static void Capture(
            Form host,
            PdfRenderer renderer,
            Control toolbar,
            string path)
        {
            using (var bitmap = new Bitmap(
                Math.Max(1, host.ClientSize.Width),
                Math.Max(1, host.ClientSize.Height),
                System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
            {
                host.DrawToBitmap(
                    bitmap,
                    new System.Drawing.Rectangle(
                        Point.Empty,
                        host.ClientSize));
                Require(
                    CountApproximateColor(
                        bitmap,
                        Color.FromArgb(238, 91, 61),
                        7) >= 15,
                    "La captura contiene acento coral y geometría.");
                Require(
                    CountDarkPixels(bitmap) >= 500,
                    "La captura contiene plano y controles legibles.");
                bitmap.Save(
                    path,
                    System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        private static void DrawFloatingControl(
            Bitmap target,
            Form host,
            Control control)
        {
            if (control == null ||
                !control.Visible ||
                control.Width < 1 ||
                control.Height < 1)
            {
                return;
            }

            using (var overlay = new Bitmap(
                control.Width,
                control.Height,
                System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
            using (var graphics = Graphics.FromImage(target))
            {
                control.DrawToBitmap(overlay, control.ClientRectangle);
                var location = host.PointToClient(
                    control.PointToScreen(Point.Empty));
                graphics.DrawImageUnscaled(overlay, location);
            }
        }

        private static int CountApproximateColor(
            Bitmap bitmap,
            Color expected,
            int tolerance)
        {
            var count = 0;
            for (var y = 0; y < bitmap.Height; y += 2)
            {
                for (var x = 0; x < bitmap.Width; x += 2)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    if (Math.Abs(pixel.R - expected.R) <= tolerance &&
                        Math.Abs(pixel.G - expected.G) <= tolerance &&
                        Math.Abs(pixel.B - expected.B) <= tolerance)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private static int CountDarkPixels(Bitmap bitmap)
        {
            var count = 0;
            for (var y = 0; y < bitmap.Height; y += 3)
            {
                for (var x = 0; x < bitmap.Width; x += 3)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    if (pixel.R < 85 && pixel.G < 85 && pixel.B < 85)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private static void Pump(int milliseconds)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);
            do
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
            while (DateTime.UtcNow < deadline);
        }

        private static void CreateFixture(string path)
        {
            using (var stream = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            {
                var document = new Document(
                    PageSize.A3.Rotate(),
                    0f,
                    0f,
                    0f,
                    0f);
                var writer = PdfWriter.GetInstance(document, stream);
                writer.SetFullCompression();
                document.Open();
                DrawPlan(writer.DirectContent, PageSize.A3.Rotate(), 1);
                document.SetPageSize(PageSize.A4);
                document.NewPage();
                DrawPlan(writer.DirectContent, PageSize.A4, 2);
                document.Close();
            }
        }

        private static void DrawPlan(
            PdfContentByte canvas,
            iTextSharp.text.Rectangle page,
            int pageNumber)
        {
            canvas.SaveState();
            canvas.SetColorStroke(new BaseColor(42, 43, 40));
            canvas.SetLineWidth(0.8f);
            canvas.Rectangle(28f, 28f, page.Width - 56f, page.Height - 56f);
            canvas.Stroke();

            canvas.SetColorStroke(new BaseColor(185, 184, 180));
            canvas.SetLineWidth(0.35f);
            for (var x = 70f; x < page.Width - 60f; x += 36f)
            {
                canvas.MoveTo(x, 70f);
                canvas.LineTo(x, page.Height - 70f);
            }
            for (var y = 70f; y < page.Height - 60f; y += 36f)
            {
                canvas.MoveTo(70f, y);
                canvas.LineTo(page.Width - 70f, y);
            }
            canvas.Stroke();

            canvas.SetColorStroke(BaseColor.BLACK);
            canvas.SetLineWidth(3.2f);
            canvas.Rectangle(
                95f,
                110f,
                page.Width * 0.62f,
                page.Height * 0.52f);
            canvas.MoveTo(95f + page.Width * 0.31f, 110f);
            canvas.LineTo(
                95f + page.Width * 0.31f,
                110f + page.Height * 0.52f);
            canvas.Stroke();

            var font = BaseFont.CreateFont(
                BaseFont.HELVETICA,
                BaseFont.CP1252,
                BaseFont.NOT_EMBEDDED);
            canvas.BeginText();
            canvas.SetFontAndSize(font, 12f);
            canvas.SetTextMatrix(70f, page.Height - 52f);
            canvas.ShowText(
                "PLANO DE MEDICIÓN · PÁGINA " + pageNumber);
            canvas.SetFontAndSize(font, 7f);
            for (var index = 0; index < 12; index++)
            {
                canvas.SetTextMatrix(
                    80f + index * 45f,
                    86f + (index % 2) * 12f);
                canvas.ShowText("EJE " + (index + 1));
            }
            canvas.EndText();
            canvas.RestoreState();
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidDataException(message);
            }
        }

        private sealed class FileIdentity
        {
            private FileIdentity(
                string path,
                long length,
                DateTime writeUtc,
                string hash)
            {
                Path = path;
                Length = length;
                WriteUtc = writeUtc;
                Hash = hash;
            }

            public string Path { get; private set; }

            public long Length { get; private set; }

            public DateTime WriteUtc { get; private set; }

            public string Hash { get; private set; }

            public static FileIdentity Capture(string path)
            {
                var info = new FileInfo(path);
                return new FileIdentity(
                    path,
                    info.Length,
                    info.LastWriteTimeUtc,
                    ComputeHash(path));
            }

            public bool IsUnchanged()
            {
                var info = new FileInfo(Path);
                return info.Exists &&
                    info.Length == Length &&
                    info.LastWriteTimeUtc == WriteUtc &&
                    string.Equals(
                        ComputeHash(Path),
                        Hash,
                        StringComparison.Ordinal);
            }

            private static string ComputeHash(string path)
            {
                using (var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                using (var algorithm = SHA256.Create())
                {
                    return BitConverter.ToString(
                        algorithm.ComputeHash(stream))
                        .Replace("-", string.Empty);
                }
            }
        }
    }
}

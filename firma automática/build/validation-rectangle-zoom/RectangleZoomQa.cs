using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using iTextSharp.text;
using iTextSharp.text.pdf;
using PdfiumViewer;
using PdfiumDocument = PdfiumViewer.PdfDocument;
using PdfRectangle = PdfiumViewer.PdfRectangle;
using Rectangle = System.Drawing.Rectangle;

namespace FirmaAutomatica
{
    internal static class RectangleZoomQa
    {
        [STAThread]
        private static int Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var root = AppDomain.CurrentDomain.BaseDirectory;
            var fixturePath = Path.Combine(root, "fixture-rectangle-zoom.pdf");
            var captureDirectory = Path.Combine(root, "captures");
            Directory.CreateDirectory(captureDirectory);
            CreateFixture(fixturePath);

            try
            {
                using (var document = PdfiumDocument.Load(fixturePath))
                using (var form = new Form
                {
                    Text = "QA · Zoom por rectángulo",
                    ClientSize = new Size(980, 720),
                    StartPosition = FormStartPosition.CenterScreen,
                    BackColor = Color.FromArgb(234, 233, 230)
                })
                using (var renderer = new PdfRenderer
                {
                    Dock = DockStyle.Fill,
                    BackColor = Color.FromArgb(234, 233, 230),
                    ZoomMode = PdfViewerZoomMode.FitWidth
                })
                {
                    form.Controls.Add(renderer);
                    renderer.Load(document);

                    var gestureEnabled = true;
                    using (var controller = new PdfRectangleZoomController(
                        renderer,
                        delegate { return gestureEnabled; },
                        Color.FromArgb(238, 91, 61),
                        Color.FromArgb(185, 68, 45),
                        Color.FromArgb(250, 249, 247)))
                    {
                        form.Show();
                        PumpMessages(350);

                        var pageSize = document.PageSizes[0];
                        var pageBounds = renderer.BoundsFromPdf(
                            new PdfRectangle(
                                0,
                                new RectangleF(
                                    0,
                                    0,
                                    pageSize.Width,
                                    pageSize.Height)));
                        Assert(
                            pageBounds.Width > 300 &&
                            pageBounds.Height > 400,
                            "La página no quedó disponible para el gesto.");

                        var selection = new Rectangle(
                            pageBounds.Left + pageBounds.Width / 4,
                            pageBounds.Top + pageBounds.Height / 5,
                            pageBounds.Width / 3,
                            pageBounds.Height / 4);
                        CreateSelection(controller, selection);
                        Assert(
                            controller.HasSelection,
                            "El rectángulo no quedó seleccionado al soltar.");
                        Assert(
                            controller.AcceptButton.Visible,
                            "El botón central no quedó visible.");
                        Assert(
                            renderer.Markers.Count == 1,
                            "El marco no se integró como marcador PDF.");

                        Capture(
                            form,
                            Path.Combine(
                                captureDirectory,
                                "01-seleccion-pendiente.png"));

                        var zoomBefore = renderer.Zoom;
                        var selectedPdfBounds = controller.Selection.Value;
                        controller.AcceptButton.PerformClick();
                        PumpMessages(450);
                        Assert(
                            !controller.HasSelection,
                            "La selección no se limpió tras ampliar.");
                        Assert(
                            renderer.Zoom > zoomBefore + 0.05,
                            "El zoom no aumentó.");

                        var framedBounds =
                            renderer.BoundsFromPdf(selectedPdfBounds);
                        var viewportCenter = new Point(
                            renderer.ClientSize.Width / 2,
                            renderer.ClientSize.Height / 2);
                        var framedCenter = new Point(
                            framedBounds.Left + framedBounds.Width / 2,
                            framedBounds.Top + framedBounds.Height / 2);
                        Assert(
                            Math.Abs(viewportCenter.X - framedCenter.X) < 6 &&
                            Math.Abs(viewportCenter.Y - framedCenter.Y) < 6,
                            "La región ampliada no quedó centrada. Visor=" +
                            viewportCenter +
                            " selección=" +
                            framedCenter +
                            " display=" +
                            renderer.DisplayRectangle +
                            " framed=" +
                            framedBounds +
                            " page=" +
                            renderer.BoundsFromPdf(
                                new PdfRectangle(
                                    0,
                                    new RectangleF(
                                        0,
                                        0,
                                        pageSize.Width,
                                        pageSize.Height))) +
                            " zoom=" +
                            renderer.Zoom.ToString("0.000"));

                        Capture(
                            form,
                            Path.Combine(
                                captureDirectory,
                                "02-seleccion-encuadrada.png"));

                        // Escape uses Cancel() in PdfViewerForm and also has a
                        // direct message-filter path when focus stays in PDF.
                        CreateSelection(
                            controller,
                            new Rectangle(
                                pageBounds.Left + 80,
                                pageBounds.Top + 90,
                                190,
                                140));
                        controller.Cancel();
                        Assert(
                            !controller.HasSelection &&
                            !controller.AcceptButton.Visible,
                            "Escape/cancelación dejó restos visuales.");

                        renderer.Zoom = 1;
                        renderer.Page = 0;
                        PumpMessages(120);
                        pageBounds = renderer.BoundsFromPdf(
                            new PdfRectangle(
                                0,
                                new RectangleF(
                                    0,
                                    0,
                                    pageSize.Width,
                                    pageSize.Height)));
                        CreateSelection(
                            controller,
                            new Rectangle(
                                pageBounds.Left + 70,
                                pageBounds.Top + 70,
                                170,
                                130));
                        renderer.Page = 1;
                        PumpMessages(160);
                        controller.NotifyActivePage(renderer.Page);
                        Assert(
                            !controller.HasSelection,
                            "El cambio de página no canceló la selección. " +
                            "Página activa=" +
                            renderer.Page +
                            " display=" +
                            renderer.DisplayRectangle);

                        gestureEnabled = false;
                        renderer.Page = 0;
                        PumpMessages(100);
                        Assert(
                            !controller.BeginSelection(
                                new Point(
                                    pageBounds.Left + 100,
                                    pageBounds.Top + 100)),
                            "El gesto arrancó con una herramienta activa.");

                        File.WriteAllText(
                            Path.Combine(root, "qa-report.txt"),
                            "PASS: zoom por rectángulo validado.\r\n" +
                            "PASS selección persistente + control central.\r\n" +
                            "PASS encuadre y centrado.\r\n" +
                            "PASS cancelación por Escape y cambio de página.\r\n" +
                            "PASS bloqueo cuando hay herramienta activa.\r\n");
                    }
                }

                return 0;
            }
            catch (Exception error)
            {
                File.WriteAllText(
                    Path.Combine(root, "qa-report.txt"),
                    "FAIL: " + error + Environment.NewLine);
                return 1;
            }
        }

        private static void CreateSelection(
            PdfRectangleZoomController controller,
            Rectangle bounds)
        {
            Assert(
                controller.BeginSelection(bounds.Location),
                "No se pudo iniciar la selección.");
            controller.UpdateSelection(
                new Point(bounds.Right, bounds.Bottom));
            Assert(
                controller.CompleteSelection(
                    new Point(bounds.Right, bounds.Bottom)),
                "No se pudo completar la selección.");
            PumpMessages(80);
        }

        private static void CreateFixture(string path)
        {
            using (var stream = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            {
                var document = new iTextSharp.text.Document(PageSize.A4);
                PdfWriter.GetInstance(document, stream);
                document.Open();
                document.Add(new Paragraph("PDF LIGERO / PRUEBA DE ENCUADRE"));
                document.Add(new Paragraph(
                    "Planta arquitectónica · detalle seleccionado"));
                var table = new PdfPTable(4)
                {
                    WidthPercentage = 88,
                    SpacingBefore = 36
                };
                for (var i = 0; i < 28; i++)
                {
                    table.AddCell("E" + (i + 1));
                }

                document.Add(table);
                document.NewPage();
                document.Add(new Paragraph("SEGUNDA PÁGINA"));
                document.Close();
            }
        }

        private static void Capture(Form form, string path)
        {
            using (var bitmap = new Bitmap(
                form.ClientSize.Width,
                form.ClientSize.Height))
            {
                form.DrawToBitmap(
                    bitmap,
                    new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(path);
            }
        }

        private static void PumpMessages(int milliseconds)
        {
            var until = Environment.TickCount + milliseconds;
            while (Environment.TickCount < until)
            {
                Application.DoEvents();
                Thread.Sleep(12);
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}

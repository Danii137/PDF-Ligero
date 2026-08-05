using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using iTextSharp.text;
using iTextSharp.text.pdf;
using PdfiumViewer;
using PdfDocument = iTextSharp.text.Document;
using PdfiumDocument = PdfiumViewer.PdfDocument;

namespace FirmaAutomatica
{
    internal static class PaperPreviewQa
    {
        private static readonly string OutputDirectory = Path.GetFullPath(
            Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "captures"));

        [STAThread]
        private static int Main()
        {
            var failures = new List<string>();
            var metrics = new List<string>();
            Directory.CreateDirectory(OutputDirectory);
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                var fixturePath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "formatos-mixtos.pdf");

                CreateFixture(fixturePath);
                ValidateFormatter(failures);
                ValidateViewerHeader(fixturePath, failures);
                ValidatePrintPreview(fixturePath, failures);

                var largeFixturePath = Path.GetFullPath(
                    Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory,
                        "..",
                        "validation-phase1-large",
                        "fixture-scanned-a4-32mb.pdf"));
                if (File.Exists(largeFixturePath))
                {
                    ValidateLargePrintPreview(
                        largeFixturePath,
                        failures,
                        metrics);
                }
            }
            catch (Exception ex)
            {
                failures.Add(
                    "FAIL: excepción no controlada: " + ex);
            }

            var reportPath = Path.Combine(
                OutputDirectory,
                "qa-report.txt");
            var report = new List<string>();
            report.Add(
                failures.Count == 0
                    ? "PASS: formato de página y vista previa validados."
                    : "FAIL: se detectaron incidencias.");
            report.AddRange(metrics);
            report.AddRange(failures);
            File.WriteAllLines(reportPath, report.ToArray());

            Console.WriteLine("captures=" + OutputDirectory);
            Console.WriteLine("failures=" + failures.Count);
            foreach (var failure in failures)
            {
                Console.WriteLine(failure);
            }

            return failures.Count == 0 ? 0 : 1;
        }

        private static void CreateFixture(string path)
        {
            using (var stream = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            using (var pdf = new PdfDocument(PageSize.A4))
            {
                PdfWriter.GetInstance(pdf, stream);
                pdf.Open();
                pdf.Add(new Paragraph(
                    "PAGINA 1 / A4 VERTICAL",
                    FontFactory.GetFont(
                        FontFactory.HELVETICA_BOLD,
                        20f)));
                pdf.Add(new Paragraph(
                    "Plano y memoria de prueba para PDF Ligero."));

                pdf.SetPageSize(PageSize.A3.Rotate());
                pdf.NewPage();
                pdf.Add(new Paragraph(
                    "PAGINA 2 / A3 HORIZONTAL",
                    FontFactory.GetFont(
                        FontFactory.HELVETICA_BOLD,
                        26f)));
                pdf.Add(new Paragraph(
                    "La cabecera debe cambiar de formato al navegar."));

                pdf.SetPageSize(new iTextSharp.text.Rectangle(400f, 700f));
                pdf.NewPage();
                pdf.Add(new Paragraph(
                    "PAGINA 3 / FORMATO PERSONALIZADO",
                    FontFactory.GetFont(
                        FontFactory.HELVETICA_BOLD,
                        18f)));
            }
        }

        private static void ValidateFormatter(IList<string> failures)
        {
            var a4 = PdfPageSizeFormatter.FromPoints(
                new SizeF(595.276f, 841.89f));
            Require(
                a4.StandardName == "A4",
                "El conversor no reconoce A4.",
                failures);
            Require(
                a4.MillimetreText == "210 × 297 mm",
                "Medidas A4 incorrectas: " + a4.MillimetreText,
                failures);
            Require(
                a4.OrientationName == "VERTICAL",
                "Orientación A4 incorrecta.",
                failures);

            var rotated = PdfPageSizeFormatter.FromPoints(
                new SizeF(595.276f, 841.89f),
                true);
            Require(
                rotated.StandardName == "A4" &&
                rotated.OrientationName == "HORIZONTAL" &&
                rotated.MillimetreText == "297 × 210 mm",
                "El giro visual no intercambia correctamente las medidas.",
                failures);

            var custom = PdfPageSizeFormatter.FromPoints(
                new SizeF(400f, 700f));
            Require(
                string.IsNullOrWhiteSpace(custom.StandardName),
                "Un formato personalizado se ha clasificado como estándar.",
                failures);
        }

        private static void ValidateViewerHeader(
            string fixturePath,
            IList<string> failures)
        {
            using (var form = new PdfViewerForm(
                new[] { fixturePath }))
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-30000, -30000);
                form.ShowInTaskbar = false;
                form.ClientSize = new Size(900, 620);
                form.Show();
                Pump(1300);

                var header = GetField<Control>(form, "headerPanel");
                var paperEyebrow = GetField<Label>(
                    form,
                    "paperEyebrowLabel");
                var paperSize = GetField<Label>(
                    form,
                    "paperSizeLabel");
                var previous = GetField<Button>(
                    form,
                    "previousPageButton");
                var next = GetField<Button>(
                    form,
                    "nextPageButton");
                var pageEditor = GetField<TextBox>(
                    form,
                    "currentPageTextBox");
                var pageTotal = GetField<Label>(
                    form,
                    "pageTotalLabel");

                Require(
                    header.Height == 50,
                    "La cabecera ha crecido y resta espacio al PDF.",
                    failures);
                Require(
                    paperEyebrow.Visible && paperSize.Visible,
                    "El formato de página no aparece en la cabecera.",
                    failures);
                Require(
                    paperSize.Text == "A4 · 210 × 297 mm",
                    "Texto de cabecera inesperado: " + paperSize.Text,
                    failures);
                Require(
                    header.ClientRectangle.Contains(paperSize.Bounds),
                    "Las medidas quedan fuera de la cabecera.",
                    failures);

                var navigationBounds = System.Drawing.Rectangle.Union(
                    System.Drawing.Rectangle.Union(
                        previous.Bounds,
                        next.Bounds),
                    System.Drawing.Rectangle.Union(
                        pageEditor.Bounds,
                        pageTotal.Bounds));
                Require(
                    !navigationBounds.IntersectsWith(paperSize.Bounds),
                    "Las medidas se solapan con la navegación.",
                    failures);

                SaveControl(
                    form,
                    Path.Combine(
                        OutputDirectory,
                        "01-cabecera-a4-900x620.png"));

                next.PerformClick();
                Pump(300);
                Require(
                    paperSize.Text == "A3 · 420 × 297 mm",
                    "La medida no cambia al navegar a A3: " +
                    paperSize.Text,
                    failures);
                Require(
                    paperEyebrow.Text.Contains("HORIZONTAL"),
                    "La orientación A3 no se actualiza.",
                    failures);

                InvokePrivate(
                    form,
                    "RotateActiveDocument",
                    true);
                Pump(150);
                Require(
                    paperSize.Text == "A3 · 297 × 420 mm",
                    "La medida no sigue el giro visual: " +
                    paperSize.Text,
                    failures);
            }
        }

        private static void ValidatePrintPreview(
            string fixturePath,
            IList<string> failures)
        {
            using (var document = PdfiumDocument.Load(fixturePath))
            using (var preview = new PdfPrintPreviewForm(
                document,
                Path.GetFileName(fixturePath),
                0))
            {
                preview.StartPosition = FormStartPosition.Manual;
                preview.Location = new Point(-30000, -30000);
                preview.ShowInTaskbar = false;
                preview.ClientSize = new Size(1060, 780);

                var watch = Stopwatch.StartNew();
                preview.Show();
                Pump(500);
                watch.Stop();

                var previewImage = GetField<System.Drawing.Image>(
                    preview,
                    "previewImage");
                var paperLabel = GetField<Label>(
                    preview,
                    "paperLabel");
                var surface = GetField<Control>(
                    preview,
                    "previewSurface");
                var footer = GetField<Control>(
                    preview,
                    "footerPanel");

                Require(
                    previewImage != null,
                    "La vista previa no ha renderizado la página.",
                    failures);
                Require(
                    paperLabel.Text == "A4 · 210 × 297 mm",
                    "La vista previa no muestra las medidas A4.",
                    failures);
                Require(
                    surface.ClientSize.Width > 600 &&
                    surface.ClientSize.Height > 480,
                    "El documento no dispone de suficiente área visible.",
                    failures);
                Require(
                    footer.Height == 60,
                    "La barra de impresión tiene una altura inesperada.",
                    failures);
                Require(
                    watch.ElapsedMilliseconds < 3000,
                    "La vista previa tardó demasiado: " +
                    watch.ElapsedMilliseconds + " ms.",
                    failures);

                SaveFormClient(
                    preview,
                    Path.Combine(
                        OutputDirectory,
                        "02-vista-previa-a4-1060x780.png"));

                preview.ClientSize = new Size(820, 600);
                Pump(150);
                SaveFormClient(
                    preview,
                    Path.Combine(
                        OutputDirectory,
                        "03-vista-previa-minima-820x600.png"));
            }
        }

        private static void ValidateLargePrintPreview(
            string fixturePath,
            IList<string> failures,
            IList<string> metrics)
        {
            using (var document = PdfiumDocument.Load(fixturePath))
            using (var preview = new PdfPrintPreviewForm(
                document,
                Path.GetFileName(fixturePath),
                0))
            {
                preview.StartPosition = FormStartPosition.Manual;
                preview.Location = new Point(-30000, -30000);
                preview.ShowInTaskbar = false;
                preview.ClientSize = new Size(1060, 780);

                var process = Process.GetCurrentProcess();
                var memoryBefore = process.PrivateMemorySize64;
                var watch = Stopwatch.StartNew();
                preview.Show();
                watch.Stop();
                Pump(100);
                process.Refresh();
                var memoryIncrease =
                    Math.Max(
                        0,
                        process.PrivateMemorySize64 - memoryBefore);
                var previewImage = GetField<System.Drawing.Image>(
                    preview,
                    "previewImage");

                metrics.Add(
                    "PERF: vista previa PDF escaneado de " +
                    new FileInfo(fixturePath).Length +
                    " bytes = " +
                    watch.ElapsedMilliseconds +
                    " ms; incremento privado observado = " +
                    memoryIncrease +
                    " bytes; bitmap = " +
                    (previewImage == null
                        ? "sin imagen"
                        : previewImage.Width + " × " +
                            previewImage.Height) +
                    ".");
                Require(
                    previewImage != null,
                    "El PDF escaneado grande no generó vista previa.",
                    failures);
                Require(
                    watch.ElapsedMilliseconds < 3000,
                    "La vista previa del escaneado grande tardó " +
                    watch.ElapsedMilliseconds + " ms.",
                    failures);
                Require(
                    memoryIncrease < 160L * 1024L * 1024L,
                    "La vista previa del escaneado grande aumentó " +
                    memoryIncrease + " bytes de memoria privada.",
                    failures);
            }
        }

        private static void SaveControl(Control control, string path)
        {
            using (var bitmap = new Bitmap(
                control.ClientSize.Width,
                control.ClientSize.Height,
                PixelFormat.Format32bppArgb))
            {
                control.DrawToBitmap(
                    bitmap,
                    new System.Drawing.Rectangle(
                        Point.Empty,
                        control.ClientSize));
                bitmap.Save(path, ImageFormat.Png);
            }
        }

        private static void SaveFormClient(Form form, string path)
        {
            using (var bitmap = new Bitmap(
                form.ClientSize.Width,
                form.ClientSize.Height,
                PixelFormat.Format32bppArgb))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(form.BackColor);
                foreach (Control child in form.Controls)
                {
                    if (child.Visible)
                    {
                        child.DrawToBitmap(bitmap, child.Bounds);
                    }
                }

                bitmap.Save(path, ImageFormat.Png);
            }
        }

        private static T GetField<T>(object target, string fieldName)
        {
            var field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            return (T)field.GetValue(target);
        }

        private static object InvokePrivate(
            object target,
            string methodName,
            params object[] arguments)
        {
            var method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            return method.Invoke(target, arguments);
        }

        private static void Require(
            bool condition,
            string message,
            IList<string> failures)
        {
            if (!condition)
            {
                failures.Add("FAIL: " + message);
            }
        }

        private static void Pump(int milliseconds)
        {
            var deadline = Environment.TickCount + milliseconds;
            while (Environment.TickCount < deadline)
            {
                Application.DoEvents();
                Thread.Sleep(15);
            }
        }
    }
}

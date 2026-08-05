using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using iTextSharp.text;
using iTextSharp.text.pdf;

namespace FirmaAutomatica
{
    internal static class PdfPlanComparisonSurfaceQa
    {
        private static readonly List<string> Report =
            new List<string>();

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.Error.WriteLine(
                    "Uso: PdfPlanComparisonSurfaceQa <carpeta-salida>");
                return 2;
            }

            var output = Path.GetFullPath(args[0]);
            Directory.CreateDirectory(output);
            var baselinePath = Path.Combine(
                output,
                "revision-A-ui.pdf");
            var revisedPath = Path.Combine(
                output,
                "revision-B-ui.pdf");
            var alternativePath = Path.Combine(
                output,
                "revision-C-ui.pdf");

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                CreateFixture(baselinePath, 0, false);
                CreateFixture(revisedPath, 7, true);
                CreateFixture(alternativePath, -5, true);
                var identities = new[]
                {
                    CaptureIdentity(baselinePath),
                    CaptureIdentity(revisedPath),
                    CaptureIdentity(alternativePath)
                };

                AssertCompleteSurface(
                    output,
                    baselinePath,
                    revisedPath,
                    alternativePath);
                AssertCancellation(
                    baselinePath,
                    revisedPath);
                foreach (var identity in identities)
                {
                    Require(
                        identity.IsUnchanged(),
                        "El original permanece idéntico: " +
                        Path.GetFileName(identity.Path) + ".");
                }

                Report.Add("RESULTADO: PASS");
                File.WriteAllLines(
                    Path.Combine(output, "qa-report.txt"),
                    Report.ToArray(),
                    Encoding.UTF8);
                Console.WriteLine(string.Join(
                    Environment.NewLine,
                    Report.ToArray()));
                return 0;
            }
            catch (Exception ex)
            {
                Report.Add("RESULTADO: FAIL");
                Report.Add(ex.ToString());
                File.WriteAllLines(
                    Path.Combine(output, "qa-report.txt"),
                    Report.ToArray(),
                    Encoding.UTF8);
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        private static void AssertCompleteSurface(
            string output,
            string baselinePath,
            string revisedPath,
            string alternativePath)
        {
            var baseline = new PdfPlanComparisonSource(
                "REV-A · estado anterior.pdf",
                baselinePath,
                0);
            var candidates =
                new List<PdfPlanComparisonSource>
                {
                    new PdfPlanComparisonSource(
                        "REV-B · propuesta.pdf",
                        revisedPath,
                        0),
                    new PdfPlanComparisonSource(
                        "REV-C · alternativa.pdf",
                        alternativePath,
                        1)
                };

            using (var host = new Form())
            using (var surface =
                new PdfPlanComparisonSurface(
                    baseline,
                    candidates))
            {
                host.Text = "QA comparación";
                host.StartPosition =
                    FormStartPosition.Manual;
                host.Location = new Point(20, 20);
                host.ClientSize = new Size(1220, 840);
                host.BackColor =
                    Color.FromArgb(234, 233, 230);
                surface.Bounds = host.ClientRectangle;
                surface.Anchor =
                    AnchorStyles.Top |
                    AnchorStyles.Bottom |
                    AnchorStyles.Left |
                    AnchorStyles.Right;
                host.Controls.Add(surface);
                host.Show();
                Pump(100);

                Require(
                    surface.CommandPanelForTesting.Height == 46,
                    "Barra de una fila en anchura normal.");
                Require(
                    surface.Controls.GetChildIndex(
                        surface.CommandPanelForTesting) == 0,
                    "La barra flota por encima del lienzo.");
                Require(
                    surface.RevisedSourceForTesting.Items.Count == 2,
                    "Selector B contiene las pestañas candidatas.");
                Require(
                    surface.RevisedSourceForTesting.AccessibleName ==
                        "Revisión B",
                    "Selector B accesible.");
                Require(
                    surface.CanvasForTesting.AccessibleName.IndexOf(
                        "comparación",
                        StringComparison.OrdinalIgnoreCase) >= 0,
                    "Lienzo accesible.");

                surface.Begin();
                WaitForRender(surface, 30000);
                AssertResultLimits(surface);
                Require(
                    surface.ViewModeForTesting ==
                        PdfPlanComparisonMode.Overlay,
                    "Superponer es el modo inicial.");
                Require(
                    surface.OpacityForTesting.Value == 50,
                    "Opacidad inicial del 50 %.");
                Require(
                    surface.DifferenceForTesting == null,
                    "Rojo/cian no ocupa memoria fuera de Diferencias.");
                Require(
                    surface.HeaderTitle.IndexOf(
                        "REV-A",
                        StringComparison.Ordinal) >= 0 &&
                    surface.HeaderTitle.IndexOf(
                        "REV-B",
                        StringComparison.Ordinal) >= 0,
                    "Cabecera A/B expuesta al visor.");
                Capture(
                    host,
                    Path.Combine(
                        output,
                        "01-superponer-ancho.png"));
                Report.Add(
                    "PASS · render inicial bajo demanda y barra ancha");

                surface.OpacityForTesting.Value = 35;
                Require(
                    surface.ProcessShortcutForTesting(Keys.D2),
                    "Atajo 2 gestionado.");
                Pump(80);
                Require(
                    surface.ViewModeForTesting ==
                        PdfPlanComparisonMode.RedCyan,
                    "Modo Diferencias seleccionable.");
                Require(
                    surface.DifferenceForTesting != null,
                    "Diferencias reutilizan el tercer bitmap.");
                Capture(
                    host,
                    Path.Combine(
                        output,
                        "02-diferencias-ancho.png"));

                Require(
                    surface.ProcessShortcutForTesting(Keys.D3),
                    "Atajo 3 gestionado.");
                Pump(40);
                Require(
                    surface.ViewModeForTesting ==
                        PdfPlanComparisonMode.Baseline,
                    "Modo Alternar seleccionable.");
                Require(
                    surface.DifferenceForTesting == null,
                    "Salir de Diferencias libera su bitmap.");
                var previousAlternate =
                    surface.CanvasForTesting
                        .AlternateShowingRevised;
                Require(
                    surface.ProcessShortcutForTesting(Keys.Space) &&
                    surface.CanvasForTesting
                        .AlternateShowingRevised !=
                        previousAlternate,
                    "Espacio alterna manualmente A/B.");
                Require(
                    surface.ProcessShortcutForTesting(Keys.D4),
                    "Atajo 4 gestionado.");
                surface.CanvasForTesting.SplitPosition = 0.37F;
                surface.CanvasForTesting.Invalidate();
                Pump(60);
                Require(
                    surface.ViewModeForTesting ==
                        PdfPlanComparisonMode.Split,
                    "Modo Dividir seleccionable.");
                Report.Add(
                    "PASS · Superponer/Diferencias/Alternar/Dividir");

                var previousB =
                    Decimal.ToInt32(
                        surface.RevisedPageForTesting.Value);
                surface.BaselinePageForTesting.Value = 2;
                Pump(80);
                Require(
                    Decimal.ToInt32(
                        surface.RevisedPageForTesting.Value) ==
                        previousB + 1,
                    "Páginas vinculadas avanzan juntas.");
                WaitForRender(surface, 30000);
                Require(
                    surface.BaselinePageForTesting.Maximum == 2 &&
                    surface.RevisedPageForTesting.Maximum == 2,
                    "Rangos de página proceden de la sesión.");

                surface.SwapForTesting.PerformClick();
                WaitForRender(surface, 30000);
                Require(
                    surface.HeaderTitle.StartsWith(
                        "REV-B",
                        StringComparison.Ordinal),
                    "Intercambiar convierte B en A.");
                Report.Add(
                    "PASS · vincular páginas, clamp e intercambio A/B");

                surface.AlignmentForTesting.SelectedIndex = 1;
                WaitForRender(surface, 45000);
                Require(
                    surface.ResultForTesting != null,
                    "Alineación automática termina con un par visible.");
                surface.AlignmentForTesting.SelectedIndex = 2;
                WaitForRender(surface, 30000);
                surface.OffsetXForTesting.Value = 2.5M;
                surface.OffsetYForTesting.Value = -1.5M;
                Pump(300);
                WaitForRender(surface, 30000);
                Require(
                    Math.Abs(
                        surface.ResultForTesting
                            .AppliedAdjustment
                            .OffsetXPoints -
                        2.5D * 72D / 25.4D) < 0.25D,
                    "Offset X manual aplicado en puntos.");
                Require(
                    Math.Abs(
                        surface.ResultForTesting
                            .AppliedAdjustment
                            .OffsetYPoints -
                        -1.5D * 72D / 25.4D) < 0.25D,
                    "Offset Y manual aplicado en puntos.");
                Report.Add(
                    "PASS · alineación automática y offset manual");

                host.ClientSize = new Size(900, 620);
                Pump(100);
                Require(
                    surface.CommandPanelForTesting.Height == 82,
                    "Barra responsive de dos filas a 900 × 620.");
                Require(
                    surface.CommandPanelForTesting.Right <=
                        surface.ClientSize.Width,
                    "Barra compacta dentro del visor.");
                Capture(
                    host,
                    Path.Combine(
                        output,
                        "03-manual-compacto.png"));

                surface.CollapseForTesting.PerformClick();
                Pump(40);
                Require(
                    !surface.CommandPanelForTesting.Visible &&
                    surface.RestoreForTesting.Visible,
                    "Controles plegables a un único botón A/B.");
                surface.RestoreForTesting.PerformClick();
                Pump(40);
                Require(
                    surface.CommandPanelForTesting.Visible,
                    "Controles recuperables sin perder el render.");
                Capture(
                    host,
                    Path.Combine(
                        output,
                        "04-restaurado-compacto.png"));

                var closeRequested = false;
                surface.CloseRequested += delegate
                {
                    closeRequested = true;
                };
                Require(
                    surface.ProcessShortcutForTesting(Keys.Escape),
                    "Esc gestionado por la superficie.");
                Require(
                    closeRequested,
                    "Esc publica CloseRequested.");
                Report.Add(
                    "PASS · responsive, plegado y contrato de cierre");
            }
        }

        private static void AssertResultLimits(
            PdfPlanComparisonSurface surface)
        {
            var result = surface.ResultForTesting;
            Require(
                result != null,
                "La superficie conserva un resultado válido.");
            Require(
                (long)result.PixelSize.Width *
                    result.PixelSize.Height <= 4000000L,
                "Render limitado a 4 millones de píxeles.");
            Require(
                result.EstimatedPeakMemoryBytes <=
                    128L * 1024L * 1024L,
                "Estimación de memoria bajo 128 MB.");
        }

        private static void AssertCancellation(
            string baselinePath,
            string revisedPath)
        {
            for (var cycle = 0; cycle < 3; cycle++)
            {
                using (var host = new Form())
                {
                    host.ClientSize = new Size(900, 620);
                    var surface =
                        new PdfPlanComparisonSurface(
                            new PdfPlanComparisonSource(
                                "A.pdf",
                                baselinePath,
                                cycle % 2),
                            new[]
                            {
                                new PdfPlanComparisonSource(
                                    "B.pdf",
                                    revisedPath,
                                    cycle % 2)
                            });
                    surface.Dock = DockStyle.Fill;
                    host.Controls.Add(surface);
                    host.Show();
                    Pump(30);
                    surface.Begin();
                    Pump(15);
                    surface.CancelAndDispose();
                    Pump(80);
                }
            }

            Report.Add(
                "PASS · tres cancelaciones/cierres durante render sin fuga visible");
        }

        private static void WaitForRender(
            PdfPlanComparisonSurface surface,
            int timeoutMilliseconds)
        {
            var deadline =
                DateTime.UtcNow.AddMilliseconds(
                    timeoutMilliseconds);
            do
            {
                Application.DoEvents();
                Thread.Sleep(20);
                if (!surface.IsBusy &&
                    surface.ResultForTesting != null)
                {
                    return;
                }
            }
            while (DateTime.UtcNow < deadline);

            throw new TimeoutException(
                "La comparación no terminó. Estado: " +
                surface.HeaderDetail);
        }

        private static void Capture(
            Form form,
            string path)
        {
            using (var bitmap = new Bitmap(
                Math.Max(1, form.ClientSize.Width),
                Math.Max(1, form.ClientSize.Height),
                System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
            {
                form.DrawToBitmap(
                    bitmap,
                    new System.Drawing.Rectangle(
                        Point.Empty,
                        form.ClientSize));

                var surface =
                    form.Controls.OfType<
                        PdfPlanComparisonSurface>()
                        .FirstOrDefault();
                if (surface != null)
                {
                    DrawFloatingControl(
                        bitmap,
                        form,
                        surface.CommandPanelForTesting);
                    foreach (Control child in
                        surface.Controls)
                    {
                        if (child.Visible &&
                            !ReferenceEquals(
                                child,
                                surface.CanvasForTesting) &&
                            !ReferenceEquals(
                                child,
                                surface.CommandPanelForTesting))
                        {
                            DrawFloatingControl(
                                bitmap,
                                form,
                                child);
                        }
                    }
                }

                Require(
                    CountApproximateColor(
                        bitmap,
                        Color.FromArgb(238, 91, 61),
                        4) >= 20,
                    "La captura contiene el acento coral de la barra.");
                Require(
                    CountDarkPixels(bitmap) >= 500,
                    "La captura contiene controles y geometría del plano.");
                bitmap.Save(
                    path,
                    System.Drawing.Imaging.ImageFormat.Png);
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
                    if (pixel.R < 80 &&
                        pixel.G < 80 &&
                        pixel.B < 80)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private static void DrawFloatingControl(
            Bitmap target,
            Form form,
            Control control)
        {
            if (control == null ||
                !control.Visible ||
                control.Width <= 0 ||
                control.Height <= 0)
            {
                return;
            }

            using (var controlBitmap = new Bitmap(
                control.Width,
                control.Height,
                System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
            using (var graphics = Graphics.FromImage(target))
            {
                control.DrawToBitmap(
                    controlBitmap,
                    control.ClientRectangle);
                var location = form.PointToClient(
                    control.PointToScreen(Point.Empty));
                graphics.DrawImageUnscaled(
                    controlBitmap,
                    location);
            }
        }

        private static void Pump(int milliseconds)
        {
            var deadline =
                DateTime.UtcNow.AddMilliseconds(milliseconds);
            do
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
            while (DateTime.UtcNow < deadline);
        }

        private static void CreateFixture(
            string path,
            int translationPoints,
            bool revised)
        {
            using (var stream = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            {
                var document = new Document(
                    PageSize.A3.Rotate(),
                    0,
                    0,
                    0,
                    0);
                var writer = PdfWriter.GetInstance(
                    document,
                    stream);
                writer.SetFullCompression();
                document.Open();
                DrawPlan(
                    writer.DirectContent,
                    PageSize.A3.Rotate(),
                    translationPoints,
                    revised,
                    1);
                document.SetPageSize(PageSize.A4);
                document.NewPage();
                DrawPlan(
                    writer.DirectContent,
                    PageSize.A4,
                    translationPoints,
                    revised,
                    2);
                document.Close();
            }
        }

        private static void DrawPlan(
            PdfContentByte canvas,
            iTextSharp.text.Rectangle page,
            int translationPoints,
            bool revised,
            int pageNumber)
        {
            var width = page.Width;
            var height = page.Height;
            canvas.SaveState();
            canvas.SetLineWidth(0.65F);
            canvas.SetColorStroke(
                new BaseColor(55, 56, 53));
            canvas.Rectangle(
                28,
                28,
                width - 56,
                height - 56);
            canvas.Stroke();

            canvas.ConcatCTM(
                1,
                0,
                0,
                1,
                translationPoints,
                -translationPoints / 2F);
            canvas.SetLineWidth(0.45F);
            canvas.SetColorStroke(
                new BaseColor(155, 154, 149));
            for (var x = 70F; x < width - 70F; x += 42F)
            {
                canvas.MoveTo(x, 70);
                canvas.LineTo(x, height - 70);
            }
            for (var y = 70F; y < height - 70F; y += 42F)
            {
                canvas.MoveTo(70, y);
                canvas.LineTo(width - 70, y);
            }
            canvas.Stroke();

            canvas.SetColorStroke(BaseColor.BLACK);
            canvas.SetLineWidth(3.2F);
            canvas.Rectangle(
                105,
                120,
                width * 0.58F,
                height * 0.48F);
            canvas.MoveTo(105 + width * 0.29F, 120);
            canvas.LineTo(
                105 + width * 0.29F,
                120 + height * 0.48F);
            canvas.Stroke();

            canvas.SetLineWidth(1.1F);
            for (var index = 0; index < 8; index++)
            {
                var cx = 135 + index * 58;
                var cy = 155 + (index % 3) * 72;
                canvas.Circle(cx, cy, 7);
                canvas.MoveTo(cx - 11, cy);
                canvas.LineTo(cx + 11, cy);
                canvas.MoveTo(cx, cy - 11);
                canvas.LineTo(cx, cy + 11);
            }
            canvas.Stroke();

            var font = BaseFont.CreateFont(
                BaseFont.HELVETICA,
                BaseFont.CP1252,
                BaseFont.NOT_EMBEDDED);
            canvas.BeginText();
            canvas.SetFontAndSize(font, 11);
            canvas.SetTextMatrix(78, height - 52);
            canvas.ShowText(
                "PLANO DE VALIDACIÓN / PÁGINA " +
                pageNumber +
                (revised ? " / REVISIÓN B" : " / REVISIÓN A"));
            canvas.SetFontAndSize(font, 7);
            for (var index = 0; index < 10; index++)
            {
                canvas.SetTextMatrix(
                    90 + index * 48,
                    92 + (index % 2) * 16);
                canvas.ShowText("EJE " + (index + 1));
            }
            canvas.EndText();
            canvas.RestoreState();

            if (revised)
            {
                canvas.SaveState();
                canvas.SetColorStroke(
                    new BaseColor(30, 30, 28));
                canvas.SetLineWidth(4F);
                canvas.Rectangle(
                    width * 0.68F,
                    height * 0.35F,
                    58,
                    96);
                canvas.Stroke();
                canvas.RestoreState();
            }
        }

        private static FileIdentity CaptureIdentity(string path)
        {
            return new FileIdentity(
                path,
                new FileInfo(path).Length,
                File.GetLastWriteTimeUtc(path),
                ComputeHash(path));
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

        private static void Require(
            bool condition,
            string message)
        {
            if (!condition)
            {
                throw new InvalidDataException(message);
            }
        }

        private sealed class FileIdentity
        {
            public FileIdentity(
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

            public bool IsUnchanged()
            {
                var info = new FileInfo(Path);
                return info.Exists &&
                    info.Length == Length &&
                    File.GetLastWriteTimeUtc(Path) == WriteUtc &&
                    string.Equals(
                        ComputeHash(Path),
                        Hash,
                        StringComparison.Ordinal);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using iTextSharp.text;
using iTextSharp.text.pdf;

namespace FirmaAutomatica
{
    internal static class PlanComparisonEngineQa
    {
        private static readonly List<string> Failures =
            new List<string>();
        private static readonly List<string> Metrics =
            new List<string>();

        private static int Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.Error.WriteLine(
                    "Uso: PlanComparisonEngineQa <run-dir>");
                return 2;
            }

            var runDirectory = Path.GetFullPath(args[0]);
            Directory.CreateDirectory(runDirectory);
            var baselinePath = Path.Combine(
                runDirectory,
                "plano-base-vectorial.pdf");
            var revisedPath = Path.Combine(
                runDirectory,
                "plano-revision-vectorial.pdf");
            CreateFixture(baselinePath, false);
            CreateFixture(revisedPath, true);
            var baselineHash = ComputeSha256(baselinePath);
            var revisedHash = ComputeSha256(revisedPath);
            var baselineWriteTime = File.GetLastWriteTimeUtc(
                baselinePath);
            var revisedWriteTime = File.GetLastWriteTimeUtc(
                revisedPath);

            try
            {
                ValidateSessionAndRender(
                    baselinePath,
                    revisedPath,
                    runDirectory);
                ValidateDifferentPageBoxes(
                    baselinePath,
                    revisedPath);
                ValidateCancellation(
                    baselinePath,
                    revisedPath);
                ValidateOneShot(
                    baselinePath,
                    revisedPath);
                ValidateCompositeMath();
            }
            catch (Exception ex)
            {
                Failures.Add(
                    "Excepcion no controlada: " + ex);
            }

            Require(
                baselineHash == ComputeSha256(baselinePath),
                "La comparacion modifico el PDF base.");
            Require(
                revisedHash == ComputeSha256(revisedPath),
                "La comparacion modifico el PDF revisado.");
            Require(
                baselineWriteTime ==
                File.GetLastWriteTimeUtc(baselinePath),
                "Cambio la fecha del PDF base.");
            Require(
                revisedWriteTime ==
                File.GetLastWriteTimeUtc(revisedPath),
                "Cambio la fecha del PDF revisado.");

            var report = new List<string>();
            report.Add(
                Failures.Count == 0
                    ? "PASS: motor de comparacion de planos validado."
                    : "FAIL: motor de comparacion con incidencias.");
            report.AddRange(Metrics);
            foreach (var failure in Failures)
            {
                report.Add("FAIL: " + failure);
            }

            var reportPath = Path.Combine(
                runDirectory,
                "qa-report.txt");
            File.WriteAllLines(reportPath, report.ToArray());
            Console.WriteLine(File.ReadAllText(reportPath));
            return Failures.Count == 0 ? 0 : 1;
        }

        private static void ValidateSessionAndRender(
            string baselinePath,
            string revisedPath,
            string runDirectory)
        {
            var settings = new PdfPlanComparisonSettings();
            settings.TargetDpi = 220;
            settings.MaximumPixelsPerPage = 4000000;
            settings.MaximumWorkingBytes =
                128L * 1024L * 1024L;
            settings.EstimateContentOffset = true;
            settings.MaximumAutoOffsetPixels = 24;

            PdfPlanComparisonResult result = null;
            using (var session =
                PdfPlanComparisonService.OpenSession(
                    baselinePath,
                    revisedPath,
                    settings,
                    CancellationToken.None))
            {
                Require(
                    session.BaselinePageCount == 2,
                    "Numero de paginas base incorrecto.");
                Require(
                    session.RevisedPageCount == 2,
                    "Numero de paginas revisadas incorrecto.");
                var baseSize = session.GetBaselinePageSize(0);
                Require(
                    baseSize.Width > baseSize.Height,
                    "La pagina A3 horizontal no se detecto.");

                result = session.Compare(
                    0,
                    0,
                    new PdfPlanPageAdjustment(),
                    CancellationToken.None);
            }

            using (result)
            {
                var pixels = (long)result.PixelSize.Width *
                    result.PixelSize.Height;
                Require(
                    pixels <= 4000000,
                    "Se supero el limite de 4 Mpx: " + pixels);
                Require(
                    result.EstimatedPeakMemoryBytes <=
                    128L * 1024L * 1024L,
                    "Se supero el limite estimado de memoria.");
                Require(
                    result.BaselineBitmap.Size ==
                    result.RevisedBitmap.Size,
                    "Las dos paginas no comparten lienzo.");
                Require(
                    result.BaselinePage.PageNumber == 1 &&
                    result.RevisedPage.PageNumber == 1,
                    "Metadatos de pagina incorrectos.");
                Require(
                    result.AlignmentSuggestion != null,
                    "No se genero sugerencia de alineacion.");
                if (result.AlignmentSuggestion != null)
                {
                    var suggested =
                        result.AlignmentSuggestion.Adjustment;
                    Metrics.Add(string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "alignment x={0:0.00}pt y={1:0.00}pt " +
                        "score={2:0.000} reliable={3}",
                        suggested.OffsetXPoints,
                        suggested.OffsetYPoints,
                        result.AlignmentSuggestion.Score,
                        result.AlignmentSuggestion.IsReliable));
                    Require(
                        Math.Abs(
                            suggested.OffsetXPoints + 8D) <= 3.5D,
                        "La sugerencia X no compensa el " +
                        "desplazamiento conocido.");
                    Require(
                        Math.Abs(
                            suggested.OffsetYPoints - 6D) <= 3.5D,
                        "La sugerencia Y no compensa el " +
                        "desplazamiento conocido.");
                }

                result.BaselineBitmap.Save(
                    Path.Combine(
                        runDirectory,
                        "normalized-base.png"),
                    ImageFormat.Png);
                result.RevisedBitmap.Save(
                    Path.Combine(
                        runDirectory,
                        "normalized-revision.png"),
                    ImageFormat.Png);
                ValidateComposite(
                    result,
                    PdfPlanComparisonMode.Overlay,
                    "overlay.png",
                    runDirectory);
                ValidateComposite(
                    result,
                    PdfPlanComparisonMode.RedCyan,
                    "red-cyan.png",
                    runDirectory);
                ValidateComposite(
                    result,
                    PdfPlanComparisonMode.Split,
                    "split.png",
                    runDirectory);

                using (var difference = result.CreateComposite(
                    PdfPlanComparisonMode.RedCyan))
                {
                    var colorCount = CountDifferenceColours(
                        difference);
                    Metrics.Add(
                        "red-cyan coloredSamples=" + colorCount);
                    Require(
                        colorCount > 20,
                        "El modo rojo/cian no resalta cambios.");
                }

                Metrics.Add(
                    "render pixels=" + pixels +
                    " actualDpi=" +
                    result.ActualDpi.ToString(
                        "0.0",
                        System.Globalization.CultureInfo.InvariantCulture) +
                    " peakMiB=" +
                    (result.EstimatedPeakMemoryBytes /
                    1048576D).ToString(
                        "0.0",
                        System.Globalization.CultureInfo.InvariantCulture));
            }

            var disposedThrows = false;
            try
            {
                var ignored = result.PixelSize;
            }
            catch (ObjectDisposedException)
            {
                disposedThrows = true;
            }

            Require(
                disposedThrows,
                "El resultado permite acceder tras Dispose.");
        }

        private static void ValidateDifferentPageBoxes(
            string baselinePath,
            string revisedPath)
        {
            var settings = new PdfPlanComparisonSettings();
            settings.TargetDpi = 120;
            settings.AlignmentBasis =
                PdfPlanAlignmentBasis.PhysicalPageBoxes;
            using (var session =
                PdfPlanComparisonService.OpenSession(
                    baselinePath,
                    revisedPath,
                    settings,
                    CancellationToken.None))
            using (var result = session.Compare(
                1,
                1,
                new PdfPlanPageAdjustment
                {
                    OffsetXPoints = 4D,
                    OffsetYPoints = -5D,
                    Scale = 1.01D,
                    RotationDegrees = 0.4D
                },
                CancellationToken.None))
            {
                Require(
                    result.BaselinePage.SizePoints !=
                    result.RevisedPage.SizePoints,
                    "El fixture no conserva cajas distintas.");
                Require(
                    result.BaselineBitmap.Size ==
                    result.RevisedBitmap.Size,
                    "Las cajas distintas no se normalizaron.");
                Require(
                    Math.Abs(
                        result.AppliedAdjustment.Scale -
                        1.01D) < 0.001D,
                    "No se aplico la escala manual.");
                Require(
                    Math.Abs(
                        result.AppliedAdjustment.RotationDegrees -
                        0.4D) < 0.001D,
                    "No se aplico la rotacion manual.");
            }
        }

        private static void ValidateCancellation(
            string baselinePath,
            string revisedPath)
        {
            var source = new CancellationTokenSource();
            source.Cancel();
            var openCancelled = false;
            try
            {
                PdfPlanComparisonService.OpenSession(
                    baselinePath,
                    revisedPath,
                    null,
                    source.Token);
            }
            catch (OperationCanceledException)
            {
                openCancelled = true;
            }

            Require(
                openCancelled,
                "OpenSession no respeta cancelacion.");

            using (var session =
                PdfPlanComparisonService.OpenSession(
                    baselinePath,
                    revisedPath,
                    null,
                    CancellationToken.None))
            {
                var compareCancelled = false;
                try
                {
                    session.Compare(
                        0,
                        0,
                        null,
                        source.Token);
                }
                catch (OperationCanceledException)
                {
                    compareCancelled = true;
                }

                Require(
                    compareCancelled,
                    "Compare no respeta cancelacion.");
            }
        }

        private static void ValidateOneShot(
            string baselinePath,
            string revisedPath)
        {
            using (var result = PdfPlanComparisonService.Compare(
                baselinePath,
                0,
                revisedPath,
                0,
                new PdfPlanComparisonSettings
                {
                    TargetDpi = 96,
                    MaximumPixelsPerPage = 1000000
                },
                null,
                CancellationToken.None))
            {
                Require(
                    (long)result.PixelSize.Width *
                    result.PixelSize.Height <= 1000000,
                    "La API de una llamada ignora su cap.");
            }
        }

        private static void ValidateCompositeMath()
        {
            using (var baseline = new Bitmap(
                2,
                1,
                PixelFormat.Format24bppRgb))
            using (var revised = new Bitmap(
                2,
                1,
                PixelFormat.Format24bppRgb))
            {
                baseline.SetPixel(0, 0, Color.Black);
                baseline.SetPixel(1, 0, Color.White);
                revised.SetPixel(0, 0, Color.White);
                revised.SetPixel(1, 0, Color.Black);
                using (var overlay =
                    PdfPlanComparisonService.CreateComposite(
                        baseline,
                        revised,
                        PdfPlanComparisonMode.Overlay,
                        0.5F,
                        0.5F,
                        CancellationToken.None))
                {
                    var blended = overlay.GetPixel(0, 0);
                    Metrics.Add(
                        "overlay50 sample=" +
                        blended.R + "," +
                        blended.G + "," +
                        blended.B);
                    Require(
                        blended.R > 70 && blended.R < 210,
                        "Overlay no mezcla la opacidad.");
                }

                using (var redCyan =
                    PdfPlanComparisonService.CreateComposite(
                        baseline,
                        revised,
                        PdfPlanComparisonMode.RedCyan,
                        0.5F,
                        0.5F,
                        CancellationToken.None))
                {
                    var removed = redCyan.GetPixel(0, 0);
                    var added = redCyan.GetPixel(1, 0);
                    Require(
                        removed.R > 240 &&
                        removed.G < 15 &&
                        removed.B < 15,
                        "La geometria eliminada no aparece roja.");
                    Require(
                        added.R < 15 &&
                        added.G > 240 &&
                        added.B > 240,
                        "La geometria nueva no aparece cian.");
                }
            }
        }

        private static void ValidateComposite(
            PdfPlanComparisonResult result,
            PdfPlanComparisonMode mode,
            string fileName,
            string runDirectory)
        {
            using (var image = result.CreateComposite(
                mode,
                0.55F,
                0.5F,
                CancellationToken.None))
            {
                Require(
                    image.Size == result.PixelSize,
                    "Tamano incorrecto en modo " + mode + ".");
                image.Save(
                    Path.Combine(runDirectory, fileName),
                    ImageFormat.Png);
            }
        }

        private static int CountDifferenceColours(Bitmap image)
        {
            var count = 0;
            var stepX = Math.Max(1, image.Width / 180);
            var stepY = Math.Max(1, image.Height / 120);
            for (var y = 0; y < image.Height; y += stepY)
            {
                for (var x = 0; x < image.Width; x += stepX)
                {
                    var color = image.GetPixel(x, y);
                    var red = color.R > color.G + 28 &&
                        color.R > color.B + 28;
                    var cyan = color.G > color.R + 28 &&
                        color.B > color.R + 28;
                    if (red || cyan)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private static void CreateFixture(
            string path,
            bool revised)
        {
            var firstSize = PageSize.A3.Rotate();
            using (var stream = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            using (var document = new Document(
                firstSize,
                0,
                0,
                0,
                0))
            {
                var writer = PdfWriter.GetInstance(
                    document,
                    stream);
                document.Open();
                DrawPlan(
                    writer.DirectContent,
                    firstSize.Width,
                    firstSize.Height,
                    revised,
                    revised ? 8F : 0F,
                    revised ? 6F : 0F);

                var secondSize = revised
                    ? new iTextSharp.text.Rectangle(650F, 440F)
                    : PageSize.A4.Rotate();
                document.SetPageSize(secondSize);
                document.NewPage();
                DrawPlan(
                    writer.DirectContent,
                    secondSize.Width,
                    secondSize.Height,
                    revised,
                    revised ? -4F : 0F,
                    revised ? -5F : 0F);
            }
        }

        private static void DrawPlan(
            PdfContentByte canvas,
            float width,
            float height,
            bool revised,
            float translateX,
            float translateY)
        {
            canvas.SaveState();
            canvas.ConcatCTM(
                1F,
                0F,
                0F,
                1F,
                translateX,
                translateY);
            canvas.SetLineWidth(1.2F);
            canvas.SetColorStroke(BaseColor.BLACK);
            canvas.Rectangle(
                54F,
                54F,
                width - 108F,
                height - 108F);
            canvas.Stroke();

            canvas.SetLineWidth(2.2F);
            canvas.MoveTo(145F, 110F);
            canvas.LineTo(145F, height - 110F);
            canvas.MoveTo(300F, 110F);
            canvas.LineTo(300F, height - 110F);
            canvas.MoveTo(470F, 110F);
            canvas.LineTo(470F, height - 110F);
            canvas.MoveTo(90F, 230F);
            canvas.LineTo(width - 90F, 230F);
            canvas.MoveTo(90F, 390F);
            canvas.LineTo(width - 90F, 390F);
            canvas.Stroke();

            canvas.SetLineWidth(0.7F);
            for (var x = 80F; x < width - 80F; x += 42F)
            {
                canvas.MoveTo(x, 72F);
                canvas.LineTo(x, 88F);
            }

            canvas.Stroke();
            canvas.BeginText();
            canvas.SetFontAndSize(
                BaseFont.CreateFont(
                    BaseFont.HELVETICA_BOLD,
                    BaseFont.WINANSI,
                    false),
                16F);
            canvas.SetTextMatrix(72F, height - 38F);
            canvas.ShowText(
                revised
                    ? "REVISION B / PLANO VECTORIAL"
                    : "BASE A / PLANO VECTORIAL");
            canvas.EndText();
            canvas.RestoreState();

            if (revised)
            {
                canvas.SaveState();
                canvas.SetColorStroke(new BaseColor(20, 20, 20));
                canvas.SetLineWidth(4F);
                canvas.Circle(
                    width - 155F,
                    height - 155F,
                    34F);
                canvas.Stroke();
                canvas.MoveTo(width - 240F, 128F);
                canvas.LineTo(width - 110F, 175F);
                canvas.Stroke();
                canvas.RestoreState();
            }
        }

        private static string ComputeSha256(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var algorithm = SHA256.Create())
            {
                return BitConverter.ToString(
                    algorithm.ComputeHash(stream));
            }
        }

        private static void Require(
            bool condition,
            string message)
        {
            if (!condition)
            {
                Failures.Add(message);
            }
        }
    }
}

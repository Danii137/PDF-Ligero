using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FirmaAutomatica
{
    internal static class PlanComparisonEngineQa
    {
        private const long ProcessMemoryCeiling =
            448L * 1024L * 1024L;
        private const int MaximumPixels = 4000000;
        private static readonly List<string> Report =
            new List<string>();

        private sealed class SourceIdentity
        {
            public string Hash;
            public long Length;
            public DateTime LastWriteUtc;
        }

        private sealed class ExpectedPage
        {
            public int PageIndex;
            public double ExpectedCompensationX;
            public double ExpectedCompensationY;
            public ExpectedRegion[] Regions;
        }

        private sealed class ExpectedRegion
        {
            public double Left;
            public double Bottom;
            public double Right;
            public double Top;

            public ExpectedRegion(
                double left,
                double bottom,
                double right,
                double top)
            {
                Left = left;
                Bottom = bottom;
                Right = right;
                Top = top;
            }
        }

        private static readonly ExpectedPage[] ExpectedPages =
        {
            new ExpectedPage
            {
                PageIndex = 0,
                ExpectedCompensationX = -8D,
                ExpectedCompensationY = -22D,
                Regions = new[]
                {
                    new ExpectedRegion(680D, 175D, 752D, 430D),
                    new ExpectedRegion(548D, 492D, 606D, 550D),
                    new ExpectedRegion(520D, 650D, 870D, 700D),
                    new ExpectedRegion(1060D, 62D, 1170D, 118D)
                }
            },
            new ExpectedPage
            {
                PageIndex = 1,
                ExpectedCompensationX = -6D,
                ExpectedCompensationY = -13.5D,
                Regions = new[]
                {
                    new ExpectedRegion(250D, 540D, 455D, 670D),
                    new ExpectedRegion(405D, 235D, 480D, 535D),
                    new ExpectedRegion(70D, 690D, 510D, 750D),
                    new ExpectedRegion(480D, 62D, 570D, 118D)
                }
            }
        };

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.Error.WriteLine(
                    "Uso: PlanComparisonEngineQa <carpeta-run>");
                return 2;
            }

            var run = Path.GetFullPath(args[0]);
            var sourceA = Path.Combine(run, "revision-A.pdf");
            var sourceB = Path.Combine(run, "revision-B.pdf");
            try
            {
                Require(
                    File.Exists(sourceA) && File.Exists(sourceB),
                    "No existen los fixtures.");
                var identityA = CaptureIdentity(sourceA);
                var identityB = CaptureIdentity(sourceB);
                Report.Add("QA MOTOR / COMPARACIÓN DE PLANOS");
                Report.Add(
                    "Inicio UTC: " +
                    DateTime.UtcNow.ToString(
                        "O",
                        CultureInfo.InvariantCulture));

                TestOpenAndCompare(
                    run,
                    sourceA,
                    sourceB,
                    identityA,
                    identityB);
                TestCancellation(
                    run,
                    sourceA,
                    sourceB,
                    identityA,
                    identityB);
                AssertIdentity(sourceA, identityA);
                AssertIdentity(sourceB, identityB);
                AssertExclusiveReadable(sourceA);
                AssertExclusiveReadable(sourceB);

                var peak =
                    Process.GetCurrentProcess().PeakWorkingSet64;
                Report.Add(
                    "Pico de memoria del proceso: " +
                    FormatMiB(peak));
                Require(
                    peak <= ProcessMemoryCeiling,
                    "El proceso superó el techo QA de memoria.");
                Report.Add(
                    "PASS recursos: pico <= " +
                    FormatMiB(ProcessMemoryCeiling) +
                    ".");
                Report.Add(
                    "PASS originales: SHA-256, longitud y fecha intactos.");
                Report.Add("RESULTADO GLOBAL: PASS");
                WriteReport(run);
                Console.WriteLine("PASS");
                return 0;
            }
            catch (Exception ex)
            {
                Report.Add("RESULTADO GLOBAL: FAIL");
                Report.Add(ex.ToString());
                WriteReport(run);
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }

        private static void TestOpenAndCompare(
            string run,
            string sourceA,
            string sourceB,
            SourceIdentity identityA,
            SourceIdentity identityB)
        {
            var settings = CreateSettings();
            PdfPlanComparisonSession session = null;
            try
            {
                session = PdfPlanComparisonService.OpenSession(
                    sourceA,
                    sourceB,
                    settings,
                    CancellationToken.None);
                Require(
                    session.BaselinePageCount == 2 &&
                    session.RevisedPageCount == 2,
                    "La sesión no expone dos páginas por revisión.");
                ValidatePageSizes(session);

                foreach (var expected in ExpectedPages)
                {
                    TestPage(run, session, settings, expected);
                    AssertIdentity(sourceA, identityA);
                    AssertIdentity(sourceB, identityB);
                }
            }
            finally
            {
                if (session != null)
                {
                    session.Dispose();
                }
            }

            var disposedRejected = false;
            try
            {
                var ignored = session.BaselinePageCount;
            }
            catch (ObjectDisposedException)
            {
                disposedRejected = true;
            }

            Require(
                disposedRejected,
                "La sesión usada permite acceso después de Dispose.");
            Report.Add(
                "PASS sesión: carga perezosa, páginas explícitas y liberación.");
        }

        private static void TestPage(
            string run,
            PdfPlanComparisonSession session,
            PdfPlanComparisonSettings settings,
            ExpectedPage expected)
        {
            PdfPlanPageAdjustment suggestion;
            double suggestionScore;
            using (var initial = session.Compare(
                expected.PageIndex,
                expected.PageIndex,
                null,
                CancellationToken.None))
            {
                Require(
                    initial.AlignmentSuggestion != null,
                    "No se generó sugerencia de alineación.");
                Require(
                    initial.AlignmentSuggestion.IsReliable,
                    "La alineación automática no se considera fiable.");
                suggestion =
                    initial.AlignmentSuggestion.Adjustment.Clone();
                suggestionScore =
                    initial.AlignmentSuggestion.Score;
                RequireNear(
                    suggestion.OffsetXPoints,
                    expected.ExpectedCompensationX,
                    3.25D,
                    "Offset X automático");
                RequireNear(
                    suggestion.OffsetYPoints,
                    expected.ExpectedCompensationY,
                    3.25D,
                    "Offset Y automático");
                RequireNear(
                    suggestion.Scale,
                    1D,
                    0.025D,
                    "Escala automática");
                RequireNear(
                    suggestion.RotationDegrees,
                    0D,
                    0.35D,
                    "Rotación automática");
            }

            using (var result = session.Compare(
                expected.PageIndex,
                expected.PageIndex,
                suggestion,
                CancellationToken.None))
            {
                var pixels =
                    (long)result.PixelSize.Width *
                    result.PixelSize.Height;
                Require(
                    pixels > 250000L &&
                    pixels <= MaximumPixels,
                    "El render no respeta el límite de píxeles.");
                Require(
                    result.EstimatedPeakMemoryBytes <=
                    settings.MaximumWorkingBytes,
                    "La estimación supera el presupuesto de memoria.");
                Require(
                    result.BaselineBitmap.Size ==
                    result.RevisedBitmap.Size,
                    "Los renders normalizados difieren en tamaño.");
                RequireNear(
                    result.AppliedAdjustment.OffsetXPoints,
                    suggestion.OffsetXPoints,
                    0.001D,
                    "Ajuste X aplicado");
                RequireNear(
                    result.AppliedAdjustment.OffsetYPoints,
                    suggestion.OffsetYPoints,
                    0.001D,
                    "Ajuste Y aplicado");

                var label = (expected.PageIndex + 1).ToString(
                    CultureInfo.InvariantCulture);
                result.BaselineBitmap.Save(
                    Path.Combine(
                        run,
                        "engine-page-" +
                        label +
                        "-baseline.png"),
                    ImageFormat.Png);
                result.RevisedBitmap.Save(
                    Path.Combine(
                        run,
                        "engine-page-" +
                        label +
                        "-revised-aligned.png"),
                    ImageFormat.Png);

                SaveComposite(
                    result,
                    PdfPlanComparisonMode.Overlay,
                    Path.Combine(
                        run,
                        "engine-page-" +
                        label +
                        "-overlay.png"));
                SaveComposite(
                    result,
                    PdfPlanComparisonMode.RedCyan,
                    Path.Combine(
                        run,
                        "engine-page-" +
                        label +
                        "-red-cyan.png"));
                SaveComposite(
                    result,
                    PdfPlanComparisonMode.Split,
                    Path.Combine(
                        run,
                        "engine-page-" +
                        label +
                        "-split.png"));
                ValidateDifferenceLocalization(
                    result,
                    expected);

                var canceled = new CancellationTokenSource();
                canceled.Cancel();
                var compositeCanceled = false;
                try
                {
                    using (var ignored = result.CreateComposite(
                        PdfPlanComparisonMode.RedCyan,
                        0.5F,
                        0.5F,
                        canceled.Token))
                    {
                    }
                }
                catch (OperationCanceledException)
                {
                    compositeCanceled = true;
                }
                finally
                {
                    canceled.Dispose();
                }

                Require(
                    compositeCanceled,
                    "CreateComposite ignora un token cancelado.");
                Report.Add(
                    "PASS página " +
                    label +
                    ": alineación=(" +
                    suggestion.OffsetXPoints.ToString(
                        "0.00",
                        CultureInfo.InvariantCulture) +
                    ", " +
                    suggestion.OffsetYPoints.ToString(
                        "0.00",
                        CultureInfo.InvariantCulture) +
                    ") pt; score=" +
                    suggestionScore.ToString(
                        "0.000",
                        CultureInfo.InvariantCulture) +
                    "; " +
                    result.PixelSize.Width.ToString(
                        CultureInfo.InvariantCulture) +
                    "x" +
                    result.PixelSize.Height.ToString(
                        CultureInfo.InvariantCulture) +
                    "; memoria estimada=" +
                    FormatMiB(
                        result.EstimatedPeakMemoryBytes) +
                    ".");
            }
        }

        private static void ValidatePageSizes(
            PdfPlanComparisonSession session)
        {
            var a1 = session.GetBaselinePageSize(0);
            var b1 = session.GetRevisedPageSize(0);
            var a2 = session.GetBaselinePageSize(1);
            var b2 = session.GetRevisedPageSize(1);
            RequireNear(a1.Width, 1190D, 0.1D, "Ancho A página 1");
            RequireNear(a1.Height, 842D, 0.1D, "Alto A página 1");
            RequireNear(b1.Width, 1204D, 0.1D, "Ancho B página 1");
            RequireNear(b1.Height, 856D, 0.1D, "Alto B página 1");
            RequireNear(a2.Width, 595D, 0.1D, "Ancho A página 2");
            RequireNear(a2.Height, 842D, 0.1D, "Alto A página 2");
            RequireNear(b2.Width, 603D, 0.1D, "Ancho B página 2");
            RequireNear(b2.Height, 849D, 0.1D, "Alto B página 2");
        }

        private static void SaveComposite(
            PdfPlanComparisonResult result,
            PdfPlanComparisonMode mode,
            string path)
        {
            using (var bitmap = result.CreateComposite(mode))
            {
                Require(
                    bitmap.Size == result.PixelSize,
                    "El compuesto no conserva el tamaño normalizado.");
                bitmap.Save(path, ImageFormat.Png);
            }
        }

        private static void ValidateDifferenceLocalization(
            PdfPlanComparisonResult result,
            ExpectedPage expected)
        {
            var width = result.PixelSize.Width;
            var height = result.PixelSize.Height;
            var scaleX =
                width /
                (double)result.BaselinePage.SizePoints.Width;
            var scaleY =
                height /
                (double)result.BaselinePage.SizePoints.Height;
            long changed = 0L;
            long inside = 0L;
            long outside = 0L;
            var regionHits = new long[
                expected.Regions.Length];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var a = result.BaselineBitmap.GetPixel(x, y);
                    var b = result.RevisedBitmap.GetPixel(x, y);
                    if (Math.Abs(Gray(a) - Gray(b)) < 38)
                    {
                        continue;
                    }

                    changed++;
                    var pointX = x / scaleX;
                    var pointY =
                        result.BaselinePage.SizePoints.Height -
                        y / scaleY;
                    var isInside = false;
                    for (var regionIndex = 0;
                        regionIndex < expected.Regions.Length;
                        regionIndex++)
                    {
                        if (!ContainsExpected(
                            expected.Regions[regionIndex],
                            pointX,
                            pointY,
                            20D))
                        {
                            continue;
                        }

                        regionHits[regionIndex]++;
                        isInside = true;
                    }

                    if (isInside)
                    {
                        inside++;
                    }
                    else
                    {
                        outside++;
                    }
                }
            }

            var area = (long)width * height;
            Require(
                changed >= 300L,
                "No se detectan cambios tras alinear.");
            Require(
                changed <= area / 8L,
                "El resultado marca más del 12,5 % de la hoja.");
            Require(
                outside <= area * 22L / 1000L,
                "El ruido fuera de cambios supera el 2,2 %.");
            Require(
                inside >= 120L,
                "Los cambios esperados no aparecen.");
            for (var regionIndex = 0;
                regionIndex < regionHits.Length;
                regionIndex++)
            {
                Require(
                    regionHits[regionIndex] >= 40L,
                    "La región esperada " +
                    (regionIndex + 1).ToString(
                        CultureInfo.InvariantCulture) +
                    " no conserva una diferencia visible.");
            }
            Report.Add(
                "Diferencia página " +
                (expected.PageIndex + 1).ToString(
                    CultureInfo.InvariantCulture) +
                ": total=" +
                changed.ToString(CultureInfo.InvariantCulture) +
                "; dentro=" +
                inside.ToString(CultureInfo.InvariantCulture) +
                "; fuera=" +
                outside.ToString(CultureInfo.InvariantCulture) +
                ".");
        }

        private static bool ContainsExpected(
            ExpectedRegion region,
            double x,
            double y,
            double margin)
        {
            return x >= region.Left - margin &&
                x <= region.Right + margin &&
                y >= region.Bottom - margin &&
                y <= region.Top + margin;
        }

        private static void TestCancellation(
            string run,
            string sourceA,
            string sourceB,
            SourceIdentity identityA,
            SourceIdentity identityB)
        {
            var canceled = new CancellationTokenSource();
            canceled.Cancel();
            var openCanceled = false;
            try
            {
                using (PdfPlanComparisonService.OpenSession(
                    sourceA,
                    sourceB,
                    CreateSettings(),
                    canceled.Token))
                {
                }
            }
            catch (OperationCanceledException)
            {
                openCanceled = true;
            }
            finally
            {
                canceled.Dispose();
            }

            Require(
                openCanceled,
                "OpenSession ignora un token cancelado.");

            var settings = CreateSettings();
            settings.TargetDpi = 300;
            settings.MaximumPixelsPerPage = 12000000;
            settings.MaximumWorkingBytes =
                256L * 1024L * 1024L;
            settings.MaximumAutoOffsetPixels = 64;
            using (var session =
                PdfPlanComparisonService.OpenSession(
                    sourceA,
                    sourceB,
                    settings,
                    CancellationToken.None))
            using (var cancellation =
                new CancellationTokenSource())
            {
                cancellation.CancelAfter(2);
                var compareCanceled = false;
                try
                {
                    using (session.Compare(
                        0,
                        0,
                        null,
                        cancellation.Token))
                    {
                    }
                }
                catch (OperationCanceledException)
                {
                    compareCanceled = true;
                }

                Require(
                    compareCanceled,
                    "Compare no se canceló durante un render grande.");
            }

            AssertIdentity(sourceA, identityA);
            AssertIdentity(sourceB, identityB);
            var forbidden = Directory.GetFiles(
                run,
                "*cancel*",
                SearchOption.AllDirectories);
            Require(
                forbidden.Length == 0,
                "La cancelación dejó una salida parcial.");
            Report.Add(
                "PASS cancelación: apertura pre-cancelada y render " +
                "interrumpido sin salida parcial.");
        }

        private static PdfPlanComparisonSettings CreateSettings()
        {
            return new PdfPlanComparisonSettings
            {
                TargetDpi = 120,
                MaximumPixelsPerPage = MaximumPixels,
                MaximumWorkingBytes =
                    128L * 1024L * 1024L,
                AlignmentBasis =
                    PdfPlanAlignmentBasis.PhysicalPageBoxes,
                EstimateContentOffset = true,
                MaximumAutoOffsetPixels = 48,
                RenderAnnotations = true,
                OverlayOpacity = 0.5F,
                SplitPosition = 0.5F
            };
        }

        private static SourceIdentity CaptureIdentity(
            string path)
        {
            var info = new FileInfo(path);
            return new SourceIdentity
            {
                Hash = HashFile(path),
                Length = info.Length,
                LastWriteUtc = info.LastWriteTimeUtc
            };
        }

        private static void AssertIdentity(
            string path,
            SourceIdentity expected)
        {
            var current = new FileInfo(path);
            Require(
                current.Exists &&
                current.Length == expected.Length &&
                current.LastWriteTimeUtc == expected.LastWriteUtc &&
                string.Equals(
                    HashFile(path),
                    expected.Hash,
                    StringComparison.Ordinal),
                "Cambió el original " +
                Path.GetFileName(path) +
                ".");
        }

        private static void AssertExclusiveReadable(string path)
        {
            using (File.Open(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.None))
            {
            }
        }

        private static int Gray(Color color)
        {
            return (color.R * 30 +
                color.G * 59 +
                color.B * 11) / 100;
        }

        private static string HashFile(string path)
        {
            using (var input = File.OpenRead(path))
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(
                        sha.ComputeHash(input))
                    .Replace("-", string.Empty);
            }
        }

        private static void RequireNear(
            double actual,
            double expected,
            double tolerance,
            string label)
        {
            Require(
                Math.Abs(actual - expected) <= tolerance,
                label +
                " inesperado: " +
                actual.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture) +
                "; esperado " +
                expected.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture) +
                " ± " +
                tolerance.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture) +
                ".");
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

        private static string FormatMiB(long bytes)
        {
            return (bytes / 1048576D).ToString(
                "0.0",
                CultureInfo.InvariantCulture) +
                " MiB";
        }

        private static void WriteReport(string run)
        {
            try
            {
                File.WriteAllLines(
                    Path.Combine(run, "engine-qa-report.txt"),
                    Report.ToArray(),
                    new UTF8Encoding(true));
            }
            catch
            {
            }
        }
    }
}

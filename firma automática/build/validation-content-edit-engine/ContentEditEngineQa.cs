using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using iTextSharp.text;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.security;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using PdfiumDocument = PdfiumViewer.PdfDocument;
using PdfRectangle = iTextSharp.text.Rectangle;
using PdfTextExtractor = iTextSharp.text.pdf.parser.PdfTextExtractor;

namespace FirmaAutomatica
{
    internal static class ContentEditEngineQa
    {
        private static readonly int[] Rotations = { 0, 90, 180, 270 };
        private static readonly Color CoverColor =
            Color.FromArgb(238, 91, 61);

        private static int Main()
        {
            var runDirectory = Path.Combine(
                Path.GetDirectoryName(typeof(ContentEditEngineQa)
                    .Assembly.Location),
                "run-" + DateTime.Now.ToString(
                    "yyyyMMdd-HHmmss-fff",
                    CultureInfo.InvariantCulture));
            Directory.CreateDirectory(runDirectory);
            var report = new List<string>();

            try
            {
                var sourcePath = Path.Combine(
                    runDirectory,
                    "origen rotado con formulario.pdf");
                CreateFixture(sourcePath);
                AssertFixtureHasRealXmpAndUserUnit(sourcePath);
                report.Add(
                    "PASS fixture XMP descriptivo real y /UserUnit=2");
                TestXmpSemanticValidation(sourcePath, report);
                var sourceHash = ComputeHash(sourcePath);
                var sourceLength = new FileInfo(sourcePath).Length;
                var sourceWriteTime = File.GetLastWriteTimeUtc(sourcePath);

                TestViewerBoundsFactory(report);
                TestCombinedPreparation(sourcePath, report);
                for (var pageIndex = 0;
                    pageIndex < Rotations.Length;
                    pageIndex++)
                {
                    TestRotation(
                        sourcePath,
                        runDirectory,
                        pageIndex,
                        sourceHash,
                        report);
                }

                Assert(
                    string.Equals(
                        sourceHash,
                        ComputeHash(sourcePath),
                        StringComparison.Ordinal),
                    "El original cambio durante las pruebas.");
                Assert(
                    sourceLength == new FileInfo(sourcePath).Length,
                    "Cambio la longitud del original.");
                Assert(
                    sourceWriteTime == File.GetLastWriteTimeUtc(sourcePath),
                    "Cambio la fecha del original.");

                TestSourceIdentity(sourcePath, runDirectory, report);
                TestExternalReplacementRace(
                    sourcePath,
                    runDirectory,
                    report);
                TestXfaBlock(sourcePath, runDirectory, report);
                TestSignedRevision(sourcePath, runDirectory, report);
                TestCjkAndEmoji(sourcePath, runDirectory, report);
                AssertNoTemporaryFiles(runDirectory);

                report.Add("PASS original SHA/longitud/fecha intactos");
                report.Add("PASS cero temporales residuales");
                WriteReport(runDirectory, report, true, null);
                Console.WriteLine("PASS");
                Console.WriteLine(runDirectory);
                return 0;
            }
            catch (Exception ex)
            {
                WriteReport(runDirectory, report, false, ex);
                Console.Error.WriteLine("FAIL: " + ex);
                Console.Error.WriteLine(runDirectory);
                return 1;
            }
        }

        private static void TestViewerBoundsFactory(IList<string> report)
        {
            var region = PdfTextEditRegion.FromViewerBounds(
                1,
                new RectangleF(150F, 260F, 300F, 210F),
                new RectangleF(50F, 50F, 500F, 700F));
            AssertNearlyEqual(0.2D, region.LeftRatio, "viewer left");
            AssertNearlyEqual(0.3D, region.TopRatio, "viewer top");
            AssertNearlyEqual(0.8D, region.RightRatio, "viewer right");
            AssertNearlyEqual(0.6D, region.BottomRatio, "viewer bottom");
            report.Add("PASS selector WinForms top-left -> ratios");
        }

        private static void TestCombinedPreparation(
            string sourcePath,
            IList<string> report)
        {
            var expectedRegion = new PdfTextEditRegion(
                1,
                0.18D,
                0.24D,
                0.82D,
                0.48D);
            RectangleF rawBounds;
            using (var reader = new PdfReader(sourcePath))
            {
                var raw = PdfTextPageTransform
                    .Create(reader, 1)
                    .GetRawRectangle(expectedRegion);
                rawBounds = RectangleF.FromLTRB(
                    raw.Left,
                    raw.Top,
                    raw.Right,
                    raw.Bottom);
            }

            var legacyCounts = new Dictionary<string, int>(
                StringComparer.Ordinal);
            var combinedCounts = new Dictionary<string, int>(
                StringComparer.Ordinal);
            var legacyWatch = Stopwatch.StartNew();
            PdfTextEditAnalysis legacyAnalysis;
            PdfTextEditRegion legacyRegion;
            string legacyText;
            string legacyError;
            PdfTextEditService.DiagnosticIoActivityForTesting =
                delegate(string activity)
                {
                    IncrementCount(legacyCounts, activity);
                };
            try
            {
                legacyAnalysis = PdfTextEditService.Analyze(sourcePath);
                legacyRegion =
                    PdfTextEditService.CreateRegionFromPdfBounds(
                        legacyAnalysis,
                        0,
                        rawBounds);
                PdfTextEditService.TryExtractText(
                    legacyAnalysis,
                    legacyRegion,
                    out legacyText,
                    out legacyError);
            }
            finally
            {
                legacyWatch.Stop();
                PdfTextEditService.DiagnosticIoActivityForTesting = null;
            }

            var info = new FileInfo(sourcePath);
            var combinedWatch = Stopwatch.StartNew();
            PdfTextEditPreparation prepared;
            PdfTextEditService.DiagnosticIoActivityForTesting =
                delegate(string activity)
                {
                    IncrementCount(combinedCounts, activity);
                };
            try
            {
                prepared = PdfTextEditService.PrepareSelection(
                    sourcePath,
                    0,
                    rawBounds,
                    new PdfEditViewIdentity(
                        sourcePath,
                        info.Length,
                        info.LastWriteTimeUtc.Ticks));
            }
            finally
            {
                combinedWatch.Stop();
                PdfTextEditService.DiagnosticIoActivityForTesting = null;
            }

            AssertRegionNearlyEqual(legacyRegion, prepared.Region);
            Assert(
                string.Equals(
                    legacyAnalysis.SourceFingerprint,
                    prepared.Analysis.SourceFingerprint,
                    StringComparison.Ordinal),
                "Preparacion combinada: huella distinta.");
            Assert(
                string.Equals(
                    NormalizeWhitespace(legacyText),
                    NormalizeWhitespace(prepared.ExtractedText),
                    StringComparison.Ordinal) &&
                string.Equals(
                    legacyError,
                    prepared.ExtractionError,
                    StringComparison.Ordinal),
                "Preparacion combinada: extraccion distinta.");
            AssertIoCounts(legacyCounts, 3, "ruta anterior");
            AssertIoCounts(combinedCounts, 1, "ruta combinada");

            report.Add(
                "PASS preparacion combinada: guard/hash/reader 3->1; " +
                legacyWatch.Elapsed.TotalMilliseconds.ToString(
                    "0.0",
                    CultureInfo.InvariantCulture) +
                " ms -> " +
                combinedWatch.Elapsed.TotalMilliseconds.ToString(
                    "0.0",
                    CultureInfo.InvariantCulture) + " ms");
        }

        private static void IncrementCount(
            IDictionary<string, int> counts,
            string activity)
        {
            int current;
            counts.TryGetValue(activity, out current);
            counts[activity] = current + 1;
        }

        private static void AssertIoCounts(
            IDictionary<string, int> counts,
            int expected,
            string label)
        {
            foreach (var activity in new[]
            {
                "read-guard",
                "sha256",
                "pdf-reader"
            })
            {
                int actual;
                counts.TryGetValue(activity, out actual);
                Assert(
                    actual == expected,
                    label + ": " + activity + "=" +
                    actual.ToString(CultureInfo.InvariantCulture) +
                    ", esperado=" +
                    expected.ToString(CultureInfo.InvariantCulture));
            }
        }

        private static void TestRotation(
            string sourcePath,
            string runDirectory,
            int pageIndex,
            string sourceHash,
            IList<string> report)
        {
            var analysis = PdfTextEditService.Analyze(sourcePath);
            Assert(analysis.PageCount == 4, "Analisis: paginas.");
            Assert(analysis.FormFieldCount == 1, "Analisis: formulario.");
            Assert(!analysis.ContainsXfa, "Analisis: falso XFA.");

            var region = new PdfTextEditRegion(
                pageIndex + 1,
                0.18D,
                0.24D,
                0.82D,
                0.48D);

            AssertPdfiumCoordinateMapping(
                sourcePath,
                analysis,
                pageIndex,
                region);

            // Prove that the raw Pdfium/PDF rectangle overload is the inverse
            // of the page mapper for every intrinsic page rotation.
            PdfRectangle rawRectangle;
            using (var reader = new PdfReader(sourcePath))
            {
                rawRectangle = PdfTextPageTransform
                    .Create(reader, pageIndex + 1)
                    .GetRawRectangle(region);
            }
            var rawDrawing = RectangleF.FromLTRB(
                rawRectangle.Left,
                rawRectangle.Top,
                rawRectangle.Right,
                rawRectangle.Bottom);
            var roundTrip = PdfTextEditService.CreateRegionFromPdfBounds(
                analysis,
                pageIndex,
                rawDrawing);
            AssertRegionNearlyEqual(region, roundTrip);

            string existingText;
            string extractError;
            Assert(
                PdfTextEditService.TryExtractText(
                    analysis,
                    region,
                    out existingText,
                    out extractError),
                "Extraccion previa: " + extractError);

            var replacementText =
                "Reemplazo Unicode página " +
                (pageIndex + 1).ToString(CultureInfo.InvariantCulture) +
                " - Málaga, árbol, número 123, año, Ω";
            var replacement = new PdfTextReplacement(
                region,
                replacementText)
            {
                FontFamily = PdfTextEditFontFamily.SansSerif,
                FontSizePoints = 26F,
                MinimumFontSizePoints = 5F,
                AutoFit = true,
                Alignment = pageIndex % 3 == 0
                    ? PdfTextEditAlignment.Left
                    : (pageIndex % 3 == 1
                        ? PdfTextEditAlignment.Center
                        : PdfTextEditAlignment.Right),
                TextColor = Color.Black,
                CoverOriginal = true,
                CoverColor = CoverColor,
                PaddingPoints = 5F
            };
            var outputPath = Path.Combine(
                runDirectory,
                "revision-rotacion-" +
                Rotations[pageIndex].ToString(
                    CultureInfo.InvariantCulture) +
                ".pdf");
            var progress = new List<int>();
            var result = PdfTextEditService.Save(
                sourcePath,
                outputPath,
                analysis,
                replacement,
                value => progress.Add(value.CompletedSteps),
                System.Threading.CancellationToken.None);

            Assert(File.Exists(outputPath), "No existe la revision.");
            Assert(result.PageNumber == pageIndex + 1, "Pagina resultado.");
            Assert(result.ActualFontSizePoints >= 5F, "Fuente autoajustada.");
            Assert(!string.IsNullOrWhiteSpace(result.FontDisplayName),
                "Fuente resuelta.");
            Assert(progress.SequenceEqual(new[] { 0, 1, 2, 3, 4 }),
                "Secuencia de progreso.");
            AssertPrefixIsIdentical(sourcePath, outputPath);
            Assert(
                string.Equals(
                    sourceHash,
                    ComputeHash(sourcePath),
                    StringComparison.Ordinal),
                "Original modificado en rotacion " + Rotations[pageIndex]);
            AssertFormUnchanged(outputPath);
            AssertMetadataSemantics(sourcePath, outputPath);
            AssertTextExtracted(outputPath, pageIndex + 1, replacementText);
            AssertRegionExtraction(outputPath, region, replacementText);
            AssertUnicodeFontEmbedded(outputPath, pageIndex + 1);
            AssertCoverRendered(outputPath, pageIndex, region);

            report.Add(
                "PASS /Rotate " +
                Rotations[pageIndex].ToString(
                    CultureInfo.InvariantCulture) +
                ": append, CropBox, render, texto y formulario");
        }

        private static void AssertPdfiumCoordinateMapping(
            string sourcePath,
            PdfTextEditAnalysis analysis,
            int pageIndex,
            PdfTextEditRegion expected)
        {
            using (var document = PdfiumDocument.Load(sourcePath))
            {
                var size = document.PageSizes[pageIndex];
                var first = document.PointToPdf(
                    pageIndex,
                    new Point(
                        (int)Math.Round(expected.LeftRatio * size.Width),
                        (int)Math.Round(expected.TopRatio * size.Height)));
                var second = document.PointToPdf(
                    pageIndex,
                    new Point(
                        (int)Math.Round(expected.RightRatio * size.Width),
                        (int)Math.Round(expected.BottomRatio * size.Height)));
                var raw = RectangleF.FromLTRB(
                    Math.Min(first.X, second.X),
                    Math.Max(first.Y, second.Y),
                    Math.Max(first.X, second.X),
                    Math.Min(first.Y, second.Y));
                var actual = PdfTextEditService.CreateRegionFromPdfBounds(
                    analysis,
                    pageIndex,
                    raw);
                AssertRegionNearlyEqual(expected, actual, 0.004D);
            }
        }

        private static void TestSourceIdentity(
            string sourcePath,
            string runDirectory,
            IList<string> report)
        {
            var copyPath = Path.Combine(runDirectory, "identidad.pdf");
            File.Copy(sourcePath, copyPath);
            var analysis = PdfTextEditService.Analyze(copyPath);
            File.SetLastWriteTimeUtc(
                copyPath,
                File.GetLastWriteTimeUtc(copyPath).AddSeconds(2));
            var outputPath = Path.Combine(runDirectory, "no-debe-existir.pdf");
            var rejected = false;
            try
            {
                PdfTextEditService.Save(
                    copyPath,
                    outputPath,
                    analysis,
                    new PdfTextReplacement(
                        new PdfTextEditRegion(1, 0.2, 0.2, 0.5, 0.4),
                        "No escribir"));
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }

            Assert(rejected, "No se detecto el cambio de identidad.");
            Assert(!File.Exists(outputPath), "Se publico con origen cambiado.");
            report.Add("PASS identidad de origen y salida ausente");
        }

        private static void TestExternalReplacementRace(
            string sourcePath,
            string runDirectory,
            IList<string> report)
        {
            var visiblePath = Path.Combine(
                runDirectory,
                "carrera-vista-antigua.pdf");
            var replacementPath = Path.Combine(
                runDirectory,
                "carrera-contenido-nuevo.pdf");
            File.Copy(sourcePath, visiblePath);
            CreateDifferentGeometryFixture(replacementPath);
            var session = PdfEditSession.Create(visiblePath);
            var oldViewIdentity = session.CurrentViewIdentity;
            RectangleF oldViewBounds;
            var visibleBytes = File.ReadAllBytes(visiblePath);
            using (var visibleStream = new MemoryStream(
                visibleBytes,
                false))
            using (var visibleDocument = PdfiumDocument.Load(visibleStream))
            {
                var size = visibleDocument.PageSizes[0];
                var first = visibleDocument.PointToPdf(
                    0,
                    new Point(
                        (int)Math.Round(size.Width * 0.2F),
                        (int)Math.Round(size.Height * 0.2F)));
                var second = visibleDocument.PointToPdf(
                    0,
                    new Point(
                        (int)Math.Round(size.Width * 0.8F),
                        (int)Math.Round(size.Height * 0.45F)));
                oldViewBounds = RectangleF.FromLTRB(
                    Math.Min(first.X, second.X),
                    Math.Max(first.Y, second.Y),
                    Math.Max(first.X, second.X),
                    Math.Min(first.Y, second.Y));

                File.Copy(replacementPath, visiblePath, true);
                File.SetLastWriteTimeUtc(
                    visiblePath,
                    DateTime.UtcNow.AddSeconds(3));

                var blocked = false;
                try
                {
                    PdfTextEditService.PrepareSelection(
                        visiblePath,
                        0,
                        oldViewBounds,
                        oldViewIdentity);
                }
                catch (InvalidOperationException ex)
                {
                    blocked = ex.Message.IndexOf(
                        "visible cambio en disco",
                        StringComparison.OrdinalIgnoreCase) >= 0;
                }

                Assert(
                    blocked,
                    "Se aceptaron coordenadas Pdfium de una vista antigua " +
                    "sobre un PDF reemplazado externamente.");

                var switchedPathBlocked = false;
                try
                {
                    PdfTextEditService.PrepareSelection(
                        replacementPath,
                        0,
                        oldViewBounds,
                        oldViewIdentity);
                }
                catch (InvalidOperationException ex)
                {
                    switchedPathBlocked = ex.Message.IndexOf(
                        "visible cambio en disco",
                        StringComparison.OrdinalIgnoreCase) >= 0;
                }

                Assert(
                    switchedPathBlocked,
                    "Se aceptaron coordenadas de una vista cuyo ContentPath " +
                    "fue sustituido por otra revision.");
            }

            session.DeleteRecovery();
            report.Add(
                "PASS carrera externa: token bloquea reemplazo y ContentPath distinto");
        }

        private static void CreateDifferentGeometryFixture(string path)
        {
            using (var output = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            using (var document = new Document(PageSize.A5.Rotate()))
            {
                PdfWriter.GetInstance(document, output);
                document.AddTitle("Contenido externo distinto");
                document.Open();
                document.Add(new Paragraph(
                    "PDF reemplazado despues de cargar la vista Pdfium."));
            }
        }

        private static void TestXfaBlock(
            string sourcePath,
            string runDirectory,
            IList<string> report)
        {
            var xfaPath = Path.Combine(runDirectory, "xfa-bloqueado.pdf");
            CreateXfaFixture(sourcePath, xfaPath);
            var analysis = PdfTextEditService.Analyze(xfaPath);
            Assert(analysis.ContainsXfa, "No se detecto XFA.");
            var outputPath = Path.Combine(runDirectory, "xfa-salida.pdf");
            var blocked = false;
            try
            {
                PdfTextEditService.Save(
                    xfaPath,
                    outputPath,
                    analysis,
                    new PdfTextReplacement(
                        new PdfTextEditRegion(1, 0.2, 0.2, 0.5, 0.4),
                        "No escribir"));
            }
            catch (NotSupportedException)
            {
                blocked = true;
            }

            Assert(blocked, "XFA no fue bloqueado.");
            Assert(!File.Exists(outputPath), "XFA creo una salida.");
            report.Add("PASS bloqueo XFA antes de escribir");
        }

        private static void TestSignedRevision(
            string sourcePath,
            string runDirectory,
            IList<string> report)
        {
            var signedPath = Path.Combine(
                runDirectory,
                "origen-firmado.pdf");
            var outputPath = Path.Combine(
                runDirectory,
                "revision-de-origen-firmado.pdf");
            CreateSignedFixture(sourcePath, signedPath);
            AssertSignatureState(signedPath, true);

            var analysis = PdfTextEditService.Analyze(signedPath);
            Assert(
                analysis.ContainsDigitalSignatures &&
                analysis.SignatureNames.Contains("qa_signature"),
                "El analisis no detecto la firma de QA.");
            var result = PdfTextEditService.Save(
                signedPath,
                outputPath,
                analysis,
                new PdfTextReplacement(
                    new PdfTextEditRegion(1, 0.2D, 0.52D, 0.8D, 0.66D),
                    "Revision posterior a firma - Málaga, año, Ω")
                {
                    FontFamily = PdfTextEditFontFamily.SansSerif,
                    FontSizePoints = 18F,
                    AutoFit = true,
                    CoverOriginal = true,
                    CoverColor = Color.White,
                    TextColor = Color.Black
                });

            Assert(
                result.DigitalSignaturesInvalidated,
                "No se notifico que el documento tenia una firma previa.");
            AssertPrefixIsIdentical(signedPath, outputPath);
            AssertSignatureState(outputPath, false);
            report.Add(
                "PASS firma previa criptograficamente valida tras revision incremental");
        }

        private static void CreateSignedFixture(
            string sourcePath,
            string outputPath)
        {
            var random = new SecureRandom();
            var keyGenerator = new RsaKeyPairGenerator();
            keyGenerator.Init(new KeyGenerationParameters(random, 2048));
            var keyPair = keyGenerator.GenerateKeyPair();

            var certificateGenerator = new X509V3CertificateGenerator();
            var subject = new X509Name("CN=PDF Ligero Content Edit QA");
            certificateGenerator.SetSerialNumber(
                BigInteger.ProbablePrime(120, random));
            certificateGenerator.SetIssuerDN(subject);
            certificateGenerator.SetSubjectDN(subject);
            certificateGenerator.SetNotBefore(DateTime.UtcNow.AddDays(-1));
            certificateGenerator.SetNotAfter(DateTime.UtcNow.AddYears(2));
            certificateGenerator.SetPublicKey(keyPair.Public);
            var signatureFactory = new Asn1SignatureFactory(
                "SHA256WITHRSA",
                keyPair.Private,
                random);
            var certificate = certificateGenerator.Generate(
                signatureFactory);

            PdfReader reader = null;
            try
            {
                reader = new PdfReader(sourcePath);
                using (var output = new FileStream(
                    outputPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None))
                {
                    var stamper = PdfStamper.CreateSignature(
                        reader,
                        output,
                        '\0',
                        null,
                        true);
                    var appearance = stamper.SignatureAppearance;
                    appearance.Reason = "Prueba de revision incremental";
                    appearance.Location = "QA local";
                    appearance.SetVisibleSignature(
                        new PdfRectangle(180F, 355F, 420F, 390F),
                        1,
                        "qa_signature");
                    var signature = new PrivateKeySignature(
                        keyPair.Private,
                        DigestAlgorithms.SHA256);
                    MakeSignature.SignDetached(
                        appearance,
                        signature,
                        new[] { certificate },
                        null,
                        null,
                        null,
                        0,
                        CryptoStandard.CMS);
                    reader = null;
                }
            }
            finally
            {
                if (reader != null)
                {
                    reader.Close();
                }
            }
        }

        private static void AssertSignatureState(
            string path,
            bool mustCoverWholeDocument)
        {
            using (var reader = new PdfReader(path))
            {
                var names = reader.AcroFields.GetSignatureNames();
                Assert(
                    names.Count == 1 && names.Contains("qa_signature"),
                    "No se conservo el campo de firma de QA.");
                Assert(
                    reader.AcroFields.VerifySignature("qa_signature").Verify(),
                    "La firma previa dejo de ser criptograficamente valida.");
                Assert(
                    reader.AcroFields.SignatureCoversWholeDocument(
                        "qa_signature") == mustCoverWholeDocument,
                    "Estado inesperado de cobertura de la firma.");
            }
        }

        private static void TestCjkAndEmoji(
            string sourcePath,
            string runDirectory,
            IList<string> report)
        {
            var analysis = PdfTextEditService.Analyze(sourcePath);
            var region = new PdfTextEditRegion(
                1,
                0.18D,
                0.55D,
                0.82D,
                0.69D);
            const string cjkText = "CJK Unicode: 中文测试 · 你好，世界";
            var probeFont = PdfUnicodeFontResolver.Create(
                PdfTextEditFontFamily.SansSerif,
                false,
                false,
                cjkText);
            var probePath = Path.Combine(runDirectory, "sonda-cjk.pdf");
            var probeExtracted = CreateUnicodeExtractionProbe(
                probePath,
                probeFont.BaseFont,
                cjkText);
            report.Add(
                "INFO fuente CJK=" + probeFont.DisplayName +
                "; extraccion=" + probeExtracted);
            var outputPath = Path.Combine(
                runDirectory,
                "revision-cjk.pdf");
            var result = PdfTextEditService.Save(
                sourcePath,
                outputPath,
                analysis,
                new PdfTextReplacement(region, cjkText)
                {
                    FontFamily = PdfTextEditFontFamily.SansSerif,
                    FontSizePoints = 22F,
                    MinimumFontSizePoints = 5F,
                    AutoFit = true,
                    CoverOriginal = true,
                    CoverColor = CoverColor,
                    TextColor = Color.Black
                });
            Assert(
                !string.IsNullOrWhiteSpace(result.FontDisplayName),
                "CJK no resolvio una fuente.");
            AssertTextExtracted(outputPath, 1, cjkText);
            AssertRegionExtraction(outputPath, region, cjkText);
            AssertUnicodeFontEmbedded(outputPath, 1);
            AssertMetadataSemantics(sourcePath, outputPath);
            AssertTextRendered(
                outputPath,
                0,
                region,
                Path.Combine(runDirectory, "render-cjk.png"));

            var emojiOutcome = string.Empty;
            try
            {
                var emojiFont = PdfUnicodeFontResolver.Create(
                    PdfTextEditFontFamily.SansSerif,
                    false,
                    false,
                    "Emoji \U0001F600");
                Assert(
                    emojiFont != null &&
                    emojiFont.BaseFont.CharExists(0x1F600),
                    "El resolver acepto emoji sin glifo.");
                emojiOutcome = "soportado por " + emojiFont.DisplayName;
            }
            catch (InvalidOperationException ex)
            {
                Assert(
                    ex.Message.IndexOf(
                        "U+1F600",
                        StringComparison.Ordinal) >= 0,
                    "El error de emoji no identifica el codepoint.");
                emojiOutcome = "rechazo claro U+1F600";
            }

            report.Add(
                "PASS CJK visible/buscable con cobertura CharExists; emoji: " +
                emojiOutcome);
        }

        private static string CreateUnicodeExtractionProbe(
            string path,
            BaseFont font,
            string text)
        {
            using (var output = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            using (var document = new Document(PageSize.A4))
            {
                var writer = PdfWriter.GetInstance(document, output);
                document.Open();
                var canvas = writer.DirectContent;
                canvas.BeginText();
                canvas.SetFontAndSize(font, 18F);
                canvas.SetTextMatrix(72F, 720F);
                canvas.ShowText(text);
                canvas.EndText();
            }

            using (var reader = new PdfReader(path))
            {
                return PdfTextExtractor.GetTextFromPage(reader, 1)
                    .Replace("\r", "\\r")
                    .Replace("\n", "\\n");
            }
        }

        private static void AssertTextRendered(
            string path,
            int pageIndex,
            PdfTextEditRegion region,
            string capturePath)
        {
            using (var document = PdfiumDocument.Load(path))
            {
                var pageSize = document.PageSizes[pageIndex];
                var width = 1000;
                var height = Math.Max(
                    1,
                    (int)Math.Round(width * pageSize.Height / pageSize.Width));
                using (var image = document.Render(
                    pageIndex,
                    width,
                    height,
                    96F,
                    96F,
                    PdfiumViewer.PdfRenderFlags.Annotations))
                using (var bitmap = new Bitmap(image))
                {
                    var darkPixels = 0;
                    var left = (int)Math.Round(
                        region.LeftRatio * bitmap.Width);
                    var right = (int)Math.Round(
                        region.RightRatio * bitmap.Width);
                    var top = (int)Math.Round(
                        region.TopRatio * bitmap.Height);
                    var bottom = (int)Math.Round(
                        region.BottomRatio * bitmap.Height);
                    for (var y = Math.Max(0, top);
                        y < Math.Min(bitmap.Height, bottom);
                        y += 2)
                    {
                        for (var x = Math.Max(0, left);
                            x < Math.Min(bitmap.Width, right);
                            x += 2)
                        {
                            var pixel = bitmap.GetPixel(x, y);
                            if (pixel.R < 80 && pixel.G < 80 && pixel.B < 80)
                            {
                                darkPixels++;
                            }
                        }
                    }

                    Assert(
                        darkPixels >= 25,
                        "El texto CJK no aparece en el render.");
                    bitmap.Save(capturePath);
                }
            }
        }

        private static void CreateFixture(string outputPath)
        {
            var temporaryPath = outputPath + ".base.pdf";
            var document = new Document(new PdfRectangle(0F, 0F, 800F, 1100F));
            using (var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            {
                var writer = PdfWriter.GetInstance(document, output);
                document.AddTitle("Fixture edicion visual");
                document.AddAuthor("PDF Ligero QA");
                document.AddSubject(
                    "XMP descriptivo que debe conservarse semanticamente");
                writer.CreateXmpMetadata();
                document.Open();
                var font = BaseFont.CreateFont(
                    BaseFont.HELVETICA,
                    BaseFont.CP1252,
                    BaseFont.NOT_EMBEDDED);

                for (var page = 0; page < Rotations.Length; page++)
                {
                    if (page > 0)
                    {
                        document.NewPage();
                    }

                    var canvas = writer.DirectContent;
                    canvas.SetColorStroke(new BaseColor(90, 90, 90));
                    canvas.Rectangle(150F, 250F, 500F, 700F);
                    canvas.Stroke();
                    canvas.BeginText();
                    canvas.SetFontAndSize(font, 14F);
                    canvas.SetTextMatrix(190F, 880F);
                    canvas.ShowText(
                        "CONTENIDO ORIGINAL PAGINA " +
                        (page + 1).ToString(CultureInfo.InvariantCulture));
                    canvas.EndText();

                    if (page == 0)
                    {
                        var field = new TextField(
                            writer,
                            new PdfRectangle(180F, 300F, 420F, 335F),
                            "project.name")
                        {
                            Text = "VALOR INTACTO",
                            FontSize = 10F
                        };
                        writer.AddAnnotation(field.GetTextField());
                    }
                }

                document.Close();
            }

            PdfReader reader = null;
            PdfStamper stamper = null;
            try
            {
                reader = new PdfReader(temporaryPath);
                stamper = new PdfStamper(
                    reader,
                    new FileStream(
                        outputPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None));
                for (var page = 1; page <= Rotations.Length; page++)
                {
                    var pageDictionary = reader.GetPageN(page);
                    pageDictionary.Put(
                        PdfName.MEDIABOX,
                        CreateBoxArray(100F, 200F, 700F, 1000F));
                    pageDictionary.Put(
                        PdfName.CROPBOX,
                        CreateBoxArray(150F, 250F, 650F, 950F));
                    pageDictionary.Put(
                        PdfName.ROTATE,
                        new PdfNumber(Rotations[page - 1]));
                    pageDictionary.Put(
                        new PdfName("UserUnit"),
                        new PdfNumber(2F));
                }

                stamper.Close();
                stamper = null;
                reader.Close();
                reader = null;
            }
            finally
            {
                if (stamper != null)
                {
                    stamper.Close();
                }
                else if (reader != null)
                {
                    reader.Close();
                }
                File.Delete(temporaryPath);
            }
        }

        private static void AssertFixtureHasRealXmpAndUserUnit(string path)
        {
            using (var reader = new PdfReader(path))
            {
                Assert(
                    reader.Metadata != null && reader.Metadata.Length > 100,
                    "El fixture no contiene un paquete XMP real.");
                var descriptive = CaptureDescriptiveXmp(reader.Metadata);
                Assert(
                    descriptive.Any(value => value.IndexOf(
                        "Fixture edicion visual",
                        StringComparison.Ordinal) >= 0) &&
                    descriptive.Any(value => value.IndexOf(
                        "XMP descriptivo",
                        StringComparison.Ordinal) >= 0),
                    "El XMP no contiene titulo y descripcion de control. " +
                    "Propiedades: " + string.Join(" || ", descriptive));

                for (var page = 1; page <= reader.NumberOfPages; page++)
                {
                    var userUnit = reader.GetPageN(page).GetAsNumber(
                        new PdfName("UserUnit"));
                    Assert(
                        userUnit != null &&
                        Math.Abs(userUnit.FloatValue - 2F) < 0.001F,
                        "El fixture no conserva /UserUnit=2 en pagina " + page);
                }
            }
        }

        private static void TestXmpSemanticValidation(
            string sourcePath,
            IList<string> report)
        {
            byte[] sourceXmp;
            using (var reader = new PdfReader(sourcePath))
            {
                sourceXmp = reader.Metadata;
            }

            var flags = System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Static;
            var serviceType = typeof(PdfTextEditService);
            var capture = serviceType.GetMethod(
                "CaptureXmpExpectation",
                flags);
            var validate = serviceType.GetMethod("ValidateXmp", flags);
            Assert(
                capture != null && validate != null,
                "No se encontro el validador XMP del motor.");
            var expected = capture.Invoke(
                null,
                new object[] { sourceXmp });

            validate.Invoke(
                null,
                new[]
                {
                    (object)MutateFixtureXmp(sourceXmp, false),
                    expected
                });

            var descriptiveRejected = false;
            try
            {
                validate.Invoke(
                    null,
                    new[]
                    {
                        (object)MutateFixtureXmp(sourceXmp, true),
                        expected
                    });
            }
            catch (System.Reflection.TargetInvocationException ex)
            {
                descriptiveRejected = ex.InnerException is
                    InvalidDataException;
            }

            Assert(
                descriptiveRejected,
                "El validador XMP acepto una mutacion descriptiva.");
            report.Add(
                "PASS XMP semantico: tolera fecha tecnica y bloquea titulo alterado");
        }

        private static byte[] MutateFixtureXmp(
            byte[] sourceXmp,
            bool mutateDescription)
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreWhitespace = true
            };
            var document = new XmlDocument
            {
                PreserveWhitespace = false,
                XmlResolver = null
            };
            using (var input = new MemoryStream(sourceXmp, false))
            using (var reader = XmlReader.Create(input, settings))
            {
                document.Load(reader);
            }

            if (mutateDescription)
            {
                var titles = document.GetElementsByTagName(
                    "title",
                    "http://purl.org/dc/elements/1.1/");
                Assert(titles.Count == 1, "No se encontro dc:title.");
                titles[0].InnerText = "Titulo descriptivo alterado";
            }
            else
            {
                XmlAttribute modifyDate = null;
                foreach (XmlNode description in document.GetElementsByTagName(
                    "Description",
                    "http://www.w3.org/1999/02/22-rdf-syntax-ns#"))
                {
                    if (description.Attributes == null)
                    {
                        continue;
                    }

                    modifyDate = description.Attributes
                        .Cast<XmlAttribute>()
                        .FirstOrDefault(attribute =>
                            string.Equals(
                                attribute.NamespaceURI,
                                "http://ns.adobe.com/xap/1.0/",
                                StringComparison.Ordinal) &&
                            string.Equals(
                                attribute.LocalName,
                                "ModifyDate",
                                StringComparison.Ordinal));
                    if (modifyDate != null)
                    {
                        break;
                    }
                }

                Assert(modifyDate != null, "No se encontro xmp:ModifyDate.");
                modifyDate.Value = "2099-01-02T03:04:05Z";
            }

            using (var output = new MemoryStream())
            using (var writer = XmlWriter.Create(
                output,
                new XmlWriterSettings
                {
                    Encoding = new UTF8Encoding(false),
                    Indent = true,
                    CloseOutput = false
                }))
            {
                document.Save(writer);
                writer.Flush();
                return output.ToArray();
            }
        }

        private static void AssertMetadataSemantics(
            string sourcePath,
            string outputPath)
        {
            using (var source = new PdfReader(sourcePath))
            using (var output = new PdfReader(outputPath))
            {
                Assert(
                    CaptureDescriptiveInfo(source.Info).SequenceEqual(
                        CaptureDescriptiveInfo(output.Info)),
                    "Cambiaron los metadatos Info descriptivos.");
                Assert(
                    CaptureDescriptiveXmp(source.Metadata).SequenceEqual(
                        CaptureDescriptiveXmp(output.Metadata)),
                    "Cambiaron los metadatos XMP descriptivos.");

                string sourceProducer;
                string outputProducer;
                source.Info.TryGetValue("Producer", out sourceProducer);
                output.Info.TryGetValue("Producer", out outputProducer);
                var expectedProducer = string.Equals(
                        sourceProducer,
                        outputProducer,
                        StringComparison.Ordinal) ||
                    (!string.IsNullOrEmpty(sourceProducer) &&
                     !string.IsNullOrEmpty(outputProducer) &&
                     string.Equals(
                        outputProducer,
                        sourceProducer + "; modified using " +
                            iTextSharp.text.Version
                                .GetInstance()
                                .GetVersion,
                        StringComparison.Ordinal));
                Assert(
                    expectedProducer,
                    "Transicion Producer inesperada.");

                Assert(
                    source.NumberOfPages == output.NumberOfPages,
                    "Cambio el numero de paginas al validar /UserUnit.");
                for (var page = 1; page <= source.NumberOfPages; page++)
                {
                    var before = source.GetPageN(page).GetAsNumber(
                        new PdfName("UserUnit"));
                    var after = output.GetPageN(page).GetAsNumber(
                        new PdfName("UserUnit"));
                    Assert(
                        (before == null && after == null) ||
                        (before != null && after != null &&
                         Math.Abs(before.FloatValue - after.FloatValue) <
                            0.0001F),
                        "Cambio /UserUnit en pagina " + page);
                }
            }
        }

        private static IList<string> CaptureDescriptiveInfo(
            IDictionary<string, string> info)
        {
            return (info ?? new Dictionary<string, string>())
                .Where(item =>
                    !string.Equals(
                        item.Key,
                        "Producer",
                        StringComparison.Ordinal) &&
                    !string.Equals(
                        item.Key,
                        "ModDate",
                        StringComparison.Ordinal))
                .Select(item => item.Key + "\u001f" + item.Value)
                .OrderBy(item => item, StringComparer.Ordinal)
                .ToList();
        }

        private static IList<string> CaptureDescriptiveXmp(byte[] bytes)
        {
            Assert(bytes != null && bytes.Length > 0, "Falta XMP.");
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                IgnoreWhitespace = true
            };
            var document = new XmlDocument
            {
                PreserveWhitespace = false,
                XmlResolver = null
            };
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = XmlReader.Create(stream, settings))
            {
                document.Load(reader);
            }

            var rdfNodes = document.GetElementsByTagName(
                "RDF",
                "http://www.w3.org/1999/02/22-rdf-syntax-ns#");
            Assert(rdfNodes.Count == 1, "El XMP no contiene un RDF unico.");
            return new List<string>
            {
                CanonicalizeQaXmpNode(rdfNodes[0])
            };
        }

        private static string CanonicalizeQaXmpNode(XmlNode node)
        {
            if (node == null || IsQaTechnicalXmp(node))
            {
                return string.Empty;
            }

            if (node.NodeType == XmlNodeType.Text ||
                node.NodeType == XmlNodeType.CDATA ||
                node.NodeType == XmlNodeType.SignificantWhitespace)
            {
                var text = new StringBuilder("T");
                AppendQaXmpScalar(text, node.Value ?? string.Empty);
                return text.ToString();
            }

            if (node.NodeType != XmlNodeType.Element)
            {
                return string.Empty;
            }

            if (string.Equals(
                    node.NamespaceURI,
                    "http://www.w3.org/1999/02/22-rdf-syntax-ns#",
                    StringComparison.Ordinal) &&
                string.Equals(
                    node.LocalName,
                    "Description",
                    StringComparison.Ordinal))
            {
                return CanonicalizeQaRdfDescription(node);
            }

            var builder = new StringBuilder("E");
            AppendQaXmpScalar(builder, node.NamespaceURI ?? string.Empty);
            AppendQaXmpScalar(builder, node.LocalName ?? string.Empty);

            var attributes = new List<string>();
            if (node.Attributes != null)
            {
                foreach (XmlAttribute attribute in node.Attributes)
                {
                    if (attribute.NamespaceURI ==
                            "http://www.w3.org/2000/xmlns/" ||
                        IsQaTechnicalXmp(attribute))
                    {
                        continue;
                    }

                    attributes.Add(CanonicalizeQaXmpAttribute(attribute));
                }
            }

            attributes.Sort(StringComparer.Ordinal);
            foreach (var attribute in attributes)
            {
                AppendQaXmpScalar(builder, attribute);
            }

            var children = new List<string>();
            foreach (XmlNode child in node.ChildNodes)
            {
                var token = CanonicalizeQaXmpNode(child);
                if (!string.IsNullOrEmpty(token))
                {
                    children.Add(token);
                }
            }

            if (HasQaUnorderedXmpChildren(node))
            {
                children.Sort(StringComparer.Ordinal);
            }

            foreach (var child in children)
            {
                AppendQaXmpScalar(builder, child);
            }

            return builder.ToString();
        }

        private static string CanonicalizeQaRdfDescription(XmlNode node)
        {
            var builder = new StringBuilder("D");
            AppendQaXmpScalar(builder, node.NamespaceURI ?? string.Empty);
            AppendQaXmpScalar(builder, node.LocalName ?? string.Empty);

            var structuralAttributes = new List<string>();
            var properties = new List<string>();
            if (node.Attributes != null)
            {
                foreach (XmlAttribute attribute in node.Attributes)
                {
                    if (attribute.NamespaceURI ==
                            "http://www.w3.org/2000/xmlns/" ||
                        IsQaTechnicalXmp(attribute))
                    {
                        continue;
                    }

                    if (IsQaRdfStructuralAttribute(attribute))
                    {
                        structuralAttributes.Add(
                            CanonicalizeQaXmpAttribute(attribute));
                    }
                    else
                    {
                        properties.Add(CanonicalizeQaSimpleXmpProperty(
                            attribute.NamespaceURI,
                            attribute.LocalName,
                            attribute.Value));
                    }
                }
            }

            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element ||
                    IsQaTechnicalXmp(child))
                {
                    continue;
                }

                properties.Add(IsQaSimpleXmpPropertyElement(child)
                    ? CanonicalizeQaSimpleXmpProperty(
                        child.NamespaceURI,
                        child.LocalName,
                        child.InnerText)
                    : "P" + CanonicalizeQaXmpNode(child));
            }

            structuralAttributes.Sort(StringComparer.Ordinal);
            properties.Sort(StringComparer.Ordinal);
            foreach (var attribute in structuralAttributes)
            {
                AppendQaXmpScalar(builder, attribute);
            }

            foreach (var property in properties)
            {
                AppendQaXmpScalar(builder, property);
            }

            return builder.ToString();
        }

        private static string CanonicalizeQaXmpAttribute(
            XmlAttribute attribute)
        {
            var builder = new StringBuilder("A");
            AppendQaXmpScalar(
                builder,
                attribute.NamespaceURI ?? string.Empty);
            AppendQaXmpScalar(builder, attribute.LocalName ?? string.Empty);
            AppendQaXmpScalar(builder, attribute.Value ?? string.Empty);
            return builder.ToString();
        }

        private static string CanonicalizeQaSimpleXmpProperty(
            string namespaceUri,
            string localName,
            string value)
        {
            var builder = new StringBuilder("S");
            AppendQaXmpScalar(builder, namespaceUri ?? string.Empty);
            AppendQaXmpScalar(builder, localName ?? string.Empty);
            AppendQaXmpScalar(builder, value ?? string.Empty);
            return builder.ToString();
        }

        private static bool IsQaRdfStructuralAttribute(XmlAttribute attribute)
        {
            return attribute != null &&
                (string.Equals(
                        attribute.NamespaceURI,
                        "http://www.w3.org/1999/02/22-rdf-syntax-ns#",
                        StringComparison.Ordinal) ||
                 string.Equals(
                        attribute.NamespaceURI,
                        "http://www.w3.org/XML/1998/namespace",
                        StringComparison.Ordinal));
        }

        private static bool IsQaSimpleXmpPropertyElement(XmlNode node)
        {
            if (node == null || node.NodeType != XmlNodeType.Element)
            {
                return false;
            }

            if (node.Attributes != null)
            {
                foreach (XmlAttribute attribute in node.Attributes)
                {
                    if (attribute.NamespaceURI !=
                        "http://www.w3.org/2000/xmlns/")
                    {
                        return false;
                    }
                }
            }

            return !node.ChildNodes.Cast<XmlNode>().Any(child =>
                child.NodeType == XmlNodeType.Element);
        }

        private static void AppendQaXmpScalar(
            StringBuilder builder,
            string value)
        {
            var normalized = value ?? string.Empty;
            builder.Append(normalized.Length.ToString(
                CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(normalized);
        }

        private static bool IsQaTechnicalXmp(XmlNode node)
        {
            if (node == null)
            {
                return false;
            }

            if (string.Equals(
                    node.NamespaceURI,
                    "http://ns.adobe.com/pdf/1.3/",
                    StringComparison.Ordinal) &&
                string.Equals(
                    node.LocalName,
                    "Producer",
                    StringComparison.Ordinal))
            {
                return true;
            }

            return string.Equals(
                    node.NamespaceURI,
                    "http://ns.adobe.com/xap/1.0/",
                    StringComparison.Ordinal) &&
                (string.Equals(
                    node.LocalName,
                    "ModifyDate",
                    StringComparison.Ordinal) ||
                 string.Equals(
                    node.LocalName,
                    "MetadataDate",
                    StringComparison.Ordinal));
        }

        private static bool HasQaUnorderedXmpChildren(XmlNode node)
        {
            if (node == null || !string.Equals(
                    node.NamespaceURI,
                    "http://www.w3.org/1999/02/22-rdf-syntax-ns#",
                    StringComparison.Ordinal))
            {
                return false;
            }

            return string.Equals(
                    node.LocalName,
                    "RDF",
                    StringComparison.Ordinal) ||
                string.Equals(
                    node.LocalName,
                    "Description",
                    StringComparison.Ordinal) ||
                string.Equals(
                    node.LocalName,
                    "Bag",
                    StringComparison.Ordinal) ||
                string.Equals(
                    node.LocalName,
                    "Alt",
                    StringComparison.Ordinal);
        }

        private static PdfArray CreateBoxArray(
            float left,
            float bottom,
            float right,
            float top)
        {
            var array = new PdfArray();
            array.Add(new PdfNumber(left));
            array.Add(new PdfNumber(bottom));
            array.Add(new PdfNumber(right));
            array.Add(new PdfNumber(top));
            return array;
        }

        private static void CreateXfaFixture(
            string sourcePath,
            string outputPath)
        {
            PdfReader reader = null;
            PdfStamper stamper = null;
            try
            {
                reader = new PdfReader(sourcePath);
                stamper = new PdfStamper(
                    reader,
                    new FileStream(
                        outputPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None));
                var acroForm = PdfReader.GetPdfObject(
                    reader.Catalog.Get(PdfName.ACROFORM)) as PdfDictionary;
                Assert(acroForm != null, "Fixture sin AcroForm.");
                acroForm.Put(
                    PdfName.XFA,
                    new PdfString("<template xmlns=\"http://www.xfa.org/schema/xfa-template/2.5/\"/>")
                        .SetHexWriting(false));
                stamper.Close();
                stamper = null;
                reader.Close();
                reader = null;
            }
            finally
            {
                if (stamper != null)
                {
                    stamper.Close();
                }
                else if (reader != null)
                {
                    reader.Close();
                }
            }
        }

        private static void AssertCoverRendered(
            string path,
            int pageIndex,
            PdfTextEditRegion region)
        {
            using (var document = PdfiumDocument.Load(path))
            {
                var pageSize = document.PageSizes[pageIndex];
                var width = 1000;
                var height = Math.Max(
                    1,
                    (int)Math.Round(width * pageSize.Height / pageSize.Width));
                using (var image = document.Render(
                    pageIndex,
                    width,
                    height,
                    96F,
                    96F,
                    PdfiumViewer.PdfRenderFlags.Annotations))
                using (var bitmap = new Bitmap(image))
                {
                    var probes = new[]
                    {
                        new PointF(0.22F, 0.28F),
                        new PointF(0.78F, 0.28F),
                        new PointF(0.22F, 0.44F),
                        new PointF(0.78F, 0.44F)
                    };
                    var matching = probes.Count(probe =>
                    {
                        var pixel = bitmap.GetPixel(
                            Math.Max(0, Math.Min(
                                bitmap.Width - 1,
                                (int)Math.Round(probe.X * bitmap.Width))),
                            Math.Max(0, Math.Min(
                                bitmap.Height - 1,
                                (int)Math.Round(probe.Y * bitmap.Height))));
                        return Math.Abs(pixel.R - CoverColor.R) <= 8 &&
                            Math.Abs(pixel.G - CoverColor.G) <= 8 &&
                            Math.Abs(pixel.B - CoverColor.B) <= 8;
                    });
                    Assert(
                        matching >= 3,
                        "El relleno no aparece en el rectangulo visual de /Rotate " +
                        Rotations[pageIndex].ToString(
                            CultureInfo.InvariantCulture) +
                            ". Coincidencias=" + matching);

                    var darkPixels = 0;
                    var scanLeft = (int)Math.Round(
                        region.LeftRatio * bitmap.Width);
                    var scanRight = (int)Math.Round(
                        region.RightRatio * bitmap.Width);
                    var scanTop = (int)Math.Round(
                        region.TopRatio * bitmap.Height);
                    var scanBottom = (int)Math.Round(
                        region.BottomRatio * bitmap.Height);
                    for (var y = Math.Max(0, scanTop);
                        y < Math.Min(bitmap.Height, scanBottom);
                        y += 2)
                    {
                        for (var x = Math.Max(0, scanLeft);
                            x < Math.Min(bitmap.Width, scanRight);
                            x += 2)
                        {
                            var pixel = bitmap.GetPixel(x, y);
                            if (pixel.R < 80 && pixel.G < 80 && pixel.B < 80)
                            {
                                darkPixels++;
                            }
                        }
                    }
                    Assert(
                        darkPixels >= 30,
                        "El texto Unicode no aparece en el render. Pixeles=" +
                        darkPixels.ToString(CultureInfo.InvariantCulture));
                    bitmap.Save(
                        Path.Combine(
                            Path.GetDirectoryName(path),
                            "render-rotacion-" +
                            Rotations[pageIndex].ToString(
                                CultureInfo.InvariantCulture) +
                            ".png"));
                }
            }
        }

        private static void AssertUnicodeFontEmbedded(
            string path,
            int pageNumber)
        {
            using (var reader = new PdfReader(path))
            {
                var page = reader.GetPageN(pageNumber);
                var resources = ResolveDictionary(
                    page == null ? null : page.Get(PdfName.RESOURCES));
                var fonts = ResolveDictionary(
                    resources == null ? null : resources.Get(PdfName.FONT));
                var embeddedIdentityFont = false;
                if (fonts != null)
                {
                    foreach (var name in fonts.Keys)
                    {
                        var font = ResolveDictionary(fonts.Get(name));
                        if (font == null ||
                            !new PdfName("Identity-H").Equals(
                                font.GetAsName(PdfName.ENCODING)))
                        {
                            continue;
                        }

                        var descendants = font.GetAsArray(
                            PdfName.DESCENDANTFONTS);
                        var descendant = descendants == null ||
                            descendants.Size == 0
                            ? null
                            : ResolveDictionary(descendants[0]);
                        var descriptor = ResolveDictionary(
                            descendant == null
                                ? null
                                : descendant.Get(PdfName.FONTDESCRIPTOR));
                        if (descriptor != null &&
                            (descriptor.Get(PdfName.FONTFILE2) != null ||
                             descriptor.Get(PdfName.FONTFILE3) != null))
                        {
                            embeddedIdentityFont = true;
                            break;
                        }
                    }
                }

                Assert(
                    embeddedIdentityFont,
                    "No se encontro la fuente Unicode Identity-H embebida.");
            }
        }

        private static PdfDictionary ResolveDictionary(PdfObject value)
        {
            return value == null
                ? null
                : PdfReader.GetPdfObject(value) as PdfDictionary;
        }

        private static void AssertRegionExtraction(
            string outputPath,
            PdfTextEditRegion region,
            string expectedText)
        {
            var analysis = PdfTextEditService.Analyze(outputPath);
            var extracted = PdfTextEditService.ExtractText(analysis, region);
            Assert(
                ContainsExtractedTextQa(extracted, expectedText),
                "La extraccion regional no encontro el reemplazo.");
        }

        private static void AssertTextExtracted(
            string path,
            int pageNumber,
            string expectedText)
        {
            using (var reader = new PdfReader(path))
            {
                var text = PdfTextExtractor.GetTextFromPage(reader, pageNumber);
                Assert(
                    ContainsExtractedTextQa(text, expectedText),
                    "El reemplazo no es texto buscable.");
            }
        }

        private static bool ContainsExtractedTextQa(
            string extracted,
            string expected)
        {
            var normalizedExtracted = NormalizeWhitespace(extracted);
            var normalizedExpected = NormalizeWhitespace(expected);
            if (normalizedExtracted.IndexOf(
                    normalizedExpected,
                    StringComparison.Ordinal) >= 0)
            {
                return true;
            }

            return new string(normalizedExtracted
                    .Where(character => !char.IsWhiteSpace(character))
                    .ToArray())
                .IndexOf(
                    new string(normalizedExpected
                        .Where(character => !char.IsWhiteSpace(character))
                        .ToArray()),
                    StringComparison.Ordinal) >= 0;
        }

        private static void AssertFormUnchanged(string path)
        {
            using (var reader = new PdfReader(path))
            {
                Assert(reader.AcroFields.Fields.Count == 1,
                    "Cambio el numero de campos.");
                Assert(
                    string.Equals(
                        reader.AcroFields.GetField("project.name"),
                        "VALOR INTACTO",
                        StringComparison.Ordinal),
                    "Cambio el valor AcroForm.");
            }
        }

        private static void AssertPrefixIsIdentical(
            string sourcePath,
            string outputPath)
        {
            using (var source = File.OpenRead(sourcePath))
            using (var output = File.OpenRead(outputPath))
            {
                Assert(output.Length > source.Length,
                    "La revision incremental no crecio.");
                var sourceBuffer = new byte[1024 * 1024];
                var outputBuffer = new byte[sourceBuffer.Length];
                while (true)
                {
                    var read = source.Read(
                        sourceBuffer,
                        0,
                        sourceBuffer.Length);
                    if (read == 0)
                    {
                        break;
                    }

                    var outputRead = 0;
                    while (outputRead < read)
                    {
                        var chunk = output.Read(
                            outputBuffer,
                            outputRead,
                            read - outputRead);
                        Assert(chunk > 0, "Revision truncada.");
                        outputRead += chunk;
                    }

                    for (var index = 0; index < read; index++)
                    {
                        if (sourceBuffer[index] != outputBuffer[index])
                        {
                            throw new InvalidDataException(
                                "Append mode no conservo el prefijo original.");
                        }
                    }
                }
            }
        }

        private static void AssertNoTemporaryFiles(string directory)
        {
            var temporaries = Directory.GetFiles(
                directory,
                ".*.tmp",
                SearchOption.TopDirectoryOnly);
            Assert(temporaries.Length == 0,
                "Quedaron temporales: " + string.Join(", ", temporaries));
        }

        private static void AssertRegionNearlyEqual(
            PdfTextEditRegion expected,
            PdfTextEditRegion actual)
        {
            AssertRegionNearlyEqual(expected, actual, 0.0001D);
        }

        private static void AssertRegionNearlyEqual(
            PdfTextEditRegion expected,
            PdfTextEditRegion actual,
            double tolerance)
        {
            Assert(expected.PageNumber == actual.PageNumber, "region page");
            AssertNearlyEqual(expected.LeftRatio, actual.LeftRatio, "region left", tolerance);
            AssertNearlyEqual(expected.TopRatio, actual.TopRatio, "region top", tolerance);
            AssertNearlyEqual(expected.RightRatio, actual.RightRatio, "region right", tolerance);
            AssertNearlyEqual(expected.BottomRatio, actual.BottomRatio, "region bottom", tolerance);
        }

        private static void AssertNearlyEqual(
            double expected,
            double actual,
            string label)
        {
            AssertNearlyEqual(expected, actual, label, 0.0001D);
        }

        private static void AssertNearlyEqual(
            double expected,
            double actual,
            string label,
            double tolerance)
        {
            if (Math.Abs(expected - actual) > tolerance)
            {
                throw new InvalidDataException(
                    label + ": esperado=" + expected + ", actual=" + actual);
            }
        }

        private static string NormalizeWhitespace(string value)
        {
            return string.Join(
                " ",
                (value ?? string.Empty)
                    .Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
        }

        private static string ComputeHash(string path)
        {
            using (var input = File.OpenRead(path))
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(input))
                    .Replace("-", string.Empty);
            }
        }

        private static void WriteReport(
            string runDirectory,
            IEnumerable<string> lines,
            bool passed,
            Exception error)
        {
            var reportPath = Path.Combine(runDirectory, "qa-report.txt");
            var output = new List<string>
            {
                "PDF Ligero - motor de edicion visual",
                "Resultado: " + (passed ? "PASS" : "FAIL"),
                "Fecha: " + DateTime.Now.ToString("O", CultureInfo.InvariantCulture),
                string.Empty
            };
            output.AddRange(lines);
            if (error != null)
            {
                output.Add(string.Empty);
                output.Add(error.ToString());
            }
            File.WriteAllLines(reportPath, output.ToArray());
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidDataException(message);
            }
        }
    }
}

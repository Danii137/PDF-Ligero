using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using iTextSharp.text;
using iTextSharp.text.pdf;
using DrawingImage = System.Drawing.Image;
using PdfImage = iTextSharp.text.Image;
using PdfRectangle = iTextSharp.text.Rectangle;

namespace FirmaAutomatica
{
    internal static class LargeUndoRedoHarness
    {
        private const long MinimumFixtureBytes = 25L * 1024L * 1024L;
        private const long TargetFixtureBytes = 32L * 1024L * 1024L;
        private const long MaximumFixtureBytes = 50L * 1024L * 1024L;
        private const int ScanWidth = 2048;
        private const int ScanHeight = 2896;
        private const long JpegQuality = 91L;
        private const int MeasuredCycles = 5;

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                if (args == null || args.Length < 2)
                {
                    Console.Error.WriteLine(
                        "Uso: LargeUndoRedoHarness.exe " +
                        "--generate|--render|--measure <directorio>");
                    return 2;
                }

                var outputDirectory = Path.GetFullPath(args[1]);
                Directory.CreateDirectory(outputDirectory);

                if (string.Equals(
                        args[0],
                        "--generate",
                        StringComparison.OrdinalIgnoreCase))
                {
                    GenerateArtifacts(outputDirectory);
                    return 0;
                }

                if (string.Equals(
                        args[0],
                        "--render",
                        StringComparison.OrdinalIgnoreCase))
                {
                    RenderFixturePreview(outputDirectory);
                    return 0;
                }

                if (string.Equals(
                        args[0],
                        "--measure",
                        StringComparison.OrdinalIgnoreCase))
                {
                    MeasureUndoRedo(outputDirectory);
                    return 0;
                }

                Console.Error.WriteLine("Modo desconocido: " + args[0]);
                return 2;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        private static void GenerateArtifacts(string outputDirectory)
        {
            var fixturePath = GetFixturePath(outputDirectory);
            var variantPath = GetVariantPath(outputDirectory);
            var pageDirectory = Path.Combine(outputDirectory, "scan-pages-temp");

            DeleteDirectoryIfPresent(pageDirectory);
            Directory.CreateDirectory(pageDirectory);
            TryDeleteFile(fixturePath);
            TryDeleteFile(variantPath);

            var pagePaths = new List<string>();
            long jpegBytes = 0;
            var pageNumber = 1;
            while (jpegBytes < TargetFixtureBytes && pageNumber <= 36)
            {
                var pagePath = Path.Combine(
                    pageDirectory,
                    "scan-page-" +
                    pageNumber.ToString("00", CultureInfo.InvariantCulture) +
                    ".jpg");
                CreateScannedJpeg(pagePath, pageNumber);
                pagePaths.Add(pagePath);
                jpegBytes += new FileInfo(pagePath).Length;
                Console.WriteLine(
                    "Página {0}: {1:F2} MiB; acumulado JPEG {2:F2} MiB",
                    pageNumber,
                    ToMiB(new FileInfo(pagePath).Length),
                    ToMiB(jpegBytes));
                pageNumber++;
            }

            if (jpegBytes < TargetFixtureBytes)
            {
                throw new InvalidOperationException(
                    "No se alcanzó el tamaño objetivo del escaneado.");
            }

            AssembleScannedPdf(fixturePath, pagePaths);
            CreateVariantPdf(fixturePath, variantPath);
            DeleteDirectoryIfPresent(pageDirectory);

            var fixtureInfo = new FileInfo(fixturePath);
            var variantInfo = new FileInfo(variantPath);
            if (fixtureInfo.Length < MinimumFixtureBytes ||
                fixtureInfo.Length > MaximumFixtureBytes)
            {
                throw new InvalidOperationException(
                    "El fixture quedó fuera del intervalo de 25-50 MiB: " +
                    ToMiB(fixtureInfo.Length).ToString("F2", CultureInfo.InvariantCulture) +
                    " MiB.");
            }

            if (variantInfo.Length < MinimumFixtureBytes ||
                variantInfo.Length > MaximumFixtureBytes)
            {
                throw new InvalidOperationException(
                    "La revisión quedó fuera del intervalo de 25-50 MiB: " +
                    ToMiB(variantInfo.Length).ToString("F2", CultureInfo.InvariantCulture) +
                    " MiB.");
            }

            int pages;
            using (var reader = new PdfReader(fixturePath))
            {
                pages = reader.NumberOfPages;
            }

            var manifestPath = Path.Combine(outputDirectory, "fixture-manifest.txt");
            using (var writer = new StreamWriter(manifestPath, false, new UTF8Encoding(false)))
            {
                writer.WriteLine("Fixture=" + fixturePath);
                writer.WriteLine(
                    "FixtureBytes=" +
                    fixtureInfo.Length.ToString(CultureInfo.InvariantCulture));
                writer.WriteLine(
                    "FixtureMiB=" +
                    ToMiB(fixtureInfo.Length).ToString("F2", CultureInfo.InvariantCulture));
                writer.WriteLine("FixtureSha256=" + ComputeSha256(fixturePath));
                writer.WriteLine("RevisionSource=" + variantPath);
                writer.WriteLine(
                    "RevisionBytes=" +
                    variantInfo.Length.ToString(CultureInfo.InvariantCulture));
                writer.WriteLine(
                    "RevisionMiB=" +
                    ToMiB(variantInfo.Length).ToString("F2", CultureInfo.InvariantCulture));
                writer.WriteLine("RevisionSha256=" + ComputeSha256(variantPath));
                writer.WriteLine(
                    "Pages=" + pages.ToString(CultureInfo.InvariantCulture));
                writer.WriteLine(
                    "RasterPixels=" +
                    ScanWidth.ToString(CultureInfo.InvariantCulture) +
                    "x" +
                    ScanHeight.ToString(CultureInfo.InvariantCulture));
                writer.WriteLine(
                    "ApproximateDpi=248x248");
                writer.WriteLine(
                    "JpegQuality=" +
                    JpegQuality.ToString(CultureInfo.InvariantCulture));
            }

            Console.WriteLine(
                "FIXTURE_OK path=\"{0}\" size={1:F2}MiB pages={2} sha256={3}",
                fixturePath,
                ToMiB(fixtureInfo.Length),
                pages,
                ComputeSha256(fixturePath));
            Console.WriteLine(
                "VARIANT_OK path=\"{0}\" size={1:F2}MiB sha256={2}",
                variantPath,
                ToMiB(variantInfo.Length),
                ComputeSha256(variantPath));
        }

        private static void CreateScannedJpeg(string path, int pageNumber)
        {
            using (var bitmap = new Bitmap(
                ScanWidth,
                ScanHeight,
                PixelFormat.Format24bppRgb))
            {
                FillPaperNoise(bitmap, pageNumber);
                DrawScannedDocument(bitmap, pageNumber);

                var codec = ImageCodecInfo.GetImageEncoders()
                    .First(item =>
                        string.Equals(
                            item.MimeType,
                            "image/jpeg",
                            StringComparison.OrdinalIgnoreCase));
                using (var parameters = new EncoderParameters(1))
                {
                    parameters.Param[0] = new EncoderParameter(
                        System.Drawing.Imaging.Encoder.Quality,
                        JpegQuality);
                    bitmap.Save(path, codec, parameters);
                }
            }
        }

        private static void FillPaperNoise(Bitmap bitmap, int pageNumber)
        {
            var bounds = new System.Drawing.Rectangle(
                0,
                0,
                bitmap.Width,
                bitmap.Height);
            var data = bitmap.LockBits(
                bounds,
                ImageLockMode.WriteOnly,
                PixelFormat.Format24bppRgb);
            try
            {
                var bytes = new byte[data.Stride * data.Height];
                uint state = unchecked(
                    (uint)(0x9E3779B9u + ((uint)pageNumber * 2654435761u)));
                for (var y = 0; y < data.Height; y++)
                {
                    var row = y * data.Stride;
                    var verticalShade =
                        (int)(4.0 * y / Math.Max(1, data.Height - 1));
                    for (var x = 0; x < data.Width; x++)
                    {
                        state ^= state << 13;
                        state ^= state >> 17;
                        state ^= state << 5;
                        var noise = (int)(state & 31u);
                        var scannerBand =
                            ((x + pageNumber * 37) % 503) < 5 ? -3 : 0;
                        var value = 246 - verticalShade - noise + scannerBand;
                        if (value < 205)
                        {
                            value = 205;
                        }
                        else if (value > 255)
                        {
                            value = 255;
                        }

                        var offset = row + x * 3;
                        bytes[offset] = (byte)Math.Max(0, value - 1);
                        bytes[offset + 1] = (byte)value;
                        bytes[offset + 2] = (byte)Math.Min(255, value + 1);
                    }
                }

                Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        private static void DrawScannedDocument(Bitmap bitmap, int pageNumber)
        {
            using (var graphics = Graphics.FromImage(bitmap))
            using (var titleFont = new System.Drawing.Font(
                "Arial",
                34f,
                FontStyle.Bold,
                GraphicsUnit.Pixel))
            using (var smallFont = new System.Drawing.Font(
                "Arial",
                19f,
                FontStyle.Regular,
                GraphicsUnit.Pixel))
            using (var captionFont = new System.Drawing.Font(
                "Arial",
                16f,
                FontStyle.Bold,
                GraphicsUnit.Pixel))
            using (var ink = new SolidBrush(Color.FromArgb(43, 46, 45)))
            using (var fadedInk = new SolidBrush(Color.FromArgb(73, 75, 72)))
            using (var hairline = new Pen(Color.FromArgb(78, 80, 77), 2f))
            using (var lightLine = new Pen(Color.FromArgb(146, 145, 139), 1f))
            using (var stampPen = new Pen(Color.FromArgb(178, 72, 57), 4f))
            using (var stampBrush = new SolidBrush(Color.FromArgb(178, 72, 57)))
            {
                graphics.TextRenderingHint =
                    System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
                graphics.SmoothingMode =
                    System.Drawing.Drawing2D.SmoothingMode.HighQuality;

                var margin = 145;
                graphics.DrawRectangle(
                    hairline,
                    margin,
                    120,
                    bitmap.Width - margin * 2,
                    bitmap.Height - 250);
                graphics.DrawString(
                    "EXPEDIENTE TÉCNICO / DOCUMENTO ESCANEADO",
                    titleFont,
                    ink,
                    margin + 34,
                    155);
                graphics.DrawString(
                    "VALIDACIÓN DE RENDIMIENTO · PDF LIGERO",
                    captionFont,
                    stampBrush,
                    margin + 36,
                    211);
                graphics.DrawLine(
                    stampPen,
                    margin + 35,
                    248,
                    bitmap.Width - margin - 35,
                    248);

                graphics.DrawString(
                    "PROYECTO:",
                    captionFont,
                    fadedInk,
                    margin + 36,
                    285);
                graphics.DrawString(
                    "EDIFICIO ADMINISTRATIVO - MEMORIA Y PLANOS",
                    smallFont,
                    ink,
                    margin + 190,
                    283);
                graphics.DrawString(
                    "HOJA " +
                    pageNumber.ToString("00", CultureInfo.InvariantCulture),
                    captionFont,
                    fadedInk,
                    bitmap.Width - margin - 175,
                    285);

                var seed = unchecked(pageNumber * 7919 + 17);
                var random = new Random(seed);
                var textTop = 365;
                for (var section = 0; section < 4; section++)
                {
                    var sectionTop = textTop + section * 505;
                    graphics.DrawString(
                        (section + 1).ToString(CultureInfo.InvariantCulture) +
                        ". " +
                        GetSectionTitle(pageNumber, section),
                        captionFont,
                        ink,
                        margin + 36,
                        sectionTop);
                    graphics.DrawLine(
                        lightLine,
                        margin + 36,
                        sectionTop + 30,
                        bitmap.Width - margin - 36,
                        sectionTop + 30);

                    for (var line = 0; line < 9; line++)
                    {
                        var y = sectionTop + 65 + line * 39;
                        var indent = line == 0 ? 42 : 0;
                        var usable = bitmap.Width - margin * 2 - 92 - indent;
                        var width = usable - random.Next(0, 285);
                        var thickness = 3 + random.Next(0, 3);
                        graphics.FillRectangle(
                            fadedInk,
                            margin + 52 + indent,
                            y,
                            width,
                            thickness);
                        if (line % 3 == 1)
                        {
                            graphics.FillRectangle(
                                fadedInk,
                                margin + 52,
                                y + 11,
                                Math.Max(340, width - random.Next(160, 430)),
                                2);
                        }
                    }
                }

                var tableTop = 2380;
                var tableLeft = margin + 36;
                var tableWidth = bitmap.Width - margin * 2 - 72;
                var rowHeight = 55;
                graphics.DrawString(
                    "CUADRO DE SUPERFICIES Y CONTROL DOCUMENTAL",
                    captionFont,
                    ink,
                    tableLeft,
                    tableTop - 42);
                for (var row = 0; row <= 5; row++)
                {
                    graphics.DrawLine(
                        hairline,
                        tableLeft,
                        tableTop + row * rowHeight,
                        tableLeft + tableWidth,
                        tableTop + row * rowHeight);
                }

                var columns = new[] { 0, 650, 1100, 1450, tableWidth };
                for (var index = 0; index < columns.Length; index++)
                {
                    graphics.DrawLine(
                        hairline,
                        tableLeft + columns[index],
                        tableTop,
                        tableLeft + columns[index],
                        tableTop + rowHeight * 5);
                }

                for (var row = 0; row < 5; row++)
                {
                    graphics.DrawString(
                        row == 0 ? "DESCRIPCIÓN" : "Unidad " + row,
                        smallFont,
                        ink,
                        tableLeft + 14,
                        tableTop + row * rowHeight + 13);
                    graphics.DrawString(
                        (18 + pageNumber * 3 + row * 7).ToString(
                            CultureInfo.InvariantCulture) +
                        ",25 m²",
                        smallFont,
                        ink,
                        tableLeft + 675,
                        tableTop + row * rowHeight + 13);
                    graphics.DrawString(
                        row % 2 == 0 ? "REVISADO" : "PENDIENTE",
                        smallFont,
                        row % 2 == 0 ? stampBrush : fadedInk,
                        tableLeft + 1120,
                        tableTop + row * rowHeight + 13);
                }

                graphics.DrawEllipse(
                    stampPen,
                    bitmap.Width - margin - 310,
                    bitmap.Height - 225,
                    170,
                    90);
                graphics.DrawString(
                    "CONTROL\n" +
                    pageNumber.ToString("00", CultureInfo.InvariantCulture),
                    captionFont,
                    stampBrush,
                    bitmap.Width - margin - 263,
                    bitmap.Height - 211);
                graphics.DrawString(
                    "Documento rasterizado para pruebas internas. " +
                    "Sin capa de texto OCR.",
                    smallFont,
                    fadedInk,
                    margin + 36,
                    bitmap.Height - 185);
            }
        }

        private static string GetSectionTitle(int pageNumber, int section)
        {
            var titles = new[]
            {
                "OBJETO Y ALCANCE",
                "ANTECEDENTES Y CONDICIONES",
                "DESCRIPCIÓN CONSTRUCTIVA",
                "JUSTIFICACIÓN NORMATIVA",
                "MEDICIONES Y COMPROBACIONES",
                "OBSERVACIONES DE OBRA"
            };
            return titles[(pageNumber + section) % titles.Length];
        }

        private static void AssembleScannedPdf(
            string outputPath,
            IList<string> pagePaths)
        {
            using (var stream = new FileStream(
                outputPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            using (var document = new Document(PageSize.A4, 0, 0, 0, 0))
            {
                var pdfWriter = PdfWriter.GetInstance(document, stream);
                pdfWriter.SetFullCompression();
                pdfWriter.CompressionLevel = PdfStream.BEST_COMPRESSION;
                document.AddTitle("Escaneado sintético realista para validación");
                document.AddSubject("Fixture rasterizado 25-50 MiB");
                document.AddCreator("PDF Ligero - banco de pruebas");
                document.Open();

                for (var index = 0; index < pagePaths.Count; index++)
                {
                    if (index > 0)
                    {
                        document.NewPage();
                    }

                    var image = PdfImage.GetInstance(pagePaths[index]);
                    image.SetAbsolutePosition(0, 0);
                    image.ScaleAbsolute(PageSize.A4.Width, PageSize.A4.Height);
                    document.Add(image);
                }
            }
        }

        private static void CreateVariantPdf(
            string sourcePath,
            string outputPath)
        {
            using (var reader = new PdfReader(sourcePath))
            using (var stream = new FileStream(
                outputPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            using (var stamper = new PdfStamper(reader, stream, '\0', true))
            {
                var metadata = reader.Info == null
                    ? new Dictionary<string, string>()
                    : new Dictionary<string, string>(reader.Info);
                metadata["Subject"] =
                    "Revisión de validación para cinco ciclos Undo/Redo";
                metadata["ModDate"] = DateTime.UtcNow.ToString(
                    "yyyyMMddHHmmss",
                    CultureInfo.InvariantCulture);
                stamper.MoreInfo = metadata;

                var canvas = stamper.GetOverContent(1);
                canvas.SaveState();
                canvas.SetColorStroke(new BaseColor(178, 72, 57));
                canvas.SetLineWidth(1.1f);
                canvas.Rectangle(430f, 16f, 145f, 20f);
                canvas.Stroke();
                canvas.RestoreState();

                var font = BaseFont.CreateFont(
                    BaseFont.HELVETICA_BOLD,
                    BaseFont.CP1252,
                    BaseFont.NOT_EMBEDDED);
                canvas.BeginText();
                canvas.SetFontAndSize(font, 6.8f);
                canvas.SetColorFill(new BaseColor(178, 72, 57));
                canvas.SetTextMatrix(438f, 23f);
                canvas.ShowText("REVISION DE VALIDACION");
                canvas.EndText();
            }
        }

        private static void RenderFixturePreview(string outputDirectory)
        {
            var fixturePath = GetFixturePath(outputDirectory);
            ValidateLargePdf(fixturePath);
            var previewPath = Path.Combine(
                outputDirectory,
                "fixture-page-01-preview.png");
            var imageCounts = new List<int>();
            var extractedTextCharacters = 0;

            using (var document = PdfiumViewer.PdfDocument.Load(fixturePath))
            using (var image = document.Render(
                0,
                1240,
                1754,
                150,
                150,
                PdfiumViewer.PdfRenderFlags.Annotations |
                PdfiumViewer.PdfRenderFlags.LcdText |
                PdfiumViewer.PdfRenderFlags.LimitImageCacheSize))
            {
                image.Save(previewPath, ImageFormat.Png);
            }

            using (var reader = new PdfReader(fixturePath))
            {
                for (var page = 1; page <= reader.NumberOfPages; page++)
                {
                    extractedTextCharacters +=
                        iTextSharp.text.pdf.parser.PdfTextExtractor
                            .GetTextFromPage(reader, page)
                            .Length;
                    var resources = reader
                        .GetPageN(page)
                        .GetAsDict(PdfName.RESOURCES);
                    var xObjects = resources == null
                        ? null
                        : resources.GetAsDict(PdfName.XOBJECT);
                    var imageCount = 0;
                    if (xObjects != null)
                    {
                        foreach (PdfName key in xObjects.Keys)
                        {
                            var stream = PdfReader.GetPdfObject(
                                xObjects.Get(key)) as PRStream;
                            if (stream != null &&
                                PdfName.IMAGE.Equals(
                                    stream.GetAsName(PdfName.SUBTYPE)))
                            {
                                imageCount++;
                            }
                        }
                    }

                    imageCounts.Add(imageCount);
                }
            }

            Console.WriteLine(
                "RENDER_OK path=\"{0}\" pages={1} imagesPerPage={2}-{3} " +
                "extractedTextChars={4}",
                previewPath,
                imageCounts.Count,
                imageCounts.Min(),
                imageCounts.Max(),
                extractedTextCharacters);
        }

        private static void MeasureUndoRedo(string outputDirectory)
        {
            var fixturePath = GetFixturePath(outputDirectory);
            var variantPath = GetVariantPath(outputDirectory);
            ValidateLargePdf(fixturePath);
            ValidateLargePdf(variantPath);

            var fixtureHashBefore = ComputeSha256(fixturePath);
            var variantHashBefore = ComputeSha256(variantPath);
            var recoveryRoot = Path.Combine(
                outputDirectory,
                "isolated-recovery-root");
            DeleteDirectoryIfPresent(recoveryRoot);
            Directory.CreateDirectory(recoveryRoot);
            Environment.SetEnvironmentVariable(
                PdfEditSession.RecoveryRootOverrideEnvironmentVariable,
                recoveryRoot);

            var csvPath = Path.Combine(outputDirectory, "undo-redo-results.csv");
            var summaryPath = Path.Combine(outputDirectory, "undo-redo-summary.txt");
            var metrics = new List<OperationMetrics>();
            string ownedRevisionPath = null;
            string fixtureHashAfter;
            string variantHashAfter;
            MemorySnapshot initialBaseline;
            MemorySnapshot finalSnapshot;
            int pageCount;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            PdfViewerForm form = null;
            try
            {
                form = new PdfViewerForm(new[] { fixturePath });
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(60, 60);
                form.Size = new Size(1120, 780);
                form.Show();

                PumpUntil(
                    delegate
                    {
                        var active = GetActiveWorkspace(form);
                        return active != null &&
                            GetWorkspaceBoolean(active, "IsLoaded");
                    },
                    TimeSpan.FromSeconds(45),
                    "El PDF grande no terminó de abrirse.");
                PumpEvents(TimeSpan.FromMilliseconds(900));

                var workspace = GetActiveWorkspace(form);
                var session = GetWorkspaceSession(workspace);
                var variantLength = new FileInfo(variantPath).Length;
                ownedRevisionPath = session.ReserveRevisionPath(variantLength);
                CopyFileBuffered(variantPath, ownedRevisionPath);
                session.CommitRevision(
                    ownedRevisionPath,
                    "Revisión grande de validación");

                var applied = InvokeApplyRevision(
                    form,
                    workspace,
                    ownedRevisionPath,
                    0);
                if (!applied)
                {
                    throw new InvalidOperationException(
                        "La revisión grande no pudo activarse en el visor.");
                }

                PumpEvents(TimeSpan.FromMilliseconds(900));
                if (!string.Equals(
                        session.CurrentPath,
                        ownedRevisionPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "La sesión y el PDF visible no quedaron en la misma revisión.");
                }

                using (var reader = new PdfReader(fixturePath))
                {
                    pageCount = reader.NumberOfPages;
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                PumpEvents(TimeSpan.FromMilliseconds(250));
                initialBaseline = MemorySnapshot.Capture();

                var undoMethod = typeof(PdfViewerForm).GetMethod(
                    "UndoActiveDocument",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var redoMethod = typeof(PdfViewerForm).GetMethod(
                    "RedoActiveDocument",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (undoMethod == null || redoMethod == null)
                {
                    throw new MissingMethodException(
                        "No se localizaron UndoActiveDocument/RedoActiveDocument.");
                }

                for (var cycle = 1; cycle <= MeasuredCycles; cycle++)
                {
                    var undoMetrics = MeasureOperation(
                        cycle,
                        "Undo",
                        delegate { undoMethod.Invoke(form, null); });
                    metrics.Add(undoMetrics);
                    if (!string.Equals(
                            session.CurrentPath,
                            fixturePath,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            "Undo no volvió al fixture en el ciclo " + cycle + ".");
                    }

                    var redoMetrics = MeasureOperation(
                        cycle,
                        "Redo",
                        delegate { redoMethod.Invoke(form, null); });
                    metrics.Add(redoMetrics);
                    if (!string.Equals(
                            session.CurrentPath,
                            ownedRevisionPath,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            "Redo no volvió a la revisión en el ciclo " + cycle + ".");
                    }

                    Console.WriteLine(
                        "CYCLE {0}: undo={1:F1}ms redo={2:F1}ms " +
                        "peakWS={3:F1}MiB peakPrivate={4:F1}MiB handles={5}",
                        cycle,
                        undoMetrics.ElapsedMilliseconds,
                        redoMetrics.ElapsedMilliseconds,
                        ToMiB(Math.Max(
                            undoMetrics.Peak.WorkingSetBytes,
                            redoMetrics.Peak.WorkingSetBytes)),
                        ToMiB(Math.Max(
                            undoMetrics.Peak.PrivateBytes,
                            redoMetrics.Peak.PrivateBytes)),
                        Math.Max(
                            undoMetrics.Peak.HandleCount,
                            redoMetrics.Peak.HandleCount));
                }

                finalSnapshot = MemorySnapshot.Capture();
                session.MarkCurrentRevisionSaved();
                SetWorkspaceString(workspace, "ContentPath", fixturePath);
                form.Close();
                PumpEvents(TimeSpan.FromMilliseconds(300));
                form.Dispose();
                form = null;
            }
            finally
            {
                if (form != null)
                {
                    try
                    {
                        var workspace = GetActiveWorkspace(form);
                        var session = workspace == null
                            ? null
                            : GetWorkspaceSession(workspace);
                        if (session != null)
                        {
                            session.MarkCurrentRevisionSaved();
                        }

                        SetWorkspaceString(workspace, "ContentPath", fixturePath);
                        form.Close();
                        form.Dispose();
                    }
                    catch
                    {
                    }
                }

                Environment.SetEnvironmentVariable(
                    PdfEditSession.RecoveryRootOverrideEnvironmentVariable,
                    null);
            }

            fixtureHashAfter = ComputeSha256(fixturePath);
            variantHashAfter = ComputeSha256(variantPath);
            if (!string.Equals(
                    fixtureHashBefore,
                    fixtureHashAfter,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    variantHashBefore,
                    variantHashAfter,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "La validación modificó uno de los PDF fuente.");
            }

            EnsureExclusiveOpen(fixturePath);
            EnsureExclusiveOpen(variantPath);
            if (!string.IsNullOrWhiteSpace(ownedRevisionPath) &&
                File.Exists(ownedRevisionPath))
            {
                throw new InvalidOperationException(
                    "La revisión temporal no fue eliminada al cerrar el visor.");
            }

            WriteCsv(csvPath, metrics);
            WriteSummary(
                summaryPath,
                fixturePath,
                variantPath,
                pageCount,
                fixtureHashBefore,
                variantHashBefore,
                initialBaseline,
                finalSnapshot,
                metrics);

            DeleteDirectoryIfPresent(recoveryRoot);
            Console.WriteLine("MEASUREMENT_OK csv=\"" + csvPath + "\"");
            Console.WriteLine("SUMMARY=\"" + summaryPath + "\"");
        }

        private static OperationMetrics MeasureOperation(
            int cycle,
            string operation,
            Action action)
        {
            var sampler = new ProcessSampler();
            sampler.Start();
            var stopwatch = Stopwatch.StartNew();
            action();
            stopwatch.Stop();
            PumpEvents(TimeSpan.FromMilliseconds(300));
            var peak = sampler.Stop();
            var post = MemorySnapshot.Capture();
            return new OperationMetrics(
                cycle,
                operation,
                stopwatch.Elapsed.TotalMilliseconds,
                sampler.Baseline,
                peak,
                post);
        }

        private static object GetActiveWorkspace(PdfViewerForm form)
        {
            var field = typeof(PdfViewerForm).GetField(
                "activeWorkspace",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new MissingFieldException("activeWorkspace");
            }

            return field.GetValue(form);
        }

        private static bool GetWorkspaceBoolean(object workspace, string name)
        {
            var field = workspace.GetType().GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public);
            if (field == null)
            {
                throw new MissingFieldException(name);
            }

            return (bool)field.GetValue(workspace);
        }

        private static PdfEditSession GetWorkspaceSession(object workspace)
        {
            var field = workspace.GetType().GetField(
                "EditSession",
                BindingFlags.Instance | BindingFlags.Public);
            if (field == null)
            {
                throw new MissingFieldException("EditSession");
            }

            return (PdfEditSession)field.GetValue(workspace);
        }

        private static void SetWorkspaceString(
            object workspace,
            string name,
            string value)
        {
            if (workspace == null)
            {
                return;
            }

            var field = workspace.GetType().GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public);
            if (field == null)
            {
                throw new MissingFieldException(name);
            }

            field.SetValue(workspace, value);
        }

        private static bool InvokeApplyRevision(
            PdfViewerForm form,
            object workspace,
            string path,
            int pageIndex)
        {
            var method = typeof(PdfViewerForm)
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .FirstOrDefault(
                    item =>
                        string.Equals(
                            item.Name,
                            "ApplyRevisionToWorkspace",
                            StringComparison.Ordinal) &&
                        item.GetParameters().Length == 3);
            if (method == null)
            {
                throw new MissingMethodException(
                    "ApplyRevisionToWorkspace(3 argumentos)");
            }

            return (bool)method.Invoke(
                form,
                new object[] { workspace, path, pageIndex });
        }

        private static void PumpUntil(
            Func<bool> condition,
            TimeSpan timeout,
            string timeoutMessage)
        {
            var stopwatch = Stopwatch.StartNew();
            while (!condition())
            {
                Application.DoEvents();
                Thread.Sleep(12);
                if (stopwatch.Elapsed > timeout)
                {
                    throw new TimeoutException(timeoutMessage);
                }
            }
        }

        private static void PumpEvents(TimeSpan duration)
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < duration)
            {
                Application.DoEvents();
                Thread.Sleep(8);
            }
        }

        private static void ValidateLargePdf(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    "Falta el PDF de validación. Ejecuta antes --generate.",
                    path);
            }

            var length = new FileInfo(path).Length;
            if (length < MinimumFixtureBytes || length > MaximumFixtureBytes)
            {
                throw new InvalidDataException(
                    "El PDF no está entre 25 y 50 MiB: " + path);
            }

            using (var reader = new PdfReader(path))
            {
                if (reader.NumberOfPages < 1)
                {
                    throw new InvalidDataException("PDF sin páginas: " + path);
                }
            }
        }

        private static void CopyFileBuffered(string source, string destination)
        {
            using (var input = new FileStream(
                source,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            using (var output = new FileStream(
                destination,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            {
                input.CopyTo(output, 1024 * 1024);
                output.Flush(true);
            }
        }

        private static void EnsureExclusiveOpen(string path)
        {
            using (new FileStream(
                path,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None))
            {
            }
        }

        private static string ComputeSha256(string path)
        {
            using (var algorithm = SHA256.Create())
            using (var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            {
                return BitConverter
                    .ToString(algorithm.ComputeHash(stream))
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
            }
        }

        private static void WriteCsv(
            string path,
            IEnumerable<OperationMetrics> metrics)
        {
            using (var writer = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                writer.WriteLine(
                    "cycle,operation,elapsed_ms," +
                    "baseline_ws_mib,peak_ws_mib,delta_ws_mib,post_ws_mib," +
                    "baseline_private_mib,peak_private_mib,delta_private_mib," +
                    "post_private_mib,baseline_handles,peak_handles," +
                    "delta_handles,post_handles");
                foreach (var item in metrics)
                {
                    writer.WriteLine(
                        string.Join(
                            ",",
                            new[]
                            {
                                item.Cycle.ToString(CultureInfo.InvariantCulture),
                                item.Operation,
                                item.ElapsedMilliseconds.ToString(
                                    "F1",
                                    CultureInfo.InvariantCulture),
                                ToMiB(item.Baseline.WorkingSetBytes).ToString(
                                    "F1",
                                    CultureInfo.InvariantCulture),
                                ToMiB(item.Peak.WorkingSetBytes).ToString(
                                    "F1",
                                    CultureInfo.InvariantCulture),
                                ToMiB(
                                    item.Peak.WorkingSetBytes -
                                    item.Baseline.WorkingSetBytes).ToString(
                                    "F1",
                                    CultureInfo.InvariantCulture),
                                ToMiB(item.Post.WorkingSetBytes).ToString(
                                    "F1",
                                    CultureInfo.InvariantCulture),
                                ToMiB(item.Baseline.PrivateBytes).ToString(
                                    "F1",
                                    CultureInfo.InvariantCulture),
                                ToMiB(item.Peak.PrivateBytes).ToString(
                                    "F1",
                                    CultureInfo.InvariantCulture),
                                ToMiB(
                                    item.Peak.PrivateBytes -
                                    item.Baseline.PrivateBytes).ToString(
                                    "F1",
                                    CultureInfo.InvariantCulture),
                                ToMiB(item.Post.PrivateBytes).ToString(
                                    "F1",
                                    CultureInfo.InvariantCulture),
                                item.Baseline.HandleCount.ToString(
                                    CultureInfo.InvariantCulture),
                                item.Peak.HandleCount.ToString(
                                    CultureInfo.InvariantCulture),
                                (item.Peak.HandleCount -
                                 item.Baseline.HandleCount).ToString(
                                    CultureInfo.InvariantCulture),
                                item.Post.HandleCount.ToString(
                                    CultureInfo.InvariantCulture)
                            }));
                }
            }
        }

        private static void WriteSummary(
            string path,
            string fixturePath,
            string variantPath,
            int pageCount,
            string fixtureHash,
            string variantHash,
            MemorySnapshot initialBaseline,
            MemorySnapshot finalSnapshot,
            IList<OperationMetrics> metrics)
        {
            var peakWs = metrics.Max(item => item.Peak.WorkingSetBytes);
            var peakPrivate = metrics.Max(item => item.Peak.PrivateBytes);
            var peakHandles = metrics.Max(item => item.Peak.HandleCount);
            var maxUndo = metrics
                .Where(item => item.Operation == "Undo")
                .Max(item => item.ElapsedMilliseconds);
            var maxRedo = metrics
                .Where(item => item.Operation == "Redo")
                .Max(item => item.ElapsedMilliseconds);
            var medianUndo = Median(
                metrics
                    .Where(item => item.Operation == "Undo")
                    .Select(item => item.ElapsedMilliseconds));
            var medianRedo = Median(
                metrics
                    .Where(item => item.Operation == "Redo")
                    .Select(item => item.ElapsedMilliseconds));

            using (var writer = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                writer.WriteLine("PDF Ligero - validación Undo/Redo de PDF escaneado grande");
                writer.WriteLine(
                    "FechaUTC=" +
                    DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
                writer.WriteLine("OS=" + Environment.OSVersion);
                writer.WriteLine(
                    "Process64Bit=" +
                    Environment.Is64BitProcess.ToString(CultureInfo.InvariantCulture));
                writer.WriteLine(
                    "LogicalProcessors=" +
                    Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture));
                writer.WriteLine("Fixture=" + fixturePath);
                writer.WriteLine(
                    "FixtureMiB=" +
                    ToMiB(new FileInfo(fixturePath).Length).ToString(
                        "F2",
                        CultureInfo.InvariantCulture));
                writer.WriteLine("FixtureSha256BeforeAfter=" + fixtureHash);
                writer.WriteLine("RevisionSource=" + variantPath);
                writer.WriteLine(
                    "RevisionMiB=" +
                    ToMiB(new FileInfo(variantPath).Length).ToString(
                        "F2",
                        CultureInfo.InvariantCulture));
                writer.WriteLine("RevisionSha256BeforeAfter=" + variantHash);
                writer.WriteLine(
                    "Pages=" + pageCount.ToString(CultureInfo.InvariantCulture));
                writer.WriteLine(
                    "Cycles=" +
                    MeasuredCycles.ToString(CultureInfo.InvariantCulture));
                writer.WriteLine(
                    "InitialWorkingSetMiB=" +
                    ToMiB(initialBaseline.WorkingSetBytes).ToString(
                        "F1",
                        CultureInfo.InvariantCulture));
                writer.WriteLine(
                    "InitialPrivateMiB=" +
                    ToMiB(initialBaseline.PrivateBytes).ToString(
                        "F1",
                        CultureInfo.InvariantCulture));
                writer.WriteLine(
                    "InitialHandles=" +
                    initialBaseline.HandleCount.ToString(CultureInfo.InvariantCulture));
                writer.WriteLine(
                    "PeakWorkingSetMiB=" +
                    ToMiB(peakWs).ToString("F1", CultureInfo.InvariantCulture));
                writer.WriteLine(
                    "PeakPrivateMiB=" +
                    ToMiB(peakPrivate).ToString("F1", CultureInfo.InvariantCulture));
                writer.WriteLine(
                    "PeakHandles=" +
                    peakHandles.ToString(CultureInfo.InvariantCulture));
                writer.WriteLine(
                    "FinalWorkingSetMiB=" +
                    ToMiB(finalSnapshot.WorkingSetBytes).ToString(
                        "F1",
                        CultureInfo.InvariantCulture));
                writer.WriteLine(
                    "FinalPrivateMiB=" +
                    ToMiB(finalSnapshot.PrivateBytes).ToString(
                        "F1",
                        CultureInfo.InvariantCulture));
                writer.WriteLine(
                    "FinalHandles=" +
                    finalSnapshot.HandleCount.ToString(CultureInfo.InvariantCulture));
                writer.WriteLine(
                    "UndoMedianMs=" +
                    medianUndo.ToString("F1", CultureInfo.InvariantCulture));
                writer.WriteLine(
                    "UndoMaxMs=" +
                    maxUndo.ToString("F1", CultureInfo.InvariantCulture));
                writer.WriteLine(
                    "RedoMedianMs=" +
                    medianRedo.ToString("F1", CultureInfo.InvariantCulture));
                writer.WriteLine(
                    "RedoMaxMs=" +
                    maxRedo.ToString("F1", CultureInfo.InvariantCulture));
                writer.WriteLine("SourceHashesUnchanged=True");
                writer.WriteLine("SourceLocksReleased=True");
                writer.WriteLine("OwnedRevisionRemovedOnClose=True");
                writer.WriteLine();
                writer.WriteLine(
                    "Nota: cada pico cubre la llamada síncrona y 300 ms de " +
                    "repintado/estabilización con la ventana real visible.");
            }
        }

        private static double Median(IEnumerable<double> values)
        {
            var ordered = values.OrderBy(value => value).ToArray();
            if (ordered.Length == 0)
            {
                return 0;
            }

            var middle = ordered.Length / 2;
            return ordered.Length % 2 == 0
                ? (ordered[middle - 1] + ordered[middle]) / 2.0
                : ordered[middle];
        }

        private static string GetFixturePath(string outputDirectory)
        {
            return Path.Combine(
                outputDirectory,
                "fixture-scanned-a4-32mb.pdf");
        }

        private static string GetVariantPath(string outputDirectory)
        {
            return Path.Combine(
                outputDirectory,
                "fixture-scanned-a4-32mb-revision.pdf");
        }

        private static double ToMiB(long bytes)
        {
            return bytes / (1024.0 * 1024.0);
        }

        private static void DeleteDirectoryIfPresent(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }

        private static void TryDeleteFile(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private sealed class ProcessSampler
        {
            private readonly ManualResetEvent ready = new ManualResetEvent(false);
            private readonly object sync = new object();
            private volatile bool stopRequested;
            private Thread thread;
            private MemorySnapshot peak;

            public MemorySnapshot Baseline { get; private set; }

            public void Start()
            {
                Baseline = MemorySnapshot.Capture();
                peak = Baseline;
                thread = new Thread(SampleLoop);
                thread.IsBackground = true;
                thread.Name = "PDF Ligero validation sampler";
                thread.Start();
                if (!ready.WaitOne(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException(
                        "El muestreador de memoria no arrancó.");
                }
            }

            public MemorySnapshot Stop()
            {
                stopRequested = true;
                if (thread != null && !thread.Join(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException(
                        "El muestreador de memoria no terminó.");
                }

                var final = MemorySnapshot.Capture();
                Record(final);
                ready.Close();
                lock (sync)
                {
                    return peak;
                }
            }

            private void SampleLoop()
            {
                ready.Set();
                while (!stopRequested)
                {
                    try
                    {
                        Record(MemorySnapshot.Capture());
                    }
                    catch
                    {
                    }

                    Thread.Sleep(2);
                }
            }

            private void Record(MemorySnapshot sample)
            {
                lock (sync)
                {
                    peak = new MemorySnapshot(
                        Math.Max(peak.WorkingSetBytes, sample.WorkingSetBytes),
                        Math.Max(peak.PrivateBytes, sample.PrivateBytes),
                        Math.Max(peak.HandleCount, sample.HandleCount));
                }
            }
        }

        private sealed class OperationMetrics
        {
            public OperationMetrics(
                int cycle,
                string operation,
                double elapsedMilliseconds,
                MemorySnapshot baseline,
                MemorySnapshot peak,
                MemorySnapshot post)
            {
                Cycle = cycle;
                Operation = operation;
                ElapsedMilliseconds = elapsedMilliseconds;
                Baseline = baseline;
                Peak = peak;
                Post = post;
            }

            public int Cycle { get; private set; }

            public string Operation { get; private set; }

            public double ElapsedMilliseconds { get; private set; }

            public MemorySnapshot Baseline { get; private set; }

            public MemorySnapshot Peak { get; private set; }

            public MemorySnapshot Post { get; private set; }
        }

        private sealed class MemorySnapshot
        {
            public MemorySnapshot(
                long workingSetBytes,
                long privateBytes,
                int handleCount)
            {
                WorkingSetBytes = workingSetBytes;
                PrivateBytes = privateBytes;
                HandleCount = handleCount;
            }

            public long WorkingSetBytes { get; private set; }

            public long PrivateBytes { get; private set; }

            public int HandleCount { get; private set; }

            public static MemorySnapshot Capture()
            {
                using (var process = Process.GetCurrentProcess())
                {
                    process.Refresh();
                    return new MemorySnapshot(
                        process.WorkingSet64,
                        process.PrivateMemorySize64,
                        process.HandleCount);
                }
            }
        }
    }
}

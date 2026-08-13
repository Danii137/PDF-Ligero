using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Xml;
using iTextSharp.text;
using iTextSharp.text.pdf;
using DrawingRectangleF = System.Drawing.RectangleF;
using FilteredTextRenderListener =
    iTextSharp.text.pdf.parser.FilteredTextRenderListener;
using LocationTextExtractionStrategy =
    iTextSharp.text.pdf.parser.LocationTextExtractionStrategy;
using PdfTextExtractor = iTextSharp.text.pdf.parser.PdfTextExtractor;
using PdfRectangle = iTextSharp.text.Rectangle;
using RegionTextRenderFilter =
    iTextSharp.text.pdf.parser.RegionTextRenderFilter;

namespace FirmaAutomatica
{
    internal enum PdfTextEditFontFamily
    {
        SansSerif,
        Serif,
        Monospace
    }

    internal enum PdfTextEditAlignment
    {
        Left,
        Center,
        Right
    }

    /// <summary>
    /// A rectangle normalized against the visible, rotated page. X grows from
    /// left to right and Y grows from top to bottom, like a WinForms control.
    /// Keeping ratios rather than screen pixels makes the edit independent of
    /// zoom, DPI, CropBox offsets and PDF UserUnit values.
    /// </summary>
    internal sealed class PdfTextEditRegion
    {
        public PdfTextEditRegion(
            int pageNumber,
            double leftRatio,
            double topRatio,
            double rightRatio,
            double bottomRatio)
        {
            if (pageNumber < 1)
            {
                throw new ArgumentOutOfRangeException("pageNumber");
            }

            EnsureFinite(leftRatio, "leftRatio");
            EnsureFinite(topRatio, "topRatio");
            EnsureFinite(rightRatio, "rightRatio");
            EnsureFinite(bottomRatio, "bottomRatio");

            var left = ClampRatio(Math.Min(leftRatio, rightRatio));
            var right = ClampRatio(Math.Max(leftRatio, rightRatio));
            var top = ClampRatio(Math.Min(topRatio, bottomRatio));
            var bottom = ClampRatio(Math.Max(topRatio, bottomRatio));
            if (right - left < 0.000001D ||
                bottom - top < 0.000001D)
            {
                throw new ArgumentException(
                    "El rectangulo de edicion no tiene superficie.");
            }

            PageNumber = pageNumber;
            LeftRatio = left;
            TopRatio = top;
            RightRatio = right;
            BottomRatio = bottom;
        }

        public int PageNumber { get; private set; }

        public double LeftRatio { get; private set; }

        public double TopRatio { get; private set; }

        public double RightRatio { get; private set; }

        public double BottomRatio { get; private set; }

        /// <summary>
        /// Converts a client-space selection to normalized visible-page ratios.
        /// Both rectangles use a top-left origin. The selection is clipped to
        /// the page before the ratios are calculated.
        /// </summary>
        public static PdfTextEditRegion FromViewerBounds(
            int pageNumber,
            DrawingRectangleF selectionBounds,
            DrawingRectangleF pageBounds)
        {
            var page = NormalizeDrawingRectangle(pageBounds);
            var selection = NormalizeDrawingRectangle(selectionBounds);
            if (page.Width <= 0F || page.Height <= 0F)
            {
                throw new ArgumentException(
                    "La pagina visible no tiene un tamano valido.",
                    "pageBounds");
            }

            var left = Math.Max(page.Left, selection.Left);
            var top = Math.Max(page.Top, selection.Top);
            var right = Math.Min(page.Right, selection.Right);
            var bottom = Math.Min(page.Bottom, selection.Bottom);
            if (right <= left || bottom <= top)
            {
                throw new ArgumentException(
                    "La seleccion no intersecta la pagina visible.",
                    "selectionBounds");
            }

            return new PdfTextEditRegion(
                pageNumber,
                (left - page.Left) / page.Width,
                (top - page.Top) / page.Height,
                (right - page.Left) / page.Width,
                (bottom - page.Top) / page.Height);
        }

        private static DrawingRectangleF NormalizeDrawingRectangle(
            DrawingRectangleF value)
        {
            var left = Math.Min(value.Left, value.Right);
            var right = Math.Max(value.Left, value.Right);
            var top = Math.Min(value.Top, value.Bottom);
            var bottom = Math.Max(value.Top, value.Bottom);
            return DrawingRectangleF.FromLTRB(left, top, right, bottom);
        }

        private static double ClampRatio(double value)
        {
            return Math.Max(0D, Math.Min(1D, value));
        }

        private static void EnsureFinite(double value, string parameterName)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }
    }

    internal sealed class PdfTextReplacement
    {
        public PdfTextReplacement(
            PdfTextEditRegion region,
            string replacementText)
        {
            if (region == null)
            {
                throw new ArgumentNullException("region");
            }

            Region = region;
            ReplacementText = replacementText ?? string.Empty;
            FontFamily = PdfTextEditFontFamily.SansSerif;
            FontSizePoints = 11F;
            MinimumFontSizePoints = 4F;
            AutoFit = true;
            Alignment = PdfTextEditAlignment.Left;
            TextColor = Color.Black;
            CoverOriginal = true;
            CoverColor = Color.White;
            PaddingPoints = 2F;
            LineSpacingMultiplier = 1.18F;
        }

        public PdfTextEditRegion Region { get; private set; }

        public string ReplacementText { get; set; }

        public PdfTextEditFontFamily FontFamily { get; set; }

        public bool Bold { get; set; }

        public bool Italic { get; set; }

        /// <summary>
        /// Fuente detectada en el texto original. Se intenta primero para que el
        /// reemplazo se parezca al resto de la pagina; si no esta instalada o no
        /// cubre el texto, se usa la familia generica de FontFamily.
        /// </summary>
        public string PreferredFontName { get; set; }

        public float FontSizePoints { get; set; }

        public float MinimumFontSizePoints { get; set; }

        public bool AutoFit { get; set; }

        public PdfTextEditAlignment Alignment { get; set; }

        public Color TextColor { get; set; }

        public bool CoverOriginal { get; set; }

        public Color CoverColor { get; set; }

        public float PaddingPoints { get; set; }

        public float LineSpacingMultiplier { get; set; }
    }

    internal sealed class PdfTextEditAnalysis
    {
        internal PdfTextEditAnalysis(
            string sourcePath,
            long sourceLength,
            long sourceLastWriteUtcTicks,
            string sourceFingerprint,
            int pageCount,
            bool openedWithFullPermissions,
            bool containsXfa,
            bool containsDigitalSignatures,
            bool encrypted,
            int formFieldCount,
            IList<string> signatureNames)
        {
            SourcePath = sourcePath;
            SourceLength = sourceLength;
            SourceLastWriteUtcTicks = sourceLastWriteUtcTicks;
            SourceFingerprint = sourceFingerprint;
            PageCount = pageCount;
            OpenedWithFullPermissions = openedWithFullPermissions;
            ContainsXfa = containsXfa;
            ContainsDigitalSignatures = containsDigitalSignatures;
            IsEncrypted = encrypted;
            FormFieldCount = formFieldCount;
            SignatureNames = new List<string>(
                signatureNames ?? new string[0]).AsReadOnly();
        }

        public string SourcePath { get; private set; }

        public long SourceLength { get; private set; }

        public long SourceLastWriteUtcTicks { get; private set; }

        internal string SourceFingerprint { get; private set; }

        public int PageCount { get; private set; }

        public bool OpenedWithFullPermissions { get; private set; }

        public bool ContainsXfa { get; private set; }

        public bool ContainsDigitalSignatures { get; private set; }

        public bool IsEncrypted { get; private set; }

        public int FormFieldCount { get; private set; }

        public IList<string> SignatureNames { get; private set; }
    }

    /// <summary>
    /// Immutable result of preparing one visual text selection. Analysis,
    /// Pdfium-to-PDF coordinate conversion and regional extraction are done
    /// while the same source read guard and PdfReader remain open.
    /// </summary>
    internal sealed class PdfTextEditPreparation
    {
        internal PdfTextEditPreparation(
            PdfTextEditAnalysis analysis,
            PdfTextEditRegion region,
            string extractedText,
            string extractionError)
            : this(analysis, region, extractedText, extractionError, null)
        {
        }

        internal PdfTextEditPreparation(
            PdfTextEditAnalysis analysis,
            PdfTextEditRegion region,
            string extractedText,
            string extractionError,
            PdfTextStyle detectedStyle)
        {
            if (analysis == null)
            {
                throw new ArgumentNullException("analysis");
            }
            if (region == null)
            {
                throw new ArgumentNullException("region");
            }

            Analysis = analysis;
            Region = region;
            ExtractedText = extractedText ?? string.Empty;
            ExtractionError = extractionError ?? string.Empty;
            DetectedStyle = detectedStyle;
        }

        public PdfTextEditAnalysis Analysis { get; private set; }

        public PdfTextEditRegion Region { get; private set; }

        public string ExtractedText { get; private set; }

        public string ExtractionError { get; private set; }

        /// <summary>
        /// Tipografia real del texto seleccionado, o null si no se pudo leer.
        /// </summary>
        public PdfTextStyle DetectedStyle { get; private set; }

        public bool TextExtractionSucceeded
        {
            get { return string.IsNullOrEmpty(ExtractionError); }
        }
    }

    internal sealed class PdfTextEditSaveResult
    {
        internal PdfTextEditSaveResult(
            string outputPath,
            int pageNumber,
            float actualFontSizePoints,
            string fontDisplayName,
            bool digitalSignaturesInvalidated)
        {
            OutputPath = outputPath;
            PageNumber = pageNumber;
            ActualFontSizePoints = actualFontSizePoints;
            FontDisplayName = fontDisplayName ?? string.Empty;
            DigitalSignaturesInvalidated =
                digitalSignaturesInvalidated;
        }

        public string OutputPath { get; private set; }

        public int PageNumber { get; private set; }

        public float ActualFontSizePoints { get; private set; }

        public string FontDisplayName { get; private set; }

        public bool DigitalSignaturesInvalidated { get; private set; }
    }

    internal sealed class PdfTextEditProgress
    {
        public PdfTextEditProgress(
            int completedSteps,
            int totalSteps,
            string stage)
        {
            CompletedSteps = completedSteps;
            TotalSteps = totalSteps;
            Stage = stage ?? string.Empty;
        }

        public int CompletedSteps { get; private set; }

        public int TotalSteps { get; private set; }

        public string Stage { get; private set; }
    }

    internal static class PdfTextEditService
    {
        public const string DigitalSignatureWarning =
            "La firma digital anterior permanece incrustada, pero el " +
            "reemplazo visual es una modificacion posterior que normalmente " +
            "no queda cubierta por esa firma. Su estado final depende de las " +
            "restricciones definidas por el firmante.";

        public const string XfaUnsupportedMessage =
            "Los formularios XFA pueden cambiar dinamicamente de geometria. " +
            "PDF Ligero no aplica reemplazos visuales sobre XFA porque no " +
            "puede garantizar que el rectangulo siga correspondiendo al " +
            "contenido mostrado.";

        public const string VisualReplacementNotice =
            "Esta operacion cubre el contenido anterior y agrega texto nuevo. " +
            "No elimina el texto ni los datos que puedan existir debajo y no " +
            "debe utilizarse como redaccion segura.";

        private const int ProgressStepCount = 4;

        // Used only by the isolated QA harness to prove that the combined
        // preparation path performs one guard, one hash and one PdfReader.
        // It remains null in the application and has no observable API cost.
        internal static Action<string> DiagnosticIoActivityForTesting;

        public static PdfTextEditAnalysis Analyze(string sourcePdfPath)
        {
            var sourcePath = NormalizeExistingPdfPath(sourcePdfPath);
            using (var sourceGuard = OpenSourceReadGuard(sourcePath))
            {
                var sourceInfo = new FileInfo(sourcePath);
                var sourceFingerprint = ComputeFileHash(sourceGuard);
                PdfReader reader = null;
                try
                {
                    reader = OpenPdfReader(sourcePath);
                    return AnalyzeReader(
                        sourcePath,
                        sourceInfo.Length,
                        sourceInfo.LastWriteTimeUtc.Ticks,
                        sourceFingerprint,
                        reader);
                }
                catch (InvalidDataException)
                {
                    throw;
                }
                catch (UnauthorizedAccessException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException(
                        "No se pudo analizar el PDF para editar texto: " +
                        ex.GetBaseException().Message,
                        ex);
                }
                finally
                {
                    if (reader != null)
                    {
                        reader.Close();
                    }
                }
            }
        }

        /// <summary>
        /// Prepares a visual selection with one source guard, one SHA-256 pass
        /// and one PdfReader. The expected length/timestamp must be the file
        /// identity captured when Pdfium loaded the revision being displayed;
        /// this prevents applying old-view coordinates to a file replaced on
        /// disk before the selection is prepared.
        /// </summary>
        public static PdfTextEditPreparation PrepareSelection(
            string sourcePdfPath,
            int pageIndex,
            DrawingRectangleF pdfBounds,
            long expectedSourceLength,
            long expectedSourceLastWriteUtcTicks)
        {
            return PrepareSelection(
                sourcePdfPath,
                pageIndex,
                pdfBounds,
                new PdfEditViewIdentity(
                    sourcePdfPath,
                    expectedSourceLength,
                    expectedSourceLastWriteUtcTicks));
        }

        /// <summary>
        /// Preferred overload. The immutable token includes the path as well
        /// as the file identity captured for the Pdfium document, so it also
        /// rejects coordinates from a view whose ContentPath was switched.
        /// </summary>
        public static PdfTextEditPreparation PrepareSelection(
            string sourcePdfPath,
            int pageIndex,
            DrawingRectangleF pdfBounds,
            PdfEditViewIdentity expectedView)
        {
            var sourcePath = NormalizeExistingPdfPath(sourcePdfPath);
            if (expectedView == null)
            {
                throw new ArgumentNullException("expectedView");
            }
            if (expectedView.ExpectedLength < 0)
            {
                throw new ArgumentOutOfRangeException(
                    "expectedView.ExpectedLength");
            }
            if (expectedView.ExpectedLastWriteUtcTicks < 0)
            {
                throw new ArgumentOutOfRangeException(
                    "expectedView.ExpectedLastWriteUtcTicks");
            }

            using (var sourceGuard = OpenSourceReadGuard(sourcePath))
            {
                var sourceInfo = new FileInfo(sourcePath);
                EnsureExpectedViewIdentity(
                    sourcePath,
                    sourceInfo,
                    expectedView);
                var sourceFingerprint = ComputeFileHash(sourceGuard);
                PdfReader reader = null;
                try
                {
                    reader = OpenPdfReader(sourcePath);
                    var analysis = AnalyzeReader(
                        sourcePath,
                        sourceInfo.Length,
                        sourceInfo.LastWriteTimeUtc.Ticks,
                        sourceFingerprint,
                        reader);
                    var region = CreateRegionFromPdfBounds(
                        reader,
                        analysis,
                        pageIndex,
                        pdfBounds);

                    string extractedText;
                    string extractionError;
                    try
                    {
                        extractedText = ExtractText(reader, region);
                        extractionError = string.Empty;
                    }
                    catch (Exception ex)
                    {
                        extractedText = string.Empty;
                        extractionError = ex.GetBaseException().Message;
                    }

                    // La tipografia se lee aparte del texto: si la deteccion
                    // falla se sigue igualmente, solo que sin preseleccionar.
                    var detectedStyle = PdfTextStyleProbe.Detect(reader, region);

                    return new PdfTextEditPreparation(
                        analysis,
                        region,
                        extractedText,
                        extractionError,
                        detectedStyle);
                }
                catch (InvalidDataException)
                {
                    throw;
                }
                catch (UnauthorizedAccessException)
                {
                    throw;
                }
                catch (InvalidOperationException)
                {
                    throw;
                }
                catch (ArgumentException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException(
                        "No se pudo preparar la zona para editar texto: " +
                        ex.GetBaseException().Message,
                        ex);
                }
                finally
                {
                    if (reader != null)
                    {
                        reader.Close();
                    }
                }
            }
        }

        /// <summary>
        /// Extracts existing static page text intersecting the selected region.
        /// Dynamic XFA values are intentionally outside this helper.
        /// </summary>
        public static string ExtractText(
            PdfTextEditAnalysis analysis,
            PdfTextEditRegion region)
        {
            EnsureAnalysis(analysis);
            EnsureRegion(analysis, region);
            using (OpenSourceReadGuard(analysis.SourcePath))
            {
                EnsureSourceIdentity(analysis);
                PdfReader reader = null;
                try
                {
                    reader = OpenPdfReader(analysis.SourcePath);
                    return ExtractText(reader, region);
                }
                finally
                {
                    if (reader != null)
                    {
                        reader.Close();
                    }
                }
            }
        }

        public static bool TryExtractText(
            PdfTextEditAnalysis analysis,
            PdfTextEditRegion region,
            out string text,
            out string errorMessage)
        {
            try
            {
                text = ExtractText(analysis, region);
                errorMessage = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                text = string.Empty;
                errorMessage = ex.GetBaseException().Message;
                return false;
            }
        }

        /// <summary>
        /// Converts the native PdfiumViewer rectangle returned by
        /// PdfRenderer.PointToPdf/BoundsToPdf to the normalized region used by
        /// the writer. pageIndex is deliberately zero-based to match Pdfium.
        /// RectangleF may have a negative height on PDF coordinates.
        /// </summary>
        public static PdfTextEditRegion CreateRegionFromPdfBounds(
            PdfTextEditAnalysis analysis,
            int pageIndex,
            DrawingRectangleF pdfBounds)
        {
            EnsureAnalysis(analysis);
            if (pageIndex < 0 || pageIndex >= analysis.PageCount)
            {
                throw new ArgumentOutOfRangeException("pageIndex");
            }

            var left = Math.Min(pdfBounds.Left, pdfBounds.Right);
            var right = Math.Max(pdfBounds.Left, pdfBounds.Right);
            var bottom = Math.Min(pdfBounds.Top, pdfBounds.Bottom);
            var top = Math.Max(pdfBounds.Top, pdfBounds.Bottom);
            if (right - left < 0.0001F || top - bottom < 0.0001F)
            {
                throw new ArgumentException(
                    "La seleccion PDF no tiene superficie.",
                    "pdfBounds");
            }

            using (OpenSourceReadGuard(analysis.SourcePath))
            {
                EnsureSourceIdentity(analysis);
                PdfReader reader = null;
                try
                {
                    reader = OpenPdfReader(analysis.SourcePath);
                    return CreateRegionFromPdfBounds(
                        reader,
                        analysis,
                        pageIndex,
                        pdfBounds);
                }
                finally
                {
                    if (reader != null)
                    {
                        reader.Close();
                    }
                }
            }
        }

        public static PdfTextEditSaveResult Save(
            string sourcePdfPath,
            string outputPath,
            PdfTextEditAnalysis analysis,
            PdfTextReplacement replacement)
        {
            return Save(
                sourcePdfPath,
                outputPath,
                analysis,
                replacement,
                null,
                CancellationToken.None);
        }

        public static PdfTextEditSaveResult Save(
            string sourcePdfPath,
            string outputPath,
            PdfTextEditAnalysis analysis,
            PdfTextReplacement replacement,
            Action<PdfTextEditProgress> reportProgress,
            CancellationToken cancellationToken)
        {
            EnsureAnalysis(analysis);
            var sourcePath = NormalizeExistingPdfPath(sourcePdfPath);
            if (!string.Equals(
                    sourcePath,
                    analysis.SourcePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "El analisis pertenece a otra revision del PDF.");
            }

            EnsureReplacement(analysis, replacement);
            if (!analysis.OpenedWithFullPermissions)
            {
                throw new UnauthorizedAccessException(
                    "El PDF esta protegido y no permite editar su contenido.");
            }
            if (analysis.ContainsXfa)
            {
                throw new NotSupportedException(XfaUnsupportedMessage);
            }

            var normalizedOutputPath = ValidateOutputPath(
                sourcePath,
                outputPath);
            var outputDirectory = Path.GetDirectoryName(
                normalizedOutputPath);
            var temporaryPath = Path.Combine(
                outputDirectory,
                "." +
                Path.GetFileNameWithoutExtension(normalizedOutputPath) +
                "." + Guid.NewGuid().ToString("N") + ".tmp");

            Report(reportProgress, 0, "Preparando reemplazo");
            using (OpenSourceReadGuard(sourcePath))
            {
                EnsureSourceIdentity(analysis);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    float actualFontSize;
                    string fontDisplayName;
                    var expectations = WriteEditedPdf(
                        sourcePath,
                        temporaryPath,
                        replacement,
                        reportProgress,
                        cancellationToken,
                        out actualFontSize,
                        out fontDisplayName);

                    cancellationToken.ThrowIfCancellationRequested();
                    Report(reportProgress, 3, "Comprobando revision");
                    ValidateWrittenPdf(
                        temporaryPath,
                        replacement,
                        expectations);
                    cancellationToken.ThrowIfCancellationRequested();
                    // The FileShare.Read guard acquired before the first
                    // identity check remains open through publication, so a
                    // second full-file SHA-256 pass here would be redundant.
                    CommitTemporaryFile(
                        temporaryPath,
                        normalizedOutputPath);
                    Report(reportProgress, 4, "Reemplazo aplicado");

                    return new PdfTextEditSaveResult(
                        normalizedOutputPath,
                        replacement.Region.PageNumber,
                        actualFontSize,
                        fontDisplayName,
                        analysis.ContainsDigitalSignatures);
                }
                finally
                {
                    TryDeleteFile(temporaryPath);
                }
            }
        }

        internal static PdfTextEditRegion RegionFromRawPdfBounds(
            string sourcePdfPath,
            int pageNumber,
            PdfRectangle rawBounds)
        {
            var path = NormalizeExistingPdfPath(sourcePdfPath);
            PdfReader reader = null;
            try
            {
                reader = OpenPdfReader(path);
                return PdfTextPageTransform.Create(reader, pageNumber)
                    .GetRegionFromRawRectangle(pageNumber, rawBounds);
            }
            finally
            {
                if (reader != null)
                {
                    reader.Close();
                }
            }
        }

        private static DocumentExpectations WriteEditedPdf(
            string sourcePath,
            string temporaryPath,
            PdfTextReplacement replacement,
            Action<PdfTextEditProgress> reportProgress,
            CancellationToken cancellationToken,
            out float actualFontSize,
            out string fontDisplayName)
        {
            PdfReader reader = null;
            PdfStamper stamper = null;
            FileStream output = null;
            actualFontSize = replacement.FontSizePoints;
            fontDisplayName = string.Empty;
            try
            {
                reader = OpenPdfReader(sourcePath);
                if (!reader.IsOpenedWithFullPermissions)
                {
                    throw new UnauthorizedAccessException(
                        "El PDF esta protegido y no permite editar su contenido.");
                }
                if (ContainsXfa(reader))
                {
                    throw new NotSupportedException(XfaUnsupportedMessage);
                }

                var expectations = CaptureDocumentExpectations(
                    reader,
                    replacement.Region.PageNumber);
                var transform = PdfTextPageTransform.Create(
                    reader,
                    replacement.Region.PageNumber);
                var visualRectangle = transform.GetVisualRectangle(
                    replacement.Region);

                output = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    1024 * 1024,
                    FileOptions.SequentialScan |
                    FileOptions.WriteThrough);
                stamper = new PdfStamper(
                    reader,
                    output,
                    '\0',
                    true);
                stamper.Writer.CloseStream = false;
                stamper.RotateContents = false;
                stamper.MoreInfo = CloneMetadata(expectations.Metadata);

                // Sin esto, PdfStamper regenera el paquete XMP a partir del
                // diccionario Info y por el camino pierde propiedades que no
                // sabe reconstruir, como xmpMM:DocumentID e InstanceID, que
                // llevan todos los PDF de Word. La validacion posterior lo
                // detectaba y abortaba la edicion, asi que hasta ahora no se
                // podia editar el texto de un PDF hecho con Word.
                //
                // Es el mismo criterio que ya aplican PdfAcroFormService y
                // PdfPageOrganizerService: se conserva el paquete original y se
                // deja que el stamper refresque Producer, ModifyDate y
                // MetadataDate, que son los tres campos tecnicos que la
                // validacion tolera.
                var sourceXmpMetadata = reader.Metadata;
                if (sourceXmpMetadata != null && sourceXmpMetadata.Length > 0)
                {
                    stamper.XmpMetadata = sourceXmpMetadata;
                }

                cancellationToken.ThrowIfCancellationRequested();
                Report(reportProgress, 1, "Ajustando texto");
                var canvas = stamper.GetOverContent(
                    replacement.Region.PageNumber);
                PdfTextResolvedFont resolvedFont = null;
                var text = replacement.ReplacementText ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    resolvedFont = PdfUnicodeFontResolver.Create(
                        replacement.FontFamily,
                        replacement.Bold,
                        replacement.Italic,
                        text,
                        replacement.PreferredFontName);
                    fontDisplayName = resolvedFont.DisplayName;
                    actualFontSize = ResolveFontSize(
                        canvas,
                        visualRectangle,
                        replacement,
                        resolvedFont.BaseFont);
                }

                cancellationToken.ThrowIfCancellationRequested();
                Report(reportProgress, 2, "Escribiendo revision segura");
                canvas.SaveState();
                try
                {
                    transform.ConcatVisualToRaw(canvas);
                    if (replacement.CoverOriginal)
                    {
                        canvas.SetColorFill(ToPdfColor(
                            replacement.CoverColor));
                        canvas.Rectangle(
                            visualRectangle.Left,
                            visualRectangle.Bottom,
                            visualRectangle.Width,
                            visualRectangle.Height);
                        canvas.Fill();
                    }

                    if (resolvedFont != null)
                    {
                        var status = RenderText(
                            canvas,
                            visualRectangle,
                            replacement,
                            resolvedFont.BaseFont,
                            actualFontSize,
                            false);
                        if (ColumnText.HasMoreText(status))
                        {
                            throw new InvalidOperationException(
                                "El texto no cabe completo en el rectangulo " +
                                "seleccionado.");
                        }
                    }
                }
                finally
                {
                    canvas.RestoreState();
                }

                stamper.Close();
                stamper = null;
                reader.Close();
                reader = null;
                output.Flush(true);
                output.Dispose();
                output = null;
                return expectations;
            }
            finally
            {
                if (stamper != null)
                {
                    try
                    {
                        stamper.Close();
                    }
                    catch
                    {
                    }
                }
                else if (reader != null)
                {
                    reader.Close();
                }

                if (output != null)
                {
                    output.Dispose();
                }
            }
        }

        private static float ResolveFontSize(
            PdfContentByte canvas,
            PdfRectangle rectangle,
            PdfTextReplacement replacement,
            BaseFont baseFont)
        {
            var maximum = replacement.FontSizePoints;
            var minimum = Math.Min(
                maximum,
                replacement.MinimumFontSizePoints);
            if (TextFits(
                    canvas,
                    rectangle,
                    replacement,
                    baseFont,
                    maximum))
            {
                return maximum;
            }

            if (!replacement.AutoFit)
            {
                throw new InvalidOperationException(
                    "El texto no cabe con el tamano elegido. Activa el " +
                    "autoajuste, reduce la fuente o amplia el rectangulo.");
            }

            if (!TextFits(
                    canvas,
                    rectangle,
                    replacement,
                    baseFont,
                    minimum))
            {
                throw new InvalidOperationException(
                    "El texto no cabe ni con el tamano minimo. Amplia el " +
                    "rectangulo o reduce el contenido.");
            }

            var low = minimum;
            var high = maximum;
            for (var pass = 0; pass < 14; pass++)
            {
                var candidate = (low + high) / 2F;
                if (TextFits(
                        canvas,
                        rectangle,
                        replacement,
                        baseFont,
                        candidate))
                {
                    low = candidate;
                }
                else
                {
                    high = candidate;
                }
            }

            return (float)Math.Floor(low * 10F) / 10F;
        }

        private static bool TextFits(
            PdfContentByte canvas,
            PdfRectangle rectangle,
            PdfTextReplacement replacement,
            BaseFont baseFont,
            float fontSize)
        {
            return !ColumnText.HasMoreText(
                RenderText(
                    canvas,
                    rectangle,
                    replacement,
                    baseFont,
                    fontSize,
                    true));
        }

        private static int RenderText(
            PdfContentByte canvas,
            PdfRectangle rectangle,
            PdfTextReplacement replacement,
            BaseFont baseFont,
            float fontSize,
            bool simulate)
        {
            var padding = Math.Max(
                0F,
                Math.Min(
                    replacement.PaddingPoints,
                    Math.Min(
                        rectangle.Width / 3F,
                        rectangle.Height / 3F)));
            var left = rectangle.Left + padding;
            var bottom = rectangle.Bottom + padding;
            var right = rectangle.Right - padding;
            var top = rectangle.Top - padding;
            if (right - left < 0.5F || top - bottom < 0.5F)
            {
                throw new InvalidOperationException(
                    "El rectangulo es demasiado pequeno para escribir texto.");
            }

            var font = new iTextSharp.text.Font(
                baseFont,
                fontSize,
                iTextSharp.text.Font.NORMAL,
                ToPdfColor(replacement.TextColor));
            var phrase = new Phrase(
                replacement.ReplacementText ?? string.Empty,
                font);
            var leading = Math.Max(
                fontSize,
                fontSize * replacement.LineSpacingMultiplier);
            var column = new ColumnText(canvas);
            column.SetSimpleColumn(
                phrase,
                left,
                bottom,
                right,
                top,
                leading,
                ToElementAlignment(replacement.Alignment));
            return column.Go(simulate);
        }

        private static int ToElementAlignment(
            PdfTextEditAlignment alignment)
        {
            switch (alignment)
            {
                case PdfTextEditAlignment.Center:
                    return Element.ALIGN_CENTER;

                case PdfTextEditAlignment.Right:
                    return Element.ALIGN_RIGHT;

                default:
                    return Element.ALIGN_LEFT;
            }
        }

        private static BaseColor ToPdfColor(Color color)
        {
            return new BaseColor(color.R, color.G, color.B);
        }

        private static void ValidateWrittenPdf(
            string outputPath,
            PdfTextReplacement replacement,
            DocumentExpectations expected)
        {
            if (!File.Exists(outputPath) ||
                new FileInfo(outputPath).Length <= 0)
            {
                throw new InvalidDataException(
                    "La revision de texto esta vacia.");
            }

            PdfReader reader = null;
            try
            {
                reader = OpenPdfReader(outputPath);
                if (reader.NumberOfPages != expected.PageCount)
                {
                    throw new InvalidDataException(
                        "El numero de paginas cambio.");
                }

                var actualGeometry = CapturePageGeometry(reader);
                if (!ListsEqual(expected.PageGeometry, actualGeometry))
                {
                    throw new InvalidDataException(
                        "Las cajas o la rotacion de una pagina cambiaron.");
                }

                for (var page = 1; page <= reader.NumberOfPages; page++)
                {
                    if (page == replacement.Region.PageNumber)
                    {
                        continue;
                    }

                    var actualHash = ComputeBytesHash(
                        reader.GetPageContent(page));
                    if (!string.Equals(
                            expected.PageContentHashes[page - 1],
                            actualHash,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            "Cambio el contenido de una pagina no editada.");
                    }
                }

                ValidateMetadata(reader, expected);
                ValidateFormFields(reader, expected);
                ValidateSignatures(reader, expected);

                var replacementText =
                    NormalizeWhitespace(replacement.ReplacementText);
                if (!string.IsNullOrEmpty(replacementText))
                {
                    var extracted = NormalizeWhitespace(
                        PdfTextExtractor.GetTextFromPage(
                            reader,
                            replacement.Region.PageNumber));
                    if (!ContainsExtractedText(
                            extracted,
                            replacementText))
                    {
                        throw new InvalidDataException(
                            "El texto nuevo no se pudo recuperar de la " +
                            "revision escrita.");
                    }
                }
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidDataException(
                    "La revision de texto no supero la comprobacion: " +
                    ex.GetBaseException().Message,
                    ex);
            }
            finally
            {
                if (reader != null)
                {
                    reader.Close();
                }
            }
        }

        private static bool ContainsExtractedText(
            string extracted,
            string expected)
        {
            if ((extracted ?? string.Empty).IndexOf(
                    expected ?? string.Empty,
                    StringComparison.Ordinal) >= 0)
            {
                return true;
            }

            // Extractors may insert layout whitespace at a visual line break,
            // including between adjacent CJK glyphs. The PDF remains searchable
            // even though that whitespace was not present in the source string.
            return RemoveWhitespace(extracted).IndexOf(
                RemoveWhitespace(expected),
                StringComparison.Ordinal) >= 0;
        }

        private static DocumentExpectations CaptureDocumentExpectations(
            PdfReader reader,
            int editedPageNumber)
        {
            var pageContentHashes = new List<string>();
            for (var page = 1; page <= reader.NumberOfPages; page++)
            {
                pageContentHashes.Add(
                    ComputeBytesHash(reader.GetPageContent(page)));
            }

            var formValues = CaptureFormValues(reader);
            var signatures = CaptureSignatures(reader);
            return new DocumentExpectations(
                reader.NumberOfPages,
                editedPageNumber,
                pageContentHashes,
                CapturePageGeometry(reader),
                CloneMetadata(reader.Info),
                CaptureXmpExpectation(reader.Metadata),
                formValues,
                CountWidgets(reader),
                signatures);
        }

        private static IList<string> CapturePageGeometry(PdfReader reader)
        {
            var result = new List<string>();
            for (var page = 1; page <= reader.NumberOfPages; page++)
            {
                var media = reader.GetPageSize(page);
                var crop = reader.GetCropBox(page) ?? media;
                var pageDictionary = reader.GetPageN(page);
                var userUnit = pageDictionary == null
                    ? null
                    : pageDictionary.GetAsNumber(
                        new PdfName("UserUnit"));
                result.Add(
                    SerializeRectangle(media) + "|" +
                    SerializeRectangle(crop) + "|" +
                    reader.GetPageRotation(page).ToString(
                        CultureInfo.InvariantCulture) + "|" +
                    (userUnit == null
                        ? "1"
                        : userUnit.FloatValue.ToString(
                            "R",
                            CultureInfo.InvariantCulture)));
            }

            return result;
        }

        private static string SerializeRectangle(PdfRectangle rectangle)
        {
            if (rectangle == null)
            {
                return "<missing>";
            }

            return rectangle.Left.ToString("R", CultureInfo.InvariantCulture) +
                "," + rectangle.Bottom.ToString("R", CultureInfo.InvariantCulture) +
                "," + rectangle.Right.ToString("R", CultureInfo.InvariantCulture) +
                "," + rectangle.Top.ToString("R", CultureInfo.InvariantCulture);
        }

        private static SortedDictionary<string, FormExpectation>
            CaptureFormValues(PdfReader reader)
        {
            var result = new SortedDictionary<string, FormExpectation>(
                StringComparer.Ordinal);
            var fields = reader.AcroFields;
            if (fields == null)
            {
                return result;
            }

            foreach (var field in fields.Fields)
            {
                result[field.Key] = new FormExpectation(
                    fields.GetFieldType(field.Key),
                    fields.GetField(field.Key) ?? string.Empty);
            }

            return result;
        }

        private static IList<SignatureExpectation> CaptureSignatures(
            PdfReader reader)
        {
            var result = new List<SignatureExpectation>();
            if (reader.AcroFields == null)
            {
                return result;
            }

            foreach (var name in reader.AcroFields.GetSignatureNames()
                .OrderBy(value => value, StringComparer.Ordinal))
            {
                bool? cryptographicallyValid = null;
                try
                {
                    var signature = reader.AcroFields.VerifySignature(name);
                    if (signature != null)
                    {
                        cryptographicallyValid = signature.Verify();
                    }
                }
                catch
                {
                    // Some malformed legacy signatures cannot be verified by
                    // iText. Their field/name is still preserved and checked.
                }

                result.Add(new SignatureExpectation(
                    name,
                    cryptographicallyValid));
            }

            return result;
        }

        private static void ValidateMetadata(
            PdfReader reader,
            DocumentExpectations expected)
        {
            var expectedDescriptive = GetDescriptiveInfo(
                expected.Metadata);
            var actualDescriptive = GetDescriptiveInfo(reader.Info);
            if (expectedDescriptive.Count != actualDescriptive.Count)
            {
                throw new InvalidDataException(
                    "Cambio el numero de metadatos descriptivos.");
            }

            foreach (var item in expectedDescriptive)
            {
                string actualValue;
                if (!actualDescriptive.TryGetValue(
                        item.Key,
                        out actualValue) ||
                    !string.Equals(
                        item.Value,
                        actualValue,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "No se conservo el metadato \"" + item.Key + "\".");
                }
            }

            string expectedProducer;
            string actualProducer;
            expected.Metadata.TryGetValue(
                "Producer",
                out expectedProducer);
            reader.Info.TryGetValue("Producer", out actualProducer);
            if (!IsExpectedProducerTransition(
                    expectedProducer,
                    actualProducer))
            {
                throw new InvalidDataException(
                    "Producer cambio de una forma no atribuible a la " +
                    "revision incremental de iTextSharp.");
            }

            ValidateXmp(
                reader.Metadata,
                expected.Xmp);
        }

        private static Dictionary<string, string> GetDescriptiveInfo(
            IDictionary<string, string> metadata)
        {
            var result = new Dictionary<string, string>(
                StringComparer.Ordinal);
            if (metadata == null)
            {
                return result;
            }

            foreach (var item in metadata)
            {
                if (string.Equals(
                        item.Key,
                        "ModDate",
                        StringComparison.Ordinal) ||
                    string.Equals(
                        item.Key,
                        "Producer",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                result[item.Key] = item.Value;
            }

            return result;
        }

        private static bool IsExpectedProducerTransition(
            string before,
            string after)
        {
            var original = before ?? string.Empty;
            var actual = after ?? string.Empty;
            if (string.Equals(
                    original,
                    actual,
                    StringComparison.Ordinal))
            {
                return true;
            }

            var iTextVersion = iTextSharp.text.Version
                .GetInstance()
                .GetVersion;
            if (original.Length == 0)
            {
                return string.Equals(
                    actual,
                    iTextVersion,
                    StringComparison.Ordinal);
            }

            return string.Equals(
                actual,
                original + "; modified using " + iTextVersion,
                StringComparison.Ordinal);
        }

        private static void ValidateXmp(
            byte[] actualBytes,
            XmpExpectation expected)
        {
            var actual = CaptureXmpExpectation(actualBytes);
            if (!expected.WasParsed || !actual.WasParsed)
            {
                if (!string.Equals(
                        expected.RawHash,
                        actual.RawHash,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "El paquete XMP no valido cambio.");
                }

                return;
            }

            if (expected.Exists && !actual.Exists)
            {
                throw new InvalidDataException(
                    "Se elimino el paquete XMP del documento.");
            }
            if (!ListsEqual(
                    expected.DescriptiveProperties,
                    actual.DescriptiveProperties))
            {
                throw new InvalidDataException(
                    "Los metadatos XMP descriptivos cambiaron.");
            }
        }

        private static void ValidateFormFields(
            PdfReader reader,
            DocumentExpectations expected)
        {
            var actual = CaptureFormValues(reader);
            if (expected.FormValues.Count != actual.Count)
            {
                throw new InvalidDataException(
                    "Cambio el numero de campos de formulario.");
            }

            foreach (var item in expected.FormValues)
            {
                FormExpectation value;
                if (!actual.TryGetValue(item.Key, out value) ||
                    value.FieldType != item.Value.FieldType ||
                    !string.Equals(
                        value.Value,
                        item.Value.Value,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Cambio el campo de formulario \"" +
                        item.Key + "\".");
                }
            }

            if (expected.WidgetCount != CountWidgets(reader))
            {
                throw new InvalidDataException(
                    "Cambio el numero de widgets de formulario.");
            }
        }

        private static void ValidateSignatures(
            PdfReader reader,
            DocumentExpectations expected)
        {
            var actual = CaptureSignatures(reader);
            if (expected.Signatures.Count != actual.Count)
            {
                throw new InvalidDataException(
                    "Cambio el numero de firmas digitales.");
            }

            for (var index = 0; index < expected.Signatures.Count; index++)
            {
                var before = expected.Signatures[index];
                var after = actual[index];
                if (!string.Equals(
                        before.Name,
                        after.Name,
                        StringComparison.Ordinal) ||
                    (before.CryptographicallyValid == true &&
                     after.CryptographicallyValid != true))
                {
                    throw new InvalidDataException(
                        "No se conservo la firma digital \"" +
                        before.Name + "\".");
                }
            }
        }

        private static int CountWidgets(PdfReader reader)
        {
            var count = 0;
            for (var page = 1; page <= reader.NumberOfPages; page++)
            {
                var dictionary = reader.GetPageN(page);
                var annotations = dictionary == null
                    ? null
                    : dictionary.GetAsArray(PdfName.ANNOTS);
                if (annotations == null)
                {
                    continue;
                }

                for (var index = 0; index < annotations.Size; index++)
                {
                    var annotation = ResolveDictionary(
                        annotations[index]);
                    if (annotation != null &&
                        PdfName.WIDGET.Equals(
                            annotation.GetAsName(PdfName.SUBTYPE)))
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private static bool ContainsXfa(PdfReader reader)
        {
            var acroForm = ResolveDictionary(
                reader.Catalog.Get(PdfName.ACROFORM));
            return acroForm != null &&
                acroForm.Get(PdfName.XFA) != null;
        }

        private static PdfDictionary ResolveDictionary(PdfObject value)
        {
            return value == null
                ? null
                : PdfReader.GetPdfObject(value) as PdfDictionary;
        }

        private static void EnsureReplacement(
            PdfTextEditAnalysis analysis,
            PdfTextReplacement replacement)
        {
            if (replacement == null)
            {
                throw new ArgumentNullException("replacement");
            }

            EnsureRegion(analysis, replacement.Region);
            if (!replacement.CoverOriginal &&
                string.IsNullOrWhiteSpace(replacement.ReplacementText))
            {
                throw new ArgumentException(
                    "La edicion no cubre ni agrega contenido.",
                    "replacement");
            }
            if (replacement.FontSizePoints < 1F ||
                replacement.FontSizePoints > 300F ||
                float.IsNaN(replacement.FontSizePoints) ||
                float.IsInfinity(replacement.FontSizePoints))
            {
                throw new ArgumentOutOfRangeException(
                    "replacement.FontSizePoints");
            }
            if (replacement.MinimumFontSizePoints < 1F ||
                replacement.MinimumFontSizePoints > 300F ||
                float.IsNaN(replacement.MinimumFontSizePoints) ||
                float.IsInfinity(replacement.MinimumFontSizePoints))
            {
                throw new ArgumentOutOfRangeException(
                    "replacement.MinimumFontSizePoints");
            }
            if (replacement.PaddingPoints < 0F ||
                replacement.PaddingPoints > 100F ||
                float.IsNaN(replacement.PaddingPoints) ||
                float.IsInfinity(replacement.PaddingPoints))
            {
                throw new ArgumentOutOfRangeException(
                    "replacement.PaddingPoints");
            }
            if (replacement.LineSpacingMultiplier < 0.8F ||
                replacement.LineSpacingMultiplier > 3F ||
                float.IsNaN(replacement.LineSpacingMultiplier) ||
                float.IsInfinity(replacement.LineSpacingMultiplier))
            {
                throw new ArgumentOutOfRangeException(
                    "replacement.LineSpacingMultiplier");
            }
        }

        private static void EnsureAnalysis(PdfTextEditAnalysis analysis)
        {
            if (analysis == null)
            {
                throw new ArgumentNullException("analysis");
            }
            if (string.IsNullOrWhiteSpace(analysis.SourcePath) ||
                analysis.PageCount < 1)
            {
                throw new InvalidOperationException(
                    "El analisis del PDF no es valido.");
            }
        }

        private static PdfTextEditAnalysis AnalyzeReader(
            string sourcePath,
            long sourceLength,
            long sourceLastWriteUtcTicks,
            string sourceFingerprint,
            PdfReader reader)
        {
            if (reader == null)
            {
                throw new ArgumentNullException("reader");
            }

            // Detect XFA before constructing AcroFields. Malformed or dynamic
            // XFA packets may throw while iText parses XML, but this edit path
            // is blocked regardless and must report that reason clearly.
            var containsXfa = ContainsXfa(reader);
            var fields = containsXfa
                ? null
                : reader.AcroFields;
            var signatureNames = fields == null
                ? new List<string>()
                : fields.GetSignatureNames()
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToList();
            var formFieldCount = fields == null
                ? 0
                : fields.Fields.Count;
            return new PdfTextEditAnalysis(
                sourcePath,
                sourceLength,
                sourceLastWriteUtcTicks,
                sourceFingerprint,
                reader.NumberOfPages,
                reader.IsOpenedWithFullPermissions,
                containsXfa,
                signatureNames.Count > 0,
                reader.IsEncrypted(),
                formFieldCount,
                signatureNames);
        }

        private static PdfTextEditRegion CreateRegionFromPdfBounds(
            PdfReader reader,
            PdfTextEditAnalysis analysis,
            int pageIndex,
            DrawingRectangleF pdfBounds)
        {
            EnsureAnalysis(analysis);
            if (reader == null)
            {
                throw new ArgumentNullException("reader");
            }
            if (pageIndex < 0 || pageIndex >= analysis.PageCount)
            {
                throw new ArgumentOutOfRangeException("pageIndex");
            }

            var left = Math.Min(pdfBounds.Left, pdfBounds.Right);
            var right = Math.Max(pdfBounds.Left, pdfBounds.Right);
            var bottom = Math.Min(pdfBounds.Top, pdfBounds.Bottom);
            var top = Math.Max(pdfBounds.Top, pdfBounds.Bottom);
            if (right - left < 0.0001F || top - bottom < 0.0001F)
            {
                throw new ArgumentException(
                    "La seleccion PDF no tiene superficie.",
                    "pdfBounds");
            }

            return PdfTextPageTransform
                .Create(reader, pageIndex + 1)
                .GetRegionFromRawRectangle(
                    pageIndex + 1,
                    new PdfRectangle(left, bottom, right, top));
        }

        private static string ExtractText(
            PdfReader reader,
            PdfTextEditRegion region)
        {
            if (reader == null)
            {
                throw new ArgumentNullException("reader");
            }
            if (region == null)
            {
                throw new ArgumentNullException("region");
            }

            var transform = PdfTextPageTransform.Create(
                reader,
                region.PageNumber);
            var rawRectangle = transform.GetRawRectangle(region);
            var strategy = new FilteredTextRenderListener(
                new LocationTextExtractionStrategy(),
                new RegionTextRenderFilter(rawRectangle));
            return (PdfTextExtractor.GetTextFromPage(
                reader,
                region.PageNumber,
                strategy) ?? string.Empty).Trim();
        }

        private static void EnsureExpectedViewIdentity(
            string sourcePath,
            FileInfo current,
            PdfEditViewIdentity expectedView)
        {
            if (expectedView == null ||
                !string.Equals(
                    Path.GetFullPath(sourcePath),
                    Path.GetFullPath(expectedView.ContentPath),
                    StringComparison.OrdinalIgnoreCase) ||
                current == null ||
                !current.Exists ||
                current.Length != expectedView.ExpectedLength ||
                current.LastWriteTimeUtc.Ticks !=
                    expectedView.ExpectedLastWriteUtcTicks)
            {
                throw new InvalidOperationException(
                    "El PDF visible cambio en disco desde que se cargo la " +
                    "pagina. Recarga el documento y vuelve a seleccionar " +
                    "la zona.");
            }
        }

        private static void EnsureRegion(
            PdfTextEditAnalysis analysis,
            PdfTextEditRegion region)
        {
            if (region == null)
            {
                throw new ArgumentNullException("region");
            }
            if (region.PageNumber < 1 ||
                region.PageNumber > analysis.PageCount)
            {
                throw new ArgumentOutOfRangeException(
                    "region.PageNumber");
            }
        }

        private static void EnsureSourceIdentity(
            PdfTextEditAnalysis analysis)
        {
            var info = new FileInfo(analysis.SourcePath);
            if (!info.Exists ||
                info.Length != analysis.SourceLength ||
                info.LastWriteTimeUtc.Ticks !=
                    analysis.SourceLastWriteUtcTicks ||
                !string.Equals(
                    ComputeFileHash(analysis.SourcePath),
                    analysis.SourceFingerprint,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "El PDF cambio desde que se abrio el editor de texto. " +
                    "Vuelve a seleccionar la zona sobre la revision actual.");
            }
        }

        private static string NormalizeExistingPdfPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException(
                    "Se necesita el PDF de origen.",
                    "path");
            }

            var normalized = Path.GetFullPath(path);
            if (!File.Exists(normalized))
            {
                throw new FileNotFoundException(
                    "No existe el PDF de origen.",
                    normalized);
            }
            if (!string.Equals(
                    Path.GetExtension(normalized),
                    ".pdf",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "El archivo de origen no es PDF.",
                    "path");
            }

            return normalized;
        }

        private static string ValidateOutputPath(
            string sourcePath,
            string outputPath)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new ArgumentException(
                    "Se necesita una ruta para la revision.",
                    "outputPath");
            }

            var normalized = Path.GetFullPath(outputPath);
            if (string.Equals(
                    sourcePath,
                    normalized,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "La edicion no puede sobrescribir el PDF original.");
            }
            if (!string.Equals(
                    Path.GetExtension(normalized),
                    ".pdf",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "La revision debe tener extension PDF.",
                    "outputPath");
            }

            var directory = Path.GetDirectoryName(normalized);
            if (string.IsNullOrWhiteSpace(directory) ||
                !Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException(
                    "La carpeta de la revision no existe.");
            }

            return normalized;
        }

        private static PdfReader OpenPdfReader(string path)
        {
            ReportDiagnosticIo("pdf-reader");
            try
            {
                return new PdfReader(path);
            }
            catch (iTextSharp.text.exceptions.BadPasswordException ex)
            {
                throw new UnauthorizedAccessException(
                    "El PDF esta protegido con contrasena.",
                    ex);
            }
        }

        private static FileStream OpenSourceReadGuard(string sourcePath)
        {
            ReportDiagnosticIo("read-guard");
            return new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.RandomAccess);
        }

        private static Dictionary<string, string> CloneMetadata(
            IDictionary<string, string> source)
        {
            var result = new Dictionary<string, string>(
                StringComparer.Ordinal);
            if (source != null)
            {
                foreach (var item in source)
                {
                    result[item.Key] = item.Value;
                }
            }

            return result;
        }

        private static XmpExpectation CaptureXmpExpectation(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return new XmpExpectation(
                    false,
                    true,
                    new List<string>(),
                    ComputeBytesHash(bytes));
            }

            try
            {
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
                if (rdfNodes.Count != 1)
                {
                    throw new InvalidDataException(
                        "El paquete XMP no contiene un unico grafo RDF.");
                }

                // Compare the RDF graph rather than packet bytes. iText may
                // change prefixes, padding and the three technical fields
                // deliberately excluded below during an incremental save.
                var canonical = CanonicalizeXmpGraph(rdfNodes[0]);
                var semanticHash = ComputeBytesHash(
                    new UTF8Encoding(false).GetBytes(canonical));
                return new XmpExpectation(
                    true,
                    true,
                    new List<string> { semanticHash },
                    ComputeBytesHash(bytes));
            }
            catch
            {
                // If a legacy packet is not valid XMP, exact bytes are the
                // only safe preservation criterion.
                return new XmpExpectation(
                    true,
                    false,
                    new List<string>(),
                    ComputeBytesHash(bytes));
            }
        }

        private static string CanonicalizeXmpNode(XmlNode node)
        {
            if (node == null || IsExpectedTechnicalXmpProperty(node))
            {
                return string.Empty;
            }

            if (node.NodeType == XmlNodeType.Text ||
                node.NodeType == XmlNodeType.CDATA ||
                node.NodeType == XmlNodeType.SignificantWhitespace)
            {
                var text = new StringBuilder("T");
                AppendXmpScalar(text, node.Value ?? string.Empty);
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
                    StringComparison.Ordinal) &&
                node.NodeType == XmlNodeType.Element)
            {
                return CanonicalizeRdfDescription(node);
            }

            var builder = new StringBuilder("E");
            AppendXmpScalar(builder, node.NamespaceURI ?? string.Empty);
            AppendXmpScalar(builder, node.LocalName ?? string.Empty);

            var attributes = new List<string>();
            if (node.Attributes != null)
            {
                foreach (XmlAttribute attribute in node.Attributes)
                {
                    if (attribute.NamespaceURI ==
                            "http://www.w3.org/2000/xmlns/" ||
                        IsExpectedTechnicalXmpProperty(attribute))
                    {
                        continue;
                    }

                    attributes.Add(CanonicalizeXmpAttribute(attribute));
                }
            }

            attributes.Sort(StringComparer.Ordinal);
            foreach (var attribute in attributes)
            {
                AppendXmpScalar(builder, attribute);
            }

            var children = new List<string>();
            foreach (XmlNode child in node.ChildNodes)
            {
                var childToken = CanonicalizeXmpNode(child);
                if (!string.IsNullOrEmpty(childToken))
                {
                    children.Add(childToken);
                }
            }

            if (HasUnorderedXmpChildren(node))
            {
                children.Sort(StringComparer.Ordinal);
            }

            foreach (var child in children)
            {
                AppendXmpScalar(builder, child);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Canoniza el grafo RDF entero agrupando las propiedades por sujeto.
        ///
        /// XMP permite repartir las propiedades de un mismo sujeto entre varios
        /// rdf:Description o juntarlas en uno solo, y son equivalentes. Word
        /// escribe uno por espacio de nombres e iText los fusiona al reescribir
        /// el paquete, asi que comparar bloque a bloque hacia fallar la
        /// validacion por un detalle de forma, sin que hubiera cambiado ningun
        /// dato. El efecto practico era que no se podia editar el texto de
        /// ningun PDF hecho con Word.
        /// </summary>
        private static string CanonicalizeXmpGraph(XmlNode rdfNode)
        {
            var subjectOrder = new List<string>();
            var propertiesBySubject = new Dictionary<string, List<string>>(
                StringComparer.Ordinal);
            var structuralBySubject = new Dictionary<string, List<string>>(
                StringComparer.Ordinal);
            var otherNodes = new List<string>();

            foreach (XmlNode child in rdfNode.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (!IsRdfDescriptionElement(child))
                {
                    var token = CanonicalizeXmpNode(child);
                    if (!string.IsNullOrEmpty(token))
                    {
                        otherNodes.Add(token);
                    }

                    continue;
                }

                var subject = GetRdfSubject(child);
                if (!propertiesBySubject.ContainsKey(subject))
                {
                    subjectOrder.Add(subject);
                    propertiesBySubject[subject] = new List<string>();
                    structuralBySubject[subject] = new List<string>();
                }

                CollectRdfDescription(
                    child,
                    structuralBySubject[subject],
                    propertiesBySubject[subject]);
            }

            var builder = new StringBuilder("G");
            subjectOrder.Sort(StringComparer.Ordinal);
            foreach (var subject in subjectOrder)
            {
                var subjectBuilder = new StringBuilder("D");
                AppendXmpScalar(subjectBuilder, subject);

                var structural = structuralBySubject[subject];
                structural.Sort(StringComparer.Ordinal);
                foreach (var attribute in Distinct(structural))
                {
                    AppendXmpScalar(subjectBuilder, attribute);
                }

                var properties = propertiesBySubject[subject];
                properties.Sort(StringComparer.Ordinal);
                foreach (var property in properties)
                {
                    AppendXmpScalar(subjectBuilder, property);
                }

                AppendXmpScalar(builder, subjectBuilder.ToString());
            }

            otherNodes.Sort(StringComparer.Ordinal);
            foreach (var token in otherNodes)
            {
                AppendXmpScalar(builder, token);
            }

            return builder.ToString();
        }

        private static IEnumerable<string> Distinct(IList<string> values)
        {
            string previous = null;
            foreach (var value in values)
            {
                if (previous == null ||
                    !string.Equals(previous, value, StringComparison.Ordinal))
                {
                    yield return value;
                }

                previous = value;
            }
        }

        private static bool IsRdfDescriptionElement(XmlNode node)
        {
            return node != null &&
                node.NodeType == XmlNodeType.Element &&
                string.Equals(
                    node.NamespaceURI,
                    "http://www.w3.org/1999/02/22-rdf-syntax-ns#",
                    StringComparison.Ordinal) &&
                string.Equals(
                    node.LocalName,
                    "Description",
                    StringComparison.Ordinal);
        }

        /// <summary>
        /// Sujeto del que habla un rdf:Description. Casi siempre es la cadena
        /// vacia, que en XMP significa "este documento".
        /// </summary>
        private static string GetRdfSubject(XmlNode node)
        {
            if (node.Attributes == null)
            {
                return string.Empty;
            }

            foreach (XmlAttribute attribute in node.Attributes)
            {
                if (string.Equals(
                        attribute.NamespaceURI,
                        "http://www.w3.org/1999/02/22-rdf-syntax-ns#",
                        StringComparison.Ordinal) &&
                    (string.Equals(
                        attribute.LocalName,
                        "about",
                        StringComparison.Ordinal) ||
                     string.Equals(
                        attribute.LocalName,
                        "ID",
                        StringComparison.Ordinal)))
                {
                    return attribute.Value ?? string.Empty;
                }
            }

            return string.Empty;
        }

        private static string CanonicalizeRdfDescription(XmlNode node)
        {
            var builder = new StringBuilder("D");
            AppendXmpScalar(builder, node.NamespaceURI ?? string.Empty);
            AppendXmpScalar(builder, node.LocalName ?? string.Empty);

            var structuralAttributes = new List<string>();
            var properties = new List<string>();
            CollectRdfDescription(node, structuralAttributes, properties);

            structuralAttributes.Sort(StringComparer.Ordinal);
            properties.Sort(StringComparer.Ordinal);
            foreach (var attribute in structuralAttributes)
            {
                AppendXmpScalar(builder, attribute);
            }

            foreach (var property in properties)
            {
                AppendXmpScalar(builder, property);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Reparte el contenido de un rdf:Description entre atributos
        /// estructurales y propiedades, sin distinguir si vienen escritas como
        /// atributo o como elemento hijo: XMP admite las dos formas para lo
        /// mismo.
        /// </summary>
        private static void CollectRdfDescription(
            XmlNode node,
            List<string> structuralAttributes,
            List<string> properties)
        {
            if (node.Attributes != null)
            {
                foreach (XmlAttribute attribute in node.Attributes)
                {
                    if (attribute.NamespaceURI ==
                            "http://www.w3.org/2000/xmlns/" ||
                        IsExpectedTechnicalXmpProperty(attribute))
                    {
                        continue;
                    }

                    if (IsRdfStructuralAttribute(attribute))
                    {
                        structuralAttributes.Add(
                            CanonicalizeXmpAttribute(attribute));
                    }
                    else
                    {
                        properties.Add(CanonicalizeSimpleXmpProperty(
                            attribute.NamespaceURI,
                            attribute.LocalName,
                            attribute.Value));
                    }
                }
            }

            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element ||
                    IsExpectedTechnicalXmpProperty(child))
                {
                    continue;
                }

                properties.Add(IsSimpleXmpPropertyElement(child)
                    ? CanonicalizeSimpleXmpProperty(
                        child.NamespaceURI,
                        child.LocalName,
                        child.InnerText)
                    : "P" + CanonicalizeXmpNode(child));
            }
        }

        private static string CanonicalizeXmpAttribute(
            XmlAttribute attribute)
        {
            var builder = new StringBuilder("A");
            AppendXmpScalar(
                builder,
                attribute.NamespaceURI ?? string.Empty);
            AppendXmpScalar(builder, attribute.LocalName ?? string.Empty);
            AppendXmpScalar(builder, attribute.Value ?? string.Empty);
            return builder.ToString();
        }

        private static string CanonicalizeSimpleXmpProperty(
            string namespaceUri,
            string localName,
            string value)
        {
            var builder = new StringBuilder("S");
            AppendXmpScalar(builder, namespaceUri ?? string.Empty);
            AppendXmpScalar(builder, localName ?? string.Empty);
            AppendXmpScalar(builder, value ?? string.Empty);
            return builder.ToString();
        }

        private static bool IsRdfStructuralAttribute(XmlAttribute attribute)
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

        private static bool IsSimpleXmpPropertyElement(XmlNode node)
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

        private static void AppendXmpScalar(
            StringBuilder builder,
            string value)
        {
            var normalized = value ?? string.Empty;
            builder.Append(normalized.Length.ToString(
                CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(normalized);
        }

        private static bool IsExpectedTechnicalXmpProperty(XmlNode node)
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

        private static bool HasUnorderedXmpChildren(XmlNode node)
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

        private static bool ListsEqual(
            IList<string> expected,
            IList<string> actual)
        {
            if (expected.Count != actual.Count)
            {
                return false;
            }

            for (var index = 0; index < expected.Count; index++)
            {
                if (!string.Equals(
                        expected[index],
                        actual[index],
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static string ComputeFileHash(string path)
        {
            ReportDiagnosticIo("sha256");
            using (var input = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                1024 * 1024,
                FileOptions.SequentialScan))
            using (var sha = SHA256.Create())
            {
                return ToHex(sha.ComputeHash(input));
            }
        }

        private static string ComputeFileHash(FileStream input)
        {
            if (input == null)
            {
                throw new ArgumentNullException("input");
            }

            ReportDiagnosticIo("sha256");
            var originalPosition = input.Position;
            try
            {
                input.Position = 0;
                using (var sha = SHA256.Create())
                {
                    return ToHex(sha.ComputeHash(input));
                }
            }
            finally
            {
                input.Position = originalPosition;
            }
        }

        private static void ReportDiagnosticIo(string activity)
        {
            var observer = DiagnosticIoActivityForTesting;
            if (observer != null)
            {
                observer(activity);
            }
        }

        private static string ComputeBytesHash(byte[] value)
        {
            if (value == null)
            {
                return "<missing>";
            }

            using (var sha = SHA256.Create())
            {
                return ToHex(sha.ComputeHash(value));
            }
        }

        private static string ToHex(byte[] value)
        {
            return BitConverter.ToString(value)
                .Replace("-", string.Empty);
        }

        private static string NormalizeWhitespace(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var result = new StringBuilder(value.Length);
            var pendingSpace = false;
            foreach (var character in value.Trim())
            {
                if (char.IsWhiteSpace(character))
                {
                    pendingSpace = true;
                    continue;
                }

                if (pendingSpace && result.Length > 0)
                {
                    result.Append(' ');
                }
                result.Append(character);
                pendingSpace = false;
            }

            return result.ToString();
        }

        private static string RemoveWhitespace(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var result = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                if (!char.IsWhiteSpace(character))
                {
                    result.Append(character);
                }
            }

            return result.ToString();
        }

        private static void CommitTemporaryFile(
            string temporaryPath,
            string outputPath)
        {
            if (File.Exists(outputPath))
            {
                File.Replace(temporaryPath, outputPath, null);
            }
            else
            {
                File.Move(temporaryPath, outputPath);
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        private static void Report(
            Action<PdfTextEditProgress> report,
            int completed,
            string stage)
        {
            if (report != null)
            {
                report(new PdfTextEditProgress(
                    completed,
                    ProgressStepCount,
                    stage));
            }
        }

        private sealed class DocumentExpectations
        {
            public DocumentExpectations(
                int pageCount,
                int editedPageNumber,
                IList<string> pageContentHashes,
                IList<string> pageGeometry,
                IDictionary<string, string> metadata,
                XmpExpectation xmp,
                SortedDictionary<string, FormExpectation> formValues,
                int widgetCount,
                IList<SignatureExpectation> signatures)
            {
                PageCount = pageCount;
                EditedPageNumber = editedPageNumber;
                PageContentHashes = pageContentHashes;
                PageGeometry = pageGeometry;
                Metadata = metadata;
                Xmp = xmp;
                FormValues = formValues;
                WidgetCount = widgetCount;
                Signatures = signatures;
            }

            public int PageCount { get; private set; }

            public int EditedPageNumber { get; private set; }

            public IList<string> PageContentHashes { get; private set; }

            public IList<string> PageGeometry { get; private set; }

            public IDictionary<string, string> Metadata { get; private set; }

            public XmpExpectation Xmp { get; private set; }

            public SortedDictionary<string, FormExpectation> FormValues
            {
                get;
                private set;
            }

            public int WidgetCount { get; private set; }

            public IList<SignatureExpectation> Signatures { get; private set; }
        }

        private sealed class XmpExpectation
        {
            public XmpExpectation(
                bool exists,
                bool wasParsed,
                IList<string> descriptiveProperties,
                string rawHash)
            {
                Exists = exists;
                WasParsed = wasParsed;
                DescriptiveProperties = descriptiveProperties == null
                    ? new List<string>()
                    : new List<string>(descriptiveProperties);
                RawHash = rawHash ?? string.Empty;
            }

            public bool Exists { get; private set; }

            public bool WasParsed { get; private set; }

            public IList<string> DescriptiveProperties { get; private set; }

            public string RawHash { get; private set; }
        }

        private sealed class FormExpectation
        {
            public FormExpectation(int fieldType, string value)
            {
                FieldType = fieldType;
                Value = value ?? string.Empty;
            }

            public int FieldType { get; private set; }

            public string Value { get; private set; }
        }

        private sealed class SignatureExpectation
        {
            public SignatureExpectation(
                string name,
                bool? cryptographicallyValid)
            {
                Name = name ?? string.Empty;
                CryptographicallyValid = cryptographicallyValid;
            }

            public string Name { get; private set; }

            public bool? CryptographicallyValid { get; private set; }
        }
    }

    internal sealed class PdfTextResolvedFont
    {
        public PdfTextResolvedFont(BaseFont baseFont, string displayName)
        {
            BaseFont = baseFont;
            DisplayName = displayName ?? string.Empty;
        }

        public BaseFont BaseFont { get; private set; }

        public string DisplayName { get; private set; }
    }

    /// <summary>
    /// Resolves and opens a Unicode font only while an edit is being written.
    /// No font file is touched during normal viewer startup.
    /// </summary>
    internal static class PdfUnicodeFontResolver
    {
        private static readonly object Sync = new object();
        private static Dictionary<string, string> resolvedPaths;

        public static PdfTextResolvedFont Create(
            PdfTextEditFontFamily family,
            bool bold,
            bool italic,
            string text)
        {
            return Create(family, bold, italic, text, null);
        }

        /// <summary>
        /// Igual que la anterior, pero probando primero la fuente detectada en
        /// el PDF original. Si esa fuente no esta instalada o no cubre el texto,
        /// se sigue con los candidatos genericos de siempre: reconocer la
        /// tipografia es una mejora, nunca un motivo para no poder escribir.
        /// </summary>
        public static PdfTextResolvedFont Create(
            PdfTextEditFontFamily family,
            bool bold,
            bool italic,
            string text,
            string preferredFontName)
        {
            var unsupportedSupplementary =
                FindFirstSupplementaryCodePoint(text);
            if (unsupportedSupplementary >= 0)
            {
                // iTextSharp 5's Identity-H pipeline is UCS-2 oriented. A
                // font may report a supplementary glyph through CharExists,
                // yet the resulting PDF is not reliably renderable/searchable.
                throw new InvalidOperationException(
                    "Este editor aun no admite caracteres Unicode fuera del " +
                    "plano basico (U+" +
                    unsupportedSupplementary.ToString(
                        "X",
                        CultureInfo.InvariantCulture) + ").");
            }

            Exception lastError = null;
            foreach (var candidate in
                GetCandidates(family, bold, italic, preferredFontName))
            {
                var path = ResolvePath(candidate.FileName);
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                try
                {
                    var font = BaseFont.CreateFont(
                        path,
                        BaseFont.IDENTITY_H,
                        BaseFont.EMBEDDED);
                    if (!FontCoversText(font, text))
                    {
                        continue;
                    }

                    return new PdfTextResolvedFont(
                        font,
                        candidate.DisplayName);
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    // Try the next installed Windows font. Some fonts declare
                    // embedding restrictions even though they can be previewed.
                }
            }

            if (!CanEncodeWinAnsi(text))
            {
                throw new InvalidOperationException(
                    "No se encontro una fuente Unicode integrable para todos " +
                    "los caracteres del texto" +
                    DescribeFirstNonWinAnsiCodePoint(text) + ".",
                    lastError);
            }

            return new PdfTextResolvedFont(
                BaseFont.CreateFont(
                    GetBuiltInFontName(family, bold, italic),
                    BaseFont.CP1252,
                    BaseFont.NOT_EMBEDDED),
                GetBuiltInDisplayName(family));
        }

        private static IEnumerable<FontCandidate> GetCandidates(
            PdfTextEditFontFamily family,
            bool bold,
            bool italic,
            string preferredFontName)
        {
            // La fuente que tenia el texto original va primero, para que el
            // reemplazo se parezca al resto de la pagina. Si no esta instalada o
            // no cubre el texto, el bucle de Create sigue con las siguientes.
            if (!string.IsNullOrEmpty(preferredFontName))
            {
                string displayName;
                var preferido = PdfSystemFontCatalog.ResolveFileName(
                    preferredFontName,
                    bold,
                    italic,
                    out displayName);
                if (!string.IsNullOrEmpty(preferido))
                {
                    yield return new FontCandidate(preferido, displayName);
                }
            }

            if (family == PdfTextEditFontFamily.Serif)
            {
                if (bold && italic)
                {
                    yield return new FontCandidate("timesbi.ttf", "Times New Roman");
                    yield return new FontCandidate("georgiaz.ttf", "Georgia");
                }
                else if (bold)
                {
                    yield return new FontCandidate("timesbd.ttf", "Times New Roman");
                    yield return new FontCandidate("georgiab.ttf", "Georgia");
                }
                else if (italic)
                {
                    yield return new FontCandidate("timesi.ttf", "Times New Roman");
                    yield return new FontCandidate("georgiai.ttf", "Georgia");
                }
                else
                {
                    yield return new FontCandidate("times.ttf", "Times New Roman");
                    yield return new FontCandidate("georgia.ttf", "Georgia");
                }
            }
            else if (family == PdfTextEditFontFamily.Monospace)
            {
                if (bold && italic)
                {
                    yield return new FontCandidate("consolaz.ttf", "Consolas");
                    yield return new FontCandidate("courbi.ttf", "Courier New");
                }
                else if (bold)
                {
                    yield return new FontCandidate("consolab.ttf", "Consolas");
                    yield return new FontCandidate("courbd.ttf", "Courier New");
                }
                else if (italic)
                {
                    yield return new FontCandidate("consolai.ttf", "Consolas");
                    yield return new FontCandidate("couri.ttf", "Courier New");
                }
                else
                {
                    yield return new FontCandidate("consola.ttf", "Consolas");
                    yield return new FontCandidate("cour.ttf", "Courier New");
                }
            }
            else if (bold && italic)
            {
                yield return new FontCandidate("segoeuiz.ttf", "Segoe UI");
                yield return new FontCandidate("arialbi.ttf", "Arial");
            }
            else if (bold)
            {
                yield return new FontCandidate("segoeuib.ttf", "Segoe UI");
                yield return new FontCandidate("arialbd.ttf", "Arial");
            }
            else if (italic)
            {
                yield return new FontCandidate("segoeuii.ttf", "Segoe UI");
                yield return new FontCandidate("ariali.ttf", "Arial");
            }
            else
            {
                yield return new FontCandidate("segoeui.ttf", "Segoe UI");
                yield return new FontCandidate("arial.ttf", "Arial");
            }

            // Cross-family fallbacks are deliberately last. They preserve the
            // requested style whenever possible, but a visible CJK glyph is
            // safer than silently embedding a preferred font with blank boxes.
            yield return new FontCandidate("calibri.ttf", "Calibri");
            yield return new FontCandidate("arialuni.ttf", "Arial Unicode MS");
            yield return new FontCandidate("msyh.ttc,0", "Microsoft YaHei");
            yield return new FontCandidate("simsun.ttc,0", "SimSun");
            yield return new FontCandidate("msgothic.ttc,0", "MS Gothic");
            yield return new FontCandidate("YuGothR.ttc,0", "Yu Gothic");
            yield return new FontCandidate("meiryo.ttc,0", "Meiryo");
            yield return new FontCandidate("malgun.ttf", "Malgun Gothic");
            yield return new FontCandidate("seguiemj.ttf", "Segoe UI Emoji");
        }

        private static string ResolvePath(string fileName)
        {
            lock (Sync)
            {
                if (resolvedPaths == null)
                {
                    resolvedPaths = new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase);
                }

                string cached;
                if (resolvedPaths.TryGetValue(fileName, out cached))
                {
                    return cached;
                }

                var fontsDirectory = Environment.GetFolderPath(
                    Environment.SpecialFolder.Fonts);
                var collectionMarker = fileName.LastIndexOf(
                    ".ttc,",
                    StringComparison.OrdinalIgnoreCase);
                var diskFileName = collectionMarker < 0
                    ? fileName
                    : fileName.Substring(0, collectionMarker + 4);
                var candidate = string.IsNullOrWhiteSpace(fontsDirectory)
                    ? string.Empty
                    : Path.Combine(fontsDirectory, diskFileName);
                var result = !string.IsNullOrWhiteSpace(candidate) &&
                    File.Exists(candidate)
                    ? candidate + (collectionMarker < 0
                        ? string.Empty
                        : fileName.Substring(collectionMarker + 4))
                    : string.Empty;
                resolvedPaths[fileName] = result;
                return result;
            }
        }

        private static string GetBuiltInFontName(
            PdfTextEditFontFamily family,
            bool bold,
            bool italic)
        {
            if (family == PdfTextEditFontFamily.Serif)
            {
                if (bold && italic)
                {
                    return BaseFont.TIMES_BOLDITALIC;
                }
                if (bold)
                {
                    return BaseFont.TIMES_BOLD;
                }
                if (italic)
                {
                    return BaseFont.TIMES_ITALIC;
                }
                return BaseFont.TIMES_ROMAN;
            }

            if (family == PdfTextEditFontFamily.Monospace)
            {
                if (bold && italic)
                {
                    return BaseFont.COURIER_BOLDOBLIQUE;
                }
                if (bold)
                {
                    return BaseFont.COURIER_BOLD;
                }
                if (italic)
                {
                    return BaseFont.COURIER_OBLIQUE;
                }
                return BaseFont.COURIER;
            }

            if (bold && italic)
            {
                return BaseFont.HELVETICA_BOLDOBLIQUE;
            }
            if (bold)
            {
                return BaseFont.HELVETICA_BOLD;
            }
            if (italic)
            {
                return BaseFont.HELVETICA_OBLIQUE;
            }
            return BaseFont.HELVETICA;
        }

        private static string GetBuiltInDisplayName(
            PdfTextEditFontFamily family)
        {
            switch (family)
            {
                case PdfTextEditFontFamily.Serif:
                    return "Times";

                case PdfTextEditFontFamily.Monospace:
                    return "Courier";

                default:
                    return "Helvetica";
            }
        }

        private static bool CanEncodeWinAnsi(string text)
        {
            try
            {
                var encoding = Encoding.GetEncoding(
                    1252,
                    EncoderFallback.ExceptionFallback,
                    DecoderFallback.ExceptionFallback);
                encoding.GetBytes(text ?? string.Empty);
                return true;
            }
            catch (EncoderFallbackException)
            {
                return false;
            }
        }

        private static bool FontCoversText(BaseFont font, string text)
        {
            if (font == null)
            {
                return false;
            }

            var value = text ?? string.Empty;
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (char.IsControl(character))
                {
                    continue;
                }

                int codePoint;
                if (char.IsHighSurrogate(character))
                {
                    if (index + 1 >= value.Length ||
                        !char.IsLowSurrogate(value[index + 1]))
                    {
                        return false;
                    }

                    codePoint = char.ConvertToUtf32(
                        character,
                        value[++index]);
                }
                else if (char.IsLowSurrogate(character))
                {
                    return false;
                }
                else
                {
                    codePoint = character;
                }

                if (!font.CharExists(codePoint))
                {
                    return false;
                }
            }

            return true;
        }

        private static int FindFirstSupplementaryCodePoint(string text)
        {
            var value = text ?? string.Empty;
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (char.IsHighSurrogate(character) &&
                    index + 1 < value.Length &&
                    char.IsLowSurrogate(value[index + 1]))
                {
                    return char.ConvertToUtf32(
                        character,
                        value[index + 1]);
                }
            }

            return -1;
        }

        private static string DescribeFirstNonWinAnsiCodePoint(string text)
        {
            var value = text ?? string.Empty;
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (char.IsControl(character))
                {
                    continue;
                }

                int codePoint = character;
                if (char.IsHighSurrogate(character) &&
                    index + 1 < value.Length &&
                    char.IsLowSurrogate(value[index + 1]))
                {
                    codePoint = char.ConvertToUtf32(
                        character,
                        value[++index]);
                }

                if (char.IsSurrogate(character) &&
                    codePoint == character)
                {
                    return " (primer caracter no cubierto: U+" +
                        codePoint.ToString(
                            "X",
                            CultureInfo.InvariantCulture) + ")";
                }

                var candidate = char.ConvertFromUtf32(codePoint);
                if (!CanEncodeWinAnsi(candidate))
                {
                    return " (primer caracter no cubierto: U+" +
                        codePoint.ToString("X", CultureInfo.InvariantCulture) +
                        ")";
                }
            }

            return string.Empty;
        }

        private sealed class FontCandidate
        {
            public FontCandidate(string fileName, string displayName)
            {
                FileName = fileName;
                DisplayName = displayName;
            }

            public string FileName { get; private set; }

            public string DisplayName { get; private set; }
        }
    }
}

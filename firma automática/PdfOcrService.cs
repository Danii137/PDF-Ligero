using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using iTextSharp.text.pdf;
using PdfiumViewer;
using PdfiumDocument = PdfiumViewer.PdfDocument;
using LocationTextExtractionStrategy =
    iTextSharp.text.pdf.parser.LocationTextExtractionStrategy;
using PdfTextExtractor =
    iTextSharp.text.pdf.parser.PdfTextExtractor;

namespace FirmaAutomatica
{
    internal sealed class PdfOcrSettings
    {
        public PdfOcrSettings()
        {
            Language = "spa+eng";
            OcrDpi = 240;
            AnalysisDpi = 120;
            AutoOrient = true;
            AutoDeskew = true;
            ReprocessPagesWithText = false;
            MinimumExistingTextCharacters = 24;
            MaximumPixelsPerPage = 16000000;
            SelectedPages = null;
        }

        public string Language { get; set; }

        public int OcrDpi { get; set; }

        public int AnalysisDpi { get; set; }

        public bool AutoOrient { get; set; }

        public bool AutoDeskew { get; set; }

        public bool ReprocessPagesWithText { get; set; }

        public int MinimumExistingTextCharacters { get; set; }

        public int MaximumPixelsPerPage { get; set; }

        /// <summary>
        /// Optional one-based page numbers. Null or empty means every page.
        /// </summary>
        public ICollection<int> SelectedPages { get; set; }

        internal PdfOcrSettings Snapshot()
        {
            var copy = new PdfOcrSettings();
            copy.Language = string.IsNullOrWhiteSpace(Language)
                ? "spa+eng"
                : Language.Trim();
            copy.OcrDpi = Math.Max(150, Math.Min(400, OcrDpi));
            copy.AnalysisDpi = Math.Max(
                72,
                Math.Min(180, AnalysisDpi));
            copy.AutoOrient = AutoOrient;
            copy.AutoDeskew = AutoDeskew;
            copy.ReprocessPagesWithText = ReprocessPagesWithText;
            copy.MinimumExistingTextCharacters = Math.Max(
                1,
                MinimumExistingTextCharacters);
            copy.MaximumPixelsPerPage = Math.Max(
                4000000,
                Math.Min(50000000, MaximumPixelsPerPage));
            copy.SelectedPages = SelectedPages == null
                ? null
                : new List<int>(SelectedPages);
            return copy;
        }
    }

    internal sealed class PdfOcrAvailability
    {
        public PdfOcrAvailability(
            bool isAvailable,
            string executablePath,
            IList<string> availableLanguages,
            string message)
        {
            IsAvailable = isAvailable;
            ExecutablePath = executablePath ?? string.Empty;
            AvailableLanguages = availableLanguages ??
                new List<string>();
            Message = message ?? string.Empty;
        }

        public bool IsAvailable { get; private set; }

        public string ExecutablePath { get; private set; }

        public IList<string> AvailableLanguages { get; private set; }

        public string Message { get; private set; }
    }

    internal sealed class PdfOcrPageAnalysis
    {
        public PdfOcrPageAnalysis(
            int pageNumber,
            int existingTextCharacters,
            bool selected,
            bool needsOcr,
            int annotationCount,
            int suggestedClockwiseRotationDegrees,
            float orientationConfidence,
            float suggestedDeskewDegrees,
            float deskewConfidence,
            string note)
        {
            PageNumber = pageNumber;
            ExistingTextCharacters = existingTextCharacters;
            Selected = selected;
            NeedsOcr = needsOcr;
            AnnotationCount = annotationCount;
            SuggestedClockwiseRotationDegrees =
                suggestedClockwiseRotationDegrees;
            OrientationConfidence = orientationConfidence;
            SuggestedDeskewDegrees = suggestedDeskewDegrees;
            DeskewConfidence = deskewConfidence;
            Note = note ?? string.Empty;
        }

        public int PageNumber { get; private set; }

        public int ExistingTextCharacters { get; private set; }

        public bool HasSearchableText
        {
            get { return ExistingTextCharacters > 0; }
        }

        public bool Selected { get; private set; }

        public bool NeedsOcr { get; private set; }

        public int AnnotationCount { get; private set; }

        public int SuggestedClockwiseRotationDegrees
        {
            get;
            private set;
        }

        public float OrientationConfidence { get; private set; }

        /// <summary>
        /// Rotation in screen/image coordinates. A positive value is clockwise.
        /// </summary>
        public float SuggestedDeskewDegrees { get; private set; }

        public float DeskewConfidence { get; private set; }

        public bool WillDeskew
        {
            get
            {
                return Math.Abs(SuggestedDeskewDegrees) >= 0.35F;
            }
        }

        public string Note { get; private set; }
    }

    internal sealed class PdfOcrAnalysis
    {
        private readonly string sourceFingerprint;

        internal PdfOcrAnalysis(
            string sourcePath,
            string fingerprint,
            int pageCount,
            IList<PdfOcrPageAnalysis> pages,
            bool containsDigitalSignatures,
            bool containsXfa,
            PdfOcrAvailability engine)
        {
            SourcePath = sourcePath;
            sourceFingerprint = fingerprint;
            PageCount = pageCount;
            Pages = pages ?? new List<PdfOcrPageAnalysis>();
            ContainsDigitalSignatures = containsDigitalSignatures;
            ContainsXfa = containsXfa;
            Engine = engine;

            var ocrCount = 0;
            var orientationCount = 0;
            var deskewCount = 0;
            foreach (var page in Pages)
            {
                if (page.NeedsOcr)
                {
                    ocrCount++;
                }

                if (page.SuggestedClockwiseRotationDegrees != 0)
                {
                    orientationCount++;
                }

                if (page.WillDeskew)
                {
                    deskewCount++;
                }
            }

            OcrPageCount = ocrCount;
            OrientationCorrectionCount = orientationCount;
            DeskewCorrectionCount = deskewCount;
        }

        public string SourcePath { get; private set; }

        public int PageCount { get; private set; }

        public IList<PdfOcrPageAnalysis> Pages { get; private set; }

        public int OcrPageCount { get; private set; }

        public int OrientationCorrectionCount { get; private set; }

        public int DeskewCorrectionCount { get; private set; }

        public bool ContainsDigitalSignatures { get; private set; }

        public bool ContainsXfa { get; private set; }

        public PdfOcrAvailability Engine { get; private set; }

        internal string SourceFingerprint
        {
            get { return sourceFingerprint; }
        }
    }

    /// <summary>
    /// User-approved per-page plan. The UI can start with the analysis
    /// suggestions, then change rotation or deskew before Process is called.
    /// Angles use the visible page convention: positive values are clockwise.
    /// </summary>
    internal sealed class PdfOcrPageInstruction
    {
        public PdfOcrPageInstruction(
            int pageNumber,
            bool process,
            int clockwiseRotationDegrees,
            bool applyDeskew,
            float deskewDegrees)
        {
            PageNumber = pageNumber;
            Process = process;
            ClockwiseRotationDegrees = clockwiseRotationDegrees;
            ApplyDeskew = applyDeskew;
            DeskewDegrees = deskewDegrees;
        }

        public int PageNumber { get; private set; }

        public bool Process { get; set; }

        public int ClockwiseRotationDegrees { get; set; }

        public bool ApplyDeskew { get; set; }

        public float DeskewDegrees { get; set; }
    }

    internal sealed class PdfOcrProgress
    {
        public PdfOcrProgress(
            int completedSteps,
            int totalSteps,
            int processedPages,
            int totalPages,
            string stage)
        {
            CompletedSteps = completedSteps;
            TotalSteps = totalSteps;
            ProcessedPages = processedPages;
            TotalPages = totalPages;
            Stage = stage ?? string.Empty;
        }

        public int CompletedSteps { get; private set; }

        public int TotalSteps { get; private set; }

        public int ProcessedPages { get; private set; }

        public int TotalPages { get; private set; }

        public string Stage { get; private set; }

        public int Percentage
        {
            get
            {
                return TotalSteps <= 0
                    ? 0
                    : Math.Min(
                        100,
                        (int)Math.Round(
                            CompletedSteps * 100D / TotalSteps));
            }
        }
    }

    internal sealed class PdfOcrResult
    {
        public PdfOcrResult(
            string outputPath,
            int pageCount,
            int processedPageCount,
            int orientationCorrectionCount,
            int deskewCorrectionCount,
            int recognizedWordCount,
            bool digitalSignaturesInvalidated)
        {
            OutputPath = outputPath;
            PageCount = pageCount;
            ProcessedPageCount = processedPageCount;
            OrientationCorrectionCount = orientationCorrectionCount;
            DeskewCorrectionCount = deskewCorrectionCount;
            RecognizedWordCount = recognizedWordCount;
            DigitalSignaturesInvalidated =
                digitalSignaturesInvalidated;
        }

        public string OutputPath { get; private set; }

        public int PageCount { get; private set; }

        public int ProcessedPageCount { get; private set; }

        public int OrientationCorrectionCount { get; private set; }

        public int DeskewCorrectionCount { get; private set; }

        public int RecognizedWordCount { get; private set; }

        public bool DigitalSignaturesInvalidated { get; private set; }
    }

    /// <summary>
    /// Local OCR pipeline. PDFium renders at most one page at a time and
    /// Tesseract performs recognition in a separate process. Existing page
    /// streams remain vector/native: orientation changes the page rotation and
    /// deskew wraps existing content in a small transformation matrix.
    /// </summary>
    internal static class PdfOcrService
    {
        public const string DigitalSignatureInvalidationWarning =
            "La copia con OCR ya no conserva la validez criptografica de " +
            "las firmas digitales del documento original.";

        public const string XfaUnsupportedMessage =
            "Los formularios XFA no se pueden modificar con OCR de forma " +
            "segura. Guarda antes una copia PDF normal del formulario.";

        // Tesseract OSD is deliberately conservative; real office scans with
        // many readable lines commonly report 3-8 even when the direction is
        // unambiguous. Below 3 the service leaves the page untouched so it can
        // be corrected manually in the preview.
        private const float MinimumOrientationConfidence = 3F;
        private const float MinimumDeskewDegrees = 0.35F;
        private const float MaximumDeskewDegrees = 5F;
        private const float MinimumDeskewImprovementPercent = 2.25F;

        public static string SuggestOutputPath(string sourcePdfPath)
        {
            var source = NormalizeExistingPdfPath(sourcePdfPath);
            var directory = Path.GetDirectoryName(source);
            var baseName = Path.GetFileNameWithoutExtension(source);
            var candidate = Path.Combine(
                directory,
                baseName + "_ocr.pdf");
            var suffix = 2;
            while (File.Exists(candidate))
            {
                candidate = Path.Combine(
                    directory,
                    baseName + "_ocr_" +
                    suffix.ToString(CultureInfo.InvariantCulture) +
                    ".pdf");
                suffix++;
            }

            return candidate;
        }

        public static PdfOcrAvailability GetAvailability()
        {
            var executable = FindTesseractExecutable();
            if (string.IsNullOrWhiteSpace(executable))
            {
                return new PdfOcrAvailability(
                    false,
                    string.Empty,
                    new List<string>(),
                    "No se encuentra el motor OCR local Tesseract.");
            }

            try
            {
                var result = RunProcess(
                    executable,
                    "--list-langs",
                    CancellationToken.None,
                    false);
                if (result.ExitCode != 0)
                {
                    return new PdfOcrAvailability(
                        false,
                        executable,
                        new List<string>(),
                        "Tesseract esta instalado, pero no puede leer sus idiomas.");
                }

                var languages = ParseLanguageList(
                    result.StandardOutput + "\n" +
                    result.StandardError);
                return new PdfOcrAvailability(
                    languages.Count > 0,
                    executable,
                    languages,
                    languages.Count > 0
                        ? "OCR local disponible."
                        : "Tesseract no tiene idiomas OCR instalados.");
            }
            catch (Exception ex)
            {
                return new PdfOcrAvailability(
                    false,
                    executable,
                    new List<string>(),
                    "No se pudo iniciar Tesseract: " + ex.Message);
            }
        }

        public static PdfOcrAnalysis Analyze(
            string sourcePdfPath,
            PdfOcrSettings settings,
            Action<PdfOcrProgress> reportProgress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = NormalizeExistingPdfPath(sourcePdfPath);
            var effectiveSettings = (settings ?? new PdfOcrSettings())
                .Snapshot();
            var availability = GetAvailability();
            EnsureEngineSupportsSettings(
                availability,
                effectiveSettings);

            var fingerprint =
                PdfAtomicFileService.ComputeContentFingerprint(source);
            var pages = new List<PdfOcrPageAnalysis>();
            var selectedPages = new HashSet<int>();
            var candidates = new List<PageCandidate>();
            var pageCount = 0;
            var containsSignatures = false;
            var containsXfa = false;

            using (var reader = new PdfReader(
                source,
                (byte[])null,
                true))
            {
                pageCount = reader.NumberOfPages;
                if (pageCount < 1)
                {
                    throw new InvalidDataException(
                        "El PDF no contiene paginas.");
                }

                selectedPages = NormalizeSelectedPages(
                    effectiveSettings.SelectedPages,
                    pageCount);
                var fields = reader.AcroFields;
                containsSignatures =
                    fields != null &&
                    fields.GetSignatureNames().Count > 0;
                var acroForm =
                    reader.Catalog.GetAsDict(PdfName.ACROFORM);
                containsXfa =
                    acroForm != null &&
                    acroForm.Get(PdfName.XFA) != null;

                for (var page = 1; page <= pageCount; page++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var selected = selectedPages.Count == 0 ||
                        selectedPages.Contains(page);
                    var existingCharacters =
                        CountMeaningfulCharacters(
                            ExtractPageTextSafely(reader, page));
                    var hasUsableText =
                        existingCharacters >=
                        effectiveSettings
                            .MinimumExistingTextCharacters;
                    var needsOcr =
                        selected &&
                        (effectiveSettings.ReprocessPagesWithText ||
                         !hasUsableText);
                    var pageDictionary = reader.GetPageN(page);
                    var annotations = pageDictionary == null
                        ? null
                        : pageDictionary.GetAsArray(PdfName.ANNOTS);
                    var annotationCount = annotations == null
                        ? 0
                        : annotations.Size;

                    if (needsOcr)
                    {
                        candidates.Add(
                            new PageCandidate(
                                page,
                                existingCharacters,
                                annotationCount));
                    }
                    else
                    {
                        pages.Add(
                            new PdfOcrPageAnalysis(
                                page,
                                existingCharacters,
                                selected,
                                false,
                                annotationCount,
                                0,
                                0F,
                                0F,
                                0F,
                                selected
                                    ? "La pagina ya contiene texto buscable."
                                    : "Pagina fuera de la seleccion."));
                    }
                }
            }

            var analyzedCandidates =
                new Dictionary<int, PdfOcrPageAnalysis>();
            var totalSteps = Math.Max(1, candidates.Count);
            if (candidates.Count > 0)
            {
                using (var document = PdfDocumentOpenService.Load(source))
                {
                    for (var index = 0;
                        index < candidates.Count;
                        index++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var candidate = candidates[index];
                        Report(
                            reportProgress,
                            index,
                            totalSteps,
                            index,
                            candidates.Count,
                            "Analizando pagina " +
                            candidate.PageNumber.ToString(
                                CultureInfo.InvariantCulture));

                        int actualDpi;
                        using (var bitmap = RenderPage(
                            document,
                            candidate.PageNumber - 1,
                            effectiveSettings.AnalysisDpi,
                            Math.Min(
                                effectiveSettings.MaximumPixelsPerPage,
                                6000000),
                            out actualDpi))
                        {
                            var orientation = effectiveSettings.AutoOrient
                                ? DetectOrientation(
                                    availability.ExecutablePath,
                                    bitmap,
                                    actualDpi,
                                    cancellationToken)
                                : new OrientationResult(0, 0F);

                            var acceptedRotation =
                                orientation.Confidence >=
                                    MinimumOrientationConfidence
                                    ? NormalizeRightAngle(
                                        orientation
                                            .ClockwiseRotationDegrees)
                                    : 0;
                            if (acceptedRotation != 0)
                            {
                                RotateBitmapClockwise(
                                    bitmap,
                                    acceptedRotation);
                            }

                            var deskew = effectiveSettings.AutoDeskew
                                ? DetectDeskew(bitmap)
                                : new DeskewResult(0F, 0F);
                            var note = string.Empty;
                            var acceptedDeskew = 0F;
                            if (Math.Abs(deskew.CorrectionDegrees) >=
                                    MinimumDeskewDegrees &&
                                deskew.ConfidencePercent >=
                                    MinimumDeskewImprovementPercent)
                            {
                                if (candidate.AnnotationCount == 0)
                                {
                                    acceptedDeskew =
                                        deskew.CorrectionDegrees;
                                }
                                else
                                {
                                    note =
                                        "Se detecto inclinacion, pero no se " +
                                        "endereza automaticamente porque la " +
                                        "pagina contiene enlaces o campos.";
                                }
                            }

                            analyzedCandidates[candidate.PageNumber] =
                                new PdfOcrPageAnalysis(
                                    candidate.PageNumber,
                                    candidate.ExistingTextCharacters,
                                    true,
                                    true,
                                    candidate.AnnotationCount,
                                    acceptedRotation,
                                    orientation.Confidence,
                                    acceptedDeskew,
                                    deskew.ConfidencePercent,
                                    note);
                        }

                        Report(
                            reportProgress,
                            index + 1,
                            totalSteps,
                            index + 1,
                            candidates.Count,
                            "Pagina analizada");
                    }
                }
            }

            for (var page = 1; page <= pageCount; page++)
            {
                PdfOcrPageAnalysis analyzed;
                if (analyzedCandidates.TryGetValue(page, out analyzed))
                {
                    pages.Add(analyzed);
                }
            }

            pages.Sort(delegate(
                PdfOcrPageAnalysis left,
                PdfOcrPageAnalysis right)
            {
                return left.PageNumber.CompareTo(right.PageNumber);
            });

            var currentFingerprint =
                PdfAtomicFileService.ComputeContentFingerprint(source);
            if (!string.Equals(
                    fingerprint,
                    currentFingerprint,
                    StringComparison.Ordinal))
            {
                throw new IOException(
                    "El PDF cambio mientras se analizaba. Vuelve a intentarlo.");
            }

            return new PdfOcrAnalysis(
                source,
                fingerprint,
                pageCount,
                pages,
                containsSignatures,
                containsXfa,
                availability);
        }

        public static IList<PdfOcrPageInstruction>
            CreateDefaultInstructions(PdfOcrAnalysis analysis)
        {
            if (analysis == null)
            {
                throw new ArgumentNullException("analysis");
            }

            var instructions =
                new List<PdfOcrPageInstruction>();
            foreach (var page in analysis.Pages)
            {
                instructions.Add(
                    new PdfOcrPageInstruction(
                        page.PageNumber,
                        page.NeedsOcr,
                        page.SuggestedClockwiseRotationDegrees,
                        page.WillDeskew,
                        page.SuggestedDeskewDegrees));
            }

            return instructions;
        }

        public static byte[] RenderPreviewPng(
            string sourcePdfPath,
            PdfOcrPageInstruction instruction,
            int dpi,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = NormalizeExistingPdfPath(sourcePdfPath);
            if (instruction == null)
            {
                throw new ArgumentNullException("instruction");
            }

            ValidateInstructionAngles(instruction);
            using (var document = PdfDocumentOpenService.Load(source))
            {
                if (instruction.PageNumber < 1 ||
                    instruction.PageNumber > document.PageCount)
                {
                    throw new ArgumentOutOfRangeException(
                        "instruction",
                        "La pagina de vista previa no existe.");
                }

                int actualDpi;
                using (var bitmap = RenderPage(
                    document,
                    instruction.PageNumber - 1,
                    Math.Max(72, Math.Min(180, dpi)),
                    8000000,
                    out actualDpi))
                {
                    RotateBitmapClockwise(
                        bitmap,
                        instruction.ClockwiseRotationDegrees);
                    using (var corrected =
                        instruction.ApplyDeskew &&
                        Math.Abs(instruction.DeskewDegrees) >=
                            MinimumDeskewDegrees
                            ? RotateBitmapOnFixedCanvas(
                                bitmap,
                                instruction.DeskewDegrees)
                            : new Bitmap(bitmap))
                    using (var output = new MemoryStream())
                    {
                        corrected.Save(output, ImageFormat.Png);
                        return output.ToArray();
                    }
                }
            }
        }

        public static PdfOcrResult Process(
            string sourcePdfPath,
            string outputPath,
            PdfOcrAnalysis analysis,
            PdfOcrSettings settings,
            Action<PdfOcrProgress> reportProgress,
            CancellationToken cancellationToken)
        {
            var instructions = analysis == null
                ? null
                : CreateDefaultInstructions(analysis);
            return Process(
                sourcePdfPath,
                outputPath,
                analysis,
                instructions,
                settings,
                reportProgress,
                cancellationToken);
        }

        public static PdfOcrResult Process(
            string sourcePdfPath,
            string outputPath,
            PdfOcrAnalysis analysis,
            IList<PdfOcrPageInstruction> instructions,
            PdfOcrSettings settings,
            Action<PdfOcrProgress> reportProgress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = NormalizeExistingPdfPath(sourcePdfPath);
            var target = ValidateOutputPath(source, outputPath);
            var effectiveSettings = (settings ?? new PdfOcrSettings())
                .Snapshot();
            if (analysis == null)
            {
                analysis = Analyze(
                    source,
                    effectiveSettings,
                    reportProgress,
                    cancellationToken);
                if (instructions == null)
                {
                    instructions =
                        CreateDefaultInstructions(analysis);
                }
            }

            ValidateAnalysis(source, analysis);
            EnsureEngineSupportsSettings(
                analysis.Engine,
                effectiveSettings);
            if (analysis.ContainsXfa)
            {
                throw new NotSupportedException(
                    XfaUnsupportedMessage);
            }

            var instructionMap = NormalizeInstructions(
                analysis,
                instructions);
            var pagesToProcess = new List<PageWorkPlan>();
            foreach (var page in analysis.Pages)
            {
                PdfOcrPageInstruction instruction;
                if (instructionMap.TryGetValue(
                        page.PageNumber,
                        out instruction) &&
                    instruction.Process)
                {
                    pagesToProcess.Add(
                        new PageWorkPlan(page, instruction));
                }
            }

            EnsureFreeSpace(source, Path.GetDirectoryName(target));
            var workingDirectory = CreateWorkingDirectory();
            var outputTemporaryPath = Path.Combine(
                Path.GetDirectoryName(target),
                "." + Path.GetFileNameWithoutExtension(target) +
                "." + Guid.NewGuid().ToString("N") + ".ocr.tmp");
            var currentPdfPath = source;
            var recognizedWords = 0;
            var pageData = new List<OcrPageData>();
            try
            {
                if (pagesToProcess.Count == 0)
                {
                    PdfAtomicFileService.SaveCopy(source, target);
                    return new PdfOcrResult(
                        target,
                        analysis.PageCount,
                        0,
                        0,
                        0,
                        0,
                        false);
                }

                Report(
                    reportProgress,
                    0,
                    pagesToProcess.Count + 3,
                    0,
                    pagesToProcess.Count,
                    "Preparando copia OCR");

                var orientedPath = ApplyOrientationCorrections(
                    source,
                    analysis,
                    instructionMap,
                    workingDirectory,
                    cancellationToken);
                if (!string.Equals(
                        orientedPath,
                        source,
                        StringComparison.OrdinalIgnoreCase))
                {
                    currentPdfPath = orientedPath;
                }

                var deskewedPath = ApplyDeskewCorrections(
                    currentPdfPath,
                    analysis,
                    instructionMap,
                    workingDirectory,
                    cancellationToken);
                if (!string.Equals(
                        deskewedPath,
                        currentPdfPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    currentPdfPath = deskewedPath;
                }

                using (var document = PdfDocumentOpenService.Load(currentPdfPath))
                {
                    for (var index = 0;
                        index < pagesToProcess.Count;
                        index++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var page = pagesToProcess[index];
                        Report(
                            reportProgress,
                            index + 1,
                            pagesToProcess.Count + 3,
                            index,
                            pagesToProcess.Count,
                            "Reconociendo pagina " +
                            page.Analysis.PageNumber.ToString(
                                CultureInfo.InvariantCulture));

                        int actualDpi;
                        var imagePath = Path.Combine(
                            workingDirectory,
                            "page-" +
                            page.Analysis.PageNumber.ToString(
                                "D6",
                                CultureInfo.InvariantCulture) +
                            ".png");
                        var tsvBase = Path.Combine(
                            workingDirectory,
                            "page-" +
                            page.Analysis.PageNumber.ToString(
                                "D6",
                                CultureInfo.InvariantCulture));
                        var tsvPath = tsvBase + ".tsv";
                        using (var bitmap = RenderPage(
                            document,
                            page.Analysis.PageNumber - 1,
                            effectiveSettings.OcrDpi,
                            effectiveSettings.MaximumPixelsPerPage,
                            out actualDpi))
                        {
                            SavePngDurably(bitmap, imagePath);
                            RunTesseractTsv(
                                analysis.Engine.ExecutablePath,
                                imagePath,
                                tsvBase,
                                effectiveSettings.Language,
                                actualDpi,
                                cancellationToken);
                            var words = ParseTsv(tsvPath);
                            recognizedWords += words.Count;
                            pageData.Add(
                                new OcrPageData(
                                    page.Analysis.PageNumber,
                                    bitmap.Width,
                                    bitmap.Height,
                                    tsvPath,
                                    words.Count));
                        }

                        TryDeleteFile(imagePath);
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                Report(
                    reportProgress,
                    pagesToProcess.Count + 1,
                    pagesToProcess.Count + 3,
                    pagesToProcess.Count,
                    pagesToProcess.Count,
                    "Escribiendo capa de texto");
                WriteTextLayer(
                    currentPdfPath,
                    outputTemporaryPath,
                    pageData,
                    cancellationToken);

                Report(
                    reportProgress,
                    pagesToProcess.Count + 2,
                    pagesToProcess.Count + 3,
                    pagesToProcess.Count,
                    pagesToProcess.Count,
                    "Comprobando copia OCR");
                ValidateWrittenPdf(
                    outputTemporaryPath,
                    analysis,
                    pageData);
                cancellationToken.ThrowIfCancellationRequested();
                EnsureSourceUnchanged(source, analysis.SourceFingerprint);

                File.Move(outputTemporaryPath, target);
                Report(
                    reportProgress,
                    pagesToProcess.Count + 3,
                    pagesToProcess.Count + 3,
                    pagesToProcess.Count,
                    pagesToProcess.Count,
                    "OCR terminado");
                return new PdfOcrResult(
                    target,
                    analysis.PageCount,
                    pagesToProcess.Count,
                    CountOrientationCorrections(
                        instructionMap),
                    CountDeskewCorrections(
                        instructionMap),
                    recognizedWords,
                    analysis.ContainsDigitalSignatures);
            }
            finally
            {
                TryDeleteFile(outputTemporaryPath);
                CleanupWorkingDirectory(workingDirectory);
            }
        }

        private static void ValidateAnalysis(
            string sourcePath,
            PdfOcrAnalysis analysis)
        {
            if (analysis == null ||
                !string.Equals(
                    sourcePath,
                    analysis.SourcePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "El analisis OCR no corresponde al PDF actual.");
            }

            EnsureSourceUnchanged(
                sourcePath,
                analysis.SourceFingerprint);
        }

        private static IDictionary<int, PdfOcrPageInstruction>
            NormalizeInstructions(
                PdfOcrAnalysis analysis,
                IList<PdfOcrPageInstruction> instructions)
        {
            if (instructions == null)
            {
                instructions = CreateDefaultInstructions(analysis);
            }

            var result =
                new Dictionary<int, PdfOcrPageInstruction>();
            foreach (var instruction in instructions)
            {
                if (instruction == null)
                {
                    throw new ArgumentException(
                        "El plan OCR contiene una instruccion vacia.",
                        "instructions");
                }

                if (instruction.PageNumber < 1 ||
                    instruction.PageNumber > analysis.PageCount ||
                    result.ContainsKey(instruction.PageNumber))
                {
                    throw new ArgumentException(
                        "El plan OCR contiene una pagina repetida o inexistente.",
                        "instructions");
                }

                ValidateInstructionAngles(instruction);
                PdfOcrPageAnalysis analyzedPage = null;
                foreach (var page in analysis.Pages)
                {
                    if (page.PageNumber == instruction.PageNumber)
                    {
                        analyzedPage = page;
                        break;
                    }
                }

                if (instruction.Process &&
                    analyzedPage != null &&
                    instruction.ApplyDeskew &&
                    analyzedPage.AnnotationCount > 0)
                {
                    throw new NotSupportedException(
                        "No se puede enderezar automaticamente la pagina " +
                        instruction.PageNumber.ToString(
                            CultureInfo.InvariantCulture) +
                        " porque contiene enlaces o campos.");
                }

                result[instruction.PageNumber] =
                    new PdfOcrPageInstruction(
                        instruction.PageNumber,
                        instruction.Process,
                        NormalizeRightAngle(
                            instruction.ClockwiseRotationDegrees),
                        instruction.ApplyDeskew,
                        instruction.DeskewDegrees);
            }

            foreach (var page in analysis.Pages)
            {
                if (!result.ContainsKey(page.PageNumber))
                {
                    result[page.PageNumber] =
                        new PdfOcrPageInstruction(
                            page.PageNumber,
                            false,
                            0,
                            false,
                            0F);
                }
            }

            return result;
        }

        private static void ValidateInstructionAngles(
            PdfOcrPageInstruction instruction)
        {
            if (instruction.ClockwiseRotationDegrees % 90 != 0)
            {
                throw new ArgumentOutOfRangeException(
                    "instruction",
                    "La orientacion manual debe ser un multiplo de 90 grados.");
            }

            if (float.IsNaN(instruction.DeskewDegrees) ||
                float.IsInfinity(instruction.DeskewDegrees) ||
                Math.Abs(instruction.DeskewDegrees) >
                    MaximumDeskewDegrees)
            {
                throw new ArgumentOutOfRangeException(
                    "instruction",
                    "El enderezado debe estar entre -5 y 5 grados.");
            }
        }

        private static int CountOrientationCorrections(
            IDictionary<int, PdfOcrPageInstruction> instructions)
        {
            var count = 0;
            foreach (var entry in instructions)
            {
                if (entry.Value.Process &&
                    NormalizeRightAngle(
                        entry.Value.ClockwiseRotationDegrees) != 0)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountDeskewCorrections(
            IDictionary<int, PdfOcrPageInstruction> instructions)
        {
            var count = 0;
            foreach (var entry in instructions)
            {
                if (entry.Value.Process &&
                    entry.Value.ApplyDeskew &&
                    Math.Abs(entry.Value.DeskewDegrees) >=
                        MinimumDeskewDegrees)
                {
                    count++;
                }
            }

            return count;
        }

        private static void EnsureSourceUnchanged(
            string sourcePath,
            string expectedFingerprint)
        {
            var currentFingerprint =
                PdfAtomicFileService.ComputeContentFingerprint(sourcePath);
            if (!string.Equals(
                    currentFingerprint,
                    expectedFingerprint,
                    StringComparison.Ordinal))
            {
                throw new IOException(
                    "El PDF original cambio despues del analisis OCR.");
            }
        }

        private static void EnsureEngineSupportsSettings(
            PdfOcrAvailability availability,
            PdfOcrSettings settings)
        {
            if (availability == null || !availability.IsAvailable)
            {
                throw new NotSupportedException(
                    availability == null
                        ? "No se encuentra el motor OCR local."
                        : availability.Message);
            }

            var requestedLanguages = settings.Language.Split(
                new[] { '+' },
                StringSplitOptions.RemoveEmptyEntries);
            foreach (var requested in requestedLanguages)
            {
                var language = requested.Trim();
                if (!ContainsIgnoreCase(
                        availability.AvailableLanguages,
                        language))
                {
                    throw new NotSupportedException(
                        "Falta el idioma OCR \"" + language +
                        "\" en Tesseract.");
                }
            }

            if (settings.AutoOrient &&
                !ContainsIgnoreCase(
                    availability.AvailableLanguages,
                    "osd"))
            {
                throw new NotSupportedException(
                    "Falta el modelo local osd para detectar la orientacion.");
            }
        }

        private static bool ContainsIgnoreCase(
            IList<string> values,
            string value)
        {
            if (values == null)
            {
                return false;
            }

            foreach (var item in values)
            {
                if (string.Equals(
                        item,
                        value,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static HashSet<int> NormalizeSelectedPages(
            ICollection<int> selectedPages,
            int pageCount)
        {
            var result = new HashSet<int>();
            if (selectedPages == null)
            {
                return result;
            }

            foreach (var page in selectedPages)
            {
                if (page < 1 || page > pageCount)
                {
                    throw new ArgumentOutOfRangeException(
                        "selectedPages",
                        "La seleccion OCR contiene una pagina que no existe.");
                }

                result.Add(page);
            }

            return result;
        }

        private static string ExtractPageTextSafely(
            PdfReader reader,
            int pageNumber)
        {
            try
            {
                return PdfTextExtractor.GetTextFromPage(
                    reader,
                    pageNumber,
                    new LocationTextExtractionStrategy()) ??
                    string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static int CountMeaningfulCharacters(string value)
        {
            var count = 0;
            foreach (var character in value ?? string.Empty)
            {
                if (!char.IsWhiteSpace(character) &&
                    !char.IsControl(character))
                {
                    count++;
                }
            }

            return count;
        }

        private static Bitmap RotateBitmapOnFixedCanvas(
            Bitmap source,
            float clockwiseDegrees)
        {
            var result = new Bitmap(
                source.Width,
                source.Height,
                PixelFormat.Format24bppRgb);
            result.SetResolution(
                source.HorizontalResolution,
                source.VerticalResolution);
            using (var graphics = Graphics.FromImage(result))
            {
                graphics.Clear(Color.White);
                graphics.InterpolationMode =
                    InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode =
                    PixelOffsetMode.HighQuality;
                graphics.TranslateTransform(
                    source.Width / 2F,
                    source.Height / 2F);
                graphics.RotateTransform(clockwiseDegrees);
                graphics.TranslateTransform(
                    -source.Width / 2F,
                    -source.Height / 2F);
                graphics.DrawImageUnscaled(source, 0, 0);
            }

            return result;
        }

        private static Bitmap RenderPage(
            PdfiumDocument document,
            int pageIndex,
            int requestedDpi,
            int maximumPixels,
            out int actualDpi)
        {
            if (document == null ||
                pageIndex < 0 ||
                pageIndex >= document.PageCount)
            {
                throw new ArgumentOutOfRangeException("pageIndex");
            }

            var pageSize = document.PageSizes[pageIndex];
            var width = Math.Max(
                1,
                (int)Math.Ceiling(
                    pageSize.Width * requestedDpi / 72D));
            var height = Math.Max(
                1,
                (int)Math.Ceiling(
                    pageSize.Height * requestedDpi / 72D));
            var pixels = (long)width * height;
            actualDpi = requestedDpi;
            if (pixels > maximumPixels)
            {
                var scale = Math.Sqrt(
                    maximumPixels / (double)pixels);
                actualDpi = Math.Max(
                    72,
                    (int)Math.Floor(requestedDpi * scale));
                width = Math.Max(
                    1,
                    (int)Math.Ceiling(
                        pageSize.Width * actualDpi / 72D));
                height = Math.Max(
                    1,
                    (int)Math.Ceiling(
                        pageSize.Height * actualDpi / 72D));
            }

            using (var rendered = document.Render(
                pageIndex,
                width,
                height,
                actualDpi,
                actualDpi,
                PdfRenderFlags.Annotations |
                PdfRenderFlags.LcdText |
                PdfRenderFlags.LimitImageCacheSize))
            {
                var bitmap = new Bitmap(
                    width,
                    height,
                    PixelFormat.Format24bppRgb);
                bitmap.SetResolution(actualDpi, actualDpi);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.White);
                    graphics.CompositingMode =
                        CompositingMode.SourceCopy;
                    graphics.DrawImage(
                        rendered,
                        new System.Drawing.Rectangle(
                            0,
                            0,
                            width,
                            height));
                }

                return bitmap;
            }
        }

        private static OrientationResult DetectOrientation(
            string tesseractPath,
            Bitmap bitmap,
            int dpi,
            CancellationToken cancellationToken)
        {
            var temporaryPath = Path.Combine(
                Path.GetTempPath(),
                "pdf-ligero-osd-" +
                Guid.NewGuid().ToString("N") +
                ".png");
            try
            {
                SavePngDurably(bitmap, temporaryPath);
                var arguments =
                    Quote(temporaryPath) +
                    " stdout --dpi " +
                    dpi.ToString(CultureInfo.InvariantCulture) +
                    " --psm 0 -l osd";
                var result = RunProcess(
                    tesseractPath,
                    arguments,
                    cancellationToken,
                    false);
                var output =
                    result.StandardOutput + "\n" +
                    result.StandardError;
                var rotation = ParseOsdInt(output, "Rotate");
                var confidence = ParseOsdFloat(
                    output,
                    "Orientation confidence");
                if (result.ExitCode != 0 ||
                    (rotation != 0 &&
                     rotation != 90 &&
                     rotation != 180 &&
                     rotation != 270))
                {
                    return new OrientationResult(0, 0F);
                }

                return new OrientationResult(
                    rotation,
                    confidence);
            }
            finally
            {
                TryDeleteFile(temporaryPath);
            }
        }

        private static int ParseOsdInt(
            string output,
            string fieldName)
        {
            int parsed;
            return int.TryParse(
                FindOsdValue(output, fieldName),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out parsed)
                ? parsed
                : 0;
        }

        private static float ParseOsdFloat(
            string output,
            string fieldName)
        {
            float parsed;
            return float.TryParse(
                FindOsdValue(output, fieldName),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out parsed)
                ? parsed
                : 0F;
        }

        private static string FindOsdValue(
            string output,
            string fieldName)
        {
            var lines = (output ?? string.Empty).Split(
                new[] { "\r\n", "\n" },
                StringSplitOptions.RemoveEmptyEntries);
            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (!line.StartsWith(
                        fieldName + ":",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return line.Substring(line.IndexOf(':') + 1).Trim();
            }

            return string.Empty;
        }

        private static void RotateBitmapClockwise(
            Bitmap bitmap,
            int clockwiseDegrees)
        {
            switch (NormalizeRightAngle(clockwiseDegrees))
            {
                case 90:
                    bitmap.RotateFlip(
                        RotateFlipType.Rotate90FlipNone);
                    break;
                case 180:
                    bitmap.RotateFlip(
                        RotateFlipType.Rotate180FlipNone);
                    break;
                case 270:
                    bitmap.RotateFlip(
                        RotateFlipType.Rotate270FlipNone);
                    break;
            }
        }

        private static int NormalizeRightAngle(int degrees)
        {
            var normalized = degrees % 360;
            if (normalized < 0)
            {
                normalized += 360;
            }

            return normalized;
        }

        private static DeskewResult DetectDeskew(Bitmap source)
        {
            const int maximumDimension = 1000;
            var scale = Math.Min(
                1D,
                maximumDimension /
                (double)Math.Max(source.Width, source.Height));
            var width = Math.Max(
                1,
                (int)Math.Round(source.Width * scale));
            var height = Math.Max(
                1,
                (int)Math.Round(source.Height * scale));
            using (var bitmap = new Bitmap(
                width,
                height,
                PixelFormat.Format24bppRgb))
            {
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.White);
                    graphics.InterpolationMode =
                        InterpolationMode.HighQualityBilinear;
                    graphics.DrawImage(
                        source,
                        new System.Drawing.Rectangle(
                            0,
                            0,
                            width,
                            height));
                }

                var grayscale = ReadGrayscale(bitmap);
                var threshold = GetOtsuThreshold(grayscale);
                var points = CollectForegroundPoints(
                    grayscale,
                    width,
                    height,
                    threshold);
                if (points.Count < 250)
                {
                    return new DeskewResult(0F, 0F);
                }

                var zeroScore = ProjectionScore(
                    points,
                    width,
                    height,
                    0F);
                var bestScore = zeroScore;
                var bestSkew = 0F;
                for (var angle = -MaximumDeskewDegrees;
                    angle <= MaximumDeskewDegrees + 0.01F;
                    angle += 0.25F)
                {
                    if (Math.Abs(angle) < 0.01F)
                    {
                        continue;
                    }

                    var score = ProjectionScore(
                        points,
                        width,
                        height,
                        angle);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestSkew = angle;
                    }
                }

                var confidence = zeroScore <= 0D
                    ? 0F
                    : (float)(
                        (bestScore - zeroScore) *
                        100D /
                        zeroScore);
                if (Math.Abs(bestSkew) < MinimumDeskewDegrees ||
                    confidence < MinimumDeskewImprovementPercent)
                {
                    return new DeskewResult(0F, confidence);
                }

                // Projection removes a measured screen-coordinate slope. The
                // bitmap correction therefore uses the opposite angle.
                return new DeskewResult(-bestSkew, confidence);
            }
        }

        private static byte[] ReadGrayscale(Bitmap bitmap)
        {
            var rectangle = new System.Drawing.Rectangle(
                0,
                0,
                bitmap.Width,
                bitmap.Height);
            var data = bitmap.LockBits(
                rectangle,
                ImageLockMode.ReadOnly,
                PixelFormat.Format24bppRgb);
            try
            {
                var raw = new byte[Math.Abs(data.Stride) * bitmap.Height];
                Marshal.Copy(data.Scan0, raw, 0, raw.Length);
                var grayscale =
                    new byte[bitmap.Width * bitmap.Height];
                for (var y = 0; y < bitmap.Height; y++)
                {
                    var rowOffset = y * data.Stride;
                    for (var x = 0; x < bitmap.Width; x++)
                    {
                        var offset = rowOffset + x * 3;
                        var blue = raw[offset];
                        var green = raw[offset + 1];
                        var red = raw[offset + 2];
                        grayscale[y * bitmap.Width + x] =
                            (byte)(
                                (red * 77 +
                                 green * 150 +
                                 blue * 29) >> 8);
                    }
                }

                return grayscale;
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        private static byte GetOtsuThreshold(byte[] grayscale)
        {
            var histogram = new long[256];
            foreach (var value in grayscale)
            {
                histogram[value]++;
            }

            long total = grayscale.Length;
            double totalWeighted = 0D;
            for (var index = 0; index < histogram.Length; index++)
            {
                totalWeighted += index * histogram[index];
            }

            long backgroundCount = 0;
            double backgroundWeighted = 0D;
            double maximumVariance = -1D;
            var threshold = 180;
            for (var index = 0; index < 256; index++)
            {
                backgroundCount += histogram[index];
                if (backgroundCount == 0)
                {
                    continue;
                }

                var foregroundCount = total - backgroundCount;
                if (foregroundCount == 0)
                {
                    break;
                }

                backgroundWeighted += index * histogram[index];
                var backgroundMean =
                    backgroundWeighted / backgroundCount;
                var foregroundMean =
                    (totalWeighted - backgroundWeighted) /
                    foregroundCount;
                var difference =
                    backgroundMean - foregroundMean;
                var variance =
                    backgroundCount *
                    (double)foregroundCount *
                    difference *
                    difference;
                if (variance > maximumVariance)
                {
                    maximumVariance = variance;
                    threshold = index;
                }
            }

            return (byte)Math.Max(80, Math.Min(235, threshold));
        }

        private static IList<IntPoint> CollectForegroundPoints(
            byte[] grayscale,
            int width,
            int height,
            byte threshold)
        {
            var points = new List<IntPoint>();
            var marginX = Math.Max(2, width / 50);
            var marginY = Math.Max(2, height / 50);
            var sampleStep = Math.Max(
                1,
                (int)Math.Sqrt(
                    width * (double)height / 250000D));
            for (var y = marginY;
                y < height - marginY;
                y += sampleStep)
            {
                for (var x = marginX;
                    x < width - marginX;
                    x += sampleStep)
                {
                    if (grayscale[y * width + x] <
                        threshold)
                    {
                        points.Add(new IntPoint(x, y));
                    }
                }
            }

            return points;
        }

        private static double ProjectionScore(
            IList<IntPoint> points,
            int width,
            int height,
            float measuredSkewDegrees)
        {
            var margin = (int)Math.Ceiling(
                width *
                Math.Tan(
                    MaximumDeskewDegrees *
                    Math.PI /
                    180D)) + 3;
            var bins = new int[height + margin * 2];
            var tangent = Math.Tan(
                measuredSkewDegrees *
                Math.PI /
                180D);
            var centerX = width / 2D;
            foreach (var point in points)
            {
                var projectedY = (int)Math.Round(
                    point.Y -
                    tangent * (point.X - centerX)) + margin;
                if (projectedY >= 0 &&
                    projectedY < bins.Length)
                {
                    bins[projectedY]++;
                }
            }

            double score = 0D;
            foreach (var count in bins)
            {
                score += count * (double)count;
            }

            return score;
        }

        private static string ApplyOrientationCorrections(
            string sourcePath,
            PdfOcrAnalysis analysis,
            IDictionary<int, PdfOcrPageInstruction> instructions,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            if (CountOrientationCorrections(instructions) == 0)
            {
                return sourcePath;
            }

            var pages = new List<PdfPageOrganizerPage>();
            for (var pageNumber = 1;
                pageNumber <= analysis.PageCount;
                pageNumber++)
            {
                PdfOcrPageInstruction instruction;
                var rotation =
                    instructions.TryGetValue(
                        pageNumber,
                        out instruction) &&
                    instruction.Process
                        ? instruction.ClockwiseRotationDegrees
                        : 0;
                pages.Add(
                    new PdfPageOrganizerPage(
                        pageNumber,
                        rotation));
            }

            var outputPath = Path.Combine(
                workingDirectory,
                "oriented.pdf");
            PdfPageOrganizerService.Organize(
                sourcePath,
                pages,
                outputPath,
                null,
                cancellationToken);
            return outputPath;
        }

        private static string ApplyDeskewCorrections(
            string sourcePath,
            PdfOcrAnalysis analysis,
            IDictionary<int, PdfOcrPageInstruction> instructions,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            if (CountDeskewCorrections(instructions) == 0)
            {
                return sourcePath;
            }

            var outputPath = Path.Combine(
                workingDirectory,
                "deskewed.pdf");
            PdfReader reader = null;
            FileStream output = null;
            PdfStamper stamper = null;
            try
            {
                reader = new PdfReader(
                    sourcePath,
                    (byte[])null,
                    true);
                output = new FileStream(
                    outputPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    1024 * 1024,
                    FileOptions.SequentialScan |
                    FileOptions.WriteThrough);
                stamper = new PdfStamper(reader, output);
                stamper.Writer.CloseStream = false;
                for (var pageNumber = 1;
                    pageNumber <= analysis.PageCount;
                    pageNumber++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    PdfOcrPageInstruction instruction;
                    if (!instructions.TryGetValue(
                            pageNumber,
                            out instruction) ||
                        !instruction.Process ||
                        !instruction.ApplyDeskew ||
                        Math.Abs(instruction.DeskewDegrees) <
                            MinimumDeskewDegrees)
                    {
                        continue;
                    }

                    WrapPageContentsWithRotation(
                        reader,
                        stamper,
                        pageNumber,
                        instruction.DeskewDegrees);
                }

                stamper.Close();
                stamper = null;
                reader = null;
                output.Flush(true);
                output.Dispose();
                output = null;
                return outputPath;
            }
            catch
            {
                TryDeleteFile(outputPath);
                throw;
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

        private static void WrapPageContentsWithRotation(
            PdfReader reader,
            PdfStamper stamper,
            int pageNumber,
            float visibleClockwiseDegrees)
        {
            var pageDictionary = reader.GetPageN(pageNumber);
            var rawContents = pageDictionary.Get(PdfName.CONTENTS);
            if (rawContents == null)
            {
                return;
            }

            var crop = reader.GetCropBox(pageNumber) ??
                reader.GetPageSize(pageNumber);
            var centerX = (crop.Left + crop.Right) / 2D;
            var centerY = (crop.Bottom + crop.Top) / 2D;

            // PDF coordinates use an upward Y axis. The visible clockwise
            // correction therefore has the opposite matrix angle.
            var pdfDegrees = -visibleClockwiseDegrees;
            var radians = pdfDegrees * Math.PI / 180D;
            var cosine = Math.Cos(radians);
            var sine = Math.Sin(radians);
            var translateX =
                centerX -
                cosine * centerX +
                sine * centerY;
            var translateY =
                centerY -
                sine * centerX -
                cosine * centerY;
            var prefixText =
                "q " +
                PdfNumber(cosine) + " " +
                PdfNumber(sine) + " " +
                PdfNumber(-sine) + " " +
                PdfNumber(cosine) + " " +
                PdfNumber(translateX) + " " +
                PdfNumber(translateY) + " cm\n";
            var prefix = new PdfStream(
                Encoding.ASCII.GetBytes(prefixText));
            prefix.FlateCompress();
            var suffix = new PdfStream(
                Encoding.ASCII.GetBytes("\nQ\n"));
            suffix.FlateCompress();
            var prefixReference =
                stamper.Writer.AddToBody(prefix).IndirectReference;
            var suffixReference =
                stamper.Writer.AddToBody(suffix).IndirectReference;

            var newContents = new PdfArray();
            newContents.Add(prefixReference);
            var resolvedContents =
                PdfReader.GetPdfObject(rawContents);
            var contentArray = resolvedContents as PdfArray;
            if (contentArray != null)
            {
                for (var index = 0;
                    index < contentArray.Size;
                    index++)
                {
                    newContents.Add(contentArray[index]);
                }
            }
            else
            {
                newContents.Add(rawContents);
            }

            newContents.Add(suffixReference);
            pageDictionary.Put(PdfName.CONTENTS, newContents);
        }

        private static string PdfNumber(double value)
        {
            if (Math.Abs(value) < 0.0000001D)
            {
                value = 0D;
            }

            return value.ToString(
                "0.########",
                CultureInfo.InvariantCulture);
        }

        private static void RunTesseractTsv(
            string tesseractPath,
            string imagePath,
            string outputBase,
            string language,
            int dpi,
            CancellationToken cancellationToken)
        {
            var arguments =
                Quote(imagePath) + " " +
                Quote(outputBase) +
                " -l " + Quote(language) +
                " --dpi " +
                dpi.ToString(CultureInfo.InvariantCulture) +
                // psm 4: una sola columna de texto con tamanos variables.
                //
                // Antes se usaba psm 3, que ademas intenta detectar columnas. En
                // documentos a una columna con titulos y parrafos —memorias,
                // contratos, informes— se equivocaba y partia la pagina en
                // columnas inventadas, dejando el texto troceado en vertical en
                // vez de seguido y bien estructurado.
                //
                // psm 4 respeta los cambios de cuerpo entre titulo y parrafo,
                // que es lo que distingue a estos documentos, sin buscar
                // columnas donde no las hay.
                " --psm 4 tsv";
            RunProcess(
                tesseractPath,
                arguments,
                cancellationToken,
                true);
            var tsvPath = outputBase + ".tsv";
            if (!File.Exists(tsvPath))
            {
                throw new InvalidDataException(
                    "Tesseract no genero los datos de texto esperados.");
            }
        }

        private static IList<OcrWord> ParseTsv(string path)
        {
            var words = new List<OcrWord>();
            if (!File.Exists(path))
            {
                return words;
            }

            using (var reader = new StreamReader(
                path,
                Encoding.UTF8,
                true))
            {
                string line;
                var first = true;
                while ((line = reader.ReadLine()) != null)
                {
                    if (first)
                    {
                        first = false;
                        if (line.StartsWith(
                                "level\t",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }

                    var fields = line.Split(
                        new[] { '\t' },
                        12);
                    if (fields.Length < 12 ||
                        fields[0] != "5")
                    {
                        continue;
                    }

                    int left;
                    int top;
                    int width;
                    int height;
                    float confidence;
                    if (!int.TryParse(
                            fields[6],
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out left) ||
                        !int.TryParse(
                            fields[7],
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out top) ||
                        !int.TryParse(
                            fields[8],
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out width) ||
                        !int.TryParse(
                            fields[9],
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out height) ||
                        !float.TryParse(
                            fields[10],
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out confidence))
                    {
                        continue;
                    }

                    var text = NormalizeOcrText(fields[11]);
                    if (width <= 0 ||
                        height <= 0 ||
                        confidence < 0F ||
                        text.Length == 0)
                    {
                        continue;
                    }

                    words.Add(
                        new OcrWord(
                            left,
                            top,
                            width,
                            height,
                            confidence,
                            text));
                }
            }

            return words;
        }

        private static string NormalizeOcrText(string text)
        {
            var builder = new StringBuilder();
            foreach (var character in text ?? string.Empty)
            {
                if (!char.IsControl(character) &&
                    !char.IsSurrogate(character))
                {
                    builder.Append(character);
                }
            }

            return builder.ToString().Trim();
        }

        private static void WriteTextLayer(
            string sourcePath,
            string outputPath,
            IList<OcrPageData> pages,
            CancellationToken cancellationToken)
        {
            PdfReader reader = null;
            PdfStamper stamper = null;
            FileStream output = null;
            try
            {
                reader = new PdfReader(
                    sourcePath,
                    (byte[])null,
                    true);
                output = new FileStream(
                    outputPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    1024 * 1024,
                    FileOptions.SequentialScan |
                    FileOptions.WriteThrough);
                stamper = new PdfStamper(reader, output);
                stamper.Writer.CloseStream = false;
                var font = CreateOcrFont();
                foreach (var page in pages)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var words = ParseTsv(page.TsvPath);
                    if (words.Count == 0)
                    {
                        continue;
                    }

                    var pageSize =
                        reader.GetPageSizeWithRotation(
                            page.PageNumber);
                    var scaleX =
                        pageSize.Width / page.RenderWidth;
                    var scaleY =
                        pageSize.Height / page.RenderHeight;
                    var content =
                        stamper.GetOverContent(page.PageNumber);
                    content.SaveState();
                    content.BeginText();
                    content.SetTextRenderingMode(
                        PdfContentByte.TEXT_RENDER_MODE_INVISIBLE);
                    foreach (var word in words)
                    {
                        var x = word.Left * scaleX;
                        var boxWidth = word.Width * scaleX;
                        var boxHeight = word.Height * scaleY;
                        var y =
                            pageSize.Height -
                            (word.Top + word.Height) *
                            scaleY +
                            boxHeight * 0.12F;
                        var fontSize = Math.Max(
                            2F,
                            boxHeight * 0.82F);
                        var naturalWidth =
                            font.GetWidthPoint(
                                word.Text,
                                fontSize);
                        var horizontalScale =
                            naturalWidth <= 0.01F
                                ? 100F
                                : Math.Max(
                                    15F,
                                    Math.Min(
                                        600F,
                                        boxWidth /
                                        naturalWidth *
                                        100F));
                        content.SetFontAndSize(font, fontSize);
                        content.SetHorizontalScaling(
                            horizontalScale);
                        content.SetTextMatrix(x, y);
                        content.ShowText(word.Text);
                    }

                    content.SetHorizontalScaling(100F);
                    content.EndText();
                    content.RestoreState();
                }

                stamper.Close();
                stamper = null;
                reader = null;
                output.Flush(true);
                output.Dispose();
                output = null;
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

        private static BaseFont CreateOcrFont()
        {
            var fontsDirectory =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.Fonts);
            var candidates = new[]
            {
                Path.Combine(fontsDirectory, "arial.ttf"),
                Path.Combine(fontsDirectory, "segoeui.ttf"),
                Path.Combine(fontsDirectory, "calibri.ttf")
            };
            foreach (var candidate in candidates)
            {
                if (!File.Exists(candidate))
                {
                    continue;
                }

                try
                {
                    return BaseFont.CreateFont(
                        candidate,
                        BaseFont.IDENTITY_H,
                        BaseFont.EMBEDDED);
                }
                catch
                {
                }
            }

            return BaseFont.CreateFont(
                BaseFont.HELVETICA,
                BaseFont.CP1252,
                BaseFont.EMBEDDED);
        }

        private static void ValidateWrittenPdf(
            string path,
            PdfOcrAnalysis analysis,
            IList<OcrPageData> pageData)
        {
            if (!File.Exists(path) ||
                new FileInfo(path).Length <= 0)
            {
                throw new InvalidDataException(
                    "La copia OCR temporal esta vacia.");
            }

            using (var reader = new PdfReader(path))
            {
                if (reader.NumberOfPages != analysis.PageCount)
                {
                    throw new InvalidDataException(
                        "La copia OCR no conserva todas las paginas.");
                }

                foreach (var page in pageData)
                {
                    if (page.RecognizedWordCount <= 0)
                    {
                        continue;
                    }

                    var text = ExtractPageTextSafely(
                        reader,
                        page.PageNumber);
                    if (CountMeaningfulCharacters(text) == 0)
                    {
                        throw new InvalidDataException(
                            "No se pudo verificar la capa OCR de la pagina " +
                            page.PageNumber.ToString(
                                CultureInfo.InvariantCulture) + ".");
                    }
                }
            }

            using (var document = PdfDocumentOpenService.Load(path))
            {
                if (document.PageCount != analysis.PageCount)
                {
                    throw new InvalidDataException(
                        "PDFium no puede abrir todas las paginas de la copia OCR.");
                }
            }
        }

        private static void SavePngDurably(
            Bitmap bitmap,
            string path)
        {
            using (var output = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                1024 * 1024,
                FileOptions.SequentialScan |
                FileOptions.WriteThrough))
            {
                bitmap.Save(output, ImageFormat.Png);
                output.Flush(true);
            }
        }

        private static string CreateWorkingDirectory()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "PDFLigero",
                "ocr");
            Directory.CreateDirectory(root);
            var directory = Path.Combine(
                root,
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static void CleanupWorkingDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            string normalized;
            string expectedRoot;
            try
            {
                normalized = Path.GetFullPath(path);
                expectedRoot = Path.GetFullPath(
                    Path.Combine(
                        Path.GetTempPath(),
                        "PDFLigero",
                        "ocr"));
            }
            catch
            {
                return;
            }

            if (!normalized.StartsWith(
                    expectedRoot + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            for (var attempt = 0; attempt < 8; attempt++)
            {
                try
                {
                    if (!Directory.Exists(normalized))
                    {
                        return;
                    }

                    Directory.Delete(normalized, true);
                    return;
                }
                catch
                {
                    if (attempt < 7)
                    {
                        Thread.Sleep(125);
                    }
                }
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) &&
                    File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        private static void EnsureFreeSpace(
            string sourcePath,
            string targetDirectory)
        {
            try
            {
                var sourceLength = new FileInfo(sourcePath).Length;
                var root = Path.GetPathRoot(targetDirectory);
                if (string.IsNullOrWhiteSpace(root))
                {
                    return;
                }

                var drive = new DriveInfo(root);
                const long reserve = 192L * 1024L * 1024L;
                var required = Math.Max(
                    sourceLength * 2L,
                    64L * 1024L * 1024L) + reserve;
                if (drive.IsReady &&
                    drive.AvailableFreeSpace < required)
                {
                    throw new IOException(
                        "No hay espacio libre suficiente para crear " +
                        "la copia OCR de forma segura.");
                }
            }
            catch (IOException)
            {
                throw;
            }
            catch
            {
                // Network providers do not always expose free space.
            }
        }

        private static void Report(
            Action<PdfOcrProgress> reportProgress,
            int completedSteps,
            int totalSteps,
            int processedPages,
            int totalPages,
            string stage)
        {
            if (reportProgress != null)
            {
                reportProgress(
                    new PdfOcrProgress(
                        completedSteps,
                        totalSteps,
                        processedPages,
                        totalPages,
                        stage));
            }
        }

        private static string FindTesseractExecutable()
        {
            var candidates = new List<string>();
            var configured = Environment.GetEnvironmentVariable(
                "PDFLIGERO_TESSERACT_PATH");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                candidates.Add(configured.Trim().Trim('"'));
            }

            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            candidates.Add(Path.Combine(
                baseDirectory,
                "ocr",
                "tesseract.exe"));
            candidates.Add(Path.Combine(
                baseDirectory,
                "tesseract",
                "tesseract.exe"));
            candidates.Add(Path.Combine(
                baseDirectory,
                "tesseract.exe"));

            var programFiles = Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrWhiteSpace(programFiles))
            {
                candidates.Add(Path.Combine(
                    programFiles,
                    "Tesseract-OCR",
                    "tesseract.exe"));
            }

            var programFilesX86 = Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrWhiteSpace(programFilesX86))
            {
                candidates.Add(Path.Combine(
                    programFilesX86,
                    "Tesseract-OCR",
                    "tesseract.exe"));
            }

            var path = Environment.GetEnvironmentVariable("PATH") ??
                string.Empty;
            foreach (var directory in path.Split(
                new[] { Path.PathSeparator },
                StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    candidates.Add(Path.Combine(
                        directory.Trim().Trim('"'),
                        "tesseract.exe"));
                }
                catch
                {
                }
            }

            foreach (var candidate in candidates)
            {
                try
                {
                    var normalized = Path.GetFullPath(candidate);
                    if (File.Exists(normalized))
                    {
                        return normalized;
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        private static IList<string> ParseLanguageList(string output)
        {
            var result = new List<string>();
            var lines = (output ?? string.Empty).Split(
                new[] { "\r\n", "\n" },
                StringSplitOptions.RemoveEmptyEntries);
            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (line.Length == 0 ||
                    line.StartsWith(
                        "List of available languages",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var valid = true;
                for (var index = 0; index < line.Length; index++)
                {
                    var character = line[index];
                    if (!char.IsLetterOrDigit(character) &&
                        character != '_' &&
                        character != '-')
                    {
                        valid = false;
                        break;
                    }
                }

                if (valid && !result.Contains(line))
                {
                    result.Add(line);
                }
            }

            return result;
        }

        private static ProcessResult RunProcess(
            string executable,
            string arguments,
            CancellationToken cancellationToken,
            bool failOnNonZeroExit)
        {
            var output = new StringBuilder();
            var error = new StringBuilder();
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = Path.GetDirectoryName(executable)
                };
                var localTessdata = Path.Combine(
                    Path.GetDirectoryName(executable),
                    "tessdata");
                if (Directory.Exists(localTessdata))
                {
                    process.StartInfo.EnvironmentVariables[
                        "TESSDATA_PREFIX"] = localTessdata;
                }
                process.OutputDataReceived += delegate(
                    object sender,
                    DataReceivedEventArgs eventArgs)
                {
                    if (eventArgs.Data != null)
                    {
                        output.AppendLine(eventArgs.Data);
                    }
                };
                process.ErrorDataReceived += delegate(
                    object sender,
                    DataReceivedEventArgs eventArgs)
                {
                    if (eventArgs.Data != null)
                    {
                        error.AppendLine(eventArgs.Data);
                    }
                };

                if (!process.Start())
                {
                    throw new InvalidOperationException(
                        "No se pudo iniciar el motor OCR local.");
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                try
                {
                    while (!process.WaitForExit(100))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    process.WaitForExit();
                }
                catch (OperationCanceledException)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill();
                        }

                        if (process.WaitForExit(5000))
                        {
                            process.WaitForExit();
                        }
                    }
                    catch
                    {
                    }

                    throw;
                }

                var result = new ProcessResult(
                    process.ExitCode,
                    output.ToString(),
                    error.ToString());
                if (failOnNonZeroExit && result.ExitCode != 0)
                {
                    throw new InvalidDataException(
                        "Tesseract no pudo reconocer la pagina. " +
                        GetUsefulProcessError(result));
                }

                return result;
            }
        }

        private static string GetUsefulProcessError(ProcessResult result)
        {
            var message = string.IsNullOrWhiteSpace(result.StandardError)
                ? result.StandardOutput
                : result.StandardError;
            message = (message ?? string.Empty).Trim();
            return message.Length > 400
                ? message.Substring(0, 400)
                : message;
        }

        private static string Quote(string value)
        {
            // ProcessStartInfo.Arguments uses the Windows command-line parser.
            // Directory separators are literal and must not be doubled.
            return "\"" + (value ?? string.Empty)
                .Replace("\"", "\\\"") + "\"";
        }

        private static string NormalizeExistingPdfPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new FileNotFoundException(
                    "No se encuentra el PDF para aplicar OCR.",
                    path);
            }

            var normalized = Path.GetFullPath(path);
            if (!File.Exists(normalized))
            {
                throw new FileNotFoundException(
                    "No se encuentra el PDF para aplicar OCR.",
                    normalized);
            }

            if (!string.Equals(
                    Path.GetExtension(normalized),
                    ".pdf",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "El archivo seleccionado no es un PDF.");
            }

            return normalized;
        }

        private static string ValidateOutputPath(
            string sourcePath,
            string outputPath)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new InvalidOperationException(
                    "Selecciona donde guardar la copia con OCR.");
            }

            var normalized = Path.GetFullPath(outputPath);
            if (!string.Equals(
                    Path.GetExtension(normalized),
                    ".pdf",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "La salida del OCR debe ser un archivo PDF.");
            }

            if (string.Equals(
                    normalized,
                    sourcePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "El OCR se guarda en una copia; el original no se sobrescribe.");
            }

            var directory = Path.GetDirectoryName(normalized);
            if (string.IsNullOrWhiteSpace(directory) ||
                !Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException(
                    "La carpeta de destino ya no existe.");
            }

            if (File.Exists(normalized))
            {
                throw new IOException(
                    "Ya existe el PDF de salida. Elige otro nombre.");
            }

            return normalized;
        }

        private sealed class PageCandidate
        {
            public PageCandidate(
                int pageNumber,
                int existingTextCharacters,
                int annotationCount)
            {
                PageNumber = pageNumber;
                ExistingTextCharacters = existingTextCharacters;
                AnnotationCount = annotationCount;
            }

            public int PageNumber { get; private set; }

            public int ExistingTextCharacters { get; private set; }

            public int AnnotationCount { get; private set; }
        }

        private sealed class PageWorkPlan
        {
            public PageWorkPlan(
                PdfOcrPageAnalysis analysis,
                PdfOcrPageInstruction instruction)
            {
                Analysis = analysis;
                Instruction = instruction;
            }

            public PdfOcrPageAnalysis Analysis { get; private set; }

            public PdfOcrPageInstruction Instruction
            {
                get;
                private set;
            }
        }

        private sealed class OrientationResult
        {
            public OrientationResult(
                int clockwiseRotationDegrees,
                float confidence)
            {
                ClockwiseRotationDegrees =
                    clockwiseRotationDegrees;
                Confidence = confidence;
            }

            public int ClockwiseRotationDegrees { get; private set; }

            public float Confidence { get; private set; }
        }

        private sealed class DeskewResult
        {
            public DeskewResult(
                float correctionDegrees,
                float confidencePercent)
            {
                CorrectionDegrees = correctionDegrees;
                ConfidencePercent = confidencePercent;
            }

            public float CorrectionDegrees { get; private set; }

            public float ConfidencePercent { get; private set; }
        }

        private sealed class IntPoint
        {
            public IntPoint(int x, int y)
            {
                X = x;
                Y = y;
            }

            public int X { get; private set; }

            public int Y { get; private set; }
        }

        private sealed class OcrWord
        {
            public OcrWord(
                int left,
                int top,
                int width,
                int height,
                float confidence,
                string text)
            {
                Left = left;
                Top = top;
                Width = width;
                Height = height;
                Confidence = confidence;
                Text = text;
            }

            public int Left { get; private set; }

            public int Top { get; private set; }

            public int Width { get; private set; }

            public int Height { get; private set; }

            public float Confidence { get; private set; }

            public string Text { get; private set; }
        }

        private sealed class OcrPageData
        {
            public OcrPageData(
                int pageNumber,
                int renderWidth,
                int renderHeight,
                string tsvPath,
                int recognizedWordCount)
            {
                PageNumber = pageNumber;
                RenderWidth = renderWidth;
                RenderHeight = renderHeight;
                TsvPath = tsvPath;
                RecognizedWordCount = recognizedWordCount;
            }

            public int PageNumber { get; private set; }

            public int RenderWidth { get; private set; }

            public int RenderHeight { get; private set; }

            public string TsvPath { get; private set; }

            public int RecognizedWordCount { get; private set; }
        }

        private sealed class ProcessResult
        {
            public ProcessResult(
                int exitCode,
                string standardOutput,
                string standardError)
            {
                ExitCode = exitCode;
                StandardOutput = standardOutput ?? string.Empty;
                StandardError = standardError ?? string.Empty;
            }

            public int ExitCode { get; private set; }

            public string StandardOutput { get; private set; }

            public string StandardError { get; private set; }
        }
    }
}

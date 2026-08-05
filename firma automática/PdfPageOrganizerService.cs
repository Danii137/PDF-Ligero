using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using iTextSharp.text;
using iTextSharp.text.factories;
using iTextSharp.text.pdf;

namespace FirmaAutomatica
{
    /// <summary>
    /// Describes one output page. SourcePageNumber is one-based and
    /// ClockwiseRotationDegrees is a rotation relative to the source page.
    /// </summary>
    internal sealed class PdfPageOrganizerPage
    {
        public PdfPageOrganizerPage(
            int sourcePageNumber,
            int clockwiseRotationDegrees)
        {
            SourcePageNumber = sourcePageNumber;
            ClockwiseRotationDegrees = clockwiseRotationDegrees;
        }

        public int SourcePageNumber { get; private set; }

        public int ClockwiseRotationDegrees { get; private set; }
    }

    internal sealed class PdfPageOrganizerProgress
    {
        public PdfPageOrganizerProgress(
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

    internal sealed class PdfPageOrganizerAnalysis
    {
        public PdfPageOrganizerAnalysis(
            int sourcePageCount,
            int resultPageCount,
            int rotatedPageCount,
            bool containsDigitalSignatures)
        {
            SourcePageCount = sourcePageCount;
            ResultPageCount = resultPageCount;
            RotatedPageCount = rotatedPageCount;
            ContainsDigitalSignatures = containsDigitalSignatures;
        }

        public int SourcePageCount { get; private set; }

        public int ResultPageCount { get; private set; }

        public int RemovedPageCount
        {
            get { return SourcePageCount - ResultPageCount; }
        }

        public int RotatedPageCount { get; private set; }

        public bool ContainsDigitalSignatures { get; private set; }

        public bool DigitalSignaturesWillBeInvalidated
        {
            get { return ContainsDigitalSignatures; }
        }

        public string DigitalSignatureWarning
        {
            get
            {
                return ContainsDigitalSignatures
                    ? PdfPageOrganizerService.DigitalSignatureInvalidationWarning
                    : string.Empty;
            }
        }
    }

    internal sealed class PdfPageOrganizerResult
    {
        public PdfPageOrganizerResult(
            string outputPath,
            int sourcePageCount,
            int pageCount,
            int rotatedPageCount,
            bool digitalSignaturesInvalidated)
        {
            OutputPath = outputPath;
            SourcePageCount = sourcePageCount;
            PageCount = pageCount;
            RotatedPageCount = rotatedPageCount;
            DigitalSignaturesInvalidated = digitalSignaturesInvalidated;
        }

        public string OutputPath { get; private set; }

        public int SourcePageCount { get; private set; }

        public int PageCount { get; private set; }

        public int RemovedPageCount
        {
            get { return SourcePageCount - PageCount; }
        }

        public int RotatedPageCount { get; private set; }

        public bool DigitalSignaturesInvalidated { get; private set; }

        public string DigitalSignatureWarning
        {
            get
            {
                return DigitalSignaturesInvalidated
                    ? PdfPageOrganizerService.DigitalSignatureInvalidationWarning
                    : string.Empty;
            }
        }
    }

    /// <summary>
    /// Creates an organized copy of one PDF in a single structural operation.
    /// Page streams and resources are imported without rasterization or image
    /// recompression. The source file is always read-only.
    /// </summary>
    internal static class PdfPageOrganizerService
    {
        public const string DigitalSignatureInvalidationWarning =
            "La copia organizada ya no conserva la validez criptografica de " +
            "las firmas digitales del documento original.";

        public const string XfaUnsupportedMessage =
            "Los formularios XFA no se pueden reorganizar de forma segura. " +
            "Guarda antes una copia PDF normal del formulario.";

        public const string AdvancedOutlineDeletionUnsupportedMessage =
            "No se pueden eliminar paginas de este PDF porque contiene " +
            "marcadores con acciones avanzadas que no se pueden reconstruir " +
            "sin perder informacion. Puedes reordenar o girar sus paginas " +
            "sin eliminarlas.";

        public const string ComplexActionDeletionUnsupportedMessage =
            "No se pueden eliminar paginas de este PDF porque contiene una " +
            "cadena de acciones internas que no se puede simplificar de forma " +
            "segura.";

        public static string SuggestOutputPath(string sourcePdfPath)
        {
            var normalizedSourcePath = NormalizeExistingPdfPath(sourcePdfPath);
            var directory = Path.GetDirectoryName(normalizedSourcePath);
            if (string.IsNullOrWhiteSpace(directory) ||
                !Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException(
                    "La carpeta del PDF ya no existe.");
            }

            var baseName =
                Path.GetFileNameWithoutExtension(normalizedSourcePath);
            var candidate = Path.Combine(
                directory,
                baseName + "_organizado.pdf");
            var suffix = 2;
            while (File.Exists(candidate))
            {
                candidate = Path.Combine(
                    directory,
                    baseName + "_organizado_" +
                    suffix.ToString(CultureInfo.InvariantCulture) + ".pdf");
                suffix++;
            }

            return candidate;
        }

        public static PdfPageOrganizerAnalysis Analyze(
            string sourcePdfPath,
            IList<PdfPageOrganizerPage> pages)
        {
            var normalizedSourcePath =
                NormalizeExistingPdfPath(sourcePdfPath);
            using (OpenSourceReadGuard(normalizedSourcePath))
            {
                var plan = CreatePlan(normalizedSourcePath, pages);
                return new PdfPageOrganizerAnalysis(
                    plan.SourcePageCount,
                    plan.Pages.Count,
                    plan.RotatedPageCount,
                    plan.ContainsDigitalSignatures);
            }
        }

        public static PdfPageOrganizerResult Organize(
            string sourcePdfPath,
            IList<PdfPageOrganizerPage> pages,
            Action<PdfPageOrganizerProgress> reportProgress)
        {
            var outputPath = SuggestOutputPath(sourcePdfPath);
            return Organize(
                sourcePdfPath,
                pages,
                outputPath,
                reportProgress,
                CancellationToken.None);
        }

        public static PdfPageOrganizerResult Organize(
            string sourcePdfPath,
            IList<PdfPageOrganizerPage> pages,
            string outputPath,
            Action<PdfPageOrganizerProgress> reportProgress)
        {
            return Organize(
                sourcePdfPath,
                pages,
                outputPath,
                reportProgress,
                CancellationToken.None);
        }

        public static PdfPageOrganizerResult Organize(
            string sourcePdfPath,
            IList<PdfPageOrganizerPage> pages,
            string outputPath,
            Action<PdfPageOrganizerProgress> reportProgress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalizedSourcePath =
                NormalizeExistingPdfPath(sourcePdfPath);
            using (OpenSourceReadGuard(normalizedSourcePath))
            {
                var plan = CreatePlan(normalizedSourcePath, pages);
                var normalizedOutputPath =
                    ValidateOutputPath(plan.SourcePath, outputPath);
                var outputDirectory =
                    Path.GetDirectoryName(normalizedOutputPath);
                var temporaryPath = Path.Combine(
                    outputDirectory,
                    "." +
                    Path.GetFileNameWithoutExtension(normalizedOutputPath) +
                    "." + Guid.NewGuid().ToString("N") + ".tmp");

                try
                {
                    var expectations = WriteOrganizedPdf(
                        plan,
                        temporaryPath,
                        reportProgress,
                        cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();

                    ValidateWrittenPdf(
                        temporaryPath,
                        plan,
                        expectations);
                    cancellationToken.ThrowIfCancellationRequested();

                    // The temporary file is on the destination volume.
                    // File.Move is therefore atomic and intentionally fails if
                    // another process creates the selected name meanwhile.
                    File.Move(temporaryPath, normalizedOutputPath);

                    return new PdfPageOrganizerResult(
                        normalizedOutputPath,
                        plan.SourcePageCount,
                        plan.Pages.Count,
                        plan.RotatedPageCount,
                        plan.ContainsDigitalSignatures);
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                    {
                        try
                        {
                            File.Delete(temporaryPath);
                        }
                        catch
                        {
                        }
                    }
                }
            }
        }

        private static OrganizationPlan CreatePlan(
            string sourcePdfPath,
            IList<PdfPageOrganizerPage> requestedPages)
        {
            var normalizedSourcePath =
                NormalizeExistingPdfPath(sourcePdfPath);
            if (requestedPages == null || requestedPages.Count == 0)
            {
                throw new InvalidOperationException(
                    "El documento organizado debe conservar al menos una pagina.");
            }

            PdfReader reader = null;
            try
            {
                reader = OpenPdfReader(normalizedSourcePath);
                if (reader.NumberOfPages < 1)
                {
                    throw new InvalidDataException(
                        "El PDF no contiene paginas.");
                }

                var acroForm =
                    reader.Catalog.GetAsDict(PdfName.ACROFORM);
                if (acroForm != null &&
                    acroForm.Get(PdfName.XFA) != null)
                {
                    throw new NotSupportedException(
                        XfaUnsupportedMessage);
                }

                var pages =
                    new List<PlannedPage>(requestedPages.Count);
                var usedSourcePages = new HashSet<int>();
                var rotatedPageCount = 0;
                for (var index = 0;
                    index < requestedPages.Count;
                    index++)
                {
                    var requestedPage = requestedPages[index];
                    if (requestedPage == null)
                    {
                        throw new ArgumentException(
                            "La lista de paginas contiene una entrada vacia.",
                            "requestedPages");
                    }

                    var sourcePage = requestedPage.SourcePageNumber;
                    if (sourcePage < 1 ||
                        sourcePage > reader.NumberOfPages)
                    {
                        throw new ArgumentOutOfRangeException(
                            "requestedPages",
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "La pagina de origen {0} no existe; el PDF " +
                                "tiene {1} paginas.",
                                sourcePage,
                                reader.NumberOfPages));
                    }

                    if (!usedSourcePages.Add(sourcePage))
                    {
                        throw new ArgumentException(
                            "Una pagina de origen no puede aparecer dos veces " +
                            "en la misma organizacion.",
                            "requestedPages");
                    }

                    var delta = NormalizeRequestedRotation(
                        requestedPage.ClockwiseRotationDegrees);
                    var sourceRotation = NormalizeRotation(
                        reader.GetPageRotation(sourcePage));
                    var outputRotation = NormalizeRotation(
                        sourceRotation + delta);
                    if (delta != 0)
                    {
                        rotatedPageCount++;
                    }

                    var mediaBox = reader.GetPageSize(sourcePage);
                    var cropBox = reader.GetCropBox(sourcePage);
                    var pageDictionary = reader.GetPageN(sourcePage);
                    var annotations = pageDictionary == null
                        ? null
                        : pageDictionary.GetAsArray(PdfName.ANNOTS);
                    pages.Add(
                        new PlannedPage(
                            sourcePage,
                            delta,
                            outputRotation,
                            mediaBox == null ? 0F : mediaBox.Width,
                            mediaBox == null ? 0F : mediaBox.Height,
                            cropBox == null ? 0F : cropBox.Width,
                            cropBox == null ? 0F : cropBox.Height,
                            annotations == null ? 0 : annotations.Size));
                }

                var expectedFormFieldCount =
                    CountSelectedFormFields(reader, usedSourcePages);
                var expectedFormWidgetCount =
                    CountSelectedFormWidgets(reader, usedSourcePages);
                var signatureNames = reader.AcroFields == null
                    ? null
                    : reader.AcroFields.GetSignatureNames();
                var containsDigitalSignatures =
                    signatureNames != null && signatureNames.Count > 0;
                if (usedSourcePages.Count < reader.NumberOfPages)
                {
                    EnsureOutlinesCompatibleWithDeletion(reader);
                }

                return new OrganizationPlan(
                    normalizedSourcePath,
                    reader.NumberOfPages,
                    pages,
                    usedSourcePages,
                    rotatedPageCount,
                    expectedFormFieldCount,
                    expectedFormWidgetCount,
                    containsDigitalSignatures);
            }
            catch (NotSupportedException)
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
                    "No se pudo preparar \"" +
                    Path.GetFileName(normalizedSourcePath) + "\": " +
                    ex.Message,
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

        private static OutputExpectations WriteOrganizedPdf(
            OrganizationPlan plan,
            string temporaryPath,
            Action<PdfPageOrganizerProgress> reportProgress,
            CancellationToken cancellationToken)
        {
            var reporter =
                new ThrottledProgressReporter(reportProgress);
            var totalSteps = plan.Pages.Count + 1;
            reporter.Report(
                0,
                totalSteps,
                0,
                plan.Pages.Count,
                "Preparando paginas",
                true);

            PdfReader reader = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                reader = OpenPdfReader(plan.SourcePath);

                var stringNamedDestinations =
                    SimpleNamedDestination.GetNamedDestination(
                        reader,
                        false);
                var nameNamedDestinations =
                    SimpleNamedDestination.GetNamedDestination(
                        reader,
                        true);
                var bookmarks = plan.RemovesPages
                    ? PrepareBookmarks(
                        reader,
                        plan.OutputPageBySourcePage,
                        stringNamedDestinations,
                        nameNamedDestinations)
                    : null;
                var expectedBookmarkCount = plan.RemovesPages
                    ? CountBookmarks(bookmarks)
                    : CountBookmarks(
                        SimpleBookmark.GetBookmark(reader));
                var outputStringNamedDestinations =
                    PrepareNamedDestinations(
                        plan.OutputPageBySourcePage,
                        stringNamedDestinations);
                var outputNameNamedDestinations =
                    PrepareNamedDestinations(
                    plan.OutputPageBySourcePage,
                    nameNamedDestinations);
                var outputLabels =
                    PreparePageLabels(reader, plan.Pages);
                var sourceMetadata =
                    CloneMetadata(reader.Info);
                var xmpMetadata = reader.Metadata;
                if (plan.RemovesPages)
                {
                    CleanDestinationsToDeletedPages(
                        reader,
                        plan.SelectedSourcePages,
                        ReadPageNumbersByReference(reader),
                        stringNamedDestinations,
                        nameNamedDestinations);
                }

                for (var outputIndex = 0;
                    outputIndex < plan.Pages.Count;
                    outputIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var page = plan.Pages[outputIndex];
                    ApplyRotation(reader, page);
                    reporter.Report(
                        outputIndex + 1,
                        totalSteps,
                        outputIndex + 1,
                        plan.Pages.Count,
                        "Preparando paginas",
                        false);
                }

                cancellationToken.ThrowIfCancellationRequested();
                reader.SelectPages(plan.SourcePageNumbers);
                if (plan.RemovesPages)
                {
                    CleanAcroFormCalculationOrder(reader);
                }
                reporter.Report(
                    plan.Pages.Count,
                    totalSteps,
                    plan.Pages.Count,
                    plan.Pages.Count,
                    "Escribiendo copia estructural",
                    true);

                using (var outputStream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    1024 * 1024,
                    FileOptions.SequentialScan |
                    FileOptions.WriteThrough))
                {
                    using (var stamper = new PdfStamper(
                        reader,
                        outputStream))
                    {
                        // A stamper starts from the original catalog, so
                        // attachments, optional-content configuration,
                        // document JavaScript and other unrelated catalog
                        // features survive whenever iText can preserve them.
                        // SelectPages has already adjusted the AcroForm tree.
                        stamper.Writer.CloseStream = false;
                        ApplyDocumentProperties(
                            reader,
                            stamper,
                            sourceMetadata,
                            xmpMetadata,
                            outputLabels.Labels);
                        if (plan.RemovesPages)
                        {
                            ReplaceNamedDestinations(
                                reader,
                                stamper,
                                outputStringNamedDestinations,
                                outputNameNamedDestinations);

                            // Only deletion requires rebuilding the outline
                            // tree. Reorder/rotation keeps the original raw
                            // dictionaries so uncommon actions survive intact.
                            stamper.Outlines = bookmarks;
                        }
                    }

                    // The validation and atomic move must never observe bytes
                    // that only exist in the Windows write cache.
                    outputStream.Flush(true);
                }

                cancellationToken.ThrowIfCancellationRequested();
                reporter.Report(
                    totalSteps,
                    totalSteps,
                    plan.Pages.Count,
                    plan.Pages.Count,
                    "Comprobando resultado",
                    true);
                return new OutputExpectations(
                    expectedBookmarkCount,
                    outputStringNamedDestinations,
                    outputNameNamedDestinations,
                    GetVerifiableMetadata(sourceMetadata),
                    outputLabels.ExpectedLabels,
                    outputLabels.ExpectedRules);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidDataException(
                    "No se pudo crear la copia organizada de \"" +
                    Path.GetFileName(plan.SourcePath) + "\": " +
                    ex.Message,
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

        private static void ApplyRotation(
            PdfReader reader,
            PlannedPage page)
        {
            if (page.RotationDelta == 0)
            {
                return;
            }

            var pageDictionary =
                reader.GetPageN(page.SourcePageNumber);
            if (pageDictionary == null)
            {
                throw new InvalidDataException(
                    "No se pudo acceder a una de las paginas que se iban a girar.");
            }

            // Write an explicit zero as well. Removing /Rotate could expose an
            // inherited rotation from the source page tree and undo a -90/270
            // request on documents that keep /Rotate at an ancestor node.
            pageDictionary.Put(
                PdfName.ROTATE,
                new PdfNumber(page.OutputRotation));
        }

        private static void ApplyDocumentProperties(
            PdfReader reader,
            PdfStamper stamper,
            IDictionary<string, string> metadata,
            byte[] xmpMetadata,
            PdfPageLabels outputLabels)
        {
            stamper.ViewerPreferences =
                reader.SimpleViewerPreferences;

            if (metadata != null)
            {
                stamper.MoreInfo = metadata;
            }

            if (xmpMetadata != null && xmpMetadata.Length > 0)
            {
                stamper.XmpMetadata = xmpMetadata;
            }

            if (outputLabels != null)
            {
                reader.Catalog.Remove(PdfName.PAGELABELS);
                stamper.MarkUsed(reader.Catalog);
                stamper.Writer.PageLabels = outputLabels;
            }
        }

        private static void ReplaceNamedDestinations(
            PdfReader reader,
            PdfStamper stamper,
            IDictionary<string, string> stringNamedDestinations,
            IDictionary<string, string> nameNamedDestinations)
        {
            var catalog = reader == null
                ? null
                : reader.Catalog;
            if (catalog == null || stamper == null)
            {
                return;
            }

            catalog.Remove(PdfName.DESTS);
            if (nameNamedDestinations != null &&
                nameNamedDestinations.Count > 0)
            {
                var destinations =
                    new Dictionary<string, string>(
                        nameNamedDestinations,
                        StringComparer.Ordinal);
                catalog.Put(
                    PdfName.DESTS,
                    SimpleNamedDestination
                        .OutputNamedDestinationAsNames(
                            destinations,
                            stamper.Writer));
            }

            var names = catalog.GetAsDict(PdfName.NAMES);
            if (names == null &&
                stringNamedDestinations != null &&
                stringNamedDestinations.Count > 0)
            {
                names = new PdfDictionary();
                catalog.Put(PdfName.NAMES, names);
            }

            if (names != null)
            {
                // Remove only the destinations subtree. Embedded files,
                // JavaScript and every other /Names feature remain untouched.
                names.Remove(PdfName.DESTS);
                if (stringNamedDestinations != null &&
                    stringNamedDestinations.Count > 0)
                {
                    var destinations =
                        new Dictionary<string, string>(
                            stringNamedDestinations,
                            StringComparer.Ordinal);
                    var destinationTree =
                        SimpleNamedDestination
                            .OutputNamedDestinationAsStrings(
                                destinations,
                                stamper.Writer);
                    names.Put(PdfName.DESTS, destinationTree);
                }

                stamper.MarkUsed(names);
            }

            stamper.MarkUsed(catalog);
        }

        private static void EnsureOutlinesCompatibleWithDeletion(
            PdfReader reader)
        {
            var outlines =
                reader.Catalog.GetAsDict(
                    PdfName.OUTLINES);
            if (outlines == null)
            {
                return;
            }

            InspectOutlineItemsForDeletion(
                outlines.Get(PdfName.FIRST),
                new HashSet<string>(
                    StringComparer.Ordinal),
                0);
        }

        private static void InspectOutlineItemsForDeletion(
            PdfObject firstOutline,
            ISet<string> visitedOutlines,
            int depth)
        {
            if (depth > 256)
            {
                throw new NotSupportedException(
                    AdvancedOutlineDeletionUnsupportedMessage);
            }

            var current = firstOutline;
            while (current != null)
            {
                var reference =
                    current as PRIndirectReference;
                if (reference != null &&
                    !visitedOutlines.Add(
                        GetReferenceKey(reference)))
                {
                    throw new NotSupportedException(
                        AdvancedOutlineDeletionUnsupportedMessage);
                }

                var outline = PdfReader.GetPdfObject(
                    current) as PdfDictionary;
                if (outline == null ||
                    outline.Get(new PdfName("SE")) != null ||
                    (outline.Get(PdfName.DEST) != null &&
                     outline.Get(PdfName.A) != null))
                {
                    throw new NotSupportedException(
                        AdvancedOutlineDeletionUnsupportedMessage);
                }

                var action =
                    outline.GetAsDict(PdfName.A);
                if (action != null &&
                    !IsOutlineActionSafelyRebuildable(action))
                {
                    throw new NotSupportedException(
                        AdvancedOutlineDeletionUnsupportedMessage);
                }

                InspectOutlineItemsForDeletion(
                    outline.Get(PdfName.FIRST),
                    visitedOutlines,
                    depth + 1);
                current = outline.Get(PdfName.NEXT);
            }
        }

        private static bool IsOutlineActionSafelyRebuildable(
            PdfDictionary action)
        {
            var actionType =
                action.GetAsName(PdfName.S);
            if (PdfName.GOTO.Equals(actionType))
            {
                return action.Get(PdfName.D) != null &&
                    ContainsOnlyActionKeys(
                        action,
                        PdfName.D);
            }

            if (PdfName.GOTOR.Equals(actionType))
            {
                var file = PdfReader.GetPdfObject(
                    action.Get(PdfName.F)) as PdfString;
                var destination = PdfReader.GetPdfObject(
                    action.Get(PdfName.D));
                return file != null &&
                    destination != null &&
                    (destination.IsArray() ||
                     destination.IsName() ||
                     destination.IsString()) &&
                    ContainsOnlyActionKeys(
                        action,
                        PdfName.D,
                        PdfName.F,
                        PdfName.NEWWINDOW);
            }

            if (PdfName.URI.Equals(actionType))
            {
                return action.GetAsString(PdfName.URI) != null &&
                    ContainsOnlyActionKeys(
                        action,
                        PdfName.URI);
            }

            return false;
        }

        private static bool ContainsOnlyActionKeys(
            PdfDictionary action,
            params PdfName[] allowedValueKeys)
        {
            foreach (var key in action.Keys)
            {
                if (PdfName.S.Equals(key) ||
                    PdfName.TYPE.Equals(key))
                {
                    continue;
                }

                var allowed = false;
                foreach (var allowedKey in
                    allowedValueKeys)
                {
                    if (allowedKey.Equals(key))
                    {
                        allowed = true;
                        break;
                    }
                }

                if (!allowed)
                {
                    return false;
                }
            }

            return true;
        }

        private static List<Dictionary<string, object>> PrepareBookmarks(
            PdfReader reader,
            IDictionary<int, int> outputPageBySourcePage,
            IDictionary<string, string> stringNamedDestinations,
            IDictionary<string, string> nameNamedDestinations)
        {
            var bookmarks = SimpleBookmark.GetBookmark(reader);
            if (bookmarks == null || bookmarks.Count == 0)
            {
                return new List<Dictionary<string, object>>();
            }

            ConvertNamedBookmarksToExplicitPages(
                bookmarks,
                stringNamedDestinations,
                nameNamedDestinations);
            return RemapBookmarks(
                bookmarks,
                outputPageBySourcePage);
        }

        private static List<Dictionary<string, object>> RemapBookmarks(
            IList<Dictionary<string, object>> bookmarks,
            IDictionary<int, int> outputPageBySourcePage)
        {
            var result =
                new List<Dictionary<string, object>>();
            if (bookmarks == null)
            {
                return result;
            }

            foreach (var bookmark in bookmarks)
            {
                if (bookmark == null)
                {
                    continue;
                }

                object kidsValue;
                var sourceKids =
                    bookmark.TryGetValue("Kids", out kidsValue)
                        ? kidsValue as
                            IList<Dictionary<string, object>>
                        : null;
                var mappedKids = RemapBookmarks(
                    sourceKids,
                    outputPageBySourcePage);
                if (mappedKids.Count > 0)
                {
                    bookmark["Kids"] = mappedKids;
                }
                else
                {
                    bookmark.Remove("Kids");
                }

                object actionValue;
                var action = bookmark.TryGetValue(
                    "Action",
                    out actionValue)
                        ? Convert.ToString(
                            actionValue,
                            CultureInfo.InvariantCulture)
                        : string.Empty;
                var isLocalGoTo =
                    string.IsNullOrEmpty(action) ||
                    string.Equals(
                        action,
                        "GoTo",
                        StringComparison.Ordinal);
                object pageValue;
                if (isLocalGoTo &&
                    bookmark.TryGetValue("Page", out pageValue))
                {
                    var destination = Convert.ToString(
                        pageValue,
                        CultureInfo.InvariantCulture);
                    int sourcePage;
                    if (TryReadDestinationPage(
                            destination,
                            out sourcePage))
                    {
                        int outputPage;
                        if (outputPageBySourcePage.TryGetValue(
                                sourcePage,
                                out outputPage))
                        {
                            bookmark["Page"] =
                                ReplaceDestinationPage(
                                    destination,
                                    outputPage);
                        }
                        else
                        {
                            // A bookmark whose destination was deleted is not
                            // redirected to unrelated content. A parent with
                            // surviving children remains as a non-clickable
                            // outline heading.
                            bookmark.Remove("Action");
                            bookmark.Remove("Page");
                            bookmark.Remove("Named");
                            bookmark.Remove("NamedN");
                            if (mappedKids.Count == 0)
                            {
                                continue;
                            }
                        }
                    }
                }

                result.Add(bookmark);
            }

            return result;
        }

        private static void ConvertNamedBookmarksToExplicitPages(
            IList<Dictionary<string, object>> bookmarks,
            IDictionary<string, string> stringNamedDestinations,
            IDictionary<string, string> nameNamedDestinations)
        {
            if (bookmarks == null)
            {
                return;
            }

            foreach (var bookmark in bookmarks)
            {
                if (bookmark == null)
                {
                    continue;
                }

                object actionValue;
                var isLocalGoTo =
                    bookmark.TryGetValue("Action", out actionValue) &&
                    string.Equals(
                        Convert.ToString(
                            actionValue,
                            CultureInfo.InvariantCulture),
                        "GoTo",
                        StringComparison.Ordinal);
                if (isLocalGoTo)
                {
                    object namedValue;
                    string destination;
                    if (bookmark.TryGetValue(
                            "Named",
                            out namedValue) &&
                        TryResolveNamedDestination(
                            Convert.ToString(
                                namedValue,
                                CultureInfo.InvariantCulture),
                            stringNamedDestinations,
                            nameNamedDestinations,
                            false,
                            out destination))
                    {
                        bookmark["Page"] = destination;
                        bookmark.Remove("Named");
                        bookmark.Remove("NamedN");
                    }
                    else if (bookmark.TryGetValue(
                            "NamedN",
                            out namedValue) &&
                        TryResolveNamedDestination(
                            Convert.ToString(
                                namedValue,
                                CultureInfo.InvariantCulture),
                            nameNamedDestinations,
                            stringNamedDestinations,
                            true,
                            out destination))
                    {
                        bookmark["Page"] = destination;
                        bookmark.Remove("Named");
                        bookmark.Remove("NamedN");
                    }
                }

                object kidsValue;
                var kids =
                    bookmark.TryGetValue("Kids", out kidsValue)
                        ? kidsValue as
                            IList<Dictionary<string, object>>
                        : null;
                ConvertNamedBookmarksToExplicitPages(
                    kids,
                    stringNamedDestinations,
                    nameNamedDestinations);
            }
        }

        private static bool TryResolveNamedDestination(
            string name,
            IDictionary<string, string> primaryDestinations,
            IDictionary<string, string> secondaryDestinations,
            bool isPdfName,
            out string destination)
        {
            destination = null;
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            var candidates = new List<string> { name };
            if (isPdfName)
            {
                var encodedName =
                    name[0] == '/' ? name : "/" + name;
                var decodedName = PdfName.DecodeName(encodedName);
                if (!string.Equals(
                        decodedName,
                        name,
                        StringComparison.Ordinal))
                {
                    candidates.Add(decodedName);
                }
            }
            else
            {
                var unescapedName =
                    SimpleBookmark.UnEscapeBinaryString(name);
                if (!string.Equals(
                        unescapedName,
                        name,
                        StringComparison.Ordinal))
                {
                    candidates.Add(unescapedName);
                }
            }

            foreach (var candidate in candidates)
            {
                if (primaryDestinations != null &&
                    primaryDestinations.TryGetValue(
                        candidate,
                        out destination))
                {
                    return true;
                }

                if (secondaryDestinations != null &&
                    secondaryDestinations.TryGetValue(
                        candidate,
                        out destination))
                {
                    return true;
                }
            }

            destination = null;
            return false;
        }

        private static Dictionary<string, string>
            PrepareNamedDestinations(
                IDictionary<int, int> outputPageBySourcePage,
                IDictionary<string, string> sourceNamedDestinations)
        {
            var result =
                new Dictionary<string, string>(
                    StringComparer.Ordinal);
            AddMappedNamedDestinations(
                sourceNamedDestinations,
                outputPageBySourcePage,
                result);
            return result;
        }

        private static void AddMappedNamedDestinations(
            IDictionary<string, string> source,
            IDictionary<int, int> outputPageBySourcePage,
            IDictionary<string, string> target)
        {
            if (source == null)
            {
                return;
            }

            foreach (var entry in source)
            {
                int sourcePage;
                int outputPage;
                if (string.IsNullOrEmpty(entry.Key) ||
                    !TryReadDestinationPage(
                        entry.Value,
                        out sourcePage) ||
                    !outputPageBySourcePage.TryGetValue(
                        sourcePage,
                        out outputPage))
                {
                    continue;
                }

                var mappedDestination =
                    ReplaceDestinationPage(
                        entry.Value,
                        outputPage);
                target[entry.Key] = mappedDestination;
            }
        }

        private static bool TryReadDestinationPage(
            string destination,
            out int pageNumber)
        {
            pageNumber = 0;
            if (string.IsNullOrWhiteSpace(destination))
            {
                return false;
            }

            var separatorIndex = destination.IndexOf(' ');
            var pageText = separatorIndex < 0
                ? destination
                : destination.Substring(0, separatorIndex);
            return int.TryParse(
                pageText,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out pageNumber);
        }

        private static string ReplaceDestinationPage(
            string destination,
            int outputPage)
        {
            var separatorIndex = destination.IndexOf(' ');
            return outputPage.ToString(
                    CultureInfo.InvariantCulture) +
                (separatorIndex < 0
                    ? string.Empty
                    : destination.Substring(separatorIndex));
        }

        private static Dictionary<string, int>
            ReadPageNumbersByReference(PdfReader reader)
        {
            var result =
                new Dictionary<string, int>(
                    StringComparer.Ordinal);
            for (var page = 1;
                page <= reader.NumberOfPages;
                page++)
            {
                var reference = reader.GetPageOrigRef(page);
                if (reference != null)
                {
                    result[GetReferenceKey(reference)] = page;
                }
            }

            return result;
        }

        private static void CleanDestinationsToDeletedPages(
            PdfReader reader,
            ISet<int> selectedSourcePages,
            IDictionary<string, int> pageNumbersByReference,
            IDictionary<string, string> stringNamedDestinations,
            IDictionary<string, string> nameNamedDestinations)
        {
            var catalog = reader.Catalog;
            var openAction = PdfReader.GetPdfObject(
                catalog.Get(PdfName.OPENACTION));
            var openActionDictionary =
                openAction as PdfDictionary;
            if (openActionDictionary != null)
            {
                if (!CleanActionObject(
                        catalog.Get(PdfName.OPENACTION),
                        selectedSourcePages,
                        pageNumbersByReference,
                        stringNamedDestinations,
                        nameNamedDestinations))
                {
                    catalog.Remove(PdfName.OPENACTION);
                }
            }
            else if (DestinationTargetsDeletedPage(
                        catalog.Get(PdfName.OPENACTION),
                        selectedSourcePages,
                        pageNumbersByReference,
                        stringNamedDestinations,
                        nameNamedDestinations))
            {
                catalog.Remove(PdfName.OPENACTION);
            }

            CleanAdditionalActions(
                catalog,
                selectedSourcePages,
                pageNumbersByReference,
                stringNamedDestinations,
                nameNamedDestinations);

            foreach (var sourcePage in selectedSourcePages)
            {
                var pageDictionary =
                    reader.GetPageN(sourcePage);
                if (pageDictionary == null)
                {
                    continue;
                }

                CleanAdditionalActions(
                    pageDictionary,
                    selectedSourcePages,
                    pageNumbersByReference,
                    stringNamedDestinations,
                    nameNamedDestinations);

                var annotations =
                    pageDictionary.GetAsArray(PdfName.ANNOTS);
                if (annotations == null)
                {
                    continue;
                }

                for (var index = 0;
                    index < annotations.Size;
                    index++)
                {
                    var annotation = PdfReader.GetPdfObject(
                        annotations[index]) as PdfDictionary;
                    if (annotation == null)
                    {
                        continue;
                    }

                    if (DestinationTargetsDeletedPage(
                            annotation.Get(PdfName.DEST),
                            selectedSourcePages,
                            pageNumbersByReference,
                            stringNamedDestinations,
                            nameNamedDestinations))
                    {
                        annotation.Remove(PdfName.DEST);
                    }

                    var actionObject =
                        annotation.Get(PdfName.A);
                    if (actionObject != null &&
                        !CleanActionObject(
                            actionObject,
                            selectedSourcePages,
                            pageNumbersByReference,
                            stringNamedDestinations,
                            nameNamedDestinations))
                    {
                        // Keep comments, appearance and geometry, but remove
                        // an action whose local destination was deleted.
                        annotation.Remove(PdfName.A);
                    }

                    CleanAdditionalActions(
                        annotation,
                        selectedSourcePages,
                        pageNumbersByReference,
                        stringNamedDestinations,
                        nameNamedDestinations);
                }
            }

            var acroForm =
                catalog.GetAsDict(PdfName.ACROFORM);
            var fields = acroForm == null
                ? null
                : acroForm.GetAsArray(PdfName.FIELDS);
            if (fields != null)
            {
                var visitedFields =
                    new HashSet<string>(
                        StringComparer.Ordinal);
                for (var index = 0;
                    index < fields.Size;
                    index++)
                {
                    CleanFieldAdditionalActions(
                        fields[index],
                        selectedSourcePages,
                        pageNumbersByReference,
                        stringNamedDestinations,
                        nameNamedDestinations,
                        visitedFields,
                        0);
                }
            }
        }

        private static void CleanFieldAdditionalActions(
            PdfObject fieldObject,
            ISet<int> selectedSourcePages,
            IDictionary<string, int> pageNumbersByReference,
            IDictionary<string, string> stringNamedDestinations,
            IDictionary<string, string> nameNamedDestinations,
            ISet<string> visitedFields,
            int depth)
        {
            if (fieldObject == null || depth > 256)
            {
                return;
            }

            var reference =
                fieldObject as PRIndirectReference;
            if (reference != null &&
                !visitedFields.Add(
                    GetReferenceKey(reference)))
            {
                return;
            }

            var field = PdfReader.GetPdfObject(
                fieldObject) as PdfDictionary;
            if (field == null)
            {
                return;
            }

            CleanAdditionalActions(
                field,
                selectedSourcePages,
                pageNumbersByReference,
                stringNamedDestinations,
                nameNamedDestinations);
            var kids = field.GetAsArray(PdfName.KIDS);
            if (kids == null)
            {
                return;
            }

            for (var index = 0;
                index < kids.Size;
                index++)
            {
                CleanFieldAdditionalActions(
                    kids[index],
                    selectedSourcePages,
                    pageNumbersByReference,
                    stringNamedDestinations,
                    nameNamedDestinations,
                    visitedFields,
                    depth + 1);
            }
        }

        private static void CleanAdditionalActions(
            PdfDictionary owner,
            ISet<int> selectedSourcePages,
            IDictionary<string, int> pageNumbersByReference,
            IDictionary<string, string> stringNamedDestinations,
            IDictionary<string, string> nameNamedDestinations)
        {
            var additionalActions =
                owner.GetAsDict(PdfName.AA);
            if (additionalActions == null)
            {
                return;
            }

            var keys =
                new PdfName[additionalActions.Size];
            additionalActions.Keys.CopyTo(keys, 0);
            foreach (var key in keys)
            {
                if (!CleanActionObject(
                        additionalActions.Get(key),
                        selectedSourcePages,
                        pageNumbersByReference,
                        stringNamedDestinations,
                        nameNamedDestinations))
                {
                    additionalActions.Remove(key);
                }
            }

            if (additionalActions.Size == 0)
            {
                owner.Remove(PdfName.AA);
            }
        }

        private static bool CleanActionObject(
            PdfObject actionObject,
            ISet<int> selectedSourcePages,
            IDictionary<string, int> pageNumbersByReference,
            IDictionary<string, string> stringNamedDestinations,
            IDictionary<string, string> nameNamedDestinations)
        {
            return CleanActionObject(
                actionObject,
                selectedSourcePages,
                pageNumbersByReference,
                stringNamedDestinations,
                nameNamedDestinations,
                new HashSet<string>(
                    StringComparer.Ordinal),
                0);
        }

        private static bool CleanActionObject(
            PdfObject actionObject,
            ISet<int> selectedSourcePages,
            IDictionary<string, int> pageNumbersByReference,
            IDictionary<string, string> stringNamedDestinations,
            IDictionary<string, string> nameNamedDestinations,
            ISet<string> visitedActions,
            int depth)
        {
            if (depth > 256)
            {
                throw new NotSupportedException(
                    ComplexActionDeletionUnsupportedMessage);
            }

            var reference =
                actionObject as PRIndirectReference;
            if (reference != null &&
                !visitedActions.Add(
                    GetReferenceKey(reference)))
            {
                throw new NotSupportedException(
                    ComplexActionDeletionUnsupportedMessage);
            }

            var resolved =
                PdfReader.GetPdfObject(actionObject);
            var action =
                resolved as PdfDictionary;
            if (action != null)
            {
                return CleanActionDictionary(
                    action,
                    selectedSourcePages,
                    pageNumbersByReference,
                    stringNamedDestinations,
                    nameNamedDestinations,
                    visitedActions,
                    depth);
            }

            var actions = resolved as PdfArray;
            if (actions == null)
            {
                return false;
            }

            for (var index = actions.Size - 1;
                index >= 0;
                index--)
            {
                if (!CleanActionObject(
                        actions[index],
                        selectedSourcePages,
                        pageNumbersByReference,
                        stringNamedDestinations,
                        nameNamedDestinations,
                        visitedActions,
                        depth + 1))
                {
                    actions.Remove(index);
                }
            }

            return actions.Size > 0;
        }

        private static bool CleanActionDictionary(
            PdfDictionary action,
            ISet<int> selectedSourcePages,
            IDictionary<string, int> pageNumbersByReference,
            IDictionary<string, string> stringNamedDestinations,
            IDictionary<string, string> nameNamedDestinations,
            ISet<string> visitedActions,
            int depth)
        {
            var actionType =
                action.GetAsName(PdfName.S);
            if ((actionType == null ||
                 PdfName.GOTO.Equals(actionType)) &&
                DestinationTargetsDeletedPage(
                    action.Get(PdfName.D),
                    selectedSourcePages,
                    pageNumbersByReference,
                    stringNamedDestinations,
                    nameNamedDestinations))
            {
                if (action.Get(PdfName.NEXT) != null)
                {
                    throw new NotSupportedException(
                        ComplexActionDeletionUnsupportedMessage);
                }

                return false;
            }

            if (action.Get(PdfName.NEXT) != null &&
                !CleanActionObject(
                    action.Get(PdfName.NEXT),
                    selectedSourcePages,
                    pageNumbersByReference,
                    stringNamedDestinations,
                    nameNamedDestinations,
                    visitedActions,
                    depth + 1))
            {
                action.Remove(PdfName.NEXT);
            }

            return true;
        }

        private static bool DestinationTargetsDeletedPage(
            PdfObject destinationObject,
            ISet<int> selectedSourcePages,
            IDictionary<string, int> pageNumbersByReference,
            IDictionary<string, string> stringNamedDestinations,
            IDictionary<string, string> nameNamedDestinations)
        {
            var resolved =
                PdfReader.GetPdfObject(destinationObject);
            var destination = resolved as PdfArray;
            int sourcePage;
            if (destination != null &&
                destination.Size > 0)
            {
                sourcePage = ResolveDestinationPage(
                    destination.GetPdfObject(0),
                    pageNumbersByReference);
                return sourcePage > 0 &&
                    !selectedSourcePages.Contains(
                        sourcePage);
            }

            var destinationDictionary =
                resolved as PdfDictionary;
            if (destinationDictionary != null)
            {
                return DestinationTargetsDeletedPage(
                    destinationDictionary.Get(PdfName.D),
                    selectedSourcePages,
                    pageNumbersByReference,
                    stringNamedDestinations,
                    nameNamedDestinations);
            }

            string mappedDestination;
            var destinationName = resolved as PdfName;
            if (destinationName != null &&
                TryResolveNamedDestination(
                    PdfName.DecodeName(
                        destinationName.ToString()),
                    nameNamedDestinations,
                    stringNamedDestinations,
                    true,
                    out mappedDestination) &&
                TryReadDestinationPage(
                    mappedDestination,
                    out sourcePage))
            {
                return !selectedSourcePages.Contains(
                    sourcePage);
            }

            var destinationString =
                resolved as PdfString;
            if (destinationString == null ||
                !TryResolveNamedDestination(
                    destinationString.ToString(),
                    stringNamedDestinations,
                    nameNamedDestinations,
                    false,
                    out mappedDestination) ||
                !TryReadDestinationPage(
                    mappedDestination,
                    out sourcePage))
            {
                return false;
            }

            return sourcePage > 0 &&
                !selectedSourcePages.Contains(sourcePage);
        }

        private static int ResolveDestinationPage(
            PdfObject target,
            IDictionary<string, int> pageNumbersByReference)
        {
            var reference = target as PRIndirectReference;
            int page;
            if (reference != null &&
                pageNumbersByReference.TryGetValue(
                    GetReferenceKey(reference),
                    out page))
            {
                return page;
            }

            var pageNumber = target as PdfNumber;
            return pageNumber == null
                ? 0
                : pageNumber.IntValue + 1;
        }

        private static string GetReferenceKey(
            PRIndirectReference reference)
        {
            return reference.Number.ToString(
                    CultureInfo.InvariantCulture) +
                ":" +
                reference.Generation.ToString(
                    CultureInfo.InvariantCulture);
        }

        private static PageLabelPlan PreparePageLabels(
            PdfReader reader,
            IList<PlannedPage> pages)
        {
            if (reader.Catalog.Get(PdfName.PAGELABELS) == null)
            {
                return new PageLabelPlan(
                    null,
                    null,
                    null);
            }

            var sourceRules =
                ReadPageLabelRules(reader);
            if (sourceRules.Count == 0)
            {
                throw new InvalidDataException(
                    "No se pudieron leer las reglas de etiquetas de pagina.");
            }

            var outputLabels = new PdfPageLabels();
            var expectedLabels =
                new string[pages.Count];
            var expectedRules =
                new PageLabelRule[pages.Count];
            for (var outputIndex = 0;
                outputIndex < pages.Count;
                outputIndex++)
            {
                var sourcePage =
                    pages[outputIndex].SourcePageNumber;
                var format = FindPageLabelRule(
                    sourceRules,
                    sourcePage);
                if (format == null)
                {
                    throw new InvalidDataException(
                        "Las etiquetas de pagina no definen la primera pagina.");
                }

                var logicalPage =
                    format.LogicalPage +
                    sourcePage -
                    format.PhysicalPage;
                outputLabels.AddPageLabel(
                    outputIndex + 1,
                    format.NumberStyle,
                    format.HasPrefix
                        ? format.Prefix
                        : null,
                    logicalPage);
                expectedRules[outputIndex] =
                    new PageLabelRule(
                        outputIndex + 1,
                        format.NumberStyle,
                        format.Prefix,
                        format.HasPrefix,
                        logicalPage);
                expectedLabels[outputIndex] =
                    RenderPageLabel(
                        format.NumberStyle,
                        format.Prefix,
                        logicalPage);
            }

            return new PageLabelPlan(
                outputLabels,
                expectedLabels,
                expectedRules);
        }

        private static List<PageLabelRule>
            ReadPageLabelRules(PdfReader reader)
        {
            var pageLabels =
                reader.Catalog.GetAsDict(
                    PdfName.PAGELABELS);
            var tree = pageLabels == null
                ? null
                : PdfNumberTree.ReadTree(
                    pageLabels);
            var result =
                new List<PageLabelRule>();
            if (tree == null || tree.Count == 0)
            {
                return result;
            }

            var pageIndexes =
                new int[tree.Count];
            tree.Keys.CopyTo(pageIndexes, 0);
            Array.Sort(pageIndexes);
            foreach (var pageIndex in pageIndexes)
            {
                PdfObject ruleObject;
                if (!tree.TryGetValue(
                        pageIndex,
                        out ruleObject))
                {
                    continue;
                }

                var rule = PdfReader.GetPdfObject(
                    ruleObject) as PdfDictionary;
                if (rule == null)
                {
                    throw new InvalidDataException(
                        "El PDF contiene una regla de etiqueta no valida.");
                }

                var prefixObject =
                    rule.Get(PdfName.P);
                var prefix = prefixObject == null
                    ? null
                    : PdfReader.GetPdfObject(
                        prefixObject) as PdfString;
                if (prefixObject != null &&
                    prefix == null)
                {
                    throw new InvalidDataException(
                        "El PDF contiene un prefijo de etiqueta no valido.");
                }

                var start =
                    rule.GetAsNumber(PdfName.ST);
                var logicalPage = start == null
                    ? 1
                    : start.IntValue;
                if (logicalPage < 1)
                {
                    throw new InvalidDataException(
                        "El PDF contiene un inicio de etiqueta no valido.");
                }

                result.Add(
                    new PageLabelRule(
                        pageIndex + 1,
                        ReadPageLabelNumberStyle(rule),
                        prefix == null
                            ? null
                            : prefix.ToUnicodeString(),
                        prefixObject != null,
                        logicalPage));
            }

            return result;
        }

        private static int ReadPageLabelNumberStyle(
            PdfDictionary rule)
        {
            var style = rule.GetAsName(PdfName.S);
            if (style == null)
            {
                return PdfPageLabels.EMPTY;
            }

            if (PdfName.D.Equals(style))
            {
                return PdfPageLabels.DECIMAL_ARABIC_NUMERALS;
            }

            if (PdfName.R.Equals(style))
            {
                return PdfPageLabels.UPPERCASE_ROMAN_NUMERALS;
            }

            if (new PdfName("r").Equals(style))
            {
                return PdfPageLabels.LOWERCASE_ROMAN_NUMERALS;
            }

            if (PdfName.A.Equals(style))
            {
                return PdfPageLabels.UPPERCASE_LETTERS;
            }

            if (new PdfName("a").Equals(style))
            {
                return PdfPageLabels.LOWERCASE_LETTERS;
            }

            throw new InvalidDataException(
                "El PDF contiene un estilo de etiqueta de pagina desconocido.");
        }

        private static PageLabelRule FindPageLabelRule(
                IList<PageLabelRule> formats,
                int sourcePage)
        {
            PageLabelRule result = null;
            for (var index = 0;
                index < formats.Count;
                index++)
            {
                var candidate = formats[index];
                if (candidate == null ||
                    candidate.PhysicalPage > sourcePage)
                {
                    continue;
                }

                if (result == null ||
                    candidate.PhysicalPage >
                        result.PhysicalPage)
                {
                    result = candidate;
                }
            }

            return result;
        }

        private static string RenderPageLabel(
            int numberStyle,
            string prefix,
            int logicalPage)
        {
            var safePrefix = prefix ?? string.Empty;
            switch (numberStyle)
            {
                case PdfPageLabels.DECIMAL_ARABIC_NUMERALS:
                    return safePrefix +
                        logicalPage.ToString(
                            CultureInfo.InvariantCulture);
                case PdfPageLabels.UPPERCASE_ROMAN_NUMERALS:
                    return safePrefix +
                        RomanNumberFactory.GetUpperCaseString(
                            logicalPage);
                case PdfPageLabels.LOWERCASE_ROMAN_NUMERALS:
                    return safePrefix +
                        RomanNumberFactory.GetLowerCaseString(
                            logicalPage);
                case PdfPageLabels.UPPERCASE_LETTERS:
                    return safePrefix +
                        RomanAlphabetFactory.GetUpperCaseString(
                            logicalPage);
                case PdfPageLabels.LOWERCASE_LETTERS:
                    return safePrefix +
                        RomanAlphabetFactory.GetLowerCaseString(
                            logicalPage);
                case PdfPageLabels.EMPTY:
                    return safePrefix;
                default:
                    throw new InvalidDataException(
                        "El PDF contiene un estilo de etiqueta de pagina " +
                        "desconocido.");
            }
        }

        private static Dictionary<string, string> CloneMetadata(
            IDictionary<string, string> source)
        {
            var result =
                new Dictionary<string, string>(
                    StringComparer.Ordinal);
            if (source == null)
            {
                return result;
            }

            foreach (var entry in source)
            {
                result[entry.Key] = entry.Value;
            }

            return result;
        }

        private static Dictionary<string, string>
            GetVerifiableMetadata(
                IDictionary<string, string> metadata)
        {
            var result =
                new Dictionary<string, string>(
                    StringComparer.Ordinal);
            if (metadata == null)
            {
                return result;
            }

            var keys = new[]
            {
                "Title",
                "Author",
                "Subject",
                "Keywords",
                "Creator"
            };
            foreach (var key in keys)
            {
                string value;
                if (metadata.TryGetValue(key, out value) &&
                    value != null)
                {
                    result[key] = value;
                }
            }

            return result;
        }

        private static int CountSelectedFormFields(
            PdfReader reader,
            ISet<int> selectedPages)
        {
            if (reader.AcroFields == null)
            {
                return 0;
            }

            var count = 0;
            foreach (var entry in reader.AcroFields.Fields)
            {
                var item = entry.Value;
                var retained = false;
                for (var index = 0;
                    index < item.Size;
                    index++)
                {
                    if (selectedPages.Contains(
                            item.GetPage(index)))
                    {
                        retained = true;
                        break;
                    }
                }

                if (retained)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountSelectedFormWidgets(
            PdfReader reader,
            ISet<int> selectedPages)
        {
            var count = 0;
            foreach (var page in selectedPages)
            {
                var pageDictionary = reader.GetPageN(page);
                var annotations = pageDictionary == null
                    ? null
                    : pageDictionary.GetAsArray(PdfName.ANNOTS);
                if (annotations == null)
                {
                    continue;
                }

                for (var index = 0;
                    index < annotations.Size;
                    index++)
                {
                    var annotation = PdfReader.GetPdfObject(
                        annotations[index]) as PdfDictionary;
                    if (annotation != null &&
                        PdfName.WIDGET.Equals(
                            annotation.GetAsName(
                                PdfName.SUBTYPE)))
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private static void CleanAcroFormCalculationOrder(
            PdfReader reader)
        {
            var acroForm =
                reader.Catalog.GetAsDict(
                    PdfName.ACROFORM);
            var calculationOrder = acroForm == null
                ? null
                : acroForm.GetAsArray(PdfName.CO);
            if (calculationOrder == null)
            {
                return;
            }

            HashSet<string> fieldReferences;
            HashSet<PdfDictionary> fieldDictionaries;
            ReadReachableFormFields(
                acroForm,
                out fieldReferences,
                out fieldDictionaries);
            for (var index = calculationOrder.Size - 1;
                index >= 0;
                index--)
            {
                if (!IsReachableFormField(
                        calculationOrder[index],
                        fieldReferences,
                        fieldDictionaries))
                {
                    calculationOrder.Remove(index);
                }
            }

            if (calculationOrder.Size == 0)
            {
                acroForm.Remove(PdfName.CO);
            }
        }

        private static void ValidateAcroFormCalculationOrder(
            PdfReader reader)
        {
            var acroForm =
                reader.Catalog.GetAsDict(
                    PdfName.ACROFORM);
            var calculationOrder = acroForm == null
                ? null
                : acroForm.GetAsArray(PdfName.CO);
            if (calculationOrder == null)
            {
                return;
            }

            HashSet<string> fieldReferences;
            HashSet<PdfDictionary> fieldDictionaries;
            ReadReachableFormFields(
                acroForm,
                out fieldReferences,
                out fieldDictionaries);
            for (var index = 0;
                index < calculationOrder.Size;
                index++)
            {
                if (!IsReachableFormField(
                        calculationOrder[index],
                        fieldReferences,
                        fieldDictionaries))
                {
                    throw new InvalidDataException(
                        "La comprobacion final encontro una referencia " +
                        "invalida en el orden de calculo del formulario.");
                }
            }
        }

        private static void ReadReachableFormFields(
            PdfDictionary acroForm,
            out HashSet<string> fieldReferences,
            out HashSet<PdfDictionary> fieldDictionaries)
        {
            fieldReferences =
                new HashSet<string>(
                    StringComparer.Ordinal);
            fieldDictionaries =
                new HashSet<PdfDictionary>();
            var fields =
                acroForm.GetAsArray(PdfName.FIELDS);
            if (fields == null)
            {
                return;
            }

            for (var index = 0;
                index < fields.Size;
                index++)
            {
                AddReachableFormField(
                    fields[index],
                    fieldReferences,
                    fieldDictionaries,
                    0);
            }
        }

        private static void AddReachableFormField(
            PdfObject fieldObject,
            ISet<string> fieldReferences,
            ISet<PdfDictionary> fieldDictionaries,
            int depth)
        {
            if (fieldObject == null || depth > 256)
            {
                return;
            }

            var reference =
                fieldObject as PRIndirectReference;
            if (reference != null &&
                !fieldReferences.Add(
                    GetReferenceKey(reference)))
            {
                return;
            }

            var field = PdfReader.GetPdfObject(
                fieldObject) as PdfDictionary;
            if (field == null ||
                !fieldDictionaries.Add(field))
            {
                return;
            }

            var kids = field.GetAsArray(PdfName.KIDS);
            if (kids == null)
            {
                return;
            }

            for (var index = 0;
                index < kids.Size;
                index++)
            {
                AddReachableFormField(
                    kids[index],
                    fieldReferences,
                    fieldDictionaries,
                    depth + 1);
            }
        }

        private static bool IsReachableFormField(
            PdfObject fieldObject,
            ISet<string> fieldReferences,
            ISet<PdfDictionary> fieldDictionaries)
        {
            var reference =
                fieldObject as PRIndirectReference;
            if (reference != null)
            {
                return fieldReferences.Contains(
                    GetReferenceKey(reference));
            }

            var field = PdfReader.GetPdfObject(
                fieldObject) as PdfDictionary;
            return field != null &&
                fieldDictionaries.Contains(field);
        }

        private static void ValidateWrittenPdf(
            string path,
            OrganizationPlan plan,
            OutputExpectations expectations)
        {
            PdfReader reader = null;
            try
            {
                reader = OpenPdfReader(path);
                if (reader.NumberOfPages != plan.Pages.Count)
                {
                    throw new InvalidDataException(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "La comprobacion final esperaba {0} paginas y " +
                            "encontro {1}.",
                            plan.Pages.Count,
                            reader.NumberOfPages));
                }

                var actualAnnotationCount = 0;
                for (var outputIndex = 0;
                    outputIndex < plan.Pages.Count;
                    outputIndex++)
                {
                    var pageNumber = outputIndex + 1;
                    var expectedPage = plan.Pages[outputIndex];
                    var pageDictionary =
                        reader.GetPageN(pageNumber);
                    var mediaBox =
                        reader.GetPageSize(pageNumber);
                    var cropBox =
                        reader.GetCropBox(pageNumber);
                    if (pageDictionary == null ||
                        mediaBox == null ||
                        mediaBox.Width <= 0 ||
                        mediaBox.Height <= 0)
                    {
                        throw new InvalidDataException(
                            "La comprobacion final encontro una pagina no valida.");
                    }

                    if (!NearlyEqual(
                            mediaBox.Width,
                            expectedPage.MediaWidth) ||
                        !NearlyEqual(
                            mediaBox.Height,
                            expectedPage.MediaHeight) ||
                        (cropBox != null &&
                         (!NearlyEqual(
                              cropBox.Width,
                              expectedPage.CropWidth) ||
                          !NearlyEqual(
                              cropBox.Height,
                              expectedPage.CropHeight))))
                    {
                        throw new InvalidDataException(
                            "La comprobacion final encontro una pagina con " +
                            "medidas distintas de las esperadas.");
                    }

                    var actualRotation = NormalizeRotation(
                        reader.GetPageRotation(pageNumber));
                    if (actualRotation !=
                        expectedPage.OutputRotation)
                    {
                        throw new InvalidDataException(
                            "La comprobacion final encontro un giro de pagina " +
                            "distinto del solicitado.");
                    }

                    var annotations =
                        pageDictionary.GetAsArray(PdfName.ANNOTS);
                    var pageAnnotationCount =
                        annotations == null ? 0 : annotations.Size;
                    if (pageAnnotationCount <
                        expectedPage.AnnotationCount)
                    {
                        throw new InvalidDataException(
                            "La comprobacion final no encontro todas las " +
                            "anotaciones esperadas.");
                    }

                    actualAnnotationCount +=
                        pageAnnotationCount;
                }

                var actualFormFieldCount =
                    reader.AcroFields == null
                        ? 0
                        : reader.AcroFields.Fields.Count;
                if (actualFormFieldCount <
                    plan.ExpectedFormFieldCount)
                {
                    throw new InvalidDataException(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "La comprobacion final esperaba al menos {0} " +
                            "campos de formulario y encontro {1}.",
                            plan.ExpectedFormFieldCount,
                            actualFormFieldCount));
                }

                var actualFormWidgetCount =
                    CountAllFormWidgets(reader);
                if (actualFormWidgetCount <
                    plan.ExpectedFormWidgetCount)
                {
                    throw new InvalidDataException(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "La comprobacion final esperaba al menos {0} " +
                            "controles de formulario y encontro {1}.",
                            plan.ExpectedFormWidgetCount,
                            actualFormWidgetCount));
                }

                ValidateAcroFormCalculationOrder(reader);

                if (expectations.BookmarkCount > 0)
                {
                    var bookmarks =
                        SimpleBookmark.GetBookmark(reader);
                    if (CountBookmarks(bookmarks) <
                        expectations.BookmarkCount)
                    {
                        throw new InvalidDataException(
                            "La comprobacion final no encontro todos los " +
                            "marcadores ajustados.");
                    }
                }

                ValidateNamedDestinations(
                    SimpleNamedDestination.GetNamedDestination(
                        reader,
                        false),
                    expectations.StringNamedDestinations,
                    "arbol /Names/Dests");
                ValidateNamedDestinations(
                    SimpleNamedDestination.GetNamedDestination(
                        reader,
                        true),
                    expectations.NameNamedDestinations,
                    "diccionario /Catalog/Dests");

                ValidateMetadata(
                    reader.Info,
                    expectations.VerifiableMetadata);
                ValidatePageLabels(
                    reader,
                    expectations.ExpectedPageLabels,
                    expectations.ExpectedPageLabelRules);
            }
            finally
            {
                if (reader != null)
                {
                    reader.Close();
                }
            }
        }

        private static void ValidateMetadata(
            IDictionary<string, string> actual,
            IDictionary<string, string> expected)
        {
            if (expected == null || expected.Count == 0)
            {
                return;
            }

            foreach (var entry in expected)
            {
                string value;
                if (actual == null ||
                    !actual.TryGetValue(entry.Key, out value) ||
                    !string.Equals(
                        value,
                        entry.Value,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "La comprobacion final no pudo conservar el metadato " +
                        entry.Key + ".");
                }
            }
        }

        private static void ValidateNamedDestinations(
            IDictionary<string, string> actual,
            IDictionary<string, string> expected,
            string storageName)
        {
            var expectedCount = expected == null
                ? 0
                : expected.Count;
            var actualCount = actual == null
                ? 0
                : actual.Count;
            if (actualCount != expectedCount)
            {
                throw new InvalidDataException(
                    "La comprobacion final encontro " +
                    actualCount.ToString(
                        CultureInfo.InvariantCulture) +
                    " destinos en " + storageName +
                    " y esperaba " +
                    expectedCount.ToString(
                        CultureInfo.InvariantCulture) + ".");
            }

            if (expected == null)
            {
                return;
            }

            foreach (var entry in expected)
            {
                string actualDestination;
                if (actual == null ||
                    !actual.TryGetValue(
                        entry.Key,
                        out actualDestination) ||
                    !string.Equals(
                        actualDestination,
                        entry.Value,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "La comprobacion final no pudo conservar el destino " +
                        entry.Key + " en " + storageName + ".");
                }
            }
        }

        private static void ValidatePageLabels(
            PdfReader reader,
            string[] expectedLabels,
            PageLabelRule[] expectedRules)
        {
            if (expectedLabels == null)
            {
                return;
            }

            var actualLabels =
                RenderPageLabels(reader);
            if (actualLabels == null ||
                actualLabels.Length !=
                    expectedLabels.Length)
            {
                throw new InvalidDataException(
                    "La comprobacion final no encontro las etiquetas de pagina.");
            }

            for (var index = 0;
                index < expectedLabels.Length;
                index++)
            {
                if (!string.Equals(
                        actualLabels[index],
                        expectedLabels[index],
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "La comprobacion final encontro una etiqueta de pagina " +
                        "distinta de la esperada.");
                }
            }

            var actualRules =
                ReadPageLabelRules(reader);
            if (expectedRules == null ||
                actualRules.Count !=
                    expectedRules.Length)
            {
                throw new InvalidDataException(
                    "La comprobacion final encontro reglas de etiquetas " +
                    "distintas de las esperadas.");
            }

            for (var index = 0;
                index < expectedRules.Length;
                index++)
            {
                if (!actualRules[index].IsEquivalentTo(
                        expectedRules[index]))
                {
                    throw new InvalidDataException(
                        "La comprobacion final no pudo conservar una regla " +
                        "de etiquetas de pagina.");
                }
            }
        }

        private static string[] RenderPageLabels(
            PdfReader reader)
        {
            var rules = ReadPageLabelRules(reader);
            if (rules.Count == 0)
            {
                return null;
            }

            var labels =
                new string[reader.NumberOfPages];
            for (var page = 1;
                page <= reader.NumberOfPages;
                page++)
            {
                var rule =
                    FindPageLabelRule(rules, page);
                if (rule == null)
                {
                    throw new InvalidDataException(
                        "Las etiquetas de pagina no definen la primera pagina.");
                }

                labels[page - 1] =
                    RenderPageLabel(
                        rule.NumberStyle,
                        rule.Prefix,
                        rule.LogicalPage +
                            page -
                            rule.PhysicalPage);
            }

            return labels;
        }

        private static int CountAllFormWidgets(PdfReader reader)
        {
            var count = 0;
            for (var page = 1;
                page <= reader.NumberOfPages;
                page++)
            {
                var pageDictionary = reader.GetPageN(page);
                var annotations = pageDictionary == null
                    ? null
                    : pageDictionary.GetAsArray(PdfName.ANNOTS);
                if (annotations == null)
                {
                    continue;
                }

                for (var index = 0;
                    index < annotations.Size;
                    index++)
                {
                    var annotation = PdfReader.GetPdfObject(
                        annotations[index]) as PdfDictionary;
                    if (annotation != null &&
                        PdfName.WIDGET.Equals(
                            annotation.GetAsName(
                                PdfName.SUBTYPE)))
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private static int CountBookmarks(
            IList<Dictionary<string, object>> bookmarks)
        {
            if (bookmarks == null)
            {
                return 0;
            }

            var count = 0;
            foreach (var bookmark in bookmarks)
            {
                if (bookmark == null)
                {
                    continue;
                }

                count++;
                object kidsValue;
                var kids =
                    bookmark.TryGetValue("Kids", out kidsValue)
                        ? kidsValue as
                            IList<Dictionary<string, object>>
                        : null;
                count += CountBookmarks(kids);
            }

            return count;
        }

        private static bool NearlyEqual(float left, float right)
        {
            return Math.Abs(left - right) <= 0.1F;
        }

        private static FileStream OpenSourceReadGuard(
            string path)
        {
            return new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.RandomAccess);
        }

        private static PdfReader OpenPdfReader(
            string path)
        {
            // Partial mode keeps only the xref and objects currently needed in
            // managed memory. SelectPages remains structural, although it may
            // need to visit all page references once.
            return new PdfReader(
                path,
                (byte[])null,
                true);
        }

        private static string NormalizeExistingPdfPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new FileNotFoundException(
                    "No se encuentra el PDF que se quiere organizar.",
                    path);
            }

            var normalizedPath = Path.GetFullPath(path);
            if (!File.Exists(normalizedPath))
            {
                throw new FileNotFoundException(
                    "No se encuentra el PDF que se quiere organizar.",
                    normalizedPath);
            }

            if (!string.Equals(
                    Path.GetExtension(normalizedPath),
                    ".pdf",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "El archivo no es un PDF: " +
                    Path.GetFileName(normalizedPath));
            }

            return normalizedPath;
        }

        private static string ValidateOutputPath(
            string sourcePath,
            string outputPath)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new InvalidOperationException(
                    "Selecciona donde guardar la copia organizada.");
            }

            if (!string.Equals(
                    Path.GetExtension(outputPath),
                    ".pdf",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "El archivo de salida debe tener extension .pdf.");
            }

            var normalizedOutputPath =
                Path.GetFullPath(outputPath);
            var outputDirectory =
                Path.GetDirectoryName(normalizedOutputPath);
            if (string.IsNullOrWhiteSpace(outputDirectory) ||
                !Directory.Exists(outputDirectory))
            {
                throw new DirectoryNotFoundException(
                    "La carpeta de destino ya no existe.");
            }

            if (string.Equals(
                    normalizedOutputPath,
                    sourcePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "La organizacion se guarda en una copia; el PDF original " +
                    "no se puede sobrescribir.");
            }

            if (File.Exists(normalizedOutputPath))
            {
                throw new IOException(
                    "Ya existe el PDF de salida. Elige otro nombre para no " +
                    "sobrescribirlo.");
            }

            return normalizedOutputPath;
        }

        private static int NormalizeRequestedRotation(int rotation)
        {
            if (rotation % 90 != 0)
            {
                throw new ArgumentOutOfRangeException(
                    "rotation",
                    "El giro de pagina debe ser un multiplo de 90 grados.");
            }

            return NormalizeRotation(rotation);
        }

        private static int NormalizeRotation(int rotation)
        {
            var normalized = rotation % 360;
            return normalized < 0
                ? normalized + 360
                : normalized;
        }

        private sealed class OrganizationPlan
        {
            public OrganizationPlan(
                string sourcePath,
                int sourcePageCount,
                IList<PlannedPage> pages,
                ISet<int> selectedSourcePages,
                int rotatedPageCount,
                int expectedFormFieldCount,
                int expectedFormWidgetCount,
                bool containsDigitalSignatures)
            {
                SourcePath = sourcePath;
                SourcePageCount = sourcePageCount;
                Pages = pages;
                SelectedSourcePages = selectedSourcePages;
                RotatedPageCount = rotatedPageCount;
                ExpectedFormFieldCount = expectedFormFieldCount;
                ExpectedFormWidgetCount = expectedFormWidgetCount;
                ContainsDigitalSignatures =
                    containsDigitalSignatures;

                var pageNumbers = new List<int>(pages.Count);
                var outputPageBySourcePage =
                    new Dictionary<int, int>();
                for (var index = 0;
                    index < pages.Count;
                    index++)
                {
                    var sourcePage =
                        pages[index].SourcePageNumber;
                    pageNumbers.Add(sourcePage);
                    outputPageBySourcePage[sourcePage] =
                        index + 1;
                }

                SourcePageNumbers = pageNumbers;
                OutputPageBySourcePage =
                    outputPageBySourcePage;
            }

            public string SourcePath { get; private set; }

            public int SourcePageCount { get; private set; }

            public IList<PlannedPage> Pages { get; private set; }

            public ICollection<int> SourcePageNumbers
            {
                get;
                private set;
            }

            public ISet<int> SelectedSourcePages
            {
                get;
                private set;
            }

            public IDictionary<int, int>
                OutputPageBySourcePage
            {
                get;
                private set;
            }

            public int RotatedPageCount { get; private set; }

            public int ExpectedFormFieldCount { get; private set; }

            public int ExpectedFormWidgetCount { get; private set; }

            public bool ContainsDigitalSignatures { get; private set; }

            public bool RemovesPages
            {
                get { return Pages.Count < SourcePageCount; }
            }
        }

        private sealed class PlannedPage
        {
            public PlannedPage(
                int sourcePageNumber,
                int rotationDelta,
                int outputRotation,
                float mediaWidth,
                float mediaHeight,
                float cropWidth,
                float cropHeight,
                int annotationCount)
            {
                SourcePageNumber = sourcePageNumber;
                RotationDelta = rotationDelta;
                OutputRotation = outputRotation;
                MediaWidth = mediaWidth;
                MediaHeight = mediaHeight;
                CropWidth = cropWidth;
                CropHeight = cropHeight;
                AnnotationCount = annotationCount;
            }

            public int SourcePageNumber { get; private set; }

            public int RotationDelta { get; private set; }

            public int OutputRotation { get; private set; }

            public float MediaWidth { get; private set; }

            public float MediaHeight { get; private set; }

            public float CropWidth { get; private set; }

            public float CropHeight { get; private set; }

            public int AnnotationCount { get; private set; }
        }

        private sealed class PageLabelPlan
        {
            public PageLabelPlan(
                PdfPageLabels labels,
                string[] expectedLabels,
                PageLabelRule[] expectedRules)
            {
                Labels = labels;
                ExpectedLabels = expectedLabels;
                ExpectedRules = expectedRules;
            }

            public PdfPageLabels Labels { get; private set; }

            public string[] ExpectedLabels { get; private set; }

            public PageLabelRule[] ExpectedRules { get; private set; }
        }

        private sealed class PageLabelRule
        {
            public PageLabelRule(
                int physicalPage,
                int numberStyle,
                string prefix,
                bool hasPrefix,
                int logicalPage)
            {
                PhysicalPage = physicalPage;
                NumberStyle = numberStyle;
                Prefix = prefix;
                HasPrefix = hasPrefix;
                LogicalPage = logicalPage;
            }

            public int PhysicalPage { get; private set; }

            public int NumberStyle { get; private set; }

            public string Prefix { get; private set; }

            public bool HasPrefix { get; private set; }

            public int LogicalPage { get; private set; }

            public bool IsEquivalentTo(
                PageLabelRule other)
            {
                return other != null &&
                    PhysicalPage == other.PhysicalPage &&
                    NumberStyle == other.NumberStyle &&
                    HasPrefix == other.HasPrefix &&
                    LogicalPage == other.LogicalPage &&
                    string.Equals(
                        Prefix,
                        other.Prefix,
                        StringComparison.Ordinal);
            }
        }

        private sealed class OutputExpectations
        {
            public OutputExpectations(
                int bookmarkCount,
                IDictionary<string, string> stringNamedDestinations,
                IDictionary<string, string> nameNamedDestinations,
                IDictionary<string, string> verifiableMetadata,
                string[] expectedPageLabels,
                PageLabelRule[] expectedPageLabelRules)
            {
                BookmarkCount = bookmarkCount;
                StringNamedDestinations =
                    stringNamedDestinations;
                NameNamedDestinations =
                    nameNamedDestinations;
                VerifiableMetadata = verifiableMetadata;
                ExpectedPageLabels = expectedPageLabels;
                ExpectedPageLabelRules =
                    expectedPageLabelRules;
            }

            public int BookmarkCount { get; private set; }

            public IDictionary<string, string>
                StringNamedDestinations
            {
                get;
                private set;
            }

            public IDictionary<string, string>
                NameNamedDestinations
            {
                get;
                private set;
            }

            public IDictionary<string, string> VerifiableMetadata
            {
                get;
                private set;
            }

            public string[] ExpectedPageLabels { get; private set; }

            public PageLabelRule[] ExpectedPageLabelRules
            {
                get;
                private set;
            }
        }

        private sealed class ThrottledProgressReporter
        {
            private const int MinimumIntervalMilliseconds = 100;
            private readonly Action<PdfPageOrganizerProgress> callback;
            private readonly Stopwatch stopwatch;
            private int lastCompletedStep;
            private string lastStage;

            public ThrottledProgressReporter(
                Action<PdfPageOrganizerProgress> callback)
            {
                this.callback = callback;
                stopwatch = Stopwatch.StartNew();
                lastCompletedStep = -1;
                lastStage = null;
            }

            public void Report(
                int completedSteps,
                int totalSteps,
                int processedPages,
                int totalPages,
                string stage,
                bool force)
            {
                if (callback == null)
                {
                    return;
                }

                var stageChanged = !string.Equals(
                    stage,
                    lastStage,
                    StringComparison.Ordinal);
                if (!force &&
                    !stageChanged &&
                    stopwatch.ElapsedMilliseconds <
                    MinimumIntervalMilliseconds)
                {
                    return;
                }

                if (!force &&
                    !stageChanged &&
                    completedSteps == lastCompletedStep)
                {
                    return;
                }

                callback(
                    new PdfPageOrganizerProgress(
                        completedSteps,
                        totalSteps,
                        processedPages,
                        totalPages,
                        stage));
                lastCompletedStep = completedSteps;
                lastStage = stage;
                stopwatch.Restart();
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using iTextSharp.text;
using iTextSharp.text.pdf;

namespace FirmaAutomatica
{
    internal sealed class PdfPageInsertProgress
    {
        public PdfPageInsertProgress(
            int completedPages,
            int totalPages,
            string sourcePath)
        {
            CompletedPages = completedPages;
            TotalPages = totalPages;
            SourcePath = sourcePath;
        }

        public int CompletedPages { get; private set; }

        public int TotalPages { get; private set; }

        public string SourcePath { get; private set; }
    }

    internal sealed class PdfPageInsertAnalysis
    {
        public PdfPageInsertAnalysis(
            int basePageCount,
            int insertedPageCount,
            bool containsDigitalSignatures)
        {
            BasePageCount = basePageCount;
            InsertedPageCount = insertedPageCount;
            ContainsDigitalSignatures = containsDigitalSignatures;
        }

        public int BasePageCount { get; private set; }

        public int InsertedPageCount { get; private set; }

        public int ResultPageCount
        {
            get { return BasePageCount + InsertedPageCount; }
        }

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
                    ? PdfPageInsertService.DigitalSignatureInvalidationWarning
                    : string.Empty;
            }
        }
    }

    internal sealed class PdfPageInsertResult
    {
        public PdfPageInsertResult(
            string outputPath,
            int pageCount,
            int insertionIndex,
            int insertedPageCount,
            bool digitalSignaturesInvalidated)
        {
            OutputPath = outputPath;
            PageCount = pageCount;
            InsertionIndex = insertionIndex;
            InsertedPageCount = insertedPageCount;
            DigitalSignaturesInvalidated = digitalSignaturesInvalidated;
        }

        public string OutputPath { get; private set; }

        public int PageCount { get; private set; }

        public int InsertionIndex { get; private set; }

        public int InsertedPageCount { get; private set; }

        public bool DigitalSignaturesInvalidated { get; private set; }

        public string DigitalSignatureWarning
        {
            get
            {
                return DigitalSignaturesInvalidated
                    ? PdfPageInsertService.DigitalSignatureInvalidationWarning
                    : string.Empty;
            }
        }
    }

    /// <summary>
    /// Inserts complete PDF documents before a zero-based page position in a base
    /// PDF. Pages are imported as PDF objects; they are never rendered to bitmaps
    /// or recompressed.
    /// </summary>
    internal static class PdfPageInsertService
    {
        public const string DigitalSignatureInvalidationWarning =
            "La copia editada ya no conserva la validez criptografica de las " +
            "firmas digitales de los documentos originales.";

        public const string XfaUnsupportedMessage =
            "Los formularios XFA no se pueden insertar de forma segura. " +
            "Guarda antes una copia PDF normal del formulario.";

        public static string SuggestOutputPath(string basePdfPath)
        {
            var normalizedBasePath = NormalizeExistingPdfPath(
                basePdfPath,
                "No se encuentra el PDF base.");
            var directory = Path.GetDirectoryName(normalizedBasePath);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException(
                    "La carpeta del PDF base ya no existe.");
            }

            var baseName = Path.GetFileNameWithoutExtension(normalizedBasePath);
            int suffix;
            string editedBaseName;
            SplitEditedName(baseName, out editedBaseName, out suffix);
            var candidate = suffix == 0
                ? Path.Combine(directory, editedBaseName + ".pdf")
                : Path.Combine(
                    directory,
                    editedBaseName + "_" +
                    suffix.ToString(CultureInfo.InvariantCulture) + ".pdf");
            suffix = suffix == 0 ? 2 : suffix + 1;
            while (File.Exists(candidate))
            {
                candidate = Path.Combine(
                    directory,
                    editedBaseName + "_" +
                    suffix.ToString(CultureInfo.InvariantCulture) + ".pdf");
                suffix++;
            }

            return candidate;
        }

        public static PdfPageInsertAnalysis Analyze(
            string basePdfPath,
            IList<string> insertedPdfPaths,
            int insertBeforePageIndex)
        {
            var plan = CreatePlan(
                basePdfPath,
                insertedPdfPaths,
                insertBeforePageIndex);
            return new PdfPageInsertAnalysis(
                plan.BaseSource.PageCount,
                plan.InsertedPageCount,
                plan.ContainsDigitalSignatures);
        }

        public static PdfPageInsertResult Insert(
            string basePdfPath,
            IList<string> insertedPdfPaths,
            int insertBeforePageIndex,
            Action<PdfPageInsertProgress> reportProgress)
        {
            var outputPath = SuggestOutputPath(basePdfPath);
            return Insert(
                basePdfPath,
                insertedPdfPaths,
                insertBeforePageIndex,
                outputPath,
                reportProgress);
        }

        public static PdfPageInsertResult Insert(
            string basePdfPath,
            IList<string> insertedPdfPaths,
            int insertBeforePageIndex,
            string outputPath,
            Action<PdfPageInsertProgress> reportProgress)
        {
            var plan = CreatePlan(
                basePdfPath,
                insertedPdfPaths,
                insertBeforePageIndex);
            var normalizedOutputPath = ValidateOutputPath(plan, outputPath);
            var outputDirectory = Path.GetDirectoryName(normalizedOutputPath);
            var tempPath = Path.Combine(
                outputDirectory,
                "." + Path.GetFileNameWithoutExtension(normalizedOutputPath) +
                "." + Guid.NewGuid().ToString("N") + ".tmp");

            try
            {
                var outputStructure = WriteInsertedPdf(
                    plan,
                    normalizedOutputPath,
                    tempPath,
                    reportProgress);
                ValidateWrittenPdf(
                    tempPath,
                    plan.ResultPageCount,
                    outputStructure.HasBookmarks,
                    outputStructure.HasNamedDestinations,
                    plan.ExpectedFormFieldCount,
                    plan.ExpectedFormWidgetCount);
                CommitTemporaryFile(tempPath, normalizedOutputPath);

                return new PdfPageInsertResult(
                    normalizedOutputPath,
                    plan.ResultPageCount,
                    plan.InsertionIndex,
                    plan.InsertedPageCount,
                    plan.ContainsDigitalSignatures);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static InsertionPlan CreatePlan(
            string basePdfPath,
            IList<string> insertedPdfPaths,
            int insertionIndex)
        {
            var normalizedBasePath = NormalizeExistingPdfPath(
                basePdfPath,
                "No se encuentra el PDF base.");
            if (insertedPdfPaths == null || insertedPdfPaths.Count == 0)
            {
                throw new InvalidOperationException(
                    "Selecciona al menos un PDF para insertarlo.");
            }

            var baseSource = ReadSourceInfo(normalizedBasePath);
            if (insertionIndex < 0 || insertionIndex > baseSource.PageCount)
            {
                throw new ArgumentOutOfRangeException(
                    "insertionIndex",
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "La posicion de insercion debe estar entre 0 y {0}.",
                        baseSource.PageCount));
            }

            var insertedSources = new List<SourceInfo>();
            var insertedPageCount = 0;
            var containsDigitalSignatures = baseSource.ContainsDigitalSignatures;
            var hasFormFields = baseSource.HasFormFields;

            foreach (var insertedPdfPath in insertedPdfPaths)
            {
                var normalizedInsertedPath = NormalizeExistingPdfPath(
                    insertedPdfPath,
                    "No se encuentra uno de los PDFs que se van a insertar.");
                var insertedSource = ReadSourceInfo(normalizedInsertedPath);
                insertedSources.Add(insertedSource);
                insertedPageCount += insertedSource.PageCount;
                containsDigitalSignatures =
                    containsDigitalSignatures ||
                    insertedSource.ContainsDigitalSignatures;
                hasFormFields = hasFormFields || insertedSource.HasFormFields;
            }

            return new InsertionPlan(
                baseSource,
                insertedSources,
                insertionIndex,
                insertedPageCount,
                containsDigitalSignatures,
                hasFormFields);
        }

        private static SourceInfo ReadSourceInfo(string path)
        {
            PdfReader reader = null;
            try
            {
                reader = new PdfReader(path);
                if (reader.NumberOfPages < 1)
                {
                    throw new InvalidDataException(
                        "El PDF no contiene paginas: " + Path.GetFileName(path));
                }

                reader.MakeRemoteNamedDestinationsLocal();
                var stringNamedDestinations =
                    SimpleNamedDestination.GetNamedDestination(reader, false);
                var nameNamedDestinations =
                    SimpleNamedDestination.GetNamedDestination(reader, true);
                var bookmarks = SimpleBookmark.GetBookmark(reader);
                if (bookmarks != null && bookmarks.Count > 0)
                {
                    ConvertNamedBookmarksToExplicitPages(
                        bookmarks,
                        stringNamedDestinations,
                        nameNamedDestinations);
                }

                var acroForm =
                    reader.Catalog.GetAsDict(PdfName.ACROFORM);
                if (acroForm != null &&
                    acroForm.Get(PdfName.XFA) != null)
                {
                    throw new NotSupportedException(XfaUnsupportedMessage);
                }

                var fields = reader.AcroFields;
                return new SourceInfo(
                    path,
                    reader.NumberOfPages,
                    bookmarks,
                    stringNamedDestinations,
                    nameNamedDestinations,
                    fields == null ? 0 : fields.Fields.Count,
                    CountFormWidgets(reader),
                    fields != null && fields.GetSignatureNames().Count > 0);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException(
                    "No se pudo leer \"" + Path.GetFileName(path) + "\": " +
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

        private static string NormalizeExistingPdfPath(
            string path,
            string missingMessage)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new FileNotFoundException(missingMessage, path);
            }

            var normalizedPath = Path.GetFullPath(path);
            if (!File.Exists(normalizedPath))
            {
                throw new FileNotFoundException(missingMessage, normalizedPath);
            }

            if (!string.Equals(
                    Path.GetExtension(normalizedPath),
                    ".pdf",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "El archivo no es un PDF: " + Path.GetFileName(normalizedPath));
            }

            return normalizedPath;
        }

        private static string ValidateOutputPath(
            InsertionPlan plan,
            string outputPath)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new InvalidOperationException(
                    "Selecciona donde guardar la copia editada.");
            }

            if (!string.Equals(
                    Path.GetExtension(outputPath),
                    ".pdf",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "El archivo de salida debe tener extension .pdf.");
            }

            var normalizedOutputPath = Path.GetFullPath(outputPath);
            var outputDirectory = Path.GetDirectoryName(normalizedOutputPath);
            if (string.IsNullOrWhiteSpace(outputDirectory) ||
                !Directory.Exists(outputDirectory))
            {
                throw new DirectoryNotFoundException(
                    "La carpeta de destino ya no existe.");
            }

            if (string.Equals(
                    normalizedOutputPath,
                    plan.BaseSource.Path,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "La insercion se guarda en una copia; no se puede sobrescribir " +
                    "el PDF base.");
            }

            foreach (var source in plan.InsertedSources)
            {
                if (string.Equals(
                        normalizedOutputPath,
                        source.Path,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "El resultado no puede sobrescribir uno de los PDFs " +
                        "insertados.");
                }
            }

            if (File.Exists(normalizedOutputPath))
            {
                throw new IOException(
                    "Ya existe el PDF de salida. Elige otro nombre para no " +
                    "sobrescribirlo.");
            }

            return normalizedOutputPath;
        }

        private static OutputStructure WriteInsertedPdf(
            InsertionPlan plan,
            string outputPath,
            string tempPath,
            Action<PdfPageInsertProgress> reportProgress)
        {
            var combinedBookmarks = PrepareBookmarks(plan);
            var combinedNamedDestinations = PrepareNamedDestinations(plan);
            var segments = CreateSegments(plan);
            var usedFieldNames = new HashSet<string>(StringComparer.Ordinal);
            var readersToClose = new List<PdfReader>();
            var baseSyntheticDestinationNames =
                new Dictionary<int, string>();
            var completedPages = 0;
            var progressReporter = new ThrottledProgressReporter(reportProgress);

            try
            {
                using (var outputStream = new FileStream(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    1024 * 1024,
                    FileOptions.SequentialScan | FileOptions.WriteThrough))
                {
                    using (var document = new Document())
                    {
                        // PdfCopy transfers the existing PDF page streams and
                        // resources. No page is rasterized or recompressed.
                        var copy = new PdfCopy(document, outputStream);
                        copy.CloseStream = false;
                        copy.SetMergeFields();
                        document.AddTitle(
                            Path.GetFileNameWithoutExtension(outputPath));
                        document.AddCreator("PDF Ligero");
                        document.Open();

                    progressReporter.Report(
                        0,
                        plan.ResultPageCount,
                        plan.BaseSource.Path,
                        true);

                    for (var segmentIndex = 0;
                        segmentIndex < segments.Count;
                        segmentIndex++)
                    {
                        var segment = segments[segmentIndex];
                        PdfReader reader = null;
                        try
                        {
                            reader = new PdfReader(segment.Source.Path);
                            reader.MakeRemoteNamedDestinationsLocal();

                            // The base document keeps its named GoTo actions because its
                            // named destinations are remapped after insertion. Other
                            // documents are consolidated to avoid cross-document name
                            // collisions.
                            if (!segment.IsBaseSegment)
                            {
                                reader.ConsolidateNamedDestinations();
                            }
                            else
                            {
                                RewriteBaseExplicitPageDestinations(
                                    reader,
                                    segment.Pages,
                                    plan,
                                    combinedNamedDestinations,
                                    baseSyntheticDestinationNames);
                            }

                            if (segment.Pages != null)
                            {
                                reader.SelectPages(segment.Pages);
                            }

                            EnsureNonCollidingFormFieldNames(
                                reader,
                                segmentIndex + 1,
                                usedFieldNames);
                            copy.AddDocument(reader);

                            // Merge-fields mode finishes writing the field tree when the
                            // destination closes, so successful readers stay alive until
                            // then.
                            readersToClose.Add(reader);
                            reader = null;

                            completedPages += segment.PageCount;
                            progressReporter.Report(
                                completedPages,
                                plan.ResultPageCount,
                                segment.Source.Path,
                                completedPages >= plan.ResultPageCount);
                        }
                        catch (Exception ex)
                        {
                            throw new InvalidDataException(
                                "No se pudo incorporar \"" +
                                Path.GetFileName(segment.Source.Path) + "\": " +
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

                        if (combinedNamedDestinations.Count > 0)
                        {
                            copy.AddNamedDestinations(
                                combinedNamedDestinations,
                                0);
                        }

                        if (combinedBookmarks.Count > 0)
                        {
                            copy.Outlines = combinedBookmarks;
                        }
                    }

                    // The manifest is forced to disk immediately after Commit.
                    // Force the complete PDF first so recovery can never point
                    // at data that only existed in the Windows cache.
                    outputStream.Flush(true);
                }
            }
            finally
            {
                foreach (var reader in readersToClose)
                {
                    reader.Close();
                }
            }

            return new OutputStructure(
                combinedBookmarks.Count > 0,
                combinedNamedDestinations.Count > 0);
        }

        private static List<SourceSegment> CreateSegments(InsertionPlan plan)
        {
            var segments = new List<SourceSegment>();
            if (plan.InsertionIndex > 0)
            {
                segments.Add(
                    new SourceSegment(
                        plan.BaseSource,
                        CreatePageRange(1, plan.InsertionIndex),
                        true));
            }

            foreach (var source in plan.InsertedSources)
            {
                segments.Add(new SourceSegment(source, null, false));
            }

            if (plan.InsertionIndex < plan.BaseSource.PageCount)
            {
                segments.Add(
                    new SourceSegment(
                        plan.BaseSource,
                        CreatePageRange(
                            plan.InsertionIndex + 1,
                            plan.BaseSource.PageCount),
                        true));
            }

            return segments;
        }

        private static List<int> CreatePageRange(int firstPage, int lastPage)
        {
            var pages = new List<int>(lastPage - firstPage + 1);
            for (var page = firstPage; page <= lastPage; page++)
            {
                pages.Add(page);
            }

            return pages;
        }

        private static void RewriteBaseExplicitPageDestinations(
            PdfReader reader,
            IList<int> pagesToCopy,
            InsertionPlan plan,
            IDictionary<string, string> outputDestinations,
            IDictionary<int, string> syntheticNamesByPage)
        {
            var pageNumbersByReference =
                new Dictionary<string, int>(StringComparer.Ordinal);
            for (var page = 1; page <= reader.NumberOfPages; page++)
            {
                var pageReference = reader.GetPageOrigRef(page);
                if (pageReference != null)
                {
                    pageNumbersByReference[
                        GetReferenceKey(pageReference)] = page;
                }
            }

            var sourcePages = pagesToCopy ??
                CreatePageRange(1, reader.NumberOfPages);
            foreach (var sourcePage in sourcePages)
            {
                var pageDictionary = reader.GetPageN(sourcePage);
                var annotations = pageDictionary == null
                    ? null
                    : pageDictionary.GetAsArray(PdfName.ANNOTS);
                if (annotations == null)
                {
                    continue;
                }

                for (var annotationIndex = 0;
                    annotationIndex < annotations.Size;
                    annotationIndex++)
                {
                    var annotation =
                        PdfReader.GetPdfObject(
                            annotations[annotationIndex]) as PdfDictionary;
                    if (annotation == null)
                    {
                        continue;
                    }

                    RewriteDestinationEntry(
                        annotation,
                        PdfName.DEST,
                        pageNumbersByReference,
                        plan,
                        outputDestinations,
                        syntheticNamesByPage);

                    var action =
                        PdfReader.GetPdfObject(
                            annotation.Get(PdfName.A)) as PdfDictionary;
                    if (action != null &&
                        (action.GetAsName(PdfName.S) == null ||
                         PdfName.GOTO.Equals(action.GetAsName(PdfName.S))))
                    {
                        RewriteDestinationEntry(
                            action,
                            PdfName.D,
                            pageNumbersByReference,
                            plan,
                            outputDestinations,
                            syntheticNamesByPage);
                    }
                }
            }
        }

        private static void RewriteDestinationEntry(
            PdfDictionary owner,
            PdfName key,
            IDictionary<string, int> pageNumbersByReference,
            InsertionPlan plan,
            IDictionary<string, string> outputDestinations,
            IDictionary<int, string> syntheticNamesByPage)
        {
            var destination = PdfReader.GetPdfObject(owner.Get(key)) as PdfArray;
            if (destination == null || destination.Size == 0)
            {
                return;
            }

            var originalPage = ResolveDestinationPage(
                destination.GetPdfObject(0),
                pageNumbersByReference);
            if (originalPage < 1 ||
                originalPage > plan.BaseSource.PageCount)
            {
                return;
            }

            string syntheticName;
            if (!syntheticNamesByPage.TryGetValue(
                    originalPage,
                    out syntheticName))
            {
                syntheticName =
                    "__PDFLigero_insert_base_page_" +
                    originalPage.ToString(CultureInfo.InvariantCulture);
                var suffix = 2;
                var candidate = syntheticName;
                while (outputDestinations.ContainsKey(candidate))
                {
                    candidate =
                        syntheticName + "_" +
                        suffix.ToString(CultureInfo.InvariantCulture);
                    suffix++;
                }

                syntheticName = candidate;
                syntheticNamesByPage[originalPage] = syntheticName;
                var outputPage = MapBasePage(
                    originalPage,
                    plan.InsertionIndex,
                    plan.InsertedPageCount);
                outputDestinations[syntheticName] =
                    BuildDestinationString(destination, outputPage);
            }

            owner.Put(
                key,
                new PdfString(syntheticName, PdfObject.TEXT_UNICODE));
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
            return pageNumber == null ? 0 : pageNumber.IntValue + 1;
        }

        private static string GetReferenceKey(PRIndirectReference reference)
        {
            return reference.Number.ToString(CultureInfo.InvariantCulture) +
                ":" +
                reference.Generation.ToString(CultureInfo.InvariantCulture);
        }

        private static string BuildDestinationString(
            PdfArray destination,
            int outputPage)
        {
            if (destination.Size < 2)
            {
                return outputPage.ToString(CultureInfo.InvariantCulture) +
                    " Fit";
            }

            var parts = new List<string>
            {
                outputPage.ToString(CultureInfo.InvariantCulture)
            };
            for (var index = 1; index < destination.Size; index++)
            {
                var value = destination.GetPdfObject(index);
                var name = value as PdfName;
                if (name != null)
                {
                    var nameText = name.ToString();
                    parts.Add(
                        nameText.Length > 0 && nameText[0] == '/'
                            ? nameText.Substring(1)
                            : nameText);
                }
                else if (value == null || value.IsNull())
                {
                    parts.Add("null");
                }
                else
                {
                    parts.Add(value.ToString());
                }
            }

            return string.Join(" ", parts.ToArray());
        }

        private static List<Dictionary<string, object>> PrepareBookmarks(
            InsertionPlan plan)
        {
            var combined = new List<Dictionary<string, object>>();
            var beforeInsertion = new List<Dictionary<string, object>>();
            var afterInsertion = new List<Dictionary<string, object>>();

            if (plan.BaseSource.Bookmarks != null)
            {
                foreach (var bookmark in plan.BaseSource.Bookmarks)
                {
                    var firstTargetPage = FindFirstBookmarkPage(bookmark);
                    if (firstTargetPage <= 0 ||
                        firstTargetPage <= plan.InsertionIndex)
                    {
                        beforeInsertion.Add(bookmark);
                    }
                    else
                    {
                        afterInsertion.Add(bookmark);
                    }
                }
            }

            AdjustBookmarkPageNumbers(
                beforeInsertion,
                delegate(int page)
                {
                    return MapBasePage(
                        page,
                        plan.InsertionIndex,
                        plan.InsertedPageCount);
                });
            AdjustBookmarkPageNumbers(
                afterInsertion,
                delegate(int page)
                {
                    return MapBasePage(
                        page,
                        plan.InsertionIndex,
                        plan.InsertedPageCount);
                });
            combined.AddRange(beforeInsertion);

            var precedingInsertedPages = 0;
            foreach (var source in plan.InsertedSources)
            {
                if (source.Bookmarks != null)
                {
                    var outputOffset =
                        plan.InsertionIndex + precedingInsertedPages;
                    AdjustBookmarkPageNumbers(
                        source.Bookmarks,
                        delegate(int page) { return page + outputOffset; });
                    combined.AddRange(source.Bookmarks);
                }

                precedingInsertedPages += source.PageCount;
            }

            combined.AddRange(afterInsertion);
            return combined;
        }

        private static Dictionary<string, string> PrepareNamedDestinations(
            InsertionPlan plan)
        {
            var combined = new Dictionary<string, string>(StringComparer.Ordinal);
            var usedNames = new HashSet<string>(StringComparer.Ordinal);

            AddNamedDestinationsForOutput(
                plan.BaseSource.StringNamedDestinations,
                delegate(int page)
                {
                    return MapBasePage(
                        page,
                        plan.InsertionIndex,
                        plan.InsertedPageCount);
                },
                1,
                combined,
                usedNames);
            AddNamedDestinationsForOutput(
                plan.BaseSource.NameNamedDestinations,
                delegate(int page)
                {
                    return MapBasePage(
                        page,
                        plan.InsertionIndex,
                        plan.InsertedPageCount);
                },
                1,
                combined,
                usedNames);

            var precedingInsertedPages = 0;
            for (var sourceIndex = 0;
                sourceIndex < plan.InsertedSources.Count;
                sourceIndex++)
            {
                var source = plan.InsertedSources[sourceIndex];
                var outputOffset =
                    plan.InsertionIndex + precedingInsertedPages;
                AddNamedDestinationsForOutput(
                    source.StringNamedDestinations,
                    delegate(int page) { return page + outputOffset; },
                    sourceIndex + 2,
                    combined,
                    usedNames);
                AddNamedDestinationsForOutput(
                    source.NameNamedDestinations,
                    delegate(int page) { return page + outputOffset; },
                    sourceIndex + 2,
                    combined,
                    usedNames);
                precedingInsertedPages += source.PageCount;
            }

            return combined;
        }

        private static int MapBasePage(
            int page,
            int insertionIndex,
            int insertedPageCount)
        {
            return page <= insertionIndex
                ? page
                : page + insertedPageCount;
        }

        private static int FindFirstBookmarkPage(
            IDictionary<string, object> bookmark)
        {
            if (bookmark == null)
            {
                return 0;
            }

            object pageValue;
            int pageNumber;
            if (bookmark.TryGetValue("Page", out pageValue) &&
                TryReadDestinationPage(
                    Convert.ToString(pageValue, CultureInfo.InvariantCulture),
                    out pageNumber))
            {
                return pageNumber;
            }

            object kidsValue;
            var kids = bookmark.TryGetValue("Kids", out kidsValue)
                ? kidsValue as IList<Dictionary<string, object>>
                : null;
            if (kids == null)
            {
                return 0;
            }

            foreach (var kid in kids)
            {
                pageNumber = FindFirstBookmarkPage(kid);
                if (pageNumber > 0)
                {
                    return pageNumber;
                }
            }

            return 0;
        }

        private static void AdjustBookmarkPageNumbers(
            IList<Dictionary<string, object>> bookmarks,
            Func<int, int> mapPage)
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

                object pageValue;
                if (bookmark.TryGetValue("Page", out pageValue))
                {
                    var destination =
                        Convert.ToString(pageValue, CultureInfo.InvariantCulture);
                    bookmark["Page"] = MapDestinationPage(
                        destination,
                        mapPage);
                }

                object kidsValue;
                var kids = bookmark.TryGetValue("Kids", out kidsValue)
                    ? kidsValue as IList<Dictionary<string, object>>
                    : null;
                if (kids != null)
                {
                    AdjustBookmarkPageNumbers(kids, mapPage);
                }
            }
        }

        private static string MapDestinationPage(
            string destination,
            Func<int, int> mapPage)
        {
            int pageNumber;
            if (string.IsNullOrWhiteSpace(destination) ||
                !TryReadDestinationPage(destination, out pageNumber))
            {
                return destination;
            }

            var separatorIndex = destination.IndexOf(' ');
            return mapPage(pageNumber).ToString(CultureInfo.InvariantCulture) +
                (separatorIndex < 0
                    ? string.Empty
                    : destination.Substring(separatorIndex));
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
                        Convert.ToString(actionValue, CultureInfo.InvariantCulture),
                        "GoTo",
                        StringComparison.Ordinal);

                if (isLocalGoTo)
                {
                    string destination;
                    object namedValue;
                    if (bookmark.TryGetValue("Named", out namedValue) &&
                        TryResolveNamedDestination(
                            Convert.ToString(namedValue, CultureInfo.InvariantCulture),
                            stringNamedDestinations,
                            nameNamedDestinations,
                            false,
                            out destination))
                    {
                        bookmark["Page"] = destination;
                        bookmark.Remove("Named");
                        bookmark.Remove("NamedN");
                    }
                    else if (bookmark.TryGetValue("NamedN", out namedValue) &&
                        TryResolveNamedDestination(
                            Convert.ToString(namedValue, CultureInfo.InvariantCulture),
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
                var kids = bookmark.TryGetValue("Kids", out kidsValue)
                    ? kidsValue as IList<Dictionary<string, object>>
                    : null;
                if (kids != null)
                {
                    ConvertNamedBookmarksToExplicitPages(
                        kids,
                        stringNamedDestinations,
                        nameNamedDestinations);
                }
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
                var encodedName = name[0] == '/' ? name : "/" + name;
                var decodedName = PdfName.DecodeName(encodedName);
                if (!string.Equals(decodedName, name, StringComparison.Ordinal))
                {
                    candidates.Add(decodedName);
                }
            }
            else
            {
                var unescapedName = SimpleBookmark.UnEscapeBinaryString(name);
                if (!string.Equals(unescapedName, name, StringComparison.Ordinal))
                {
                    candidates.Add(unescapedName);
                }
            }

            foreach (var candidate in candidates)
            {
                if (primaryDestinations != null &&
                    primaryDestinations.TryGetValue(candidate, out destination))
                {
                    return true;
                }

                if (secondaryDestinations != null &&
                    secondaryDestinations.TryGetValue(candidate, out destination))
                {
                    return true;
                }
            }

            destination = null;
            return false;
        }

        private static void AddNamedDestinationsForOutput(
            IDictionary<string, string> sourceDestinations,
            Func<int, int> mapPage,
            int sourceIndex,
            IDictionary<string, string> outputDestinations,
            ISet<string> usedNames)
        {
            if (sourceDestinations == null)
            {
                return;
            }

            foreach (var entry in sourceDestinations)
            {
                var mappedDestination = MapDestinationPage(
                    entry.Value,
                    mapPage);
                if (string.IsNullOrEmpty(entry.Key) ||
                    string.IsNullOrEmpty(mappedDestination))
                {
                    continue;
                }

                var outputName = entry.Key;
                if (!usedNames.Add(outputName))
                {
                    var suffix = "__PDF" +
                        sourceIndex.ToString(CultureInfo.InvariantCulture);
                    outputName = entry.Key + suffix;
                    var collisionIndex = 2;
                    while (!usedNames.Add(outputName))
                    {
                        outputName =
                            entry.Key + suffix + "_" +
                            collisionIndex.ToString(CultureInfo.InvariantCulture);
                        collisionIndex++;
                    }
                }

                outputDestinations[outputName] = mappedDestination;
            }
        }

        private static void EnsureNonCollidingFormFieldNames(
            PdfReader reader,
            int sourceIndex,
            ISet<string> usedFieldNames)
        {
            var sourceFieldNames = new List<string>(reader.AcroFields.Fields.Keys);
            if (sourceFieldNames.Count == 0)
            {
                return;
            }

            var needsNamespace = HasFieldNameCollision(
                sourceFieldNames,
                usedFieldNames);
            if (!needsNamespace)
            {
                foreach (var fieldName in sourceFieldNames)
                {
                    usedFieldNames.Add(fieldName);
                }

                return;
            }

            var prefixBase =
                "__PDF" + sourceIndex.ToString(CultureInfo.InvariantCulture) + "_";
            var prefix = prefixBase;
            var suffixIndex = 2;
            while (HasFieldNameCollision(
                PrefixFieldNames(sourceFieldNames, prefix),
                usedFieldNames))
            {
                prefix =
                    prefixBase + suffixIndex.ToString(CultureInfo.InvariantCulture) +
                    "_";
                suffixIndex++;
            }

            PrefixTopLevelFieldNames(reader, prefix);
            foreach (var fieldName in sourceFieldNames)
            {
                usedFieldNames.Add(prefix + fieldName);
            }
        }

        private static IList<string> PrefixFieldNames(
            IList<string> fieldNames,
            string prefix)
        {
            var result = new List<string>(fieldNames.Count);
            foreach (var fieldName in fieldNames)
            {
                result.Add(prefix + fieldName);
            }

            return result;
        }

        private static bool HasFieldNameCollision(
            IEnumerable<string> sourceFieldNames,
            IEnumerable<string> usedFieldNames)
        {
            foreach (var sourceFieldName in sourceFieldNames)
            {
                foreach (var usedFieldName in usedFieldNames)
                {
                    if (string.Equals(
                            sourceFieldName,
                            usedFieldName,
                            StringComparison.Ordinal) ||
                        sourceFieldName.StartsWith(
                            usedFieldName + ".",
                            StringComparison.Ordinal) ||
                        usedFieldName.StartsWith(
                            sourceFieldName + ".",
                            StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void PrefixTopLevelFieldNames(
            PdfReader reader,
            string prefix)
        {
            var acroForm = reader.Catalog.GetAsDict(PdfName.ACROFORM);
            var fields = acroForm == null
                ? null
                : acroForm.GetAsArray(PdfName.FIELDS);
            if (fields == null)
            {
                return;
            }

            for (var index = 0; index < fields.Size; index++)
            {
                PrefixFirstNamedField(fields[index], prefix);
            }
        }

        private static void PrefixFirstNamedField(
            PdfObject fieldObject,
            string prefix)
        {
            var field = PdfReader.GetPdfObject(fieldObject) as PdfDictionary;
            if (field == null)
            {
                return;
            }

            var partialName = field.GetAsString(PdfName.T);
            if (partialName != null)
            {
                field.Put(
                    PdfName.T,
                    new PdfString(
                        prefix + partialName.ToUnicodeString(),
                        PdfObject.TEXT_UNICODE));
                return;
            }

            var kids = field.GetAsArray(PdfName.KIDS);
            if (kids == null)
            {
                return;
            }

            for (var index = 0; index < kids.Size; index++)
            {
                PrefixFirstNamedField(kids[index], prefix);
            }
        }

        private static void ValidateWrittenPdf(
            string path,
            int expectedPageCount,
            bool expectedBookmarks,
            bool expectedNamedDestinations,
            int expectedFormFieldCount,
            int expectedFormWidgetCount)
        {
            PdfReader reader = null;
            try
            {
                reader = new PdfReader(path);
                if (reader.NumberOfPages != expectedPageCount)
                {
                    throw new InvalidDataException(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "La comprobacion final esperaba {0} paginas y " +
                            "encontro {1}.",
                            expectedPageCount,
                            reader.NumberOfPages));
                }

                for (var page = 1; page <= reader.NumberOfPages; page++)
                {
                    var pageDictionary = reader.GetPageN(page);
                    var pageSize = reader.GetPageSizeWithRotation(page);
                    if (pageDictionary == null ||
                        pageSize == null ||
                        pageSize.Width <= 0 ||
                        pageSize.Height <= 0)
                    {
                        throw new InvalidDataException(
                            "La comprobacion final encontro una pagina no valida.");
                    }
                }

                var actualFormFieldCount = reader.AcroFields == null
                    ? 0
                    : reader.AcroFields.Fields.Count;
                if (actualFormFieldCount < expectedFormFieldCount)
                {
                    throw new InvalidDataException(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "La comprobacion final esperaba al menos {0} campos " +
                            "de formulario y encontro {1}.",
                            expectedFormFieldCount,
                            actualFormFieldCount));
                }

                var actualFormWidgetCount = CountFormWidgets(reader);
                if (actualFormWidgetCount < expectedFormWidgetCount)
                {
                    throw new InvalidDataException(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "La comprobacion final esperaba al menos {0} controles " +
                            "de formulario y encontro {1}.",
                            expectedFormWidgetCount,
                            actualFormWidgetCount));
                }

                if (expectedBookmarks)
                {
                    var bookmarks = SimpleBookmark.GetBookmark(reader);
                    if (bookmarks == null || bookmarks.Count == 0)
                    {
                        throw new InvalidDataException(
                            "La comprobacion final no encontro los marcadores " +
                            "esperados.");
                    }
                }

                if (expectedNamedDestinations)
                {
                    var stringDestinations =
                        SimpleNamedDestination.GetNamedDestination(reader, false);
                    var nameDestinations =
                        SimpleNamedDestination.GetNamedDestination(reader, true);
                    if ((stringDestinations == null ||
                         stringDestinations.Count == 0) &&
                        (nameDestinations == null ||
                         nameDestinations.Count == 0))
                    {
                        throw new InvalidDataException(
                            "La comprobacion final no encontro los destinos " +
                            "esperados.");
                    }
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

        private static void CommitTemporaryFile(
            string tempPath,
            string outputPath)
        {
            // File.Move is atomic inside the destination volume and, unlike
            // File.Replace, fails if another process creates the suggested name
            // while the PDF is being prepared.
            File.Move(tempPath, outputPath);
        }

        private static void SplitEditedName(
            string baseName,
            out string editedBaseName,
            out int nextSuffix)
        {
            const string marker = "_editado";
            var markerIndex = baseName.LastIndexOf(
                marker,
                StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
            {
                editedBaseName = baseName + marker;
                nextSuffix = 0;
                return;
            }

            var suffixText = baseName.Substring(markerIndex + marker.Length);
            if (suffixText.Length == 0)
            {
                editedBaseName = baseName;
                nextSuffix = 2;
                return;
            }

            int existingSuffix;
            if (suffixText[0] == '_' &&
                int.TryParse(
                    suffixText.Substring(1),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out existingSuffix) &&
                existingSuffix >= 2)
            {
                editedBaseName = baseName.Substring(
                    0,
                    markerIndex + marker.Length);
                nextSuffix = existingSuffix + 1;
                return;
            }

            editedBaseName = baseName + marker;
            nextSuffix = 0;
        }

        private static int CountFormWidgets(PdfReader reader)
        {
            var count = 0;
            for (var page = 1; page <= reader.NumberOfPages; page++)
            {
                var pageDictionary = reader.GetPageN(page);
                var annotations = pageDictionary == null
                    ? null
                    : pageDictionary.GetAsArray(PdfName.ANNOTS);
                if (annotations == null)
                {
                    continue;
                }

                for (var index = 0; index < annotations.Size; index++)
                {
                    var annotation =
                        PdfReader.GetPdfObject(annotations[index]) as PdfDictionary;
                    if (annotation != null &&
                        PdfName.WIDGET.Equals(annotation.GetAsName(PdfName.SUBTYPE)))
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private sealed class SourceInfo
        {
            public SourceInfo(
                string path,
                int pageCount,
                IList<Dictionary<string, object>> bookmarks,
                IDictionary<string, string> stringNamedDestinations,
                IDictionary<string, string> nameNamedDestinations,
                int formFieldCount,
                int formWidgetCount,
                bool containsDigitalSignatures)
            {
                Path = path;
                PageCount = pageCount;
                Bookmarks = bookmarks;
                StringNamedDestinations = stringNamedDestinations;
                NameNamedDestinations = nameNamedDestinations;
                FormFieldCount = formFieldCount;
                FormWidgetCount = formWidgetCount;
                ContainsDigitalSignatures = containsDigitalSignatures;
            }

            public string Path { get; private set; }

            public int PageCount { get; private set; }

            public IList<Dictionary<string, object>> Bookmarks
            {
                get;
                private set;
            }

            public IDictionary<string, string> StringNamedDestinations
            {
                get;
                private set;
            }

            public IDictionary<string, string> NameNamedDestinations
            {
                get;
                private set;
            }

            public int FormFieldCount { get; private set; }

            public int FormWidgetCount { get; private set; }

            public bool HasFormFields
            {
                get { return FormFieldCount > 0 || FormWidgetCount > 0; }
            }

            public bool ContainsDigitalSignatures { get; private set; }
        }

        private sealed class InsertionPlan
        {
            public InsertionPlan(
                SourceInfo baseSource,
                IList<SourceInfo> insertedSources,
                int insertionIndex,
                int insertedPageCount,
                bool containsDigitalSignatures,
                bool hasFormFields)
            {
                BaseSource = baseSource;
                InsertedSources = insertedSources;
                InsertionIndex = insertionIndex;
                InsertedPageCount = insertedPageCount;
                ContainsDigitalSignatures = containsDigitalSignatures;
                HasFormFields = hasFormFields;
                ExpectedFormFieldCount = baseSource.FormFieldCount;
                ExpectedFormWidgetCount = baseSource.FormWidgetCount;
                foreach (var insertedSource in insertedSources)
                {
                    ExpectedFormFieldCount += insertedSource.FormFieldCount;
                    ExpectedFormWidgetCount += insertedSource.FormWidgetCount;
                }
            }

            public SourceInfo BaseSource { get; private set; }

            public IList<SourceInfo> InsertedSources { get; private set; }

            public int InsertionIndex { get; private set; }

            public int InsertedPageCount { get; private set; }

            public int ResultPageCount
            {
                get { return BaseSource.PageCount + InsertedPageCount; }
            }

            public bool ContainsDigitalSignatures { get; private set; }

            public bool HasFormFields { get; private set; }

            public int ExpectedFormFieldCount { get; private set; }

            public int ExpectedFormWidgetCount { get; private set; }
        }

        private sealed class SourceSegment
        {
            public SourceSegment(
                SourceInfo source,
                List<int> pages,
                bool isBaseSegment)
            {
                Source = source;
                Pages = pages;
                IsBaseSegment = isBaseSegment;
            }

            public SourceInfo Source { get; private set; }

            public List<int> Pages { get; private set; }

            public bool IsBaseSegment { get; private set; }

            public int PageCount
            {
                get { return Pages == null ? Source.PageCount : Pages.Count; }
            }
        }

        private sealed class OutputStructure
        {
            public OutputStructure(
                bool hasBookmarks,
                bool hasNamedDestinations)
            {
                HasBookmarks = hasBookmarks;
                HasNamedDestinations = hasNamedDestinations;
            }

            public bool HasBookmarks { get; private set; }

            public bool HasNamedDestinations { get; private set; }
        }

        private sealed class ThrottledProgressReporter
        {
            private const int MinimumIntervalMilliseconds = 100;
            private readonly Action<PdfPageInsertProgress> callback;
            private readonly Stopwatch stopwatch;
            private int lastPercentage;

            public ThrottledProgressReporter(
                Action<PdfPageInsertProgress> callback)
            {
                this.callback = callback;
                stopwatch = Stopwatch.StartNew();
                lastPercentage = -1;
            }

            public void Report(
                int completedPages,
                int totalPages,
                string sourcePath,
                bool force)
            {
                if (callback == null)
                {
                    return;
                }

                var percentage = totalPages <= 0
                    ? 0
                    : Math.Min(
                        100,
                        (int)Math.Round(
                            completedPages * 100D / totalPages));
                if (percentage == lastPercentage)
                {
                    return;
                }

                if (!force &&
                    stopwatch.ElapsedMilliseconds <
                    MinimumIntervalMilliseconds)
                {
                    return;
                }

                callback(
                    new PdfPageInsertProgress(
                        completedPages,
                        totalPages,
                        sourcePath));
                lastPercentage = percentage;
                stopwatch.Restart();
            }
        }
    }
}

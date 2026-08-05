using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using iTextSharp.text;
using iTextSharp.text.pdf;

namespace FirmaAutomatica
{
    internal sealed class PdfMergeProgress
    {
        public PdfMergeProgress(int completedPages, int totalPages, string sourcePath)
        {
            CompletedPages = completedPages;
            TotalPages = totalPages;
            SourcePath = sourcePath;
        }

        public int CompletedPages { get; private set; }

        public int TotalPages { get; private set; }

        public string SourcePath { get; private set; }
    }

    internal sealed class PdfMergeResult
    {
        public PdfMergeResult(string outputPath, int pageCount)
        {
            OutputPath = outputPath;
            PageCount = pageCount;
        }

        public string OutputPath { get; private set; }

        public int PageCount { get; private set; }
    }

    internal static class PdfMergeService
    {
        public static int ReadPageCount(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                throw new FileNotFoundException("No se encuentra el PDF.", path);
            }

            PdfReader reader = null;
            try
            {
                reader = new PdfReader(path);
                if (reader.NumberOfPages < 1)
                {
                    throw new InvalidDataException("El PDF no contiene paginas.");
                }

                return reader.NumberOfPages;
            }
            finally
            {
                if (reader != null)
                {
                    reader.Close();
                }
            }
        }

        public static PdfMergeResult Merge(
            IList<string> sourcePaths,
            string outputPath,
            Action<PdfMergeProgress> reportProgress)
        {
            return Merge(sourcePaths, outputPath, 0, reportProgress);
        }

        public static PdfMergeResult Merge(
            IList<string> sourcePaths,
            string outputPath,
            int knownPageCount,
            Action<PdfMergeProgress> reportProgress)
        {
            var sources = NormalizeAndValidateSources(sourcePaths, outputPath);
            var normalizedOutputPath = Path.GetFullPath(outputPath);
            var outputDirectory = Path.GetDirectoryName(normalizedOutputPath);
            if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
            {
                throw new DirectoryNotFoundException("La carpeta de destino ya no existe.");
            }

            var tempPath = Path.Combine(
                outputDirectory,
                "." + Path.GetFileNameWithoutExtension(normalizedOutputPath) + "." +
                Guid.NewGuid().ToString("N") + ".tmp");

            var expectedPageCount = knownPageCount;
            if (expectedPageCount < 1)
            {
                foreach (var source in sources)
                {
                    expectedPageCount += ReadPageCount(source);
                }
            }

            try
            {
                var outlineExpectations = WriteMergedPdf(
                    sources,
                    normalizedOutputPath,
                    tempPath,
                    expectedPageCount,
                    reportProgress);
                ValidateMergedPdf(
                    tempPath,
                    expectedPageCount,
                    outlineExpectations);
                CommitTemporaryFile(tempPath, normalizedOutputPath);
                return new PdfMergeResult(normalizedOutputPath, expectedPageCount);
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

        private static List<string> NormalizeAndValidateSources(IList<string> sourcePaths, string outputPath)
        {
            if (sourcePaths == null || sourcePaths.Count < 2)
            {
                throw new InvalidOperationException("Añade al menos dos PDFs para combinarlos.");
            }

            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new InvalidOperationException("Selecciona donde guardar el PDF combinado.");
            }

            if (!string.Equals(Path.GetExtension(outputPath), ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("El archivo de salida debe tener extension .pdf.");
            }

            var normalizedOutputPath = Path.GetFullPath(outputPath);
            var normalizedSources = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var sourcePath in sourcePaths)
            {
                if (string.IsNullOrWhiteSpace(sourcePath))
                {
                    continue;
                }

                var normalizedSourcePath = Path.GetFullPath(sourcePath);
                if (!File.Exists(normalizedSourcePath))
                {
                    throw new FileNotFoundException(
                        "Ya no se encuentra uno de los PDFs seleccionados.",
                        normalizedSourcePath);
                }

                if (!string.Equals(Path.GetExtension(normalizedSourcePath), ".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "El archivo no es un PDF: " + Path.GetFileName(normalizedSourcePath));
                }

                if (string.Equals(normalizedSourcePath, normalizedOutputPath, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "El resultado no puede sobrescribir uno de los PDFs de origen.");
                }

                if (seen.Add(normalizedSourcePath))
                {
                    normalizedSources.Add(normalizedSourcePath);
                }
            }

            if (normalizedSources.Count < 2)
            {
                throw new InvalidOperationException("Añade al menos dos PDFs distintos para combinarlos.");
            }

            return normalizedSources;
        }

        private static MergeValidationExpectations
            WriteMergedPdf(
            IList<string> sourcePaths,
            string outputPath,
            string tempPath,
            int totalPages,
            Action<PdfMergeProgress> reportProgress)
        {
            var combinedOutlines = new List<MergedOutlineNode>();
            var combinedOptionalContent =
                new MergedOptionalContent();
            var combinedNamedDestinations =
                new Dictionary<string, string>(
                    StringComparer.Ordinal);
            var usedNamedDestinationNames =
                new HashSet<string>(StringComparer.Ordinal);
            var usedFieldNames = new HashSet<string>(StringComparer.Ordinal);
            var completedPages = 0;
            var progressReporter = new ThrottledProgressReporter(reportProgress);
            var readersToClose = new List<PdfReader>();

            try
            {
                using (var outputStream = new FileStream(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None))
                using (var document = new Document())
                {
                    // PdfCopy with merge-fields mode is required here. Importing pages one by
                    // one (and PdfSmartCopy's object deduplication) drops or corrupts AcroForm
                    // field trees in otherwise valid source documents.
                    var copy = new OutlineAwarePdfCopy(
                        document,
                        outputStream);
                    copy.SetMergeFields();
                    document.AddTitle(Path.GetFileNameWithoutExtension(outputPath));
                    document.AddCreator("PDF Ligero");
                    document.Open();

                    if (sourcePaths.Count > 0)
                    {
                        progressReporter.Report(0, totalPages, sourcePaths[0], true);
                    }

                    for (var sourceIndex = 0; sourceIndex < sourcePaths.Count; sourceIndex++)
                    {
                        var sourcePath = sourcePaths[sourceIndex];
                        PdfReader reader = null;
                        try
                        {
                            reader = new PdfReader(sourcePath);
                            reader.MakeRemoteNamedDestinationsLocal();

                            var stringNamedDestinations =
                                SimpleNamedDestination.GetNamedDestination(
                                    reader,
                                    false);
                            var nameNamedDestinations =
                                SimpleNamedDestination.GetNamedDestination(
                                    reader,
                                    true);
                            var rawNamedDestinations =
                                ReadRawNamedDestinations(reader);
                            AddNamedDestinationsForOutput(
                                stringNamedDestinations,
                                completedPages,
                                sourceIndex + 1,
                                combinedNamedDestinations,
                                usedNamedDestinationNames);
                            AddNamedDestinationsForOutput(
                                nameNamedDestinations,
                                completedPages,
                                sourceIndex + 1,
                                combinedNamedDestinations,
                                usedNamedDestinationNames);

                            // Links and actions must no longer depend on a source document's
                            // /Dests or /Names tree: those trees can contain the same key in
                            // different PDFs. Explicit page destinations survive the copy and
                            // keep pointing at the correct imported page.
                            reader.ConsolidateNamedDestinations();

                            EnsureNonCollidingFormFieldNames(
                                reader,
                                sourceIndex + 1,
                                usedFieldNames);

                            var sourcePageCount = reader.NumberOfPages;
                            copy.AddDocument(reader);
                            CopyOptionalContentProperties(
                                reader,
                                copy,
                                combinedOptionalContent);
                            combinedOutlines.AddRange(
                                CopySourceOutlines(
                                    reader,
                                    copy,
                                    completedPages,
                                    rawNamedDestinations));

                            // Merge-fields mode writes parts of the field tree only when the
                            // destination document closes. Keep every successful reader alive
                            // until that flush has completed.
                            readersToClose.Add(reader);
                            reader = null;

                            completedPages += sourcePageCount;
                            progressReporter.Report(
                                completedPages,
                                totalPages,
                                sourcePath,
                                completedPages >= totalPages);
                        }
                        catch (Exception ex)
                        {
                            // Se usa el diagnostico comun para no incrustar aqui
                            // el texto ingles de iText, que despues ya no se
                            // puede recuperar de la cadena de excepciones.
                            throw new InvalidDataException(
                                "No se pudo incorporar \"" +
                                Path.GetFileName(sourcePath) + "\": " +
                                PdfProblemDiagnostics.Describe(ex, sourcePath),
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
                    WriteCombinedOptionalContent(
                        copy,
                        combinedOptionalContent);
                    WriteCombinedOutlines(copy, combinedOutlines);
                }

                return new MergeValidationExpectations(
                    CreateOutlineExpectations(combinedOutlines),
                    combinedOptionalContent
                        .OptionalContentGroups.Count);
            }
            finally
            {
                foreach (var reader in readersToClose)
                {
                    reader.Close();
                }
            }
        }

        private static List<MergedOutlineNode> CopySourceOutlines(
            PdfReader reader,
            OutlineAwarePdfCopy copy,
            int pageOffset,
            IDictionary<string, PdfObject> namedDestinations)
        {
            var root = ResolveDictionary(
                reader.Catalog.Get(PdfName.OUTLINES));
            if (root == null)
            {
                return new List<MergedOutlineNode>();
            }

            var pageNumbers =
                ReadPageNumbersByReference(reader);
            var visited = new HashSet<string>(
                StringComparer.Ordinal);
            var count = 0;
            return CopyOutlineSiblings(
                root.Get(PdfName.FIRST),
                reader,
                copy,
                pageOffset,
                pageNumbers,
                namedDestinations,
                visited,
                0,
                ref count);
        }

        private static List<MergedOutlineNode>
            CopyOutlineSiblings(
                PdfObject firstObject,
                PdfReader reader,
                OutlineAwarePdfCopy copy,
                int pageOffset,
                IDictionary<string, int> pageNumbers,
                IDictionary<string, PdfObject> namedDestinations,
                ISet<string> visited,
                int depth,
                ref int count)
        {
            if (depth > 256)
            {
                throw new NotSupportedException(
                    "El arbol de marcadores es demasiado profundo para " +
                    "combinarlo de forma segura.");
            }

            var result = new List<MergedOutlineNode>();
            var currentObject = firstObject;
            while (currentObject != null)
            {
                count++;
                if (count > 100000)
                {
                    throw new NotSupportedException(
                        "El PDF contiene demasiados marcadores para " +
                        "combinarlos de forma segura.");
                }

                var sourceDictionary =
                    ResolveDictionary(currentObject);
                if (sourceDictionary == null)
                {
                    throw new NotSupportedException(
                        "El PDF contiene un marcador estructuralmente " +
                        "invalido. No se ha creado ninguna salida.");
                }

                var reference =
                    currentObject as PdfIndirectReference;
                if (reference != null &&
                    !visited.Add(GetReferenceKey(reference)))
                {
                    throw new NotSupportedException(
                        "El arbol de marcadores contiene un ciclo. " +
                        "No se ha creado ninguna salida.");
                }

                var copiedDictionary = CopyOutlineDictionary(
                    sourceDictionary,
                    reader,
                    copy,
                    pageOffset,
                    pageNumbers,
                    namedDestinations);
                var sourceCount =
                    sourceDictionary.GetAsNumber(PdfName.COUNT);
                var node = new MergedOutlineNode(
                    copiedDictionary,
                    sourceCount == null ||
                        sourceCount.IntValue >= 0);
                node.Children.AddRange(
                    CopyOutlineSiblings(
                        sourceDictionary.Get(PdfName.FIRST),
                        reader,
                        copy,
                        pageOffset,
                        pageNumbers,
                        namedDestinations,
                        visited,
                        depth + 1,
                        ref count));
                result.Add(node);
                currentObject =
                    sourceDictionary.Get(PdfName.NEXT);
            }

            return result;
        }

        private static PdfDictionary CopyOutlineDictionary(
            PdfDictionary source,
            PdfReader reader,
            OutlineAwarePdfCopy copy,
            int pageOffset,
            IDictionary<string, int> pageNumbers,
            IDictionary<string, PdfObject> namedDestinations)
        {
            var result = new PdfDictionary();
            foreach (var key in source.Keys)
            {
                if (IsOutlineStructuralKey(key))
                {
                    continue;
                }

                var value = source.Get(key);
                if (key.Equals(PdfName.DEST))
                {
                    result.Put(
                        key,
                        CopyLocalDestination(
                            value,
                            copy,
                            pageOffset,
                            pageNumbers,
                            namedDestinations));
                }
                else if (key.Equals(PdfName.A))
                {
                    result.Put(
                        key,
                        CopyAction(
                            value,
                            copy,
                            pageOffset,
                            pageNumbers,
                            namedDestinations,
                            new HashSet<string>(
                                StringComparer.Ordinal),
                            0));
                }
                else
                {
                    result.Put(
                        key,
                        copy.CopyForOutline(value));
                }
            }

            return result;
        }

        private static PdfObject CopyAction(
            PdfObject actionObject,
            OutlineAwarePdfCopy copy,
            int pageOffset,
            IDictionary<string, int> pageNumbers,
            IDictionary<string, PdfObject> namedDestinations,
            ISet<string> activeReferences,
            int depth)
        {
            if (depth > 64)
            {
                throw new NotSupportedException(
                    "Una cadena de acciones de marcador es demasiado " +
                    "profunda para combinarla de forma segura.");
            }

            var reference =
                actionObject as PdfIndirectReference;
            var referenceKey = reference == null
                ? null
                : GetReferenceKey(reference);
            if (referenceKey != null &&
                !activeReferences.Add(referenceKey))
            {
                throw new NotSupportedException(
                    "Una accion de marcador contiene un ciclo. " +
                    "No se ha creado ninguna salida.");
            }

            try
            {
                var sourceAction =
                    ResolveDictionary(actionObject);
                if (sourceAction == null)
                {
                    throw new NotSupportedException(
                        "Un marcador contiene una accion que no se puede " +
                        "copiar de forma segura.");
                }

                var result = new PdfDictionary();
                var isLocalGoTo = PdfName.GOTO.Equals(
                    sourceAction.GetAsName(PdfName.S));
                foreach (var key in sourceAction.Keys)
                {
                    var value = sourceAction.Get(key);
                    if (isLocalGoTo &&
                        key.Equals(PdfName.D))
                    {
                        result.Put(
                            key,
                            CopyLocalDestination(
                                value,
                                copy,
                                pageOffset,
                                pageNumbers,
                                namedDestinations));
                    }
                    else if (key.Equals(PdfName.NEXT))
                    {
                        result.Put(
                            key,
                            CopyNextActions(
                                value,
                                copy,
                                pageOffset,
                                pageNumbers,
                                namedDestinations,
                                activeReferences,
                                depth + 1));
                    }
                    else
                    {
                        result.Put(
                            key,
                            copy.CopyForOutline(value));
                    }
                }

                return result;
            }
            finally
            {
                if (referenceKey != null)
                {
                    activeReferences.Remove(referenceKey);
                }
            }
        }

        private static PdfObject CopyNextActions(
            PdfObject value,
            OutlineAwarePdfCopy copy,
            int pageOffset,
            IDictionary<string, int> pageNumbers,
            IDictionary<string, PdfObject> namedDestinations,
            ISet<string> activeReferences,
            int depth)
        {
            var resolved = ResolvePdfObject(value);
            var actions = resolved as PdfArray;
            if (actions == null)
            {
                return CopyAction(
                    value,
                    copy,
                    pageOffset,
                    pageNumbers,
                    namedDestinations,
                    activeReferences,
                    depth);
            }

            var result = new PdfArray();
            for (var index = 0;
                index < actions.Size;
                index++)
            {
                result.Add(
                    CopyAction(
                        actions[index],
                        copy,
                        pageOffset,
                        pageNumbers,
                        namedDestinations,
                        activeReferences,
                        depth));
            }

            return result;
        }

        private static PdfArray CopyLocalDestination(
            PdfObject destinationObject,
            OutlineAwarePdfCopy copy,
            int pageOffset,
            IDictionary<string, int> pageNumbers,
            IDictionary<string, PdfObject> namedDestinations)
        {
            var resolved = ResolveNamedDestination(
                destinationObject,
                namedDestinations);
            var sourceArray =
                ResolvePdfObject(resolved) as PdfArray;
            if (sourceArray == null ||
                sourceArray.Size < 2)
            {
                throw new NotSupportedException(
                    "Un marcador contiene un destino interno que no se " +
                    "puede resolver. La combinacion se ha cancelado para " +
                    "no perderlo.");
            }

            int sourcePageNumber;
            if (!TryResolveDestinationPage(
                    sourceArray[0],
                    pageNumbers,
                    out sourcePageNumber))
            {
                throw new NotSupportedException(
                    "Un marcador apunta a una pagina que no se puede " +
                    "identificar. La combinacion se ha cancelado.");
            }

            var outputPageNumber =
                pageOffset + sourcePageNumber;
            var result = new PdfArray();
            result.Add(copy.GetPageReference(outputPageNumber));
            for (var index = 1;
                index < sourceArray.Size;
                index++)
            {
                result.Add(
                    copy.CopyForOutline(sourceArray[index]));
            }

            return result;
        }

        private static PdfObject ResolveNamedDestination(
            PdfObject destinationObject,
            IDictionary<string, PdfObject> namedDestinations)
        {
            var resolved =
                ResolvePdfObject(destinationObject);
            string name = null;
            var pdfName = resolved as PdfName;
            if (pdfName != null)
            {
                name = "N:" +
                    PdfName.DecodeName(
                        pdfName.ToString());
            }
            else
            {
                var pdfString = resolved as PdfString;
                if (pdfString != null)
                {
                    name = "S:" +
                        pdfString.ToUnicodeString();
                }
            }

            PdfObject mapped;
            return name != null &&
                namedDestinations.TryGetValue(
                    name,
                    out mapped)
                ? mapped
                : resolved;
        }

        private static Dictionary<string, PdfObject>
            ReadRawNamedDestinations(PdfReader reader)
        {
            var result = new Dictionary<string, PdfObject>(
                StringComparer.Ordinal);
            AddRawNamedDestinations(
                result,
                reader.GetNamedDestination(true));
            return result;
        }

        private static void AddRawNamedDestinations(
            IDictionary<string, PdfObject> target,
            IDictionary<object, PdfObject> source)
        {
            if (source == null)
            {
                return;
            }

            foreach (var item in source)
            {
                var name = NormalizeRawDestinationName(
                    item.Key);
                if (!string.IsNullOrEmpty(name))
                {
                    target[
                        (item.Key is PdfName ? "N:" : "S:") +
                        name] = item.Value;
                }
            }
        }

        private static string NormalizeRawDestinationName(
            object value)
        {
            var name = value as PdfName;
            if (name != null)
            {
                return PdfName.DecodeName(name.ToString());
            }

            var text = value as PdfString;
            if (text != null)
            {
                return text.ToUnicodeString();
            }

            return value == null
                ? null
                : value.ToString();
        }

        private static Dictionary<string, int>
            ReadPageNumbersByReference(PdfReader reader)
        {
            var result = new Dictionary<string, int>(
                StringComparer.Ordinal);
            for (var page = 1;
                page <= reader.NumberOfPages;
                page++)
            {
                result[GetReferenceKey(
                    reader.GetPageOrigRef(page))] = page;
            }

            return result;
        }

        private static bool TryResolveDestinationPage(
            PdfObject value,
            IDictionary<string, int> pageNumbers,
            out int pageNumber)
        {
            pageNumber = 0;
            var reference =
                value as PdfIndirectReference;
            if (reference != null)
            {
                return pageNumbers.TryGetValue(
                    GetReferenceKey(reference),
                    out pageNumber);
            }

            var number =
                ResolvePdfObject(value) as PdfNumber;
            if (number != null)
            {
                pageNumber = number.IntValue + 1;
                return pageNumber > 0;
            }

            return false;
        }

        private static void CopyOptionalContentProperties(
            PdfReader reader,
            OutlineAwarePdfCopy copy,
            MergedOptionalContent target)
        {
            var source = ResolveDictionary(
                reader.Catalog.Get(PdfName.OCPROPERTIES));
            if (source == null)
            {
                return;
            }

            var groups = ResolvePdfObject(
                source.Get(PdfName.OCGS)) as PdfArray;
            if (groups == null)
            {
                throw new NotSupportedException(
                    "El PDF contiene propiedades de capas incompletas. " +
                    "La combinacion se ha cancelado para conservar sus " +
                    "acciones de marcador.");
            }

            for (var index = 0;
                index < groups.Size;
                index++)
            {
                var copied =
                    copy.CopyForOutline(groups[index]);
                var key = CanonicalizePdfObject(copied);
                if (target.OptionalContentGroupKeys.Add(key))
                {
                    target.OptionalContentGroups.Add(copied);
                }
            }

            var defaultConfiguration =
                ResolveDictionary(source.Get(PdfName.D));
            if (defaultConfiguration != null)
            {
                target.DefaultConfigurations.Add(
                    CopyPlainDictionary(
                        defaultConfiguration,
                        copy));
            }

            var configurations = ResolvePdfObject(
                source.Get(PdfName.CONFIGS)) as PdfArray;
            if (configurations != null)
            {
                for (var index = 0;
                    index < configurations.Size;
                    index++)
                {
                    var configuration =
                        ResolveDictionary(
                            configurations[index]);
                    if (configuration == null)
                    {
                        throw new NotSupportedException(
                            "El PDF contiene una configuracion de capas " +
                            "que no se puede copiar de forma segura.");
                    }

                    target.AlternateConfigurations.Add(
                        CopyPlainDictionary(
                            configuration,
                            copy));
                }
            }

            foreach (var key in source.Keys)
            {
                if (key.Equals(PdfName.OCGS) ||
                    key.Equals(PdfName.D) ||
                    key.Equals(PdfName.CONFIGS))
                {
                    continue;
                }

                if (target.ExtraRootProperties.ContainsKey(key))
                {
                    throw new NotSupportedException(
                        "Varios PDFs contienen una extension de capas " +
                        "incompatible. La combinacion se ha cancelado.");
                }

                target.ExtraRootProperties[key] =
                    copy.CopyForOutline(source.Get(key));
            }
        }

        private static PdfDictionary CopyPlainDictionary(
            PdfDictionary source,
            OutlineAwarePdfCopy copy)
        {
            var result = new PdfDictionary();
            foreach (var key in source.Keys)
            {
                result.Put(
                    key,
                    copy.CopyForOutline(source.Get(key)));
            }

            return result;
        }

        private static void WriteCombinedOptionalContent(
            OutlineAwarePdfCopy copy,
            MergedOptionalContent source)
        {
            if (source.OptionalContentGroups.Count == 0)
            {
                return;
            }

            var properties = new PdfDictionary();
            var groups = new PdfArray();
            foreach (var group in
                source.OptionalContentGroups)
            {
                groups.Add(group);
            }

            properties.Put(PdfName.OCGS, groups);
            if (source.DefaultConfigurations.Count > 0)
            {
                properties.Put(
                    PdfName.D,
                    MergeDefaultConfigurations(
                        source.DefaultConfigurations));
            }

            if (source.AlternateConfigurations.Count > 0)
            {
                var configurations = new PdfArray();
                foreach (var configuration in
                    source.AlternateConfigurations)
                {
                    configurations.Add(configuration);
                }

                properties.Put(
                    PdfName.CONFIGS,
                    configurations);
            }

            foreach (var item in
                source.ExtraRootProperties)
            {
                properties.Put(item.Key, item.Value);
            }

            copy.ExtraCatalog.Put(
                PdfName.OCPROPERTIES,
                properties);
        }

        private static PdfDictionary MergeDefaultConfigurations(
            IList<PdfDictionary> configurations)
        {
            var result = new PdfDictionary();
            var arrayKeys = new[]
            {
                PdfName.ORDER,
                PdfName.ON,
                PdfName.OFF,
                PdfName.LOCKED,
                PdfName.RBGROUPS,
                PdfName.AS
            };
            foreach (var arrayKey in arrayKeys)
            {
                var merged = new PdfArray();
                foreach (var configuration in configurations)
                {
                    var values = configuration.GetAsArray(arrayKey);
                    if (values == null)
                    {
                        continue;
                    }

                    for (var index = 0;
                        index < values.Size;
                        index++)
                    {
                        merged.Add(values[index]);
                    }
                }

                if (merged.Size > 0)
                {
                    result.Put(arrayKey, merged);
                }
            }

            foreach (var configuration in configurations)
            {
                foreach (var key in configuration.Keys)
                {
                    if (ContainsPdfName(arrayKeys, key))
                    {
                        continue;
                    }

                    var value = configuration.Get(key);
                    var existing = result.Get(key);
                    if (existing == null)
                    {
                        result.Put(key, value);
                    }
                    else if (key.Equals(PdfName.NAME))
                    {
                        // /Name labels the configuration only; the first
                        // source supplies the combined display label.
                        continue;
                    }
                    else if (!string.Equals(
                        CanonicalizePdfObject(existing),
                        CanonicalizePdfObject(value),
                        StringComparison.Ordinal))
                    {
                        throw new NotSupportedException(
                            "Los PDFs usan configuraciones de capas " +
                            "incompatibles. La combinacion se ha cancelado.");
                    }
                }
            }

            return result;
        }

        private static bool ContainsPdfName(
            IEnumerable<PdfName> values,
            PdfName expected)
        {
            foreach (var value in values)
            {
                if (value.Equals(expected))
                {
                    return true;
                }
            }

            return false;
        }

        private static void WriteCombinedOutlines(
            OutlineAwarePdfCopy copy,
            IList<MergedOutlineNode> outlines)
        {
            if (outlines == null || outlines.Count == 0)
            {
                return;
            }

            var rootReference =
                copy.PdfIndirectReference;
            ReserveOutlineReferences(copy, outlines);
            RelinkAndWriteOutlines(
                copy,
                outlines,
                rootReference);

            var root = new PdfDictionary(PdfName.OUTLINES);
            root.Put(
                PdfName.FIRST,
                outlines[0].Reference);
            root.Put(
                PdfName.LAST,
                outlines[outlines.Count - 1].Reference);
            var visibleCount = outlines.Count;
            foreach (var outline in outlines)
            {
                if (outline.IsOpen)
                {
                    visibleCount +=
                        CountVisibleDescendants(outline);
                }
            }

            root.Put(
                PdfName.COUNT,
                new PdfNumber(visibleCount));
            copy.AddToBody(root, rootReference);
            copy.ExtraCatalog.Put(
                PdfName.OUTLINES,
                rootReference);
        }

        private static void ReserveOutlineReferences(
            OutlineAwarePdfCopy copy,
            IList<MergedOutlineNode> nodes)
        {
            foreach (var node in nodes)
            {
                node.Reference =
                    copy.PdfIndirectReference;
                ReserveOutlineReferences(
                    copy,
                    node.Children);
            }
        }

        private static void RelinkAndWriteOutlines(
            OutlineAwarePdfCopy copy,
            IList<MergedOutlineNode> nodes,
            PdfIndirectReference parentReference)
        {
            for (var index = 0;
                index < nodes.Count;
                index++)
            {
                var node = nodes[index];
                var dictionary = node.Dictionary;
                RemoveOutlineStructuralKeys(dictionary);
                dictionary.Put(
                    PdfName.PARENT,
                    parentReference);
                if (index > 0)
                {
                    dictionary.Put(
                        PdfName.PREV,
                        nodes[index - 1].Reference);
                }

                if (index + 1 < nodes.Count)
                {
                    dictionary.Put(
                        PdfName.NEXT,
                        nodes[index + 1].Reference);
                }

                if (node.Children.Count > 0)
                {
                    dictionary.Put(
                        PdfName.FIRST,
                        node.Children[0].Reference);
                    dictionary.Put(
                        PdfName.LAST,
                        node.Children[
                            node.Children.Count - 1].Reference);
                    dictionary.Put(
                        PdfName.COUNT,
                        new PdfNumber(
                            node.IsOpen
                                ? CountVisibleDescendants(node)
                                : -CountVisibleDescendants(node)));
                    RelinkAndWriteOutlines(
                        copy,
                        node.Children,
                        node.Reference);
                }

                copy.AddToBody(
                    dictionary,
                    node.Reference);
            }
        }

        private static int CountVisibleDescendants(
            MergedOutlineNode node)
        {
            var count = node.Children.Count;
            foreach (var child in node.Children)
            {
                if (child.IsOpen)
                {
                    count +=
                        CountVisibleDescendants(child);
                }
            }

            return count;
        }

        private static bool IsOutlineStructuralKey(
            PdfName key)
        {
            return key.Equals(PdfName.PARENT) ||
                key.Equals(PdfName.PREV) ||
                key.Equals(PdfName.NEXT) ||
                key.Equals(PdfName.FIRST) ||
                key.Equals(PdfName.LAST) ||
                key.Equals(PdfName.COUNT);
        }

        private static void RemoveOutlineStructuralKeys(
            PdfDictionary dictionary)
        {
            dictionary.Remove(PdfName.PARENT);
            dictionary.Remove(PdfName.PREV);
            dictionary.Remove(PdfName.NEXT);
            dictionary.Remove(PdfName.FIRST);
            dictionary.Remove(PdfName.LAST);
            dictionary.Remove(PdfName.COUNT);
        }

        private static IList<MergedOutlineExpectation>
            CreateOutlineExpectations(
                IList<MergedOutlineNode> nodes)
        {
            var result =
                new List<MergedOutlineExpectation>(
                    nodes.Count);
            foreach (var node in nodes)
            {
                var expectation =
                    new MergedOutlineExpectation(
                        ComputeOutlineFingerprint(
                            node.Dictionary),
                        GetReferenceKey(node.Reference));
                foreach (var child in
                    CreateOutlineExpectations(node.Children))
                {
                    expectation.Children.Add(child);
                }

                result.Add(expectation);
            }

            return result;
        }

        private static string ComputeOutlineFingerprint(
            PdfDictionary dictionary)
        {
            var keys = new List<PdfName>();
            foreach (var key in dictionary.Keys)
            {
                if (!IsOutlineStructuralKey(key))
                {
                    keys.Add(key);
                }
            }

            keys.Sort(
                delegate(PdfName left, PdfName right)
                {
                    return string.CompareOrdinal(
                        left.ToString(),
                        right.ToString());
                });
            var result = new System.Text.StringBuilder();
            foreach (var key in keys)
            {
                result.Append(key.ToString());
                result.Append('=');
                result.Append(
                    CanonicalizePdfObject(
                        dictionary.Get(key)));
                result.Append(';');
            }

            return result.ToString();
        }

        private static string CanonicalizePdfObject(
            PdfObject value)
        {
            if (value == null)
            {
                return "<missing>";
            }

            var reference =
                value as PdfIndirectReference;
            if (reference != null)
            {
                return GetReferenceKey(reference);
            }

            var text = value as PdfString;
            if (text != null)
            {
                return "S:" + text.ToUnicodeString();
            }

            var name = value as PdfName;
            if (name != null)
            {
                return "N:" + name.ToString();
            }

            var array = value as PdfArray;
            if (array != null)
            {
                var result =
                    new System.Text.StringBuilder("A[");
                for (var index = 0;
                    index < array.Size;
                    index++)
                {
                    if (index > 0)
                    {
                        result.Append(',');
                    }

                    result.Append(
                        CanonicalizePdfObject(array[index]));
                }

                result.Append(']');
                return result.ToString();
            }

            var dictionary = value as PdfDictionary;
            if (dictionary != null)
            {
                var keys =
                    new List<PdfName>(dictionary.Keys);
                keys.Sort(
                    delegate(PdfName left, PdfName right)
                    {
                        return string.CompareOrdinal(
                            left.ToString(),
                            right.ToString());
                    });
                var result =
                    new System.Text.StringBuilder("D{");
                foreach (var key in keys)
                {
                    result.Append(key.ToString());
                    result.Append('=');
                    result.Append(
                        CanonicalizePdfObject(
                            dictionary.Get(key)));
                    result.Append(';');
                }

                result.Append('}');
                return result.ToString();
            }

            return value.Type.ToString(
                    CultureInfo.InvariantCulture) +
                ":" + value.ToString();
        }

        private static PdfObject ResolvePdfObject(
            PdfObject value)
        {
            return value == null
                ? null
                : PdfReader.GetPdfObject(value);
        }

        private static PdfDictionary ResolveDictionary(
            PdfObject value)
        {
            return ResolvePdfObject(value) as PdfDictionary;
        }

        private static string GetReferenceKey(
            PdfIndirectReference reference)
        {
            return reference == null
                ? string.Empty
                : reference.Number.ToString(
                    CultureInfo.InvariantCulture) +
                  ":" +
                  reference.Generation.ToString(
                    CultureInfo.InvariantCulture);
        }

        private static void AddNamedDestinationsForOutput(
            IDictionary<string, string> sourceDestinations,
            int pageOffset,
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
                var shiftedDestination = ShiftDestinationPageNumber(entry.Value, pageOffset);
                if (string.IsNullOrEmpty(entry.Key) || string.IsNullOrEmpty(shiftedDestination))
                {
                    continue;
                }

                var outputName = entry.Key;
                if (!usedNames.Add(outputName))
                {
                    var suffix = "__PDF" + sourceIndex.ToString(CultureInfo.InvariantCulture);
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

                outputDestinations[outputName] = shiftedDestination;
            }
        }

        private static string ShiftDestinationPageNumber(string destination, int pageOffset)
        {
            if (string.IsNullOrWhiteSpace(destination))
            {
                return destination;
            }

            var separatorIndex = destination.IndexOf(' ');
            var pageText = separatorIndex < 0
                ? destination
                : destination.Substring(0, separatorIndex);
            int pageNumber;
            if (!int.TryParse(
                pageText,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out pageNumber))
            {
                return destination;
            }

            return (pageNumber + pageOffset).ToString(CultureInfo.InvariantCulture) +
                (separatorIndex < 0 ? string.Empty : destination.Substring(separatorIndex));
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

            var needsNamespace = HasFieldNameCollision(sourceFieldNames, usedFieldNames);
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
                    prefixBase + suffixIndex.ToString(CultureInfo.InvariantCulture) + "_";
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

        private static void PrefixTopLevelFieldNames(PdfReader reader, string prefix)
        {
            var acroForm = reader.Catalog.GetAsDict(PdfName.ACROFORM);
            var fields = acroForm == null ? null : acroForm.GetAsArray(PdfName.FIELDS);
            if (fields == null)
            {
                return;
            }

            for (var index = 0; index < fields.Size; index++)
            {
                PrefixFirstNamedField(fields[index], prefix);
            }
        }

        private static void PrefixFirstNamedField(PdfObject fieldObject, string prefix)
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

        private sealed class ThrottledProgressReporter
        {
            private const int MinimumIntervalMilliseconds = 100;
            private readonly Action<PdfMergeProgress> callback;
            private readonly Stopwatch stopwatch;
            private int lastPercentage;

            public ThrottledProgressReporter(Action<PdfMergeProgress> callback)
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
                        (int)Math.Round(completedPages * 100D / totalPages));
                if (percentage == lastPercentage)
                {
                    return;
                }

                if (!force && stopwatch.ElapsedMilliseconds < MinimumIntervalMilliseconds)
                {
                    return;
                }

                callback(new PdfMergeProgress(completedPages, totalPages, sourcePath));
                lastPercentage = percentage;
                stopwatch.Restart();
            }
        }

        private static void ValidateMergedPdf(
            string tempPath,
            int expectedPageCount,
            MergeValidationExpectations expectations)
        {
            PdfReader validationReader = null;
            try
            {
                validationReader = new PdfReader(tempPath);
                if (validationReader.NumberOfPages != expectedPageCount)
                {
                    throw new InvalidDataException(
                        string.Format(
                            "La comprobacion final esperaba {0} paginas y encontro {1}.",
                            expectedPageCount,
                            validationReader.NumberOfPages));
                }

                ValidateMergedOutlines(
                    validationReader,
                    expectations.Outlines);
                ValidateOptionalContent(
                    validationReader,
                    expectations.OptionalContentGroupCount);
            }
            finally
            {
                if (validationReader != null)
                {
                    validationReader.Close();
                }
            }
        }

        private static void ValidateMergedOutlines(
            PdfReader reader,
            IList<MergedOutlineExpectation> expected)
        {
            var root = ResolveDictionary(
                reader.Catalog.Get(PdfName.OUTLINES));
            if (expected == null || expected.Count == 0)
            {
                if (root != null &&
                    root.Get(PdfName.FIRST) != null)
                {
                    throw new InvalidDataException(
                        "La salida contiene marcadores inesperados.");
                }

                return;
            }

            if (root == null)
            {
                throw new InvalidDataException(
                    "La combinacion ha perdido los marcadores.");
            }

            ValidateMergedOutlineSiblings(
                root.Get(PdfName.FIRST),
                expected,
                new HashSet<string>(
                    StringComparer.Ordinal));
        }

        private static void ValidateMergedOutlineSiblings(
            PdfObject firstObject,
            IList<MergedOutlineExpectation> expected,
            ISet<string> visited)
        {
            var current = firstObject;
            var index = 0;
            while (current != null)
            {
                if (index >= expected.Count)
                {
                    throw new InvalidDataException(
                        "La salida contiene marcadores adicionales.");
                }

                var reference =
                    current as PdfIndirectReference;
                if (reference == null ||
                    !visited.Add(GetReferenceKey(reference)))
                {
                    throw new InvalidDataException(
                        "La salida contiene un arbol de marcadores invalido.");
                }

                var dictionary =
                    ResolveDictionary(current);
                if (dictionary == null)
                {
                    throw new InvalidDataException(
                        "La salida contiene un marcador ilegible.");
                }

                var expectation = expected[index];
                if (!string.Equals(
                        expectation.ReferenceKey,
                        GetReferenceKey(reference),
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        expectation.Fingerprint,
                        ComputeOutlineFingerprint(dictionary),
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Una accion, destino o estilo de marcador no se " +
                        "ha conservado durante la combinacion.");
                }

                ValidateMergedOutlineSiblings(
                    dictionary.Get(PdfName.FIRST),
                    expectation.Children,
                    visited);
                current = dictionary.Get(PdfName.NEXT);
                index++;
            }

            if (index != expected.Count)
            {
                throw new InvalidDataException(
                    "La combinacion ha perdido parte del arbol de marcadores.");
            }
        }

        private static void ValidateOptionalContent(
            PdfReader reader,
            int expectedGroupCount)
        {
            var properties = ResolveDictionary(
                reader.Catalog.Get(PdfName.OCPROPERTIES));
            if (expectedGroupCount == 0)
            {
                return;
            }

            var groups = properties == null
                ? null
                : properties.GetAsArray(PdfName.OCGS);
            if (groups == null ||
                groups.Size != expectedGroupCount)
            {
                throw new InvalidDataException(
                    "Las capas necesarias para las acciones de marcador " +
                    "no se conservaron.");
            }

            var groupReferences = new HashSet<string>(
                StringComparer.Ordinal);
            for (var index = 0;
                index < groups.Size;
                index++)
            {
                var reference =
                    groups[index] as PdfIndirectReference;
                var dictionary =
                    ResolveDictionary(groups[index]);
                if (reference == null ||
                    dictionary == null ||
                    !PdfName.OCG.Equals(
                        dictionary.GetAsName(PdfName.TYPE)))
                {
                    throw new InvalidDataException(
                        "La salida contiene una definicion de capa invalida.");
                }

                groupReferences.Add(
                    GetReferenceKey(reference));
            }

            var outlines = ResolveDictionary(
                reader.Catalog.Get(PdfName.OUTLINES));
            if (outlines != null)
            {
                ValidateSetOcgActions(
                    outlines.Get(PdfName.FIRST),
                    groupReferences,
                    new HashSet<string>(
                        StringComparer.Ordinal));
            }
        }

        private static void ValidateSetOcgActions(
            PdfObject firstOutline,
            ISet<string> groupReferences,
            ISet<string> visitedOutlines)
        {
            var current = firstOutline;
            while (current != null)
            {
                var reference =
                    current as PdfIndirectReference;
                if (reference == null ||
                    !visitedOutlines.Add(
                        GetReferenceKey(reference)))
                {
                    throw new InvalidDataException(
                        "El arbol de marcadores de salida contiene un ciclo.");
                }

                var outline = ResolveDictionary(current);
                ValidateActionOptionalContentReferences(
                    outline == null
                        ? null
                        : outline.Get(PdfName.A),
                    groupReferences,
                    new HashSet<string>(
                        StringComparer.Ordinal),
                    0);
                ValidateSetOcgActions(
                    outline == null
                        ? null
                        : outline.Get(PdfName.FIRST),
                    groupReferences,
                    visitedOutlines);
                current = outline == null
                    ? null
                    : outline.Get(PdfName.NEXT);
            }
        }

        private static void
            ValidateActionOptionalContentReferences(
                PdfObject actionObject,
                ISet<string> groupReferences,
                ISet<string> activeActions,
                int depth)
        {
            if (actionObject == null)
            {
                return;
            }

            if (depth > 64)
            {
                throw new InvalidDataException(
                    "La cadena de acciones de salida es demasiado profunda.");
            }

            var reference =
                actionObject as PdfIndirectReference;
            var referenceKey = reference == null
                ? null
                : GetReferenceKey(reference);
            if (referenceKey != null &&
                !activeActions.Add(referenceKey))
            {
                throw new InvalidDataException(
                    "La salida contiene un ciclo de acciones.");
            }

            try
            {
                var action =
                    ResolveDictionary(actionObject);
                if (action == null)
                {
                    throw new InvalidDataException(
                        "La salida contiene una accion de marcador invalida.");
                }

                if (PdfName.SETOCGSTATE.Equals(
                        action.GetAsName(PdfName.S)))
                {
                    var state =
                        action.GetAsArray(PdfName.STATE);
                    if (state == null)
                    {
                        throw new InvalidDataException(
                            "Una accion SetOCGState perdio su estado.");
                    }

                    for (var index = 0;
                        index < state.Size;
                        index++)
                    {
                        var groupReference =
                            state[index] as PdfIndirectReference;
                        if (groupReference != null &&
                            !groupReferences.Contains(
                                GetReferenceKey(groupReference)))
                        {
                            throw new InvalidDataException(
                                "Una accion SetOCGState apunta a una capa " +
                                "que no figura en /OCProperties.");
                        }
                    }
                }

                var next = ResolvePdfObject(
                    action.Get(PdfName.NEXT));
                var nextArray = next as PdfArray;
                if (nextArray != null)
                {
                    for (var index = 0;
                        index < nextArray.Size;
                        index++)
                    {
                        ValidateActionOptionalContentReferences(
                            nextArray[index],
                            groupReferences,
                            activeActions,
                            depth + 1);
                    }
                }
                else if (next != null)
                {
                    ValidateActionOptionalContentReferences(
                        next,
                        groupReferences,
                        activeActions,
                        depth + 1);
                }
            }
            finally
            {
                if (referenceKey != null)
                {
                    activeActions.Remove(referenceKey);
                }
            }
        }

        private static void CommitTemporaryFile(string tempPath, string outputPath)
        {
            if (File.Exists(outputPath))
            {
                File.Replace(tempPath, outputPath, null, true);
                return;
            }

            File.Move(tempPath, outputPath);
        }

        private sealed class OutlineAwarePdfCopy : PdfCopy
        {
            public OutlineAwarePdfCopy(
                Document document,
                Stream output)
                : base(document, output)
            {
            }

            public PdfObject CopyForOutline(PdfObject value)
            {
                if (value == null)
                {
                    return PdfNull.PDFNULL;
                }

                return CopyObject(value);
            }
        }

        private sealed class MergedOutlineNode
        {
            public MergedOutlineNode(
                PdfDictionary dictionary,
                bool isOpen)
            {
                Dictionary = dictionary;
                IsOpen = isOpen;
                Children = new List<MergedOutlineNode>();
            }

            public PdfDictionary Dictionary { get; private set; }

            public bool IsOpen { get; private set; }

            public PdfIndirectReference Reference { get; set; }

            public List<MergedOutlineNode> Children
            {
                get;
                private set;
            }
        }

        private sealed class MergedOutlineExpectation
        {
            public MergedOutlineExpectation(
                string fingerprint,
                string referenceKey)
            {
                Fingerprint = fingerprint;
                ReferenceKey = referenceKey;
                Children =
                    new List<MergedOutlineExpectation>();
            }

            public string Fingerprint { get; private set; }

            public string ReferenceKey { get; private set; }

            public List<MergedOutlineExpectation> Children
            {
                get;
                private set;
            }
        }

        private sealed class MergedOptionalContent
        {
            public MergedOptionalContent()
            {
                OptionalContentGroups = new List<PdfObject>();
                OptionalContentGroupKeys =
                    new HashSet<string>(StringComparer.Ordinal);
                DefaultConfigurations =
                    new List<PdfDictionary>();
                AlternateConfigurations =
                    new List<PdfDictionary>();
                ExtraRootProperties =
                    new Dictionary<PdfName, PdfObject>();
            }

            public List<PdfObject> OptionalContentGroups
            {
                get;
                private set;
            }

            public HashSet<string> OptionalContentGroupKeys
            {
                get;
                private set;
            }

            public List<PdfDictionary> DefaultConfigurations
            {
                get;
                private set;
            }

            public List<PdfDictionary> AlternateConfigurations
            {
                get;
                private set;
            }

            public Dictionary<PdfName, PdfObject> ExtraRootProperties
            {
                get;
                private set;
            }
        }

        private sealed class MergeValidationExpectations
        {
            public MergeValidationExpectations(
                IList<MergedOutlineExpectation> outlines,
                int optionalContentGroupCount)
            {
                Outlines = outlines;
                OptionalContentGroupCount =
                    optionalContentGroupCount;
            }

            public IList<MergedOutlineExpectation> Outlines
            {
                get;
                private set;
            }

            public int OptionalContentGroupCount
            {
                get;
                private set;
            }
        }
    }
}

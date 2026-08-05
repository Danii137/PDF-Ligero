using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using iTextSharp.text.pdf;

namespace FirmaAutomatica
{
    internal enum PdfBookmarkDestinationMode
    {
        Xyz,
        Fit,
        FitHorizontal,
        FitVertical,
        FitRectangle,
        FitBoundingBox,
        FitBoundingBoxHorizontal,
        FitBoundingBoxVertical
    }

    /// <summary>
    /// A destination expressed in page-relative PDF coordinates. Zero percent
    /// is the left/top edge of the CropBox and one hundred is the right/bottom
    /// edge. Zoom is the PDF /XYZ scale factor (1.0 means 100 percent).
    /// </summary>
    internal sealed class PdfBookmarkDestination
    {
        public PdfBookmarkDestination(
            int pageNumber,
            double? topPositionPercent)
            : this(
                pageNumber,
                PdfBookmarkDestinationMode.Xyz,
                topPositionPercent,
                null,
                null,
                null,
                null)
        {
        }

        public PdfBookmarkDestination(
            int pageNumber,
            double? topPositionPercent,
            double? leftPositionPercent,
            double? zoom)
            : this(
                pageNumber,
                PdfBookmarkDestinationMode.Xyz,
                topPositionPercent,
                leftPositionPercent,
                null,
                null,
                zoom)
        {
        }

        public PdfBookmarkDestination(
            int pageNumber,
            PdfBookmarkDestinationMode mode,
            double? topPositionPercent,
            double? leftPositionPercent,
            double? bottomPositionPercent,
            double? rightPositionPercent,
            double? zoom)
            : this(
                pageNumber,
                mode,
                topPositionPercent,
                leftPositionPercent,
                bottomPositionPercent,
                rightPositionPercent,
                zoom,
                false)
        {
        }

        private PdfBookmarkDestination(
            int pageNumber,
            PdfBookmarkDestinationMode mode,
            double? topPositionPercent,
            double? leftPositionPercent,
            double? bottomPositionPercent,
            double? rightPositionPercent,
            double? zoom,
            bool preserveRawCoordinates)
        {
            ValidatePageNumber(pageNumber);
            if (!preserveRawCoordinates)
            {
                ValidatePercent(
                    topPositionPercent,
                    "topPositionPercent");
                ValidatePercent(
                    leftPositionPercent,
                    "leftPositionPercent");
                ValidatePercent(
                    bottomPositionPercent,
                    "bottomPositionPercent");
                ValidatePercent(
                    rightPositionPercent,
                    "rightPositionPercent");
            }
            ValidateZoom(zoom);
            ValidateModeParameters(
                mode,
                topPositionPercent,
                leftPositionPercent,
                bottomPositionPercent,
                rightPositionPercent);

            PageNumber = pageNumber;
            Mode = mode;
            TopPositionPercent = topPositionPercent;
            LeftPositionPercent = leftPositionPercent;
            BottomPositionPercent = bottomPositionPercent;
            RightPositionPercent = rightPositionPercent;
            Zoom = zoom;
        }

        internal static PdfBookmarkDestination FromPdf(
            int pageNumber,
            PdfBookmarkDestinationMode mode,
            double? topPositionPercent,
            double? leftPositionPercent,
            double? bottomPositionPercent,
            double? rightPositionPercent,
            double? zoom)
        {
            return new PdfBookmarkDestination(
                pageNumber,
                mode,
                topPositionPercent,
                leftPositionPercent,
                bottomPositionPercent,
                rightPositionPercent,
                zoom,
                true);
        }

        public int PageNumber { get; private set; }

        public PdfBookmarkDestinationMode Mode { get; private set; }

        public double? TopPositionPercent { get; private set; }

        public double? LeftPositionPercent { get; private set; }

        public double? BottomPositionPercent { get; private set; }

        public double? RightPositionPercent { get; private set; }

        public double? Zoom { get; private set; }

        internal PdfBookmarkDestination Clone()
        {
            return FromPdf(
                PageNumber,
                Mode,
                TopPositionPercent,
                LeftPositionPercent,
                BottomPositionPercent,
                RightPositionPercent,
                Zoom);
        }

        private static void ValidateModeParameters(
            PdfBookmarkDestinationMode mode,
            double? top,
            double? left,
            double? bottom,
            double? right)
        {
            if (mode == PdfBookmarkDestinationMode.FitRectangle &&
                (!top.HasValue ||
                 !left.HasValue ||
                 !bottom.HasValue ||
                 !right.HasValue))
            {
                throw new ArgumentException(
                    "El destino FitR necesita los cuatro limites.");
            }
        }

        private static void ValidatePageNumber(int pageNumber)
        {
            if (pageNumber < 1)
            {
                throw new ArgumentOutOfRangeException(
                    "pageNumber",
                    "La pagina de destino debe ser mayor que cero.");
            }
        }

        private static void ValidatePercent(
            double? value,
            string parameterName)
        {
            if (!value.HasValue)
            {
                return;
            }

            if (double.IsNaN(value.Value) ||
                double.IsInfinity(value.Value) ||
                value.Value < 0D ||
                value.Value > 100D)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "La posicion debe estar entre 0 y 100.");
            }
        }

        private static void ValidateZoom(double? zoom)
        {
            if (!zoom.HasValue)
            {
                return;
            }

            if (double.IsNaN(zoom.Value) ||
                double.IsInfinity(zoom.Value) ||
                zoom.Value <= 0D)
            {
                throw new ArgumentOutOfRangeException(
                    "zoom",
                    "El zoom debe ser un factor mayor que cero.");
            }
        }
    }

    internal sealed class PdfBookmarkPageGeometry
    {
        internal PdfBookmarkPageGeometry(
            int pageNumber,
            double cropLeft,
            double cropBottom,
            double cropRight,
            double cropTop,
            int clockwiseRotationDegrees)
        {
            PageNumber = pageNumber;
            CropLeft = cropLeft;
            CropBottom = cropBottom;
            CropRight = cropRight;
            CropTop = cropTop;
            ClockwiseRotationDegrees = clockwiseRotationDegrees;
        }

        public int PageNumber { get; private set; }

        public double CropLeft { get; private set; }

        public double CropBottom { get; private set; }

        public double CropRight { get; private set; }

        public double CropTop { get; private set; }

        public double CropWidth
        {
            get { return CropRight - CropLeft; }
        }

        public double CropHeight
        {
            get { return CropTop - CropBottom; }
        }

        public int ClockwiseRotationDegrees { get; private set; }
    }

    internal sealed class PdfBookmarkPdfPoint
    {
        internal PdfBookmarkPdfPoint(
            int pageNumber,
            double x,
            double y,
            bool hasX,
            bool hasY)
        {
            PageNumber = pageNumber;
            X = x;
            Y = y;
            HasX = hasX;
            HasY = hasY;
        }

        public int PageNumber { get; private set; }

        public double X { get; private set; }

        public double Y { get; private set; }

        public bool HasX { get; private set; }

        public bool HasY { get; private set; }
    }

    internal sealed class PdfBookmarkNode
    {
        private readonly List<PdfBookmarkNode> children;
        private readonly ReadOnlyCollection<PdfBookmarkNode>
            readOnlyChildren;

        internal PdfBookmarkNode(
            string id,
            string title,
            PdfBookmarkDestination destination,
            bool isOpen,
            bool isDestinationEditable,
            bool isOriginal,
            int sourceObjectNumber,
            int sourceObjectGeneration,
            string sourcePathKey,
            bool titleChanged,
            bool destinationChanged)
        {
            Id = id;
            Title = title ?? string.Empty;
            Destination = destination;
            IsOpen = isOpen;
            IsDestinationEditable = isDestinationEditable;
            IsOriginal = isOriginal;
            SourceObjectNumber = sourceObjectNumber;
            SourceObjectGeneration = sourceObjectGeneration;
            SourcePathKey = sourcePathKey ?? string.Empty;
            TitleChanged = titleChanged;
            DestinationChanged = destinationChanged;
            children = new List<PdfBookmarkNode>();
            readOnlyChildren =
                new ReadOnlyCollection<PdfBookmarkNode>(children);
        }

        public string Id { get; private set; }

        public string Title { get; private set; }

        public PdfBookmarkDestination Destination { get; private set; }

        public IList<PdfBookmarkNode> Children
        {
            get { return readOnlyChildren; }
        }

        public bool IsOpen { get; private set; }

        public bool IsDestinationEditable { get; private set; }

        internal bool IsOriginal { get; private set; }

        internal int SourceObjectNumber { get; private set; }

        internal int SourceObjectGeneration { get; private set; }

        internal string SourcePathKey { get; private set; }

        internal bool TitleChanged { get; private set; }

        internal bool DestinationChanged { get; private set; }

        internal List<PdfBookmarkNode> MutableChildren
        {
            get { return children; }
        }

        internal void Rename(string title)
        {
            Title = title;
            TitleChanged = true;
        }

        internal void ChangeDestination(
            PdfBookmarkDestination destination)
        {
            Destination = destination;
            DestinationChanged = true;
            IsDestinationEditable = true;
        }

        internal void SetOpen(bool isOpen)
        {
            IsOpen = isOpen;
        }

        internal PdfBookmarkNode Clone()
        {
            var clone = new PdfBookmarkNode(
                Id,
                Title,
                Destination == null ? null : Destination.Clone(),
                IsOpen,
                IsDestinationEditable,
                IsOriginal,
                SourceObjectNumber,
                SourceObjectGeneration,
                SourcePathKey,
                TitleChanged,
                DestinationChanged);
            foreach (var child in children)
            {
                clone.children.Add(child.Clone());
            }

            return clone;
        }
    }

    internal sealed class PdfBookmarkDocument
    {
        private readonly List<PdfBookmarkNode> bookmarks;
        private readonly ReadOnlyCollection<PdfBookmarkNode>
            readOnlyBookmarks;
        private readonly List<PdfBookmarkPageGeometry> pageGeometries;
        private readonly ReadOnlyCollection<PdfBookmarkPageGeometry>
            readOnlyPageGeometries;

        internal PdfBookmarkDocument(
            string sourcePath,
            long sourceLength,
            long sourceLastWriteUtcTicks,
            string sourceFingerprint,
            int pageCount,
            bool containsDigitalSignatures,
            bool openedWithFullPermissions,
            List<PdfBookmarkPageGeometry> pageGeometries,
            List<PdfBookmarkNode> bookmarks)
        {
            SourcePath = sourcePath;
            SourceLength = sourceLength;
            SourceLastWriteUtcTicks = sourceLastWriteUtcTicks;
            SourceFingerprint = sourceFingerprint;
            PageCount = pageCount;
            ContainsDigitalSignatures = containsDigitalSignatures;
            OpenedWithFullPermissions = openedWithFullPermissions;
            this.pageGeometries = pageGeometries;
            this.bookmarks = bookmarks;
            readOnlyPageGeometries =
                new ReadOnlyCollection<PdfBookmarkPageGeometry>(
                    this.pageGeometries);
            readOnlyBookmarks =
                new ReadOnlyCollection<PdfBookmarkNode>(this.bookmarks);
        }

        public string SourcePath { get; private set; }

        public int PageCount { get; private set; }

        public bool ContainsDigitalSignatures { get; private set; }

        public bool OpenedWithFullPermissions { get; private set; }

        public IList<PdfBookmarkPageGeometry> PageGeometries
        {
            get { return readOnlyPageGeometries; }
        }

        public IList<PdfBookmarkNode> Bookmarks
        {
            get { return readOnlyBookmarks; }
        }

        internal long SourceLength { get; private set; }

        internal long SourceLastWriteUtcTicks { get; private set; }

        internal string SourceFingerprint { get; private set; }

        internal List<PdfBookmarkNode> MutableBookmarks
        {
            get { return bookmarks; }
        }

        public PdfBookmarkPageGeometry GetPageGeometry(int pageNumber)
        {
            if (pageNumber < 1 || pageNumber > pageGeometries.Count)
            {
                throw new ArgumentOutOfRangeException(
                    "pageNumber",
                    "La pagina indicada no existe.");
            }

            return pageGeometries[pageNumber - 1];
        }

        internal PdfBookmarkDocument Clone()
        {
            var clonedNodes =
                new List<PdfBookmarkNode>(bookmarks.Count);
            foreach (var node in bookmarks)
            {
                clonedNodes.Add(node.Clone());
            }

            return new PdfBookmarkDocument(
                SourcePath,
                SourceLength,
                SourceLastWriteUtcTicks,
                SourceFingerprint,
                PageCount,
                ContainsDigitalSignatures,
                OpenedWithFullPermissions,
                new List<PdfBookmarkPageGeometry>(pageGeometries),
                clonedNodes);
        }
    }

    internal sealed class PdfBookmarkProgress
    {
        internal PdfBookmarkProgress(
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

    internal sealed class PdfBookmarkSaveResult
    {
        internal PdfBookmarkSaveResult(
            string outputPath,
            int bookmarkCount,
            bool digitalSignaturesInvalidated)
        {
            OutputPath = outputPath;
            BookmarkCount = bookmarkCount;
            DigitalSignaturesInvalidated =
                digitalSignaturesInvalidated;
        }

        public string OutputPath { get; private set; }

        public int BookmarkCount { get; private set; }

        public bool DigitalSignaturesInvalidated { get; private set; }

        public string DigitalSignatureWarning
        {
            get
            {
                return DigitalSignaturesInvalidated
                    ? PdfBookmarkService
                        .DigitalSignatureInvalidationWarning
                    : string.Empty;
            }
        }
    }

    /// <summary>
    /// Reads and edits the PDF outline tree without rasterizing or copying page
    /// streams. Existing outline dictionaries are reused, and only structural
    /// links, explicitly changed titles, and explicitly changed destinations
    /// are touched. This preserves actions that SimpleBookmark cannot model,
    /// such as JavaScript and SetOCGState.
    /// </summary>
    internal static class PdfBookmarkService
    {
        public const string DigitalSignatureInvalidationWarning =
            "La firma digital anterior permanece incrustada y puede seguir " +
            "verificandose, pero la edicion de marcadores es una modificacion " +
            "posterior que no queda cubierta por esa firma. Su estado final " +
            "tambien depende de las restricciones definidas por el firmante.";

        public const string SourceChangedMessage =
            "El PDF cambio desde que se abrio el editor de marcadores. " +
            "Vuelve a abrir el editor para trabajar sobre la revision actual.";

        public const string XfaUnsupportedMessage =
            "Los formularios XFA pueden reconstruir su estructura al abrirse. " +
            "PDF Ligero no reescribe los marcadores de un XFA porque no puede " +
            "garantizar que la copia resultante siga siendo válida. Guarda antes " +
            "una copia como PDF normal.";

        private const int MaximumOutlineDepth = 256;
        private const int MaximumOutlineItems = 100000;

        private static readonly PdfName[] StructuralOutlineKeys =
        {
            PdfName.PARENT,
            PdfName.PREV,
            PdfName.NEXT,
            PdfName.FIRST,
            PdfName.LAST,
            PdfName.COUNT
        };

        public static PdfBookmarkDocument Load(string sourcePdfPath)
        {
            var sourcePath =
                NormalizeExistingPdfPath(sourcePdfPath);
            var sourceInfo = new FileInfo(sourcePath);
            var sourceLength = sourceInfo.Length;
            var sourceLastWriteUtcTicks =
                sourceInfo.LastWriteTimeUtc.Ticks;
            var sourceFingerprint =
                ComputeContentFingerprint(sourcePath);

            using (OpenSourceReadGuard(sourcePath))
            {
                PdfReader reader = null;
                try
                {
                    reader = OpenPdfReader(sourcePath);
                    var geometries = ReadPageGeometries(reader);
                    var pageReferences =
                        ReadPageNumbersByReference(reader);
                    var namedDestinations =
                        ReadNamedDestinations(reader);
                    var records = ReadOutlineTree(reader);
                    var bookmarkCount = 0;
                    var bookmarks = ConvertRecordsToNodes(
                        records,
                        pageReferences,
                        namedDestinations,
                        geometries,
                        ref bookmarkCount);
                    var signatures = reader.AcroFields == null
                        ? null
                        : reader.AcroFields.GetSignatureNames();

                    return new PdfBookmarkDocument(
                        sourcePath,
                        sourceLength,
                        sourceLastWriteUtcTicks,
                        sourceFingerprint,
                        reader.NumberOfPages,
                        signatures != null && signatures.Count > 0,
                        reader.IsOpenedWithFullPermissions,
                        geometries,
                        bookmarks);
                }
                catch (InvalidDataException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException(
                        "No se pudieron leer los marcadores de \"" +
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
        }

        public static PdfBookmarkDocument CloneDocument(
            PdfBookmarkDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException("document");
            }

            return document.Clone();
        }

        public static PdfBookmarkNode Create(
            PdfBookmarkDocument document,
            string parentId,
            int index,
            string title,
            PdfBookmarkDestination destination)
        {
            EnsureDocument(document);
            ValidateDestination(document, destination);
            var container = GetContainer(
                document,
                parentId,
                true);
            ValidateInsertIndex(container, index);

            var node = new PdfBookmarkNode(
                Guid.NewGuid().ToString("N"),
                NormalizeTitle(title),
                destination == null ? null : destination.Clone(),
                true,
                true,
                false,
                0,
                0,
                string.Empty,
                true,
                destination != null);
            container.Insert(index, node);
            return node;
        }

        public static void Rename(
            PdfBookmarkDocument document,
            string nodeId,
            string title)
        {
            EnsureDocument(document);
            var location = FindNode(document, nodeId);
            location.Node.Rename(NormalizeTitle(title));
        }

        public static void Delete(
            PdfBookmarkDocument document,
            string nodeId)
        {
            EnsureDocument(document);
            var location = FindNode(document, nodeId);
            location.Container.RemoveAt(location.Index);
        }

        public static void Move(
            PdfBookmarkDocument document,
            string nodeId,
            string newParentId,
            int newIndex)
        {
            EnsureDocument(document);
            var source = FindNode(document, nodeId);
            PdfBookmarkNode newParent = null;
            var target = document.MutableBookmarks;
            if (!string.IsNullOrWhiteSpace(newParentId))
            {
                newParent = FindNode(document, newParentId).Node;
                if (ReferenceEquals(source.Node, newParent) ||
                    ContainsNode(source.Node, newParent.Id))
                {
                    throw new InvalidOperationException(
                        "Un marcador no puede ser hijo de si mismo ni de " +
                        "uno de sus descendientes.");
                }

                target = newParent.MutableChildren;
            }

            var targetCountAfterRemoval = target.Count;
            if (ReferenceEquals(source.Container, target))
            {
                targetCountAfterRemoval--;
            }

            if (newIndex < 0 ||
                newIndex > targetCountAfterRemoval)
            {
                throw new ArgumentOutOfRangeException(
                    "newIndex",
                    "La posicion de destino no es valida.");
            }

            source.Container.RemoveAt(source.Index);
            target.Insert(newIndex, source.Node);
        }

        public static void SetDestination(
            PdfBookmarkDocument document,
            string nodeId,
            PdfBookmarkDestination destination)
        {
            EnsureDocument(document);
            if (destination == null)
            {
                throw new ArgumentNullException("destination");
            }

            ValidateDestination(document, destination);
            FindNode(document, nodeId).Node.ChangeDestination(
                destination.Clone());
        }

        public static void SetOpen(
            PdfBookmarkDocument document,
            string nodeId,
            bool isOpen)
        {
            EnsureDocument(document);
            FindNode(document, nodeId).Node.SetOpen(isOpen);
        }

        public static PdfBookmarkDestination
            CreateDestinationFromPdfPoint(
                PdfBookmarkDocument document,
                int pageNumber,
                double pdfX,
                double pdfY,
                double? zoom)
        {
            EnsureDocument(document);
            var geometry = document.GetPageGeometry(pageNumber);
            var leftPercent =
                100D *
                (pdfX - geometry.CropLeft) /
                Math.Max(0.000001D, geometry.CropWidth);
            var topPercent =
                100D *
                (geometry.CropTop - pdfY) /
                Math.Max(0.000001D, geometry.CropHeight);

            return new PdfBookmarkDestination(
                pageNumber,
                ClampPercent(topPercent),
                ClampPercent(leftPercent),
                zoom);
        }

        public static PdfBookmarkPdfPoint GetPdfPoint(
            PdfBookmarkDocument document,
            PdfBookmarkDestination destination)
        {
            EnsureDocument(document);
            if (destination == null)
            {
                throw new ArgumentNullException("destination");
            }

            ValidateDestination(document, destination);
            var geometry =
                document.GetPageGeometry(destination.PageNumber);
            var x = destination.LeftPositionPercent.HasValue
                ? geometry.CropLeft +
                    geometry.CropWidth *
                    destination.LeftPositionPercent.Value / 100D
                : geometry.CropLeft;
            var y = destination.TopPositionPercent.HasValue
                ? geometry.CropTop -
                    geometry.CropHeight *
                    destination.TopPositionPercent.Value / 100D
                : geometry.CropTop;
            return new PdfBookmarkPdfPoint(
                destination.PageNumber,
                x,
                y,
                destination.LeftPositionPercent.HasValue,
                destination.TopPositionPercent.HasValue);
        }

        public static string SuggestOutputPath(string sourcePdfPath)
        {
            var sourcePath =
                NormalizeExistingPdfPath(sourcePdfPath);
            var directory = Path.GetDirectoryName(sourcePath);
            var baseName = Path.GetFileNameWithoutExtension(sourcePath);
            var candidate = Path.Combine(
                directory,
                baseName + "_marcadores.pdf");
            var suffix = 2;
            while (File.Exists(candidate))
            {
                candidate = Path.Combine(
                    directory,
                    baseName + "_marcadores_" +
                    suffix.ToString(CultureInfo.InvariantCulture) +
                    ".pdf");
                suffix++;
            }

            return candidate;
        }

        public static PdfBookmarkSaveResult Save(
            string sourcePdfPath,
            PdfBookmarkDocument document,
            string outputPath,
            Action<PdfBookmarkProgress> reportProgress,
            CancellationToken cancellationToken)
        {
            EnsureDocument(document);
            cancellationToken.ThrowIfCancellationRequested();
            var sourcePath =
                NormalizeExistingPdfPath(sourcePdfPath);
            var normalizedOutputPath =
                ValidateOutputPath(sourcePath, outputPath);
            ValidateModel(document);
            EnsureSourceQuickIdentity(sourcePath, document);
            var outputDirectory =
                Path.GetDirectoryName(normalizedOutputPath);
            var temporaryPath = Path.Combine(
                outputDirectory,
                "." +
                Path.GetFileNameWithoutExtension(normalizedOutputPath) +
                "." + Guid.NewGuid().ToString("N") + ".tmp");

            Report(reportProgress, 0, 4, "Preparando marcadores");
            using (OpenSourceReadGuard(sourcePath))
            {
                EnsureSourceIdentity(sourcePath, document);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var expectations = WriteEditedPdf(
                        sourcePath,
                        document,
                        temporaryPath,
                        reportProgress,
                        cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    Report(
                        reportProgress,
                        3,
                        4,
                        "Comprobando el PDF");
                    ValidateWrittenPdf(
                        temporaryPath,
                        document,
                        expectations);
                    cancellationToken.ThrowIfCancellationRequested();
                    EnsureSourceIdentity(sourcePath, document);
                    CommitTemporaryFile(
                        temporaryPath,
                        normalizedOutputPath);
                    Report(
                        reportProgress,
                        4,
                        4,
                        "Marcadores guardados");

                    return new PdfBookmarkSaveResult(
                        normalizedOutputPath,
                        CountNodes(document.Bookmarks),
                        document.ContainsDigitalSignatures);
                }
                finally
                {
                    TryDeleteFile(temporaryPath);
                }
            }
        }

        public static PdfBookmarkSaveResult Save(
            string sourcePdfPath,
            PdfBookmarkDocument document,
            string outputPath,
            Action<PdfBookmarkProgress> reportProgress)
        {
            return Save(
                sourcePdfPath,
                document,
                outputPath,
                reportProgress,
                CancellationToken.None);
        }

        public static PdfBookmarkSaveResult Save(
            string sourcePdfPath,
            string outputPath,
            PdfBookmarkDocument document)
        {
            return Save(
                sourcePdfPath,
                document,
                outputPath,
                null,
                CancellationToken.None);
        }

        private static WriteExpectations WriteEditedPdf(
            string sourcePath,
            PdfBookmarkDocument document,
            string temporaryPath,
            Action<PdfBookmarkProgress> reportProgress,
            CancellationToken cancellationToken)
        {
            PdfReader reader = null;
            PdfStamper stamper = null;
            FileStream output = null;
            try
            {
                reader = OpenPdfReader(sourcePath);
                if (!reader.IsOpenedWithFullPermissions)
                {
                    throw new UnauthorizedAccessException(
                        "El PDF está protegido y no permite editar marcadores.");
                }

                // El resto de operaciones estructurales ya bloqueaban XFA. Aqui
                // faltaba, y reescribir el arbol de marcadores de un formulario
                // dinamico puede dejar una copia dañada sin avisar.
                if (HasXfa(reader))
                {
                    throw new NotSupportedException(XfaUnsupportedMessage);
                }

                if (reader.NumberOfPages != document.PageCount)
                {
                    throw new InvalidOperationException(
                        SourceChangedMessage);
                }

                var sourceExpectations =
                    CaptureDocumentExpectations(reader);
                var sourceRecords = ReadOutlineTree(reader);
                var recordsByObject =
                    IndexRecordsByObject(sourceRecords);
                var recordsByPath =
                    IndexRecordsByPath(sourceRecords);
                var preservedFingerprints =
                    CapturePreservedFingerprints(
                        document.Bookmarks,
                        recordsByObject,
                        recordsByPath);

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
                // PdfStamper otherwise refreshes Producer/ModDate even when
                // only the outline is edited. Supplying the complete source
                // dictionary keeps the user's document metadata unchanged.
                stamper.MoreInfo =
                    CloneMetadata(sourceExpectations.Metadata);

                cancellationToken.ThrowIfCancellationRequested();
                Report(
                    reportProgress,
                    1,
                    4,
                    "Actualizando el arbol");
                var root = PrepareOutlineRoot(
                    reader,
                    stamper,
                    document.Bookmarks.Count > 0);
                var savedNodes = PrepareSavedNodes(
                    document.Bookmarks,
                    recordsByObject,
                    recordsByPath,
                    stamper);
                if (root != null)
                {
                    RelinkOutlineTree(
                        savedNodes,
                        root.Reference,
                        reader,
                        stamper,
                        document);
                    RelinkRoot(root, savedNodes, stamper);
                }

                UpdateCatalogOutlineReference(
                    reader,
                    stamper,
                    root);
                cancellationToken.ThrowIfCancellationRequested();
                Report(
                    reportProgress,
                    2,
                    4,
                    "Escribiendo copia segura");

                stamper.Close();
                stamper = null;
                output = null;
                reader = null;

                return new WriteExpectations(
                    sourceExpectations,
                    preservedFingerprints,
                    CaptureSavedNodeReferences(savedNodes));
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
                else
                {
                    if (output != null)
                    {
                        output.Dispose();
                    }

                    if (reader != null)
                    {
                        reader.Close();
                    }
                }
            }
        }

        private static OutlineRoot PrepareOutlineRoot(
            PdfReader reader,
            PdfStamper stamper,
            bool isRequired)
        {
            if (!isRequired)
            {
                return null;
            }

            var rootObject = reader.Catalog.Get(PdfName.OUTLINES);
            var rootDictionary =
                ResolveDictionary(rootObject);
            var rootReference =
                rootObject as PdfIndirectReference;
            var isNewOrDirect = false;

            if (rootDictionary == null)
            {
                rootDictionary =
                    new PdfDictionary(PdfName.OUTLINES);
                rootReference =
                    stamper.Writer.PdfIndirectReference;
                isNewOrDirect = true;
            }
            else if (rootReference == null)
            {
                rootReference =
                    stamper.Writer.PdfIndirectReference;
                isNewOrDirect = true;
            }
            else
            {
                var sourceReference =
                    rootReference as PRIndirectReference;
                if (sourceReference != null)
                {
                    rootDictionary.IndRef = sourceReference;
                }
            }

            return new OutlineRoot(
                rootDictionary,
                rootReference,
                isNewOrDirect);
        }

        private static List<SavedOutlineNode> PrepareSavedNodes(
            IList<PdfBookmarkNode> nodes,
            IDictionary<string, RawOutlineRecord> recordsByObject,
            IDictionary<string, RawOutlineRecord> recordsByPath,
            PdfStamper stamper)
        {
            var result = new List<SavedOutlineNode>(nodes.Count);
            foreach (var node in nodes)
            {
                RawOutlineRecord record = null;
                PdfDictionary dictionary;
                PdfIndirectReference reference;
                var mustAddToBody = false;
                if (node.IsOriginal)
                {
                    record = ResolveOriginalRecord(
                        node,
                        recordsByObject,
                        recordsByPath);
                    dictionary = record.Dictionary;
                    reference = record.Reference;
                    if (reference == null)
                    {
                        reference =
                            stamper.Writer.PdfIndirectReference;
                        mustAddToBody = true;
                    }
                    else
                    {
                        var sourceReference =
                            reference as PRIndirectReference;
                        if (sourceReference != null)
                        {
                            dictionary.IndRef = sourceReference;
                        }
                    }
                }
                else
                {
                    dictionary = new PdfDictionary();
                    reference = stamper.Writer.PdfIndirectReference;
                    mustAddToBody = true;
                }

                var saved = new SavedOutlineNode(
                    node,
                    dictionary,
                    reference,
                    mustAddToBody);
                saved.Children.AddRange(
                    PrepareSavedNodes(
                        node.Children,
                        recordsByObject,
                        recordsByPath,
                        stamper));
                result.Add(saved);
            }

            return result;
        }

        private static void RelinkOutlineTree(
            IList<SavedOutlineNode> nodes,
            PdfIndirectReference parentReference,
            PdfReader reader,
            PdfStamper stamper,
            PdfBookmarkDocument document)
        {
            for (var index = 0; index < nodes.Count; index++)
            {
                var saved = nodes[index];
                var dictionary = saved.Dictionary;
                RemoveStructuralLinks(dictionary);
                dictionary.Put(PdfName.PARENT, parentReference);
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

                if (!saved.Node.IsOriginal ||
                    saved.Node.TitleChanged)
                {
                    dictionary.Put(
                        PdfName.TITLE,
                        new PdfString(
                            saved.Node.Title,
                            PdfObject.TEXT_UNICODE));
                }

                if (saved.Node.DestinationChanged)
                {
                    ApplyChangedDestination(
                        dictionary,
                        reader,
                        stamper,
                        document,
                        saved.Node.Destination);
                }

                if (saved.Children.Count > 0)
                {
                    dictionary.Put(
                        PdfName.FIRST,
                        saved.Children[0].Reference);
                    dictionary.Put(
                        PdfName.LAST,
                        saved.Children[
                            saved.Children.Count - 1].Reference);
                    var count = saved.Node.IsOpen
                        ? CountVisibleDescendants(saved)
                        : -CountVisibleDescendants(saved);
                    dictionary.Put(
                        PdfName.COUNT,
                        new PdfNumber(count));
                    RelinkOutlineTree(
                        saved.Children,
                        saved.Reference,
                        reader,
                        stamper,
                        document);
                }

                if (saved.MustAddToBody)
                {
                    stamper.Writer.AddToBody(
                        dictionary,
                        saved.Reference);
                }
                else
                {
                    stamper.MarkUsed(dictionary);
                }
            }
        }

        private static void ApplyChangedDestination(
            PdfDictionary outline,
            PdfReader reader,
            PdfStamper stamper,
            PdfBookmarkDocument document,
            PdfBookmarkDestination destination)
        {
            var destinationArray = CreateDestinationArray(
                reader,
                document,
                destination);
            var actionObject = outline.Get(PdfName.A);
            var action = ResolveDictionary(actionObject);
            if (action != null &&
                PdfName.GOTO.Equals(
                    action.GetAsName(PdfName.S)))
            {
                action.Put(PdfName.D, destinationArray);
                var actionReference =
                    actionObject as PRIndirectReference;
                if (actionReference != null)
                {
                    action.IndRef = actionReference;
                    stamper.MarkUsed(action);
                }

                return;
            }

            // Replacing an external action is an explicit retargeting. For a
            // local GoTo action only /D is changed above so /Next and all
            // opaque action keys survive.
            outline.Remove(PdfName.A);
            outline.Put(PdfName.DEST, destinationArray);
        }

        private static void RelinkRoot(
            OutlineRoot root,
            IList<SavedOutlineNode> nodes,
            PdfStamper stamper)
        {
            RemoveStructuralLinks(root.Dictionary);
            if (nodes.Count > 0)
            {
                root.Dictionary.Put(
                    PdfName.FIRST,
                    nodes[0].Reference);
                root.Dictionary.Put(
                    PdfName.LAST,
                    nodes[nodes.Count - 1].Reference);
                var visibleCount = nodes.Count;
                foreach (var node in nodes)
                {
                    if (node.Node.IsOpen)
                    {
                        visibleCount +=
                            CountVisibleDescendants(node);
                    }
                }

                root.Dictionary.Put(
                    PdfName.COUNT,
                    new PdfNumber(visibleCount));
            }

            if (root.MustAddToBody)
            {
                stamper.Writer.AddToBody(
                    root.Dictionary,
                    root.Reference);
            }
            else
            {
                stamper.MarkUsed(root.Dictionary);
            }
        }

        private static void UpdateCatalogOutlineReference(
            PdfReader reader,
            PdfStamper stamper,
            OutlineRoot root)
        {
            var catalog = reader.Catalog;
            if (root == null)
            {
                catalog.Remove(PdfName.OUTLINES);
            }
            else
            {
                catalog.Put(PdfName.OUTLINES, root.Reference);
            }

            stamper.MarkUsed(catalog);
        }

        private static PdfArray CreateDestinationArray(
            PdfReader reader,
            PdfBookmarkDocument document,
            PdfBookmarkDestination destination)
        {
            if (destination == null)
            {
                throw new InvalidOperationException(
                    "Todo marcador nuevo o redirigido necesita un destino.");
            }

            ValidateDestination(document, destination);
            var geometry =
                document.GetPageGeometry(destination.PageNumber);
            var result = new PdfArray();
            result.Add(reader.GetPageOrigRef(destination.PageNumber));
            if (destination.Mode == PdfBookmarkDestinationMode.Fit)
            {
                result.Add(PdfName.FIT);
                return result;
            }

            if (destination.Mode ==
                PdfBookmarkDestinationMode.FitBoundingBox)
            {
                result.Add(PdfName.FITB);
                return result;
            }

            if (destination.Mode ==
                    PdfBookmarkDestinationMode.FitHorizontal ||
                destination.Mode ==
                    PdfBookmarkDestinationMode
                        .FitBoundingBoxHorizontal)
            {
                result.Add(
                    destination.Mode ==
                        PdfBookmarkDestinationMode.FitHorizontal
                        ? PdfName.FITH
                        : PdfName.FITBH);
                result.Add(
                    ToPdfNumberOrNull(
                        destination.TopPositionPercent,
                        geometry.CropTop,
                        -geometry.CropHeight));
                return result;
            }

            if (destination.Mode ==
                    PdfBookmarkDestinationMode.FitVertical ||
                destination.Mode ==
                    PdfBookmarkDestinationMode
                        .FitBoundingBoxVertical)
            {
                result.Add(
                    destination.Mode ==
                        PdfBookmarkDestinationMode.FitVertical
                        ? PdfName.FITV
                        : PdfName.FITBV);
                result.Add(
                    ToPdfNumberOrNull(
                        destination.LeftPositionPercent,
                        geometry.CropLeft,
                        geometry.CropWidth));
                return result;
            }

            if (destination.Mode ==
                PdfBookmarkDestinationMode.FitRectangle)
            {
                result.Add(PdfName.FITR);
                result.Add(
                    ToRequiredPdfNumber(
                        destination.LeftPositionPercent,
                        geometry.CropLeft,
                        geometry.CropWidth));
                result.Add(
                    ToRequiredPdfNumber(
                        destination.BottomPositionPercent,
                        geometry.CropTop,
                        -geometry.CropHeight));
                result.Add(
                    ToRequiredPdfNumber(
                        destination.RightPositionPercent,
                        geometry.CropLeft,
                        geometry.CropWidth));
                result.Add(
                    ToRequiredPdfNumber(
                        destination.TopPositionPercent,
                        geometry.CropTop,
                        -geometry.CropHeight));
                return result;
            }

            result.Add(PdfName.XYZ);
            result.Add(
                ToPdfNumberOrNull(
                    destination.LeftPositionPercent,
                    geometry.CropLeft,
                    geometry.CropWidth));
            result.Add(
                ToPdfNumberOrNull(
                    destination.TopPositionPercent,
                    geometry.CropTop,
                    -geometry.CropHeight));
            result.Add(
                destination.Zoom.HasValue
                    ? (PdfObject)new PdfNumber(destination.Zoom.Value)
                    : new PdfNumber(0));
            return result;
        }

        private static PdfObject ToPdfNumberOrNull(
            double? percent,
            double origin,
            double extent)
        {
            return percent.HasValue
                ? (PdfObject)new PdfNumber(
                    origin + extent * percent.Value / 100D)
                : PdfNull.PDFNULL;
        }

        private static PdfNumber ToRequiredPdfNumber(
            double? percent,
            double origin,
            double extent)
        {
            if (!percent.HasValue)
            {
                throw new InvalidOperationException(
                    "El destino FitR esta incompleto.");
            }

            return new PdfNumber(
                origin + extent * percent.Value / 100D);
        }

        private static void ValidateWrittenPdf(
            string outputPath,
            PdfBookmarkDocument document,
            WriteExpectations expectations)
        {
            if (!File.Exists(outputPath) ||
                new FileInfo(outputPath).Length <= 0)
            {
                throw new InvalidDataException(
                    "No se pudo crear una copia PDF valida.");
            }

            PdfReader reader = null;
            try
            {
                reader = OpenPdfReader(outputPath);
                ValidateDocumentExpectations(
                    reader,
                    expectations.Document);
                var records = ReadOutlineTree(reader);
                var pageReferences =
                    ReadPageNumbersByReference(reader);
                var namedDestinations =
                    ReadNamedDestinations(reader);
                ValidateSavedTree(
                    document.Bookmarks,
                    records,
                    expectations.NodeReferences,
                    expectations.PreservedFingerprints,
                    pageReferences,
                    namedDestinations,
                    document.PageGeometries);
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidDataException(
                    "La copia con marcadores no supero la comprobacion: " +
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

        private static void ValidateSavedTree(
            IList<PdfBookmarkNode> expectedNodes,
            IList<RawOutlineRecord> actualRecords,
            IDictionary<string, string> nodeReferences,
            IDictionary<string, string> preservedFingerprints,
            IDictionary<string, int> pageReferences,
            IDictionary<string, PdfObject> namedDestinations,
            IList<PdfBookmarkPageGeometry> geometries)
        {
            if (expectedNodes.Count != actualRecords.Count)
            {
                throw new InvalidDataException(
                    "El numero de marcadores raiz no coincide.");
            }

            for (var index = 0; index < expectedNodes.Count; index++)
            {
                var expected = expectedNodes[index];
                var actual = actualRecords[index];
                string expectedReference;
                if (!nodeReferences.TryGetValue(
                        expected.Id,
                        out expectedReference) ||
                    !string.Equals(
                        expectedReference,
                        GetReferenceKey(actual.Reference),
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Cambio inesperado en la identidad de un marcador.");
                }

                var actualTitle =
                    ReadTitle(actual.Dictionary);
                if (!string.Equals(
                        expected.Title,
                        actualTitle,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "El titulo de un marcador no coincide: \"" +
                        expected.Title + "\" / \"" +
                        actualTitle + "\".");
                }

                if (expected.Children.Count > 0)
                {
                    var count =
                        actual.Dictionary.GetAsNumber(PdfName.COUNT);
                    if (count == null ||
                        (count.IntValue >= 0) != expected.IsOpen)
                    {
                        throw new InvalidDataException(
                            "El estado abierto/cerrado de un marcador " +
                            "no coincide.");
                    }
                }

                if (expected.DestinationChanged)
                {
                    var parsed = ReadDestination(
                        actual.Dictionary,
                        pageReferences,
                        namedDestinations,
                        geometries);
                    EnsureDestinationsEquivalent(
                        expected.Destination,
                        parsed);
                }

                if (expected.IsOriginal)
                {
                    string preserved;
                    if (!preservedFingerprints.TryGetValue(
                            expected.Id,
                            out preserved) ||
                        !string.Equals(
                            preserved,
                            ComputePreservedFingerprint(
                                actual.Dictionary,
                                expected.DestinationChanged),
                            StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            "Una accion o propiedad avanzada de marcador " +
                            "cambio durante la escritura.");
                    }
                }

                ValidateSavedTree(
                    expected.Children,
                    actual.Children,
                    nodeReferences,
                    preservedFingerprints,
                    pageReferences,
                    namedDestinations,
                    geometries);
            }
        }

        private static void EnsureDestinationsEquivalent(
            PdfBookmarkDestination expected,
            PdfBookmarkDestination actual)
        {
            if (expected == null || actual == null ||
                expected.PageNumber != actual.PageNumber ||
                expected.Mode != actual.Mode ||
                !NullableNearlyEqual(
                    expected.TopPositionPercent,
                    actual.TopPositionPercent,
                    0.02D,
                    false) ||
                !NullableNearlyEqual(
                    expected.LeftPositionPercent,
                    actual.LeftPositionPercent,
                    0.02D,
                    false) ||
                !NullableNearlyEqual(
                    expected.BottomPositionPercent,
                    actual.BottomPositionPercent,
                    0.02D,
                    false) ||
                !NullableNearlyEqual(
                    expected.RightPositionPercent,
                    actual.RightPositionPercent,
                    0.02D,
                    false) ||
                !NullableNearlyEqual(
                    expected.Zoom,
                    actual.Zoom,
                    0.001D,
                    true))
            {
                throw new InvalidDataException(
                    "El destino de un marcador no coincide.");
            }
        }

        private static bool NullableNearlyEqual(
            double? expected,
            double? actual,
            double tolerance,
            bool zeroEqualsNull)
        {
            if (zeroEqualsNull &&
                !expected.HasValue &&
                actual.HasValue &&
                Math.Abs(actual.Value) <= tolerance)
            {
                return true;
            }

            if (expected.HasValue != actual.HasValue)
            {
                return false;
            }

            return !expected.HasValue ||
                Math.Abs(expected.Value - actual.Value) <= tolerance;
        }

        private static DocumentExpectations
            CaptureDocumentExpectations(PdfReader reader)
        {
            var pageContentReferences = new List<string>();
            for (var page = 1; page <= reader.NumberOfPages; page++)
            {
                var pageDictionary = reader.GetPageN(page);
                pageContentReferences.Add(
                    CanonicalizePdfObject(
                        pageDictionary == null
                            ? null
                            : pageDictionary.Get(PdfName.CONTENTS)));
            }

            var formValues =
                new SortedDictionary<string, string>(
                    StringComparer.Ordinal);
            if (reader.AcroFields != null)
            {
                foreach (var field in reader.AcroFields.Fields)
                {
                    formValues[field.Key] =
                        reader.AcroFields.GetField(field.Key) ??
                        string.Empty;
                }
            }

            return new DocumentExpectations(
                reader.NumberOfPages,
                pageContentReferences,
                CloneMetadata(reader.Info),
                ComputeBytesHash(reader.Metadata),
                formValues,
                CountWidgets(reader));
        }

        private static void ValidateDocumentExpectations(
            PdfReader reader,
            DocumentExpectations expected)
        {
            if (reader.NumberOfPages != expected.PageCount)
            {
                throw new InvalidDataException(
                    "El numero de paginas cambio.");
            }

            for (var page = 1; page <= reader.NumberOfPages; page++)
            {
                var dictionary = reader.GetPageN(page);
                var actual = CanonicalizePdfObject(
                    dictionary == null
                        ? null
                        : dictionary.Get(PdfName.CONTENTS));
                if (!string.Equals(
                        expected.PageContentReferences[page - 1],
                        actual,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "El contenido de una pagina cambio.");
                }
            }

            foreach (var item in expected.Metadata)
            {
                // ModDate is intentionally refreshed by an incremental PDF
                // revision. All descriptive/user metadata remains exact.
                if (string.Equals(
                        item.Key,
                        "ModDate",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                string actualValue;
                if (!reader.Info.TryGetValue(
                        item.Key,
                        out actualValue) ||
                    !string.Equals(
                        item.Value,
                        actualValue,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "El metadato \"" + item.Key +
                        "\" no se conservo.");
                }
            }

            if (!string.Equals(
                    expected.XmpHash,
                    ComputeBytesHash(reader.Metadata),
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Los metadatos XMP del PDF cambiaron.");
            }

            var actualFields =
                new SortedDictionary<string, string>(
                    StringComparer.Ordinal);
            if (reader.AcroFields != null)
            {
                foreach (var field in reader.AcroFields.Fields)
                {
                    actualFields[field.Key] =
                        reader.AcroFields.GetField(field.Key) ??
                        string.Empty;
                }
            }

            if (!DictionariesEqual(
                    expected.FormValues,
                    actualFields) ||
                expected.WidgetCount != CountWidgets(reader))
            {
                throw new InvalidDataException(
                    "Los campos de formulario no se conservaron.");
            }
        }

        private static Dictionary<string, string>
            CapturePreservedFingerprints(
                IList<PdfBookmarkNode> nodes,
                IDictionary<string, RawOutlineRecord> recordsByObject,
                IDictionary<string, RawOutlineRecord> recordsByPath)
        {
            var result = new Dictionary<string, string>(
                StringComparer.Ordinal);
            CapturePreservedFingerprints(
                nodes,
                recordsByObject,
                recordsByPath,
                result);
            return result;
        }

        private static void CapturePreservedFingerprints(
            IList<PdfBookmarkNode> nodes,
            IDictionary<string, RawOutlineRecord> recordsByObject,
            IDictionary<string, RawOutlineRecord> recordsByPath,
            IDictionary<string, string> result)
        {
            foreach (var node in nodes)
            {
                if (node.IsOriginal)
                {
                    var record = ResolveOriginalRecord(
                        node,
                        recordsByObject,
                        recordsByPath);
                    result[node.Id] =
                        ComputePreservedFingerprint(
                            record.Dictionary,
                            node.DestinationChanged);
                }

                CapturePreservedFingerprints(
                    node.Children,
                    recordsByObject,
                    recordsByPath,
                    result);
            }
        }

        private static string ComputePreservedFingerprint(
            PdfDictionary dictionary,
            bool destinationChanged)
        {
            var keys = new List<PdfName>();
            foreach (var key in dictionary.Keys)
            {
                if (key.Equals(PdfName.TITLE) ||
                    IsStructuralKey(key) ||
                    (destinationChanged &&
                        key.Equals(PdfName.DEST)))
                {
                    continue;
                }

                if (destinationChanged &&
                    key.Equals(PdfName.A))
                {
                    var action = ResolveDictionary(
                        dictionary.Get(key));
                    if (action == null ||
                        !PdfName.GOTO.Equals(
                            action.GetAsName(PdfName.S)))
                    {
                        // An explicit retarget may replace an external
                        // action. A local GoTo action is retained below.
                        continue;
                    }
                }

                keys.Add(key);
            }

            keys.Sort(
                delegate(PdfName left, PdfName right)
                {
                    return string.CompareOrdinal(
                        left.ToString(),
                        right.ToString());
                });
            var value = new StringBuilder();
            foreach (var key in keys)
            {
                value.Append(key.ToString());
                value.Append('=');
                value.Append(
                    destinationChanged &&
                    key.Equals(PdfName.A)
                        ? CanonicalizeGoToActionWithoutDestination(
                            dictionary.Get(key))
                        : CanonicalizePdfObject(
                            dictionary.Get(key)));
                value.Append(';');
            }

            return ComputeTextHash(value.ToString());
        }

        private static string
            CanonicalizeGoToActionWithoutDestination(
                PdfObject actionObject)
        {
            var action = ResolveDictionary(actionObject);
            if (action == null)
            {
                return "<missing-action>";
            }

            var keys = new List<PdfName>();
            foreach (var key in action.Keys)
            {
                if (!key.Equals(PdfName.D))
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
            var result = new StringBuilder();
            var reference =
                actionObject as PdfIndirectReference;
            if (reference != null)
            {
                result.Append(GetReferenceKey(reference));
                result.Append('|');
            }

            result.Append("D{");
            foreach (var key in keys)
            {
                result.Append(key.ToString());
                result.Append('=');
                result.Append(
                    CanonicalizePdfObject(action.Get(key)));
                result.Append(';');
            }

            result.Append('}');
            return result.ToString();
        }

        private static string CanonicalizePdfObject(
            PdfObject value)
        {
            if (value == null)
            {
                return "<missing>";
            }

            var reference = value as PdfIndirectReference;
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
                var result = new StringBuilder("A[");
                for (var index = 0; index < array.Size; index++)
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
                var keys = new List<PdfName>(dictionary.Keys);
                keys.Sort(
                    delegate(PdfName left, PdfName right)
                    {
                        return string.CompareOrdinal(
                            left.ToString(),
                            right.ToString());
                    });
                var result = new StringBuilder("D{");
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

        private static List<RawOutlineRecord> ReadOutlineTree(
            PdfReader reader)
        {
            var root = ResolveDictionary(
                reader.Catalog.Get(PdfName.OUTLINES));
            if (root == null)
            {
                return new List<RawOutlineRecord>();
            }

            var visited = new HashSet<string>(
                StringComparer.Ordinal);
            var count = 0;
            return ReadOutlineSiblings(
                root.Get(PdfName.FIRST),
                string.Empty,
                0,
                visited,
                ref count);
        }

        private static List<RawOutlineRecord>
            ReadOutlineSiblings(
                PdfObject firstObject,
                string parentPath,
                int depth,
                ISet<string> visited,
                ref int count)
        {
            if (depth > MaximumOutlineDepth)
            {
                throw new InvalidDataException(
                    "El arbol de marcadores supera la profundidad segura.");
            }

            var result = new List<RawOutlineRecord>();
            var currentObject = firstObject;
            var siblingIndex = 0;
            while (currentObject != null)
            {
                count++;
                if (count > MaximumOutlineItems)
                {
                    throw new InvalidDataException(
                        "El PDF contiene demasiados marcadores.");
                }

                var dictionary =
                    ResolveDictionary(currentObject);
                if (dictionary == null)
                {
                    throw new InvalidDataException(
                        "El arbol de marcadores contiene un elemento invalido.");
                }

                var reference =
                    currentObject as PdfIndirectReference;
                var path = string.IsNullOrEmpty(parentPath)
                    ? siblingIndex.ToString(
                        CultureInfo.InvariantCulture)
                    : parentPath + "/" +
                        siblingIndex.ToString(
                            CultureInfo.InvariantCulture);
                var visitKey = reference == null
                    ? "path:" + path
                    : GetReferenceKey(reference);
                if (!visited.Add(visitKey))
                {
                    throw new InvalidDataException(
                        "El arbol de marcadores contiene un ciclo.");
                }

                var record = new RawOutlineRecord(
                    dictionary,
                    reference,
                    path);
                record.Children.AddRange(
                    ReadOutlineSiblings(
                        dictionary.Get(PdfName.FIRST),
                        path,
                        depth + 1,
                        visited,
                        ref count));
                result.Add(record);
                currentObject = dictionary.Get(PdfName.NEXT);
                siblingIndex++;
            }

            return result;
        }

        private static List<PdfBookmarkNode>
            ConvertRecordsToNodes(
                IList<RawOutlineRecord> records,
                IDictionary<string, int> pageReferences,
                IDictionary<string, PdfObject> namedDestinations,
                IList<PdfBookmarkPageGeometry> geometries,
                ref int bookmarkCount)
        {
            var result =
                new List<PdfBookmarkNode>(records.Count);
            foreach (var record in records)
            {
                bookmarkCount++;
                var destination = ReadDestination(
                    record.Dictionary,
                    pageReferences,
                    namedDestinations,
                    geometries);
                var node = new PdfBookmarkNode(
                    Guid.NewGuid().ToString("N"),
                    ReadTitle(record.Dictionary),
                    destination,
                    IsOutlineOpen(record.Dictionary),
                    IsInternalDestination(
                        record.Dictionary),
                    true,
                    record.Reference == null
                        ? 0
                        : record.Reference.Number,
                    record.Reference == null
                        ? 0
                        : record.Reference.Generation,
                    record.Path,
                    false,
                    false);
                node.MutableChildren.AddRange(
                    ConvertRecordsToNodes(
                        record.Children,
                        pageReferences,
                        namedDestinations,
                        geometries,
                        ref bookmarkCount));
                result.Add(node);
            }

            return result;
        }

        private static PdfBookmarkDestination ReadDestination(
            PdfDictionary outline,
            IDictionary<string, int> pageReferences,
            IDictionary<string, PdfObject> namedDestinations,
            IList<PdfBookmarkPageGeometry> geometries)
        {
            var destinationObject =
                outline.Get(PdfName.DEST);
            var action =
                ResolveDictionary(outline.Get(PdfName.A));
            if (destinationObject == null &&
                action != null &&
                PdfName.GOTO.Equals(
                    action.GetAsName(PdfName.S)))
            {
                destinationObject = action.Get(PdfName.D);
            }

            destinationObject =
                ResolveDestinationObject(
                    destinationObject,
                    namedDestinations);
            var array =
                ResolvePdfObject(destinationObject) as PdfArray;
            if (array == null || array.Size < 2)
            {
                return null;
            }

            int pageNumber;
            if (!TryResolveDestinationPage(
                    array[0],
                    pageReferences,
                    out pageNumber) ||
                pageNumber < 1 ||
                pageNumber > geometries.Count)
            {
                return null;
            }

            var geometry = geometries[pageNumber - 1];
            var kind =
                ResolvePdfObject(array[1]) as PdfName;
            var mode = PdfBookmarkDestinationMode.Xyz;
            double? left = null;
            double? top = null;
            double? bottom = null;
            double? right = null;
            double? zoom = null;
            if (PdfName.XYZ.Equals(kind))
            {
                left = ReadNumber(array, 2);
                top = ReadNumber(array, 3);
                zoom = ReadNumber(array, 4);
                if (zoom.HasValue &&
                    Math.Abs(zoom.Value) < 0.000001D)
                {
                    zoom = null;
                }
            }
            else if (PdfName.FITH.Equals(kind) ||
                     PdfName.FITBH.Equals(kind))
            {
                mode = PdfName.FITH.Equals(kind)
                    ? PdfBookmarkDestinationMode.FitHorizontal
                    : PdfBookmarkDestinationMode
                        .FitBoundingBoxHorizontal;
                top = ReadNumber(array, 2);
            }
            else if (PdfName.FITV.Equals(kind) ||
                     PdfName.FITBV.Equals(kind))
            {
                mode = PdfName.FITV.Equals(kind)
                    ? PdfBookmarkDestinationMode.FitVertical
                    : PdfBookmarkDestinationMode
                        .FitBoundingBoxVertical;
                left = ReadNumber(array, 2);
            }
            else if (PdfName.FITR.Equals(kind))
            {
                mode = PdfBookmarkDestinationMode.FitRectangle;
                left = ReadNumber(array, 2);
                bottom = ReadNumber(array, 3);
                right = ReadNumber(array, 4);
                top = ReadNumber(array, 5);
            }
            else if (PdfName.FITB.Equals(kind))
            {
                mode = PdfBookmarkDestinationMode.FitBoundingBox;
            }
            else if (PdfName.FIT.Equals(kind))
            {
                mode = PdfBookmarkDestinationMode.Fit;
            }
            else
            {
                return null;
            }

            var leftPercent = left.HasValue
                ? (double?)(
                    100D *
                    (left.Value - geometry.CropLeft) /
                    Math.Max(0.000001D, geometry.CropWidth))
                : null;
            var topPercent = top.HasValue
                ? (double?)(
                    100D *
                    (geometry.CropTop - top.Value) /
                    Math.Max(0.000001D, geometry.CropHeight))
                : null;
            var bottomPercent = bottom.HasValue
                ? (double?)(
                    100D *
                    (geometry.CropTop - bottom.Value) /
                    Math.Max(0.000001D, geometry.CropHeight))
                : null;
            var rightPercent = right.HasValue
                ? (double?)(
                    100D *
                    (right.Value - geometry.CropLeft) /
                    Math.Max(0.000001D, geometry.CropWidth))
                : null;
            return PdfBookmarkDestination.FromPdf(
                pageNumber,
                mode,
                topPercent,
                leftPercent,
                bottomPercent,
                rightPercent,
                zoom);
        }

        private static PdfObject ResolveDestinationObject(
            PdfObject destination,
            IDictionary<string, PdfObject> namedDestinations)
        {
            var resolved = ResolvePdfObject(destination);
            var name = resolved as PdfName;
            var text = resolved as PdfString;
            string key = null;
            if (name != null)
            {
                key = "N:" +
                    PdfName.DecodeName(name.ToString());
            }
            else if (text != null)
            {
                key = "S:" + text.ToUnicodeString();
            }

            PdfObject mapped;
            return key != null &&
                namedDestinations.TryGetValue(key, out mapped)
                    ? ResolvePdfObject(mapped)
                    : resolved;
        }

        private static Dictionary<string, PdfObject>
            ReadNamedDestinations(PdfReader reader)
        {
            var result = new Dictionary<string, PdfObject>(
                StringComparer.Ordinal);
            AddNamedDestinations(
                result,
                reader.GetNamedDestination(true));
            return result;
        }

        private static void AddNamedDestinations(
            IDictionary<string, PdfObject> target,
            IDictionary<object, PdfObject> source)
        {
            if (source == null)
            {
                return;
            }

            foreach (var item in source)
            {
                var key = NormalizeDestinationName(item.Key);
                if (!string.IsNullOrEmpty(key))
                {
                    target[
                        (item.Key is PdfName ? "N:" : "S:") +
                        key] = item.Value;
                }
            }
        }

        private static string NormalizeDestinationName(object value)
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

            var raw = value as string;
            return raw ?? (value == null ? null : value.ToString());
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
            IDictionary<string, int> pageReferences,
            out int pageNumber)
        {
            pageNumber = 0;
            var reference = value as PdfIndirectReference;
            if (reference != null)
            {
                return pageReferences.TryGetValue(
                    GetReferenceKey(reference),
                    out pageNumber);
            }

            var number =
                ResolvePdfObject(value) as PdfNumber;
            if (number != null)
            {
                pageNumber = number.IntValue + 1;
                return true;
            }

            return false;
        }

        private static List<PdfBookmarkPageGeometry>
            ReadPageGeometries(PdfReader reader)
        {
            var result =
                new List<PdfBookmarkPageGeometry>(
                    reader.NumberOfPages);
            for (var page = 1;
                page <= reader.NumberOfPages;
                page++)
            {
                var crop = reader.GetCropBox(page) ??
                    reader.GetPageSize(page);
                if (crop == null)
                {
                    throw new InvalidDataException(
                        "La pagina " +
                        page.ToString(
                            CultureInfo.InvariantCulture) +
                        " no tiene un tamano valido.");
                }

                result.Add(
                    new PdfBookmarkPageGeometry(
                        page,
                        crop.Left,
                        crop.Bottom,
                        crop.Right,
                        crop.Top,
                        NormalizeRotation(
                            reader.GetPageRotation(page))));
            }

            return result;
        }

        private static Dictionary<string, RawOutlineRecord>
            IndexRecordsByObject(
                IList<RawOutlineRecord> records)
        {
            var result =
                new Dictionary<string, RawOutlineRecord>(
                    StringComparer.Ordinal);
            IndexRecords(records, result, null);
            return result;
        }

        private static Dictionary<string, RawOutlineRecord>
            IndexRecordsByPath(
                IList<RawOutlineRecord> records)
        {
            var result =
                new Dictionary<string, RawOutlineRecord>(
                    StringComparer.Ordinal);
            IndexRecords(records, null, result);
            return result;
        }

        private static void IndexRecords(
            IList<RawOutlineRecord> records,
            IDictionary<string, RawOutlineRecord> byObject,
            IDictionary<string, RawOutlineRecord> byPath)
        {
            foreach (var record in records)
            {
                if (byObject != null &&
                    record.Reference != null)
                {
                    byObject[
                        GetReferenceKey(record.Reference)] =
                        record;
                }

                if (byPath != null)
                {
                    byPath[record.Path] = record;
                }

                IndexRecords(
                    record.Children,
                    byObject,
                    byPath);
            }
        }

        private static RawOutlineRecord ResolveOriginalRecord(
            PdfBookmarkNode node,
            IDictionary<string, RawOutlineRecord> recordsByObject,
            IDictionary<string, RawOutlineRecord> recordsByPath)
        {
            RawOutlineRecord record;
            if (node.SourceObjectNumber > 0 &&
                recordsByObject.TryGetValue(
                    GetReferenceKey(
                        node.SourceObjectNumber,
                        node.SourceObjectGeneration),
                    out record))
            {
                return record;
            }

            if (!string.IsNullOrEmpty(node.SourcePathKey) &&
                recordsByPath.TryGetValue(
                    node.SourcePathKey,
                    out record))
            {
                return record;
            }

            throw new InvalidOperationException(
                SourceChangedMessage);
        }

        private static Dictionary<string, string>
            CaptureSavedNodeReferences(
                IList<SavedOutlineNode> nodes)
        {
            var result = new Dictionary<string, string>(
                StringComparer.Ordinal);
            CaptureSavedNodeReferences(nodes, result);
            return result;
        }

        private static void CaptureSavedNodeReferences(
            IList<SavedOutlineNode> nodes,
            IDictionary<string, string> result)
        {
            foreach (var node in nodes)
            {
                result[node.Node.Id] =
                    GetReferenceKey(node.Reference);
                CaptureSavedNodeReferences(
                    node.Children,
                    result);
            }
        }

        private static void RemoveStructuralLinks(
            PdfDictionary dictionary)
        {
            foreach (var key in StructuralOutlineKeys)
            {
                dictionary.Remove(key);
            }
        }

        private static bool IsStructuralKey(PdfName key)
        {
            foreach (var structuralKey in StructuralOutlineKeys)
            {
                if (structuralKey.Equals(key))
                {
                    return true;
                }
            }

            return false;
        }

        private static int CountVisibleDescendants(
            SavedOutlineNode node)
        {
            var count = node.Children.Count;
            foreach (var child in node.Children)
            {
                if (child.Node.IsOpen)
                {
                    count += CountVisibleDescendants(child);
                }
            }

            return count;
        }

        private static bool IsOutlineOpen(PdfDictionary dictionary)
        {
            var count = dictionary.GetAsNumber(PdfName.COUNT);
            return count == null || count.IntValue >= 0;
        }

        private static bool IsInternalDestination(
            PdfDictionary dictionary)
        {
            if (dictionary.Get(PdfName.DEST) != null)
            {
                return true;
            }

            var action =
                ResolveDictionary(dictionary.Get(PdfName.A));
            return action != null &&
                PdfName.GOTO.Equals(
                    action.GetAsName(PdfName.S));
        }

        private static string ReadTitle(
            PdfDictionary dictionary)
        {
            var title =
                dictionary.GetAsString(PdfName.TITLE);
            return title == null
                ? string.Empty
                : title.ToUnicodeString();
        }

        private static double? ReadNumber(
            PdfArray array,
            int index)
        {
            if (array == null ||
                index < 0 ||
                index >= array.Size)
            {
                return null;
            }

            var value = ResolvePdfObject(array[index]);
            if (value == null || value.IsNull())
            {
                return null;
            }

            var number = value as PdfNumber;
            return number == null
                ? null
                : (double?)number.DoubleValue;
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

        /// <summary>
        /// Misma deteccion doble que PdfAcroFormService: el paquete /XFA del
        /// catalogo y el estado real de AcroFields.
        /// </summary>
        private static bool HasXfa(PdfReader reader)
        {
            var catalog = reader == null ? null : reader.Catalog;
            var acroForm = ResolveDictionary(
                catalog == null
                    ? null
                    : catalog.Get(PdfName.ACROFORM));
            if (acroForm != null && acroForm.Get(PdfName.XFA) != null)
            {
                return true;
            }

            return reader != null &&
                reader.AcroFields != null &&
                reader.AcroFields.Xfa != null &&
                reader.AcroFields.Xfa.XfaPresent;
        }

        private static int NormalizeRotation(int rotation)
        {
            var normalized = rotation % 360;
            return normalized < 0
                ? normalized + 360
                : normalized;
        }

        private static double ClampPercent(double value)
        {
            return Math.Max(0D, Math.Min(100D, value));
        }

        private static void ValidateModel(
            PdfBookmarkDocument document)
        {
            var ids = new HashSet<string>(
                StringComparer.Ordinal);
            var originalKeys = new HashSet<string>(
                StringComparer.Ordinal);
            var count = 0;
            ValidateNodes(
                document,
                document.Bookmarks,
                ids,
                originalKeys,
                0,
                ref count);
        }

        private static void ValidateNodes(
            PdfBookmarkDocument document,
            IList<PdfBookmarkNode> nodes,
            ISet<string> ids,
            ISet<string> originalKeys,
            int depth,
            ref int count)
        {
            if (depth > MaximumOutlineDepth)
            {
                throw new InvalidOperationException(
                    "El arbol de marcadores es demasiado profundo.");
            }

            foreach (var node in nodes)
            {
                count++;
                if (count > MaximumOutlineItems)
                {
                    throw new InvalidOperationException(
                        "El documento contiene demasiados marcadores.");
                }

                if (node == null ||
                    string.IsNullOrEmpty(node.Id) ||
                    !ids.Add(node.Id))
                {
                    throw new InvalidOperationException(
                        "El modelo de marcadores contiene elementos duplicados.");
                }

                NormalizeTitle(node.Title);
                if (node.Destination != null)
                {
                    ValidateDestination(
                        document,
                        node.Destination);
                }

                if (!node.IsOriginal &&
                    node.Destination == null)
                {
                    throw new InvalidOperationException(
                        "Los marcadores nuevos necesitan un destino.");
                }

                if (node.IsOriginal)
                {
                    var key = node.SourceObjectNumber > 0
                        ? GetReferenceKey(
                            node.SourceObjectNumber,
                            node.SourceObjectGeneration)
                        : "path:" + node.SourcePathKey;
                    if (!originalKeys.Add(key))
                    {
                        throw new InvalidOperationException(
                            "Un marcador original aparece mas de una vez.");
                    }
                }

                ValidateNodes(
                    document,
                    node.Children,
                    ids,
                    originalKeys,
                    depth + 1,
                    ref count);
            }
        }

        private static void ValidateDestination(
            PdfBookmarkDocument document,
            PdfBookmarkDestination destination)
        {
            if (destination == null)
            {
                return;
            }

            if (destination.PageNumber < 1 ||
                destination.PageNumber > document.PageCount)
            {
                throw new ArgumentOutOfRangeException(
                    "destination",
                    "La pagina de destino no existe.");
            }
        }

        private static string NormalizeTitle(string title)
        {
            var normalized = (title ?? string.Empty).Trim();
            if (normalized.Length == 0)
            {
                throw new ArgumentException(
                    "El titulo del marcador no puede estar vacio.",
                    "title");
            }

            if (normalized.Length > 4096)
            {
                throw new ArgumentException(
                    "El titulo del marcador es demasiado largo.",
                    "title");
            }

            return normalized;
        }

        private static void EnsureDocument(
            PdfBookmarkDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException("document");
            }
        }

        private static NodeLocation FindNode(
            PdfBookmarkDocument document,
            string nodeId)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException(
                    "Falta el identificador del marcador.",
                    "nodeId");
            }

            NodeLocation result;
            if (!TryFindNode(
                    document.MutableBookmarks,
                    nodeId,
                    out result))
            {
                throw new KeyNotFoundException(
                    "El marcador ya no existe.");
            }

            return result;
        }

        private static bool TryFindNode(
            List<PdfBookmarkNode> container,
            string nodeId,
            out NodeLocation result)
        {
            for (var index = 0;
                index < container.Count;
                index++)
            {
                var node = container[index];
                if (string.Equals(
                        node.Id,
                        nodeId,
                        StringComparison.Ordinal))
                {
                    result = new NodeLocation(
                        node,
                        container,
                        index);
                    return true;
                }

                if (TryFindNode(
                        node.MutableChildren,
                        nodeId,
                        out result))
                {
                    return true;
                }
            }

            result = null;
            return false;
        }

        private static List<PdfBookmarkNode> GetContainer(
            PdfBookmarkDocument document,
            string parentId,
            bool allowRoot)
        {
            if (string.IsNullOrWhiteSpace(parentId))
            {
                if (!allowRoot)
                {
                    throw new ArgumentException(
                        "Falta el marcador padre.",
                        "parentId");
                }

                return document.MutableBookmarks;
            }

            return FindNode(document, parentId)
                .Node.MutableChildren;
        }

        private static bool ContainsNode(
            PdfBookmarkNode node,
            string id)
        {
            foreach (var child in node.Children)
            {
                if (string.Equals(
                        child.Id,
                        id,
                        StringComparison.Ordinal) ||
                    ContainsNode(child, id))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ValidateInsertIndex(
            IList<PdfBookmarkNode> container,
            int index)
        {
            if (index < 0 || index > container.Count)
            {
                throw new ArgumentOutOfRangeException(
                    "index",
                    "La posicion del marcador no es valida.");
            }
        }

        private static int CountNodes(
            IList<PdfBookmarkNode> nodes)
        {
            var count = nodes.Count;
            foreach (var node in nodes)
            {
                count += CountNodes(node.Children);
            }

            return count;
        }

        private static string NormalizeExistingPdfPath(
            string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException(
                    "Falta la ruta del PDF.",
                    "path");
            }

            var normalized = Path.GetFullPath(path);
            if (!File.Exists(normalized))
            {
                throw new FileNotFoundException(
                    "No se encuentra el PDF.",
                    normalized);
            }

            if (!string.Equals(
                    Path.GetExtension(normalized),
                    ".pdf",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "El archivo debe ser un PDF.",
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
                    "Falta la ruta de salida.",
                    "outputPath");
            }

            var normalized = Path.GetFullPath(outputPath);
            if (string.Equals(
                    normalized,
                    sourcePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "El PDF original nunca se sobrescribe directamente.");
            }

            if (!string.Equals(
                    Path.GetExtension(normalized),
                    ".pdf",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "La salida debe tener extension PDF.",
                    "outputPath");
            }

            var directory = Path.GetDirectoryName(normalized);
            if (string.IsNullOrWhiteSpace(directory) ||
                !Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException(
                    "La carpeta de salida no existe.");
            }

            return normalized;
        }

        private static PdfReader OpenPdfReader(string path)
        {
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

        private static IDisposable OpenSourceReadGuard(
            string sourcePath)
        {
            return new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.RandomAccess);
        }

        private static void EnsureSourceIdentity(
            string sourcePath,
            PdfBookmarkDocument document)
        {
            var info = new FileInfo(sourcePath);
            if (info.Length != document.SourceLength ||
                info.LastWriteTimeUtc.Ticks !=
                    document.SourceLastWriteUtcTicks ||
                !string.Equals(
                    ComputeContentFingerprint(sourcePath),
                    document.SourceFingerprint,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    SourceChangedMessage);
            }
        }

        private static void EnsureSourceQuickIdentity(
            string sourcePath,
            PdfBookmarkDocument document)
        {
            var info = new FileInfo(sourcePath);
            if (info.Length != document.SourceLength ||
                info.LastWriteTimeUtc.Ticks !=
                    document.SourceLastWriteUtcTicks)
            {
                throw new InvalidOperationException(
                    SourceChangedMessage);
            }
        }

        private static string ComputeContentFingerprint(
            string path)
        {
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

        private static void CommitTemporaryFile(
            string temporaryPath,
            string outputPath)
        {
            if (File.Exists(outputPath))
            {
                File.Replace(
                    temporaryPath,
                    outputPath,
                    null);
            }
            else
            {
                File.Move(
                    temporaryPath,
                    outputPath);
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        private static void Report(
            Action<PdfBookmarkProgress> report,
            int completed,
            int total,
            string stage)
        {
            if (report != null)
            {
                report(
                    new PdfBookmarkProgress(
                        completed,
                        total,
                        stage));
            }
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

        private static bool DictionariesEqual(
            IDictionary<string, string> expected,
            IDictionary<string, string> actual)
        {
            if (expected.Count != actual.Count)
            {
                return false;
            }

            foreach (var item in expected)
            {
                string value;
                if (!actual.TryGetValue(item.Key, out value) ||
                    !string.Equals(
                        item.Value,
                        value,
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static int CountWidgets(PdfReader reader)
        {
            var count = 0;
            for (var page = 1;
                page <= reader.NumberOfPages;
                page++)
            {
                var dictionary = reader.GetPageN(page);
                var annotations = dictionary == null
                    ? null
                    : dictionary.GetAsArray(PdfName.ANNOTS);
                if (annotations == null)
                {
                    continue;
                }

                for (var index = 0;
                    index < annotations.Size;
                    index++)
                {
                    var annotation =
                        ResolveDictionary(annotations[index]);
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

        private static string ComputeTextHash(string value)
        {
            using (var sha = SHA256.Create())
            {
                return ToHex(
                    sha.ComputeHash(
                        Encoding.UTF8.GetBytes(value ?? string.Empty)));
            }
        }

        private static string ToHex(byte[] value)
        {
            return BitConverter.ToString(value)
                .Replace("-", string.Empty);
        }

        private static string GetReferenceKey(
            PdfIndirectReference reference)
        {
            return reference == null
                ? string.Empty
                : GetReferenceKey(
                    reference.Number,
                    reference.Generation);
        }

        private static string GetReferenceKey(
            int number,
            int generation)
        {
            return number.ToString(
                    CultureInfo.InvariantCulture) +
                ":" +
                generation.ToString(
                    CultureInfo.InvariantCulture);
        }

        private sealed class RawOutlineRecord
        {
            public RawOutlineRecord(
                PdfDictionary dictionary,
                PdfIndirectReference reference,
                string path)
            {
                Dictionary = dictionary;
                Reference = reference;
                Path = path;
                Children = new List<RawOutlineRecord>();
            }

            public PdfDictionary Dictionary { get; private set; }

            public PdfIndirectReference Reference { get; private set; }

            public string Path { get; private set; }

            public List<RawOutlineRecord> Children { get; private set; }
        }

        private sealed class SavedOutlineNode
        {
            public SavedOutlineNode(
                PdfBookmarkNode node,
                PdfDictionary dictionary,
                PdfIndirectReference reference,
                bool mustAddToBody)
            {
                Node = node;
                Dictionary = dictionary;
                Reference = reference;
                MustAddToBody = mustAddToBody;
                Children = new List<SavedOutlineNode>();
            }

            public PdfBookmarkNode Node { get; private set; }

            public PdfDictionary Dictionary { get; private set; }

            public PdfIndirectReference Reference { get; private set; }

            public bool MustAddToBody { get; private set; }

            public List<SavedOutlineNode> Children { get; private set; }
        }

        private sealed class OutlineRoot
        {
            public OutlineRoot(
                PdfDictionary dictionary,
                PdfIndirectReference reference,
                bool mustAddToBody)
            {
                Dictionary = dictionary;
                Reference = reference;
                MustAddToBody = mustAddToBody;
            }

            public PdfDictionary Dictionary { get; private set; }

            public PdfIndirectReference Reference { get; private set; }

            public bool MustAddToBody { get; private set; }
        }

        private sealed class NodeLocation
        {
            public NodeLocation(
                PdfBookmarkNode node,
                List<PdfBookmarkNode> container,
                int index)
            {
                Node = node;
                Container = container;
                Index = index;
            }

            public PdfBookmarkNode Node { get; private set; }

            public List<PdfBookmarkNode> Container { get; private set; }

            public int Index { get; private set; }
        }

        private sealed class DocumentExpectations
        {
            public DocumentExpectations(
                int pageCount,
                IList<string> pageContentReferences,
                IDictionary<string, string> metadata,
                string xmpHash,
                IDictionary<string, string> formValues,
                int widgetCount)
            {
                PageCount = pageCount;
                PageContentReferences = pageContentReferences;
                Metadata = metadata;
                XmpHash = xmpHash;
                FormValues = formValues;
                WidgetCount = widgetCount;
            }

            public int PageCount { get; private set; }

            public IList<string> PageContentReferences
            {
                get;
                private set;
            }

            public IDictionary<string, string> Metadata
            {
                get;
                private set;
            }

            public string XmpHash { get; private set; }

            public IDictionary<string, string> FormValues
            {
                get;
                private set;
            }

            public int WidgetCount { get; private set; }
        }

        private sealed class WriteExpectations
        {
            public WriteExpectations(
                DocumentExpectations document,
                IDictionary<string, string> preservedFingerprints,
                IDictionary<string, string> nodeReferences)
            {
                Document = document;
                PreservedFingerprints = preservedFingerprints;
                NodeReferences = nodeReferences;
            }

            public DocumentExpectations Document { get; private set; }

            public IDictionary<string, string> PreservedFingerprints
            {
                get;
                private set;
            }

            public IDictionary<string, string> NodeReferences
            {
                get;
                private set;
            }
        }
    }
}

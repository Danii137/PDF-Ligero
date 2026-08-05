using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using PdfiumViewer;
using PdfiumDocument = PdfiumViewer.PdfDocument;

namespace FirmaAutomatica
{
    internal enum PdfPlanComparisonMode
    {
        Baseline,
        Revised,
        Overlay,
        RedCyan,
        Split
    }

    internal enum PdfPlanAlignmentBasis
    {
        PhysicalPageBoxes,
        FitRevisionToBaseline
    }

    internal sealed class PdfPlanComparisonSettings
    {
        public PdfPlanComparisonSettings()
        {
            TargetDpi = 144;
            MaximumPixelsPerPage = 4000000;
            MaximumWorkingBytes = 128L * 1024L * 1024L;
            RenderAnnotations = true;
            AlignmentBasis = PdfPlanAlignmentBasis.PhysicalPageBoxes;
            OverlayOpacity = 0.50F;
            SplitPosition = 0.50F;
            EstimateContentOffset = false;
            MaximumAutoOffsetPixels = 32;
        }

        public int TargetDpi { get; set; }
        public int MaximumPixelsPerPage { get; set; }
        public long MaximumWorkingBytes { get; set; }
        public bool RenderAnnotations { get; set; }
        public PdfPlanAlignmentBasis AlignmentBasis { get; set; }
        public float OverlayOpacity { get; set; }
        public float SplitPosition { get; set; }
        public bool EstimateContentOffset { get; set; }
        public int MaximumAutoOffsetPixels { get; set; }

        internal PdfPlanComparisonSettings Snapshot()
        {
            var copy = new PdfPlanComparisonSettings();
            copy.TargetDpi = Math.Max(72, Math.Min(300, TargetDpi));
            copy.MaximumPixelsPerPage = Math.Max(
                1,
                Math.Min(12000000, MaximumPixelsPerPage));
            copy.MaximumWorkingBytes = Math.Max(
                1L,
                Math.Min(512L * 1024L * 1024L, MaximumWorkingBytes));
            copy.RenderAnnotations = RenderAnnotations;
            copy.AlignmentBasis = AlignmentBasis;
            copy.OverlayOpacity = Clamp01(OverlayOpacity);
            copy.SplitPosition = Clamp01(SplitPosition);
            copy.EstimateContentOffset = EstimateContentOffset;
            copy.MaximumAutoOffsetPixels = Math.Max(
                0,
                Math.Min(96, MaximumAutoOffsetPixels));
            return copy;
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return 0.5F;
            }

            return Math.Max(0F, Math.Min(1F, value));
        }
    }

    internal sealed class PdfPlanPageAdjustment
    {
        public PdfPlanPageAdjustment()
        {
            Scale = 1D;
        }

        /// <summary>
        /// Visible-canvas convention: positive X moves right and positive Y
        /// moves down. Values are PDF points (1/72 inch).
        /// </summary>
        public double OffsetXPoints { get; set; }
        public double OffsetYPoints { get; set; }

        public double Scale { get; set; }

        /// <summary>
        /// Positive angles rotate clockwise on the visible canvas.
        /// </summary>
        public double RotationDegrees { get; set; }

        public PdfPlanPageAdjustment Clone()
        {
            return new PdfPlanPageAdjustment
            {
                OffsetXPoints = OffsetXPoints,
                OffsetYPoints = OffsetYPoints,
                Scale = Scale,
                RotationDegrees = RotationDegrees
            };
        }

        internal PdfPlanPageAdjustment Snapshot()
        {
            var copy = Clone();
            if (double.IsNaN(copy.OffsetXPoints) ||
                double.IsInfinity(copy.OffsetXPoints))
            {
                copy.OffsetXPoints = 0D;
            }

            if (double.IsNaN(copy.OffsetYPoints) ||
                double.IsInfinity(copy.OffsetYPoints))
            {
                copy.OffsetYPoints = 0D;
            }

            if (double.IsNaN(copy.Scale) ||
                double.IsInfinity(copy.Scale))
            {
                copy.Scale = 1D;
            }

            if (double.IsNaN(copy.RotationDegrees) ||
                double.IsInfinity(copy.RotationDegrees))
            {
                copy.RotationDegrees = 0D;
            }

            copy.OffsetXPoints = Math.Max(
                -144D,
                Math.Min(144D, copy.OffsetXPoints));
            copy.OffsetYPoints = Math.Max(
                -144D,
                Math.Min(144D, copy.OffsetYPoints));
            copy.Scale = Math.Max(0.75D, Math.Min(1.25D, copy.Scale));
            copy.RotationDegrees = Math.Max(
                -5D,
                Math.Min(5D, copy.RotationDegrees));
            return copy;
        }
    }

    internal sealed class PdfPlanComparisonPageInfo
    {
        internal PdfPlanComparisonPageInfo(int pageIndex, SizeF sizePoints)
        {
            PageIndex = pageIndex;
            SizePoints = sizePoints;
        }

        public int PageIndex { get; private set; }
        public int PageNumber { get { return PageIndex + 1; } }
        public SizeF SizePoints { get; private set; }
    }

    internal sealed class PdfPlanAlignmentSuggestion
    {
        internal PdfPlanAlignmentSuggestion(
            PdfPlanPageAdjustment adjustment,
            double score,
            bool isReliable)
        {
            Adjustment = adjustment;
            Score = score;
            IsReliable = isReliable;
        }

        public PdfPlanPageAdjustment Adjustment { get; private set; }
        public double Score { get; private set; }
        public bool IsReliable { get; private set; }
    }

    /// <summary>
    /// Owns exactly two normalized page bitmaps. Dispose the result instead of
    /// either bitmap. Composites are caller-owned and can be recreated without
    /// reopening or rerendering either PDF.
    /// </summary>
    internal sealed class PdfPlanComparisonResult : IDisposable
    {
        private Bitmap baselineBitmap;
        private Bitmap revisedBitmap;
        private bool disposed;
        private readonly float defaultOverlayOpacity;
        private readonly float defaultSplitPosition;

        internal PdfPlanComparisonResult(
            Bitmap baseline,
            Bitmap revised,
            PdfPlanComparisonPageInfo baselinePage,
            PdfPlanComparisonPageInfo revisedPage,
            float actualDpi,
            PdfPlanPageAdjustment appliedAdjustment,
            PdfPlanAlignmentSuggestion alignmentSuggestion,
            long estimatedPeakMemoryBytes,
            float overlayOpacity,
            float splitPosition)
        {
            baselineBitmap = baseline;
            revisedBitmap = revised;
            BaselinePage = baselinePage;
            RevisedPage = revisedPage;
            ActualDpi = actualDpi;
            AppliedAdjustment = appliedAdjustment;
            AlignmentSuggestion = alignmentSuggestion;
            EstimatedPeakMemoryBytes = estimatedPeakMemoryBytes;
            defaultOverlayOpacity = overlayOpacity;
            defaultSplitPosition = splitPosition;
        }

        public Bitmap BaselineBitmap
        {
            get
            {
                ThrowIfDisposed();
                return baselineBitmap;
            }
        }

        public Bitmap RevisedBitmap
        {
            get
            {
                ThrowIfDisposed();
                return revisedBitmap;
            }
        }

        public Size PixelSize
        {
            get
            {
                ThrowIfDisposed();
                return baselineBitmap.Size;
            }
        }

        public PdfPlanComparisonPageInfo BaselinePage { get; private set; }
        public PdfPlanComparisonPageInfo RevisedPage { get; private set; }
        public float ActualDpi { get; private set; }
        public PdfPlanPageAdjustment AppliedAdjustment { get; private set; }
        public PdfPlanAlignmentSuggestion AlignmentSuggestion
        {
            get;
            private set;
        }
        public long EstimatedPeakMemoryBytes { get; private set; }

        public Bitmap CreateComposite(PdfPlanComparisonMode mode)
        {
            return CreateComposite(
                mode,
                defaultOverlayOpacity,
                defaultSplitPosition,
                CancellationToken.None);
        }

        public Bitmap CreateComposite(
            PdfPlanComparisonMode mode,
            float overlayOpacity,
            float splitPosition,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            return PdfPlanComparisonService.CreateComposite(
                baselineBitmap,
                revisedBitmap,
                mode,
                overlayOpacity,
                splitPosition,
                cancellationToken);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (baselineBitmap != null)
            {
                baselineBitmap.Dispose();
                baselineBitmap = null;
            }

            if (revisedBitmap != null)
            {
                revisedBitmap.Dispose();
                revisedBitmap = null;
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(
                    "PdfPlanComparisonResult");
            }
        }
    }

    /// <summary>
    /// Explicit, lazy comparison session. Opening the application does not
    /// create this object; PDFium is touched only when OpenSession is called.
    /// A Compare call renders one requested page from each source, never the
    /// rest of either document. Compare, page queries and Dispose are
    /// serialized because PDFium document handles are not thread-safe.
    /// </summary>
    internal sealed class PdfPlanComparisonSession : IDisposable
    {
        private PdfiumDocument baselineDocument;
        private PdfiumDocument revisedDocument;
        private readonly PdfPlanComparisonSettings settings;
        private readonly object synchronizationRoot = new object();
        private bool disposed;

        internal PdfPlanComparisonSession(
            PdfiumDocument baseline,
            PdfiumDocument revised,
            PdfPlanComparisonSettings effectiveSettings)
        {
            baselineDocument = baseline;
            revisedDocument = revised;
            settings = effectiveSettings;
        }

        public int BaselinePageCount
        {
            get
            {
                lock (synchronizationRoot)
                {
                    ThrowIfDisposed();
                    return baselineDocument.PageCount;
                }
            }
        }

        public int RevisedPageCount
        {
            get
            {
                lock (synchronizationRoot)
                {
                    ThrowIfDisposed();
                    return revisedDocument.PageCount;
                }
            }
        }

        public SizeF GetBaselinePageSize(int pageIndex)
        {
            lock (synchronizationRoot)
            {
                ThrowIfDisposed();
                return GetPageSize(baselineDocument, pageIndex);
            }
        }

        public SizeF GetRevisedPageSize(int pageIndex)
        {
            lock (synchronizationRoot)
            {
                ThrowIfDisposed();
                return GetPageSize(revisedDocument, pageIndex);
            }
        }

        public PdfPlanComparisonResult Compare(
            int baselinePageIndex,
            int revisedPageIndex,
            PdfPlanPageAdjustment revisionAdjustment,
            CancellationToken cancellationToken)
        {
            lock (synchronizationRoot)
            {
                ThrowIfDisposed();
                return PdfPlanComparisonService.CompareLoaded(
                    baselineDocument,
                    revisedDocument,
                    baselinePageIndex,
                    revisedPageIndex,
                    settings,
                    revisionAdjustment,
                    cancellationToken);
            }
        }

        public void Dispose()
        {
            lock (synchronizationRoot)
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                if (baselineDocument != null)
                {
                    baselineDocument.Dispose();
                    baselineDocument = null;
                }

                if (revisedDocument != null)
                {
                    revisedDocument.Dispose();
                    revisedDocument = null;
                }
            }
        }

        private static SizeF GetPageSize(
            PdfiumDocument document,
            int pageIndex)
        {
            if (pageIndex < 0 || pageIndex >= document.PageCount)
            {
                throw new ArgumentOutOfRangeException("pageIndex");
            }

            return document.PageSizes[pageIndex];
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(
                    "PdfPlanComparisonSession");
            }
        }
    }

    internal static class PdfPlanComparisonService
    {
        public static PdfPlanComparisonSession OpenSession(
            string baselinePdfPath,
            string revisedPdfPath,
            PdfPlanComparisonSettings settings,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var baselinePath = NormalizeExistingPdfPath(
                baselinePdfPath,
                "baselinePdfPath");
            var revisedPath = NormalizeExistingPdfPath(
                revisedPdfPath,
                "revisedPdfPath");
            var effectiveSettings = (settings ??
                new PdfPlanComparisonSettings()).Snapshot();

            PdfiumDocument baseline = null;
            PdfiumDocument revised = null;
            try
            {
                baseline = PdfiumDocument.Load(baselinePath);
                cancellationToken.ThrowIfCancellationRequested();
                revised = PdfiumDocument.Load(revisedPath);
                cancellationToken.ThrowIfCancellationRequested();
                if (baseline.PageCount < 1)
                {
                    throw new InvalidDataException(
                        "El PDF base no contiene paginas.");
                }

                if (revised.PageCount < 1)
                {
                    throw new InvalidDataException(
                        "El PDF revisado no contiene paginas.");
                }

                var session = new PdfPlanComparisonSession(
                    baseline,
                    revised,
                    effectiveSettings);
                baseline = null;
                revised = null;
                return session;
            }
            finally
            {
                if (baseline != null)
                {
                    baseline.Dispose();
                }

                if (revised != null)
                {
                    revised.Dispose();
                }
            }
        }

        public static PdfPlanComparisonResult Compare(
            string baselinePdfPath,
            int baselinePageIndex,
            string revisedPdfPath,
            int revisedPageIndex,
            PdfPlanComparisonSettings settings,
            PdfPlanPageAdjustment revisionAdjustment,
            CancellationToken cancellationToken)
        {
            using (var session = OpenSession(
                baselinePdfPath,
                revisedPdfPath,
                settings,
                cancellationToken))
            {
                return session.Compare(
                    baselinePageIndex,
                    revisedPageIndex,
                    revisionAdjustment,
                    cancellationToken);
            }
        }

        internal static PdfPlanComparisonResult CompareLoaded(
            PdfiumDocument baselineDocument,
            PdfiumDocument revisedDocument,
            int baselinePageIndex,
            int revisedPageIndex,
            PdfPlanComparisonSettings settings,
            PdfPlanPageAdjustment revisionAdjustment,
            CancellationToken cancellationToken)
        {
            if (baselineDocument == null)
            {
                throw new ArgumentNullException("baselineDocument");
            }

            if (revisedDocument == null)
            {
                throw new ArgumentNullException("revisedDocument");
            }

            ValidatePageIndex(
                baselineDocument,
                baselinePageIndex,
                "baselinePageIndex");
            ValidatePageIndex(
                revisedDocument,
                revisedPageIndex,
                "revisedPageIndex");
            cancellationToken.ThrowIfCancellationRequested();

            var effectiveSettings = (settings ??
                new PdfPlanComparisonSettings()).Snapshot();
            var adjustment = (revisionAdjustment ??
                new PdfPlanPageAdjustment()).Snapshot();
            var baselineSize = ValidatePageSize(
                baselineDocument.PageSizes[baselinePageIndex],
                "base");
            var revisedSize = ValidatePageSize(
                revisedDocument.PageSizes[revisedPageIndex],
                "revisada");
            var layout = BuildLayout(
                baselineSize,
                revisedSize,
                effectiveSettings,
                adjustment);
            var renderPlan = BuildRenderPlan(
                layout,
                effectiveSettings);

            Bitmap baselineBitmap = null;
            Bitmap revisedBitmap = null;
            try
            {
                baselineBitmap = RenderNormalizedPage(
                    baselineDocument,
                    baselinePageIndex,
                    baselineSize,
                    renderPlan,
                    layout.BaselineScale,
                    0D,
                    0D,
                    0D,
                    effectiveSettings.RenderAnnotations,
                    cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                revisedBitmap = RenderNormalizedPage(
                    revisedDocument,
                    revisedPageIndex,
                    revisedSize,
                    renderPlan,
                    layout.RevisedScale,
                    adjustment.OffsetXPoints,
                    adjustment.OffsetYPoints,
                    adjustment.RotationDegrees,
                    effectiveSettings.RenderAnnotations,
                    cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                PdfPlanAlignmentSuggestion suggestion = null;
                if (effectiveSettings.EstimateContentOffset &&
                    effectiveSettings.MaximumAutoOffsetPixels > 0)
                {
                    suggestion = EstimateContentOffset(
                        baselineBitmap,
                        revisedBitmap,
                        adjustment,
                        renderPlan.ActualDpi,
                        effectiveSettings.MaximumAutoOffsetPixels,
                        cancellationToken);
                }

                var result = new PdfPlanComparisonResult(
                    baselineBitmap,
                    revisedBitmap,
                    new PdfPlanComparisonPageInfo(
                        baselinePageIndex,
                        baselineSize),
                    new PdfPlanComparisonPageInfo(
                        revisedPageIndex,
                        revisedSize),
                    renderPlan.ActualDpi,
                    adjustment.Clone(),
                    suggestion,
                    renderPlan.EstimatedPeakMemoryBytes,
                    effectiveSettings.OverlayOpacity,
                    effectiveSettings.SplitPosition);
                baselineBitmap = null;
                revisedBitmap = null;
                return result;
            }
            finally
            {
                if (baselineBitmap != null)
                {
                    baselineBitmap.Dispose();
                }

                if (revisedBitmap != null)
                {
                    revisedBitmap.Dispose();
                }
            }
        }

        internal static Bitmap CreateComposite(
            Bitmap baseline,
            Bitmap revised,
            PdfPlanComparisonMode mode,
            float overlayOpacity,
            float splitPosition,
            CancellationToken cancellationToken)
        {
            if (baseline == null)
            {
                throw new ArgumentNullException("baseline");
            }

            if (revised == null)
            {
                throw new ArgumentNullException("revised");
            }

            if (baseline.Width != revised.Width ||
                baseline.Height != revised.Height)
            {
                throw new ArgumentException(
                    "Las paginas normalizadas deben tener el mismo tamano.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            var opacity = Math.Max(0F, Math.Min(1F, overlayOpacity));
            var split = Math.Max(0F, Math.Min(1F, splitPosition));
            if (mode == PdfPlanComparisonMode.RedCyan)
            {
                return CreateRedCyanComposite(
                    baseline,
                    revised,
                    cancellationToken);
            }

            var output = CreateWhiteBitmap(
                baseline.Width,
                baseline.Height,
                baseline.HorizontalResolution);
            try
            {
                using (var graphics = Graphics.FromImage(output))
                {
                    ConfigureGraphics(graphics);
                    if (mode == PdfPlanComparisonMode.Revised)
                    {
                        graphics.DrawImageUnscaled(revised, 0, 0);
                    }
                    else if (mode == PdfPlanComparisonMode.Overlay)
                    {
                        graphics.DrawImageUnscaled(baseline, 0, 0);
                        cancellationToken.ThrowIfCancellationRequested();
                        graphics.CompositingMode =
                            CompositingMode.SourceOver;
                        using (var attributes = new ImageAttributes())
                        {
                            var matrix = new ColorMatrix();
                            matrix.Matrix33 = opacity;
                            attributes.SetColorMatrix(
                                matrix,
                                ColorMatrixFlag.Default,
                                ColorAdjustType.Bitmap);
                            graphics.DrawImage(
                                revised,
                                new Rectangle(
                                    0,
                                    0,
                                    revised.Width,
                                    revised.Height),
                                0,
                                0,
                                revised.Width,
                                revised.Height,
                                GraphicsUnit.Pixel,
                                attributes);
                        }
                    }
                    else if (mode == PdfPlanComparisonMode.Split)
                    {
                        graphics.DrawImageUnscaled(baseline, 0, 0);
                        cancellationToken.ThrowIfCancellationRequested();
                        var splitX = Math.Max(
                            0,
                            Math.Min(
                                revised.Width,
                                (int)Math.Round(
                                    revised.Width * split)));
                        if (splitX < revised.Width)
                        {
                            var area = new Rectangle(
                                splitX,
                                0,
                                revised.Width - splitX,
                                revised.Height);
                            graphics.DrawImage(
                                revised,
                                area,
                                area,
                                GraphicsUnit.Pixel);
                        }
                    }
                    else
                    {
                        graphics.DrawImageUnscaled(baseline, 0, 0);
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                return output;
            }
            catch
            {
                output.Dispose();
                throw;
            }
        }

        private const long EstimatedBytesPerOutputPixel = 16L;

        private sealed class ComparisonLayout
        {
            public double CanvasWidthPoints;
            public double CanvasHeightPoints;
            public double BaselineScale;
            public double RevisedScale;
        }

        private sealed class ComparisonRenderPlan
        {
            public int Width;
            public int Height;
            public float ActualDpi;
            public long EstimatedPeakMemoryBytes;
        }

        private sealed class InkPoint
        {
            public int X;
            public int Y;
            public int Darkness;
        }

        private static string NormalizeExistingPdfPath(
            string path,
            string parameterName)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException(
                    "Debe indicar un archivo PDF.",
                    parameterName);
            }

            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException(
                    "No se encuentra el archivo PDF.",
                    fullPath);
            }

            if (!string.Equals(
                Path.GetExtension(fullPath),
                ".pdf",
                StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "El archivo debe tener extension PDF.",
                    parameterName);
            }

            return fullPath;
        }

        private static void ValidatePageIndex(
            PdfiumDocument document,
            int pageIndex,
            string parameterName)
        {
            if (pageIndex < 0 || pageIndex >= document.PageCount)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        private static SizeF ValidatePageSize(
            SizeF size,
            string description)
        {
            if (size.Width <= 0F ||
                size.Height <= 0F ||
                float.IsNaN(size.Width) ||
                float.IsNaN(size.Height) ||
                float.IsInfinity(size.Width) ||
                float.IsInfinity(size.Height))
            {
                throw new InvalidDataException(
                    "La pagina " + description +
                    " tiene una caja no valida.");
            }

            return size;
        }

        private static ComparisonLayout BuildLayout(
            SizeF baselineSize,
            SizeF revisedSize,
            PdfPlanComparisonSettings settings,
            PdfPlanPageAdjustment adjustment)
        {
            var layout = new ComparisonLayout();
            layout.BaselineScale = 1D;
            layout.RevisedScale = adjustment.Scale;
            layout.CanvasWidthPoints = baselineSize.Width;
            layout.CanvasHeightPoints = baselineSize.Height;

            if (settings.AlignmentBasis ==
                PdfPlanAlignmentBasis.FitRevisionToBaseline)
            {
                var fitScale = Math.Min(
                    baselineSize.Width / revisedSize.Width,
                    baselineSize.Height / revisedSize.Height);
                layout.RevisedScale *= fitScale;
                return layout;
            }

            var radians = adjustment.RotationDegrees *
                Math.PI / 180D;
            var cosine = Math.Abs(Math.Cos(radians));
            var sine = Math.Abs(Math.Sin(radians));
            var revisedWidth = revisedSize.Width *
                layout.RevisedScale;
            var revisedHeight = revisedSize.Height *
                layout.RevisedScale;
            var rotatedWidth = revisedWidth * cosine +
                revisedHeight * sine;
            var rotatedHeight = revisedWidth * sine +
                revisedHeight * cosine;
            layout.CanvasWidthPoints = Math.Max(
                baselineSize.Width,
                rotatedWidth + 2D * Math.Abs(
                    adjustment.OffsetXPoints));
            layout.CanvasHeightPoints = Math.Max(
                baselineSize.Height,
                rotatedHeight + 2D * Math.Abs(
                    adjustment.OffsetYPoints));
            return layout;
        }

        private static ComparisonRenderPlan BuildRenderPlan(
            ComparisonLayout layout,
            PdfPlanComparisonSettings settings)
        {
            const int maximumBitmapDimension = 16000;
            var requestedDpi = settings.TargetDpi;
            var memoryPixelLimit = Math.Max(
                1L,
                settings.MaximumWorkingBytes /
                EstimatedBytesPerOutputPixel);
            var pixelLimit = Math.Max(
                1L,
                Math.Min(
                    settings.MaximumPixelsPerPage,
                    memoryPixelLimit));
            var actualDpi = (double)requestedDpi;
            var requestedWidth =
                layout.CanvasWidthPoints * actualDpi / 72D;
            var requestedHeight =
                layout.CanvasHeightPoints * actualDpi / 72D;
            var dimensionScale = Math.Min(
                1D,
                Math.Min(
                    maximumBitmapDimension / requestedWidth,
                    maximumBitmapDimension / requestedHeight));
            actualDpi *= dimensionScale;
            var requestedPixels =
                layout.CanvasWidthPoints *
                layout.CanvasHeightPoints *
                actualDpi * actualDpi /
                (72D * 72D);
            if (requestedPixels > pixelLimit)
            {
                actualDpi *= Math.Sqrt(
                    pixelLimit / (double)requestedPixels);
            }

            actualDpi = Math.Max(24D, actualDpi);
            var calculatedWidth =
                layout.CanvasWidthPoints * actualDpi / 72D;
            var calculatedHeight =
                layout.CanvasHeightPoints * actualDpi / 72D;
            if (calculatedWidth > maximumBitmapDimension ||
                calculatedHeight > maximumBitmapDimension)
            {
                throw new InvalidOperationException(
                    "La caja de pagina es demasiado larga para una " +
                    "vista segura.");
            }

            var width = Math.Max(
                1,
                (int)Math.Floor(
                    calculatedWidth));
            var height = Math.Max(
                1,
                (int)Math.Floor(
                    calculatedHeight));
            while ((long)width * height > pixelLimit &&
                actualDpi > 24D)
            {
                actualDpi = Math.Max(24D, actualDpi - 0.25D);
                width = Math.Max(
                    1,
                    (int)Math.Floor(
                        layout.CanvasWidthPoints *
                        actualDpi / 72D));
                height = Math.Max(
                    1,
                    (int)Math.Floor(
                        layout.CanvasHeightPoints *
                        actualDpi / 72D));
            }

            var finalPixels = (long)width * height;
            if (finalPixels > pixelLimit)
            {
                throw new InvalidOperationException(
                    "La pagina requiere mas memoria que el limite " +
                    "configurado, incluso con la vista minima.");
            }

            return new ComparisonRenderPlan
            {
                Width = width,
                Height = height,
                ActualDpi = (float)actualDpi,
                EstimatedPeakMemoryBytes = finalPixels *
                    EstimatedBytesPerOutputPixel
            };
        }

        private static Bitmap RenderNormalizedPage(
            PdfiumDocument document,
            int pageIndex,
            SizeF pageSize,
            ComparisonRenderPlan plan,
            double scale,
            double offsetXPoints,
            double offsetYPoints,
            double rotationDegrees,
            bool renderAnnotations,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var renderWidth = Math.Max(
                1,
                (int)Math.Round(
                    pageSize.Width * scale *
                    plan.ActualDpi / 72D));
            var renderHeight = Math.Max(
                1,
                (int)Math.Round(
                    pageSize.Height * scale *
                    plan.ActualDpi / 72D));
            var flags = PdfRenderFlags.LcdText |
                PdfRenderFlags.LimitImageCacheSize;
            if (renderAnnotations)
            {
                flags |= PdfRenderFlags.Annotations;
            }

            using (var rendered = document.Render(
                pageIndex,
                renderWidth,
                renderHeight,
                plan.ActualDpi,
                plan.ActualDpi,
                flags))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var output = CreateWhiteBitmap(
                    plan.Width,
                    plan.Height,
                    plan.ActualDpi);
                try
                {
                    using (var graphics = Graphics.FromImage(output))
                    {
                        ConfigureGraphics(graphics);
                        graphics.TranslateTransform(
                            plan.Width / 2F +
                            (float)(offsetXPoints *
                            plan.ActualDpi / 72D),
                            plan.Height / 2F +
                            (float)(offsetYPoints *
                            plan.ActualDpi / 72D));
                        if (Math.Abs(rotationDegrees) > 0.0001D)
                        {
                            graphics.RotateTransform(
                                (float)rotationDegrees);
                        }

                        graphics.DrawImage(
                            rendered,
                            new RectangleF(
                                -renderWidth / 2F,
                                -renderHeight / 2F,
                                renderWidth,
                                renderHeight));
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    return output;
                }
                catch
                {
                    output.Dispose();
                    throw;
                }
            }
        }

        private static Bitmap CreateWhiteBitmap(
            int width,
            int height,
            float dpi)
        {
            var bitmap = new Bitmap(
                width,
                height,
                PixelFormat.Format24bppRgb);
            var safeDpi = Math.Max(24F, Math.Min(600F, dpi));
            bitmap.SetResolution(safeDpi, safeDpi);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.White);
            }

            return bitmap;
        }

        private static void ConfigureGraphics(Graphics graphics)
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.CompositingQuality =
                CompositingQuality.HighQuality;
            graphics.InterpolationMode =
                InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
        }

        private static PdfPlanAlignmentSuggestion EstimateContentOffset(
            Bitmap baseline,
            Bitmap revised,
            PdfPlanPageAdjustment appliedAdjustment,
            float actualDpi,
            int maximumOffsetPixels,
            CancellationToken cancellationToken)
        {
            const int maximumSampleDimension = 384;
            var downsampleScale = Math.Min(
                1D,
                maximumSampleDimension /
                (double)Math.Max(
                    baseline.Width,
                    baseline.Height));
            var sampleWidth = Math.Max(
                1,
                (int)Math.Round(
                    baseline.Width * downsampleScale));
            var sampleHeight = Math.Max(
                1,
                (int)Math.Round(
                    baseline.Height * downsampleScale));
            var baselineDarkness = CreateDarknessSample(
                baseline,
                sampleWidth,
                sampleHeight,
                cancellationToken);
            var revisedDarkness = CreateDarknessSample(
                revised,
                sampleWidth,
                sampleHeight,
                cancellationToken);
            var inkPoints = new System.Collections.Generic.List<InkPoint>();
            long baselineInk = 0L;
            long revisedInk = 0L;
            var index = 0;
            for (var y = 0; y < sampleHeight; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (var x = 0; x < sampleWidth; x++, index++)
                {
                    var baselineValue = baselineDarkness[index];
                    var revisedValue = revisedDarkness[index];
                    baselineInk += baselineValue;
                    revisedInk += revisedValue;
                    if (baselineValue >= 20)
                    {
                        inkPoints.Add(new InkPoint
                        {
                            X = x,
                            Y = y,
                            Darkness = baselineValue
                        });
                    }
                }
            }

            var maximumSampleOffset = Math.Max(
                1,
                Math.Min(
                    24,
                    (int)Math.Ceiling(
                        maximumOffsetPixels *
                        downsampleScale)));
            var bestScore = -1D;
            var zeroScore = 0D;
            var bestX = 0;
            var bestY = 0;
            var denominator = Math.Max(
                1D,
                baselineInk + revisedInk);
            for (var shiftY = -maximumSampleOffset;
                shiftY <= maximumSampleOffset;
                shiftY++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (var shiftX = -maximumSampleOffset;
                    shiftX <= maximumSampleOffset;
                    shiftX++)
                {
                    long overlap = 0L;
                    foreach (var point in inkPoints)
                    {
                        var revisedX = point.X - shiftX;
                        var revisedY = point.Y - shiftY;
                        if (revisedX < 0 ||
                            revisedY < 0 ||
                            revisedX >= sampleWidth ||
                            revisedY >= sampleHeight)
                        {
                            continue;
                        }

                        var revisedValue = revisedDarkness[
                            revisedY * sampleWidth + revisedX];
                        overlap += Math.Min(
                            point.Darkness,
                            revisedValue);
                    }

                    var score = 2D * overlap / denominator;
                    if (shiftX == 0 && shiftY == 0)
                    {
                        zeroScore = score;
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestX = shiftX;
                        bestY = shiftY;
                    }
                }
            }

            var safeScale = Math.Max(0.0001D, downsampleScale);
            var offsetXPoints = bestX / safeScale *
                72D / actualDpi;
            var offsetYPoints = bestY / safeScale *
                72D / actualDpi;
            var suggested = appliedAdjustment.Clone();
            suggested.OffsetXPoints += offsetXPoints;
            suggested.OffsetYPoints += offsetYPoints;
            suggested = suggested.Snapshot();
            var improvement = bestScore - zeroScore;
            var reliable = inkPoints.Count >= 20 &&
                bestScore >= 0.30D &&
                (bestX != 0 || bestY != 0
                    ? improvement >= 0.012D
                    : bestScore >= 0.55D);
            return new PdfPlanAlignmentSuggestion(
                suggested,
                Math.Max(0D, Math.Min(1D, bestScore)),
                reliable);
        }

        private static byte[] CreateDarknessSample(
            Bitmap source,
            int width,
            int height,
            CancellationToken cancellationToken)
        {
            using (var sample = new Bitmap(
                width,
                height,
                PixelFormat.Format24bppRgb))
            {
                using (var graphics = Graphics.FromImage(sample))
                {
                    graphics.Clear(Color.White);
                    graphics.InterpolationMode =
                        InterpolationMode.HighQualityBilinear;
                    graphics.PixelOffsetMode =
                        PixelOffsetMode.HighQuality;
                    graphics.DrawImage(
                        source,
                        new Rectangle(0, 0, width, height));
                }

                var values = new byte[width * height];
                var area = new Rectangle(0, 0, width, height);
                var data = sample.LockBits(
                    area,
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format24bppRgb);
                try
                {
                    var rowLength = width * 3;
                    var raw = new byte[rowLength];
                    for (var y = 0; y < height; y++)
                    {
                        if ((y & 15) == 0)
                        {
                            cancellationToken
                                .ThrowIfCancellationRequested();
                        }

                        Marshal.Copy(
                            GetRowPointer(data, y),
                            raw,
                            0,
                            rowLength);
                        var outputIndex = y * width;
                        for (var x = 0; x < width; x++)
                        {
                            var pixel = x * 3;
                            var blue = raw[pixel];
                            var green = raw[pixel + 1];
                            var red = raw[pixel + 2];
                            var luminance = (
                                29 * blue +
                                150 * green +
                                77 * red + 128) >> 8;
                            values[outputIndex + x] =
                                (byte)(255 - luminance);
                        }
                    }
                }
                finally
                {
                    sample.UnlockBits(data);
                }

                return values;
            }
        }

        private static Bitmap CreateRedCyanComposite(
            Bitmap baseline,
            Bitmap revised,
            CancellationToken cancellationToken)
        {
            var width = baseline.Width;
            var height = baseline.Height;
            var output = CreateWhiteBitmap(
                width,
                height,
                baseline.HorizontalResolution);
            var area = new Rectangle(0, 0, width, height);
            Bitmap baseline24 = null;
            Bitmap revised24 = null;
            var ownsBaseline24 = false;
            var ownsRevised24 = false;
            BitmapData baselineData = null;
            BitmapData revisedData = null;
            BitmapData outputData = null;
            try
            {
                if (baseline.PixelFormat ==
                    PixelFormat.Format24bppRgb)
                {
                    baseline24 = baseline;
                }
                else
                {
                    baseline24 = CloneAs24Bit(baseline);
                    ownsBaseline24 = true;
                }

                if (revised.PixelFormat ==
                    PixelFormat.Format24bppRgb)
                {
                    revised24 = revised;
                }
                else
                {
                    revised24 = CloneAs24Bit(revised);
                    ownsRevised24 = true;
                }

                baselineData = baseline24.LockBits(
                    area,
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format24bppRgb);
                revisedData = revised24.LockBits(
                    area,
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format24bppRgb);
                outputData = output.LockBits(
                    area,
                    ImageLockMode.WriteOnly,
                    PixelFormat.Format24bppRgb);
                var rowLength = width * 3;
                var baselineRaw = new byte[rowLength];
                var revisedRaw = new byte[rowLength];
                var outputRaw = new byte[rowLength];
                for (var y = 0; y < height; y++)
                {
                    if ((y & 31) == 0)
                    {
                        cancellationToken
                            .ThrowIfCancellationRequested();
                    }

                    Marshal.Copy(
                        GetRowPointer(baselineData, y),
                        baselineRaw,
                        0,
                        rowLength);
                    Marshal.Copy(
                        GetRowPointer(revisedData, y),
                        revisedRaw,
                        0,
                        rowLength);
                    for (var x = 0; x < width; x++)
                    {
                        var pixel = x * 3;
                        var baselineLuminance = (
                            29 * baselineRaw[pixel] +
                            150 * baselineRaw[
                                pixel + 1] +
                            77 * baselineRaw[
                                pixel + 2] +
                            128) >> 8;
                        var revisedLuminance = (
                            29 * revisedRaw[pixel] +
                            150 * revisedRaw[
                                pixel + 1] +
                            77 * revisedRaw[
                                pixel + 2] +
                            128) >> 8;
                        // Base-only ink becomes red, revised-only ink
                        // becomes cyan, coincident ink remains neutral.
                        outputRaw[pixel] =
                            (byte)baselineLuminance;
                        outputRaw[pixel + 1] =
                            (byte)baselineLuminance;
                        outputRaw[pixel + 2] =
                            (byte)revisedLuminance;
                    }

                    Marshal.Copy(
                        outputRaw,
                        0,
                        GetRowPointer(outputData, y),
                        rowLength);
                }

                output.UnlockBits(outputData);
                outputData = null;
                cancellationToken.ThrowIfCancellationRequested();
                return output;
            }
            catch
            {
                output.Dispose();
                throw;
            }
            finally
            {
                if (outputData != null)
                {
                    output.UnlockBits(outputData);
                }

                if (baselineData != null)
                {
                    baseline24.UnlockBits(baselineData);
                }

                if (revisedData != null)
                {
                    revised24.UnlockBits(revisedData);
                }

                if (ownsBaseline24 && baseline24 != null)
                {
                    baseline24.Dispose();
                }

                if (ownsRevised24 && revised24 != null)
                {
                    revised24.Dispose();
                }
            }
        }

        private static Bitmap CloneAs24Bit(Bitmap source)
        {
            var copy = new Bitmap(
                source.Width,
                source.Height,
                PixelFormat.Format24bppRgb);
            copy.SetResolution(
                Math.Max(24F, source.HorizontalResolution),
                Math.Max(24F, source.VerticalResolution));
            using (var graphics = Graphics.FromImage(copy))
            {
                graphics.DrawImageUnscaled(source, 0, 0);
            }

            return copy;
        }

        private static IntPtr GetRowPointer(
            BitmapData data,
            int row)
        {
            return IntPtr.Add(
                data.Scan0,
                row * data.Stride);
        }
    }
}

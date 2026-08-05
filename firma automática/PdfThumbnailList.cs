using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using PdfiumViewer;
using PdfiumDocument = PdfiumViewer.PdfDocument;

namespace FirmaAutomatica
{
    /// <summary>
    /// Lightweight, virtualized page thumbnail list.
    ///
    /// Pdfium documents are not assumed to be thread-safe: thumbnails are rendered
    /// lazily on the UI thread, one page per timer tick. Only visible/nearby pages
    /// are queued and the bitmap cache has a fixed upper bound.
    /// </summary>
    internal sealed class PdfThumbnailList : ScrollableControl
    {
        private static readonly Color PanelBackColor = Color.FromArgb(241, 240, 236);
        private static readonly Color CardBackColor = Color.FromArgb(248, 247, 243);
        private static readonly Color SelectedBackColor = Color.FromArgb(252, 250, 246);
        private static readonly Color SelectedBorderColor = Color.FromArgb(238, 91, 61);
        private static readonly Color TechnicalLineColor = Color.FromArgb(207, 204, 197);
        private static readonly Color PageBorderColor = Color.FromArgb(191, 188, 180);
        private static readonly Color PageShadowColor = Color.FromArgb(218, 216, 210);
        private static readonly Color PlaceholderColor = Color.FromArgb(235, 233, 227);
        private static readonly Color PlaceholderLineColor = Color.FromArgb(211, 208, 200);
        private static readonly Color TextColor = Color.FromArgb(42, 44, 46);
        private static readonly Color MutedTextColor = Color.FromArgb(112, 112, 107);
        private static readonly Color InsertIndicatorColor = Color.FromArgb(238, 91, 61);

        private const int OuterHorizontalPadding = 9;
        private const int OuterVerticalPadding = 8;
        private const int CardVerticalPadding = 9;
        private const int ItemGap = 8;
        private const int PageNumberHeight = 27;
        private const int MaximumThumbnailWidth = 168;
        private const int MaximumThumbnailHeight = 154;
        private const int MinimumItemHeight = 190;
        private const int DefaultCacheCapacity = 18;
        private const int NearbyPageCount = 2;
        private const int DragAutoScrollMargin = 30;
        private const int DragAutoScrollStep = 28;
        private const int DragAutoScrollInterval = 55;
        private const string InternalPageDragFormat =
            "PDFLigero.InternalPageSelection";

        private readonly Timer renderTimer;
        private readonly Font ownedUiFont;
        private readonly Font pageNumberFont;
        private readonly Font technicalCaptionFont;
        private readonly HashSet<int> selectedPages = new HashSet<int>();
        private readonly ContextMenuStrip pageContextMenu;
        private readonly ToolStripMenuItem selectionSummaryMenuItem;
        private readonly ToolStripMenuItem rotateLeftMenuItem;
        private readonly ToolStripMenuItem rotateRightMenuItem;
        private readonly ToolStripMenuItem deletePagesMenuItem;
        private readonly Dictionary<int, ThumbnailCacheEntry> thumbnailCache =
            new Dictionary<int, ThumbnailCacheEntry>();
        private readonly HashSet<int> failedPages = new HashSet<int>();
        private readonly List<int> renderQueue = new List<int>();
        private PdfiumDocument document;
        private bool ownsDocument;
        private int selectedPage = -1;
        private int activePage = -1;
        private int documentGeneration;
        private int itemHeight = MinimumItemHeight;
        private int cacheCapacity = DefaultCacheCapacity;
        private long cacheAccessCounter;
        private bool isRendering;
        private int dragInsertionPageIndex = -1;
        private int lastDragAutoScrollTick;
        private int selectionAnchorPage = -1;
        private int mouseDownPage = -1;
        private Point mouseDownLocation;
        private bool mouseDownMayStartDrag;
        private bool collapseSelectionOnMouseUp;
        private bool internalDragInProgress;
        private bool pageOperationsEnabled = true;

        public PdfThumbnailList()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.Selectable,
                true);

            AutoScroll = true;
            BackColor = PanelBackColor;
            ownedUiFont = CreateArchitecturalFont(8.6f, false);
            // Page references read like drawing numbers: narrow, precise and
            // deliberately regular-weight. Selection is conveyed by colour and
            // the datum line, so a heavy bold face is unnecessary.
            pageNumberFont = CreateArchitecturalFont(9.2f, true);
            technicalCaptionFont = CreateArchitecturalFont(6.8f, true);
            Font = ownedUiFont;
            TabStop = true;
            AccessibleName = "Miniaturas de páginas";
            AccessibleRole = AccessibleRole.List;
            AllowDrop = true;

            renderTimer = new Timer
            {
                Interval = 35
            };
            renderTimer.Tick += RenderTimer_Tick;

            pageContextMenu = new ContextMenuStrip
            {
                ShowImageMargin = false,
                BackColor = Color.FromArgb(250, 249, 247),
                ForeColor = TextColor,
                Font = CreateArchitecturalFont(8.6f, false),
                Padding = new Padding(3)
            };
            selectionSummaryMenuItem = new ToolStripMenuItem
            {
                Enabled = false,
                ForeColor = MutedTextColor
            };
            rotateLeftMenuItem = new ToolStripMenuItem(
                "Girar a la izquierda");
            rotateRightMenuItem = new ToolStripMenuItem(
                "Girar a la derecha");
            deletePagesMenuItem = new ToolStripMenuItem(
                "Eliminar páginas");
            rotateLeftMenuItem.Click += delegate
            {
                RaisePageOperationRequested(
                    PdfThumbnailPageOperation.RotateLeft);
            };
            rotateRightMenuItem.Click += delegate
            {
                RaisePageOperationRequested(
                    PdfThumbnailPageOperation.RotateRight);
            };
            deletePagesMenuItem.Click += delegate
            {
                RaisePageOperationRequested(
                    PdfThumbnailPageOperation.Delete);
            };
            pageContextMenu.Items.Add(selectionSummaryMenuItem);
            pageContextMenu.Items.Add(new ToolStripSeparator());
            pageContextMenu.Items.Add(rotateLeftMenuItem);
            pageContextMenu.Items.Add(rotateRightMenuItem);
            pageContextMenu.Items.Add(new ToolStripSeparator());
            pageContextMenu.Items.Add(deletePagesMenuItem);
            pageContextMenu.Opening += PageContextMenu_Opening;
            ContextMenuStrip = pageContextMenu;
        }

        /// <summary>
        /// Raised after the user selects a thumbnail. PageIndex is zero-based.
        /// Programmatic changes to SelectedPage do not raise this event.
        /// </summary>
        public event EventHandler<PdfThumbnailPageSelectedEventArgs> PageSelected;

        /// <summary>
        /// Raised when one or more PDF files are dropped at an insertion boundary.
        /// InsertionPageIndex is zero-based and identifies the existing page before
        /// which the files should be inserted; PageCount means append at the end.
        ///
        /// A recognized PDF drop is consumed here instead of being propagated to
        /// generic DragDrop subscribers, preventing it from also opening as a tab.
        /// </summary>
        public event EventHandler<PdfFilesInsertRequestedEventArgs> PdfFilesInsertRequested;

        public event EventHandler<PdfThumbnailPagesReorderRequestedEventArgs>
            PagesReorderRequested;

        public event EventHandler<PdfThumbnailPageOperationRequestedEventArgs>
            PageOperationRequested;

        /// <summary>
        /// Gets or sets the selected zero-based page index. Set -1 to clear it.
        /// </summary>
        public int SelectedPage
        {
            get { return selectedPage; }
            set { SetSelectedPage(value, true, false); }
        }

        public IList<int> SelectedPages
        {
            get
            {
                return new ReadOnlyCollection<int>(
                    selectedPages.OrderBy(index => index).ToList());
            }
        }

        public bool PageOperationsEnabled
        {
            get { return pageOperationsEnabled; }
            set
            {
                pageOperationsEnabled = value;
                if (!value)
                {
                    mouseDownMayStartDrag = false;
                    collapseSelectionOnMouseUp = false;
                    ClearDragInsertion();
                }
            }
        }

        /// <summary>
        /// Maximum number of rendered thumbnail bitmaps retained in memory.
        /// </summary>
        public int CacheCapacity
        {
            get { return cacheCapacity; }
            set
            {
                int normalizedValue = Math.Max(4, value);
                if (cacheCapacity == normalizedValue)
                {
                    return;
                }

                cacheCapacity = normalizedValue;
                TrimCache();
            }
        }

        public int PageCount
        {
            get { return document == null ? 0 : document.PageCount; }
        }

        /// <summary>
        /// Loads a document without taking ownership of it.
        /// </summary>
        public void LoadDocument(PdfiumDocument pdfDocument)
        {
            LoadDocument(pdfDocument, false);
        }

        /// <summary>
        /// Loads a document and optionally disposes it when it is replaced/cleared.
        /// This method must be called from the control's UI thread.
        /// </summary>
        public void LoadDocument(PdfiumDocument pdfDocument, bool takeOwnership)
        {
            if (InvokeRequired)
            {
                throw new InvalidOperationException(
                    "LoadDocument debe llamarse desde el hilo de la interfaz.");
            }

            if (ReferenceEquals(document, pdfDocument))
            {
                ownsDocument = takeOwnership;
                ClearThumbnailCache();
                selectedPage = document != null && document.PageCount > 0
                    ? Math.Max(0, Math.Min(document.PageCount - 1, selectedPage))
                    : -1;
                selectedPages.Clear();
                if (selectedPage >= 0)
                {
                    selectedPages.Add(selectedPage);
                }
                selectionAnchorPage = selectedPage;
                activePage = selectedPage;
                documentGeneration++;
                RecalculateVirtualSize();
                QueueVisiblePages();
                Invalidate();
                return;
            }

            ReleaseCurrentDocument();
            document = pdfDocument;
            ownsDocument = takeOwnership;
            selectedPage = document != null && document.PageCount > 0 ? 0 : -1;
            selectedPages.Clear();
            if (selectedPage >= 0)
            {
                selectedPages.Add(selectedPage);
            }
            selectionAnchorPage = selectedPage;
            activePage = selectedPage;
            documentGeneration++;
            RecalculateVirtualSize();
            QueueVisiblePages();
            Invalidate();
        }

        /// <summary>
        /// Releases cached images and the current document reference. A document is
        /// disposed only when LoadDocument(document, true) was used.
        /// </summary>
        public void DisposeDocument()
        {
            if (InvokeRequired)
            {
                throw new InvalidOperationException(
                    "DisposeDocument debe llamarse desde el hilo de la interfaz.");
            }

            ReleaseCurrentDocument();
            RecalculateVirtualSize();
            Invalidate();
        }

        public void ClearDocument()
        {
            DisposeDocument();
        }

        /// <summary>
        /// Updates the active page without producing a navigation event.
        /// Useful when the main PDF view scrolls to another page.
        /// </summary>
        public void SetActivePage(int pageIndex, bool ensureVisible)
        {
            if (document == null || document.PageCount == 0)
            {
                activePage = -1;
                return;
            }

            pageIndex = Math.Max(
                0,
                Math.Min(document.PageCount - 1, pageIndex));
            if (activePage != pageIndex)
            {
                var previousActivePage = activePage;
                activePage = pageIndex;
                if (previousActivePage >= 0)
                {
                    Invalidate(GetItemBounds(previousActivePage));
                }

                Invalidate(GetItemBounds(activePage));
            }

            if (ensureVisible)
            {
                EnsurePageVisible(activePage);
            }

            EnqueuePageFirst(activePage);
        }

        public void SetSelectedPages(
            IEnumerable<int> pageIndexes,
            int activePageIndex,
            bool ensureVisible)
        {
            var normalized = NormalizePageIndexes(pageIndexes);
            if (normalized.Count == 0 &&
                document != null &&
                document.PageCount > 0)
            {
                normalized.Add(
                    Math.Max(
                        0,
                        Math.Min(
                            document.PageCount - 1,
                            activePageIndex)));
            }

            ApplySelection(
                normalized,
                activePageIndex,
                ensureVisible,
                false);
            selectionAnchorPage = selectedPage;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            if (document == null || document.PageCount == 0)
            {
                DrawEmptyState(e.Graphics);
                return;
            }

            int firstPage;
            int lastPage;
            GetVisiblePageRange(out firstPage, out lastPage);

            for (int pageIndex = firstPage; pageIndex <= lastPage; pageIndex++)
            {
                DrawPageItem(e.Graphics, pageIndex);
            }

            DrawInsertionIndicator(e.Graphics);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (ClientSize.Width <= 0)
            {
                return;
            }

            int previousItemHeight = itemHeight;
            itemHeight = CalculateItemHeight();
            if (itemHeight != previousItemHeight)
            {
                ClearThumbnailCache();
                RecalculateVirtualSize();
            }

            QueueVisiblePages();
            Invalidate();
        }

        protected override void OnScroll(ScrollEventArgs se)
        {
            base.OnScroll(se);
            QueueVisiblePages();
            Invalidate();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible)
            {
                QueueVisiblePages();
            }
            else
            {
                renderTimer.Stop();
            }
        }

        protected override void OnDragEnter(DragEventArgs drgevent)
        {
            PdfThumbnailInternalDragData internalDrag;
            if (CanHandleInternalPageReorder(
                    drgevent,
                    out internalDrag))
            {
                UpdateDragInsertion(drgevent);
                drgevent.Effect = DragDropEffects.Move;
                return;
            }

            IList<string> pdfPaths;
            if (CanHandlePdfInsertion(drgevent, out pdfPaths))
            {
                UpdateDragInsertion(drgevent);
                drgevent.Effect = DragDropEffects.Copy;
                return;
            }

            ClearDragInsertion();
            base.OnDragEnter(drgevent);
        }

        protected override void OnDragOver(DragEventArgs drgevent)
        {
            PdfThumbnailInternalDragData internalDrag;
            if (CanHandleInternalPageReorder(
                    drgevent,
                    out internalDrag))
            {
                AutoScrollDuringDrag(
                    PointToClient(
                        new Point(drgevent.X, drgevent.Y)));
                UpdateDragInsertion(drgevent);
                drgevent.Effect = DragDropEffects.Move;
                return;
            }

            IList<string> pdfPaths;
            if (CanHandlePdfInsertion(drgevent, out pdfPaths))
            {
                AutoScrollDuringDrag(PointToClient(new Point(drgevent.X, drgevent.Y)));
                UpdateDragInsertion(drgevent);
                drgevent.Effect = DragDropEffects.Copy;
                return;
            }

            ClearDragInsertion();
            base.OnDragOver(drgevent);
        }

        protected override void OnDragLeave(EventArgs e)
        {
            ClearDragInsertion();
            base.OnDragLeave(e);
        }

        protected override void OnDragDrop(DragEventArgs drgevent)
        {
            PdfThumbnailInternalDragData internalDrag;
            if (CanHandleInternalPageReorder(
                    drgevent,
                    out internalDrag))
            {
                var clientPoint = PointToClient(
                    new Point(drgevent.X, drgevent.Y));
                var insertionPageIndex =
                    GetInsertionPageIndex(clientPoint);
                drgevent.Effect = DragDropEffects.Move;
                try
                {
                    var handler = PagesReorderRequested;
                    if (handler != null)
                    {
                        handler(
                            this,
                            new PdfThumbnailPagesReorderRequestedEventArgs(
                                internalDrag.PageIndexes,
                                insertionPageIndex));
                    }
                }
                finally
                {
                    internalDragInProgress = false;
                    ClearDragInsertion();
                }

                return;
            }

            IList<string> pdfPaths;
            if (CanHandlePdfInsertion(drgevent, out pdfPaths))
            {
                Point clientPoint = PointToClient(new Point(drgevent.X, drgevent.Y));
                int insertionPageIndex = GetInsertionPageIndex(clientPoint);
                drgevent.Effect = DragDropEffects.Copy;

                try
                {
                    EventHandler<PdfFilesInsertRequestedEventArgs> handler =
                        PdfFilesInsertRequested;
                    if (handler != null)
                    {
                        handler(
                            this,
                            new PdfFilesInsertRequestedEventArgs(
                                pdfPaths,
                                insertionPageIndex));
                    }
                }
                finally
                {
                    ClearDragInsertion();
                }

                // Deliberately do not call base.OnDragDrop. The form also attaches
                // a generic file-drop handler to this control; raising it would
                // open the same PDFs as tabs after requesting page insertion.
                return;
            }

            ClearDragInsertion();
            base.OnDragDrop(drgevent);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (document == null)
            {
                return;
            }

            Focus();
            int pageIndex = HitTestPage(e.Location);
            if (pageIndex < 0)
            {
                mouseDownPage = -1;
                mouseDownMayStartDrag = false;
                collapseSelectionOnMouseUp = false;
                return;
            }

            if (e.Button == MouseButtons.Right)
            {
                if (!selectedPages.Contains(pageIndex))
                {
                    SetSelectedPage(pageIndex, false, false);
                }
                else
                {
                    SetActivePageWithinSelection(
                        pageIndex,
                        false,
                        false);
                }

                return;
            }

            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            var modifiers = ModifierKeys;
            var controlPressed =
                (modifiers & Keys.Control) == Keys.Control;
            var shiftPressed =
                (modifiers & Keys.Shift) == Keys.Shift;
            collapseSelectionOnMouseUp = false;

            if (shiftPressed)
            {
                SelectRange(
                    pageIndex,
                    controlPressed,
                    true);
            }
            else if (controlPressed)
            {
                ToggleSelectedPage(pageIndex, true);
            }
            else if (selectedPages.Count > 1 &&
                selectedPages.Contains(pageIndex))
            {
                SetActivePageWithinSelection(
                    pageIndex,
                    false,
                    true);
                collapseSelectionOnMouseUp = true;
            }
            else
            {
                SetSelectedPage(pageIndex, false, true);
            }

            mouseDownPage = pageIndex;
            mouseDownLocation = e.Location;
            mouseDownMayStartDrag =
                pageOperationsEnabled &&
                PagesReorderRequested != null &&
                selectedPages.Contains(pageIndex);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!mouseDownMayStartDrag ||
                internalDragInProgress ||
                e.Button != MouseButtons.Left ||
                mouseDownPage < 0)
            {
                return;
            }

            var dragSize = SystemInformation.DragSize;
            var dragBounds = new Rectangle(
                mouseDownLocation.X - dragSize.Width / 2,
                mouseDownLocation.Y - dragSize.Height / 2,
                dragSize.Width,
                dragSize.Height);
            if (dragBounds.Contains(e.Location))
            {
                return;
            }

            collapseSelectionOnMouseUp = false;
            mouseDownMayStartDrag = false;
            internalDragInProgress = true;
            renderTimer.Stop();
            try
            {
                var data = new DataObject();
                data.SetData(
                    InternalPageDragFormat,
                    false,
                    new PdfThumbnailInternalDragData(
                        this,
                        documentGeneration,
                        SelectedPages));
                DoDragDrop(data, DragDropEffects.Move);
            }
            finally
            {
                internalDragInProgress = false;
                ClearDragInsertion();
                QueueVisiblePages();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left &&
                collapseSelectionOnMouseUp &&
                mouseDownPage >= 0 &&
                HitTestPage(e.Location) == mouseDownPage)
            {
                SetSelectedPage(
                    mouseDownPage,
                    false,
                    true);
            }

            mouseDownPage = -1;
            mouseDownMayStartDrag = false;
            collapseSelectionOnMouseUp = false;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (document == null || document.PageCount == 0)
            {
                return base.ProcessCmdKey(ref msg, keyData);
            }

            int targetPage = selectedPage < 0 ? 0 : selectedPage;
            if (keyData == (Keys.Control | Keys.A))
            {
                ApplySelection(
                    Enumerable.Range(0, document.PageCount),
                    targetPage,
                    false,
                    false);
                return true;
            }

            if (keyData == Keys.Delete &&
                pageOperationsEnabled &&
                PageOperationRequested != null)
            {
                RaisePageOperationRequested(
                    PdfThumbnailPageOperation.Delete);
                return true;
            }

            var shiftPressed =
                (keyData & Keys.Shift) == Keys.Shift;
            var navigationKey =
                keyData & Keys.KeyCode;
            switch (navigationKey)
            {
                case Keys.Up:
                    targetPage--;
                    break;
                case Keys.Down:
                    targetPage++;
                    break;
                case Keys.PageUp:
                    targetPage -= Math.Max(1, GetVisibleItemCount());
                    break;
                case Keys.PageDown:
                    targetPage += Math.Max(1, GetVisibleItemCount());
                    break;
                case Keys.Home:
                    targetPage = 0;
                    break;
                case Keys.End:
                    targetPage = document.PageCount - 1;
                    break;
                case Keys.Enter:
                case Keys.Space:
                    RaisePageSelected(selectedPage);
                    return true;
                default:
                    return base.ProcessCmdKey(ref msg, keyData);
            }

            targetPage = Math.Max(0, Math.Min(document.PageCount - 1, targetPage));
            if (shiftPressed)
            {
                SelectRange(targetPage, false, true);
                EnsurePageVisible(targetPage);
            }
            else
            {
                SetSelectedPage(targetPage, true, true);
            }
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                renderTimer.Stop();
                renderTimer.Tick -= RenderTimer_Tick;
                renderTimer.Dispose();
                ReleaseCurrentDocument();
                pageContextMenu.Opening -= PageContextMenu_Opening;
                var contextMenuFont = pageContextMenu.Font;
                pageContextMenu.Dispose();
                if (contextMenuFont != null)
                {
                    contextMenuFont.Dispose();
                }
                technicalCaptionFont.Dispose();
                pageNumberFont.Dispose();
                ownedUiFont.Dispose();
            }

            base.Dispose(disposing);
        }

        private void RenderTimer_Tick(object sender, EventArgs e)
        {
            if (isRendering || document == null || renderQueue.Count == 0)
            {
                if (renderQueue.Count == 0)
                {
                    renderTimer.Stop();
                }

                return;
            }

            int pageIndex = renderQueue[0];
            renderQueue.RemoveAt(0);
            if (pageIndex < 0 ||
                pageIndex >= document.PageCount ||
                thumbnailCache.ContainsKey(pageIndex) ||
                failedPages.Contains(pageIndex))
            {
                return;
            }

            Size renderSize = GetThumbnailSize(pageIndex);
            if (renderSize.Width <= 0 || renderSize.Height <= 0)
            {
                return;
            }

            isRendering = true;
            try
            {
                Image image = document.Render(
                    pageIndex,
                    renderSize.Width,
                    renderSize.Height,
                    96,
                    96,
                    PdfRenderFlags.Annotations |
                    PdfRenderFlags.LcdText |
                    PdfRenderFlags.LimitImageCacheSize);

                if (image != null)
                {
                    AddToCache(pageIndex, image, renderSize);
                    Invalidate(GetItemBounds(pageIndex));
                }
            }
            catch
            {
                failedPages.Add(pageIndex);
                Invalidate(GetItemBounds(pageIndex));
            }
            finally
            {
                isRendering = false;
            }

            if (renderQueue.Count == 0)
            {
                renderTimer.Stop();
            }
        }

        private void SetSelectedPage(int pageIndex, bool ensureVisible, bool notify)
        {
            if (document == null || document.PageCount == 0)
            {
                pageIndex = -1;
            }
            else
            {
                pageIndex = Math.Max(-1, Math.Min(document.PageCount - 1, pageIndex));
            }

            if (selectedPage == pageIndex &&
                ((pageIndex < 0 && selectedPages.Count == 0) ||
                 (pageIndex >= 0 &&
                  selectedPages.Count == 1 &&
                  selectedPages.Contains(pageIndex))))
            {
                if (ensureVisible && pageIndex >= 0)
                {
                    EnsurePageVisible(pageIndex);
                }

                if (notify && pageIndex >= 0)
                {
                    RaisePageSelected(pageIndex);
                }

                return;
            }

            var nextSelection = new List<int>();
            if (pageIndex >= 0)
            {
                nextSelection.Add(pageIndex);
            }

            ApplySelection(
                nextSelection,
                pageIndex,
                ensureVisible,
                notify);
            selectionAnchorPage = pageIndex;
        }

        private void ApplySelection(
            IEnumerable<int> pageIndexes,
            int activePageIndex,
            bool ensureVisible,
            bool notify)
        {
            var normalized = NormalizePageIndexes(pageIndexes);
            if (normalized.Count == 0)
            {
                activePageIndex = -1;
            }
            else if (!normalized.Contains(activePageIndex))
            {
                activePageIndex = normalized[normalized.Count - 1];
            }

            selectedPages.Clear();
            foreach (var pageIndex in normalized)
            {
                selectedPages.Add(pageIndex);
            }

            selectedPage = activePageIndex;
            if (notify)
            {
                activePage = selectedPage;
            }
            Invalidate();

            if (selectedPage >= 0)
            {
                if (ensureVisible)
                {
                    EnsurePageVisible(selectedPage);
                }
                else
                {
                    QueueVisiblePages();
                }

                EnqueuePageFirst(selectedPage);
                Invalidate(GetItemBounds(selectedPage));
            }

            if (notify && selectedPage >= 0)
            {
                RaisePageSelected(selectedPage);
            }
        }

        private List<int> NormalizePageIndexes(
            IEnumerable<int> pageIndexes)
        {
            if (document == null ||
                document.PageCount == 0 ||
                pageIndexes == null)
            {
                return new List<int>();
            }

            return pageIndexes
                .Where(index =>
                    index >= 0 &&
                    index < document.PageCount)
                .Distinct()
                .OrderBy(index => index)
                .ToList();
        }

        private void SetActivePageWithinSelection(
            int pageIndex,
            bool ensureVisible,
            bool notify)
        {
            if (!selectedPages.Contains(pageIndex))
            {
                SetSelectedPage(
                    pageIndex,
                    ensureVisible,
                    notify);
                return;
            }

            var previousActive = selectedPage;
            selectedPage = pageIndex;
            var previousViewerPage = activePage;
            if (notify)
            {
                activePage = pageIndex;
            }
            if (previousActive >= 0)
            {
                Invalidate(GetItemBounds(previousActive));
            }

            Invalidate(GetItemBounds(selectedPage));
            if (previousViewerPage >= 0 &&
                previousViewerPage != activePage)
            {
                Invalidate(GetItemBounds(previousViewerPage));
            }
            if (ensureVisible)
            {
                EnsurePageVisible(selectedPage);
            }

            if (notify)
            {
                RaisePageSelected(selectedPage);
            }
        }

        private void ToggleSelectedPage(int pageIndex, bool notify)
        {
            if (pageIndex < 0 ||
                document == null ||
                pageIndex >= document.PageCount)
            {
                return;
            }

            var nextSelection = new HashSet<int>(selectedPages);
            if (nextSelection.Contains(pageIndex) &&
                nextSelection.Count > 1)
            {
                nextSelection.Remove(pageIndex);
                var nextActive = selectedPage == pageIndex
                    ? nextSelection.OrderBy(index => index).Last()
                    : selectedPage;
                ApplySelection(
                    nextSelection,
                    nextActive,
                    false,
                    notify);
            }
            else
            {
                nextSelection.Add(pageIndex);
                ApplySelection(
                    nextSelection,
                    pageIndex,
                    false,
                    notify);
            }

            selectionAnchorPage = pageIndex;
        }

        private void SelectRange(
            int targetPageIndex,
            bool addToSelection,
            bool notify)
        {
            if (document == null || document.PageCount == 0)
            {
                return;
            }

            targetPageIndex = Math.Max(
                0,
                Math.Min(document.PageCount - 1, targetPageIndex));
            var anchor = selectionAnchorPage >= 0
                ? selectionAnchorPage
                : selectedPage >= 0
                    ? selectedPage
                    : targetPageIndex;
            var first = Math.Min(anchor, targetPageIndex);
            var last = Math.Max(anchor, targetPageIndex);
            var nextSelection = addToSelection
                ? new HashSet<int>(selectedPages)
                : new HashSet<int>();
            for (var pageIndex = first;
                pageIndex <= last;
                pageIndex++)
            {
                nextSelection.Add(pageIndex);
            }

            ApplySelection(
                nextSelection,
                targetPageIndex,
                false,
                notify);
        }

        private void RaisePageSelected(int pageIndex)
        {
            if (pageIndex < 0 || document == null || pageIndex >= document.PageCount)
            {
                return;
            }

            EventHandler<PdfThumbnailPageSelectedEventArgs> handler = PageSelected;
            if (handler != null)
            {
                handler(this, new PdfThumbnailPageSelectedEventArgs(pageIndex));
            }
        }

        private void DrawPageItem(Graphics graphics, int pageIndex)
        {
            Rectangle itemBounds = GetItemBounds(pageIndex);
            Rectangle cardBounds = new Rectangle(
                itemBounds.Left + 2,
                itemBounds.Top + 1,
                Math.Max(1, itemBounds.Width - 4),
                Math.Max(1, itemBounds.Height - ItemGap - 2));
            bool isSelected = selectedPages.Contains(pageIndex);
            bool isFocused = pageIndex == selectedPage;
            bool isActive = pageIndex == activePage;

            using (var cardBrush = new SolidBrush(isSelected ? SelectedBackColor : CardBackColor))
            {
                graphics.FillRectangle(cardBrush, cardBounds);
            }

            using (var cardBorderPen = new Pen(
                isSelected ? SelectedBorderColor : TechnicalLineColor,
                1f))
            {
                graphics.DrawRectangle(
                    cardBorderPen,
                    cardBounds.Left,
                    cardBounds.Top,
                    Math.Max(0, cardBounds.Width - 1),
                    Math.Max(0, cardBounds.Height - 1));
            }

            if (isFocused)
            {
                using (var accentBrush = new SolidBrush(SelectedBorderColor))
                {
                    graphics.FillRectangle(
                        accentBrush,
                        cardBounds.Left,
                        cardBounds.Top,
                        2,
                        cardBounds.Height);
                }
            }

            Size thumbnailSize = GetThumbnailSize(pageIndex);
            Rectangle thumbnailBounds = new Rectangle(
                cardBounds.Left + Math.Max(4, (cardBounds.Width - thumbnailSize.Width) / 2),
                cardBounds.Top + CardVerticalPadding,
                thumbnailSize.Width,
                thumbnailSize.Height);

            Rectangle shadowBounds = thumbnailBounds;
            shadowBounds.Offset(2, 3);
            using (var shadowBrush = new SolidBrush(PageShadowColor))
            {
                graphics.FillRectangle(shadowBrush, shadowBounds);
            }

            ThumbnailCacheEntry cacheEntry;
            if (thumbnailCache.TryGetValue(pageIndex, out cacheEntry) &&
                cacheEntry.RenderSize == thumbnailSize)
            {
                cacheEntry.LastAccess = ++cacheAccessCounter;
                graphics.DrawImage(cacheEntry.Image, thumbnailBounds);
            }
            else
            {
                DrawThumbnailPlaceholder(graphics, thumbnailBounds, failedPages.Contains(pageIndex));
            }

            using (var borderPen = new Pen(PageBorderColor))
            {
                graphics.DrawRectangle(
                    borderPen,
                    thumbnailBounds.Left,
                    thumbnailBounds.Top,
                    Math.Max(0, thumbnailBounds.Width - 1),
                    Math.Max(0, thumbnailBounds.Height - 1));
            }

            Rectangle pageNumberBounds = new Rectangle(
                cardBounds.Left + 9,
                cardBounds.Bottom - PageNumberHeight,
                Math.Max(1, cardBounds.Width - 18),
                PageNumberHeight);

            using (var footerPen = new Pen(
                isSelected ? SelectedBorderColor : TechnicalLineColor,
                1f))
            {
                graphics.DrawLine(
                    footerPen,
                    pageNumberBounds.Left,
                    pageNumberBounds.Top,
                    pageNumberBounds.Right,
                    pageNumberBounds.Top);
            }

            TextRenderer.DrawText(
                graphics,
                "PÁGINA",
                technicalCaptionFont,
                pageNumberBounds,
                MutedTextColor,
                TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding |
                TextFormatFlags.SingleLine);
            TextRenderer.DrawText(
                graphics,
                FormatPageNumber(pageIndex + 1),
                pageNumberFont,
                pageNumberBounds,
                isSelected ? SelectedBorderColor : TextColor,
                TextFormatFlags.Right |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding |
                TextFormatFlags.SingleLine);

            if (isActive && !isSelected)
            {
                using (var activePen = new Pen(MutedTextColor, 1f))
                {
                    graphics.DrawLine(
                        activePen,
                        cardBounds.Left + 1,
                        cardBounds.Top + 2,
                        cardBounds.Left + 1,
                        cardBounds.Bottom - 2);
                }
            }

            if (Focused && isFocused)
            {
                Rectangle focusBounds = cardBounds;
                focusBounds.Inflate(-5, -5);
                ControlPaint.DrawFocusRectangle(graphics, focusBounds);
            }
        }

        private void DrawThumbnailPlaceholder(Graphics graphics, Rectangle bounds, bool failed)
        {
            using (var placeholderBrush = new SolidBrush(PlaceholderColor))
            {
                graphics.FillRectangle(placeholderBrush, bounds);
            }

            if (failed)
            {
                TextRenderer.DrawText(
                    graphics,
                    "Sin vista",
                    Font,
                    bounds,
                    MutedTextColor,
                    TextFormatFlags.HorizontalCenter |
                    TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPadding);
                return;
            }

            int lineLeft = bounds.Left + Math.Max(8, bounds.Width / 7);
            int lineRight = bounds.Right - Math.Max(8, bounds.Width / 7);
            int firstLineTop = bounds.Top + Math.Max(12, bounds.Height / 5);
            int lineGap = Math.Max(8, bounds.Height / 10);
            using (var linePen = new Pen(PlaceholderLineColor, 2f))
            {
                for (int lineIndex = 0; lineIndex < 4; lineIndex++)
                {
                    int lineTop = firstLineTop + lineIndex * lineGap;
                    int right = lineIndex == 3
                        ? lineLeft + (lineRight - lineLeft) * 2 / 3
                        : lineRight;
                    graphics.DrawLine(linePen, lineLeft, lineTop, right, lineTop);
                }
            }
        }

        private void DrawEmptyState(Graphics graphics)
        {
            Rectangle bounds = ClientRectangle;
            bounds.Inflate(-12, -12);
            TextRenderer.DrawText(
                graphics,
                "Sin páginas",
                Font,
                bounds,
                MutedTextColor,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding);
        }

        private void DrawInsertionIndicator(Graphics graphics)
        {
            if (dragInsertionPageIndex < 0 ||
                document == null ||
                document.PageCount == 0)
            {
                return;
            }

            int lineY = GetInsertionLineY(dragInsertionPageIndex);
            if (lineY < -5 || lineY > ClientSize.Height + 5)
            {
                return;
            }

            int left = OuterHorizontalPadding + 6;
            int right = Math.Max(left + 12, ClientSize.Width - OuterHorizontalPadding - 6);
            using (var indicatorPen = new Pen(InsertIndicatorColor, 2f))
            {
                indicatorPen.StartCap = LineCap.Square;
                indicatorPen.EndCap = LineCap.Square;
                graphics.DrawLine(indicatorPen, left, lineY, right, lineY);
            }

            const int markerSize = 6;
            int markerTop = lineY - markerSize / 2;
            using (var markerBrush = new SolidBrush(InsertIndicatorColor))
            {
                graphics.FillRectangle(
                    markerBrush,
                    left - markerSize / 2,
                    markerTop,
                    markerSize,
                    markerSize);
                graphics.FillRectangle(
                    markerBrush,
                    right - markerSize / 2,
                    markerTop,
                    markerSize,
                    markerSize);
            }
        }

        private void QueueVisiblePages()
        {
            renderQueue.Clear();
            if (!Visible || document == null || document.PageCount == 0)
            {
                renderTimer.Stop();
                return;
            }

            int firstPage;
            int lastPage;
            GetVisiblePageRange(out firstPage, out lastPage);

            int centerPage = firstPage + (lastPage - firstPage) / 2;
            var candidates = new List<int>();
            for (int pageIndex = Math.Max(0, firstPage - NearbyPageCount);
                 pageIndex <= Math.Min(document.PageCount - 1, lastPage + NearbyPageCount);
                 pageIndex++)
            {
                if (!HasCurrentThumbnail(pageIndex) && !failedPages.Contains(pageIndex))
                {
                    candidates.Add(pageIndex);
                }
            }

            foreach (int pageIndex in candidates.OrderBy(index => Math.Abs(index - centerPage)))
            {
                renderQueue.Add(pageIndex);
            }

            if (renderQueue.Count > 0)
            {
                renderTimer.Start();
            }
            else
            {
                renderTimer.Stop();
            }
        }

        private void EnqueuePageFirst(int pageIndex)
        {
            if (document == null ||
                pageIndex < 0 ||
                pageIndex >= document.PageCount ||
                HasCurrentThumbnail(pageIndex) ||
                failedPages.Contains(pageIndex))
            {
                return;
            }

            renderQueue.Remove(pageIndex);
            renderQueue.Insert(0, pageIndex);
            if (Visible)
            {
                renderTimer.Start();
            }
        }

        private bool HasCurrentThumbnail(int pageIndex)
        {
            ThumbnailCacheEntry entry;
            if (!thumbnailCache.TryGetValue(pageIndex, out entry))
            {
                return false;
            }

            if (entry.RenderSize == GetThumbnailSize(pageIndex))
            {
                return true;
            }

            thumbnailCache.Remove(pageIndex);
            entry.Image.Dispose();
            return false;
        }

        private void GetVisiblePageRange(out int firstPage, out int lastPage)
        {
            if (document == null || document.PageCount == 0)
            {
                firstPage = 0;
                lastPage = -1;
                return;
            }

            int scrollTop = Math.Max(0, -AutoScrollPosition.Y);
            int viewportBottom = scrollTop + Math.Max(1, ClientSize.Height);
            firstPage = Math.Max(
                0,
                Math.Min(document.PageCount - 1, scrollTop / Math.Max(1, itemHeight)));
            lastPage = Math.Max(
                firstPage,
                Math.Min(document.PageCount - 1, viewportBottom / Math.Max(1, itemHeight)));
        }

        private int GetVisibleItemCount()
        {
            return Math.Max(1, ClientSize.Height / Math.Max(1, itemHeight));
        }

        private Rectangle GetItemBounds(int pageIndex)
        {
            int top = OuterVerticalPadding +
                pageIndex * itemHeight +
                AutoScrollPosition.Y;
            return new Rectangle(
                OuterHorizontalPadding,
                top,
                Math.Max(1, ClientSize.Width - OuterHorizontalPadding * 2),
                itemHeight);
        }

        private int HitTestPage(Point location)
        {
            if (document == null || itemHeight <= 0)
            {
                return -1;
            }

            int virtualY = location.Y - AutoScrollPosition.Y - OuterVerticalPadding;
            if (virtualY < 0)
            {
                return -1;
            }

            int pageIndex = virtualY / itemHeight;
            if (pageIndex < 0 || pageIndex >= document.PageCount)
            {
                return -1;
            }

            return GetItemBounds(pageIndex).Contains(location) ? pageIndex : -1;
        }

        private bool CanHandleInternalPageReorder(
            DragEventArgs dragEvent,
            out PdfThumbnailInternalDragData internalDrag)
        {
            internalDrag = null;
            if (!pageOperationsEnabled ||
                document == null ||
                document.PageCount == 0 ||
                PagesReorderRequested == null ||
                dragEvent == null ||
                dragEvent.Data == null ||
                (dragEvent.AllowedEffect & DragDropEffects.Move) == 0)
            {
                return false;
            }

            try
            {
                if (!dragEvent.Data.GetDataPresent(
                        InternalPageDragFormat,
                        false))
                {
                    return false;
                }

                internalDrag = dragEvent.Data.GetData(
                    InternalPageDragFormat,
                    false) as PdfThumbnailInternalDragData;
                return internalDrag != null &&
                    ReferenceEquals(internalDrag.Source, this) &&
                    internalDrag.DocumentGeneration ==
                        documentGeneration &&
                    internalDrag.PageIndexes.Count > 0;
            }
            catch
            {
                internalDrag = null;
                return false;
            }
        }

        private bool CanHandlePdfInsertion(
            DragEventArgs dragEvent,
            out IList<string> pdfPaths)
        {
            pdfPaths = null;
            if (document == null ||
                document.PageCount == 0 ||
                PdfFilesInsertRequested == null ||
                (dragEvent.AllowedEffect & DragDropEffects.Copy) == 0)
            {
                return false;
            }

            pdfPaths = GetPdfFilePaths(dragEvent.Data);
            return pdfPaths != null && pdfPaths.Count > 0;
        }

        private static IList<string> GetPdfFilePaths(IDataObject data)
        {
            if (data == null)
            {
                return null;
            }

            try
            {
                if (!data.GetDataPresent(DataFormats.FileDrop, true))
                {
                    return null;
                }

                string[] paths = data.GetData(DataFormats.FileDrop, true) as string[];
                if (paths == null || paths.Length == 0)
                {
                    return null;
                }

                var normalizedPaths = new List<string>(paths.Length);
                foreach (string path in paths)
                {
                    if (string.IsNullOrWhiteSpace(path) ||
                        !string.Equals(
                            Path.GetExtension(path),
                            ".pdf",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return null;
                    }

                    string normalizedPath;
                    try
                    {
                        normalizedPath = Path.GetFullPath(path);
                    }
                    catch
                    {
                        normalizedPath = path;
                    }

                    normalizedPaths.Add(normalizedPath);
                }

                return new ReadOnlyCollection<string>(normalizedPaths);
            }
            catch
            {
                return null;
            }
        }

        private void PageContextMenu_Opening(
            object sender,
            System.ComponentModel.CancelEventArgs e)
        {
            var selectionCount = selectedPages.Count;
            if (document == null ||
                document.PageCount == 0 ||
                selectionCount == 0)
            {
                e.Cancel = true;
                return;
            }

            selectionSummaryMenuItem.Text = selectionCount == 1
                ? "1 PÁGINA SELECCIONADA"
                : selectionCount + " PÁGINAS SELECCIONADAS";
            var canEdit =
                pageOperationsEnabled &&
                PageOperationRequested != null;
            rotateLeftMenuItem.Enabled = canEdit;
            rotateRightMenuItem.Enabled = canEdit;
            deletePagesMenuItem.Enabled =
                canEdit &&
                selectionCount < document.PageCount;
            deletePagesMenuItem.Text = selectionCount == 1
                ? "Eliminar página                         Supr"
                : "Eliminar " + selectionCount +
                    " páginas                         Supr";
        }

        private void RaisePageOperationRequested(
            PdfThumbnailPageOperation operation)
        {
            if (!pageOperationsEnabled ||
                selectedPages.Count == 0)
            {
                return;
            }

            var handler = PageOperationRequested;
            if (handler != null)
            {
                handler(
                    this,
                    new PdfThumbnailPageOperationRequestedEventArgs(
                        operation,
                        SelectedPages,
                        selectedPage));
            }
        }

        private void UpdateDragInsertion(DragEventArgs dragEvent)
        {
            Point clientPoint = PointToClient(new Point(dragEvent.X, dragEvent.Y));
            int insertionPageIndex = GetInsertionPageIndex(clientPoint);
            if (dragInsertionPageIndex == insertionPageIndex)
            {
                return;
            }

            dragInsertionPageIndex = insertionPageIndex;
            Invalidate();
        }

        private int GetInsertionPageIndex(Point clientPoint)
        {
            int pageCount = document == null ? 0 : document.PageCount;
            if (pageCount == 0 || itemHeight <= 0)
            {
                return 0;
            }

            int virtualY =
                clientPoint.Y -
                AutoScrollPosition.Y -
                OuterVerticalPadding;

            if (virtualY <= 0)
            {
                return 0;
            }

            long roundedBoundary =
                ((long)virtualY + itemHeight / 2L) /
                Math.Max(1, itemHeight);
            return (int)Math.Max(0L, Math.Min((long)pageCount, roundedBoundary));
        }

        private int GetInsertionLineY(int insertionPageIndex)
        {
            int pageCount = document == null ? 0 : document.PageCount;
            int clampedIndex = Math.Max(0, Math.Min(pageCount, insertionPageIndex));

            if (clampedIndex == 0)
            {
                return OuterVerticalPadding + AutoScrollPosition.Y + 1;
            }

            int nextItemTop =
                OuterVerticalPadding +
                clampedIndex * itemHeight +
                AutoScrollPosition.Y;
            return nextItemTop - ItemGap / 2;
        }

        private void AutoScrollDuringDrag(Point clientPoint)
        {
            if (!VerticalScroll.Visible || ClientSize.Height <= 0)
            {
                return;
            }

            int direction = 0;
            if (clientPoint.Y <= DragAutoScrollMargin)
            {
                direction = -1;
            }
            else if (clientPoint.Y >= ClientSize.Height - DragAutoScrollMargin)
            {
                direction = 1;
            }

            if (direction == 0)
            {
                return;
            }

            int currentTick = Environment.TickCount;
            int elapsed = unchecked(currentTick - lastDragAutoScrollTick);
            if (elapsed >= 0 && elapsed < DragAutoScrollInterval)
            {
                return;
            }

            lastDragAutoScrollTick = currentTick;
            int currentTop = Math.Max(0, -AutoScrollPosition.Y);
            int maximumTop = Math.Max(0, AutoScrollMinSize.Height - ClientSize.Height);
            int targetTop = Math.Max(
                0,
                Math.Min(maximumTop, currentTop + direction * DragAutoScrollStep));
            if (targetTop == currentTop)
            {
                return;
            }

            AutoScrollPosition = new Point(0, targetTop);
            QueueVisiblePages();
            Invalidate();
        }

        private void ClearDragInsertion()
        {
            if (dragInsertionPageIndex < 0)
            {
                return;
            }

            dragInsertionPageIndex = -1;
            lastDragAutoScrollTick = 0;
            Invalidate();
        }

        private void EnsurePageVisible(int pageIndex)
        {
            if (pageIndex < 0 || document == null || pageIndex >= document.PageCount)
            {
                return;
            }

            int pageTop = OuterVerticalPadding + pageIndex * itemHeight;
            int pageBottom = pageTop + itemHeight;
            int currentTop = Math.Max(0, -AutoScrollPosition.Y);
            int currentBottom = currentTop + ClientSize.Height;
            int targetTop = currentTop;

            if (pageTop < currentTop)
            {
                targetTop = pageTop;
            }
            else if (pageBottom > currentBottom)
            {
                targetTop = pageBottom - ClientSize.Height;
            }

            if (targetTop != currentTop)
            {
                AutoScrollPosition = new Point(0, Math.Max(0, targetTop));
            }

            QueueVisiblePages();
        }

        private Size GetThumbnailSize(int pageIndex)
        {
            int availableWidth = Math.Max(
                24,
                Math.Min(
                    MaximumThumbnailWidth,
                    ClientSize.Width - OuterHorizontalPadding * 2 - 20));
            int availableHeight = MaximumThumbnailHeight;
            float pageWidth = 595f;
            float pageHeight = 842f;

            if (document != null &&
                pageIndex >= 0 &&
                pageIndex < document.PageCount &&
                document.PageSizes != null &&
                pageIndex < document.PageSizes.Count)
            {
                SizeF pageSize = document.PageSizes[pageIndex];
                if (pageSize.Width > 0f && pageSize.Height > 0f)
                {
                    pageWidth = pageSize.Width;
                    pageHeight = pageSize.Height;
                }
            }

            float scale = Math.Min(
                availableWidth / pageWidth,
                availableHeight / pageHeight);
            int width = Math.Max(16, (int)Math.Round(pageWidth * scale));
            int height = Math.Max(16, (int)Math.Round(pageHeight * scale));
            return new Size(width, height);
        }

        private int CalculateItemHeight()
        {
            int maximumPageHeight = MaximumThumbnailHeight;
            return Math.Max(
                MinimumItemHeight,
                CardVerticalPadding * 2 + maximumPageHeight + PageNumberHeight + ItemGap);
        }

        private void RecalculateVirtualSize()
        {
            int pageCount = document == null ? 0 : document.PageCount;
            long requiredHeight = (long)OuterVerticalPadding * 2L +
                (long)pageCount * itemHeight;
            int virtualHeight = requiredHeight > int.MaxValue
                ? int.MaxValue
                : (int)requiredHeight;
            AutoScrollMinSize = new Size(0, virtualHeight);
        }

        private void AddToCache(int pageIndex, Image image, Size renderSize)
        {
            ThumbnailCacheEntry existingEntry;
            if (thumbnailCache.TryGetValue(pageIndex, out existingEntry))
            {
                existingEntry.Image.Dispose();
            }

            thumbnailCache[pageIndex] = new ThumbnailCacheEntry
            {
                Image = image,
                RenderSize = renderSize,
                LastAccess = ++cacheAccessCounter
            };
            TrimCache();
        }

        private void TrimCache()
        {
            while (thumbnailCache.Count > cacheCapacity)
            {
                KeyValuePair<int, ThumbnailCacheEntry>? oldest = null;
                foreach (KeyValuePair<int, ThumbnailCacheEntry> pair in thumbnailCache)
                {
                    if (pair.Key == selectedPage ||
                        pair.Key == activePage)
                    {
                        continue;
                    }

                    if (!oldest.HasValue ||
                        pair.Value.LastAccess < oldest.Value.Value.LastAccess)
                    {
                        oldest = pair;
                    }
                }

                if (!oldest.HasValue)
                {
                    break;
                }

                ThumbnailCacheEntry entry = oldest.Value.Value;
                thumbnailCache.Remove(oldest.Value.Key);
                entry.Image.Dispose();
            }
        }

        private void ClearThumbnailCache()
        {
            foreach (ThumbnailCacheEntry cacheEntry in thumbnailCache.Values)
            {
                cacheEntry.Image.Dispose();
            }

            thumbnailCache.Clear();
            failedPages.Clear();
            renderQueue.Clear();
            cacheAccessCounter = 0L;
        }

        private void ReleaseCurrentDocument()
        {
            ClearDragInsertion();
            renderTimer.Stop();
            renderQueue.Clear();
            ClearThumbnailCache();
            selectedPage = -1;
            activePage = -1;
            selectedPages.Clear();
            selectionAnchorPage = -1;
            documentGeneration++;
            isRendering = false;

            PdfiumDocument previousDocument = document;
            bool shouldDispose = ownsDocument;
            document = null;
            ownsDocument = false;

            if (shouldDispose && previousDocument != null)
            {
                previousDocument.Dispose();
            }
        }

        private static Font CreateArchitecturalFont(float size, bool condensed)
        {
            string[] preferredFamilies = condensed
                ? new[]
                {
                    "Bahnschrift Light SemiCondensed",
                    "Bahnschrift SemiLight SemiConde",
                    "Bahnschrift SemiCondensed",
                    "Bahnschrift Light Condensed",
                    "Bahnschrift Condensed",
                    "Bahnschrift Light",
                    "Bahnschrift SemiLight",
                    "Bahnschrift",
                    "Segoe UI Semilight",
                    "Segoe UI"
                }
                : new[]
                {
                    "Bahnschrift Light",
                    "Bahnschrift SemiLight",
                    "Bahnschrift",
                    "Segoe UI Semilight",
                    "Segoe UI"
                };

            foreach (string familyName in preferredFamilies)
            {
                try
                {
                    Font font = new Font(
                        familyName,
                        size,
                        FontStyle.Regular,
                        GraphicsUnit.Point);
                    if (string.Equals(
                        font.Name,
                        familyName,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return font;
                    }

                    font.Dispose();
                }
                catch
                {
                    // Try the next installed system family.
                }
            }

            return new Font(
                SystemFonts.MessageBoxFont.FontFamily,
                size,
                FontStyle.Regular,
                GraphicsUnit.Point);
        }

        private static string FormatPageNumber(int pageNumber)
        {
            return pageNumber < 100
                ? pageNumber.ToString("00")
                : pageNumber.ToString();
        }

        private sealed class ThumbnailCacheEntry
        {
            public Image Image { get; set; }

            public Size RenderSize { get; set; }

            public long LastAccess { get; set; }
        }
    }

    internal sealed class PdfFilesInsertRequestedEventArgs : EventArgs
    {
        public PdfFilesInsertRequestedEventArgs(
            IEnumerable<string> pdfFilePaths,
            int insertionPageIndex)
        {
            if (pdfFilePaths == null)
            {
                throw new ArgumentNullException("pdfFilePaths");
            }

            var paths = new List<string>(pdfFilePaths);
            if (paths.Count == 0)
            {
                throw new ArgumentException(
                    "Debe indicarse al menos un archivo PDF.",
                    "pdfFilePaths");
            }

            PdfFilePaths = new ReadOnlyCollection<string>(paths);
            InsertionPageIndex = Math.Max(0, insertionPageIndex);
        }

        public IList<string> PdfFilePaths { get; private set; }

        public int InsertionPageIndex { get; private set; }
    }

    internal sealed class PdfThumbnailPageSelectedEventArgs : EventArgs
    {
        public PdfThumbnailPageSelectedEventArgs(int pageIndex)
        {
            PageIndex = pageIndex;
        }

        public int PageIndex { get; private set; }

        public int PageNumber
        {
            get { return PageIndex + 1; }
        }
    }

    internal enum PdfThumbnailPageOperation
    {
        Delete,
        RotateLeft,
        RotateRight
    }

    internal sealed class PdfThumbnailPagesReorderRequestedEventArgs :
        EventArgs
    {
        public PdfThumbnailPagesReorderRequestedEventArgs(
            IEnumerable<int> pageIndexes,
            int insertionPageIndex)
        {
            if (pageIndexes == null)
            {
                throw new ArgumentNullException("pageIndexes");
            }

            PageIndexes = new ReadOnlyCollection<int>(
                pageIndexes
                    .Distinct()
                    .OrderBy(index => index)
                    .ToList());
            InsertionPageIndex = Math.Max(0, insertionPageIndex);
        }

        public IList<int> PageIndexes { get; private set; }

        public int InsertionPageIndex { get; private set; }
    }

    internal sealed class PdfThumbnailPageOperationRequestedEventArgs :
        EventArgs
    {
        public PdfThumbnailPageOperationRequestedEventArgs(
            PdfThumbnailPageOperation operation,
            IEnumerable<int> pageIndexes,
            int activePageIndex)
        {
            if (pageIndexes == null)
            {
                throw new ArgumentNullException("pageIndexes");
            }

            Operation = operation;
            PageIndexes = new ReadOnlyCollection<int>(
                pageIndexes
                    .Distinct()
                    .OrderBy(index => index)
                    .ToList());
            ActivePageIndex = activePageIndex;
        }

        public PdfThumbnailPageOperation Operation { get; private set; }

        public IList<int> PageIndexes { get; private set; }

        public int ActivePageIndex { get; private set; }
    }

    internal sealed class PdfThumbnailInternalDragData
    {
        public PdfThumbnailInternalDragData(
            PdfThumbnailList source,
            int documentGeneration,
            IEnumerable<int> pageIndexes)
        {
            Source = source;
            DocumentGeneration = documentGeneration;
            PageIndexes = new ReadOnlyCollection<int>(
                (pageIndexes ?? Enumerable.Empty<int>())
                    .Distinct()
                    .OrderBy(index => index)
                    .ToList());
        }

        public PdfThumbnailList Source { get; private set; }

        public int DocumentGeneration { get; private set; }

        public IList<int> PageIndexes { get; private set; }
    }
}

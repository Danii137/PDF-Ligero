using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using PdfiumViewer;

namespace FirmaAutomatica
{
    /// <summary>
    /// Lightweight, opt-in rectangle zoom gesture for PdfiumViewer.
    ///
    /// PdfiumViewer uses the left mouse button to pan. An IMessageFilter lets
    /// us take over only mouse moves that belong to a rectangle gesture while
    /// leaving ordinary clicks (including PDF links), the wheel and scrollbars
    /// untouched.
    /// </summary>
    internal sealed class PdfRectangleZoomController :
        IMessageFilter,
        IDisposable
    {
        private const int WmKeyDown = 0x0100;
        private const int WmLButtonDown = 0x0201;
        private const int WmMouseMove = 0x0200;
        private const int WmLButtonUp = 0x0202;
        private const int MinimumSelectionSide = 14;
        private const int SelectionViewportPadding = 42;

        private readonly PdfRenderer renderer;
        private readonly Func<bool> canStartGesture;
        private readonly RectangleZoomMarker marker;
        private readonly Button acceptButton;
        private readonly ToolTip toolTip;

        private bool disposed;
        private bool candidate;
        private bool dragging;
        private bool confirmClick;
        private bool adjustingViewport;
        private bool viewportRefreshPending;
        private Point dragStart;
        private Rectangle pageClientBounds;
        private PdfRectangle? selection;
        private int viewerPageAtSelection = -1;

        public PdfRectangleZoomController(
            PdfRenderer renderer,
            Func<bool> canStartGesture,
            Color accentColor,
            Color accentTextColor,
            Color surfaceColor)
        {
            if (renderer == null)
            {
                throw new ArgumentNullException("renderer");
            }

            this.renderer = renderer;
            this.canStartGesture = canStartGesture;
            marker = new RectangleZoomMarker(accentColor);

            acceptButton = new Button
            {
                Width = 32,
                Height = 32,
                FlatStyle = FlatStyle.Flat,
                BackColor = surfaceColor,
                ForeColor = accentTextColor,
                Text = "\u2315",
                TextAlign = ContentAlignment.MiddleCenter,
                Font = CreateButtonFont(),
                Cursor = Cursors.Hand,
                TabStop = false,
                Visible = false,
                AccessibleName = "Ampliar la zona seleccionada",
                AccessibleDescription =
                    "Ajusta el zoom para encuadrar el rectángulo seleccionado."
            };
            acceptButton.FlatAppearance.BorderColor = accentColor;
            acceptButton.FlatAppearance.BorderSize = 1;
            acceptButton.FlatAppearance.MouseOverBackColor =
                Color.FromArgb(251, 236, 231);
            acceptButton.FlatAppearance.MouseDownBackColor =
                Color.FromArgb(244, 218, 209);
            acceptButton.Click += AcceptButton_Click;

            toolTip = new ToolTip
            {
                AutoPopDelay = 4200,
                InitialDelay = 250,
                ReshowDelay = 80,
                ShowAlways = true
            };
            toolTip.SetToolTip(
                acceptButton,
                "Ampliar y encuadrar esta zona");

            renderer.Controls.Add(acceptButton);
            acceptButton.BringToFront();

            renderer.Scroll += Renderer_ViewportChanged;
            renderer.ZoomChanged += Renderer_ViewportChanged;
            renderer.SizeChanged += Renderer_ViewportChanged;
            renderer.Disposed += Renderer_Disposed;
            Application.AddMessageFilter(this);
        }

        internal bool HasSelection
        {
            get { return selection.HasValue && selection.Value.IsValid; }
        }

        internal bool IsDragging
        {
            get { return dragging; }
        }

        internal PdfRectangle? Selection
        {
            get { return selection; }
        }

        internal Button AcceptButton
        {
            get { return acceptButton; }
        }

        internal void NotifyActivePage(int pageIndex)
        {
            if (HasSelection &&
                viewerPageAtSelection >= 0 &&
                viewerPageAtSelection != pageIndex)
            {
                Cancel();
            }
        }

        public bool PreFilterMessage(ref Message message)
        {
            if (disposed)
            {
                return false;
            }

            if (message.Msg == WmKeyDown &&
                (int)message.WParam == (int)Keys.Escape &&
                (candidate || HasSelection))
            {
                Cancel();
                return true;
            }

            if (message.Msg == WmLButtonDown &&
                HasSelection &&
                message.HWnd != renderer.Handle)
            {
                var clickedControl = Control.FromHandle(message.HWnd);
                if (clickedControl != acceptButton &&
                    (clickedControl == null ||
                     !acceptButton.Contains(clickedControl)))
                {
                    Cancel();
                }

                return false;
            }

            if (candidate && message.HWnd != renderer.Handle)
            {
                var capturedLocation =
                    renderer.PointToClient(Cursor.Position);
                if (message.Msg == WmMouseMove)
                {
                    return HandleMouseMove(capturedLocation);
                }

                if (message.Msg == WmLButtonUp)
                {
                    return HandleMouseUp(capturedLocation);
                }
            }

            if (!renderer.IsHandleCreated ||
                message.HWnd != renderer.Handle)
            {
                return false;
            }

            var location = renderer.PointToClient(Cursor.Position);
            switch (message.Msg)
            {
                case WmLButtonDown:
                    return HandleMouseDown(location);

                case WmMouseMove:
                    return HandleMouseMove(location);

                case WmLButtonUp:
                    return HandleMouseUp(location);

                default:
                    return false;
            }
        }

        internal bool BeginSelection(Point location)
        {
            return BeginCandidate(location);
        }

        internal bool UpdateSelection(Point location)
        {
            if (!candidate || confirmClick)
            {
                return false;
            }

            var dx = Math.Abs(location.X - dragStart.X);
            var dy = Math.Abs(location.Y - dragStart.Y);
            var threshold = Math.Max(
                6,
                Math.Max(
                    SystemInformation.DragSize.Width,
                    SystemInformation.DragSize.Height));
            if (!dragging && Math.Max(dx, dy) < threshold)
            {
                return false;
            }

            dragging = true;
            var clientBounds = NormalizeRectangle(dragStart, location);
            clientBounds.Intersect(pageClientBounds);
            if (clientBounds.Width < 1 || clientBounds.Height < 1)
            {
                return false;
            }

            var pdfBounds = renderer.BoundsToPdf(clientBounds);
            if (!pdfBounds.IsValid)
            {
                return false;
            }

            selection = pdfBounds;
            marker.Selection = selection;
            EnsureMarkerAttached();
            acceptButton.Visible = false;
            renderer.Invalidate();
            return true;
        }

        internal bool CompleteSelection(Point location)
        {
            if (!candidate || confirmClick)
            {
                ResetPointerGesture();
                return false;
            }

            UpdateSelection(location);
            var completed =
                dragging &&
                HasSelection &&
                GetSelectionClientBounds().Width >= MinimumSelectionSide &&
                GetSelectionClientBounds().Height >= MinimumSelectionSide;

            ResetPointerGesture();
            if (!completed)
            {
                ClearSelection();
                return false;
            }

            viewerPageAtSelection = renderer.Page;
            EnsureMarkerAttached();
            UpdateAcceptButton();
            renderer.Invalidate();
            return true;
        }

        internal bool ZoomToSelection()
        {
            if (!HasSelection ||
                renderer.Document == null ||
                selection.Value.Page < 0 ||
                selection.Value.Page >= renderer.Document.PageCount)
            {
                ClearSelection();
                return false;
            }

            var selectedClientBounds = GetSelectionClientBounds();
            if (selectedClientBounds.Width < 1 ||
                selectedClientBounds.Height < 1)
            {
                ClearSelection();
                return false;
            }

            // WinForms ClientSize already excludes the native scrollbars used
            // by PdfiumViewer's CustomScrollControl.
            var viewportWidth = renderer.ClientSize.Width;
            var viewportHeight = renderer.ClientSize.Height;
            var targetWidth = Math.Max(
                MinimumSelectionSide,
                viewportWidth - (SelectionViewportPadding * 2));
            var targetHeight = Math.Max(
                MinimumSelectionSide,
                viewportHeight - (SelectionViewportPadding * 2));
            var scale = Math.Min(
                (double)targetWidth / selectedClientBounds.Width,
                (double)targetHeight / selectedClientBounds.Height);

            // A rectangle is an enlargement gesture. Keeping the current zoom
            // for very large rectangles avoids a surprising zoom-out.
            scale = Math.Max(1.0, scale);
            var targetZoom = Math.Min(
                renderer.ZoomMax,
                Math.Max(renderer.ZoomMin, renderer.Zoom * scale));
            var expectedSelection = selection.Value;

            adjustingViewport = true;
            try
            {
                renderer.Zoom = targetZoom;
                renderer.PerformLayout();
            }
            finally
            {
                adjustingViewport = false;
            }

            // PdfiumViewer synchronizes its native scrollbar positions after
            // the zoom event returns. Center on the next UI message so we use
            // the final display rectangle rather than the pre-scrollbar one.
            acceptButton.Visible = false;
            try
            {
                renderer.BeginInvoke(new Action(delegate
                {
                    CompleteZoomToSelection(expectedSelection);
                }));
            }
            catch (InvalidOperationException)
            {
                CompleteZoomToSelection(expectedSelection);
            }

            return true;
        }

        internal void Cancel()
        {
            if (disposed)
            {
                return;
            }

            if (renderer.Capture)
            {
                renderer.Capture = false;
            }

            ResetPointerGesture();
            ClearSelection();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Application.RemoveMessageFilter(this);

            renderer.Scroll -= Renderer_ViewportChanged;
            renderer.ZoomChanged -= Renderer_ViewportChanged;
            renderer.SizeChanged -= Renderer_ViewportChanged;
            renderer.Disposed -= Renderer_Disposed;

            if (!renderer.IsDisposed)
            {
                if (renderer.Markers.Contains(marker))
                {
                    renderer.Markers.Remove(marker);
                }

                renderer.Controls.Remove(acceptButton);
            }

            acceptButton.Click -= AcceptButton_Click;
            toolTip.SetToolTip(acceptButton, null);
            toolTip.Dispose();
            acceptButton.Dispose();
        }

        private bool HandleMouseDown(Point location)
        {
            if (!CanUseGesture())
            {
                Cancel();
                return false;
            }

            if (HasSelection)
            {
                var selectedClientBounds = GetSelectionClientBounds();
                if (selectedClientBounds.Contains(location))
                {
                    candidate = true;
                    confirmClick = true;
                    dragging = false;
                    dragStart = location;
                    renderer.Capture = true;
                    return true;
                }

                ClearSelection();
            }

            BeginCandidate(location);
            return false;
        }

        private bool HandleMouseMove(Point location)
        {
            if (!candidate)
            {
                return false;
            }

            // Consume the whole potential drag, even before the threshold, so
            // PdfiumViewer cannot pan the page a few pixels underneath it.
            if (!confirmClick)
            {
                UpdateSelection(location);
            }

            return true;
        }

        private bool HandleMouseUp(Point location)
        {
            if (!candidate)
            {
                return false;
            }

            if (confirmClick)
            {
                var selectedClientBounds = GetSelectionClientBounds();
                var isClick =
                    Math.Abs(location.X - dragStart.X) <=
                        SystemInformation.DragSize.Width &&
                    Math.Abs(location.Y - dragStart.Y) <=
                        SystemInformation.DragSize.Height &&
                    selectedClientBounds.Contains(location);
                renderer.Capture = false;
                ResetPointerGesture();
                if (isClick)
                {
                    ZoomToSelection();
                }

                return true;
            }

            if (!dragging)
            {
                // Let PdfiumViewer finish an ordinary click. This preserves
                // links and its normal click behaviour.
                ResetPointerGesture();
                return false;
            }

            renderer.Capture = false;
            CompleteSelection(location);
            return true;
        }

        private bool BeginCandidate(Point location)
        {
            if (!CanUseGesture() ||
                Control.ModifierKeys != Keys.None ||
                !renderer.ClientRectangle.Contains(location))
            {
                ResetPointerGesture();
                return false;
            }

            var pdfPoint = renderer.PointToPdf(location);
            if (!pdfPoint.IsValid)
            {
                ResetPointerGesture();
                return false;
            }

            var pageSize = renderer.Document.PageSizes[pdfPoint.Page];
            pageClientBounds = renderer.BoundsFromPdf(
                new PdfRectangle(
                    pdfPoint.Page,
                    new RectangleF(
                        0f,
                        0f,
                        pageSize.Width,
                        pageSize.Height)));
            pageClientBounds.Intersect(renderer.ClientRectangle);
            if (pageClientBounds.Width < MinimumSelectionSide ||
                pageClientBounds.Height < MinimumSelectionSide)
            {
                ResetPointerGesture();
                return false;
            }

            dragStart = ClampPoint(location, pageClientBounds);
            candidate = true;
            dragging = false;
            confirmClick = false;
            return true;
        }

        private bool CanUseGesture()
        {
            return !disposed &&
                renderer.Enabled &&
                renderer.Visible &&
                renderer.Document != null &&
                (canStartGesture == null || canStartGesture());
        }

        private void Renderer_ViewportChanged(object sender, EventArgs e)
        {
            if (disposed || adjustingViewport || !HasSelection)
            {
                return;
            }

            if (renderer.Document == null ||
                (viewerPageAtSelection >= 0 &&
                 renderer.Page != viewerPageAtSelection))
            {
                Cancel();
                return;
            }

            EnsureMarkerAttached();
            UpdateAcceptButton();
        }

        private void CompleteZoomToSelection(PdfRectangle expectedSelection)
        {
            if (disposed ||
                !HasSelection ||
                !selection.Value.Equals(expectedSelection) ||
                renderer.Document == null)
            {
                return;
            }

            adjustingViewport = true;
            try
            {
                // Re-evaluate a few times because changing one scrollbar can
                // alter the other viewport dimension.
                for (var pass = 0; pass < 4; pass++)
                {
                    var viewportWidth = renderer.ClientSize.Width;
                    var viewportHeight = renderer.ClientSize.Height;
                    var selectedClientBounds = GetSelectionClientBounds();
                    var viewportCenter = new Point(
                        viewportWidth / 2,
                        viewportHeight / 2);
                    var selectionCenter = new Point(
                        selectedClientBounds.Left +
                            (selectedClientBounds.Width / 2),
                        selectedClientBounds.Top +
                            (selectedClientBounds.Height / 2));
                    var correction = new Point(
                        viewportCenter.X - selectionCenter.X,
                        viewportCenter.Y - selectionCenter.Y);
                    if (Math.Abs(correction.X) <= 1 &&
                        Math.Abs(correction.Y) <= 1)
                    {
                        break;
                    }

                    var displayLocation =
                        renderer.DisplayRectangle.Location;
                    renderer.SetDisplayRectLocation(
                        new Point(
                            displayLocation.X + correction.X,
                            displayLocation.Y + correction.Y),
                        false);
                }
            }
            finally
            {
                adjustingViewport = false;
            }

            ClearSelection();
            renderer.Focus();
        }

        private void Renderer_ViewportChanged(
            object sender,
            ScrollEventArgs e)
        {
            Renderer_ViewportChanged(sender, EventArgs.Empty);
            if (disposed || viewportRefreshPending || !HasSelection)
            {
                return;
            }

            // CustomScrollControl raises Scroll while its display rectangle is
            // still being synchronized. Verify the active page once more on
            // the next UI message.
            viewportRefreshPending = true;
            try
            {
                renderer.BeginInvoke(new Action(delegate
                {
                    viewportRefreshPending = false;
                    Renderer_ViewportChanged(renderer, EventArgs.Empty);
                }));
            }
            catch (InvalidOperationException)
            {
                viewportRefreshPending = false;
            }
        }

        private void Renderer_Disposed(object sender, EventArgs e)
        {
            Dispose();
        }

        private void AcceptButton_Click(object sender, EventArgs e)
        {
            ZoomToSelection();
        }

        private void EnsureMarkerAttached()
        {
            if (HasSelection && !renderer.Markers.Contains(marker))
            {
                renderer.Markers.Add(marker);
            }
        }

        private void ClearSelection()
        {
            selection = null;
            viewerPageAtSelection = -1;
            marker.Selection = null;
            acceptButton.Visible = false;
            if (!renderer.IsDisposed && renderer.Markers.Contains(marker))
            {
                renderer.Markers.Remove(marker);
            }

            if (!renderer.IsDisposed)
            {
                renderer.Invalidate();
            }
        }

        private void ResetPointerGesture()
        {
            candidate = false;
            dragging = false;
            confirmClick = false;
            pageClientBounds = Rectangle.Empty;
        }

        private Rectangle GetSelectionClientBounds()
        {
            if (!HasSelection)
            {
                return Rectangle.Empty;
            }

            return renderer.BoundsFromPdf(selection.Value);
        }

        private void UpdateAcceptButton()
        {
            var bounds = GetSelectionClientBounds();
            if (bounds.Width < MinimumSelectionSide ||
                bounds.Height < MinimumSelectionSide ||
                !bounds.IntersectsWith(renderer.ClientRectangle))
            {
                acceptButton.Visible = false;
                return;
            }

            acceptButton.Left =
                bounds.Left + ((bounds.Width - acceptButton.Width) / 2);
            acceptButton.Top =
                bounds.Top + ((bounds.Height - acceptButton.Height) / 2);
            acceptButton.Visible = true;
            acceptButton.BringToFront();
        }

        private static Rectangle NormalizeRectangle(Point first, Point second)
        {
            return Rectangle.FromLTRB(
                Math.Min(first.X, second.X),
                Math.Min(first.Y, second.Y),
                Math.Max(first.X, second.X),
                Math.Max(first.Y, second.Y));
        }

        private static Point ClampPoint(Point value, Rectangle bounds)
        {
            return new Point(
                Math.Max(bounds.Left, Math.Min(bounds.Right - 1, value.X)),
                Math.Max(bounds.Top, Math.Min(bounds.Bottom - 1, value.Y)));
        }

        private static Font CreateButtonFont()
        {
            try
            {
                return new Font(
                    "Segoe UI Symbol",
                    15f,
                    FontStyle.Regular,
                    GraphicsUnit.Point);
            }
            catch
            {
                return new Font(
                    SystemFonts.MessageBoxFont.FontFamily,
                    13f,
                    FontStyle.Regular,
                    GraphicsUnit.Point);
            }
        }

        private sealed class RectangleZoomMarker : IPdfMarker
        {
            private readonly Color accentColor;

            public RectangleZoomMarker(Color accentColor)
            {
                this.accentColor = accentColor;
            }

            public PdfRectangle? Selection { get; set; }

            public int Page
            {
                get
                {
                    return !Selection.HasValue ||
                        !Selection.Value.IsValid
                        ? -1
                        : Selection.Value.Page;
                }
            }

            public void Draw(PdfRenderer pdfRenderer, Graphics graphics)
            {
                if (!Selection.HasValue || !Selection.Value.IsValid)
                {
                    return;
                }

                var bounds = pdfRenderer.BoundsFromPdf(Selection.Value);
                if (bounds.Width < 1 || bounds.Height < 1)
                {
                    return;
                }

                using (var fill = new SolidBrush(
                    Color.FromArgb(
                        24,
                        accentColor.R,
                        accentColor.G,
                        accentColor.B)))
                using (var outline = new Pen(accentColor, 1.35f))
                {
                    outline.DashStyle = DashStyle.Dash;
                    outline.DashPattern = new[] { 5f, 3f };
                    graphics.FillRectangle(fill, bounds);
                    graphics.DrawRectangle(
                        outline,
                        bounds.Left,
                        bounds.Top,
                        Math.Max(0, bounds.Width - 1),
                        Math.Max(0, bounds.Height - 1));
                }

                DrawCornerTicks(graphics, bounds, accentColor);
            }

            private static void DrawCornerTicks(
                Graphics graphics,
                Rectangle bounds,
                Color color)
            {
                const int tick = 10;
                using (var pen = new Pen(color, 2f))
                {
                    graphics.DrawLine(
                        pen,
                        bounds.Left,
                        bounds.Top,
                        bounds.Left + tick,
                        bounds.Top);
                    graphics.DrawLine(
                        pen,
                        bounds.Left,
                        bounds.Top,
                        bounds.Left,
                        bounds.Top + tick);
                    graphics.DrawLine(
                        pen,
                        bounds.Right - tick,
                        bounds.Top,
                        bounds.Right,
                        bounds.Top);
                    graphics.DrawLine(
                        pen,
                        bounds.Right,
                        bounds.Top,
                        bounds.Right,
                        bounds.Top + tick);
                    graphics.DrawLine(
                        pen,
                        bounds.Left,
                        bounds.Bottom,
                        bounds.Left + tick,
                        bounds.Bottom);
                    graphics.DrawLine(
                        pen,
                        bounds.Left,
                        bounds.Bottom - tick,
                        bounds.Left,
                        bounds.Bottom);
                    graphics.DrawLine(
                        pen,
                        bounds.Right - tick,
                        bounds.Bottom,
                        bounds.Right,
                        bounds.Bottom);
                    graphics.DrawLine(
                        pen,
                        bounds.Right,
                        bounds.Bottom - tick,
                        bounds.Right,
                        bounds.Bottom);
                }
            }
        }
    }
}

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using PdfiumViewer;

namespace FirmaAutomatica
{
    /// <summary>
    /// Opt-in rectangle selector used by controlled text editing.
    ///
    /// The controller is intentionally independent from the edit engine. It
    /// only returns Pdfium page coordinates; the caller decides how to inspect
    /// the selected text, show an editor and create a new PDF revision.
    /// </summary>
    internal sealed class PdfTextEditSelectionController :
        IMessageFilter,
        IDisposable
    {
        private const int WmKeyDown = 0x0100;
        private const int WmLButtonDown = 0x0201;
        private const int WmMouseMove = 0x0200;
        private const int WmLButtonUp = 0x0202;
        private const int MinimumSelectionSide = 12;

        private readonly PdfRenderer renderer;
        private readonly Func<bool> canActivate;
        private readonly TextEditSelectionMarker marker;
        private readonly Button acceptButton;
        private readonly ToolTip toolTip;

        private bool disposed;
        private bool isActive;
        private bool candidate;
        private bool dragging;
        private Point dragStart;
        private Rectangle pageClientBounds;
        private int gesturePage = -1;
        private int selectedPage = -1;
        private PdfRectangle? selection;
        private Cursor cursorBeforeSelection;

        public PdfTextEditSelectionController(
            PdfRenderer renderer,
            Func<bool> canActivate,
            Color accentColor,
            Color accentTextColor,
            Color surfaceColor)
        {
            if (renderer == null)
            {
                throw new ArgumentNullException("renderer");
            }

            this.renderer = renderer;
            this.canActivate = canActivate;
            marker = new TextEditSelectionMarker(accentColor);

            acceptButton = new Button
            {
                Width = 34,
                Height = 34,
                FlatStyle = FlatStyle.Flat,
                BackColor = surfaceColor,
                ForeColor = accentTextColor,
                Text = "T",
                TextAlign = ContentAlignment.MiddleCenter,
                Font = CreateArchitecturalFont(12f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                TabStop = false,
                Visible = false,
                AccessibleName = "Editar el texto de la zona seleccionada",
                AccessibleDescription =
                    "Abre el editor para cubrir o sustituir el texto de esta zona."
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
                AutoPopDelay = 5000,
                InitialDelay = 250,
                ReshowDelay = 80,
                ShowAlways = true
            };
            toolTip.SetToolTip(
                acceptButton,
                "Editar o añadir texto en esta zona");

            renderer.Controls.Add(acceptButton);
            acceptButton.BringToFront();

            renderer.Scroll += Renderer_ViewportChanged;
            renderer.ZoomChanged += Renderer_ViewportChanged;
            renderer.SizeChanged += Renderer_ViewportChanged;
            renderer.SetCursor += Renderer_SetCursor;
            renderer.MouseMove += Renderer_MouseMove;
            renderer.Disposed += Renderer_Disposed;
            Application.AddMessageFilter(this);
        }

        public event EventHandler ActiveStateChanged;

        public event EventHandler SelectionChanged;

        public event EventHandler<PdfTextEditSelectionEventArgs>
            SelectionAccepted;

        public event EventHandler Cancelled;

        public bool IsActive
        {
            get { return isActive; }
        }

        public bool HasSelection
        {
            get
            {
                return selection.HasValue &&
                    selection.Value.IsValid;
            }
        }

        public PdfRectangle? Selection
        {
            get { return selection; }
        }

        internal Button AcceptButton
        {
            get { return acceptButton; }
        }

        internal bool IsDragging
        {
            get { return dragging; }
        }

        internal bool IsPrecisionCursorApplied
        {
            get
            {
                return !renderer.IsDisposed &&
                    Cursor.Current == Cursors.Cross;
            }
        }

        public bool Activate()
        {
            if (disposed)
            {
                return false;
            }

            if (isActive)
            {
                ApplyPrecisionCursor();
                return true;
            }

            if (renderer.Document == null ||
                (canActivate != null && !canActivate()))
            {
                return false;
            }

            cursorBeforeSelection = Cursor.Current;
            isActive = true;
            ClearSelection(false);
            OnActiveStateChanged();
            renderer.Focus();
            ApplyPrecisionCursor();
            return true;
        }

        public void Deactivate()
        {
            Deactivate(false);
        }

        public void Cancel()
        {
            Deactivate(true);
        }

        internal void ClearCurrentSelection()
        {
            if (!disposed)
            {
                ClearSelection(true);
            }
        }

        internal void NotifyActivePage(int pageIndex)
        {
            if (HasSelection &&
                selectedPage >= 0 &&
                selectedPage != pageIndex)
            {
                ClearSelection(true);
            }
        }

        public bool PreFilterMessage(ref Message message)
        {
            if (disposed || !isActive)
            {
                return false;
            }

            if (message.Msg == WmKeyDown &&
                (int)message.WParam == (int)Keys.Escape)
            {
                Cancel();
                return true;
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
            if (!candidate)
            {
                return false;
            }

            var dx = Math.Abs(location.X - dragStart.X);
            var dy = Math.Abs(location.Y - dragStart.Y);
            var threshold = Math.Max(
                5,
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
            if (!pdfBounds.IsValid || pdfBounds.Page != gesturePage)
            {
                return false;
            }

            selection = pdfBounds;
            selectedPage = pdfBounds.Page;
            marker.Selection = selection;
            EnsureMarkerAttached();
            acceptButton.Visible = false;
            renderer.Invalidate();
            OnSelectionChanged();
            return true;
        }

        internal bool CompleteSelection(Point location)
        {
            if (!candidate)
            {
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
                ClearSelection(true);
                return false;
            }

            EnsureMarkerAttached();
            UpdateAcceptButton();
            renderer.Invalidate();
            return true;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            Deactivate(false);
            disposed = true;
            Application.RemoveMessageFilter(this);

            renderer.Scroll -= Renderer_ViewportChanged;
            renderer.ZoomChanged -= Renderer_ViewportChanged;
            renderer.SizeChanged -= Renderer_ViewportChanged;
            renderer.SetCursor -= Renderer_SetCursor;
            renderer.MouseMove -= Renderer_MouseMove;
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

        private void Deactivate(bool notifyCancellation)
        {
            if (disposed || !isActive)
            {
                return;
            }

            if (renderer.Capture)
            {
                renderer.Capture = false;
            }

            isActive = false;
            ResetPointerGesture();
            ClearSelection(true);
            RestoreRendererCursor();
            OnActiveStateChanged();

            if (notifyCancellation)
            {
                var handler = Cancelled;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            }
        }

        private bool HandleMouseDown(Point location)
        {
            if (!CanCaptureSelection())
            {
                Cancel();
                return false;
            }

            if (HasSelection)
            {
                ClearSelection(true);
            }

            return BeginCandidate(location);
        }

        private bool HandleMouseMove(Point location)
        {
            if (!candidate)
            {
                return false;
            }

            UpdateSelection(location);
            return true;
        }

        private bool HandleMouseUp(Point location)
        {
            if (!candidate)
            {
                return false;
            }

            if (renderer.Capture)
            {
                renderer.Capture = false;
            }

            CompleteSelection(location);
            return true;
        }

        private bool BeginCandidate(Point location)
        {
            if (!CanCaptureSelection() ||
                Control.ModifierKeys != Keys.None ||
                !renderer.ClientRectangle.Contains(location))
            {
                ResetPointerGesture();
                return false;
            }

            var pdfPoint = renderer.PointToPdf(location);
            if (!pdfPoint.IsValid ||
                pdfPoint.Page < 0 ||
                pdfPoint.Page >= renderer.Document.PageCount)
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
            gesturePage = pdfPoint.Page;
            candidate = true;
            dragging = false;
            renderer.Capture = true;
            return true;
        }

        private bool CanCaptureSelection()
        {
            return !disposed &&
                isActive &&
                renderer.Enabled &&
                renderer.Visible &&
                renderer.Document != null &&
                (canActivate == null || canActivate());
        }

        private void Renderer_ViewportChanged(
            object sender,
            EventArgs e)
        {
            if (disposed || !isActive || !HasSelection)
            {
                return;
            }

            if (renderer.Document == null ||
                selectedPage < 0 ||
                selectedPage >= renderer.Document.PageCount)
            {
                ClearSelection(true);
                return;
            }

            EnsureMarkerAttached();
            UpdateAcceptButton();
        }

        private void Renderer_SetCursor(
            object sender,
            SetCursorEventArgs e)
        {
            if (!disposed &&
                isActive &&
                e != null &&
                e.HitTest == HitTest.Client &&
                (!acceptButton.Visible ||
                 !acceptButton.Bounds.Contains(e.Location)))
            {
                e.Cursor = Cursors.Cross;
            }
        }

        private void Renderer_MouseMove(
            object sender,
            MouseEventArgs e)
        {
            if (!disposed &&
                isActive &&
                e != null &&
                (!acceptButton.Visible ||
                 !acceptButton.Bounds.Contains(e.Location)))
            {
                Cursor.Current = Cursors.Cross;
            }
        }

        private void Renderer_Disposed(object sender, EventArgs e)
        {
            Dispose();
        }

        private void AcceptButton_Click(object sender, EventArgs e)
        {
            if (!isActive || !HasSelection)
            {
                return;
            }

            var accepted = selection.Value;
            var handler = SelectionAccepted;
            if (handler != null)
            {
                handler(
                    this,
                    new PdfTextEditSelectionEventArgs(accepted));
            }

            if (!disposed && isActive && HasSelection)
            {
                UpdateAcceptButton();
                ApplyPrecisionCursor();
            }
        }

        private void ApplyPrecisionCursor()
        {
            if (disposed || !isActive || renderer.IsDisposed)
            {
                return;
            }

            var location = renderer.PointToClient(Cursor.Position);
            if (renderer.ClientRectangle.Contains(location) &&
                (!acceptButton.Visible ||
                 !acceptButton.Bounds.Contains(location)))
            {
                Cursor.Current = Cursors.Cross;
            }
        }

        private void RestoreRendererCursor()
        {
            if (Cursor.Current == Cursors.Cross)
            {
                Cursor.Current = cursorBeforeSelection ?? Cursors.Default;
            }

            cursorBeforeSelection = null;
        }

        private void EnsureMarkerAttached()
        {
            if (HasSelection && !renderer.Markers.Contains(marker))
            {
                renderer.Markers.Add(marker);
            }
        }

        private void ClearSelection(bool notify)
        {
            var hadSelection = HasSelection;
            selection = null;
            selectedPage = -1;
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

            if (notify && hadSelection)
            {
                OnSelectionChanged();
            }
        }

        private void ResetPointerGesture()
        {
            candidate = false;
            dragging = false;
            gesturePage = -1;
            pageClientBounds = Rectangle.Empty;
        }

        private Rectangle GetSelectionClientBounds()
        {
            return HasSelection
                ? renderer.BoundsFromPdf(selection.Value)
                : Rectangle.Empty;
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

            var preferredLeft =
                bounds.Left + ((bounds.Width - acceptButton.Width) / 2);
            var preferredTop =
                bounds.Top + ((bounds.Height - acceptButton.Height) / 2);
            acceptButton.Left = Math.Max(
                3,
                Math.Min(
                    Math.Max(3, renderer.ClientSize.Width - acceptButton.Width - 3),
                    preferredLeft));
            acceptButton.Top = Math.Max(
                3,
                Math.Min(
                    Math.Max(3, renderer.ClientSize.Height - acceptButton.Height - 3),
                    preferredTop));
            acceptButton.Visible = true;
            acceptButton.BringToFront();
        }

        private void OnActiveStateChanged()
        {
            var handler = ActiveStateChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private void OnSelectionChanged()
        {
            var handler = SelectionChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private static Rectangle NormalizeRectangle(
            Point first,
            Point second)
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

        private static Font CreateArchitecturalFont(
            float size,
            FontStyle style)
        {
            try
            {
                return new Font(
                    "Bahnschrift Light",
                    size,
                    style,
                    GraphicsUnit.Point);
            }
            catch
            {
                return new Font(
                    SystemFonts.MessageBoxFont.FontFamily,
                    size,
                    style,
                    GraphicsUnit.Point);
            }
        }

        private sealed class TextEditSelectionMarker : IPdfMarker
        {
            private readonly Color accentColor;

            public TextEditSelectionMarker(Color accentColor)
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
                        28,
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
                const int tick = 9;
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
                        bounds.Right - 1,
                        bounds.Top,
                        bounds.Right - 1,
                        bounds.Top + tick);
                    graphics.DrawLine(
                        pen,
                        bounds.Left,
                        bounds.Bottom - 1,
                        bounds.Left + tick,
                        bounds.Bottom - 1);
                    graphics.DrawLine(
                        pen,
                        bounds.Left,
                        bounds.Bottom - tick,
                        bounds.Left,
                        bounds.Bottom);
                    graphics.DrawLine(
                        pen,
                        bounds.Right - tick,
                        bounds.Bottom - 1,
                        bounds.Right,
                        bounds.Bottom - 1);
                    graphics.DrawLine(
                        pen,
                        bounds.Right - 1,
                        bounds.Bottom - tick,
                        bounds.Right - 1,
                        bounds.Bottom);
                }
            }
        }
    }

    internal sealed class PdfTextEditSelectionEventArgs : EventArgs
    {
        public PdfTextEditSelectionEventArgs(PdfRectangle selection)
        {
            if (!selection.IsValid)
            {
                throw new ArgumentException(
                    "La selección PDF no es válida.",
                    "selection");
            }

            Selection = selection;
        }

        public PdfRectangle Selection { get; private set; }

        public int PageIndex
        {
            get { return Selection.Page; }
        }

        public RectangleF Bounds
        {
            get { return Selection.Bounds; }
        }
    }
}

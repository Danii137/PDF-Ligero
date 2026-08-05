using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FirmaAutomatica
{
    public sealed class TabCloseRequestedEventArgs : CancelEventArgs
    {
        public TabCloseRequestedEventArgs(TabPage tabPage, int tabIndex)
        {
            if (tabPage == null)
            {
                throw new ArgumentNullException("tabPage");
            }

            TabPage = tabPage;
            TabIndex = tabIndex;
        }

        public TabPage TabPage { get; private set; }

        public int TabIndex { get; private set; }
    }

    public class ClosablePdfTabControl : TabControl
    {
        // Restrained "drawing set" palette: warm paper, graphite and one
        // annotation colour. Keeping these colours opaque also makes owner-draw
        // repainting cheap and predictable on remote/older Windows sessions.
        private static readonly Color SelectedTabColor = Color.FromArgb(252, 251, 248);
        private static readonly Color InactiveTabColor = Color.FromArgb(238, 237, 233);
        private static readonly Color InactiveTabHoverColor = Color.FromArgb(246, 245, 241);
        private static readonly Color BorderColor = Color.FromArgb(207, 205, 198);
        private static readonly Color SelectedTextColor = Color.FromArgb(37, 39, 41);
        private static readonly Color InactiveTextColor = Color.FromArgb(103, 103, 99);
        private static readonly Color AccentColor = Color.FromArgb(238, 91, 61);
        private static readonly Color CloseHoverColor = Color.FromArgb(252, 235, 230);
        private static readonly Color ClosePressedColor = Color.FromArgb(248, 218, 210);

        private readonly Dictionary<TabPage, string> automaticToolTips =
            new Dictionary<TabPage, string>();
        private readonly Font ownedTabFont;

        private int hoveredTabIndex = -1;
        private int hoveredCloseIndex = -1;
        private int pressedCloseIndex = -1;
        private float dpiScale = 1f;
        private bool applyingDpiMetrics;

        public ClosablePdfTabControl()
        {
            Alignment = TabAlignment.Top;
            Appearance = TabAppearance.Normal;
            DrawMode = TabDrawMode.OwnerDrawFixed;
            HotTrack = false;
            Multiline = false;
            ShowToolTips = true;
            SizeMode = TabSizeMode.Fixed;
            DisposeClosedPages = true;
            BackColor = InactiveTabColor;
            // Bahnschrift Light SemiCondensed is the closest built-in Windows
            // equivalent to a restrained DIN drawing-set face. The slightly
            // larger point size compensates for its finer strokes without
            // increasing the tab geometry.
            ownedTabFont = CreateArchitecturalFont(9f);
            Font = ownedTabFont;

            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);

            ApplyDpiMetrics();
        }

        public event EventHandler<TabCloseRequestedEventArgs> TabCloseRequested;

        public event EventHandler<TabCloseRequestedEventArgs> TabClosed;

        public bool DisposeClosedPages { get; set; }

        public bool CloseActiveTab()
        {
            return RequestCloseTab(SelectedIndex);
        }

        protected virtual void OnTabCloseRequested(TabCloseRequestedEventArgs e)
        {
            var handler = TabCloseRequested;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        protected virtual void OnTabClosed(TabCloseRequestedEventArgs e)
        {
            var handler = TabClosed;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            if (applyingDpiMetrics)
            {
                return;
            }

            float detectedScale;
            using (var graphics = CreateGraphics())
            {
                detectedScale = Math.Max(1f, graphics.DpiX / 96f);
            }

            if (Math.Abs(dpiScale - detectedScale) > 0.01f)
            {
                dpiScale = detectedScale;
                ApplyDpiMetrics();
            }
        }

        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);

            var page = e.Control as TabPage;
            if (page == null)
            {
                return;
            }

            page.TextChanged += TabPage_TextChanged;
            EnsureFullNameToolTip(page);
            Invalidate();
        }

        protected override void OnControlRemoved(ControlEventArgs e)
        {
            var page = e.Control as TabPage;
            if (page != null)
            {
                page.TextChanged -= TabPage_TextChanged;
                automaticToolTips.Remove(page);
            }

            ResetMouseState();
            base.OnControlRemoved(e);
            Invalidate();
        }

        protected override void OnSelectedIndexChanged(EventArgs e)
        {
            base.OnSelectedIndexChanged(e);
            Invalidate();
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= TabPages.Count)
            {
                return;
            }

            var tabBounds = GetTabRect(e.Index);
            var selected = e.Index == SelectedIndex;
            var hovered = e.Index == hoveredTabIndex;
            var backgroundColor = selected
                ? SelectedTabColor
                : (hovered ? InactiveTabHoverColor : InactiveTabColor);

            using (var backgroundBrush = new SolidBrush(backgroundColor))
            {
                e.Graphics.FillRectangle(backgroundBrush, tabBounds);
            }

            using (var borderPen = new Pen(BorderColor))
            {
                // A continuous datum line gives the tab strip the character of
                // a technical title block without adding visual weight.
                e.Graphics.DrawLine(
                    borderPen,
                    tabBounds.Left,
                    tabBounds.Bottom - 1,
                    tabBounds.Right - 1,
                    tabBounds.Bottom - 1);
                e.Graphics.DrawLine(
                    borderPen,
                    tabBounds.Right - 1,
                    tabBounds.Top + ScaleMetric(8),
                    tabBounds.Right - 1,
                    tabBounds.Bottom - ScaleMetric(8));
            }

            if (selected)
            {
                using (var accentBrush = new SolidBrush(AccentColor))
                {
                    e.Graphics.FillRectangle(
                        accentBrush,
                        tabBounds.Left + ScaleMetric(9),
                        tabBounds.Top,
                        Math.Max(1, tabBounds.Width - ScaleMetric(18)),
                        ScaleMetric(2));
                }
            }

            var closeBounds = GetCloseRectangle(e.Index);
            var textBounds = Rectangle.FromLTRB(
                tabBounds.Left + ScaleMetric(13),
                tabBounds.Top + ScaleMetric(3),
                closeBounds.Left - ScaleMetric(7),
                tabBounds.Bottom - ScaleMetric(2));

            TextRenderer.DrawText(
                e.Graphics,
                TabPages[e.Index].Text,
                Font,
                textBounds,
                selected ? SelectedTextColor : InactiveTextColor,
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix |
                TextFormatFlags.SingleLine |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.PreserveGraphicsClipping);

            DrawCloseButton(
                e.Graphics,
                closeBounds,
                e.Index == hoveredCloseIndex,
                e.Index == pressedCloseIndex);

            if (selected && Focused && ShowFocusCues)
            {
                var focusBounds = textBounds;
                focusBounds.Inflate(-ScaleMetric(1), -ScaleMetric(6));
                ControlPaint.DrawFocusRectangle(e.Graphics, focusBounds, SelectedTextColor, backgroundColor);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            var nextHoveredTab = GetTabIndexAt(e.Location);
            var nextHoveredClose = GetCloseIndexAt(e.Location);
            if (nextHoveredTab == hoveredTabIndex && nextHoveredClose == hoveredCloseIndex)
            {
                return;
            }

            var previousHoveredTab = hoveredTabIndex;
            var previousHoveredClose = hoveredCloseIndex;
            hoveredTabIndex = nextHoveredTab;
            hoveredCloseIndex = nextHoveredClose;
            Cursor = hoveredCloseIndex >= 0 ? Cursors.Hand : Cursors.Default;

            InvalidateTab(previousHoveredTab);
            InvalidateTab(previousHoveredClose);
            InvalidateTab(hoveredTabIndex);
            InvalidateTab(hoveredCloseIndex);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);

            var previousHoveredTab = hoveredTabIndex;
            var previousHoveredClose = hoveredCloseIndex;
            hoveredTabIndex = -1;
            hoveredCloseIndex = -1;
            Cursor = Cursors.Default;

            InvalidateTab(previousHoveredTab);
            InvalidateTab(previousHoveredClose);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                var closeIndex = GetCloseIndexAt(e.Location);
                if (closeIndex >= 0)
                {
                    pressedCloseIndex = closeIndex;
                    Capture = true;
                    InvalidateTab(closeIndex);
                    return;
                }
            }

            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (pressedCloseIndex >= 0)
            {
                var requestedIndex = pressedCloseIndex;
                var shouldClose =
                    e.Button == MouseButtons.Left &&
                    GetCloseIndexAt(e.Location) == requestedIndex;

                pressedCloseIndex = -1;
                Capture = false;
                InvalidateTab(requestedIndex);

                if (shouldClose)
                {
                    RequestCloseTab(requestedIndex);
                }

                return;
            }

            base.OnMouseUp(e);
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);

            if (Capture || pressedCloseIndex < 0)
            {
                return;
            }

            var previousPressedIndex = pressedCloseIndex;
            pressedCloseIndex = -1;
            InvalidateTab(previousPressedIndex);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (TabPage page in TabPages)
                {
                    page.TextChanged -= TabPage_TextChanged;
                }

                automaticToolTips.Clear();
                ownedTabFont.Dispose();
            }

            base.Dispose(disposing);
        }

        private bool RequestCloseTab(int tabIndex)
        {
            if (tabIndex < 0 || tabIndex >= TabPages.Count)
            {
                return false;
            }

            var page = TabPages[tabIndex];
            var eventArgs = new TabCloseRequestedEventArgs(page, tabIndex);
            OnTabCloseRequested(eventArgs);
            if (eventArgs.Cancel)
            {
                return false;
            }

            if (TabPages.Contains(page))
            {
                TabPages.Remove(page);
                if (DisposeClosedPages)
                {
                    page.Dispose();
                }
            }

            OnTabClosed(eventArgs);
            return true;
        }

        private void DrawCloseButton(
            Graphics graphics,
            Rectangle bounds,
            bool hovered,
            bool pressed)
        {
            if (hovered || pressed)
            {
                using (var hoverBrush = new SolidBrush(pressed ? ClosePressedColor : CloseHoverColor))
                {
                    graphics.FillRectangle(hoverBrush, bounds);
                }

                using (var hoverBorderPen = new Pen(Color.FromArgb(246, 184, 170)))
                {
                    graphics.DrawRectangle(
                        hoverBorderPen,
                        bounds.Left,
                        bounds.Top,
                        Math.Max(0, bounds.Width - 1),
                        Math.Max(0, bounds.Height - 1));
                }
            }

            var previousSmoothingMode = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            try
            {
                var inset = ScaleMetric(6);
                using (var closePen = new Pen(
                    hovered || pressed ? AccentColor : InactiveTextColor,
                    Math.Max(1f, 1.15f * dpiScale)))
                {
                    closePen.StartCap = LineCap.Square;
                    closePen.EndCap = LineCap.Square;
                    graphics.DrawLine(
                        closePen,
                        bounds.Left + inset,
                        bounds.Top + inset,
                        bounds.Right - inset - 1,
                        bounds.Bottom - inset - 1);
                    graphics.DrawLine(
                        closePen,
                        bounds.Right - inset - 1,
                        bounds.Top + inset,
                        bounds.Left + inset,
                        bounds.Bottom - inset - 1);
                }
            }
            finally
            {
                graphics.SmoothingMode = previousSmoothingMode;
            }
        }

        private Rectangle GetCloseRectangle(int tabIndex)
        {
            var tabBounds = GetTabRect(tabIndex);
            var closeSize = ScaleMetric(17);
            return new Rectangle(
                tabBounds.Right - closeSize - ScaleMetric(8),
                tabBounds.Top + (tabBounds.Height - closeSize) / 2,
                closeSize,
                closeSize);
        }

        private int GetTabIndexAt(Point location)
        {
            for (var index = 0; index < TabPages.Count; index++)
            {
                if (GetTabRect(index).Contains(location))
                {
                    return index;
                }
            }

            return -1;
        }

        private int GetCloseIndexAt(Point location)
        {
            var tabIndex = GetTabIndexAt(location);
            if (tabIndex < 0)
            {
                return -1;
            }

            return GetCloseRectangle(tabIndex).Contains(location) ? tabIndex : -1;
        }

        private void InvalidateTab(int tabIndex)
        {
            if (tabIndex >= 0 && tabIndex < TabPages.Count)
            {
                Invalidate(GetTabRect(tabIndex));
            }
        }

        private void ResetMouseState()
        {
            hoveredTabIndex = -1;
            hoveredCloseIndex = -1;
            pressedCloseIndex = -1;
            Capture = false;
            Cursor = Cursors.Default;
        }

        private void TabPage_TextChanged(object sender, EventArgs e)
        {
            var page = sender as TabPage;
            if (page == null)
            {
                return;
            }

            string previousAutomaticToolTip;
            if (automaticToolTips.TryGetValue(page, out previousAutomaticToolTip))
            {
                if (string.Equals(
                    page.ToolTipText,
                    previousAutomaticToolTip,
                    StringComparison.Ordinal))
                {
                    page.ToolTipText = page.Text;
                    automaticToolTips[page] = page.Text;
                }
                else
                {
                    automaticToolTips.Remove(page);
                }
            }
            else
            {
                EnsureFullNameToolTip(page);
            }

            InvalidateTab(TabPages.IndexOf(page));
        }

        private void EnsureFullNameToolTip(TabPage page)
        {
            if (!string.IsNullOrEmpty(page.ToolTipText))
            {
                return;
            }

            page.ToolTipText = page.Text;
            automaticToolTips[page] = page.Text;
        }

        private void ApplyDpiMetrics()
        {
            if (applyingDpiMetrics)
            {
                return;
            }

            applyingDpiMetrics = true;
            try
            {
                ItemSize = new Size(ScaleMetric(168), ScaleMetric(32));
                Padding = new Point(ScaleMetric(12), ScaleMetric(4));
            }
            finally
            {
                applyingDpiMetrics = false;
            }

            Invalidate();
        }

        private int ScaleMetric(int logicalPixels)
        {
            return Math.Max(1, (int)Math.Round(logicalPixels * dpiScale));
        }

        private static Font CreateArchitecturalFont(float size)
        {
            // The named Bahnschrift instances are system fonts on current
            // Windows versions. Older systems fall back progressively to a
            // light Windows UI face and finally to the configured message font.
            string[] preferredFamilies =
            {
                "Bahnschrift Light SemiCondensed",
                "Bahnschrift SemiLight SemiConde",
                "Bahnschrift Light",
                "Bahnschrift SemiLight",
                "Bahnschrift SemiCondensed",
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
    }
}

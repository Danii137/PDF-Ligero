using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FirmaAutomatica
{
    internal sealed class SignatureSelectionBox : PictureBox
    {
        private static readonly Color SelectionFillColor = Color.FromArgb(52, 77, 124, 216);
        private static readonly Color SelectionBorderColor = Color.FromArgb(66, 95, 145, 226);
        private static readonly Color DetectedFieldFillColor = Color.FromArgb(22, 87, 127, 177);
        private static readonly Color DetectedFieldBorderColor = Color.FromArgb(132, 108, 141, 181);
        private static readonly Color HoveredFieldBorderColor = Color.FromArgb(186, 90, 122, 164);
        private static readonly Color SurfaceBorderColor = Color.FromArgb(224, 218, 210);
        private static readonly Color PlaceholderBackgroundColor = Color.FromArgb(252, 250, 247);
        private static readonly Color PlaceholderTextColor = Color.FromArgb(124, 128, 136);
        private const int ClickThreshold = 5;
        private Point startPoint;
        private bool dragging;
        private string hoveredFieldName;

        public event EventHandler SelectionChanged;

        public Rectangle Selection { get; private set; }

        public string SelectedFieldName { get; private set; }

        public SignatureAppearanceData PreviewData { get; set; }

        public Size DefaultClickSelectionSize { get; set; }

        public string PlaceholderText { get; set; }

        public IList<DetectedFieldArea> DetectedFields { get; private set; }

        public bool UsesDetectedField
        {
            get { return !string.IsNullOrWhiteSpace(SelectedFieldName); }
        }

        public SignatureSelectionBox()
        {
            DoubleBuffered = true;
            Cursor = Cursors.Cross;
            DetectedFields = new List<DetectedFieldArea>();
            MouseDown += SignatureSelectionBox_MouseDown;
            MouseMove += SignatureSelectionBox_MouseMove;
            MouseUp += SignatureSelectionBox_MouseUp;
            MouseLeave += SignatureSelectionBox_MouseLeave;
        }

        public void ClearSelection()
        {
            Selection = Rectangle.Empty;
            SelectedFieldName = null;
            Invalidate();
        }

        public void SetDetectedFields(IEnumerable<DetectedFieldArea> detectedFields)
        {
            DetectedFields = detectedFields == null
                ? new List<DetectedFieldArea>()
                : new List<DetectedFieldArea>(detectedFields);
            hoveredFieldName = null;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs pe)
        {
            base.OnPaint(pe);

            if (Image == null)
            {
                DrawPlaceholder(pe.Graphics);
            }

            if (Image != null)
            {
                DrawDetectedFields(pe.Graphics);
            }

            if (Selection.Width > 0 && Selection.Height > 0)
            {
                using (var fill = new SolidBrush(SelectionFillColor))
                using (var pen = new Pen(SelectionBorderColor, 2))
                {
                    pe.Graphics.FillRectangle(fill, Selection);
                    pe.Graphics.DrawRectangle(pen, Selection);
                }

                DrawPreview(pe.Graphics);
            }

            using (var border = new Pen(SurfaceBorderColor))
            {
                pe.Graphics.DrawRectangle(border, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
            }
        }

        private void SignatureSelectionBox_MouseDown(object sender, MouseEventArgs e)
        {
            dragging = true;
            startPoint = e.Location;
            Selection = Rectangle.Empty;
            SelectedFieldName = null;
            Invalidate();
        }

        private void SignatureSelectionBox_MouseMove(object sender, MouseEventArgs e)
        {
            if (!dragging)
            {
                UpdateHoverState(e.Location);
                return;
            }

            Selection = Normalize(startPoint, e.Location);
            SelectedFieldName = null;
            Invalidate();
        }

        private void SignatureSelectionBox_MouseUp(object sender, MouseEventArgs e)
        {
            if (!dragging)
            {
                return;
            }

            dragging = false;
            var selection = Normalize(startPoint, e.Location);
            if (selection.Width <= ClickThreshold && selection.Height <= ClickThreshold)
            {
                var detectedField = HitTestDetectedField(e.Location);
                if (detectedField != null)
                {
                    Selection = detectedField.Bounds;
                    SelectedFieldName = detectedField.FieldName;
                }
                else
                {
                    Selection = CreateDefaultClickSelection(e.Location);
                    SelectedFieldName = null;
                }
            }
            else
            {
                Selection = ClampToBounds(selection);
                SelectedFieldName = null;
            }

            UpdateHoverState(e.Location);
            Invalidate();

            var handler = SelectionChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private void SignatureSelectionBox_MouseLeave(object sender, EventArgs e)
        {
            hoveredFieldName = null;
            Cursor = Enabled ? Cursors.Cross : Cursors.Default;
            Invalidate();
        }

        private static Rectangle Normalize(Point a, Point b)
        {
            var left = Math.Min(a.X, b.X);
            var top = Math.Min(a.Y, b.Y);
            var width = Math.Abs(a.X - b.X);
            var height = Math.Abs(a.Y - b.Y);
            return new Rectangle(left, top, width, height);
        }

        private Rectangle CreateDefaultClickSelection(Point location)
        {
            var defaultWidth = DefaultClickSelectionSize.Width > 0 ? DefaultClickSelectionSize.Width : Math.Max(120, ClientSize.Width / 4);
            var defaultHeight = DefaultClickSelectionSize.Height > 0 ? DefaultClickSelectionSize.Height : Math.Max(60, ClientSize.Height / 8);

            var left = location.X - (defaultWidth / 2);
            var top = location.Y - (defaultHeight / 2);
            return ClampToBounds(new Rectangle(left, top, defaultWidth, defaultHeight));
        }

        private Rectangle ClampToBounds(Rectangle rectangle)
        {
            var width = Math.Min(rectangle.Width, ClientSize.Width);
            var height = Math.Min(rectangle.Height, ClientSize.Height);
            var left = Math.Max(0, Math.Min(rectangle.Left, ClientSize.Width - width));
            var top = Math.Max(0, Math.Min(rectangle.Top, ClientSize.Height - height));
            return new Rectangle(left, top, width, height);
        }

        private void UpdateHoverState(Point location)
        {
            var detectedField = HitTestDetectedField(location);
            var nextHoveredFieldName = detectedField == null ? null : detectedField.FieldName;
            if (!string.Equals(hoveredFieldName, nextHoveredFieldName, StringComparison.Ordinal))
            {
                hoveredFieldName = nextHoveredFieldName;
                Invalidate();
            }

            Cursor = !Enabled
                ? Cursors.Default
                : detectedField == null ? Cursors.Cross : Cursors.Hand;
        }

        private DetectedFieldArea HitTestDetectedField(Point location)
        {
            DetectedFieldArea bestMatch = null;
            var bestArea = int.MaxValue;
            foreach (var detectedField in DetectedFields)
            {
                if (!detectedField.Bounds.Contains(location))
                {
                    continue;
                }

                var area = detectedField.Bounds.Width * detectedField.Bounds.Height;
                if (bestMatch == null || area < bestArea)
                {
                    bestMatch = detectedField;
                    bestArea = area;
                }
            }

            return bestMatch;
        }

        private void DrawDetectedFields(Graphics graphics)
        {
            foreach (var detectedField in DetectedFields)
            {
                var isSelected = string.Equals(SelectedFieldName, detectedField.FieldName, StringComparison.Ordinal);
                var isHovered = string.Equals(hoveredFieldName, detectedField.FieldName, StringComparison.Ordinal);
                var borderColor = isSelected
                    ? SelectionBorderColor
                    : isHovered ? HoveredFieldBorderColor : DetectedFieldBorderColor;
                var fillColor = isSelected
                    ? Color.FromArgb(32, SelectionBorderColor)
                    : Color.FromArgb(isHovered ? 18 : 10, DetectedFieldFillColor);

                using (var fill = new SolidBrush(fillColor))
                using (var pen = new Pen(borderColor, isSelected ? 2f : 1f))
                {
                    if (!isSelected)
                    {
                        pen.DashStyle = DashStyle.Dash;
                    }

                    graphics.FillRectangle(fill, detectedField.Bounds);
                    graphics.DrawRectangle(pen, detectedField.Bounds);
                }
            }
        }

        private void DrawPreview(Graphics graphics)
        {
            var previewRect = Rectangle.Inflate(Selection, -8, -8);
            if (previewRect.Width < 24 || previewRect.Height < 24)
            {
                return;
            }

            try
            {
                SignatureAppearanceRenderer.Draw(graphics, previewRect, PreviewData, false);
            }
            catch (Exception ex)
            {
                AppLog.Write("Fallo al dibujar la vista previa de la firma: " + ex.Message);
                using (var brush = new SolidBrush(Color.FromArgb(86, 255, 255, 255)))
                using (var border = new Pen(Color.FromArgb(148, 195, 195, 195)))
                using (var font = new Font("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Pixel))
                using (var textBrush = new SolidBrush(PlaceholderTextColor))
                {
                    graphics.FillRectangle(brush, previewRect);
                    graphics.DrawRectangle(border, previewRect);

                    var format = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center,
                        Trimming = StringTrimming.EllipsisCharacter
                    };
                    graphics.DrawString("Vista previa no disponible para este tamano.", font, textBrush, previewRect, format);
                }
            }
        }

        private void DrawPlaceholder(Graphics graphics)
        {
            using (var fill = new SolidBrush(PlaceholderBackgroundColor))
            using (var textBrush = new SolidBrush(PlaceholderTextColor))
            using (var font = new Font("Segoe UI", 11f, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                graphics.FillRectangle(fill, ClientRectangle);

                var text = string.IsNullOrWhiteSpace(PlaceholderText) ? "Cargando pagina..." : PlaceholderText;
                var format = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };
                graphics.DrawString(text, font, textBrush, ClientRectangle, format);
            }
        }

        internal sealed class DetectedFieldArea
        {
            public DetectedFieldArea(string fieldName, Rectangle bounds)
            {
                FieldName = fieldName ?? string.Empty;
                Bounds = bounds;
            }

            public string FieldName { get; private set; }

            public Rectangle Bounds { get; private set; }
        }
    }
}

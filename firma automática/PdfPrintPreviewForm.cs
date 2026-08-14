using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.Windows.Forms;
using PdfiumViewer;
using PdfiumDocument = PdfiumViewer.PdfDocument;

namespace FirmaAutomatica
{
    internal sealed class PdfPrintPreviewForm : Form
    {
        private static readonly Color PaperBackground =
            Color.FromArgb(250, 249, 247);
        private static readonly Color WorkspaceBackground =
            Color.FromArgb(232, 231, 228);
        private static readonly Color DividerColor =
            Color.FromArgb(205, 203, 198);
        private static readonly Color TitleColor =
            Color.FromArgb(31, 31, 29);
        private static readonly Color BodyColor =
            Color.FromArgb(96, 94, 90);
        private static readonly Color MutedColor =
            Color.FromArgb(139, 136, 130);
        private static readonly Color AccentColor =
            Color.FromArgb(238, 91, 61);
        private static readonly Color AccentTextColor =
            Color.FromArgb(185, 68, 45);
        private static readonly Color AccentTintColor =
            Color.FromArgb(251, 236, 231);

        private readonly PdfiumDocument document;
        private readonly string displayName;
        private readonly Panel headerPanel;
        private readonly Label eyebrowLabel;
        private readonly Label titleLabel;
        private readonly Label paperEyebrowLabel;
        private readonly Label paperLabel;
        private readonly PdfMeasuredPreviewSurface previewSurface;
        private readonly Panel footerPanel;
        private readonly Label printerLabel;
        private readonly Button previousPageButton;
        private readonly Label pageLabel;
        private readonly Button nextPageButton;
        private readonly Button closeButton;
        private readonly Button printButton;

        private Image previewImage;
        private int currentPageIndex;

        public PdfPrintPreviewForm(
            PdfiumDocument document,
            string displayName,
            int initialPageIndex)
        {
            if (document == null)
            {
                throw new ArgumentNullException("document");
            }

            this.document = document;
            this.displayName = string.IsNullOrWhiteSpace(displayName)
                ? "Documento PDF"
                : displayName;
            currentPageIndex = Math.Max(
                0,
                Math.Min(document.PageCount - 1, initialPageIndex));

            Text = "Vista previa de impresión - PDF Ligero";
            AppBranding.ApplyWindowIcon(this);
            Width = 1060;
            Height = 780;
            MinimumSize = new Size(820, 600);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = WorkspaceBackground;
            Font = CreateUiFont(9.25f, FontStyle.Regular);
            KeyPreview = true;

            headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 66,
                BackColor = PaperBackground
            };
            headerPanel.Controls.Add(new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = DividerColor
            });

            eyebrowLabel = new Label
            {
                Left = 20,
                Top = 7,
                Width = 420,
                Height = 15,
                Text = "VISTA PREVIA / IMPRESIÓN",
                ForeColor = AccentTextColor,
                Font = CreateArchitecturalFont(7.5f, true),
                TextAlign = ContentAlignment.MiddleLeft
            };
            titleLabel = new Label
            {
                Left = 20,
                Top = 23,
                Width = 600,
                Height = 28,
                Text = this.displayName,
                ForeColor = TitleColor,
                Font = CreateArchitecturalFont(12f, false),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
            var accentLine = new Panel
            {
                Left = 20,
                Top = 55,
                Width = 42,
                Height = 2,
                BackColor = AccentColor
            };
            paperEyebrowLabel = new Label
            {
                Top = 7,
                Width = 310,
                Height = 15,
                Text = "FORMATO DE LA PÁGINA",
                ForeColor = AccentTextColor,
                Font = CreateArchitecturalFont(7.5f, true),
                TextAlign = ContentAlignment.MiddleRight
            };
            paperLabel = new Label
            {
                Top = 23,
                Width = 310,
                Height = 28,
                Text = "—",
                ForeColor = TitleColor,
                Font = CreateArchitecturalFont(10.5f, false),
                TextAlign = ContentAlignment.MiddleRight,
                AutoEllipsis = true
            };

            headerPanel.Controls.Add(eyebrowLabel);
            headerPanel.Controls.Add(titleLabel);
            headerPanel.Controls.Add(accentLine);
            headerPanel.Controls.Add(paperEyebrowLabel);
            headerPanel.Controls.Add(paperLabel);

            footerPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 60,
                BackColor = PaperBackground
            };
            footerPanel.Controls.Add(new Panel
            {
                Dock = DockStyle.Top,
                Height = 1,
                BackColor = DividerColor
            });

            printerLabel = new Label
            {
                Left = 20,
                Top = 10,
                Height = 38,
                Text =
                    "SALIDA / página completa · impresora y papel al continuar",
                ForeColor = MutedColor,
                Font = CreateArchitecturalFont(7.75f, false),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
            previousPageButton = CreateSmallButton("‹", "Página anterior");
            previousPageButton.Click += delegate { ChangePage(-1); };
            pageLabel = new Label
            {
                Width = 88,
                Height = 32,
                Text = "1 / 1",
                ForeColor = BodyColor,
                Font = CreateArchitecturalFont(9f, true),
                TextAlign = ContentAlignment.MiddleCenter
            };
            nextPageButton = CreateSmallButton("›", "Página siguiente");
            nextPageButton.Click += delegate { ChangePage(1); };
            closeButton = CreateActionButton(
                "Cerrar",
                false);
            closeButton.DialogResult = DialogResult.Cancel;
            closeButton.Click += delegate { Close(); };
            printButton = CreateActionButton(
                "Imprimir…",
                true);
            printButton.Click += PrintButton_Click;

            footerPanel.Controls.Add(printerLabel);
            footerPanel.Controls.Add(previousPageButton);
            footerPanel.Controls.Add(pageLabel);
            footerPanel.Controls.Add(nextPageButton);
            footerPanel.Controls.Add(closeButton);
            footerPanel.Controls.Add(printButton);

            previewSurface = new PdfMeasuredPreviewSurface
            {
                Dock = DockStyle.Fill,
                BackColor = WorkspaceBackground
            };

            Controls.Add(previewSurface);
            Controls.Add(footerPanel);
            Controls.Add(headerPanel);

            AcceptButton = printButton;
            CancelButton = closeButton;
            Shown += delegate
            {
                ShowCurrentPage();
            };
            Resize += delegate { LayoutControls(); };
            KeyDown += PdfPrintPreviewForm_KeyDown;
            LayoutControls();
        }

        private void ChangePage(int offset)
        {
            var nextPage = Math.Max(
                0,
                Math.Min(
                    document.PageCount - 1,
                    currentPageIndex + offset));
            if (nextPage == currentPageIndex)
            {
                return;
            }

            currentPageIndex = nextPage;
            ShowCurrentPage();
        }

        private void ShowCurrentPage()
        {
            if (document.PageCount < 1)
            {
                DisposePreviewImage();
                previewSurface.SetPreview(
                    null,
                    PdfPageSizeFormatter.Invalid(),
                    "El PDF no contiene páginas.");
                paperEyebrowLabel.Text = "HOJA PDF / SIN PÁGINAS";
                paperLabel.Text = "—";
                pageLabel.Text = "0 / 0";
                previousPageButton.Enabled = false;
                nextPageButton.Enabled = false;
                printButton.Enabled = false;
                return;
            }

            printButton.Enabled = true;
            currentPageIndex = Math.Max(
                0,
                Math.Min(document.PageCount - 1, currentPageIndex));
            var pageInfo = PdfPageSizeFormatter.Invalid();
            try
            {
                if (currentPageIndex < document.PageSizes.Count)
                {
                    pageInfo = PdfPageSizeFormatter.FromPoints(
                        document.PageSizes[currentPageIndex]);
                }
            }
            catch
            {
                pageInfo = PdfPageSizeFormatter.Invalid();
            }

            paperEyebrowLabel.Text = pageInfo.IsValid
                ? "HOJA PDF / " + pageInfo.OrientationName
                : "HOJA PDF / FORMATO";
            paperLabel.Text = pageInfo.CompactText;
            pageLabel.Text =
                (currentPageIndex + 1) + " / " + document.PageCount;
            previousPageButton.Enabled = currentPageIndex > 0;
            nextPageButton.Enabled =
                currentPageIndex < document.PageCount - 1;

            DisposePreviewImage();
            previewSurface.SetPreview(
                null,
                pageInfo,
                "Preparando vista previa…");

            UseWaitCursor = true;
            try
            {
                var renderSize = CalculateRenderSize(
                    pageInfo,
                    previewSurface.ClientSize);
                previewImage = document.Render(
                    currentPageIndex,
                    renderSize.Width,
                    renderSize.Height,
                    96f,
                    96f,
                    PdfRenderFlags.ForPrinting |
                    PdfRenderFlags.Annotations |
                    PdfRenderFlags.LimitImageCacheSize);
                previewSurface.SetPreview(
                    previewImage,
                    pageInfo,
                    null);
            }
            catch (Exception ex)
            {
                previewSurface.SetPreview(
                    null,
                    pageInfo,
                    "No se pudo generar la vista previa.\r\n" +
                    ex.GetBaseException().Message);
            }
            finally
            {
                UseWaitCursor = false;
            }
        }

        private static Size CalculateRenderSize(
            PdfPageSizeInfo pageInfo,
            Size previewArea)
        {
            const int maximumWidth = 1400;
            const int maximumHeight = 1600;
            const double previewSupersampling = 1.6;
            if (pageInfo == null ||
                !pageInfo.IsValid ||
                pageInfo.WidthPoints <= 0 ||
                pageInfo.HeightPoints <= 0)
            {
                return new Size(1000, 1400);
            }

            var availableWidth = Math.Max(
                320,
                previewArea.Width - 170);
            var availableHeight = Math.Max(
                320,
                previewArea.Height - 120);
            var displayScale = Math.Min(
                availableWidth / pageInfo.WidthPoints,
                availableHeight / pageInfo.HeightPoints);
            var scale = Math.Min(
                Math.Min(
                    maximumWidth / pageInfo.WidthPoints,
                    maximumHeight / pageInfo.HeightPoints),
                Math.Max(0.25, displayScale * previewSupersampling));
            return new Size(
                Math.Max(
                    1,
                    (int)Math.Round(pageInfo.WidthPoints * scale)),
                Math.Max(
                    1,
                    (int)Math.Round(pageInfo.HeightPoints * scale)));
        }

        private void PrintButton_Click(object sender, EventArgs e)
        {
            try
            {
                // Cuadro propio: el de Windows no deja elegir paginas sueltas
                // ni solo pares o impares, ni ensena si la impresora admite
                // color o doble cara.
                using (var opciones = new PdfPrintOptionsDialog(
                    document,
                    displayName,
                    currentPageIndex + 1))
                {
                    if (opciones.ShowDialog(this) != DialogResult.OK)
                    {
                        return;
                    }
                }

                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    "No se pudo imprimir.\r\n\r\n" +
                    ex.GetBaseException().Message,
                    "PDF Ligero",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                UseWaitCursor = false;
            }
        }

        private void LayoutControls()
        {
            paperEyebrowLabel.Left =
                headerPanel.ClientSize.Width -
                paperEyebrowLabel.Width - 20;
            paperLabel.Left =
                headerPanel.ClientSize.Width -
                paperLabel.Width - 20;
            titleLabel.Width = Math.Max(
                220,
                paperLabel.Left - titleLabel.Left - 24);

            printButton.Left =
                footerPanel.ClientSize.Width -
                printButton.Width - 18;
            printButton.Top = 13;
            closeButton.Left =
                printButton.Left - closeButton.Width - 8;
            closeButton.Top = 13;

            const int navigationWidth = 164;
            var navigationLeft = Math.Max(
                260,
                (footerPanel.ClientSize.Width - navigationWidth) / 2);
            previousPageButton.Left = navigationLeft;
            previousPageButton.Top = 14;
            pageLabel.Left = previousPageButton.Right + 3;
            pageLabel.Top = 14;
            nextPageButton.Left = pageLabel.Right + 3;
            nextPageButton.Top = 14;

            printerLabel.Width = Math.Max(
                180,
                Math.Min(
                    navigationLeft - printerLabel.Left - 18,
                    closeButton.Left - printerLabel.Left - 18));
        }

        private void PdfPrintPreviewForm_KeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left)
            {
                e.Handled = true;
                ChangePage(-1);
            }
            else if (e.KeyCode == Keys.Right)
            {
                e.Handled = true;
                ChangePage(1);
            }
            else if (e.Control && e.KeyCode == Keys.P)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                PrintButton_Click(sender, EventArgs.Empty);
            }
        }

        private static Button CreateSmallButton(
            string text,
            string accessibleName)
        {
            var button = new Button
            {
                Width = 34,
                Height = 32,
                Text = text,
                AccessibleName = accessibleName,
                FlatStyle = FlatStyle.Flat,
                BackColor = PaperBackground,
                ForeColor = TitleColor,
                Cursor = Cursors.Hand,
                Font = CreateArchitecturalFont(14f, false)
            };
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = DividerColor;
            button.FlatAppearance.MouseOverBackColor = AccentTintColor;
            return button;
        }

        private static Button CreateActionButton(
            string text,
            bool primary)
        {
            var button = new Button
            {
                Width = primary ? 118 : 88,
                Height = 34,
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? AccentColor : PaperBackground,
                ForeColor = primary ? Color.White : TitleColor,
                Cursor = Cursors.Hand,
                Font = CreateArchitecturalFont(9f, true)
            };
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor =
                primary ? AccentColor : DividerColor;
            button.FlatAppearance.MouseOverBackColor =
                primary ? Color.FromArgb(207, 72, 47) : AccentTintColor;
            return button;
        }

        private static Font CreateUiFont(
            float size,
            FontStyle style)
        {
            return new Font(
                "Segoe UI",
                size,
                style,
                GraphicsUnit.Point);
        }

        private static Font CreateArchitecturalFont(
            float size,
            bool emphasized)
        {
            return new Font(
                "Bahnschrift",
                size,
                emphasized ? FontStyle.Bold : FontStyle.Regular,
                GraphicsUnit.Point);
        }

        private void DisposePreviewImage()
        {
            previewSurface.SetPreview(
                null,
                PdfPageSizeFormatter.Invalid(),
                null);
            if (previewImage != null)
            {
                previewImage.Dispose();
                previewImage = null;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposePreviewImage();
            }

            base.Dispose(disposing);
        }
    }

    internal sealed class PdfMeasuredPreviewSurface : Control
    {
        private static readonly Color WorkspaceBackground =
            Color.FromArgb(232, 231, 228);
        private static readonly Color PaperColor =
            Color.FromArgb(255, 255, 254);
        private static readonly Color ShadowColor =
            Color.FromArgb(70, 58, 54, 48);
        private static readonly Color DimensionColor =
            Color.FromArgb(185, 68, 45);
        private static readonly Color DimensionBackground =
            Color.FromArgb(232, 231, 228);
        private static readonly Color MessageColor =
            Color.FromArgb(96, 94, 90);

        private Image previewImage;
        private PdfPageSizeInfo pageInfo =
            PdfPageSizeFormatter.Invalid();
        private string message;

        public PdfMeasuredPreviewSurface()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
            BackColor = WorkspaceBackground;
        }

        public void SetPreview(
            Image image,
            PdfPageSizeInfo pageInfo,
            string message)
        {
            previewImage = image;
            this.pageInfo = pageInfo ??
                PdfPageSizeFormatter.Invalid();
            this.message = message;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.InterpolationMode =
                InterpolationMode.HighQualityBicubic;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            if (previewImage == null)
            {
                DrawMessage(e.Graphics);
                return;
            }

            var pageBounds = CalculatePageBounds();
            using (var shadowBrush = new SolidBrush(ShadowColor))
            {
                e.Graphics.FillRectangle(
                    shadowBrush,
                    new RectangleF(
                        pageBounds.X + 7,
                        pageBounds.Y + 8,
                        pageBounds.Width,
                        pageBounds.Height));
            }

            using (var paperBrush = new SolidBrush(PaperColor))
            {
                e.Graphics.FillRectangle(paperBrush, pageBounds);
            }

            e.Graphics.DrawImage(previewImage, pageBounds);
            using (var borderPen = new Pen(
                Color.FromArgb(190, 187, 181),
                1f))
            {
                e.Graphics.DrawRectangle(
                    borderPen,
                    pageBounds.X,
                    pageBounds.Y,
                    pageBounds.Width,
                    pageBounds.Height);
            }

            if (pageInfo != null && pageInfo.IsValid)
            {
                DrawDimensions(e.Graphics, pageBounds);
            }
        }

        private RectangleF CalculatePageBounds()
        {
            var availableWidth = Math.Max(80, ClientSize.Width - 170);
            var availableHeight = Math.Max(80, ClientSize.Height - 120);
            var imageRatio =
                previewImage.Width / (float)previewImage.Height;
            var width = (float)availableWidth;
            var height = width / imageRatio;
            if (height > availableHeight)
            {
                height = availableHeight;
                width = height * imageRatio;
            }

            return new RectangleF(
                Math.Max(28, (ClientSize.Width - width) / 2f - 8),
                Math.Max(52, (ClientSize.Height - height) / 2f + 10),
                width,
                height);
        }

        private void DrawDimensions(
            Graphics graphics,
            RectangleF pageBounds)
        {
            using (var pen = new Pen(DimensionColor, 1f))
            using (var textBrush = new SolidBrush(DimensionColor))
            using (var backgroundBrush =
                new SolidBrush(DimensionBackground))
            using (var font = new Font(
                "Bahnschrift",
                8.25f,
                FontStyle.Regular,
                GraphicsUnit.Point))
            using (var centredFormat = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            })
            {
                var topY = pageBounds.Top - 24f;
                graphics.DrawLine(
                    pen,
                    pageBounds.Left,
                    topY,
                    pageBounds.Right,
                    topY);
                graphics.DrawLine(
                    pen,
                    pageBounds.Left,
                    topY - 5,
                    pageBounds.Left,
                    topY + 5);
                graphics.DrawLine(
                    pen,
                    pageBounds.Right,
                    topY - 5,
                    pageBounds.Right,
                    topY + 5);

                var widthText =
                    PdfPageSizeFormatter.FormatSingleMillimetres(
                        pageInfo.WidthMillimetres);
                var widthTextSize =
                    graphics.MeasureString(widthText, font);
                var widthTextBounds = new RectangleF(
                    pageBounds.Left +
                        ((pageBounds.Width - widthTextSize.Width) / 2f) - 5,
                    topY - (widthTextSize.Height / 2f),
                    widthTextSize.Width + 10,
                    widthTextSize.Height);
                graphics.FillRectangle(
                    backgroundBrush,
                    widthTextBounds);
                graphics.DrawString(
                    widthText,
                    font,
                    textBrush,
                    widthTextBounds,
                    centredFormat);

                var rightX = pageBounds.Right + 28f;
                graphics.DrawLine(
                    pen,
                    rightX,
                    pageBounds.Top,
                    rightX,
                    pageBounds.Bottom);
                graphics.DrawLine(
                    pen,
                    rightX - 5,
                    pageBounds.Top,
                    rightX + 5,
                    pageBounds.Top);
                graphics.DrawLine(
                    pen,
                    rightX - 5,
                    pageBounds.Bottom,
                    rightX + 5,
                    pageBounds.Bottom);

                var heightText =
                    PdfPageSizeFormatter.FormatSingleMillimetres(
                        pageInfo.HeightMillimetres);
                var state = graphics.Save();
                try
                {
                    graphics.TranslateTransform(
                        rightX,
                        pageBounds.Top + (pageBounds.Height / 2f));
                    graphics.RotateTransform(-90f);
                    var heightTextSize =
                        graphics.MeasureString(heightText, font);
                    var heightTextBounds = new RectangleF(
                        -(heightTextSize.Width / 2f) - 5,
                        -(heightTextSize.Height / 2f),
                        heightTextSize.Width + 10,
                        heightTextSize.Height);
                    graphics.FillRectangle(
                        backgroundBrush,
                        heightTextBounds);
                    graphics.DrawString(
                        heightText,
                        font,
                        textBrush,
                        heightTextBounds,
                        centredFormat);
                }
                finally
                {
                    graphics.Restore(state);
                }
            }
        }

        private void DrawMessage(Graphics graphics)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            using (var brush = new SolidBrush(MessageColor))
            using (var font = new Font(
                "Bahnschrift",
                10f,
                FontStyle.Regular,
                GraphicsUnit.Point))
            using (var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            })
            {
                graphics.DrawString(
                    message,
                    font,
                    brush,
                    ClientRectangle,
                    format);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using iTextSharp.text.pdf;
using PdfiumViewer;
using PdfiumDocument = PdfiumViewer.PdfDocument;

namespace FirmaAutomatica
{
    internal sealed class PdfPlacementForm : Form
    {
        private static readonly Color WindowBackgroundColor = Color.FromArgb(248, 246, 242);
        private static readonly Color SurfaceBackgroundColor = Color.FromArgb(245, 241, 236);
        private static readonly Color PanelBackgroundColor = Color.FromArgb(255, 254, 252);
        private static readonly Color DividerColor = Color.FromArgb(229, 224, 216);
        private static readonly Color TitleColor = Color.FromArgb(34, 40, 48);
        private static readonly Color BodyColor = Color.FromArgb(90, 96, 104);
        private static readonly Color MutedColor = Color.FromArgb(126, 132, 140);
        private static readonly Color PageLabelColor = Color.FromArgb(105, 111, 119);
        private static readonly Color SecondaryButtonBorderColor = Color.FromArgb(207, 199, 190);
        private static readonly Color SecondaryButtonHoverColor = Color.FromArgb(249, 247, 243);
        private static readonly Color SecondaryButtonDisabledColor = Color.FromArgb(245, 242, 238);
        private static readonly Color SecondaryButtonDisabledTextColor = Color.FromArgb(156, 161, 167);
        private static readonly Color PrimaryButtonColor = Color.FromArgb(97, 116, 132);
        private static readonly Color PrimaryButtonHoverColor = Color.FromArgb(84, 103, 118);
        private static readonly Color PrimaryButtonDisabledColor = Color.FromArgb(214, 220, 225);
        private static readonly Color PrimaryButtonDisabledTextColor = Color.FromArgb(128, 137, 145);
        private const float DefaultClickWidthCm = 6f;
        private const float DefaultClickHeightCm = 2f;
        private const float PdfPointsPerCm = 28.3464567f;
        private const int MaxPreviewWidth = 780;
        private const int MaxPreviewHeight = 1100;
        private const int PageCardSpacing = 26;
        private const int PageCanvasPadding = 30;
        private readonly string pdfPath;
        private readonly Panel headerPanel;
        private readonly Label titleLabel;
        private readonly Label documentNameLabel;
        private readonly Label instructionsLabel;
        private readonly Panel pagesViewport;
        private readonly Panel pagesCanvas;
        private readonly Panel footerPanel;
        private readonly Label selectionInfoLabel;
        private readonly Label renderStatusLabel;
        private readonly Button acceptButton;
        private readonly Button clearSelectionButton;
        private readonly Button cancelButton;
        private readonly bool isLastDocument;
        private readonly SignatureAppearanceProfile signatureProfile;
        private readonly List<PageSurface> pageSurfaces = new List<PageSurface>();
        private PageSurface selectedSurface;
        private SignaturePlacement placement;
        private CancellationTokenSource renderCancellation;
        private int renderedPageCount;
        private int detectedSignatureFieldCount;

        public PdfPlacementForm(string pdfPath, int currentIndex, int totalCount, SignatureAppearanceProfile signatureProfile)
        {
            this.pdfPath = pdfPath;
            this.signatureProfile = signatureProfile;
            isLastDocument = currentIndex == totalCount;

            Text = "Colocar firma";
            AppBranding.ApplyWindowIcon(this);
            Width = 1080;
            Height = 840;
            MinimumSize = new Size(960, 720);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            BackColor = WindowBackgroundColor;
            Font = CreateUiFont(9.25f, FontStyle.Regular);

            headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 112,
                BackColor = PanelBackgroundColor
            };
            headerPanel.Controls.Add(CreateDividerPanel(DockStyle.Bottom));

            titleLabel = new Label
            {
                Left = 24,
                Top = 19,
                Width = 320,
                Height = 30,
                Font = CreateUiFont(16.5f, FontStyle.Bold),
                ForeColor = TitleColor,
                Text = string.Format("PDF {0} de {1}", currentIndex, totalCount)
            };

            documentNameLabel = new Label
            {
                Left = 24,
                Top = 50,
                Width = 1000,
                Height = 22,
                AutoEllipsis = true,
                Font = CreateUiFont(10.2f, FontStyle.Regular),
                ForeColor = BodyColor,
                Text = Path.GetFileName(pdfPath)
            };

            instructionsLabel = new Label
            {
                Left = 24,
                Top = 77,
                Width = 1000,
                Height = 20,
                ForeColor = MutedColor,
                Text = string.Empty
            };

            headerPanel.Controls.Add(titleLabel);
            headerPanel.Controls.Add(documentNameLabel);
            headerPanel.Controls.Add(instructionsLabel);

            pagesViewport = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = SurfaceBackgroundColor
            };
            pagesViewport.Resize += PagesViewport_Resize;

            pagesCanvas = new Panel
            {
                Left = 0,
                Top = 0,
                Width = 100,
                Height = 100,
                BackColor = SurfaceBackgroundColor
            };
            pagesViewport.Controls.Add(pagesCanvas);

            footerPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 92,
                BackColor = PanelBackgroundColor
            };
            footerPanel.Controls.Add(CreateDividerPanel(DockStyle.Top));
            footerPanel.Resize += FooterPanel_Resize;

            selectionInfoLabel = new Label
            {
                Left = 24,
                Top = 16,
                Width = 520,
                Height = 24,
                Font = CreateUiFont(10f, FontStyle.Regular),
                ForeColor = TitleColor,
                Text = "Todavia no has marcado la zona de firma."
            };

            renderStatusLabel = new Label
            {
                Left = 24,
                Top = 43,
                Width = 520,
                Height = 18,
                ForeColor = MutedColor,
                Text = "Preparando vistas del PDF..."
            };

            clearSelectionButton = new Button
            {
                Text = "Borrar recuadro",
                Width = 132,
                Height = 34,
                Enabled = false
            };
            StyleButton(clearSelectionButton, false);
            clearSelectionButton.Click += ClearSelectionButton_Click;

            cancelButton = new Button
            {
                Text = "Cancelar",
                Width = 104,
                Height = 34
            };
            StyleButton(cancelButton, false);
            cancelButton.Click += (sender, args) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            acceptButton = new Button
            {
                Text = isLastDocument ? "Firmar y guardar" : "Guardar y siguiente",
                Width = 156,
                Height = 34,
                Enabled = false
            };
            StyleButton(acceptButton, true);
            acceptButton.Click += AcceptButton_Click;

            footerPanel.Controls.Add(selectionInfoLabel);
            footerPanel.Controls.Add(renderStatusLabel);
            footerPanel.Controls.Add(clearSelectionButton);
            footerPanel.Controls.Add(cancelButton);
            footerPanel.Controls.Add(acceptButton);

            Controls.Add(pagesViewport);
            Controls.Add(footerPanel);
            Controls.Add(headerPanel);

            AcceptButton = acceptButton;
            CancelButton = cancelButton;
            Load += PdfPlacementForm_Load;
            Shown += PdfPlacementForm_Shown;
            FormClosed += PdfPlacementForm_FormClosed;
            Resize += PdfPlacementForm_Resize;
            UpdateInstructionsLabel();
            LayoutFooterButtons();
        }

        public SignaturePlacement GetPlacement()
        {
            return placement;
        }

        private void PdfPlacementForm_Load(object sender, EventArgs e)
        {
            var detectedSignatureFields = LoadDetectedSignatureFields();
            detectedSignatureFieldCount = detectedSignatureFields.Count;
            var detectedFieldsByPage = detectedSignatureFields
                .GroupBy(field => field.PageNumber)
                .ToDictionary(group => group.Key, group => (IReadOnlyList<DetectedSignatureField>)group.ToList());

            pagesCanvas.SuspendLayout();
            using (var document = PdfiumDocument.Load(pdfPath))
            {
                for (var i = 0; i < document.PageCount; i++)
                {
                    var pageSize = document.PageSizes[i];
                    var renderSize = BuildRenderSize(pageSize);
                    IReadOnlyList<DetectedSignatureField> pageDetectedFields;
                    if (!detectedFieldsByPage.TryGetValue(i + 1, out pageDetectedFields))
                    {
                        pageDetectedFields = new DetectedSignatureField[0];
                    }

                    var surface = new PageSurface(i + 1, pageSize.Width, pageSize.Height, renderSize, pageDetectedFields, signatureProfile);
                    surface.SelectionChanged += Surface_SelectionChanged;
                    pageSurfaces.Add(surface);
                    pagesCanvas.Controls.Add(surface.Container);
                }
            }

            pagesCanvas.ResumeLayout();
            UpdateInstructionsLabel();
            UpdateSelectionUi();
            ArrangePageSurfaces();
            UpdateRenderStatus();
        }

        private void PdfPlacementForm_Shown(object sender, EventArgs e)
        {
            if (pageSurfaces.Count == 0 || renderCancellation != null)
            {
                return;
            }

            renderCancellation = new CancellationTokenSource();
            Task.Factory.StartNew(
                () => RenderPagesInBackground(renderCancellation.Token),
                renderCancellation.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        private void PdfPlacementForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (renderCancellation != null)
            {
                renderCancellation.Cancel();
                renderCancellation.Dispose();
                renderCancellation = null;
            }

            foreach (var surface in pageSurfaces)
            {
                surface.Dispose();
            }
        }

        private void PdfPlacementForm_Resize(object sender, EventArgs e)
        {
            LayoutFooterButtons();
            UpdateHeaderLayout();
        }

        private void PagesViewport_Resize(object sender, EventArgs e)
        {
            ArrangePageSurfaces();
        }

        private void FooterPanel_Resize(object sender, EventArgs e)
        {
            LayoutFooterButtons();
        }

        private void Surface_SelectionChanged(object sender, EventArgs e)
        {
            var current = (PageSurface)sender;
            foreach (var surface in pageSurfaces.Where(surface => surface != current))
            {
                surface.ClearSelection();
            }

            current.RefreshPreviewTimestamp();
            selectedSurface = current;
            UpdateSelectionUi();
        }

        private void AcceptButton_Click(object sender, EventArgs e)
        {
            if (selectedSurface == null || !selectedSurface.HasSelection)
            {
                MessageBox.Show(this, "Selecciona un campo de firma detectado o dibuja primero el rectangulo de la firma.", "Firma automatica", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            placement = selectedSurface.BuildPlacement(pdfPath);
            DialogResult = DialogResult.OK;
            Close();
        }

        private void ClearSelectionButton_Click(object sender, EventArgs e)
        {
            if (selectedSurface == null)
            {
                return;
            }

            selectedSurface.ClearSelection();
            selectedSurface = null;
            UpdateSelectionUi();
        }

        private void UpdateSelectionUi()
        {
            var hasSelection = selectedSurface != null && selectedSurface.HasSelection;
            acceptButton.Enabled = hasSelection;
            clearSelectionButton.Enabled = hasSelection;

            if (!hasSelection)
            {
                selectionInfoLabel.Text = detectedSignatureFieldCount > 0
                    ? "Puedes elegir un campo detectado o dibujar una zona manual."
                    : "Todavia no has marcado la zona de firma.";
                return;
            }

            if (selectedSurface.UsesDetectedField)
            {
                selectionInfoLabel.Text = string.Format(
                    "Pagina {0}: campo de firma detectado seleccionado. Pulsa Enter o \"{1}\" para continuar.",
                    selectedSurface.PageNumber,
                    isLastDocument ? "Firmar y guardar" : "Guardar y siguiente");
                return;
            }

            var selection = selectedSurface.GetSelection();
            selectionInfoLabel.Text = string.Format(
                "Pagina {0}: zona elegida de {1} x {2} px. Pulsa Enter o \"{3}\" para continuar.",
                selectedSurface.PageNumber,
                selection.Width,
                selection.Height,
                isLastDocument ? "Firmar y guardar" : "Guardar y siguiente");
        }

        private void UpdateRenderStatus()
        {
            if (renderedPageCount >= pageSurfaces.Count)
            {
                renderStatusLabel.Text = detectedSignatureFieldCount > 0
                    ? string.Format("Todas las paginas estan listas. Se han detectado {0} campo(s) de firma.", detectedSignatureFieldCount)
                    : "Todas las paginas estan listas.";
                return;
            }

            var baseText = string.Format(
                "Cargando vistas: {0}/{1} paginas listas...",
                renderedPageCount,
                pageSurfaces.Count);
            renderStatusLabel.Text = detectedSignatureFieldCount > 0
                ? baseText + string.Format(" Se han detectado {0} campo(s) de firma.", detectedSignatureFieldCount)
                : baseText;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Enter && acceptButton.Enabled)
            {
                AcceptButton_Click(acceptButton, EventArgs.Empty);
                return true;
            }

            if (keyData == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void RenderPagesInBackground(CancellationToken cancellationToken)
        {
            try
            {
                using (var document = PdfiumDocument.Load(pdfPath))
                {
                    foreach (var pageIndex in BuildRenderOrder(pageSurfaces.Count))
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            return;
                        }

                        var surface = pageSurfaces[pageIndex];
                        var image = document.Render(
                            pageIndex,
                            surface.RenderWidth,
                            surface.RenderHeight,
                            96,
                            96,
                            PdfRenderFlags.Annotations);

                        if (cancellationToken.IsCancellationRequested)
                        {
                            image.Dispose();
                            return;
                        }

                        if (IsDisposed || !IsHandleCreated)
                        {
                            image.Dispose();
                            return;
                        }

                        BeginInvoke((MethodInvoker)delegate
                        {
                            if (IsDisposed)
                            {
                                image.Dispose();
                                return;
                            }

                            surface.SetImage(image);
                            renderedPageCount++;
                            UpdateRenderStatus();
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Write("No se pudieron renderizar las paginas en segundo plano: " + ex.Message);
                if (IsDisposed || !IsHandleCreated || cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                BeginInvoke((MethodInvoker)delegate
                {
                    if (!IsDisposed)
                    {
                        renderStatusLabel.Text = "No se pudo cargar alguna vista previa.";
                    }
                });
            }
        }

        private void ArrangePageSurfaces()
        {
            if (pagesCanvas == null || pagesViewport == null || pageSurfaces.Count == 0)
            {
                return;
            }

            var availableWidth = Math.Max(640, pagesViewport.ClientSize.Width - 2);
            var maxContentWidth = pageSurfaces.Max(surface => surface.Container.Width);
            var canvasWidth = Math.Max(availableWidth, maxContentWidth + (PageCanvasPadding * 2));
            var y = PageCanvasPadding;

            pagesCanvas.SuspendLayout();
            foreach (var surface in pageSurfaces)
            {
                var container = surface.Container;
                container.Left = Math.Max(PageCanvasPadding, (canvasWidth - container.Width) / 2);
                container.Top = y;
                y += container.Height + PageCardSpacing;
            }

            pagesCanvas.Width = canvasWidth;
            pagesCanvas.Height = Math.Max(y, pagesViewport.ClientSize.Height - 1);
            pagesCanvas.ResumeLayout();
        }

        private void LayoutFooterButtons()
        {
            if (footerPanel == null)
            {
                return;
            }

            var right = footerPanel.ClientSize.Width - 24;
            acceptButton.Left = right - acceptButton.Width;
            acceptButton.Top = 27;

            cancelButton.Left = acceptButton.Left - 12 - cancelButton.Width;
            cancelButton.Top = 27;

            clearSelectionButton.Left = cancelButton.Left - 12 - clearSelectionButton.Width;
            clearSelectionButton.Top = 27;

            var textWidth = Math.Max(280, clearSelectionButton.Left - 40 - selectionInfoLabel.Left);
            selectionInfoLabel.Width = textWidth;
            renderStatusLabel.Width = textWidth;
        }

        private void UpdateHeaderLayout()
        {
            var contentWidth = Math.Max(320, ClientSize.Width - 48);
            titleLabel.Width = contentWidth;
            documentNameLabel.Width = contentWidth;
            instructionsLabel.Width = contentWidth;
        }

        private void UpdateInstructionsLabel()
        {
            instructionsLabel.Text = BuildInstructionsText();
        }

        private string BuildInstructionsText()
        {
            var primaryAction = isLastDocument ? "Firmar y guardar" : "Guardar y siguiente";
            var detectedFieldsHint = detectedSignatureFieldCount > 0
                ? string.Format(" Se han detectado {0} campo(s) de firma: puedes hacer clic directamente sobre ellos.", detectedSignatureFieldCount)
                : string.Empty;
            return string.Format(
                "Arrastra un rectangulo o haz clic para una firma de 6 x 2 cm. Pulsa Enter o \"{0}\" para continuar.{1}",
                primaryAction,
                detectedFieldsHint);
        }

        private List<DetectedSignatureField> LoadDetectedSignatureFields()
        {
            var detectedFields = new List<DetectedSignatureField>();
            try
            {
                using (var reader = new PdfReader(pdfPath))
                {
                    var blankSignatureNames = reader.AcroFields.GetBlankSignatureNames();
                    foreach (var fieldName in blankSignatureNames)
                    {
                        var fieldPositions = reader.AcroFields.GetFieldPositions(fieldName);
                        if (fieldPositions == null)
                        {
                            continue;
                        }

                        foreach (var fieldPosition in fieldPositions)
                        {
                            if (fieldPosition == null || fieldPosition.position == null)
                            {
                                continue;
                            }

                            detectedFields.Add(new DetectedSignatureField(
                                fieldName,
                                fieldPosition.page,
                                fieldPosition.position.Left,
                                fieldPosition.position.Bottom,
                                fieldPosition.position.Right,
                                fieldPosition.position.Top));
                        }
                    }
                }

                AppLog.Write("Campos de firma en blanco detectados en " + Path.GetFileName(pdfPath) + ": " + detectedFields.Count);
            }
            catch (Exception ex)
            {
                AppLog.Write("No se pudieron detectar los campos de firma existentes: " + ex.Message);
            }

            return detectedFields;
        }

        private static IEnumerable<int> BuildRenderOrder(int totalPages)
        {
            for (var i = 0; i < totalPages; i++)
            {
                yield return i;
            }
        }

        private static Size BuildRenderSize(SizeF pageSize)
        {
            var safeWidth = Math.Max(1f, pageSize.Width);
            var safeHeight = Math.Max(1f, pageSize.Height);
            var scale = Math.Min(MaxPreviewWidth / safeWidth, MaxPreviewHeight / safeHeight);
            scale = Math.Max(scale, 0.35f);

            var width = Math.Max(360, (int)Math.Round(safeWidth * scale));
            var height = Math.Max(480, (int)Math.Round(safeHeight * scale));
            return new Size(width, height);
        }

        private static Font CreateUiFont(float size, FontStyle style)
        {
            try
            {
                return new Font("Segoe UI", size, style, GraphicsUnit.Point);
            }
            catch
            {
                return new Font(SystemFonts.MessageBoxFont.FontFamily, size, style, GraphicsUnit.Point);
            }
        }

        private static void StyleButton(Button button, bool primary)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseDownBackColor = primary ? PrimaryButtonHoverColor : SecondaryButtonHoverColor;
            button.FlatAppearance.MouseOverBackColor = primary ? PrimaryButtonHoverColor : SecondaryButtonHoverColor;
            button.UseVisualStyleBackColor = false;
            button.Font = CreateUiFont(9.25f, primary ? FontStyle.Bold : FontStyle.Regular);
            button.Cursor = button.Enabled ? Cursors.Hand : Cursors.Default;
            button.EnabledChanged += delegate { ApplyButtonState(button, primary); };
            ApplyButtonState(button, primary);
        }

        private static void ApplyButtonState(Button button, bool primary)
        {
            button.Cursor = button.Enabled ? Cursors.Hand : Cursors.Default;

            if (primary)
            {
                button.BackColor = button.Enabled ? PrimaryButtonColor : PrimaryButtonDisabledColor;
                button.ForeColor = button.Enabled ? Color.White : PrimaryButtonDisabledTextColor;
                button.FlatAppearance.BorderColor = button.Enabled ? PrimaryButtonColor : PrimaryButtonDisabledColor;
                return;
            }

            button.BackColor = button.Enabled ? PanelBackgroundColor : SecondaryButtonDisabledColor;
            button.ForeColor = button.Enabled ? TitleColor : SecondaryButtonDisabledTextColor;
            button.FlatAppearance.BorderColor = SecondaryButtonBorderColor;
        }

        private static Panel CreateDividerPanel(DockStyle dock)
        {
            return new Panel
            {
                Dock = dock,
                Height = 1,
                BackColor = DividerColor
            };
        }

        private sealed class PageSurface : IDisposable
        {
            private readonly SignatureSelectionBox pictureBox;
            private readonly float pdfWidth;
            private readonly float pdfHeight;
            private readonly PageCardPanel container;
            private readonly int pageNumber;
            private readonly SignatureAppearanceProfile signatureProfile;
            private DateTime previewSignedAt;

            public PageSurface(int pageNumber, float pdfWidth, float pdfHeight, Size renderSize, IReadOnlyList<DetectedSignatureField> detectedFields, SignatureAppearanceProfile signatureProfile)
            {
                this.pageNumber = pageNumber;
                this.pdfWidth = pdfWidth;
                this.pdfHeight = pdfHeight;
                this.signatureProfile = signatureProfile;
                previewSignedAt = DateTime.Now;

                var label = new Label
                {
                    Left = 18,
                    Top = 14,
                    Width = renderSize.Width,
                    Height = 20,
                    Font = CreateUiFont(9.2f, FontStyle.Bold),
                    ForeColor = PageLabelColor,
                    Text = "Pagina " + pageNumber
                };

                pictureBox = new SignatureSelectionBox
                {
                    SizeMode = PictureBoxSizeMode.Normal,
                    BorderStyle = BorderStyle.None,
                    BackColor = Color.White,
                    Width = renderSize.Width,
                    Height = renderSize.Height,
                    Enabled = false,
                    Cursor = Cursors.WaitCursor,
                    PlaceholderText = "Cargando pagina " + pageNumber + "..."
                };
                pictureBox.SelectionChanged += (sender, args) => OnSelectionChanged();
                pictureBox.PreviewData = BuildPreviewData();
                pictureBox.DefaultClickSelectionSize = BuildDefaultSelectionSize();
                pictureBox.SetDetectedFields(BuildDetectedFieldAreas(detectedFields));
                pictureBox.Left = 18;
                pictureBox.Top = 42;

                container = new PageCardPanel
                {
                    Width = renderSize.Width + 36,
                    Height = renderSize.Height + 60,
                    BackColor = PanelBackgroundColor
                };

                container.Controls.Add(label);
                container.Controls.Add(pictureBox);
            }

            public event EventHandler SelectionChanged;

            public Panel Container
            {
                get { return container; }
            }

            public int PageNumber
            {
                get { return pageNumber; }
            }

            public int RenderWidth
            {
                get { return pictureBox.Width; }
            }

            public int RenderHeight
            {
                get { return pictureBox.Height; }
            }

            public bool HasSelection
            {
                get { return pictureBox.Selection.Width > 0 && pictureBox.Selection.Height > 0; }
            }

            public bool UsesDetectedField
            {
                get { return pictureBox.UsesDetectedField; }
            }

            public void SetImage(Image image)
            {
                var previousImage = pictureBox.Image;
                pictureBox.Image = image;
                pictureBox.Enabled = true;
                pictureBox.Cursor = Cursors.Cross;
                pictureBox.PlaceholderText = null;
                pictureBox.Invalidate();

                if (previousImage != null)
                {
                    previousImage.Dispose();
                }
            }

            public void ClearSelection()
            {
                pictureBox.ClearSelection();
            }

            public Rectangle GetSelection()
            {
                return pictureBox.Selection;
            }

            public void RefreshPreviewTimestamp()
            {
                previewSignedAt = DateTime.Now;
                pictureBox.PreviewData = BuildPreviewData();
                pictureBox.Invalidate();
            }

            public SignaturePlacement BuildPlacement(string sourcePath)
            {
                var selection = pictureBox.Selection;
                var scaleX = pdfWidth / pictureBox.ClientSize.Width;
                var scaleY = pdfHeight / pictureBox.ClientSize.Height;

                var left = selection.Left * scaleX;
                var right = selection.Right * scaleX;
                var topFromTop = selection.Top * scaleY;
                var bottomFromTop = selection.Bottom * scaleY;

                return new SignaturePlacement
                {
                    SourcePath = sourcePath,
                    ExistingFieldName = pictureBox.SelectedFieldName,
                    PageNumber = PageNumber,
                    Left = left,
                    Right = right,
                    Bottom = pdfHeight - bottomFromTop,
                    Top = pdfHeight - topFromTop,
                    SignedAt = previewSignedAt
                };
            }

            private SignatureAppearanceData BuildPreviewData()
            {
                return new SignatureAppearanceData(
                    signatureProfile == null ? string.Empty : signatureProfile.SignerName,
                    signatureProfile == null ? string.Empty : signatureProfile.DistinguishedName,
                    signatureProfile == null ? string.Empty : signatureProfile.Reason,
                    previewSignedAt,
                    signatureProfile == null ? null : signatureProfile.GraphicBytes);
            }

            private Size BuildDefaultSelectionSize()
            {
                var width = (int)Math.Round((DefaultClickWidthCm * PdfPointsPerCm / pdfWidth) * pictureBox.Width);
                var height = (int)Math.Round((DefaultClickHeightCm * PdfPointsPerCm / pdfHeight) * pictureBox.Height);
                return new Size(Math.Max(90, width), Math.Max(44, height));
            }

            private List<SignatureSelectionBox.DetectedFieldArea> BuildDetectedFieldAreas(IReadOnlyList<DetectedSignatureField> detectedFields)
            {
                var areas = new List<SignatureSelectionBox.DetectedFieldArea>();
                if (detectedFields == null)
                {
                    return areas;
                }

                foreach (var detectedField in detectedFields)
                {
                    var area = BuildDetectedFieldArea(detectedField);
                    if (area != null)
                    {
                        areas.Add(area);
                    }
                }

                return areas;
            }

            private SignatureSelectionBox.DetectedFieldArea BuildDetectedFieldArea(DetectedSignatureField detectedField)
            {
                var scaleX = pictureBox.Width / pdfWidth;
                var scaleY = pictureBox.Height / pdfHeight;
                var left = (int)Math.Round(detectedField.Left * scaleX);
                var right = (int)Math.Round(detectedField.Right * scaleX);
                var top = (int)Math.Round((pdfHeight - detectedField.Top) * scaleY);
                var bottom = (int)Math.Round((pdfHeight - detectedField.Bottom) * scaleY);
                var bounds = Rectangle.FromLTRB(left, top, right, bottom);
                bounds.Intersect(new Rectangle(Point.Empty, pictureBox.ClientSize));
                if (bounds.Width < 4 || bounds.Height < 4)
                {
                    return null;
                }

                return new SignatureSelectionBox.DetectedFieldArea(detectedField.FieldName, bounds);
            }

            private void OnSelectionChanged()
            {
                var handler = SelectionChanged;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            }

            public void Dispose()
            {
                if (pictureBox.Image != null)
                {
                    pictureBox.Image.Dispose();
                    pictureBox.Image = null;
                }
            }
        }

        private sealed class PageCardPanel : Panel
        {
            public PageCardPanel()
            {
                DoubleBuffered = true;
                ResizeRedraw = true;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                var shadowRect = new Rectangle(3, 4, Math.Max(0, Width - 7), Math.Max(0, Height - 7));
                using (var shadowBrush = new SolidBrush(Color.FromArgb(12, 34, 39, 47)))
                {
                    e.Graphics.FillRectangle(shadowBrush, shadowRect);
                }

                var rect = new Rectangle(0, 0, Math.Max(0, Width - 5), Math.Max(0, Height - 5));
                using (var fillBrush = new SolidBrush(PanelBackgroundColor))
                using (var pen = new Pen(DividerColor))
                {
                    e.Graphics.FillRectangle(fillBrush, rect);
                    e.Graphics.DrawRectangle(pen, rect);
                }
            }
        }
    }
}

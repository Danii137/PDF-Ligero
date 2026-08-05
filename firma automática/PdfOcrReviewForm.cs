using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace FirmaAutomatica
{
    internal sealed class PdfOcrReviewForm : Form
    {
        private static readonly Color PaperColor =
            Color.FromArgb(250, 249, 247);
        private static readonly Color WorkspaceColor =
            Color.FromArgb(234, 233, 230);
        private static readonly Color NavigationColor =
            Color.FromArgb(245, 244, 241);
        private static readonly Color DividerColor =
            Color.FromArgb(211, 209, 204);
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
        private const int PreviewDpi = 110;
        private const int PreviewCacheCapacity = 3;

        private readonly string sourcePath;
        private readonly PdfOcrAnalysis analysis;
        private readonly List<PdfOcrPageInstruction> instructions;
        private readonly Dictionary<int, PdfOcrPageAnalysis> pageByNumber =
            new Dictionary<int, PdfOcrPageAnalysis>();
        private readonly Dictionary<int, PdfOcrPageInstruction>
            instructionByPage =
                new Dictionary<int, PdfOcrPageInstruction>();

        private readonly ListView pageList;
        private readonly Label summaryLabel;
        private readonly Label activePageLabel;
        private readonly Label activeStateLabel;
        private readonly Label activeNoteLabel;
        private readonly PictureBox previewPictureBox;
        private readonly Label previewStatusLabel;
        private readonly CheckBox includePageCheckBox;
        private readonly Button rotateLeftButton;
        private readonly Button rotateRightButton;
        private readonly Label rotationValueLabel;
        private readonly CheckBox deskewCheckBox;
        private readonly Label deskewUnavailableLabel;
        private readonly Button applyButton;
        private readonly Button cancelButton;

        private readonly object previewSync = new object();
        private readonly Dictionary<string, PreviewCacheEntry> previewCache =
            new Dictionary<string, PreviewCacheEntry>(
                StringComparer.Ordinal);
        private readonly LinkedList<string> previewLru =
            new LinkedList<string>();

        private PreviewRequest pendingPreviewRequest;
        private CancellationTokenSource runningPreviewCancellation;
        private bool previewPumpRunning;
        private int previewGeneration;
        private bool updatingPageControls;
        private bool closing;

        public PdfOcrReviewForm(
            string sourcePath,
            PdfOcrAnalysis analysis)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                throw new ArgumentException(
                    "Indica el PDF que se va a revisar.",
                    "sourcePath");
            }
            if (analysis == null)
            {
                throw new ArgumentNullException("analysis");
            }
            if (analysis.PageCount < 1)
            {
                throw new ArgumentException(
                    "El análisis no contiene páginas.",
                    "analysis");
            }

            this.sourcePath = Path.GetFullPath(sourcePath);
            this.analysis = analysis;
            instructions = PdfOcrService
                .CreateDefaultInstructions(analysis)
                .Select(CloneInstruction)
                .ToList();
            IndexAnalysis();

            Text = "Revisión OCR - PDF Ligero";
            AppBranding.ApplyWindowIcon(this);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(980, 690);
            MinimumSize = new Size(820, 600);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = WorkspaceColor;
            Font = CreateUiFont(9.25f, FontStyle.Regular);
            KeyPreview = true;

            var headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 68,
                BackColor = PaperColor
            };
            headerPanel.Controls.Add(new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = DividerColor
            });

            var eyebrowLabel = new Label
            {
                Left = 20,
                Top = 7,
                Width = 430,
                Height = 15,
                Text = "REVISIÓN OCR / " +
                    analysis.PageCount.ToString(
                        CultureInfo.CurrentCulture) +
                    " PÁGINAS",
                ForeColor = AccentTextColor,
                Font = CreateArchitecturalFont(7.5f, true),
                TextAlign = ContentAlignment.MiddleLeft
            };
            var titleLabel = new Label
            {
                Left = 20,
                Top = 23,
                Width = 720,
                Height = 28,
                Text = Path.GetFileName(this.sourcePath),
                ForeColor = TitleColor,
                Font = CreateArchitecturalFont(12.25f, false),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
            var accentLine = new Panel
            {
                Left = 20,
                Top = 56,
                Width = 42,
                Height = 2,
                BackColor = AccentColor
            };
            var headerHintLabel = new Label
            {
                Anchor = AnchorStyles.Top,
                Left = 707,
                Top = 15,
                Width = 250,
                Height = 36,
                Text = "PREVIEW LOCAL · 110 DPI\r\nsin ejecutar reconocimiento",
                ForeColor = MutedColor,
                Font = CreateArchitecturalFont(7.25f, false),
                TextAlign = ContentAlignment.MiddleRight
            };

            headerPanel.Controls.Add(eyebrowLabel);
            headerPanel.Controls.Add(titleLabel);
            headerPanel.Controls.Add(accentLine);
            headerPanel.Controls.Add(headerHintLabel);
            headerPanel.Resize += delegate
            {
                headerHintLabel.Left = Math.Max(
                    470,
                    headerPanel.ClientSize.Width -
                    headerHintLabel.Width - 20);
                titleLabel.Width = Math.Max(
                    300,
                    headerHintLabel.Left -
                    titleLabel.Left - 24);
            };

            var footerPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 64,
                BackColor = PaperColor
            };
            footerPanel.Controls.Add(new Panel
            {
                Dock = DockStyle.Top,
                Height = 1,
                BackColor = DividerColor
            });

            summaryLabel = new Label
            {
                Left = 20,
                Top = 13,
                Width = 520,
                Height = 36,
                ForeColor = BodyColor,
                Font = CreateArchitecturalFont(8.25f, false),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                AccessibleName = "Resumen de páginas que se aplicarán"
            };

            cancelButton = CreateActionButton("Cancelar", false);
            cancelButton.Anchor = AnchorStyles.Top;
            cancelButton.Left = 756;
            cancelButton.Top = 15;
            cancelButton.Width = 96;
            cancelButton.DialogResult = DialogResult.Cancel;
            cancelButton.AccessibleName = "Cancelar revisión OCR";

            applyButton = CreateActionButton("Aplicar", true);
            applyButton.Anchor = AnchorStyles.Top;
            applyButton.Left = 862;
            applyButton.Top = 15;
            applyButton.Width = 96;
            applyButton.AccessibleName =
                "Aplicar el plan OCR revisado";
            applyButton.Click += ApplyButton_Click;

            footerPanel.Controls.Add(summaryLabel);
            footerPanel.Controls.Add(cancelButton);
            footerPanel.Controls.Add(applyButton);
            footerPanel.Resize += delegate
            {
                applyButton.Left = Math.Max(
                    20,
                    footerPanel.ClientSize.Width -
                    applyButton.Width - 22);
                cancelButton.Left = Math.Max(
                    20,
                    applyButton.Left -
                    cancelButton.Width - 10);
                summaryLabel.Width = Math.Max(
                    180,
                    cancelButton.Left -
                    summaryLabel.Left - 24);
            };

            var navigationPanel = new Panel
            {
                Dock = DockStyle.Left,
                Width = 238,
                BackColor = NavigationColor,
                Padding = new Padding(10, 10, 9, 10)
            };
            navigationPanel.Controls.Add(new Panel
            {
                Dock = DockStyle.Right,
                Width = 1,
                BackColor = DividerColor
            });

            var listCaptionLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 24,
                Text = "PÁGINAS / ESTADO",
                ForeColor = AccentTextColor,
                Font = CreateArchitecturalFont(7.25f, true),
                TextAlign = ContentAlignment.MiddleLeft
            };
            var legendLabel = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 47,
                Text =
                    "OCR  procesable\r\n" +
                    "TEXTO  ya buscable   ·   FUERA  no seleccionada",
                ForeColor = MutedColor,
                Font = CreateArchitecturalFont(7.1f, false),
                TextAlign = ContentAlignment.MiddleLeft
            };

            pageList = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                HideSelection = false,
                BorderStyle = BorderStyle.None,
                BackColor = NavigationColor,
                ForeColor = TitleColor,
                Font = CreateUiFont(8.8f, FontStyle.Regular),
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                AccessibleName = "Páginas analizadas"
            };
            pageList.Columns.Add("PÁG.", 48, HorizontalAlignment.Left);
            pageList.Columns.Add("ESTADO", 68, HorizontalAlignment.Left);
            pageList.Columns.Add("AJUSTE", 88, HorizontalAlignment.Left);
            pageList.SelectedIndexChanged +=
                PageList_SelectedIndexChanged;

            navigationPanel.Controls.Add(pageList);
            navigationPanel.Controls.Add(legendLabel);
            navigationPanel.Controls.Add(listCaptionLabel);

            var reviewPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = WorkspaceColor,
                Padding = new Padding(14, 12, 14, 12)
            };

            var pageHeaderPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 54,
                BackColor = PaperColor
            };
            pageHeaderPanel.Controls.Add(new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = DividerColor
            });
            activePageLabel = new Label
            {
                Left = 14,
                Top = 7,
                Width = 210,
                Height = 22,
                Text = "Página —",
                ForeColor = TitleColor,
                Font = CreateArchitecturalFont(10.5f, false),
                TextAlign = ContentAlignment.MiddleLeft
            };
            activeStateLabel = new Label
            {
                Left = 14,
                Top = 29,
                Width = 210,
                Height = 16,
                Text = "—",
                ForeColor = AccentTextColor,
                Font = CreateArchitecturalFont(7.25f, true),
                TextAlign = ContentAlignment.MiddleLeft
            };
            activeNoteLabel = new Label
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                Left = 238,
                Top = 8,
                Width = 472,
                Height = 36,
                ForeColor = MutedColor,
                Font = CreateUiFont(8.25f, FontStyle.Regular),
                TextAlign = ContentAlignment.MiddleRight,
                AutoEllipsis = true
            };
            pageHeaderPanel.Controls.Add(activePageLabel);
            pageHeaderPanel.Controls.Add(activeStateLabel);
            pageHeaderPanel.Controls.Add(activeNoteLabel);
            pageHeaderPanel.Resize += delegate
            {
                activeNoteLabel.Width = Math.Max(
                    120,
                    pageHeaderPanel.ClientSize.Width -
                    activeNoteLabel.Left - 14);
            };

            var adjustmentPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 102,
                BackColor = PaperColor
            };
            adjustmentPanel.Controls.Add(new Panel
            {
                Dock = DockStyle.Top,
                Height = 1,
                BackColor = DividerColor
            });
            var adjustmentCaptionLabel = new Label
            {
                Left = 14,
                Top = 9,
                Width = 180,
                Height = 16,
                Text = "AJUSTES DE ESTA PÁGINA",
                ForeColor = AccentTextColor,
                Font = CreateArchitecturalFont(7.25f, true),
                TextAlign = ContentAlignment.MiddleLeft
            };

            includePageCheckBox = new CheckBox
            {
                Left = 14,
                Top = 34,
                Width = 176,
                Height = 28,
                Text = "&Incluir en el OCR",
                ForeColor = TitleColor,
                BackColor = PaperColor,
                Font = CreateUiFont(9f, FontStyle.Regular),
                UseVisualStyleBackColor = false,
                AccessibleDescription =
                    "Incluye o excluye esta página del procesamiento."
            };
            includePageCheckBox.CheckedChanged +=
                IncludePageCheckBox_CheckedChanged;

            rotateLeftButton = CreateAdjustmentButton(
                "−90°",
                "Girar 90 grados a la izquierda");
            rotateLeftButton.Left = 204;
            rotateLeftButton.Top = 31;
            rotateLeftButton.Click += delegate
            {
                RotateActiveInstruction(-90);
            };
            rotateRightButton = CreateAdjustmentButton(
                "+90°",
                "Girar 90 grados a la derecha");
            rotateRightButton.Left = 272;
            rotateRightButton.Top = 31;
            rotateRightButton.Click += delegate
            {
                RotateActiveInstruction(90);
            };
            rotationValueLabel = new Label
            {
                Left = 204,
                Top = 67,
                Width = 136,
                Height = 20,
                Text = "Giro · 0°",
                ForeColor = MutedColor,
                Font = CreateArchitecturalFont(7.5f, false),
                TextAlign = ContentAlignment.MiddleCenter
            };

            deskewCheckBox = new CheckBox
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                Left = 352,
                Top = 31,
                Width = 351,
                Height = 28,
                ForeColor = TitleColor,
                BackColor = PaperColor,
                Font = CreateUiFont(9f, FontStyle.Regular),
                UseVisualStyleBackColor = false,
                AccessibleName = "Aplicar enderezado detectado"
            };
            deskewCheckBox.CheckedChanged +=
                DeskewCheckBox_CheckedChanged;
            deskewUnavailableLabel = new Label
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                Left = 352,
                Top = 62,
                Width = 351,
                Height = 22,
                ForeColor = MutedColor,
                Font = CreateArchitecturalFont(7.25f, false),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };

            adjustmentPanel.Controls.Add(adjustmentCaptionLabel);
            adjustmentPanel.Controls.Add(includePageCheckBox);
            adjustmentPanel.Controls.Add(rotateLeftButton);
            adjustmentPanel.Controls.Add(rotateRightButton);
            adjustmentPanel.Controls.Add(rotationValueLabel);
            adjustmentPanel.Controls.Add(deskewCheckBox);
            adjustmentPanel.Controls.Add(deskewUnavailableLabel);
            adjustmentPanel.Resize += delegate
            {
                var availableWidth = Math.Max(
                    120,
                    adjustmentPanel.ClientSize.Width -
                    deskewCheckBox.Left - 14);
                deskewCheckBox.Width = availableWidth;
                deskewUnavailableLabel.Width =
                    availableWidth;
            };

            var previewPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = WorkspaceColor,
                Padding = new Padding(18)
            };
            previewPictureBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(225, 224, 221),
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.FixedSingle,
                AccessibleName = "Vista previa de la página activa"
            };
            previewStatusLabel = new Label
            {
                AutoSize = false,
                Width = 260,
                Height = 48,
                Text = "Selecciona una página",
                ForeColor = MutedColor,
                BackColor = Color.Transparent,
                Font = CreateArchitecturalFont(8.25f, false),
                TextAlign = ContentAlignment.MiddleCenter,
                AccessibleName = "Estado de la vista previa OCR"
            };
            previewPanel.Controls.Add(previewPictureBox);
            previewPanel.Controls.Add(previewStatusLabel);
            previewStatusLabel.BringToFront();
            previewPanel.Resize += delegate
            {
                CenterPreviewStatus(previewPanel);
            };

            reviewPanel.Controls.Add(previewPanel);
            reviewPanel.Controls.Add(adjustmentPanel);
            reviewPanel.Controls.Add(pageHeaderPanel);

            var contentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = WorkspaceColor
            };
            contentPanel.Controls.Add(reviewPanel);
            contentPanel.Controls.Add(navigationPanel);

            Controls.Add(contentPanel);
            Controls.Add(footerPanel);
            Controls.Add(headerPanel);

            AcceptButton = applyButton;
            CancelButton = cancelButton;

            PopulatePageList();
            RefreshApplyAvailability();
            Shown += PdfOcrReviewForm_Shown;
            FormClosing += PdfOcrReviewForm_FormClosing;
        }

        public IList<PdfOcrPageInstruction> Instructions
        {
            get
            {
                return instructions
                    .OrderBy(instruction => instruction.PageNumber)
                    .Select(CloneInstruction)
                    .ToList();
            }
        }

        private void IndexAnalysis()
        {
            foreach (var page in analysis.Pages)
            {
                if (page == null ||
                    page.PageNumber < 1 ||
                    page.PageNumber > analysis.PageCount ||
                    pageByNumber.ContainsKey(page.PageNumber))
                {
                    throw new ArgumentException(
                        "El análisis contiene páginas no válidas.",
                        "analysis");
                }

                pageByNumber.Add(page.PageNumber, page);
            }

            foreach (var instruction in instructions)
            {
                if (instruction == null ||
                    instruction.PageNumber < 1 ||
                    instruction.PageNumber > analysis.PageCount ||
                    instructionByPage.ContainsKey(
                        instruction.PageNumber))
                {
                    throw new ArgumentException(
                        "El plan OCR contiene páginas no válidas.",
                        "analysis");
                }

                PdfOcrPageAnalysis page;
                if (!pageByNumber.TryGetValue(
                        instruction.PageNumber,
                        out page))
                {
                    throw new ArgumentException(
                        "El plan OCR no coincide con el análisis.",
                        "analysis");
                }

                if (!page.NeedsOcr)
                {
                    instruction.Process = false;
                    instruction.ClockwiseRotationDegrees = 0;
                    instruction.ApplyDeskew = false;
                    instruction.DeskewDegrees = 0F;
                }

                instructionByPage.Add(
                    instruction.PageNumber,
                    instruction);
            }

            if (pageByNumber.Count != analysis.PageCount ||
                instructionByPage.Count != analysis.PageCount)
            {
                throw new ArgumentException(
                    "El análisis no describe todas las páginas del PDF.",
                    "analysis");
            }
        }

        private void PopulatePageList()
        {
            pageList.BeginUpdate();
            try
            {
                pageList.Items.Clear();
                for (var pageNumber = 1;
                    pageNumber <= analysis.PageCount;
                    pageNumber++)
                {
                    var page = pageByNumber[pageNumber];
                    var instruction =
                        instructionByPage[pageNumber];
                    var state = GetPageState(page);
                    var item = new ListViewItem(
                        pageNumber.ToString(
                            CultureInfo.CurrentCulture))
                    {
                        Tag = pageNumber,
                        UseItemStyleForSubItems = false
                    };
                    item.SubItems.Add(state);
                    item.SubItems.Add(
                        DescribeAdjustment(page, instruction));
                    StylePageListItem(item, state);
                    pageList.Items.Add(item);
                }
            }
            finally
            {
                pageList.EndUpdate();
            }
        }

        private void PdfOcrReviewForm_Shown(
            object sender,
            EventArgs e)
        {
            var firstProcessable = analysis.Pages
                .FirstOrDefault(page => page.NeedsOcr);
            var initialPage = firstProcessable == null
                ? 1
                : firstProcessable.PageNumber;
            if (initialPage >= 1 &&
                initialPage <= pageList.Items.Count)
            {
                pageList.Items[initialPage - 1].Selected = true;
                pageList.Items[initialPage - 1].Focused = true;
                pageList.EnsureVisible(initialPage - 1);
                pageList.Focus();
            }

            CenterPreviewStatus(
                previewPictureBox.Parent as Panel);
        }

        private void PageList_SelectedIndexChanged(
            object sender,
            EventArgs e)
        {
            var pageNumber = GetActivePageNumber();
            if (pageNumber < 1)
            {
                return;
            }

            RefreshActivePageControls(pageNumber);
            QueueActivePreview();
        }

        private void RefreshActivePageControls(int pageNumber)
        {
            PdfOcrPageAnalysis page;
            PdfOcrPageInstruction instruction;
            if (!pageByNumber.TryGetValue(pageNumber, out page) ||
                !instructionByPage.TryGetValue(
                    pageNumber,
                    out instruction))
            {
                return;
            }

            updatingPageControls = true;
            try
            {
                var state = GetPageState(page);
                activePageLabel.Text =
                    "Página " +
                    pageNumber.ToString(
                        CultureInfo.CurrentCulture);
                activeStateLabel.Text = state;
                activeStateLabel.ForeColor = state == "OCR"
                    ? AccentTextColor
                    : state == "TEXTO"
                        ? BodyColor
                        : MutedColor;
                activeNoteLabel.Text = string.IsNullOrWhiteSpace(
                        page.Note)
                    ? GetDefaultPageNote(page)
                    : page.Note;

                includePageCheckBox.Enabled = page.NeedsOcr;
                includePageCheckBox.Checked =
                    page.NeedsOcr && instruction.Process;
                includePageCheckBox.Text = page.NeedsOcr
                    ? "&Incluir en el OCR"
                    : page.Selected
                        ? "Página con texto útil"
                        : "Página fuera del ámbito";

                var adjustmentsEnabled =
                    page.NeedsOcr && instruction.Process;
                rotateLeftButton.Enabled = adjustmentsEnabled;
                rotateRightButton.Enabled = adjustmentsEnabled;
                rotationValueLabel.Enabled = adjustmentsEnabled;
                rotationValueLabel.Text =
                    "Giro · " +
                    NormalizeRightAngle(
                        instruction
                            .ClockwiseRotationDegrees)
                        .ToString(
                            CultureInfo.CurrentCulture) +
                    "°";

                var deskewDetected = page.WillDeskew;
                deskewCheckBox.Visible = deskewDetected;
                deskewCheckBox.Enabled =
                    adjustmentsEnabled && deskewDetected;
                deskewCheckBox.Checked =
                    deskewDetected &&
                    instruction.ApplyDeskew;
                deskewCheckBox.Text =
                    "Enderezar " +
                    page.SuggestedDeskewDegrees.ToString(
                        "+0.0;-0.0;0.0",
                        CultureInfo.CurrentCulture) +
                    "° detectados";

                deskewUnavailableLabel.Visible =
                    !deskewDetected;
                deskewUnavailableLabel.Text =
                    page.NeedsOcr
                        ? "Sin inclinación leve aplicable."
                        : "Los ajustes solo están disponibles " +
                          "en páginas OCR.";
            }
            finally
            {
                updatingPageControls = false;
            }
        }

        private void IncludePageCheckBox_CheckedChanged(
            object sender,
            EventArgs e)
        {
            if (updatingPageControls)
            {
                return;
            }

            var pageNumber = GetActivePageNumber();
            PdfOcrPageAnalysis page;
            PdfOcrPageInstruction instruction;
            if (pageNumber < 1 ||
                !pageByNumber.TryGetValue(pageNumber, out page) ||
                !instructionByPage.TryGetValue(
                    pageNumber,
                    out instruction) ||
                !page.NeedsOcr)
            {
                return;
            }

            instruction.Process =
                includePageCheckBox.Checked;
            RefreshPageRow(pageNumber);
            RefreshActivePageControls(pageNumber);
            RefreshApplyAvailability();
            QueueActivePreview();
        }

        private void DeskewCheckBox_CheckedChanged(
            object sender,
            EventArgs e)
        {
            if (updatingPageControls)
            {
                return;
            }

            var pageNumber = GetActivePageNumber();
            PdfOcrPageAnalysis page;
            PdfOcrPageInstruction instruction;
            if (pageNumber < 1 ||
                !pageByNumber.TryGetValue(pageNumber, out page) ||
                !instructionByPage.TryGetValue(
                    pageNumber,
                    out instruction) ||
                !page.NeedsOcr ||
                !instruction.Process ||
                !page.WillDeskew)
            {
                return;
            }

            instruction.ApplyDeskew =
                deskewCheckBox.Checked;
            RefreshPageRow(pageNumber);
            QueueActivePreview();
        }

        private void RotateActiveInstruction(int deltaDegrees)
        {
            var pageNumber = GetActivePageNumber();
            PdfOcrPageAnalysis page;
            PdfOcrPageInstruction instruction;
            if (pageNumber < 1 ||
                !pageByNumber.TryGetValue(pageNumber, out page) ||
                !instructionByPage.TryGetValue(
                    pageNumber,
                    out instruction) ||
                !page.NeedsOcr ||
                !instruction.Process)
            {
                return;
            }

            instruction.ClockwiseRotationDegrees =
                NormalizeRightAngle(
                    instruction.ClockwiseRotationDegrees +
                    deltaDegrees);
            RefreshPageRow(pageNumber);
            RefreshActivePageControls(pageNumber);
            QueueActivePreview();
        }

        private void RefreshPageRow(int pageNumber)
        {
            if (pageNumber < 1 ||
                pageNumber > pageList.Items.Count)
            {
                return;
            }

            var page = pageByNumber[pageNumber];
            var instruction =
                instructionByPage[pageNumber];
            pageList.Items[pageNumber - 1]
                .SubItems[2].Text =
                    DescribeAdjustment(page, instruction);
        }

        private void RefreshApplyAvailability()
        {
            var processCount =
                instructions.Count(instruction =>
                    instruction.Process);
            applyButton.Enabled = processCount > 0;
            summaryLabel.Text = processCount == 0
                ? "No hay páginas incluidas. Selecciona al menos " +
                  "una página OCR para aplicar."
                : processCount == 1
                    ? "1 página se procesará · el original " +
                      "permanecerá intacto"
                    : processCount.ToString(
                        CultureInfo.CurrentCulture) +
                      " páginas se procesarán · el original " +
                      "permanecerá intacto";
        }

        private void ApplyButton_Click(object sender, EventArgs e)
        {
            if (!instructions.Any(instruction =>
                    instruction.Process))
            {
                RefreshApplyAvailability();
                System.Media.SystemSounds.Beep.Play();
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        private void QueueActivePreview()
        {
            var pageNumber = GetActivePageNumber();
            PdfOcrPageInstruction instruction;
            PdfOcrPageAnalysis page;
            if (pageNumber < 1 ||
                !instructionByPage.TryGetValue(
                    pageNumber,
                    out instruction) ||
                !pageByNumber.TryGetValue(pageNumber, out page))
            {
                return;
            }

            var previewInstruction =
                CreatePreviewInstruction(page, instruction);
            var key = CreatePreviewKey(previewInstruction);
            Image cachedImage;
            if (TryGetCachedPreview(key, out cachedImage))
            {
                CancelRunningPreview();
                previewGeneration++;
                ShowPreviewImage(cachedImage);
                return;
            }

            var request = new PreviewRequest(
                ++previewGeneration,
                key,
                previewInstruction);
            previewPictureBox.Image = null;
            ShowPreviewStatus("Preparando vista previa…");

            lock (previewSync)
            {
                pendingPreviewRequest = request;
                if (runningPreviewCancellation != null)
                {
                    try
                    {
                        runningPreviewCancellation.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                }

                if (!previewPumpRunning)
                {
                    previewPumpRunning = true;
                    ThreadPool.QueueUserWorkItem(
                        PreviewPump);
                }
            }
        }

        private void PreviewPump(object state)
        {
            while (true)
            {
                PreviewRequest request;
                CancellationTokenSource cancellation;
                lock (previewSync)
                {
                    request = pendingPreviewRequest;
                    pendingPreviewRequest = null;
                    if (request == null || closing)
                    {
                        previewPumpRunning = false;
                        runningPreviewCancellation = null;
                        return;
                    }

                    cancellation =
                        new CancellationTokenSource();
                    runningPreviewCancellation = cancellation;
                }

                byte[] pngBytes = null;
                Exception error = null;
                try
                {
                    pngBytes =
                        PdfOcrService.RenderPreviewPng(
                            sourcePath,
                            request.Instruction,
                            PreviewDpi,
                            cancellation.Token);
                    cancellation.Token
                        .ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    lock (previewSync)
                    {
                        if (ReferenceEquals(
                                runningPreviewCancellation,
                                cancellation))
                        {
                            runningPreviewCancellation = null;
                        }
                    }
                    cancellation.Dispose();
                }

                if (!closing &&
                    (pngBytes != null || error != null))
                {
                    try
                    {
                        BeginInvoke(
                            new Action<PreviewRequest, byte[], Exception>(
                                CompletePreview),
                            request,
                            pngBytes,
                            error);
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }
            }
        }

        private void CompletePreview(
            PreviewRequest request,
            byte[] pngBytes,
            Exception error)
        {
            if (closing ||
                request == null ||
                request.Generation != previewGeneration)
            {
                return;
            }

            if (error != null)
            {
                AppLog.Write(
                    "No se pudo crear la vista previa OCR: " +
                    error);
                previewPictureBox.Image = null;
                ShowPreviewStatus(
                    "No se pudo preparar esta página.\r\n" +
                    error.GetBaseException().Message);
                return;
            }

            Image image = null;
            try
            {
                using (var stream =
                    new MemoryStream(pngBytes, false))
                using (var decoded =
                    Image.FromStream(stream))
                {
                    image = new Bitmap(decoded);
                }

                AddCachedPreview(request.Key, image);
                image = null;

                Image cached;
                if (TryGetCachedPreview(
                        request.Key,
                        out cached))
                {
                    ShowPreviewImage(cached);
                }
            }
            catch (Exception ex)
            {
                if (image != null)
                {
                    image.Dispose();
                }

                AppLog.Write(
                    "La imagen de vista previa OCR no era válida: " +
                    ex);
                previewPictureBox.Image = null;
                ShowPreviewStatus(
                    "La vista previa no se pudo mostrar.");
            }
        }

        private bool TryGetCachedPreview(
            string key,
            out Image image)
        {
            PreviewCacheEntry entry;
            if (!previewCache.TryGetValue(key, out entry))
            {
                image = null;
                return false;
            }

            previewLru.Remove(entry.Node);
            previewLru.AddFirst(entry.Node);
            image = entry.Image;
            return true;
        }

        private void AddCachedPreview(
            string key,
            Image image)
        {
            PreviewCacheEntry existing;
            if (previewCache.TryGetValue(key, out existing))
            {
                image.Dispose();
                previewLru.Remove(existing.Node);
                previewLru.AddFirst(existing.Node);
                return;
            }

            var node = previewLru.AddFirst(key);
            previewCache.Add(
                key,
                new PreviewCacheEntry(image, node));

            while (previewCache.Count >
                PreviewCacheCapacity)
            {
                var lastNode = previewLru.Last;
                if (lastNode == null)
                {
                    break;
                }

                PreviewCacheEntry removed;
                previewLru.RemoveLast();
                if (!previewCache.TryGetValue(
                        lastNode.Value,
                        out removed))
                {
                    continue;
                }

                previewCache.Remove(lastNode.Value);
                if (ReferenceEquals(
                        previewPictureBox.Image,
                        removed.Image))
                {
                    previewPictureBox.Image = null;
                }
                removed.Image.Dispose();
            }
        }

        private void ShowPreviewImage(Image image)
        {
            previewPictureBox.Image = image;
            previewStatusLabel.Visible = false;
        }

        private void ShowPreviewStatus(string text)
        {
            previewStatusLabel.Text = text;
            previewStatusLabel.Visible = true;
            previewStatusLabel.BringToFront();
            CenterPreviewStatus(
                previewPictureBox.Parent as Panel);
        }

        private void CancelRunningPreview()
        {
            lock (previewSync)
            {
                pendingPreviewRequest = null;
                if (runningPreviewCancellation != null)
                {
                    try
                    {
                        runningPreviewCancellation.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                }
            }
        }

        private void PdfOcrReviewForm_FormClosing(
            object sender,
            FormClosingEventArgs e)
        {
            closing = true;
            CancelRunningPreview();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                closing = true;
                CancelRunningPreview();
                previewPictureBox.Image = null;
                foreach (var entry in previewCache.Values)
                {
                    entry.Image.Dispose();
                }
                previewCache.Clear();
                previewLru.Clear();
            }

            base.Dispose(disposing);
        }

        private int GetActivePageNumber()
        {
            if (pageList.SelectedItems.Count == 0 ||
                !(pageList.SelectedItems[0].Tag is int))
            {
                return -1;
            }

            return (int)pageList.SelectedItems[0].Tag;
        }

        private static string GetPageState(
            PdfOcrPageAnalysis page)
        {
            if (!page.Selected)
            {
                return "FUERA";
            }

            return page.NeedsOcr ? "OCR" : "TEXTO";
        }

        private static string GetDefaultPageNote(
            PdfOcrPageAnalysis page)
        {
            if (!page.Selected)
            {
                return "Esta página queda fuera del ámbito elegido.";
            }
            if (!page.NeedsOcr)
            {
                return "La página ya contiene texto buscable útil.";
            }
            return "Página preparada para reconocimiento local.";
        }

        private static string DescribeAdjustment(
            PdfOcrPageAnalysis page,
            PdfOcrPageInstruction instruction)
        {
            if (!page.NeedsOcr)
            {
                return "—";
            }
            if (!instruction.Process)
            {
                return "EXCLUIDA";
            }

            var parts = new List<string>();
            var rotation = NormalizeRightAngle(
                instruction.ClockwiseRotationDegrees);
            if (rotation != 0)
            {
                parts.Add(rotation.ToString(
                    CultureInfo.CurrentCulture) + "°");
            }
            if (instruction.ApplyDeskew &&
                Math.Abs(instruction.DeskewDegrees) >= 0.35F)
            {
                parts.Add("ALINEAR");
            }

            return parts.Count == 0
                ? "INCLUIR"
                : string.Join(" · ", parts.ToArray());
        }

        private static void StylePageListItem(
            ListViewItem item,
            string state)
        {
            item.ForeColor = state == "OCR"
                ? TitleColor
                : state == "TEXTO"
                    ? BodyColor
                    : MutedColor;
            item.SubItems[1].ForeColor = state == "OCR"
                ? AccentTextColor
                : item.ForeColor;
            item.SubItems[2].ForeColor = MutedColor;
        }

        private static PdfOcrPageInstruction
            CreatePreviewInstruction(
                PdfOcrPageAnalysis page,
                PdfOcrPageInstruction instruction)
        {
            if (!page.NeedsOcr ||
                !instruction.Process)
            {
                return new PdfOcrPageInstruction(
                    instruction.PageNumber,
                    false,
                    0,
                    false,
                    0F);
            }

            return CloneInstruction(instruction);
        }

        private static PdfOcrPageInstruction CloneInstruction(
            PdfOcrPageInstruction instruction)
        {
            return new PdfOcrPageInstruction(
                instruction.PageNumber,
                instruction.Process,
                instruction.ClockwiseRotationDegrees,
                instruction.ApplyDeskew,
                instruction.DeskewDegrees);
        }

        private static int NormalizeRightAngle(int degrees)
        {
            var normalized = degrees % 360;
            if (normalized < 0)
            {
                normalized += 360;
            }
            return normalized;
        }

        private static string CreatePreviewKey(
            PdfOcrPageInstruction instruction)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}|{1}|{2}|{3:R}",
                instruction.PageNumber,
                NormalizeRightAngle(
                    instruction.ClockwiseRotationDegrees),
                instruction.ApplyDeskew ? 1 : 0,
                instruction.ApplyDeskew
                    ? instruction.DeskewDegrees
                    : 0F);
        }

        private static void CenterPreviewStatus(Panel previewPanel)
        {
            if (previewPanel == null ||
                previewPanel.Controls.Count == 0)
            {
                return;
            }

            var status = previewPanel.Controls
                .OfType<Label>()
                .FirstOrDefault(label =>
                    label.AccessibleName ==
                    "Estado de la vista previa OCR");
            if (status == null)
            {
                status = previewPanel.Controls
                    .OfType<Label>()
                    .FirstOrDefault();
            }
            if (status == null)
            {
                return;
            }

            status.Left = Math.Max(
                0,
                (previewPanel.ClientSize.Width -
                 status.Width) / 2);
            status.Top = Math.Max(
                0,
                (previewPanel.ClientSize.Height -
                 status.Height) / 2);
        }

        private static Button CreateAdjustmentButton(
            string text,
            string accessibleName)
        {
            var button = new Button
            {
                Width = 60,
                Height = 32,
                Text = text,
                AccessibleName = accessibleName,
                FlatStyle = FlatStyle.Flat,
                BackColor = PaperColor,
                ForeColor = TitleColor,
                Cursor = Cursors.Hand,
                Font = CreateArchitecturalFont(8.75f, true)
            };
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = DividerColor;
            button.FlatAppearance.MouseOverBackColor =
                AccentTintColor;
            button.FlatAppearance.MouseDownBackColor =
                DividerColor;
            return button;
        }

        private static Button CreateActionButton(
            string text,
            bool primary)
        {
            var button = new Button
            {
                Width = 96,
                Height = 34,
                Text = text,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Font = CreateArchitecturalFont(9f, true),
                BackColor = primary ? TitleColor : PaperColor,
                ForeColor = primary ? Color.White : TitleColor
            };
            button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.BorderColor = DividerColor;
            button.FlatAppearance.MouseOverBackColor = primary
                ? Color.FromArgb(57, 58, 54)
                : AccentTintColor;
            button.FlatAppearance.MouseDownBackColor = primary
                ? Color.FromArgb(57, 58, 54)
                : DividerColor;
            return button;
        }

        private static Font CreateArchitecturalFont(
            float size,
            bool semibold)
        {
            var style = semibold
                ? FontStyle.Bold
                : FontStyle.Regular;
            try
            {
                return new Font(
                    semibold
                        ? "Bahnschrift SemiCondensed"
                        : "Bahnschrift Light SemiCondensed",
                    size,
                    style,
                    GraphicsUnit.Point);
            }
            catch
            {
                return CreateUiFont(size, style);
            }
        }

        private static Font CreateUiFont(
            float size,
            FontStyle style)
        {
            try
            {
                return new Font(
                    "Segoe UI Variable Text",
                    size,
                    style,
                    GraphicsUnit.Point);
            }
            catch
            {
                return new Font(
                    "Segoe UI",
                    size,
                    style,
                    GraphicsUnit.Point);
            }
        }

        private sealed class PreviewRequest
        {
            public PreviewRequest(
                int generation,
                string key,
                PdfOcrPageInstruction instruction)
            {
                Generation = generation;
                Key = key;
                Instruction = instruction;
            }

            public int Generation { get; private set; }

            public string Key { get; private set; }

            public PdfOcrPageInstruction Instruction
            {
                get;
                private set;
            }
        }

        private sealed class PreviewCacheEntry
        {
            public PreviewCacheEntry(
                Image image,
                LinkedListNode<string> node)
            {
                Image = image;
                Node = node;
            }

            public Image Image { get; private set; }

            public LinkedListNode<string> Node
            {
                get;
                private set;
            }
        }
    }
}

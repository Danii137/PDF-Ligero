using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace FirmaAutomatica
{
    internal sealed class PdfPlanComparisonSource
    {
        public PdfPlanComparisonSource(
            string displayName,
            string path,
            int initialPageIndex)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException(
                    "La ruta del PDF es obligatoria.",
                    "path");
            }

            Path = System.IO.Path.GetFullPath(path);
            DisplayName = string.IsNullOrWhiteSpace(displayName)
                ? System.IO.Path.GetFileName(Path)
                : displayName.Trim();
            InitialPageIndex = Math.Max(0, initialPageIndex);
        }

        public string DisplayName { get; private set; }

        public string Path { get; private set; }

        public int InitialPageIndex { get; private set; }

        public override string ToString()
        {
            return DisplayName;
        }

        internal PdfPlanComparisonSource Clone()
        {
            return new PdfPlanComparisonSource(
                DisplayName,
                Path,
                InitialPageIndex);
        }
    }

    internal sealed class PdfPlanComparisonStatusEventArgs : EventArgs
    {
        public PdfPlanComparisonStatusEventArgs(
            string headerTitle,
            string headerDetail)
        {
            HeaderTitle = headerTitle ?? string.Empty;
            HeaderDetail = headerDetail ?? string.Empty;
        }

        public string HeaderTitle { get; private set; }

        public string HeaderDetail { get; private set; }
    }

    /// <summary>
    /// Superficie temporal de comparación. No abre ningún PDF hasta Begin y
    /// mantiene toda la carga de PDFium en un único hilo de trabajo.
    /// </summary>
    internal sealed class PdfPlanComparisonSurface : UserControl
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
        private const int MaximumRenderPixels = 4000000;
        private const long MaximumWorkingBytes =
            128L * 1024L * 1024L;

        private readonly object workerSync = new object();
        private readonly AutoResetEvent workerSignal =
            new AutoResetEvent(false);
        private readonly List<PdfPlanComparisonSource> revisedCandidates =
            new List<PdfPlanComparisonSource>();
        private readonly ToolTip toolTip;
        private readonly ComparisonCanvas canvas;
        private readonly Panel commandPanel;
        private readonly Panel manualPanel;
        private readonly Button closeButton;
        private readonly Button baselineButton;
        private readonly NumericUpDown baselinePageInput;
        private readonly Button swapButton;
        private readonly ComboBox revisedSourceCombo;
        private readonly Button browseButton;
        private readonly NumericUpDown revisedPageInput;
        private readonly Button linkPagesButton;
        private readonly Button overlayModeButton;
        private readonly Button differencesModeButton;
        private readonly Button alternateModeButton;
        private readonly Button splitModeButton;
        private readonly Button alternatePlayButton;
        private readonly TrackBar opacitySlider;
        private readonly Label opacityLabel;
        private readonly ComboBox alignmentCombo;
        private readonly Button resetAlignmentButton;
        private readonly Button collapseButton;
        private readonly Button restoreCommandsButton;
        private readonly NumericUpDown offsetXInput;
        private readonly NumericUpDown offsetYInput;
        private readonly Button resetOffsetButton;
        private readonly Label manualCaptionLabel;
        private readonly System.Windows.Forms.Timer alternateTimer;
        private readonly System.Windows.Forms.Timer manualRenderTimer;

        private PdfPlanComparisonSource baselineSource;
        private PdfPlanComparisonSource revisedSource;
        private PdfPlanComparisonResult currentResult;
        private Bitmap differenceBitmap;
        private PdfPlanPageAdjustment currentAdjustment =
            new PdfPlanPageAdjustment();
        private PdfPlanComparisonMode viewMode =
            PdfPlanComparisonMode.Overlay;
        private Thread workerThread;
        private CancellationTokenSource activeWorkerCancellation;
        private RenderRequest pendingRequest;
        private RenderOutcome pendingPostedOutcome;
        private int requestGeneration;
        private int baselinePageCount;
        private int revisedPageCount;
        private int previousBaselinePageValue = 1;
        private float actualDpi = 144F;
        private bool started;
        private bool stopping;
        private bool disposed;
        private bool busy;
        private bool suppressUiEvents;
        private bool pagesLinked = true;
        private bool commandsCollapsed;
        private bool alternateShowingRevised;
        private bool alternatePlaying;
        private bool automaticSolutionReady;
        private string headerTitle = string.Empty;
        private string headerDetail = string.Empty;

        public PdfPlanComparisonSurface(
            PdfPlanComparisonSource baseline,
            IList<PdfPlanComparisonSource> candidates)
        {
            if (baseline == null)
            {
                throw new ArgumentNullException("baseline");
            }

            baselineSource = baseline.Clone();
            AddCandidates(candidates);
            revisedSource = revisedCandidates.FirstOrDefault();

            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = WorkspaceColor;
            Font = CreateUiFont(9.25F, FontStyle.Regular);
            TabStop = true;
            AllowDrop = true;
            DoubleBuffered = true;

            toolTip = new ToolTip
            {
                AutoPopDelay = 9000,
                InitialDelay = 350,
                ReshowDelay = 100,
                ShowAlways = true
            };

            canvas = new ComparisonCanvas
            {
                Dock = DockStyle.Fill,
                BackColor = WorkspaceColor,
                TabStop = true,
                AccessibleName =
                    "Vista de comparación de las revisiones A y B"
            };
            canvas.SplitPositionChanged += Canvas_SplitPositionChanged;
            canvas.ManualOffsetDragged += Canvas_ManualOffsetDragged;

            commandPanel = new Panel
            {
                Height = 46,
                BackColor = PaperColor,
                TabStop = false
            };
            commandPanel.Paint += CommandPanel_Paint;

            closeButton = CreateCommandButton(
                "\u00D7",
                "Cerrar comparación (Esc)");
            closeButton.Click += delegate { RequestClose(); };

            baselineButton = CreateSourceButton();
            baselineButton.AccessibleName = "Revisión A";
            baselineButton.Click += delegate
            {
                ShowSourcePath(baselineSource, baselineButton);
            };

            baselinePageInput = CreatePageInput(
                "Página de la revisión A");
            baselinePageInput.Value =
                baselineSource.InitialPageIndex + 1;
            previousBaselinePageValue =
                Decimal.ToInt32(baselinePageInput.Value);
            baselinePageInput.ValueChanged +=
                BaselinePageInput_ValueChanged;

            swapButton = CreateCommandButton(
                "\u21C4",
                "Intercambiar las revisiones A y B");
            swapButton.Click += SwapButton_Click;

            revisedSourceCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = TitleColor,
                Font = CreateUiFont(8.75F, FontStyle.Regular),
                IntegralHeight = false,
                DropDownHeight = 230,
                AccessibleName = "Revisión B"
            };
            foreach (var candidate in revisedCandidates)
            {
                revisedSourceCombo.Items.Add(candidate);
            }
            if (revisedSourceCombo.Items.Count > 0)
            {
                revisedSourceCombo.SelectedIndex = 0;
            }
            revisedSourceCombo.SelectedIndexChanged +=
                RevisedSourceCombo_SelectedIndexChanged;

            browseButton = CreateCommandButton(
                "\uE8E5",
                "Elegir el PDF de la revisión B");
            browseButton.Font = new Font(
                "Segoe MDL2 Assets",
                10.5F,
                FontStyle.Regular,
                GraphicsUnit.Point);
            browseButton.Click += BrowseButton_Click;

            revisedPageInput = CreatePageInput(
                "Página de la revisión B");
            revisedPageInput.Value = revisedSource == null
                ? 1
                : revisedSource.InitialPageIndex + 1;
            revisedPageInput.ValueChanged +=
                RevisedPageInput_ValueChanged;

            linkPagesButton = CreateCommandButton(
                "\uE71B",
                "Vincular la navegación de páginas A y B");
            linkPagesButton.AccessibleRole =
                AccessibleRole.CheckButton;
            linkPagesButton.Font = new Font(
                "Segoe MDL2 Assets",
                10.5F,
                FontStyle.Regular,
                GraphicsUnit.Point);
            linkPagesButton.Click += delegate
            {
                pagesLinked = !pagesLinked;
                UpdateSelectionStyles();
            };

            overlayModeButton = CreateModeButton(
                "\u25EB",
                "Superponer las revisiones (1)");
            overlayModeButton.Click += delegate
            {
                SetViewMode(PdfPlanComparisonMode.Overlay);
            };
            differencesModeButton = CreateModeButton(
                "\u0394",
                "Mostrar diferencias en rojo y cian (2)");
            differencesModeButton.Font =
                CreateArchitecturalFont(10.5F, true);
            differencesModeButton.Click += delegate
            {
                SetViewMode(PdfPlanComparisonMode.RedCyan);
            };
            alternateModeButton = CreateModeButton(
                "A/B",
                "Alternar entre las revisiones (3)");
            alternateModeButton.Font =
                CreateArchitecturalFont(7.25F, true);
            alternateModeButton.Click += delegate
            {
                SetViewMode(PdfPlanComparisonMode.Baseline);
            };
            splitModeButton = CreateModeButton(
                "\u25E7",
                "Dividir con una cortinilla desplazable (4)");
            splitModeButton.Click += delegate
            {
                SetViewMode(PdfPlanComparisonMode.Split);
            };

            alternatePlayButton = CreateCommandButton(
                "\u25B6",
                "Iniciar o pausar la alternancia automática");
            alternatePlayButton.AccessibleRole =
                AccessibleRole.CheckButton;
            alternatePlayButton.Font =
                CreateArchitecturalFont(9.5F, false);
            alternatePlayButton.Visible = false;
            alternatePlayButton.Click += delegate
            {
                ToggleAlternatePlayback();
            };

            opacitySlider = new TrackBar
            {
                Minimum = 0,
                Maximum = 100,
                Value = 50,
                TickStyle = TickStyle.None,
                AutoSize = false,
                Height = 28,
                SmallChange = 5,
                LargeChange = 10,
                AccessibleName = "Opacidad de la revisión B"
            };
            opacitySlider.ValueChanged += delegate
            {
                UpdateOpacityLabel();
                canvas.OverlayOpacity =
                    opacitySlider.Value / 100F;
                canvas.Invalidate();
            };

            opacityLabel = new Label
            {
                Width = 42,
                Height = 26,
                Text = "50%",
                ForeColor = BodyColor,
                TextAlign = ContentAlignment.MiddleRight,
                Font = CreateArchitecturalFont(7.75F, true),
                AccessibleName = "Valor de opacidad"
            };

            alignmentCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = TitleColor,
                Font = CreateUiFont(8.5F, FontStyle.Regular),
                IntegralHeight = false,
                DropDownHeight = 110,
                AccessibleName = "Método de alineación"
            };
            alignmentCombo.Items.Add("Por hoja");
            alignmentCombo.Items.Add("Automática");
            alignmentCombo.Items.Add("Manual");
            alignmentCombo.SelectedIndex = 0;
            alignmentCombo.SelectedIndexChanged +=
                AlignmentCombo_SelectedIndexChanged;

            resetAlignmentButton = CreateCommandButton(
                "\u21BA",
                "Restablecer la alineación");
            resetAlignmentButton.Click += delegate
            {
                ResetAlignment(true);
            };

            collapseButton = CreateCommandButton(
                "\u2303",
                "Plegar los controles de comparación");
            collapseButton.Font =
                CreateArchitecturalFont(10F, false);
            collapseButton.Click += delegate
            {
                SetCommandsCollapsed(true);
            };

            restoreCommandsButton = CreateCommandButton(
                "A/B",
                "Mostrar los controles de comparación");
            restoreCommandsButton.Font =
                CreateArchitecturalFont(7.25F, true);
            restoreCommandsButton.Size = new Size(38, 38);
            restoreCommandsButton.Visible = false;
            restoreCommandsButton.BackColor = PaperColor;
            restoreCommandsButton.Click += delegate
            {
                SetCommandsCollapsed(false);
            };

            manualPanel = new Panel
            {
                Width = 370,
                Height = 40,
                BackColor = PaperColor,
                Visible = false,
                TabStop = false
            };
            manualPanel.Paint += CommandPanel_Paint;

            manualCaptionLabel = new Label
            {
                Left = 10,
                Top = 7,
                Width = 116,
                Height = 25,
                Text = "AJUSTE X / Y · MM",
                ForeColor = AccentTextColor,
                Font = CreateArchitecturalFont(7.25F, true),
                TextAlign = ContentAlignment.MiddleLeft
            };
            offsetXInput = CreateOffsetInput(
                "Desplazamiento horizontal de B en milímetros");
            offsetXInput.Left = 122;
            offsetXInput.Top = 6;
            offsetXInput.ValueChanged += ManualOffsetInput_ValueChanged;
            offsetYInput = CreateOffsetInput(
                "Desplazamiento vertical de B en milímetros");
            offsetYInput.Left = 206;
            offsetYInput.Top = 6;
            offsetYInput.ValueChanged += ManualOffsetInput_ValueChanged;
            resetOffsetButton = CreateCommandButton(
                "0",
                "Restablecer el desplazamiento manual");
            resetOffsetButton.Left = 318;
            resetOffsetButton.Top = 5;
            resetOffsetButton.Click += delegate
            {
                SetManualOffset(0D, 0D, true);
            };
            manualPanel.Controls.Add(manualCaptionLabel);
            manualPanel.Controls.Add(offsetXInput);
            manualPanel.Controls.Add(offsetYInput);
            manualPanel.Controls.Add(resetOffsetButton);

            commandPanel.Controls.Add(closeButton);
            commandPanel.Controls.Add(baselineButton);
            commandPanel.Controls.Add(baselinePageInput);
            commandPanel.Controls.Add(swapButton);
            commandPanel.Controls.Add(revisedSourceCombo);
            commandPanel.Controls.Add(browseButton);
            commandPanel.Controls.Add(revisedPageInput);
            commandPanel.Controls.Add(linkPagesButton);
            commandPanel.Controls.Add(overlayModeButton);
            commandPanel.Controls.Add(differencesModeButton);
            commandPanel.Controls.Add(alternateModeButton);
            commandPanel.Controls.Add(splitModeButton);
            commandPanel.Controls.Add(alternatePlayButton);
            commandPanel.Controls.Add(opacitySlider);
            commandPanel.Controls.Add(opacityLabel);
            commandPanel.Controls.Add(alignmentCombo);
            commandPanel.Controls.Add(resetAlignmentButton);
            commandPanel.Controls.Add(collapseButton);

            Controls.Add(canvas);
            Controls.Add(manualPanel);
            Controls.Add(commandPanel);
            Controls.Add(restoreCommandsButton);

            alternateTimer = new System.Windows.Forms.Timer
            {
                Interval = 1000
            };
            alternateTimer.Tick += delegate
            {
                if (viewMode == PdfPlanComparisonMode.Baseline &&
                    alternatePlaying &&
                    Visible &&
                    FindForm() != null &&
                    FindForm().WindowState !=
                        FormWindowState.Minimized)
                {
                    alternateShowingRevised =
                        !alternateShowingRevised;
                    canvas.AlternateShowingRevised =
                        alternateShowingRevised;
                    canvas.Invalidate();
                }
            };

            manualRenderTimer = new System.Windows.Forms.Timer
            {
                Interval = 180
            };
            manualRenderTimer.Tick += delegate
            {
                manualRenderTimer.Stop();
                QueueRender(false);
            };

            DragEnter += Comparison_DragEnter;
            DragDrop += Comparison_DragDrop;
            Resize += delegate
            {
                LayoutFloatingControls();
            };
            VisibleChanged += delegate
            {
                if (!Visible && alternatePlaying)
                {
                    SetAlternatePlayback(false);
                }
            };

            UpdateSourceControls();
            UpdateSelectionStyles();
            UpdateModeControls();
            LayoutFloatingControls();
            canvas.StatusText = revisedSource == null
                ? "Arrastra o elige el PDF de la revisión B"
                : "Preparado para comparar";
            UpdateHeaderStatus(canvas.StatusText);
        }

        public event EventHandler CloseRequested;

        public event EventHandler<PdfPlanComparisonStatusEventArgs>
            StatusChanged;

        public bool IsBusy
        {
            get { return busy; }
        }

        public string HeaderTitle
        {
            get { return headerTitle; }
        }

        public string HeaderDetail
        {
            get { return headerDetail; }
        }

        internal ComparisonCanvas CanvasForTesting
        {
            get { return canvas; }
        }

        internal Panel CommandPanelForTesting
        {
            get { return commandPanel; }
        }

        internal ComboBox RevisedSourceForTesting
        {
            get { return revisedSourceCombo; }
        }

        internal NumericUpDown BaselinePageForTesting
        {
            get { return baselinePageInput; }
        }

        internal NumericUpDown RevisedPageForTesting
        {
            get { return revisedPageInput; }
        }

        internal Button OverlayModeForTesting
        {
            get { return overlayModeButton; }
        }

        internal Button DifferencesModeForTesting
        {
            get { return differencesModeButton; }
        }

        internal Button AlternateModeForTesting
        {
            get { return alternateModeButton; }
        }

        internal Button SplitModeForTesting
        {
            get { return splitModeButton; }
        }

        internal ComboBox AlignmentForTesting
        {
            get { return alignmentCombo; }
        }

        internal TrackBar OpacityForTesting
        {
            get { return opacitySlider; }
        }

        internal Button SwapForTesting
        {
            get { return swapButton; }
        }

        internal Button CloseForTesting
        {
            get { return closeButton; }
        }

        internal Button LinkPagesForTesting
        {
            get { return linkPagesButton; }
        }

        internal Button CollapseForTesting
        {
            get { return collapseButton; }
        }

        internal Button RestoreForTesting
        {
            get { return restoreCommandsButton; }
        }

        internal NumericUpDown OffsetXForTesting
        {
            get { return offsetXInput; }
        }

        internal NumericUpDown OffsetYForTesting
        {
            get { return offsetYInput; }
        }

        internal PdfPlanComparisonResult ResultForTesting
        {
            get { return currentResult; }
        }

        internal Bitmap DifferenceForTesting
        {
            get { return differenceBitmap; }
        }

        internal PdfPlanComparisonMode ViewModeForTesting
        {
            get { return viewMode; }
        }

        internal bool ProcessShortcutForTesting(Keys keyData)
        {
            var message = new Message();
            return ProcessCmdKey(ref message, keyData);
        }

        public void Begin()
        {
            if (disposed || started)
            {
                return;
            }

            started = true;
            if (revisedSource == null)
            {
                canvas.StatusText =
                    "Arrastra o elige el PDF de la revisión B";
                canvas.Invalidate();
                UpdateHeaderStatus(canvas.StatusText);
                revisedSourceCombo.Focus();
                return;
            }

            QueueRender(false);
            canvas.Focus();
        }

        public void CancelAndDispose()
        {
            if (!disposed)
            {
                Dispose();
            }
        }

        public bool SelectRevisedPdf(
            string path,
            string displayName,
            int initialPageIndex)
        {
            if (disposed ||
                string.IsNullOrWhiteSpace(path) ||
                !File.Exists(path) ||
                !string.Equals(
                    System.IO.Path.GetExtension(path),
                    ".pdf",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var source = new PdfPlanComparisonSource(
                displayName,
                path,
                initialPageIndex);
            var existing = FindCandidate(source.Path);
            if (existing == null)
            {
                revisedCandidates.Add(source);
                revisedSourceCombo.Items.Add(source);
                existing = source;
            }

            suppressUiEvents = true;
            try
            {
                revisedSourceCombo.SelectedItem = existing;
            }
            finally
            {
                suppressUiEvents = false;
            }

            SetRevisedSource(existing);
            return true;
        }

        protected override bool ProcessCmdKey(
            ref Message msg,
            Keys keyData)
        {
            var key = keyData & Keys.KeyCode;
            var modifiers = keyData & Keys.Modifiers;
            var editorFocused =
                revisedSourceCombo.Focused ||
                alignmentCombo.Focused ||
                baselinePageInput.Focused ||
                revisedPageInput.Focused ||
                offsetXInput.Focused ||
                offsetYInput.Focused ||
                opacitySlider.Focused;

            if (key == Keys.Escape)
            {
                RequestClose();
                return true;
            }

            if (!editorFocused &&
                modifiers == Keys.None &&
                key >= Keys.D1 &&
                key <= Keys.D4)
            {
                switch (key)
                {
                    case Keys.D1:
                        SetViewMode(PdfPlanComparisonMode.Overlay);
                        break;
                    case Keys.D2:
                        SetViewMode(PdfPlanComparisonMode.RedCyan);
                        break;
                    case Keys.D3:
                        SetViewMode(PdfPlanComparisonMode.Baseline);
                        break;
                    case Keys.D4:
                        SetViewMode(PdfPlanComparisonMode.Split);
                        break;
                }

                return true;
            }

            if (!editorFocused &&
                modifiers == Keys.None &&
                key == Keys.Space &&
                viewMode == PdfPlanComparisonMode.Baseline)
            {
                alternateShowingRevised =
                    !alternateShowingRevised;
                canvas.AlternateShowingRevised =
                    alternateShowingRevised;
                canvas.Invalidate();
                return true;
            }

            if (!editorFocused &&
                alignmentCombo.SelectedIndex == 2 &&
                (key == Keys.Left ||
                 key == Keys.Right ||
                 key == Keys.Up ||
                 key == Keys.Down))
            {
                var large = (modifiers & Keys.Shift) == Keys.Shift;
                var pixels = large ? 10D : 1D;
                var deltaX = key == Keys.Left
                    ? -pixels
                    : (key == Keys.Right ? pixels : 0D);
                var deltaY = key == Keys.Up
                    ? -pixels
                    : (key == Keys.Down ? pixels : 0D);
                NudgeManualOffset(deltaX, deltaY);
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed)
            {
                disposed = true;
                stopping = true;
                alternateTimer.Stop();
                manualRenderTimer.Stop();

                lock (workerSync)
                {
                    pendingRequest = null;
                    if (pendingPostedOutcome != null)
                    {
                        pendingPostedOutcome.Dispose();
                        pendingPostedOutcome = null;
                    }
                    if (activeWorkerCancellation != null)
                    {
                        try
                        {
                            activeWorkerCancellation.Cancel();
                        }
                        catch
                        {
                        }
                    }
                }

                try
                {
                    workerSignal.Set();
                }
                catch
                {
                }

                var thread = workerThread;
                if (thread != null &&
                    thread.IsAlive &&
                    Thread.CurrentThread != thread)
                {
                    try
                    {
                        thread.Join(2000);
                    }
                    catch
                    {
                    }
                }

                DisposeCurrentVisual();
                alternateTimer.Dispose();
                manualRenderTimer.Dispose();
                toolTip.Dispose();
            }

            base.Dispose(disposing);
        }

        private void AddCandidates(
            IEnumerable<PdfPlanComparisonSource> candidates)
        {
            foreach (var candidate in
                candidates ?? Enumerable.Empty<PdfPlanComparisonSource>())
            {
                if (candidate == null ||
                    FindCandidate(candidate.Path) != null)
                {
                    continue;
                }

                revisedCandidates.Add(candidate.Clone());
            }
        }

        private PdfPlanComparisonSource FindCandidate(string path)
        {
            return revisedCandidates.FirstOrDefault(
                candidate => string.Equals(
                    candidate.Path,
                    path,
                    StringComparison.OrdinalIgnoreCase));
        }

        private void RevisedSourceCombo_SelectedIndexChanged(
            object sender,
            EventArgs e)
        {
            if (suppressUiEvents)
            {
                return;
            }

            var source =
                revisedSourceCombo.SelectedItem
                    as PdfPlanComparisonSource;
            if (source != null)
            {
                SetRevisedSource(source);
            }
        }

        private void SetRevisedSource(
            PdfPlanComparisonSource source)
        {
            revisedSource = source == null
                ? null
                : source.Clone();
            revisedPageCount = 0;
            automaticSolutionReady = false;
            currentAdjustment = new PdfPlanPageAdjustment();

            suppressUiEvents = true;
            try
            {
                revisedPageInput.Value = revisedSource == null
                    ? 1
                    : Math.Max(
                        revisedPageInput.Minimum,
                        Math.Min(
                            revisedPageInput.Maximum,
                            revisedSource.InitialPageIndex + 1));
            }
            finally
            {
                suppressUiEvents = false;
            }

            UpdateSourceControls();
            if (started && revisedSource != null)
            {
                QueueRender(
                    alignmentCombo.SelectedIndex == 1);
            }
        }

        private void BrowseButton_Click(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "Elegir revisión B";
                dialog.Filter =
                    "Documentos PDF (*.pdf)|*.pdf|Todos los archivos (*.*)|*.*";
                dialog.Multiselect = false;
                dialog.CheckFileExists = true;
                try
                {
                    dialog.InitialDirectory =
                        System.IO.Path.GetDirectoryName(
                            baselineSource.Path);
                }
                catch
                {
                }

                if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
                {
                    SelectRevisedPdf(
                        dialog.FileName,
                        System.IO.Path.GetFileName(dialog.FileName),
                        Math.Max(
                            0,
                            Decimal.ToInt32(
                                baselinePageInput.Value) - 1));
                }
            }
        }

        private void SwapButton_Click(object sender, EventArgs e)
        {
            if (revisedSource == null)
            {
                return;
            }

            var oldBaseline = baselineSource;
            var oldBaselinePage =
                Decimal.ToInt32(baselinePageInput.Value);
            var oldRevised = revisedSource;
            var oldRevisedPage =
                Decimal.ToInt32(revisedPageInput.Value);

            var baselineCandidate =
                FindCandidate(oldBaseline.Path);
            if (baselineCandidate == null)
            {
                baselineCandidate = oldBaseline.Clone();
                revisedCandidates.Add(baselineCandidate);
                revisedSourceCombo.Items.Add(baselineCandidate);
            }

            baselineSource = oldRevised.Clone();
            revisedSource = oldBaseline.Clone();
            baselinePageCount = revisedPageCount;
            revisedPageCount = 0;
            currentAdjustment = new PdfPlanPageAdjustment();
            automaticSolutionReady = false;

            suppressUiEvents = true;
            try
            {
                baselinePageInput.Maximum = 999999;
                revisedPageInput.Maximum = 999999;
                baselinePageInput.Value = Math.Max(
                    baselinePageInput.Minimum,
                    Math.Min(
                        baselinePageInput.Maximum,
                        oldRevisedPage));
                revisedPageInput.Value = Math.Max(
                    revisedPageInput.Minimum,
                    Math.Min(
                        revisedPageInput.Maximum,
                        oldBaselinePage));
                previousBaselinePageValue =
                    Decimal.ToInt32(baselinePageInput.Value);
                revisedSourceCombo.SelectedItem =
                    baselineCandidate;
            }
            finally
            {
                suppressUiEvents = false;
            }

            UpdateSourceControls();
            if (started)
            {
                QueueRender(
                    alignmentCombo.SelectedIndex == 1);
            }
        }

        private void BaselinePageInput_ValueChanged(
            object sender,
            EventArgs e)
        {
            if (suppressUiEvents)
            {
                return;
            }

            var next =
                Decimal.ToInt32(baselinePageInput.Value);
            var delta = next - previousBaselinePageValue;
            previousBaselinePageValue = next;
            if (pagesLinked && revisedSource != null && delta != 0)
            {
                suppressUiEvents = true;
                try
                {
                    var target =
                        Decimal.ToInt32(revisedPageInput.Value) +
                        delta;
                    revisedPageInput.Value = Math.Max(
                        revisedPageInput.Minimum,
                        Math.Min(
                            revisedPageInput.Maximum,
                            target));
                }
                finally
                {
                    suppressUiEvents = false;
                }
            }

            PagePairChanged();
        }

        private void RevisedPageInput_ValueChanged(
            object sender,
            EventArgs e)
        {
            if (!suppressUiEvents)
            {
                PagePairChanged();
            }
        }

        private void PagePairChanged()
        {
            automaticSolutionReady = false;
            currentAdjustment = new PdfPlanPageAdjustment();
            SyncManualInputs();
            if (started && revisedSource != null)
            {
                QueueRender(
                    alignmentCombo.SelectedIndex == 1);
            }
        }

        private void AlignmentCombo_SelectedIndexChanged(
            object sender,
            EventArgs e)
        {
            if (suppressUiEvents)
            {
                return;
            }

            manualPanel.Visible =
                !commandsCollapsed &&
                alignmentCombo.SelectedIndex == 2;
            canvas.ManualOffsetEnabled =
                alignmentCombo.SelectedIndex == 2;
            automaticSolutionReady = false;
            if (alignmentCombo.SelectedIndex == 0)
            {
                currentAdjustment =
                    new PdfPlanPageAdjustment();
                SyncManualInputs();
                QueueRender(false);
            }
            else if (alignmentCombo.SelectedIndex == 1)
            {
                currentAdjustment =
                    new PdfPlanPageAdjustment();
                SyncManualInputs();
                QueueRender(true);
            }
            else
            {
                SyncManualInputs();
                QueueRender(false);
            }

            LayoutFloatingControls();
        }

        private void ResetAlignment(bool render)
        {
            currentAdjustment = new PdfPlanPageAdjustment();
            automaticSolutionReady = false;
            suppressUiEvents = true;
            try
            {
                alignmentCombo.SelectedIndex = 0;
            }
            finally
            {
                suppressUiEvents = false;
            }

            manualPanel.Visible = false;
            canvas.ManualOffsetEnabled = false;
            SyncManualInputs();
            LayoutFloatingControls();
            if (render && started && revisedSource != null)
            {
                QueueRender(false);
            }
        }

        private void ManualOffsetInput_ValueChanged(
            object sender,
            EventArgs e)
        {
            if (suppressUiEvents)
            {
                return;
            }

            currentAdjustment.OffsetXPoints =
                Decimal.ToDouble(offsetXInput.Value) *
                72D / 25.4D;
            currentAdjustment.OffsetYPoints =
                Decimal.ToDouble(offsetYInput.Value) *
                72D / 25.4D;
            manualRenderTimer.Stop();
            manualRenderTimer.Start();
        }

        private void SetManualOffset(
            double xPoints,
            double yPoints,
            bool render)
        {
            currentAdjustment.OffsetXPoints = Math.Max(
                -144D,
                Math.Min(144D, xPoints));
            currentAdjustment.OffsetYPoints = Math.Max(
                -144D,
                Math.Min(144D, yPoints));
            SyncManualInputs();
            if (render && started)
            {
                QueueRender(false);
            }
        }

        private void SyncManualInputs()
        {
            suppressUiEvents = true;
            try
            {
                offsetXInput.Value = ClampDecimal(
                    (decimal)(
                        currentAdjustment.OffsetXPoints *
                        25.4D / 72D),
                    offsetXInput.Minimum,
                    offsetXInput.Maximum);
                offsetYInput.Value = ClampDecimal(
                    (decimal)(
                        currentAdjustment.OffsetYPoints *
                        25.4D / 72D),
                    offsetYInput.Minimum,
                    offsetYInput.Maximum);
            }
            finally
            {
                suppressUiEvents = false;
            }
        }

        private void Canvas_ManualOffsetDragged(
            object sender,
            ManualOffsetDraggedEventArgs e)
        {
            if (alignmentCombo.SelectedIndex != 2 ||
                currentResult == null ||
                e == null ||
                e.DisplayScale <= 0F)
            {
                return;
            }

            var xPoints =
                (e.DeltaX / e.DisplayScale) *
                72D / Math.Max(1F, actualDpi);
            var yPoints =
                (e.DeltaY / e.DisplayScale) *
                72D / Math.Max(1F, actualDpi);
            SetManualOffset(
                currentAdjustment.OffsetXPoints + xPoints,
                currentAdjustment.OffsetYPoints + yPoints,
                true);
        }

        private void NudgeManualOffset(
            double deltaXPixels,
            double deltaYPixels)
        {
            var xPoints =
                deltaXPixels * 72D /
                Math.Max(1F, actualDpi);
            var yPoints =
                deltaYPixels * 72D /
                Math.Max(1F, actualDpi);
            SetManualOffset(
                currentAdjustment.OffsetXPoints + xPoints,
                currentAdjustment.OffsetYPoints + yPoints,
                true);
        }

        private void Canvas_SplitPositionChanged(
            object sender,
            SplitPositionChangedEventArgs e)
        {
            if (e == null)
            {
                return;
            }

            canvas.SplitPosition =
                Math.Max(0F, Math.Min(1F, e.Position));
            canvas.Invalidate();
        }

        private void SetViewMode(PdfPlanComparisonMode mode)
        {
            if (viewMode == PdfPlanComparisonMode.RedCyan &&
                mode != PdfPlanComparisonMode.RedCyan)
            {
                DisposeDifferenceBitmap();
            }

            viewMode = mode;
            if (viewMode != PdfPlanComparisonMode.Baseline)
            {
                SetAlternatePlayback(false);
            }

            if (viewMode == PdfPlanComparisonMode.RedCyan)
            {
                EnsureDifferenceBitmap();
            }

            canvas.ViewMode = mode;
            canvas.AlternateShowingRevised =
                alternateShowingRevised;
            UpdateModeControls();
            canvas.Invalidate();
            canvas.Focus();
        }

        private void ToggleAlternatePlayback()
        {
            SetAlternatePlayback(!alternatePlaying);
        }

        private void SetAlternatePlayback(bool play)
        {
            alternatePlaying =
                play &&
                viewMode == PdfPlanComparisonMode.Baseline &&
                currentResult != null;
            if (alternatePlaying)
            {
                alternateTimer.Start();
            }
            else
            {
                alternateTimer.Stop();
            }

            alternatePlayButton.Text =
                alternatePlaying ? "\u23F8" : "\u25B6";
            alternatePlayButton.AccessibleName =
                alternatePlaying
                    ? "Pausar la alternancia automática"
                    : "Iniciar la alternancia automática";
            toolTip.SetToolTip(
                alternatePlayButton,
                alternatePlayButton.AccessibleName);
            UpdateSelectionStyles();
        }

        private void UpdateModeControls()
        {
            overlayModeButton.Tag =
                viewMode == PdfPlanComparisonMode.Overlay;
            differencesModeButton.Tag =
                viewMode == PdfPlanComparisonMode.RedCyan;
            alternateModeButton.Tag =
                viewMode == PdfPlanComparisonMode.Baseline;
            splitModeButton.Tag =
                viewMode == PdfPlanComparisonMode.Split;

            alternatePlayButton.Visible =
                viewMode == PdfPlanComparisonMode.Baseline;
            opacitySlider.Enabled =
                viewMode == PdfPlanComparisonMode.Overlay;
            opacityLabel.Enabled =
                viewMode == PdfPlanComparisonMode.Overlay;
            canvas.ViewMode = viewMode;
            UpdateSelectionStyles();
            LayoutFloatingControls();
        }

        private void UpdateSelectionStyles()
        {
            StyleSelectableButton(
                overlayModeButton,
                viewMode == PdfPlanComparisonMode.Overlay);
            StyleSelectableButton(
                differencesModeButton,
                viewMode == PdfPlanComparisonMode.RedCyan);
            StyleSelectableButton(
                alternateModeButton,
                viewMode == PdfPlanComparisonMode.Baseline);
            StyleSelectableButton(
                splitModeButton,
                viewMode == PdfPlanComparisonMode.Split);
            StyleSelectableButton(
                linkPagesButton,
                pagesLinked);
            StyleSelectableButton(
                alternatePlayButton,
                alternatePlaying);
        }

        private static void StyleSelectableButton(
            Button button,
            bool selected)
        {
            button.BackColor = selected
                ? AccentTintColor
                : PaperColor;
            button.ForeColor = selected
                ? AccentTextColor
                : BodyColor;
            button.FlatAppearance.BorderSize =
                selected ? 1 : 0;
            button.FlatAppearance.BorderColor =
                selected ? AccentColor : DividerColor;
            button.AccessibleDescription = selected
                ? "Seleccionado"
                : "No seleccionado";
        }

        private void UpdateOpacityLabel()
        {
            opacityLabel.Text =
                opacitySlider.Value + "%";
        }

        private void UpdateSourceControls()
        {
            baselineButton.Text =
                "A · " + CompactName(
                    baselineSource.DisplayName,
                    19);
            toolTip.SetToolTip(
                baselineButton,
                "Revisión A\r\n" +
                baselineSource.DisplayName +
                "\r\n" +
                baselineSource.Path);
            revisedSourceCombo.Enabled =
                revisedSourceCombo.Items.Count > 0;
            revisedPageInput.Enabled =
                revisedSource != null;
            swapButton.Enabled =
                revisedSource != null;
            toolTip.SetToolTip(
                revisedSourceCombo,
                revisedSource == null
                    ? "Elige la revisión B"
                    : "Revisión B\r\n" +
                      revisedSource.DisplayName +
                      "\r\n" +
                      revisedSource.Path);
            UpdateHeaderStatus(
                busy
                    ? "Preparando comparación…"
                    : (revisedSource == null
                        ? "Elige la revisión B"
                        : headerDetail));
        }

        private void ShowSourcePath(
            PdfPlanComparisonSource source,
            Control owner)
        {
            if (source == null || owner == null)
            {
                return;
            }

            toolTip.Show(
                source.DisplayName + "\r\n" + source.Path,
                owner,
                0,
                owner.Height + 3,
                4500);
        }

        private void SetCommandsCollapsed(bool collapse)
        {
            commandsCollapsed = collapse;
            commandPanel.Visible = !collapse;
            manualPanel.Visible =
                !collapse &&
                alignmentCombo.SelectedIndex == 2;
            restoreCommandsButton.Visible = collapse;
            LayoutFloatingControls();
            if (collapse)
            {
                canvas.Focus();
            }
        }

        private void LayoutFloatingControls()
        {
            if (ClientSize.Width <= 0 ||
                ClientSize.Height <= 0)
            {
                return;
            }

            restoreCommandsButton.Left = Math.Max(
                8,
                ClientSize.Width -
                restoreCommandsButton.Width - 12);
            restoreCommandsButton.Top = 12;
            restoreCommandsButton.BringToFront();

            var availableWidth =
                Math.Max(620, ClientSize.Width - 24);
            commandPanel.Width = Math.Min(1120, availableWidth);
            commandPanel.Left = Math.Max(
                0,
                (ClientSize.Width - commandPanel.Width) / 2);
            commandPanel.Top = 12;

            var oneRow = commandPanel.Width >= 1040;
            commandPanel.Height = oneRow ? 46 : 82;
            var rowOneTop = oneRow ? 7 : 6;
            var rowTwoTop = oneRow ? 7 : 43;
            var x = 7;

            Place(closeButton, ref x, rowOneTop, 30, 30, 4);
            Place(
                baselineButton,
                ref x,
                rowOneTop,
                oneRow ? 132 : 142,
                30,
                4);
            Place(
                baselinePageInput,
                ref x,
                rowOneTop + 2,
                54,
                26,
                4);
            Place(swapButton, ref x, rowOneTop, 30, 30, 4);
            Place(
                revisedSourceCombo,
                ref x,
                rowOneTop + 2,
                oneRow ? 138 : 150,
                26,
                4);
            Place(browseButton, ref x, rowOneTop, 30, 30, 4);
            Place(
                revisedPageInput,
                ref x,
                rowOneTop + 2,
                54,
                26,
                4);
            Place(linkPagesButton, ref x, rowOneTop, 30, 30, 5);

            if (!oneRow)
            {
                x = 40;
            }

            Place(
                overlayModeButton,
                ref x,
                rowTwoTop,
                32,
                30,
                2);
            Place(
                differencesModeButton,
                ref x,
                rowTwoTop,
                32,
                30,
                2);
            Place(
                alternateModeButton,
                ref x,
                rowTwoTop,
                34,
                30,
                2);
            Place(
                splitModeButton,
                ref x,
                rowTwoTop,
                32,
                30,
                4);
            Place(
                alternatePlayButton,
                ref x,
                rowTwoTop,
                alternatePlayButton.Visible ? 30 : 0,
                30,
                alternatePlayButton.Visible ? 4 : 0);
            Place(
                opacitySlider,
                ref x,
                rowTwoTop + 2,
                oneRow ? 92 : 112,
                28,
                1);
            Place(
                opacityLabel,
                ref x,
                rowTwoTop + 2,
                42,
                26,
                5);
            Place(
                alignmentCombo,
                ref x,
                rowTwoTop + 2,
                oneRow ? 104 : 118,
                26,
                4);
            Place(
                resetAlignmentButton,
                ref x,
                rowTwoTop,
                30,
                30,
                2);

            collapseButton.Width = 30;
            collapseButton.Height = 30;
            collapseButton.Left =
                commandPanel.ClientSize.Width -
                collapseButton.Width - 7;
            collapseButton.Top = rowTwoTop;

            manualPanel.Left = commandPanel.Left;
            manualPanel.Top = commandPanel.Bottom + 6;
            manualPanel.BringToFront();
            commandPanel.BringToFront();
        }

        private static void Place(
            Control control,
            ref int x,
            int top,
            int width,
            int height,
            int gap)
        {
            control.Left = x;
            control.Top = top;
            control.Width = Math.Max(0, width);
            control.Height = height;
            x += width + gap;
        }

        private void CommandPanel_Paint(
            object sender,
            PaintEventArgs e)
        {
            var panel = sender as Panel;
            if (panel == null)
            {
                return;
            }

            using (var border = new Pen(DividerColor))
            {
                e.Graphics.DrawRectangle(
                    border,
                    0,
                    0,
                    Math.Max(0, panel.ClientSize.Width - 1),
                    Math.Max(0, panel.ClientSize.Height - 1));
            }

            using (var accent = new Pen(AccentColor, 2F))
            {
                e.Graphics.DrawLine(
                    accent,
                    1,
                    1,
                    1,
                    Math.Max(1, panel.ClientSize.Height - 2));
            }
        }

        private void Comparison_DragEnter(
            object sender,
            DragEventArgs e)
        {
            e.Effect = GetFirstDroppedPdf(e.Data) == null
                ? DragDropEffects.None
                : DragDropEffects.Copy;
        }

        private void Comparison_DragDrop(
            object sender,
            DragEventArgs e)
        {
            var path = GetFirstDroppedPdf(e.Data);
            if (path != null)
            {
                SelectRevisedPdf(
                    path,
                    System.IO.Path.GetFileName(path),
                    Math.Max(
                        0,
                        Decimal.ToInt32(
                            baselinePageInput.Value) - 1));
            }
        }

        private static string GetFirstDroppedPdf(IDataObject data)
        {
            if (data == null ||
                !data.GetDataPresent(DataFormats.FileDrop))
            {
                return null;
            }

            var paths = data.GetData(
                DataFormats.FileDrop) as string[];
            return (paths ?? new string[0])
                .FirstOrDefault(
                    path =>
                        File.Exists(path) &&
                        string.Equals(
                            System.IO.Path.GetExtension(path),
                            ".pdf",
                            StringComparison.OrdinalIgnoreCase));
        }

        private void QueueRender(bool automaticProbe)
        {
            if (disposed ||
                stopping ||
                !started ||
                revisedSource == null)
            {
                return;
            }

            manualRenderTimer.Stop();
            SetAlternatePlayback(false);
            var request = new RenderRequest
            {
                Generation =
                    Interlocked.Increment(
                        ref requestGeneration),
                Baseline = baselineSource.Clone(),
                Revised = revisedSource.Clone(),
                BaselinePageIndex = Math.Max(
                    0,
                    Decimal.ToInt32(
                        baselinePageInput.Value) - 1),
                RevisedPageIndex = Math.Max(
                    0,
                    Decimal.ToInt32(
                        revisedPageInput.Value) - 1),
                Adjustment = currentAdjustment.Clone(),
                AutomaticProbe = automaticProbe
            };

            DisposeCurrentVisual();
            busy = true;
            canvas.StatusText = automaticProbe
                ? "Buscando la alineación del plano…"
                : "Preparando las páginas…";
            canvas.Invalidate();
            UpdateHeaderStatus(canvas.StatusText);

            EnsureWorkerStarted();
            lock (workerSync)
            {
                pendingRequest = request;
                if (activeWorkerCancellation != null)
                {
                    try
                    {
                        activeWorkerCancellation.Cancel();
                    }
                    catch
                    {
                    }
                }
            }

            workerSignal.Set();
        }

        private void EnsureWorkerStarted()
        {
            if (workerThread != null)
            {
                return;
            }

            lock (workerSync)
            {
                if (workerThread != null)
                {
                    return;
                }

                workerThread = new Thread(WorkerLoop)
                {
                    IsBackground = true,
                    Name = "PDF Ligero - comparación de planos"
                };
                workerThread.SetApartmentState(
                    ApartmentState.MTA);
                workerThread.Start();
            }
        }

        private void WorkerLoop()
        {
            PdfPlanComparisonSession session = null;
            string sessionBaselinePath = null;
            string sessionRevisedPath = null;
            bool sessionEstimatesOffset = false;
            try
            {
                while (true)
                {
                    workerSignal.WaitOne();
                    if (stopping)
                    {
                        return;
                    }

                    while (!stopping)
                    {
                        RenderRequest request;
                        CancellationTokenSource cancellation;
                        lock (workerSync)
                        {
                            request = pendingRequest;
                            pendingRequest = null;
                            if (request == null)
                            {
                                break;
                            }

                            cancellation =
                                new CancellationTokenSource();
                            activeWorkerCancellation =
                                cancellation;
                        }

                        var token = cancellation.Token;
                        RenderOutcome outcome = null;
                        try
                        {
                            token.ThrowIfCancellationRequested();
                            var reopen =
                                session == null ||
                                !string.Equals(
                                    sessionBaselinePath,
                                    request.Baseline.Path,
                                    StringComparison.OrdinalIgnoreCase) ||
                                !string.Equals(
                                    sessionRevisedPath,
                                    request.Revised.Path,
                                    StringComparison.OrdinalIgnoreCase) ||
                                sessionEstimatesOffset !=
                                    request.AutomaticProbe;
                            if (reopen)
                            {
                                if (session != null)
                                {
                                    session.Dispose();
                                    session = null;
                                }

                                var settings =
                                    CreateSettings(
                                        request.AutomaticProbe);
                                session =
                                    PdfPlanComparisonService.OpenSession(
                                        request.Baseline.Path,
                                        request.Revised.Path,
                                        settings,
                                        token);
                                sessionBaselinePath =
                                    request.Baseline.Path;
                                sessionRevisedPath =
                                    request.Revised.Path;
                                sessionEstimatesOffset =
                                    request.AutomaticProbe;
                            }

                            var baselineIndex = Math.Max(
                                0,
                                Math.Min(
                                    session.BaselinePageCount - 1,
                                    request.BaselinePageIndex));
                            var revisedIndex = Math.Max(
                                0,
                                Math.Min(
                                    session.RevisedPageCount - 1,
                                    request.RevisedPageIndex));
                            var result = session.Compare(
                                baselineIndex,
                                revisedIndex,
                                request.Adjustment,
                                token);
                            try
                            {
                                outcome = new RenderOutcome
                                {
                                    Request = request,
                                    Result = result,
                                    BaselinePageCount =
                                        session.BaselinePageCount,
                                    RevisedPageCount =
                                        session.RevisedPageCount,
                                    BaselinePageIndex =
                                        baselineIndex,
                                    RevisedPageIndex =
                                        revisedIndex
                                };
                                result = null;
                            }
                            finally
                            {
                                if (result != null)
                                {
                                    result.Dispose();
                                }
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            if (outcome != null)
                            {
                                outcome.Dispose();
                                outcome = null;
                            }
                        }
                        catch (Exception ex)
                        {
                            if (outcome != null)
                            {
                                outcome.Dispose();
                            }

                            outcome = new RenderOutcome
                            {
                                Request = request,
                                Error = ex
                            };
                        }
                        finally
                        {
                            lock (workerSync)
                            {
                                if (ReferenceEquals(
                                    activeWorkerCancellation,
                                    cancellation))
                                {
                                    activeWorkerCancellation =
                                        null;
                                }
                            }

                            cancellation.Dispose();
                        }

                        if (outcome != null)
                        {
                            PostOutcome(outcome);
                        }
                    }
                }
            }
            finally
            {
                if (session != null)
                {
                    session.Dispose();
                }

                try
                {
                    workerSignal.Dispose();
                }
                catch
                {
                }
            }
        }

        private static PdfPlanComparisonSettings CreateSettings(
            bool estimateOffset)
        {
            return new PdfPlanComparisonSettings
            {
                TargetDpi = 144,
                MaximumPixelsPerPage =
                    MaximumRenderPixels,
                MaximumWorkingBytes =
                    MaximumWorkingBytes,
                RenderAnnotations = true,
                AlignmentBasis =
                    PdfPlanAlignmentBasis.PhysicalPageBoxes,
                OverlayOpacity = 0.50F,
                SplitPosition = 0.50F,
                EstimateContentOffset = estimateOffset,
                MaximumAutoOffsetPixels = 48
            };
        }

        private void PostOutcome(RenderOutcome outcome)
        {
            if (outcome == null)
            {
                return;
            }

            if (disposed ||
                !IsHandleCreated ||
                Disposing ||
                IsDisposed)
            {
                outcome.Dispose();
                return;
            }

            RenderOutcome previousOutcome = null;
            lock (workerSync)
            {
                if (stopping)
                {
                    outcome.Dispose();
                    return;
                }

                previousOutcome = pendingPostedOutcome;
                pendingPostedOutcome = outcome;
            }
            if (previousOutcome != null)
            {
                previousOutcome.Dispose();
            }

            try
            {
                BeginInvoke(new Action<RenderOutcome>(
                    ApplyOutcome),
                    outcome);
            }
            catch
            {
                lock (workerSync)
                {
                    if (ReferenceEquals(
                        pendingPostedOutcome,
                        outcome))
                    {
                        pendingPostedOutcome = null;
                    }
                }
                outcome.Dispose();
            }
        }

        private void ApplyOutcome(RenderOutcome outcome)
        {
            if (outcome == null)
            {
                return;
            }

            lock (workerSync)
            {
                if (ReferenceEquals(
                    pendingPostedOutcome,
                    outcome))
                {
                    pendingPostedOutcome = null;
                }
            }

            if (disposed ||
                outcome.Request == null ||
                outcome.Request.Generation !=
                    requestGeneration)
            {
                outcome.Dispose();
                return;
            }

            if (outcome.Error != null)
            {
                busy = false;
                var message =
                    outcome.Error.GetBaseException().Message;
                canvas.StatusText =
                    "No se pudo comparar: " + message;
                canvas.Invalidate();
                UpdateHeaderStatus(
                    "No se pudo completar la comparación");
                outcome.Dispose();
                return;
            }

            if (outcome.Request.AutomaticProbe)
            {
                var suggestion =
                    outcome.Result == null
                        ? null
                        : outcome.Result.AlignmentSuggestion;
                if (suggestion != null &&
                    suggestion.IsReliable &&
                    suggestion.Adjustment != null)
                {
                    currentAdjustment =
                        suggestion.Adjustment.Clone();
                    automaticSolutionReady = true;
                    SyncManualInputs();
                    UpdatePageRanges(outcome);
                    outcome.Dispose();
                    canvas.StatusText =
                        "Aplicando la alineación automática…";
                    canvas.Invalidate();
                    QueueRender(false);
                    return;
                }

                automaticSolutionReady = false;
            }

            DisposeCurrentVisual();
            currentResult = outcome.Result;
            outcome.Result = null;
            actualDpi = currentResult == null
                ? 144F
                : currentResult.ActualDpi;
            UpdatePageRanges(outcome);
            canvas.Result = currentResult;
            if (viewMode == PdfPlanComparisonMode.RedCyan)
            {
                EnsureDifferenceBitmap();
            }
            else
            {
                canvas.DifferenceBitmap = null;
            }
            canvas.StatusText = string.Empty;
            canvas.ViewMode = viewMode;
            canvas.OverlayOpacity =
                opacitySlider.Value / 100F;
            canvas.AlternateShowingRevised =
                alternateShowingRevised;
            canvas.Invalidate();
            busy = false;

            var detail =
                "A " +
                (outcome.BaselinePageIndex + 1) +
                "/" +
                outcome.BaselinePageCount +
                "  ·  B " +
                (outcome.RevisedPageIndex + 1) +
                "/" +
                outcome.RevisedPageCount +
                "  ·  " +
                Math.Round(actualDpi) +
                " ppp";
            if (alignmentCombo.SelectedIndex == 1)
            {
                detail += automaticSolutionReady
                    ? "  ·  alineada"
                    : "  ·  sin coincidencia fiable";
            }
            UpdateHeaderStatus(detail);
            outcome.Dispose();
        }

        private void UpdatePageRanges(RenderOutcome outcome)
        {
            baselinePageCount =
                Math.Max(1, outcome.BaselinePageCount);
            revisedPageCount =
                Math.Max(1, outcome.RevisedPageCount);
            suppressUiEvents = true;
            try
            {
                baselinePageInput.Maximum =
                    baselinePageCount;
                revisedPageInput.Maximum =
                    revisedPageCount;
                baselinePageInput.Value =
                    Math.Max(
                        baselinePageInput.Minimum,
                        Math.Min(
                            baselinePageInput.Maximum,
                            outcome.BaselinePageIndex + 1));
                revisedPageInput.Value =
                    Math.Max(
                        revisedPageInput.Minimum,
                        Math.Min(
                            revisedPageInput.Maximum,
                            outcome.RevisedPageIndex + 1));
                previousBaselinePageValue =
                    Decimal.ToInt32(
                        baselinePageInput.Value);
            }
            finally
            {
                suppressUiEvents = false;
            }
        }

        private void DisposeCurrentVisual()
        {
            canvas.Result = null;
            canvas.DifferenceBitmap = null;
            var result = currentResult;
            currentResult = null;
            DisposeDifferenceBitmap();
            if (result != null)
            {
                result.Dispose();
            }
        }

        private void EnsureDifferenceBitmap()
        {
            if (differenceBitmap != null ||
                currentResult == null ||
                disposed)
            {
                canvas.DifferenceBitmap =
                    differenceBitmap;
                return;
            }

            var previousCursor = Cursor;
            try
            {
                Cursor = Cursors.WaitCursor;
                differenceBitmap =
                    currentResult.CreateComposite(
                        PdfPlanComparisonMode.RedCyan,
                        0.50F,
                        0.50F,
                        CancellationToken.None);
                canvas.DifferenceBitmap =
                    differenceBitmap;
            }
            catch (Exception ex)
            {
                canvas.DifferenceBitmap = null;
                canvas.StatusText =
                    "No se pudieron calcular las diferencias: " +
                    ex.GetBaseException().Message;
            }
            finally
            {
                Cursor = previousCursor;
            }
        }

        private void DisposeDifferenceBitmap()
        {
            canvas.DifferenceBitmap = null;
            var bitmap = differenceBitmap;
            differenceBitmap = null;
            if (bitmap != null)
            {
                bitmap.Dispose();
            }
        }

        private void UpdateHeaderStatus(string detail)
        {
            headerTitle =
                baselineSource.DisplayName +
                (revisedSource == null
                    ? " ↔ revisión B"
                    : " ↔ " +
                      revisedSource.DisplayName);
            headerDetail = detail ?? string.Empty;
            var handler = StatusChanged;
            if (handler != null)
            {
                handler(
                    this,
                    new PdfPlanComparisonStatusEventArgs(
                        headerTitle,
                        headerDetail));
            }
        }

        private void RequestClose()
        {
            if (disposed)
            {
                return;
            }

            var handler = CloseRequested;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
            else
            {
                CancelAndDispose();
            }
        }

        private static decimal ClampDecimal(
            decimal value,
            decimal minimum,
            decimal maximum)
        {
            return Math.Max(
                minimum,
                Math.Min(maximum, value));
        }

        private static string CompactName(
            string value,
            int maximum)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "PDF";
            }

            return value.Length <= maximum
                ? value
                : value.Substring(
                    0,
                    Math.Max(1, maximum - 1)) +
                  "\u2026";
        }

        private Button CreateCommandButton(
            string text,
            string accessibleName)
        {
            var button = new Button
            {
                Width = 30,
                Height = 30,
                Text = text,
                AccessibleName = accessibleName,
                BackColor = PaperColor,
                ForeColor = BodyColor,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Font = CreateArchitecturalFont(
                    10F,
                    false),
                TabStop = true
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor =
                AccentTintColor;
            button.FlatAppearance.MouseDownBackColor =
                DividerColor;
            toolTip.SetToolTip(button, accessibleName);
            return button;
        }

        private Button CreateModeButton(
            string text,
            string accessibleName)
        {
            var button = CreateCommandButton(
                text,
                accessibleName);
            button.AccessibleRole =
                AccessibleRole.RadioButton;
            button.Font = CreateArchitecturalFont(
                10F,
                false);
            return button;
        }

        private Button CreateSourceButton()
        {
            var button = new Button
            {
                Height = 30,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                BackColor = NavigationColor,
                ForeColor = TitleColor,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Help,
                Font = CreateArchitecturalFont(
                    8F,
                    true)
            };
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor =
                DividerColor;
            button.FlatAppearance.MouseOverBackColor =
                AccentTintColor;
            return button;
        }

        private NumericUpDown CreatePageInput(
            string accessibleName)
        {
            return new NumericUpDown
            {
                Minimum = 1,
                Maximum = 999999,
                Value = 1,
                DecimalPlaces = 0,
                ThousandsSeparator = false,
                TextAlign = HorizontalAlignment.Center,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White,
                ForeColor = TitleColor,
                Font = CreateArchitecturalFont(
                    8.5F,
                    true),
                AccessibleName = accessibleName
            };
        }

        private NumericUpDown CreateOffsetInput(
            string accessibleName)
        {
            return new NumericUpDown
            {
                Width = 74,
                Height = 27,
                Minimum = -51M,
                Maximum = 51M,
                DecimalPlaces = 1,
                Increment = 0.1M,
                TextAlign = HorizontalAlignment.Right,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White,
                ForeColor = TitleColor,
                Font = CreateArchitecturalFont(
                    8.25F,
                    false),
                AccessibleName = accessibleName
            };
        }

        private static Font CreateUiFont(
            float size,
            FontStyle style)
        {
            string[] families =
            {
                "Segoe UI",
                SystemFonts.MessageBoxFont.FontFamily.Name
            };
            foreach (var family in families)
            {
                Font result;
                if (TryCreateFont(
                    family,
                    size,
                    style,
                    out result))
                {
                    return result;
                }
            }

            return new Font(
                SystemFonts.MessageBoxFont.FontFamily,
                size,
                style,
                GraphicsUnit.Point);
        }

        private static Font CreateArchitecturalFont(
            float size,
            bool strong)
        {
            string[] families =
            {
                strong
                    ? "Bahnschrift SemiCondensed"
                    : "Bahnschrift Light SemiCondensed",
                strong
                    ? "Bahnschrift SemiBold"
                    : "Bahnschrift SemiLight",
                "Bahnschrift",
                "Segoe UI Semilight",
                "Segoe UI"
            };
            foreach (var family in families)
            {
                Font result;
                if (TryCreateFont(
                    family,
                    size,
                    strong
                        ? FontStyle.Bold
                        : FontStyle.Regular,
                    out result))
                {
                    return result;
                }
            }

            return CreateUiFont(
                size,
                strong
                    ? FontStyle.Bold
                    : FontStyle.Regular);
        }

        private static bool TryCreateFont(
            string family,
            float size,
            FontStyle style,
            out Font result)
        {
            result = null;
            try
            {
                var candidate = new Font(
                    family,
                    size,
                    style,
                    GraphicsUnit.Point);
                if (string.Equals(
                    candidate.FontFamily.Name,
                    family,
                    StringComparison.OrdinalIgnoreCase))
                {
                    result = candidate;
                    return true;
                }

                candidate.Dispose();
            }
            catch
            {
            }

            return false;
        }

        private sealed class RenderRequest
        {
            public int Generation;
            public PdfPlanComparisonSource Baseline;
            public PdfPlanComparisonSource Revised;
            public int BaselinePageIndex;
            public int RevisedPageIndex;
            public PdfPlanPageAdjustment Adjustment;
            public bool AutomaticProbe;
        }

        private sealed class RenderOutcome : IDisposable
        {
            public RenderRequest Request;
            public PdfPlanComparisonResult Result;
            public Exception Error;
            public int BaselinePageCount;
            public int RevisedPageCount;
            public int BaselinePageIndex;
            public int RevisedPageIndex;

            public void Dispose()
            {
                if (Result != null)
                {
                    Result.Dispose();
                    Result = null;
                }
            }
        }

        internal sealed class ComparisonCanvas : Control
        {
            private RectangleF imageBounds;
            private bool draggingSplit;
            private bool draggingManual;
            private Point manualStart;
            private Point manualCurrent;

            public ComparisonCanvas()
            {
                SetStyle(
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.ResizeRedraw |
                    ControlStyles.UserPaint,
                    true);
                ViewMode = PdfPlanComparisonMode.Overlay;
                OverlayOpacity = 0.5F;
                SplitPosition = 0.5F;
            }

            public event EventHandler<SplitPositionChangedEventArgs>
                SplitPositionChanged;

            public event EventHandler<ManualOffsetDraggedEventArgs>
                ManualOffsetDragged;

            public PdfPlanComparisonResult Result { get; set; }

            public Bitmap DifferenceBitmap { get; set; }

            public PdfPlanComparisonMode ViewMode { get; set; }

            public float OverlayOpacity { get; set; }

            public float SplitPosition { get; set; }

            public bool AlternateShowingRevised { get; set; }

            public bool ManualOffsetEnabled { get; set; }

            public string StatusText { get; set; }

            public RectangleF ImageBoundsForTesting
            {
                get { return imageBounds; }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                e.Graphics.Clear(WorkspaceColor);

                var result = Result;
                if (result == null)
                {
                    DrawStatus(
                        e.Graphics,
                        string.IsNullOrWhiteSpace(StatusText)
                            ? "Preparando comparación…"
                            : StatusText);
                    return;
                }

                Bitmap baseline;
                Bitmap revised;
                try
                {
                    baseline = result.BaselineBitmap;
                    revised = result.RevisedBitmap;
                }
                catch (ObjectDisposedException)
                {
                    DrawStatus(
                        e.Graphics,
                        "Preparando comparación…");
                    return;
                }

                imageBounds = FitImageBounds(
                    baseline.Size,
                    ClientRectangle);
                if (imageBounds.Width <= 0F ||
                    imageBounds.Height <= 0F)
                {
                    return;
                }

                DrawPageFrame(e.Graphics, imageBounds);
                var state = e.Graphics.Save();
                e.Graphics.SetClip(imageBounds);
                e.Graphics.InterpolationMode =
                    InterpolationMode.HighQualityBicubic;
                e.Graphics.PixelOffsetMode =
                    PixelOffsetMode.HighQuality;
                e.Graphics.CompositingQuality =
                    CompositingQuality.HighQuality;

                var preview = draggingManual
                    ? new PointF(
                        manualCurrent.X - manualStart.X,
                        manualCurrent.Y - manualStart.Y)
                    : PointF.Empty;
                var revisedBounds = imageBounds;
                revisedBounds.Offset(preview);

                switch (ViewMode)
                {
                    case PdfPlanComparisonMode.RedCyan:
                        if (DifferenceBitmap != null)
                        {
                            e.Graphics.DrawImage(
                                DifferenceBitmap,
                                imageBounds);
                        }
                        break;

                    case PdfPlanComparisonMode.Baseline:
                        e.Graphics.DrawImage(
                            AlternateShowingRevised
                                ? revised
                                : baseline,
                            AlternateShowingRevised
                                ? revisedBounds
                                : imageBounds);
                        break;

                    case PdfPlanComparisonMode.Split:
                        e.Graphics.DrawImage(
                            baseline,
                            imageBounds);
                        var splitX =
                            imageBounds.Left +
                            imageBounds.Width *
                            Math.Max(
                                0F,
                                Math.Min(1F, SplitPosition));
                        var revisedState =
                            e.Graphics.Save();
                        e.Graphics.SetClip(
                            new RectangleF(
                                splitX,
                                imageBounds.Top,
                                Math.Max(
                                    0F,
                                    imageBounds.Right -
                                    splitX),
                                imageBounds.Height),
                            CombineMode.Intersect);
                        e.Graphics.DrawImage(
                            revised,
                            revisedBounds);
                        e.Graphics.Restore(revisedState);
                        using (var splitPen = new Pen(
                            AccentColor,
                            2F))
                        {
                            e.Graphics.DrawLine(
                                splitPen,
                                splitX,
                                imageBounds.Top,
                                splitX,
                                imageBounds.Bottom);
                        }
                        break;

                    default:
                        e.Graphics.DrawImage(
                            baseline,
                            imageBounds);
                        DrawWithOpacity(
                            e.Graphics,
                            revised,
                            revisedBounds,
                            OverlayOpacity);
                        break;
                }

                e.Graphics.Restore(state);
                DrawRevisionLabels(
                    e.Graphics,
                    imageBounds);
                if (draggingManual)
                {
                    DrawManualDelta(e.Graphics);
                }
            }

            protected override void OnMouseDown(
                MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left ||
                    Result == null ||
                    !imageBounds.Contains(e.Location))
                {
                    return;
                }

                if (ViewMode == PdfPlanComparisonMode.Split)
                {
                    var splitX =
                        imageBounds.Left +
                        imageBounds.Width *
                        SplitPosition;
                    if (Math.Abs(e.X - splitX) <= 12F)
                    {
                        draggingSplit = true;
                        Capture = true;
                        return;
                    }
                }

                if (ManualOffsetEnabled)
                {
                    draggingManual = true;
                    manualStart = e.Location;
                    manualCurrent = e.Location;
                    Capture = true;
                    Cursor = Cursors.SizeAll;
                }
            }

            protected override void OnMouseMove(
                MouseEventArgs e)
            {
                base.OnMouseMove(e);
                if (draggingSplit)
                {
                    RaiseSplitPosition(e.X);
                    return;
                }

                if (draggingManual)
                {
                    manualCurrent = e.Location;
                    Invalidate();
                    return;
                }

                if (ViewMode == PdfPlanComparisonMode.Split &&
                    Result != null)
                {
                    var splitX =
                        imageBounds.Left +
                        imageBounds.Width *
                        SplitPosition;
                    Cursor = Math.Abs(e.X - splitX) <= 12F
                        ? Cursors.VSplit
                        : (ManualOffsetEnabled
                            ? Cursors.SizeAll
                            : Cursors.Default);
                }
                else
                {
                    Cursor = ManualOffsetEnabled
                        ? Cursors.SizeAll
                        : Cursors.Default;
                }
            }

            protected override void OnMouseUp(
                MouseEventArgs e)
            {
                base.OnMouseUp(e);
                if (draggingSplit)
                {
                    draggingSplit = false;
                    Capture = false;
                    RaiseSplitPosition(e.X);
                }

                if (draggingManual)
                {
                    draggingManual = false;
                    Capture = false;
                    Cursor = Cursors.SizeAll;
                    var deltaX =
                        manualCurrent.X - manualStart.X;
                    var deltaY =
                        manualCurrent.Y - manualStart.Y;
                    var scale = Result == null ||
                        Result.PixelSize.Width <= 0
                            ? 1F
                            : imageBounds.Width /
                              Result.PixelSize.Width;
                    manualStart = Point.Empty;
                    manualCurrent = Point.Empty;
                    Invalidate();
                    var handler = ManualOffsetDragged;
                    if (handler != null &&
                        (deltaX != 0 || deltaY != 0))
                    {
                        handler(
                            this,
                            new ManualOffsetDraggedEventArgs(
                                deltaX,
                                deltaY,
                                scale));
                    }
                }
            }

            protected override void OnMouseCaptureChanged(
                EventArgs e)
            {
                base.OnMouseCaptureChanged(e);
                if (!Capture)
                {
                    draggingSplit = false;
                    draggingManual = false;
                    manualStart = Point.Empty;
                    manualCurrent = Point.Empty;
                    Invalidate();
                }
            }

            private void RaiseSplitPosition(float mouseX)
            {
                if (imageBounds.Width <= 0F)
                {
                    return;
                }

                var position =
                    (mouseX - imageBounds.Left) /
                    imageBounds.Width;
                var handler = SplitPositionChanged;
                if (handler != null)
                {
                    handler(
                        this,
                        new SplitPositionChangedEventArgs(
                            Math.Max(
                                0F,
                                Math.Min(1F, position))));
                }
            }

            private void DrawStatus(
                Graphics graphics,
                string text)
            {
                var titleBounds = new Rectangle(
                    Math.Max(20, ClientSize.Width / 2 - 260),
                    Math.Max(40, ClientSize.Height / 2 - 32),
                    Math.Min(
                        520,
                        Math.Max(100, ClientSize.Width - 40)),
                    64);
                using (var font =
                    CreateArchitecturalFont(11F, false))
                using (var brush =
                    new SolidBrush(BodyColor))
                {
                    var format = new StringFormat
                    {
                        Alignment =
                            StringAlignment.Center,
                        LineAlignment =
                            StringAlignment.Center,
                        Trimming =
                            StringTrimming.EllipsisCharacter
                    };
                    graphics.DrawString(
                        text,
                        font,
                        brush,
                        titleBounds,
                        format);
                }

                using (var accent =
                    new SolidBrush(AccentColor))
                {
                    graphics.FillRectangle(
                        accent,
                        ClientSize.Width / 2 - 20,
                        titleBounds.Bottom + 4,
                        40,
                        2);
                }
            }

            private static RectangleF FitImageBounds(
                Size imageSize,
                Rectangle client)
            {
                if (imageSize.Width <= 0 ||
                    imageSize.Height <= 0 ||
                    client.Width <= 0 ||
                    client.Height <= 0)
                {
                    return RectangleF.Empty;
                }

                const int margin = 24;
                var width = Math.Max(
                    1,
                    client.Width - margin * 2);
                var height = Math.Max(
                    1,
                    client.Height - margin * 2);
                var scale = Math.Min(
                    width / (float)imageSize.Width,
                    height / (float)imageSize.Height);
                var targetWidth =
                    imageSize.Width * scale;
                var targetHeight =
                    imageSize.Height * scale;
                return new RectangleF(
                    client.Left +
                    (client.Width - targetWidth) / 2F,
                    client.Top +
                    (client.Height - targetHeight) / 2F,
                    targetWidth,
                    targetHeight);
            }

            private static void DrawPageFrame(
                Graphics graphics,
                RectangleF bounds)
            {
                using (var shadow = new SolidBrush(
                    Color.FromArgb(42, 0, 0, 0)))
                {
                    graphics.FillRectangle(
                        shadow,
                        bounds.X + 6F,
                        bounds.Y + 7F,
                        bounds.Width,
                        bounds.Height);
                }
                using (var paper =
                    new SolidBrush(Color.White))
                {
                    graphics.FillRectangle(paper, bounds);
                }
                using (var border =
                    new Pen(DividerColor))
                {
                    graphics.DrawRectangle(
                        border,
                        bounds.X,
                        bounds.Y,
                        Math.Max(0F, bounds.Width - 1F),
                        Math.Max(0F, bounds.Height - 1F));
                }
            }

            private static void DrawWithOpacity(
                Graphics graphics,
                Image image,
                RectangleF destination,
                float opacity)
            {
                var alpha = Math.Max(
                    0F,
                    Math.Min(1F, opacity));
                using (var attributes =
                    new ImageAttributes())
                {
                    var matrix =
                        new ColorMatrix();
                    matrix.Matrix33 = alpha;
                    attributes.SetColorMatrix(
                        matrix,
                        ColorMatrixFlag.Default,
                        ColorAdjustType.Bitmap);
                    graphics.DrawImage(
                        image,
                        Rectangle.Round(destination),
                        0,
                        0,
                        image.Width,
                        image.Height,
                        GraphicsUnit.Pixel,
                        attributes);
                }
            }

            private void DrawRevisionLabels(
                Graphics graphics,
                RectangleF bounds)
            {
                using (var font =
                    CreateArchitecturalFont(7F, true))
                {
                    var aBounds = new RectangleF(
                        bounds.Left + 8F,
                        bounds.Top + 8F,
                        30F,
                        20F);
                    var bBounds = new RectangleF(
                        bounds.Right - 38F,
                        bounds.Top + 8F,
                        30F,
                        20F);
                    using (var dark =
                        new SolidBrush(
                            Color.FromArgb(
                                210,
                                TitleColor)))
                    using (var accent =
                        new SolidBrush(
                            Color.FromArgb(
                                220,
                                AccentColor)))
                    using (var white =
                        new SolidBrush(Color.White))
                    {
                        graphics.FillRectangle(dark, aBounds);
                        graphics.FillRectangle(accent, bBounds);
                        var format = new StringFormat
                        {
                            Alignment =
                                StringAlignment.Center,
                            LineAlignment =
                                StringAlignment.Center
                        };
                        graphics.DrawString(
                            "A",
                            font,
                            white,
                            aBounds,
                            format);
                        graphics.DrawString(
                            "B",
                            font,
                            white,
                            bBounds,
                            format);
                    }
                }
            }

            private void DrawManualDelta(Graphics graphics)
            {
                var deltaX =
                    manualCurrent.X - manualStart.X;
                var deltaY =
                    manualCurrent.Y - manualStart.Y;
                var text =
                    (deltaX >= 0 ? "+" : string.Empty) +
                    deltaX +
                    " px  /  " +
                    (deltaY >= 0 ? "+" : string.Empty) +
                    deltaY +
                    " px";
                var bounds = new Rectangle(
                    manualCurrent.X + 14,
                    manualCurrent.Y + 14,
                    130,
                    26);
                using (var background =
                    new SolidBrush(
                        Color.FromArgb(230, PaperColor)))
                using (var border =
                    new Pen(AccentColor))
                using (var font =
                    CreateArchitecturalFont(7.5F, true))
                using (var brush =
                    new SolidBrush(TitleColor))
                {
                    graphics.FillRectangle(
                        background,
                        bounds);
                    graphics.DrawRectangle(
                        border,
                        bounds);
                    graphics.DrawString(
                        text,
                        font,
                        brush,
                        bounds,
                        new StringFormat
                        {
                            Alignment =
                                StringAlignment.Center,
                            LineAlignment =
                                StringAlignment.Center
                        });
                }
            }
        }

        internal sealed class SplitPositionChangedEventArgs
            : EventArgs
        {
            public SplitPositionChangedEventArgs(float position)
            {
                Position = position;
            }

            public float Position { get; private set; }
        }

        internal sealed class ManualOffsetDraggedEventArgs
            : EventArgs
        {
            public ManualOffsetDraggedEventArgs(
                float deltaX,
                float deltaY,
                float displayScale)
            {
                DeltaX = deltaX;
                DeltaY = deltaY;
                DisplayScale = displayScale;
            }

            public float DeltaX { get; private set; }

            public float DeltaY { get; private set; }

            public float DisplayScale { get; private set; }
        }
    }
}

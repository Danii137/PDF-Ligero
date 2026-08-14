using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using PdfiumViewer;
using CancellationToken = System.Threading.CancellationToken;
using CancellationTokenSource = System.Threading.CancellationTokenSource;
using PdfiumDocument = PdfiumViewer.PdfDocument;

namespace FirmaAutomatica
{
    internal sealed class PdfViewerForm : Form
    {
        private static readonly Color WindowBackgroundColor = Color.FromArgb(234, 233, 230);
        private static readonly Color HeaderBackgroundColor = Color.FromArgb(250, 249, 247);
        private static readonly Color NavigationBackgroundColor = Color.FromArgb(245, 244, 241);
        private static readonly Color DividerColor = Color.FromArgb(211, 209, 204);
        private static readonly Color TitleColor = Color.FromArgb(31, 31, 29);
        private static readonly Color BodyColor = Color.FromArgb(96, 94, 90);
        private static readonly Color MutedColor = Color.FromArgb(139, 136, 130);
        private static readonly Color AccentColor = Color.FromArgb(238, 91, 61);
        private static readonly Color AccentTextColor = Color.FromArgb(185, 68, 45);
        private static readonly Color AccentHoverColor = Color.FromArgb(207, 72, 47);
        private static readonly Color AccentTintColor = Color.FromArgb(251, 236, 231);
        private static readonly Color PrimaryColor = TitleColor;
        private static readonly Color PrimaryHoverColor = Color.FromArgb(57, 58, 54);
        private static readonly Color SecondaryHoverColor = Color.FromArgb(239, 237, 233);
        private static readonly Color FaintGraphicColor = Color.FromArgb(226, 224, 219);
        private const int MaximumHighlightsOnCurrentPage = 120;
        private const int ExpandedNavigationWidth = 198;
        private const int CollapsedNavigationWidth = 36;
        private const int MaximumOpenTabs = 50;

        // Tope de seguridad del bucle de contraseña. El usuario puede reintentar
        // mientras quiera; esto solo garantiza que el bucle termine.
        private const int MaximumPasswordAttempts = 10;

        private readonly List<string> initialPaths;
        private readonly List<PdfWorkspace> workspaces = new List<PdfWorkspace>();
        private readonly Dictionary<string, PdfWorkspace> workspaceByPath =
            new Dictionary<string, PdfWorkspace>(StringComparer.OrdinalIgnoreCase);

        private readonly Panel headerPanel;
        private readonly Label documentEyebrowLabel;
        private readonly Label documentLabel;
        private readonly Panel documentAccentLine;
        private readonly Button previousPageButton;
        private readonly TextBox currentPageTextBox;
        private readonly Label pageTotalLabel;
        private readonly Button nextPageButton;
        private readonly Label paperEyebrowLabel;
        private readonly Label paperSizeLabel;

        private readonly Panel searchPanel;
        private readonly Label searchCaptionLabel;
        private readonly TextBox searchTextBox;
        private readonly Label searchStatusLabel;
        private readonly Button searchPreviousButton;
        private readonly Button searchNextButton;
        private readonly Button searchCloseButton;

        private readonly Panel contentPanel;
        private readonly Panel workspaceHost;
        private readonly ClosablePdfTabControl documentTabs;
        private readonly FlowLayoutPanel toolRail;
        private readonly Button openToolButton;
        private readonly Button searchToolButton;
        private readonly Button contentEditToolButton;
        private readonly Button ocrToolButton;
        private readonly Button signToolButton;
        private readonly Button mergeToolButton;
        private readonly Button compareToolButton;
        private readonly Button measureToolButton;
        private readonly Button annotateToolButton;
        private readonly Button inlineEditToolButton;
        private ToolStripMenuItem annotateMenuItem;
        private readonly Button moreToolButton;
        private readonly ContextMenuStrip contentEditMenu;
        private readonly ToolStripMenuItem editTextMenuItem;
        private readonly ToolStripMenuItem fillFormMenuItem;
        private readonly ContextMenuStrip moreMenu;
        private readonly ToolStripMenuItem undoMenuItem;
        private readonly ToolStripMenuItem redoMenuItem;
        private readonly ToolStripMenuItem saveCopyMenuItem;
        private readonly ToolStripMenuItem printMenuItem;
        private readonly ToolStripMenuItem fitWidthMenuItem;
        private readonly ToolStripMenuItem zoomInMenuItem;
        private readonly ToolStripMenuItem zoomOutMenuItem;
        private readonly ToolStripMenuItem rotateLeftMenuItem;
        private readonly ToolStripMenuItem rotateRightMenuItem;
        private readonly ToolStripMenuItem ocrMenuItem;
        private readonly ToolStripMenuItem organizePagesMenuItem;
        private readonly ToolStripMenuItem editBookmarksMenuItem;
        private readonly ToolStripMenuItem compareMenuItem;
        private readonly ToolStripMenuItem measureMenuItem;
        private readonly ToolStripMenuItem moreEditTextMenuItem;
        private readonly ToolStripMenuItem moreFillFormMenuItem;
        private readonly ToolTip toolTip;

        private readonly Panel emptyPanel;
        private readonly Label emptyEyebrowLabel;
        private readonly Label emptyTitleLabel;
        private readonly Panel emptyAccentLine;
        private readonly Label emptyBodyLabel;
        private readonly Button emptyOpenButton;
        private readonly Label emptyIndexLabel;
        private readonly Timer pageSyncTimer;
        private readonly BackgroundWorker pageInsertWorker;
        private readonly BackgroundWorker pageOrganizerWorker;
        private readonly BackgroundWorker ocrWorker;

        private PdfWorkspace activeWorkspace;
        private PdfWorkspace pendingWorkspaceActivationAfterContentEdit;
        private PdfPageInsertRequest pendingPageInsertRequest;
        private PdfPageInsertWorkerJob currentPageInsertJob;
        private PdfPageOrganizationUiRequest pendingPageOrganizationRequest;
        private PdfPageOrganizationWorkerJob currentPageOrganizationJob;
        private PdfOcrUiRequest pendingOcrRequest;
        private PdfOcrWorkerJob currentOcrJob;
        private CancellationTokenSource currentOcrCancellation;
        private PdfOcrProgressForm ocrProgressForm;
        private PdfPlanComparisonSurface comparisonSurface;
        private PdfWorkspace comparisonWorkspace;
        private bool initialDocumentsLoaded;
        private bool openingBatch;
        private bool activatingWorkspace;
        private bool closingAll;
        private bool suppressSearchTextChanged;
        private bool pageInsertInProgress;
        private bool pageOrganizationInProgress;
        private bool ocrInProgress;
        private bool bookmarkEditInProgress;
        private bool contentEditInProgress;
        private bool recoverySessionsOffered;

        public PdfViewerForm(string initialPath)
            : this(string.IsNullOrWhiteSpace(initialPath)
                ? new string[0]
                : new[] { initialPath })
        {
        }

        public PdfViewerForm(IEnumerable<string> initialPdfPaths)
        {
            initialPaths = (initialPdfPaths ?? Enumerable.Empty<string>())
                .Select(NormalizePdfPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaximumOpenTabs)
                .ToList();

            Text = "PDF Ligero";
            AppBranding.ApplyWindowIcon(this);
            Width = 1220;
            Height = 840;
            MinimumSize = new Size(900, 620);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = WindowBackgroundColor;
            Font = CreateUiFont(9.25f, FontStyle.Regular);
            KeyPreview = true;
            AllowDrop = true;

            toolTip = new ToolTip
            {
                AutoPopDelay = 5000,
                InitialDelay = 350,
                ReshowDelay = 100,
                ShowAlways = true
            };

            headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 50,
                BackColor = HeaderBackgroundColor
            };
            headerPanel.Controls.Add(new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = DividerColor
            });

            documentEyebrowLabel = new Label
            {
                Left = 16,
                Top = 4,
                Width = 220,
                Height = 14,
                Text = "PDF LIGERO / VISOR",
                ForeColor = AccentTextColor,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = CreateArchitecturalFont(7.5f, true)
            };

            documentLabel = new Label
            {
                Left = 16,
                Top = 17,
                Width = 520,
                Height = 25,
                Text = "Ningún PDF abierto",
                ForeColor = TitleColor,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = CreateArchitecturalFont(10.75f, false)
            };

            documentAccentLine = new Panel
            {
                Left = 16,
                Top = 44,
                Width = 38,
                Height = 2,
                BackColor = AccentColor
            };

            previousPageButton = new Button
            {
                Width = 30,
                Height = 30,
                Text = "\u2039",
                Enabled = false,
                AccessibleName = "Página anterior"
            };
            StylePageButton(previousPageButton);
            previousPageButton.Font = CreateArchitecturalFont(14.5f, false);
            previousPageButton.Click += delegate { NavigatePage(-1); };

            currentPageTextBox = new TextBox
            {
                Width = 42,
                Height = 27,
                Text = string.Empty,
                TextAlign = HorizontalAlignment.Center,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White,
                ForeColor = TitleColor,
                Enabled = false,
                Font = CreateArchitecturalFont(9.5f, true),
                AccessibleName = "Página actual"
            };
            currentPageTextBox.Enter += delegate { currentPageTextBox.SelectAll(); };
            currentPageTextBox.Leave += delegate { CommitPageNumber(); };
            currentPageTextBox.KeyDown += CurrentPageTextBox_KeyDown;

            pageTotalLabel = new Label
            {
                Width = 54,
                Height = 30,
                Text = "/ 0",
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = MutedColor,
                Font = CreateArchitecturalFont(9f, false)
            };

            nextPageButton = new Button
            {
                Width = 30,
                Height = 30,
                Text = "\u203A",
                Enabled = false,
                AccessibleName = "Página siguiente"
            };
            StylePageButton(nextPageButton);
            nextPageButton.Font = CreateArchitecturalFont(14.5f, false);
            nextPageButton.Click += delegate { NavigatePage(1); };

            paperEyebrowLabel = new Label
            {
                Top = 4,
                Width = 238,
                Height = 14,
                Text = "FORMATO DE HOJA",
                ForeColor = AccentTextColor,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleRight,
                Font = CreateArchitecturalFont(7.5f, true),
                Visible = false
            };
            paperSizeLabel = new Label
            {
                Top = 17,
                Width = 238,
                Height = 25,
                Text = "—",
                ForeColor = TitleColor,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleRight,
                Font = CreateArchitecturalFont(9.75f, false),
                Visible = false
            };

            headerPanel.Controls.Add(documentEyebrowLabel);
            headerPanel.Controls.Add(documentLabel);
            headerPanel.Controls.Add(documentAccentLine);
            headerPanel.Controls.Add(previousPageButton);
            headerPanel.Controls.Add(currentPageTextBox);
            headerPanel.Controls.Add(pageTotalLabel);
            headerPanel.Controls.Add(nextPageButton);
            headerPanel.Controls.Add(paperEyebrowLabel);
            headerPanel.Controls.Add(paperSizeLabel);
            headerPanel.Resize += HeaderPanel_Resize;

            searchPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 44,
                BackColor = HeaderBackgroundColor,
                Visible = false
            };
            searchPanel.Controls.Add(new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = DividerColor
            });

            searchCaptionLabel = new Label
            {
                Left = 16,
                Top = 12,
                Width = 50,
                Height = 22,
                Text = "BUSCAR",
                ForeColor = AccentTextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = CreateArchitecturalFont(7.75f, true)
            };

            searchTextBox = new TextBox
            {
                Left = 69,
                Top = 8,
                Width = 380,
                Height = 26,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White,
                ForeColor = TitleColor,
                Font = CreateUiFont(9.5f, FontStyle.Regular),
                AccessibleName = "Texto que buscar"
            };
            searchTextBox.TextChanged += SearchTextBox_TextChanged;
            searchTextBox.KeyDown += SearchTextBox_KeyDown;

            searchStatusLabel = new Label
            {
                Top = 9,
                Width = 160,
                Height = 25,
                Text = "Escribe y pulsa Enter",
                ForeColor = MutedColor,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };

            searchPreviousButton = new Button
            {
                Top = 6,
                Width = 34,
                Height = 29,
                Text = "\u2039",
                AccessibleName = "Coincidencia anterior",
                Enabled = false
            };
            StyleSearchButton(searchPreviousButton);
            searchPreviousButton.Font = CreateArchitecturalFont(13f, false);
            searchPreviousButton.Click += delegate { NavigateSearch(false); };

            searchNextButton = new Button
            {
                Top = 6,
                Width = 34,
                Height = 29,
                Text = "\u203A",
                AccessibleName = "Coincidencia siguiente",
                Enabled = false
            };
            StyleSearchButton(searchNextButton);
            searchNextButton.Font = CreateArchitecturalFont(13f, false);
            searchNextButton.Click += delegate { NavigateSearch(true); };

            searchCloseButton = new Button
            {
                Top = 6,
                Width = 31,
                Height = 29,
                Text = "\u00D7",
                AccessibleName = "Cerrar búsqueda"
            };
            StyleSearchButton(searchCloseButton);
            searchCloseButton.Font = CreateArchitecturalFont(12.5f, false);
            searchCloseButton.Click += delegate { CloseSearchPanel(); };
            toolTip.SetToolTip(searchPreviousButton, "Coincidencia anterior");
            toolTip.SetToolTip(searchNextButton, "Coincidencia siguiente");
            toolTip.SetToolTip(searchCloseButton, "Cerrar búsqueda");

            searchPanel.Controls.Add(searchCaptionLabel);
            searchPanel.Controls.Add(searchTextBox);
            searchPanel.Controls.Add(searchStatusLabel);
            searchPanel.Controls.Add(searchPreviousButton);
            searchPanel.Controls.Add(searchNextButton);
            searchPanel.Controls.Add(searchCloseButton);
            searchPanel.Resize += SearchPanel_Resize;

            contentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = WindowBackgroundColor
            };

            toolRail = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                Width = 48,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(5, 7, 5, 5),
                BackColor = HeaderBackgroundColor
            };
            toolRail.Controls.Add(new Panel
            {
                Width = 22,
                Height = 2,
                BackColor = AccentColor,
                Margin = new Padding(8, 0, 0, 8)
            });

            openToolButton = CreateToolButton(
                "\uE8E5",
                "Abrir uno o varios PDF (Ctrl+O)",
                OpenButton_Click);
            searchToolButton = CreateToolButton(
                "\uE721",
                "Buscar texto (Ctrl+F; empieza al pulsar Enter)",
                delegate { ShowSearchPanel(); });
            contentEditToolButton = CreateToolButton(
                "T",
                "Texto y formularios (Ctrl+E)",
                ContentEditToolButton_Click);
            contentEditToolButton.Font =
                CreateArchitecturalFont(11.5f, true);
            ocrToolButton = CreateToolButton(
                "OCR",
                "OCR, orientación y enderezado",
                OcrToolButton_Click);
            ocrToolButton.Font = CreateArchitecturalFont(7.25f, true);
            signToolButton = CreateToolButton(
                "\uE70F",
                "Firmar el PDF activo (Ctrl+Mayus+S)",
                SignButton_Click);
            mergeToolButton = CreateToolButton(
                "\uE71B",
                "Combinar los PDF abiertos",
                MergeButton_Click);
            compareToolButton = CreateToolButton(
                "\u0394",
                "Comparar revisiones (Ctrl+Mayús+C)",
                CompareToolButton_Click);
            compareToolButton.Font =
                CreateArchitecturalFont(11.5f, true);
            measureToolButton = CreateToolButton(
                "\u2194",
                "Medir plano (Ctrl+Mayús+M)",
                MeasureToolButton_Click);
            measureToolButton.Font =
                CreateArchitecturalFont(11.5f, true);
            inlineEditToolButton = CreateToolButton(
                "\uE932",
                "Editar el texto de la página (Ctrl+Mayús+E)",
                InlineEditToolButton_Click);
            annotateToolButton = CreateToolButton(
                "\uE891",
                "Anotar: rotulador, subrayador y notas (Ctrl+Mayús+A)",
                AnnotateToolButton_Click);
            moreToolButton = CreateToolButton(
                "\uE712",
                "Más herramientas",
                MoreToolButton_Click);

            toolRail.Controls.Add(openToolButton);
            toolRail.Controls.Add(searchToolButton);
            toolRail.Controls.Add(contentEditToolButton);
            toolRail.Controls.Add(ocrToolButton);
            toolRail.Controls.Add(signToolButton);
            toolRail.Controls.Add(mergeToolButton);
            toolRail.Controls.Add(compareToolButton);
            toolRail.Controls.Add(measureToolButton);
            toolRail.Controls.Add(inlineEditToolButton);
            toolRail.Controls.Add(annotateToolButton);
            toolRail.Controls.Add(moreToolButton);

            workspaceHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = WindowBackgroundColor
            };

            documentTabs = new ClosablePdfTabControl
            {
                Dock = DockStyle.Fill,
                Visible = false,
                DisposeClosedPages = false,
                BackColor = WindowBackgroundColor,
                AllowDrop = true
            };
            documentTabs.SelectedIndexChanged += DocumentTabs_SelectedIndexChanged;
            documentTabs.TabCloseRequested += DocumentTabs_TabCloseRequested;
            documentTabs.TabClosed += DocumentTabs_TabClosed;

            emptyPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = WindowBackgroundColor,
                AllowDrop = true
            };

            emptyIndexLabel = new Label
            {
                AutoSize = false,
                Width = 190,
                Height = 130,
                Text = "01",
                TextAlign = ContentAlignment.MiddleRight,
                Font = CreateArchitecturalFont(78f, false),
                ForeColor = FaintGraphicColor,
                BackColor = Color.Transparent
            };

            emptyEyebrowLabel = new Label
            {
                AutoSize = false,
                Width = 560,
                Height = 18,
                Text = "PDF LIGERO / DOCUMENTOS",
                TextAlign = ContentAlignment.MiddleCenter,
                Font = CreateArchitecturalFont(7.75f, true),
                ForeColor = AccentTextColor
            };

            emptyTitleLabel = new Label
            {
                AutoSize = false,
                Width = 560,
                Height = 42,
                Text = "Arrastra uno o varios PDF",
                TextAlign = ContentAlignment.MiddleCenter,
                Font = CreateArchitecturalFont(19.5f, false),
                ForeColor = TitleColor
            };

            emptyAccentLine = new Panel
            {
                Width = 64,
                Height = 2,
                BackColor = AccentColor
            };

            emptyBodyLabel = new Label
            {
                AutoSize = false,
                Width = 600,
                Height = 32,
                Text = "Cada documento se abrirá en una pestaña independiente.",
                TextAlign = ContentAlignment.TopCenter,
                Font = CreateUiFont(9.5f, FontStyle.Regular),
                ForeColor = BodyColor
            };

            emptyOpenButton = new Button
            {
                Width = 120,
                Height = 34,
                Text = "Abrir PDF"
            };
            StyleButton(emptyOpenButton, true);
            emptyOpenButton.Click += OpenButton_Click;

            emptyPanel.Controls.Add(emptyIndexLabel);
            emptyPanel.Controls.Add(emptyEyebrowLabel);
            emptyPanel.Controls.Add(emptyTitleLabel);
            emptyPanel.Controls.Add(emptyAccentLine);
            emptyPanel.Controls.Add(emptyBodyLabel);
            emptyPanel.Controls.Add(emptyOpenButton);
            emptyIndexLabel.SendToBack();
            emptyPanel.Resize += EmptyPanel_Resize;

            workspaceHost.Controls.Add(documentTabs);
            workspaceHost.Controls.Add(emptyPanel);
            contentPanel.Controls.Add(workspaceHost);
            contentPanel.Controls.Add(new Panel
            {
                Dock = DockStyle.Right,
                Width = 1,
                BackColor = DividerColor
            });
            contentPanel.Controls.Add(toolRail);

            Controls.Add(contentPanel);
            Controls.Add(searchPanel);
            Controls.Add(headerPanel);

            contentEditMenu = new ContextMenuStrip
            {
                ShowImageMargin = false,
                Font = CreateUiFont(9.25f, FontStyle.Regular),
                BackColor = HeaderBackgroundColor,
                ForeColor = TitleColor,
                Padding = new Padding(3),
                Renderer = new ToolStripProfessionalRenderer(
                    new ArchitecturalMenuColorTable())
            };
            editTextMenuItem = AddMenuItem(
                contentEditMenu,
                "Cubrir y reemplazar texto…       Ctrl+E",
                delegate { BeginTextEditSelection(); });
            fillFormMenuItem = AddMenuItem(
                contentEditMenu,
                "Rellenar formulario PDF…",
                delegate { FillActivePdfForm(); });
            contentEditMenu.Opening +=
                delegate { RefreshToolAvailability(); };

            moreMenu = new ContextMenuStrip
            {
                ShowImageMargin = false,
                Font = CreateUiFont(9.25f, FontStyle.Regular),
                BackColor = HeaderBackgroundColor,
                ForeColor = TitleColor,
                Padding = new Padding(3),
                Renderer = new ToolStripProfessionalRenderer(
                    new ArchitecturalMenuColorTable())
            };
            undoMenuItem = AddMenuItem(
                moreMenu,
                "Deshacer                         Ctrl+Z",
                delegate { UndoActiveDocument(); });
            redoMenuItem = AddMenuItem(
                moreMenu,
                "Rehacer                           Ctrl+Y",
                delegate { RedoActiveDocument(); });
            moreMenu.Items.Add(new ToolStripSeparator());
            saveCopyMenuItem = AddMenuItem(
                moreMenu,
                "Guardar una copia...     Ctrl+S",
                SaveCopyMenuItem_Click);
            printMenuItem = AddMenuItem(
                moreMenu,
                "Imprimir...                  Ctrl+P",
                PrintMenuItem_Click);
            moreMenu.Items.Add(new ToolStripSeparator());
            fitWidthMenuItem = AddMenuItem(
                moreMenu,
                "Ajustar al ancho",
                delegate { FitActiveDocumentToWidth(); });
            zoomInMenuItem = AddMenuItem(
                moreMenu,
                "Acercar",
                delegate { ZoomActiveDocument(true); });
            zoomOutMenuItem = AddMenuItem(
                moreMenu,
                "Alejar",
                delegate { ZoomActiveDocument(false); });
            rotateLeftMenuItem = AddMenuItem(
                moreMenu,
                "Girar vista a la izquierda",
                delegate { RotateActiveDocument(false); });
            rotateRightMenuItem = AddMenuItem(
                moreMenu,
                "Girar vista a la derecha",
                delegate { RotateActiveDocument(true); });
            moreMenu.Items.Add(new ToolStripSeparator());

            ocrMenuItem = AddMenuItem(
                moreMenu,
                "OCR y enderezado…",
                OcrToolButton_Click);
            organizePagesMenuItem = AddMenuItem(
                moreMenu,
                "Organizar páginas…",
                delegate { ActivatePageOrganizer(); });
            editBookmarksMenuItem = AddMenuItem(
                moreMenu,
                "Editar marcadores…        Ctrl+Mayús+B",
                delegate { EditActiveBookmarks(); });
            compareMenuItem = AddMenuItem(
                moreMenu,
                "Comparar revisiones…      Ctrl+Mayús+C",
                CompareToolButton_Click);
            measureMenuItem = AddMenuItem(
                moreMenu,
                "Medir plano…                  Ctrl+Mayús+M",
                MeasureToolButton_Click);
            annotateMenuItem = AddMenuItem(
                moreMenu,
                "Anotar…                          Ctrl+Mayús+A",
                AnnotateToolButton_Click);
            moreMenu.Items.Add(new ToolStripSeparator());

            var contentEditSubmenu = new ToolStripMenuItem(
                "Texto y formularios")
            {
                Padding = new Padding(10, 4, 10, 4)
            };
            moreEditTextMenuItem = new ToolStripMenuItem(
                "Cubrir y reemplazar texto…       Ctrl+E")
            {
                Padding = new Padding(10, 4, 10, 4)
            };
            moreEditTextMenuItem.Click +=
                delegate { BeginTextEditSelection(); };
            moreFillFormMenuItem = new ToolStripMenuItem(
                "Rellenar formulario PDF…")
            {
                Padding = new Padding(10, 4, 10, 4)
            };
            moreFillFormMenuItem.Click +=
                delegate { FillActivePdfForm(); };
            contentEditSubmenu.DropDownItems.Add(moreEditTextMenuItem);
            contentEditSubmenu.DropDownItems.Add(moreFillFormMenuItem);
            moreMenu.Items.Add(contentEditSubmenu);
            moreMenu.Opening += delegate { RefreshMenuAvailability(); };

            pageSyncTimer = new Timer
            {
                Interval = 150
            };
            pageSyncTimer.Tick += delegate { UpdatePageIndicator(false); };

            pageInsertWorker = new BackgroundWorker
            {
                WorkerReportsProgress = true
            };
            pageInsertWorker.DoWork += PageInsertWorker_DoWork;
            pageInsertWorker.ProgressChanged += PageInsertWorker_ProgressChanged;
            pageInsertWorker.RunWorkerCompleted += PageInsertWorker_RunWorkerCompleted;

            pageOrganizerWorker = new BackgroundWorker
            {
                WorkerReportsProgress = true
            };
            pageOrganizerWorker.DoWork += PageOrganizerWorker_DoWork;
            pageOrganizerWorker.ProgressChanged +=
                PageOrganizerWorker_ProgressChanged;
            pageOrganizerWorker.RunWorkerCompleted +=
                PageOrganizerWorker_RunWorkerCompleted;

            ocrWorker = new BackgroundWorker
            {
                WorkerReportsProgress = true,
                WorkerSupportsCancellation = true
            };
            ocrWorker.DoWork += OcrWorker_DoWork;
            ocrWorker.ProgressChanged += OcrWorker_ProgressChanged;
            ocrWorker.RunWorkerCompleted += OcrWorker_RunWorkerCompleted;

            DragEnter += PdfDragEnter;
            DragDrop += PdfDragDrop;
            emptyPanel.DragEnter += PdfDragEnter;
            emptyPanel.DragDrop += PdfDragDrop;
            documentTabs.DragEnter += PdfDragEnter;
            documentTabs.DragDrop += PdfDragDrop;
            Shown += PdfViewerForm_Shown;
            FormClosing += PdfViewerForm_FormClosing;
            FormClosed += PdfViewerForm_FormClosed;
            KeyDown += PdfViewerForm_KeyDown;

            LayoutHeaderControls();
            LayoutSearchControls();
            LayoutEmptyMessage();
            RefreshEmptyState();
            RefreshToolAvailability();
        }

        internal bool IsClosingForViewerRequests
        {
            get
            {
                return closingAll || IsDisposed || Disposing;
            }
        }

        internal void OpenPdfTabs(IEnumerable<string> paths)
        {
            if (IsClosingForViewerRequests)
            {
                return;
            }

            var normalizedPaths = (paths ?? Enumerable.Empty<string>())
                .Select(NormalizePdfPath)
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (normalizedPaths.Count == 0)
            {
                return;
            }

            var firstWorkspaceToActivate = (PdfWorkspace)null;
            var skippedForLimit = false;

            openingBatch = true;
            try
            {
                foreach (var path in normalizedPaths)
                {
                    PdfWorkspace existingWorkspace;
                    if (workspaceByPath.TryGetValue(path, out existingWorkspace))
                    {
                        // Volver a abrir el archivo es la única forma de que el
                        // diálogo de contraseña reaparezca tras cancelarlo. No se
                        // reintenta al pasear entre pestañas: un modal ahí seria
                        // reentrante con el cierre y la recuperación.
                        if (existingWorkspace.PasswordPromptCancelled &&
                            existingWorkspace.LoadFailed &&
                            !existingWorkspace.IsDisposed)
                        {
                            existingWorkspace.PasswordPromptCancelled = false;
                            existingWorkspace.LoadFailed = false;
                        }

                        if (firstWorkspaceToActivate == null)
                        {
                            firstWorkspaceToActivate = existingWorkspace;
                        }

                        continue;
                    }

                    if (workspaces.Count >= MaximumOpenTabs)
                    {
                        skippedForLimit = true;
                        continue;
                    }

                    var workspace = CreateWorkspace(path);
                    workspaces.Add(workspace);
                    workspaceByPath.Add(path, workspace);
                    documentTabs.TabPages.Add(workspace.TabPage);

                    if (firstWorkspaceToActivate == null)
                    {
                        firstWorkspaceToActivate = workspace;
                    }
                }
            }
            finally
            {
                openingBatch = false;
            }

            RefreshEmptyState();

            if (firstWorkspaceToActivate != null && contentEditInProgress)
            {
                // A second process can deliver files while a modal edit dialog
                // is running because ShowDialog keeps pumping UI messages. Add
                // the tabs now, but keep the edited document visibly active
                // until its transaction has finished.
                pendingWorkspaceActivationAfterContentEdit =
                    firstWorkspaceToActivate;
            }
            else if (firstWorkspaceToActivate != null)
            {
                documentTabs.SelectedTab = firstWorkspaceToActivate.TabPage;
                ActivateSelectedWorkspace();
            }

            if (skippedForLimit)
            {
                ShowMaximumTabsMessage();
            }

            RefreshToolAvailability();
        }

        private void ActivatePendingWorkspaceAfterContentEdit()
        {
            var pendingWorkspace =
                pendingWorkspaceActivationAfterContentEdit;
            pendingWorkspaceActivationAfterContentEdit = null;
            if (pendingWorkspace == null ||
                pendingWorkspace.IsDisposed ||
                closingAll ||
                !documentTabs.TabPages.Contains(pendingWorkspace.TabPage))
            {
                return;
            }

            try
            {
                documentTabs.SelectedTab = pendingWorkspace.TabPage;
                ActivateSelectedWorkspace();
            }
            catch (Exception ex)
            {
                AppLog.Write(
                    "No se pudo activar la pestaña recibida tras editar: " +
                    ex);
            }
        }

        private void PdfViewerForm_Shown(object sender, EventArgs e)
        {
            if (initialDocumentsLoaded)
            {
                return;
            }

            initialDocumentsLoaded = true;
            BeginInvoke(new Action(delegate
            {
                if (initialPaths.Count > 0)
                {
                    OpenPdfTabs(initialPaths);
                }

                OfferRecoverySessions();
            }));
        }

        private void OfferRecoverySessions()
        {
            if (recoverySessionsOffered)
            {
                return;
            }

            recoverySessionsOffered = true;
            IList<PdfRecoveryCandidate> candidates;
            try
            {
                candidates = PdfEditSession.FindRecoverableSessions();
            }
            catch (Exception ex)
            {
                AppLog.Write(
                    "No se pudieron comprobar las sesiones recuperables: " + ex);
                return;
            }

            // A PDF can have more than one interrupted editing session. Offer
            // only the newest one during this launch so that accepting an older
            // recovery can never replace or delete a newer recovery. Older
            // sessions remain untouched and can be offered on a later launch.
            var latestCandidates = candidates
                .OrderByDescending(candidate => candidate.UpdatedUtc)
                .GroupBy(
                    candidate =>
                    {
                        var identity = NormalizePdfPath(candidate.SourcePath);
                        if (string.IsNullOrWhiteSpace(identity))
                        {
                            identity = NormalizePdfPath(candidate.CurrentPath);
                        }

                        return string.IsNullOrWhiteSpace(identity)
                            ? candidate.SessionDirectory
                            : identity;
                    },
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            foreach (var candidate in latestCandidates)
            {
                var answer = MessageBox.Show(
                    this,
                    "Se encontró una edición protegida tras un cierre " +
                    "inesperado de:\r\n\r\n" +
                    candidate.DisplayName +
                    "\r\n\r\nÚltimo cambio: " +
                    candidate.UpdatedUtc.ToLocalTime().ToString("g") +
                    (candidate.SourceChangedSinceEditing
                        ? "\r\n\r\nAviso: el PDF original cambió después. " +
                    "La recuperación se abrirá como edición separada y " +
                          "nunca lo sobrescribirá."
                        : string.Empty) +
                    "\r\n\r\nEl PDF original sigue intacto. ¿Quieres recuperar " +
                    "la edición?\r\n\r\nCancelar la conservará para más tarde.",
                    "Recuperar edición",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button1);
                if (answer == DialogResult.Cancel)
                {
                    continue;
                }

                if (answer == DialogResult.No)
                {
                    PdfEditSession.Discard(candidate);
                    continue;
                }

                try
                {
                    OpenRecoveredSession(
                        PdfEditSession.Restore(candidate),
                        candidate);
                }
                catch (Exception ex)
                {
                    AppLog.Write(
                        "No se pudo recuperar " + candidate.DisplayName +
                        ": " + ex);
                    ShowPdfProblem(
                        "Recuperar edición",
                        "No se pudo recuperar \"" + candidate.DisplayName + "\".",
                        null,
                        ex,
                        candidate.SourcePath);
                }
            }
        }

        private void OpenRecoveredSession(
            PdfEditSession editSession,
            PdfRecoveryCandidate candidate)
        {
            var identityPath = NormalizePdfPath(candidate.SourcePath);
            if (string.IsNullOrWhiteSpace(identityPath))
            {
                identityPath = NormalizePdfPath(candidate.CurrentPath);
            }

            if (string.IsNullOrWhiteSpace(identityPath) ||
                workspaces.Count >= MaximumOpenTabs)
            {
                return;
            }

            PdfWorkspace existingWorkspace;
            if (workspaceByPath.TryGetValue(identityPath, out existingWorkspace))
            {
                documentTabs.SelectedTab = existingWorkspace.TabPage;
                ActivateSelectedWorkspace();
                if (existingWorkspace.EditSession != null)
                {
                    // Never replace an active editing session, even when it is
                    // currently marked as saved. Its saved target may have
                    // changed outside the application and the recovery can be
                    // the only copy that still matches the visible document.
                    editSession.PreserveRecovery();
                    MessageBox.Show(
                        this,
                        "Ya hay otra edición abierta de \"" +
                        candidate.DisplayName +
                        "\".\r\n\r\nLa recuperación encontrada se ha " +
                        "conservado intacta. Guarda o cierra la edición " +
                        "actual y podrás recuperarla en el próximo inicio.",
                        "Recuperación conservada",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                if (!ApplyRevisionToWorkspace(
                        existingWorkspace,
                        editSession.CurrentPath,
                        existingWorkspace.DisplayedPageIndex))
                {
                    throw new InvalidDataException(
                        "No se pudo activar la edición recuperada.");
                }

                existingWorkspace.EditSession = editSession;
                existingWorkspace.ContentPath = editSession.CurrentPath;
                existingWorkspace.LastSavedPath =
                    editSession.LastSavedTargetPath;
                RefreshWorkspaceEditState(existingWorkspace);
                return;
            }

            var workspace = CreateWorkspace(identityPath);
            workspace.EditSession = editSession;
            workspace.ContentPath = editSession.CurrentPath;
            workspace.LastSavedPath = editSession.LastSavedTargetPath;
            workspace.DisplayName = candidate.DisplayName;
            workspace.TabPage.Text = workspace.DisplayName;
            workspace.TabPage.ToolTipText =
                identityPath + "\r\nEdición recuperada";

            workspaces.Add(workspace);
            workspaceByPath.Add(identityPath, workspace);
            documentTabs.TabPages.Add(workspace.TabPage);
            documentTabs.SelectedTab = workspace.TabPage;
            RefreshWorkspaceEditState(workspace);
            RefreshEmptyState();
            ActivateSelectedWorkspace();
        }

        private void OpenButton_Click(object sender, EventArgs e)
        {
            // The text selector owns an application-wide Escape filter while it
            // is active. Leave that mode before opening a native file dialog so
            // Escape belongs to the dialog and closes it on the first press.
            if (IsTextEditSelectionActive)
            {
                DeactivateWorkspaceTextEditSelection(activeWorkspace);
                RefreshToolAvailability();
            }

            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "Abrir PDF";
                dialog.Filter = "Documentos PDF (*.pdf)|*.pdf";
                dialog.CheckFileExists = true;
                dialog.Multiselect = true;
                dialog.RestoreDirectory = true;

                if (activeWorkspace != null && !string.IsNullOrWhiteSpace(activeWorkspace.Path))
                {
                    dialog.InitialDirectory = Path.GetDirectoryName(activeWorkspace.Path);
                }

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    OpenPdfTabs(dialog.FileNames);
                }
            }
        }

        private void SignButton_Click(object sender, EventArgs e)
        {
            if (IsPageStructureOperationInProgress)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }

            var workspace = activeWorkspace;
            if (workspace == null)
            {
                return;
            }

            if (workspace.IsPasswordProtected)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }

            try
            {
                var path = PrepareWorkspaceForExternalOperation(
                    workspace,
                    "firmar");
                if (string.IsNullOrWhiteSpace(path))
                {
                    return;
                }

                Program.RunSigningFiles(new[] { path });
            }
            catch (Exception ex)
            {
                AppLog.Write("Error firmando desde el visor: " + ex);
                ShowPdfProblem(
                    "Firmar PDF",
                    "No se pudo firmar el PDF.",
                    "El PDF original no se ha modificado.",
                    ex,
                    activeWorkspace == null ? null : activeWorkspace.ContentPath);
            }
        }

        private void MergeButton_Click(object sender, EventArgs e)
        {
            if (IsPageStructureOperationInProgress)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }

            var paths = new List<string>();
            foreach (var workspace in workspaces.Where(
                item => !item.IsDisposed))
            {
                var path = PrepareWorkspaceForExternalOperation(
                    workspace,
                    "combinar");
                if (string.IsNullOrWhiteSpace(path))
                {
                    return;
                }

                paths.Add(path);
            }

            Program.RunMergeFiles(paths);
        }

        private void CompareToolButton_Click(object sender, EventArgs e)
        {
            if (comparisonSurface != null)
            {
                ClosePlanComparison(true);
                return;
            }

            BeginPlanComparison();
        }

        private void InlineEditToolButton_Click(object sender, EventArgs e)
        {
            var workspace = GetLoadedActiveWorkspace();
            if (workspace != null &&
                workspace.InlineEdit != null &&
                workspace.InlineEdit.IsActive)
            {
                workspace.InlineEdit.Deactivate();
                RefreshToolAvailability();
                workspace.Viewer.Focus();
                return;
            }

            if (workspace == null ||
                IsPageStructureOperationInProgress)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }

            CancelRectangleZoom(workspace);
            if (searchPanel.Visible)
            {
                CloseSearchPanel();
            }
            if (workspace.Measurement != null && workspace.Measurement.IsActive)
            {
                workspace.Measurement.Deactivate();
            }
            if (workspace.Annotation != null && workspace.Annotation.IsActive)
            {
                workspace.Annotation.Deactivate();
            }

            try
            {
                if (workspace.InlineEdit == null)
                {
                    var destino = workspace;
                    workspace.InlineEdit = new PdfInlineTextEditController(
                        workspace.Viewer.Renderer,
                        delegate { return CanActivateInlineEdit(destino); },
                        delegate(int pagina)
                        {
                            return LoadTextBlocks(destino, pagina);
                        },
                        delegate(string mensaje)
                        {
                            documentLabel.Text = mensaje;
                        });
                    workspace.InlineEdit.EditRequested +=
                        InlineEdit_EditRequested;
                }

                workspace.InlineEdit.Activate();
                RefreshToolAvailability();
                workspace.Viewer.Renderer.Focus();
            }
            catch (Exception ex)
            {
                DisposeWorkspaceInlineEdit(workspace);
                RefreshToolAvailability();
                AppLog.Write("No se pudo iniciar la edición de texto: " + ex);
                ShowPdfProblem(
                    "Editar texto",
                    "No se pudo iniciar la edición.",
                    "El PDF original no se ha modificado.",
                    ex,
                    workspace == null ? null : workspace.ContentPath);
            }
        }

        private bool CanActivateInlineEdit(PdfWorkspace workspace)
        {
            return workspace != null &&
                workspace == activeWorkspace &&
                workspace.IsLoaded &&
                !workspace.IsDisposed &&
                workspace.Document != null &&
                // Sin sesion de edicion no hay donde escribir la revision, y
                // las marcas se perderian al guardar. Es la misma condicion que
                // ya exigen el editor de texto y los marcadores.
                workspace.EditSession != null &&
                !workspace.EditHistoryFaulted &&
                !workspace.IsPasswordProtected &&
                comparisonSurface == null &&
                !searchPanel.Visible &&
                !pageInsertInProgress &&
                !pageOrganizationInProgress &&
                !ocrInProgress &&
                !bookmarkEditInProgress &&
                !contentEditInProgress &&
                !IsTextEditSelectionActive &&
                !activatingWorkspace &&
                !closingAll;
        }

        /// <summary>
        /// Lee las lineas de texto de una pagina. Se abre y se cierra el lector
        /// en cada consulta a proposito: el controlador las guarda en memoria y
        /// solo pregunta una vez por pagina.
        /// </summary>
        private IList<PdfTextBlock> LoadTextBlocks(
            PdfWorkspace workspace,
            int pageNumber)
        {
            if (workspace == null ||
                string.IsNullOrEmpty(workspace.ContentPath) ||
                !File.Exists(workspace.ContentPath))
            {
                return new List<PdfTextBlock>();
            }

            iTextSharp.text.pdf.PdfReader reader = null;
            try
            {
                reader = new iTextSharp.text.pdf.PdfReader(
                    workspace.ContentPath);
                return PdfTextBlockLocator.Locate(reader, pageNumber);
            }
            catch (Exception ex)
            {
                AppLog.Write("No se pudieron leer las líneas de texto: " + ex);
                return new List<PdfTextBlock>();
            }
            finally
            {
                if (reader != null)
                {
                    reader.Close();
                }
            }
        }

        private void InlineEdit_EditRequested(
            object sender,
            PdfInlineEditEventArgs e)
        {
            var workspace = GetLoadedActiveWorkspace();
            if (workspace == null || e == null || e.Request == null)
            {
                return;
            }

            var peticion = e.Request;
            var bloque = peticion.Block;
            var editSession = workspace.EditSession;
            var sourcePath = workspace.ContentPath;
            if (editSession == null || string.IsNullOrEmpty(sourcePath))
            {
                AppLog.Write(
                    "No se puede sustituir el texto: el documento no tiene " +
                    "sesión de edición activa.");
                documentLabel.Text =
                    "No se puede editar el texto de este documento. " +
                    "Guarda una copia y vuelve a abrirla.";
                return;
            }

            string outputPath = null;

            contentEditInProgress = true;
            RefreshToolAvailability();
            try
            {
                PdfTextEditAnalysis analysis = null;
                PdfTextEditRegion region = null;
                using (var progress = new PdfBackgroundOperationForm(
                    "Editar texto",
                    "Leyendo la línea seleccionada…",
                    delegate
                    {
                        var preparation = PdfTextEditService.PrepareSelection(
                            sourcePath,
                            bloque.PageNumber - 1,
                            bloque.Bounds,
                            editSession.CurrentViewIdentity);
                        analysis = preparation.Analysis;
                        region = preparation.Region;
                    }))
                {
                    progress.Run(this);
                }

                if (analysis == null || region == null)
                {
                    return;
                }
                if (!analysis.OpenedWithFullPermissions)
                {
                    throw new UnauthorizedAccessException(
                        "El PDF está protegido y no permite editar su contenido.");
                }
                if (analysis.ContainsXfa)
                {
                    throw new NotSupportedException(
                        PdfTextEditService.XfaUnsupportedMessage);
                }
                if (analysis.ContainsDigitalSignatures)
                {
                    var answer = MessageBox.Show(
                        this,
                        "Este PDF contiene firmas digitales.\r\n\r\n" +
                        PdfTextEditService.DigitalSignatureWarning +
                        "\r\n\r\n¿Quieres continuar y crear una revisión nueva?",
                        "Editar un PDF firmado",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button2);
                    if (answer != DialogResult.Yes)
                    {
                        return;
                    }
                }

                var replacement = new PdfTextReplacement(
                    region,
                    peticion.NewText)
                {
                    ReplaceInPlace = true,
                    // Cambiar el formato obliga a reescribir con una fuente del
                    // sistema: sustituir la cadena conservaria el formato viejo.
                    ForceSystemFont = peticion.FormatChanged,
                    PreferredFontName = peticion.FontName,
                    FontFamily = PdfSystemFontCatalog.GuessFamily(
                        peticion.FontName),
                    Bold = peticion.Bold,
                    Italic = peticion.Italic,
                    FontSizePoints = peticion.FontSizePoints,
                    MinimumFontSizePoints = 4F,
                    AutoFit = false,
                    TextColor = peticion.Color,
                    CoverOriginal = false,
                    PaddingPoints = 0F
                };

                long estimado;
                try
                {
                    estimado = checked(
                        Math.Max(0L, analysis.SourceLength) +
                        (2L * 1024L * 1024L));
                }
                catch (OverflowException)
                {
                    estimado = long.MaxValue;
                }

                outputPath = editSession.ReserveRevisionPath(estimado);
                using (var progress = new PdfBackgroundOperationForm(
                    "Editar texto",
                    "Sustituyendo el texto…",
                    delegate
                    {
                        PdfTextEditService.Save(
                            sourcePath,
                            outputPath,
                            analysis,
                            replacement);
                    }))
                {
                    progress.Run(this);
                }

                ApplyContentRevision(
                    workspace,
                    editSession,
                    sourcePath,
                    outputPath,
                    bloque.PageNumber - 1,
                    "Editar texto",
                    "Texto sustituido. El original no se ha modificado.");
                outputPath = null;

                if (workspace.InlineEdit != null)
                {
                    workspace.InlineEdit.InvalidateBlocks();
                }
            }
            catch (Exception ex)
            {
                AppLog.Write("No se pudo sustituir el texto: " + ex);
                ShowPdfProblem(
                    "Editar texto",
                    "No se pudo sustituir el texto.",
                    "El PDF original no se ha modificado.",
                    ex,
                    sourcePath);
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(outputPath) &&
                    !string.Equals(
                        editSession.CurrentPath,
                        outputPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    editSession.CancelReservedRevision(outputPath);
                }

                contentEditInProgress = false;
                RefreshToolAvailability();
            }
        }

        private void DisposeWorkspaceInlineEdit(PdfWorkspace workspace)
        {
            if (workspace == null || workspace.InlineEdit == null)
            {
                return;
            }

            var controller = workspace.InlineEdit;
            workspace.InlineEdit = null;
            controller.EditRequested -= InlineEdit_EditRequested;
            controller.Dispose();
        }

        private void AnnotateToolButton_Click(object sender, EventArgs e)
        {
            var workspace = GetLoadedActiveWorkspace();
            if (workspace != null &&
                workspace.Annotation != null &&
                workspace.Annotation.IsActive)
            {
                // Al cerrar se guarda lo que haya, sin preguntar. Antes salia un
                // aviso que solo dejaba descartar, que era justo lo contrario de
                // lo que se quiere al terminar de anotar. Si algo no gusta,
                // Ctrl+Z deshace la revision.
                if (workspace.Annotation.HasPending)
                {
                    AnnotationController_SaveRequested(
                        workspace.Annotation,
                        EventArgs.Empty);
                }

                workspace.Annotation.Deactivate();
                RefreshToolAvailability();
                workspace.Viewer.Focus();
                return;
            }

            if (workspace == null ||
                IsPageStructureOperationInProgress)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }

            CancelRectangleZoom(workspace);
            if (searchPanel.Visible)
            {
                CloseSearchPanel();
            }
            if (workspace.Measurement != null &&
                workspace.Measurement.IsActive)
            {
                workspace.Measurement.Deactivate();
            }

            try
            {
                if (workspace.Annotation == null)
                {
                    var destino = workspace;
                    workspace.Annotation = new PdfAnnotationController(
                        workspace.Viewer.Renderer,
                        delegate { return CanActivateAnnotation(destino); },
                        delegate { return Environment.UserName; },
                        delegate(string mensaje)
                        {
                            documentLabel.Text = mensaje;
                        },
                        delegate(int pagina)
                        {
                            return LoadTextBlocks(destino, pagina);
                        });
                    workspace.Annotation.SaveRequested +=
                        AnnotationController_SaveRequested;
                    workspace.Annotation.PendingChanged +=
                        AnnotationController_PendingChanged;
                    // Las marcas que ya tiene el documento se dibujan tambien:
                    // PDFium no las pinta, asi que las pinta la aplicacion.
                    workspace.Annotation.LoadExisting(workspace.ContentPath);
                }

                workspace.Annotation.Activate();
                RefreshToolAvailability();
                workspace.Viewer.Renderer.Focus();
                documentLabel.Text =
                    "Anotando: elige rotulador, subrayador o nota en la barra.";
            }
            catch (Exception ex)
            {
                DisposeWorkspaceAnnotation(workspace);
            DisposeWorkspaceInlineEdit(workspace);
                RefreshToolAvailability();
                AppLog.Write("No se pudo iniciar la anotación: " + ex);
                ShowPdfProblem(
                    "Anotar",
                    "No se pudo iniciar la anotación.",
                    "El PDF original no se ha modificado.",
                    ex,
                    workspace == null ? null : workspace.ContentPath);
            }
        }

        private bool CanActivateAnnotation(PdfWorkspace workspace)
        {
            return workspace != null &&
                workspace == activeWorkspace &&
                workspace.IsLoaded &&
                !workspace.IsDisposed &&
                workspace.Document != null &&
                // Sin sesion de edicion no hay donde escribir la revision, y
                // las marcas se perderian al guardar. Es la misma condicion que
                // ya exigen el editor de texto y los marcadores.
                workspace.EditSession != null &&
                !workspace.EditHistoryFaulted &&
                comparisonSurface == null &&
                !searchPanel.Visible &&
                !pageInsertInProgress &&
                !pageOrganizationInProgress &&
                !ocrInProgress &&
                !bookmarkEditInProgress &&
                !contentEditInProgress &&
                !IsTextEditSelectionActive &&
                !activatingWorkspace &&
                !closingAll;
        }

        private void AnnotationController_PendingChanged(
            object sender,
            EventArgs e)
        {
            RefreshToolAvailability();
        }

        private void AnnotationController_SaveRequested(
            object sender,
            EventArgs e)
        {
            var workspace = GetLoadedActiveWorkspace();
            if (workspace == null ||
                workspace.Annotation == null ||
                !workspace.Annotation.HasPending)
            {
                return;
            }

            var editSession = workspace.EditSession;
            var sourcePath = workspace.ContentPath;
            if (editSession == null || string.IsNullOrEmpty(sourcePath))
            {
                // Las marcas se quedan donde estan, sin perderse, y se dice por
                // que en la barra en vez de con una ventana.
                AppLog.Write(
                    "No se pueden guardar las marcas: el documento no tiene " +
                    "sesión de edición activa.");
                documentLabel.Text =
                    "No se pueden guardar las marcas en este documento. " +
                    "Las marcas siguen ahí; guarda una copia y vuelve a abrirla.";
                return;
            }

            // El ejecutable va optimizado y los metodos pequeños se integran,
            // asi que la pila de una excepcion aqui pierde los marcos y no dice
            // donde fallo. Se deja rastro del paso alcanzado para poder situarlo.
            var paso = "inicio";
            var descripcion = workspace.Annotation.Pending.Describe();
            string outputPath = null;

            contentEditInProgress = true;
            RefreshToolAvailability();
            try
            {
                paso = "estimar tamaño";
                long estimado;
                try
                {
                    estimado = checked(
                        new FileInfo(sourcePath).Length + (2L * 1024L * 1024L));
                }
                catch (OverflowException)
                {
                    estimado = long.MaxValue;
                }

                paso = "reservar revisión";
                outputPath = editSession.ReserveRevisionPath(estimado);
                paso = "escribir las marcas";
                PdfAnnotationSaveResult resultado = null;
                using (var progress = new PdfBackgroundOperationForm(
                    "Anotar",
                    "Guardando las marcas…",
                    delegate
                    {
                        resultado = PdfAnnotationService.Save(
                            sourcePath,
                            outputPath,
                            workspace.Annotation.Pending,
                            editSession.CurrentViewIdentity);
                    }))
                {
                    progress.Run(this);
                }

                paso = "aplicar la revisión";
                ApplyContentRevision(
                    workspace,
                    editSession,
                    sourcePath,
                    outputPath,
                    GetWorkspacePageIndexForComparison(workspace),
                    "Anotar",
                    descripcion + " guardado. El original no se ha modificado.");
                outputPath = null;

                paso = "releer las marcas";
                workspace.Annotation.ClearPending();
                workspace.Annotation.LoadExisting(workspace.ContentPath);

                // Aviso en la barra, no en una ventana: el trabajo ya esta
                // hecho y no hay nada que decidir, asi que interrumpir sobra.
                if (resultado != null &&
                    resultado.DigitalSignaturesInvalidated)
                {
                    documentLabel.Text =
                        "Marcas guardadas. " +
                        PdfAnnotationService.DigitalSignatureWarning;
                }
            }
            catch (Exception ex)
            {
                AppLog.Write(
                    "No se pudieron guardar las marcas (paso: " + paso + "): " +
                    ex);
                ShowPdfProblem(
                    "Anotar",
                    "No se pudieron guardar las marcas.",
                    "El PDF original no se ha modificado.",
                    ex,
                    sourcePath);
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(outputPath) &&
                    !string.Equals(
                        editSession.CurrentPath,
                        outputPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    editSession.CancelReservedRevision(outputPath);
                }

                contentEditInProgress = false;
                RefreshToolAvailability();
            }
        }

        private void DisposeWorkspaceAnnotation(PdfWorkspace workspace)
        {
            if (workspace == null || workspace.Annotation == null)
            {
                return;
            }

            var controller = workspace.Annotation;
            workspace.Annotation = null;
            controller.SaveRequested -= AnnotationController_SaveRequested;
            controller.PendingChanged -= AnnotationController_PendingChanged;
            controller.Dispose();
        }

        private void MeasureToolButton_Click(object sender, EventArgs e)
        {
            var workspace = GetLoadedActiveWorkspace();
            if (workspace != null &&
                workspace.Measurement != null &&
                workspace.Measurement.IsActive)
            {
                workspace.Measurement.Deactivate();
                RefreshToolAvailability();
                workspace.Viewer.Focus();
                return;
            }

            if (workspace == null ||
                IsPageStructureOperationInProgress)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }

            CancelRectangleZoom(workspace);
            if (searchPanel.Visible)
            {
                CloseSearchPanel();
            }

            try
            {
                if (workspace.Measurement == null)
                {
                    workspace.Measurement =
                        new PdfMeasurementController(
                            workspace.Viewer.Renderer,
                            delegate
                            {
                                return CanActivateMeasurement(
                                    workspace);
                            },
                            AccentColor,
                            AccentTextColor,
                            HeaderBackgroundColor);
                    workspace.Measurement.ActiveStateChanged +=
                        MeasurementController_ActiveStateChanged;
                    workspace.Measurement.StatusChanged +=
                        MeasurementController_StatusChanged;
                }

                workspace.Measurement.NotifyActivePage(
                    GetWorkspacePageIndexForComparison(workspace));
                workspace.Measurement.Activate();
                RefreshToolAvailability();
                workspace.Viewer.Renderer.Focus();
            }
            catch (Exception ex)
            {
                DisposeWorkspaceMeasurement(workspace);
                RefreshToolAvailability();
                AppLog.Write(
                    "No se pudo iniciar la medición del plano: " + ex);
                ShowPdfProblem(
                    "Medir plano",
                    "No se pudo iniciar la medición.",
                    "El PDF original no se ha modificado.",
                    ex,
                    workspace == null ? null : workspace.ContentPath);
            }
        }

        private void MeasurementController_ActiveStateChanged(
            object sender,
            EventArgs e)
        {
            var controller = sender as PdfMeasurementController;
            if (activeWorkspace != null &&
                activeWorkspace.Measurement == controller)
            {
                if (controller != null && controller.IsActive)
                {
                    documentLabel.Text = controller.StatusText;
                }
                else
                {
                    RefreshWorkspaceEditState(activeWorkspace);
                }
            }

            RefreshToolAvailability();
        }

        private void MeasurementController_StatusChanged(
            object sender,
            EventArgs e)
        {
            var controller = sender as PdfMeasurementController;
            if (controller == null ||
                activeWorkspace == null ||
                activeWorkspace.Measurement != controller ||
                !controller.IsActive)
            {
                return;
            }

            documentLabel.Text = controller.StatusText;
        }

        private bool CanActivateMeasurement(PdfWorkspace workspace)
        {
            return workspace != null &&
                workspace == activeWorkspace &&
                workspace.IsLoaded &&
                !workspace.IsDisposed &&
                workspace.Document != null &&
                comparisonSurface == null &&
                !searchPanel.Visible &&
                !pageInsertInProgress &&
                !pageOrganizationInProgress &&
                !ocrInProgress &&
                !bookmarkEditInProgress &&
                !contentEditInProgress &&
                !IsTextEditSelectionActive &&
                !activatingWorkspace &&
                !closingAll;
        }

        private void DeactivateWorkspaceMeasurement(
            PdfWorkspace workspace)
        {
            if (workspace != null &&
                workspace.Measurement != null &&
                workspace.Measurement.IsActive)
            {
                workspace.Measurement.Deactivate();
            }
        }

        private void DisposeWorkspaceMeasurement(
            PdfWorkspace workspace)
        {
            DisposeWorkspaceAnnotation(workspace);

            if (workspace == null ||
                workspace.Measurement == null)
            {
                return;
            }

            var controller = workspace.Measurement;
            workspace.Measurement = null;
            controller.ActiveStateChanged -=
                MeasurementController_ActiveStateChanged;
            controller.StatusChanged -=
                MeasurementController_StatusChanged;
            controller.Dispose();
        }

        private void TextEditSelection_ActiveStateChanged(
            object sender,
            EventArgs e)
        {
            var controller = sender as PdfTextEditSelectionController;
            if (activeWorkspace != null &&
                activeWorkspace.TextEditSelection == controller)
            {
                if (controller != null && controller.IsActive)
                {
                    documentLabel.Text =
                        "Editar texto · arrastra una zona y pulsa T en el centro";
                }
                else
                {
                    RefreshWorkspaceEditState(activeWorkspace);
                }
            }

            RefreshToolAvailability();
        }

        private void TextEditSelection_SelectionAccepted(
            object sender,
            PdfTextEditSelectionEventArgs e)
        {
            var workspace = activeWorkspace;
            var controller = sender as PdfTextEditSelectionController;
            if (workspace == null ||
                controller == null ||
                workspace.TextEditSelection != controller ||
                e == null)
            {
                return;
            }

            controller.Deactivate();
            BeginSelectedTextEdit(workspace, e.Selection);
        }

        private void BeginSelectedTextEdit(
            PdfWorkspace workspace,
            PdfRectangle selection)
        {
            if (workspace == null ||
                workspace.IsDisposed ||
                workspace.Document == null ||
                workspace.EditSession == null ||
                workspace.EditHistoryFaulted ||
                !selection.IsValid ||
                contentEditInProgress)
            {
                return;
            }

            var sourcePath = workspace.ContentPath;
            var editSession = workspace.EditSession;
            var expectedViewIdentity = editSession.CurrentViewIdentity;
            var preferredPageIndex = Math.Max(
                0,
                Math.Min(
                    workspace.Document.PageCount - 1,
                    selection.Page));
            PdfTextEditAnalysis analysis = null;
            PdfTextEditRegion region = null;
            string detectedText = string.Empty;
            string extractError = string.Empty;
            PdfTextStyle detectedStyle = null;
            PdfDirectTextCapability directCapability = null;
            string outputPath = null;

            contentEditInProgress = true;
            RefreshToolAvailability();
            try
            {
                using (var progress = new PdfBackgroundOperationForm(
                    "Editar texto",
                    "Leyendo solo la zona seleccionada…",
                    delegate
                    {
                        var preparation =
                            PdfTextEditService.PrepareSelection(
                                sourcePath,
                                selection.Page,
                                selection.Bounds,
                                expectedViewIdentity);
                        analysis = preparation.Analysis;
                        region = preparation.Region;
                        detectedText = preparation.ExtractedText;
                        extractError = preparation.ExtractionError;
                        detectedStyle = preparation.DetectedStyle;
                        directCapability = preparation.DirectCapability;
                    }))
                {
                    progress.Run(this);
                }

                if (!IsEditContextCurrent(
                        workspace,
                        editSession,
                        sourcePath))
                {
                    throw new InvalidOperationException(
                        "El documento cambió mientras se abría el editor.");
                }
                if (analysis == null || region == null)
                {
                    throw new InvalidDataException(
                        "No se pudo interpretar la zona seleccionada.");
                }
                if (!analysis.OpenedWithFullPermissions)
                {
                    throw new UnauthorizedAccessException(
                        "El PDF está protegido y no permite editar su contenido.");
                }
                if (analysis.ContainsXfa)
                {
                    throw new NotSupportedException(
                        PdfTextEditService.XfaUnsupportedMessage);
                }
                if (!string.IsNullOrWhiteSpace(extractError))
                {
                    AppLog.Write(
                        "La zona se puede editar, pero no se pudo precargar " +
                        "su texto: " + extractError);
                }

                if (analysis.ContainsDigitalSignatures)
                {
                    var answer = MessageBox.Show(
                        this,
                        "Este PDF contiene firmas digitales.\r\n\r\n" +
                        PdfTextEditService.DigitalSignatureWarning +
                        "\r\n\r\nEl original seguirá intacto. " +
                        "¿Quieres continuar con la edición y crear una " +
                        "revisión nueva?",
                        "Editar un PDF firmado",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button2);
                    if (answer != DialogResult.Yes)
                    {
                        return;
                    }
                }

                if (detectedText != null && detectedText.Length > 12000)
                {
                    detectedText = detectedText.Substring(0, 12000);
                }

                // La tipografia leida del propio PDF llega preseleccionada, para
                // que el reemplazo se parezca al resto de la pagina sin tener
                // que acertarla a ojo.
                var initialState = new PdfTextEditDialogState
                {
                    Text = detectedText ?? string.Empty,
                    CoverBackground = true,
                    AutoFit = true,
                    FontSizePoints = 11M
                };
                // Sustituir de verdad solo se ofrece si la zona lo admite;
                // si no, se explica el motivo en el consejo de la casilla.
                if (directCapability != null)
                {
                    initialState.CanReplaceInPlace =
                        directCapability.CanReplace;
                    initialState.ReplaceInPlaceReason =
                        directCapability.Reason;
                    initialState.ReplaceInPlace = directCapability.CanReplace;
                }

                if (detectedStyle != null &&
                    !string.IsNullOrEmpty(detectedStyle.FontName))
                {
                    initialState.DetectedFontName = detectedStyle.FontName;
                    initialState.DetectedDescription = detectedStyle.Describe();
                    initialState.BaseFontName = detectedStyle.FontName;
                    initialState.Bold = detectedStyle.Bold;
                    initialState.Italic = detectedStyle.Italic;
                    initialState.TextColor = detectedStyle.Color;
                    if (detectedStyle.FontSizePoints >= 4F &&
                        detectedStyle.FontSizePoints <= 144F)
                    {
                        initialState.FontSizePoints = Math.Round(
                            (decimal)detectedStyle.FontSizePoints,
                            1);
                        // Con el tamaño real medido, encogerlo automaticamente
                        // solo haria que dejase de parecerse al original.
                        initialState.AutoFit = false;
                    }
                }
                PdfTextEditDialogState selectedState;
                using (var editor = new PdfTextEditDialog(
                    initialState,
                    "PÁGINA " + (selection.Page + 1)))
                {
                    if (editor.ShowDialog(this) != DialogResult.OK ||
                        editor.Result == null)
                    {
                        return;
                    }

                    selectedState = editor.Result;
                }

                var usaDetectada = selectedState.UsesDetectedFont;
                var replacement = new PdfTextReplacement(
                    region,
                    selectedState.Text)
                {
                    // Con la fuente detectada, la familia generica sigue siendo
                    // necesaria: es el respaldo si esa fuente no esta instalada
                    // o no cubre alguno de los caracteres escritos.
                    FontFamily = usaDetectada
                        ? PdfSystemFontCatalog.GuessFamily(
                            selectedState.BaseFontName)
                        : string.Equals(
                                selectedState.BaseFontName,
                                PdfTextEditDialogState.TimesFontName,
                                StringComparison.Ordinal)
                            ? PdfTextEditFontFamily.Serif
                            : string.Equals(
                                    selectedState.BaseFontName,
                                    PdfTextEditDialogState.CourierFontName,
                                    StringComparison.Ordinal)
                                ? PdfTextEditFontFamily.Monospace
                                : PdfTextEditFontFamily.SansSerif,
                    PreferredFontName = usaDetectada
                        ? selectedState.BaseFontName
                        : null,
                    ReplaceInPlace = selectedState.ReplaceInPlace,
                    Bold = selectedState.Bold,
                    Italic = selectedState.Italic,
                    FontSizePoints = (float)selectedState.FontSizePoints,
                    MinimumFontSizePoints = 4F,
                    AutoFit = selectedState.AutoFit,
                    Alignment =
                        selectedState.Alignment == HorizontalAlignment.Center
                            ? PdfTextEditAlignment.Center
                            : selectedState.Alignment ==
                                HorizontalAlignment.Right
                                ? PdfTextEditAlignment.Right
                                : PdfTextEditAlignment.Left,
                    CoverOriginal = selectedState.CoverBackground,
                    TextColor = selectedState.TextColor,
                    CoverColor = selectedState.CoverColor,
                    PaddingPoints = 2F
                };

                long estimatedOutputBytes = 0;
                try
                {
                    estimatedOutputBytes = checked(
                        Math.Max(0L, analysis.SourceLength) +
                        (2L * 1024L * 1024L));
                }
                catch (OverflowException)
                {
                    estimatedOutputBytes = long.MaxValue;
                }

                outputPath = editSession.ReserveRevisionPath(
                    estimatedOutputBytes);
                PdfTextEditSaveResult result = null;
                using (var progress = new PdfBackgroundOperationForm(
                    "Aplicando texto",
                    "Creando una revisión recuperable…",
                    delegate
                    {
                        result = PdfTextEditService.Save(
                            sourcePath,
                            outputPath,
                            analysis,
                            replacement);
                    }))
                {
                    progress.Run(this);
                }

                if (result == null ||
                    string.IsNullOrWhiteSpace(result.OutputPath) ||
                    !File.Exists(result.OutputPath))
                {
                    throw new InvalidDataException(
                        "La revisión de texto no se pudo comprobar.");
                }

                ApplyContentRevision(
                    workspace,
                    editSession,
                    sourcePath,
                    result.OutputPath,
                    preferredPageIndex,
                    "Texto editado en página " + result.PageNumber,
                    "texto actualizado");
                outputPath = null;
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrWhiteSpace(outputPath) &&
                    !string.Equals(
                        editSession.CurrentPath,
                        outputPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    editSession.CancelReservedRevision(outputPath);
                }

                AppLog.Write(
                    "No se pudo aplicar la edición de texto: " + ex);
                ShowPdfProblem(
                    "Editar texto",
                    "No se pudo aplicar la edición de texto.",
                    "El PDF original no se ha modificado.",
                    ex,
                    sourcePath);
            }
            finally
            {
                contentEditInProgress = false;
                ActivatePendingWorkspaceAfterContentEdit();
                RefreshToolAvailability();
            }
        }

        private bool CanCaptureTextEditSelection(PdfWorkspace workspace)
        {
            return workspace != null &&
                workspace == activeWorkspace &&
                workspace.IsLoaded &&
                !workspace.IsDisposed &&
                workspace.Document != null &&
                comparisonSurface == null &&
                !searchPanel.Visible &&
                !pageInsertInProgress &&
                !pageOrganizationInProgress &&
                !ocrInProgress &&
                !bookmarkEditInProgress &&
                !contentEditInProgress &&
                !IsMeasurementActive &&
                !activatingWorkspace &&
                !closingAll;
        }

        private static void DeactivateWorkspaceTextEditSelection(
            PdfWorkspace workspace)
        {
            if (workspace != null &&
                workspace.TextEditSelection != null &&
                workspace.TextEditSelection.IsActive)
            {
                workspace.TextEditSelection.Deactivate();
            }
        }

        private void DisposeWorkspaceTextEditSelection(
            PdfWorkspace workspace)
        {
            if (workspace == null ||
                workspace.TextEditSelection == null)
            {
                return;
            }

            var controller = workspace.TextEditSelection;
            workspace.TextEditSelection = null;
            controller.ActiveStateChanged -=
                TextEditSelection_ActiveStateChanged;
            controller.SelectionAccepted -=
                TextEditSelection_SelectionAccepted;
            controller.Dispose();
        }

        private void BeginPlanComparison()
        {
            if (IsPageStructureOperationInProgress)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }

            var workspace = GetLoadedActiveWorkspace();
            if (workspace == null ||
                workspace.IsPasswordProtected ||
                string.IsNullOrWhiteSpace(workspace.ContentPath) ||
                !File.Exists(workspace.ContentPath))
            {
                return;
            }

            CancelRectangleZoom(workspace);
            if (searchPanel.Visible)
            {
                CloseSearchPanel();
            }

            var baselinePageIndex =
                GetWorkspacePageIndexForComparison(workspace);
            var baseline = new PdfPlanComparisonSource(
                workspace.DisplayName,
                workspace.ContentPath,
                baselinePageIndex);
            var candidates = GetPlanComparisonCandidates(workspace);
            PdfPlanComparisonSurface surface = null;

            try
            {
                surface = new PdfPlanComparisonSurface(
                    baseline,
                    candidates)
                {
                    Bounds = workspace.TabPage.ClientRectangle,
                    Anchor =
                        AnchorStyles.Top |
                        AnchorStyles.Bottom |
                        AnchorStyles.Left |
                        AnchorStyles.Right
                };
                surface.CloseRequested +=
                    PlanComparisonSurface_CloseRequested;
                surface.StatusChanged +=
                    PlanComparisonSurface_StatusChanged;

                comparisonWorkspace = workspace;
                comparisonSurface = surface;
                workspace.TabPage.Controls.Add(surface);
                surface.BringToFront();

                pageSyncTimer.Stop();
                previousPageButton.Enabled = false;
                currentPageTextBox.Enabled = false;
                nextPageButton.Enabled = false;
                ApplyPlanComparisonHeader(
                    surface.HeaderTitle,
                    surface.HeaderDetail);
                RefreshToolAvailability();
                surface.Begin();
                surface.Focus();
            }
            catch (Exception ex)
            {
                if (surface != null)
                {
                    surface.CloseRequested -=
                        PlanComparisonSurface_CloseRequested;
                    surface.StatusChanged -=
                        PlanComparisonSurface_StatusChanged;
                    surface.CancelAndDispose();
                }

                comparisonSurface = null;
                comparisonWorkspace = null;
                RefreshWorkspaceEditState(workspace);
                UpdatePageIndicator(true);
                pageSyncTimer.Start();
                RefreshToolAvailability();
                AppLog.Write(
                    "No se pudo iniciar la comparación de planos: " + ex);
                ShowPdfProblem(
                    "Comparar revisiones",
                    "No se pudo iniciar la comparación.",
                    "Los PDF originales no se han modificado.",
                    ex,
                    workspace == null ? null : workspace.ContentPath);
            }
        }

        private List<PdfPlanComparisonSource>
            GetPlanComparisonCandidates(PdfWorkspace baselineWorkspace)
        {
            var candidates =
                new List<PdfPlanComparisonSource>();
            var seenPaths = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            if (baselineWorkspace != null &&
                !string.IsNullOrWhiteSpace(
                    baselineWorkspace.ContentPath))
            {
                seenPaths.Add(
                    Path.GetFullPath(
                        baselineWorkspace.ContentPath));
            }

            foreach (TabPage tabPage in documentTabs.TabPages)
            {
                var workspace = tabPage == null
                    ? null
                    : tabPage.Tag as PdfWorkspace;
                if (workspace == null ||
                    workspace == baselineWorkspace ||
                    workspace.IsDisposed ||
                    string.IsNullOrWhiteSpace(
                        workspace.ContentPath) ||
                    !File.Exists(workspace.ContentPath))
                {
                    continue;
                }

                var fullPath =
                    Path.GetFullPath(workspace.ContentPath);
                if (!seenPaths.Add(fullPath))
                {
                    continue;
                }

                candidates.Add(
                    new PdfPlanComparisonSource(
                        workspace.DisplayName,
                        fullPath,
                        GetWorkspacePageIndexForComparison(
                            workspace)));
            }

            return candidates;
        }

        private static int GetWorkspacePageIndexForComparison(
            PdfWorkspace workspace)
        {
            if (workspace == null)
            {
                return 0;
            }

            if (workspace.DisplayedPageIndex >= 0)
            {
                return workspace.DisplayedPageIndex;
            }

            try
            {
                return workspace.Viewer == null
                    ? 0
                    : Math.Max(
                        0,
                        workspace.Viewer.Renderer.Page);
            }
            catch
            {
                return 0;
            }
        }

        private void PlanComparisonSurface_CloseRequested(
            object sender,
            EventArgs e)
        {
            if (sender == comparisonSurface)
            {
                ClosePlanComparison(true);
            }
        }

        private void PlanComparisonSurface_StatusChanged(
            object sender,
            PdfPlanComparisonStatusEventArgs e)
        {
            if (sender != comparisonSurface || e == null)
            {
                return;
            }

            ApplyPlanComparisonHeader(
                e.HeaderTitle,
                e.HeaderDetail);
        }

        private void ApplyPlanComparisonHeader(
            string title,
            string detail)
        {
            var safeTitle = string.IsNullOrWhiteSpace(title)
                ? "Comparación de revisiones"
                : title;
            var safeDetail = string.IsNullOrWhiteSpace(detail)
                ? "COMPARACIÓN DE PLANOS"
                : "COMPARACIÓN / " + detail.ToUpperInvariant();
            documentEyebrowLabel.Text = safeDetail;
            documentLabel.Text = safeTitle;
            documentLabel.Tag = null;
            Text = safeTitle + " - PDF Ligero";
            toolTip.SetToolTip(
                documentLabel,
                safeTitle +
                (string.IsNullOrWhiteSpace(detail)
                    ? string.Empty
                    : "\r\n" + detail));
        }

        private void ClosePlanComparison(bool restoreFocus)
        {
            var surface = comparisonSurface;
            var workspace = comparisonWorkspace;
            comparisonSurface = null;
            comparisonWorkspace = null;
            if (surface == null)
            {
                return;
            }

            surface.CloseRequested -=
                PlanComparisonSurface_CloseRequested;
            surface.StatusChanged -=
                PlanComparisonSurface_StatusChanged;
            if (surface.Parent != null)
            {
                surface.Parent.Controls.Remove(surface);
            }

            try
            {
                surface.CancelAndDispose();
            }
            catch (Exception ex)
            {
                AppLog.Write(
                    "Error cerrando la comparación de planos: " + ex);
            }

            if (activeWorkspace != null &&
                !activeWorkspace.IsDisposed)
            {
                RefreshWorkspaceEditState(activeWorkspace);
                toolTip.SetToolTip(
                    documentLabel,
                    activeWorkspace.ContentPath);
                UpdatePageIndicator(true);
                if (activeWorkspace.IsLoaded && !closingAll)
                {
                    pageSyncTimer.Start();
                    if (restoreFocus)
                    {
                        activeWorkspace.Viewer.Focus();
                    }
                }
            }
            else if (!closingAll)
            {
                BindNoDocumentUi();
            }

            if (workspace != null &&
                workspace != activeWorkspace &&
                !workspace.IsDisposed)
            {
                CancelRectangleZoom(workspace);
            }

            RefreshToolAvailability();
        }

        private void MoreToolButton_Click(object sender, EventArgs e)
        {
            RefreshMenuAvailability();
            moreMenu.Show(
                moreToolButton,
                new Point(0, 0),
                ToolStripDropDownDirection.Left);
        }

        private void ContentEditToolButton_Click(object sender, EventArgs e)
        {
            if (IsTextEditSelectionActive)
            {
                DeactivateWorkspaceTextEditSelection(activeWorkspace);
                RefreshToolAvailability();
                if (activeWorkspace != null)
                {
                    activeWorkspace.Viewer.Focus();
                }

                return;
            }

            RefreshToolAvailability();
            contentEditMenu.Show(
                contentEditToolButton,
                new Point(0, 0),
                ToolStripDropDownDirection.Left);
        }

        private void BeginTextEditSelection()
        {
            var workspace = GetLoadedActiveWorkspace();
            if (workspace != null &&
                workspace.TextEditSelection != null &&
                workspace.TextEditSelection.IsActive)
            {
                DeactivateWorkspaceTextEditSelection(workspace);
                RefreshToolAvailability();
                workspace.Viewer.Focus();
                return;
            }

            if (workspace == null ||
                workspace.Document == null ||
                workspace.EditSession == null ||
                workspace.EditHistoryFaulted ||
                workspace.IsPasswordProtected ||
                IsPageStructureOperationInProgress)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }

            CancelRectangleZoom(workspace);
            DeactivateWorkspaceMeasurement(workspace);
            if (searchPanel.Visible)
            {
                CloseSearchPanel();
            }

            try
            {
                if (workspace.TextEditSelection == null)
                {
                    workspace.TextEditSelection =
                        new PdfTextEditSelectionController(
                            workspace.Viewer.Renderer,
                            delegate
                            {
                                return CanCaptureTextEditSelection(
                                    workspace);
                            },
                            AccentColor,
                            AccentTextColor,
                            HeaderBackgroundColor);
                    workspace.TextEditSelection.ActiveStateChanged +=
                        TextEditSelection_ActiveStateChanged;
                    workspace.TextEditSelection.SelectionAccepted +=
                        TextEditSelection_SelectionAccepted;
                }

                if (!workspace.TextEditSelection.Activate())
                {
                    throw new InvalidOperationException(
                        "El selector no está disponible en la vista actual.");
                }

                documentLabel.Text =
                    "Editar texto · arrastra una zona y pulsa T en el centro";
                RefreshToolAvailability();
                workspace.Viewer.Renderer.Focus();
            }
            catch (Exception ex)
            {
                DisposeWorkspaceTextEditSelection(workspace);
                RefreshToolAvailability();
                AppLog.Write(
                    "No se pudo iniciar la edición de texto: " + ex);
                ShowPdfProblem(
                    "Editar texto",
                    "No se pudo iniciar la edición de texto.",
                    "El PDF original no se ha modificado.",
                    ex,
                    workspace == null ? null : workspace.ContentPath);
            }
        }

        private void FillActivePdfForm()
        {
            var workspace = GetLoadedActiveWorkspace();
            if (workspace == null ||
                workspace.Document == null ||
                workspace.EditSession == null ||
                workspace.EditHistoryFaulted ||
                workspace.IsPasswordProtected ||
                IsPageStructureOperationInProgress)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }

            CancelRectangleZoom(workspace);
            DeactivateWorkspaceMeasurement(workspace);
            if (searchPanel.Visible)
            {
                CloseSearchPanel();
            }

            var sourcePath = workspace.ContentPath;
            var editSession = workspace.EditSession;
            var preferredPageIndex = Math.Max(
                0,
                Math.Min(
                    workspace.Document.PageCount - 1,
                    workspace.Viewer.Renderer.Page));
            PdfAcroFormDocument formDocument = null;
            string outputPath = null;
            contentEditInProgress = true;
            RefreshToolAvailability();
            try
            {
                using (var progress = new PdfBackgroundOperationForm(
                    "Rellenar formulario",
                    "Leyendo los campos interactivos…",
                    delegate
                    {
                        formDocument =
                            PdfAcroFormService.Analyze(sourcePath);
                    }))
                {
                    progress.Run(this);
                }

                if (!IsEditContextCurrent(
                        workspace,
                        editSession,
                        sourcePath))
                {
                    throw new InvalidOperationException(
                        "El documento cambió mientras se abría el formulario.");
                }
                if (formDocument == null)
                {
                    throw new InvalidDataException(
                        "No se pudo leer la estructura del formulario.");
                }
                if (formDocument.Fields.Count == 0)
                {
                    MessageBox.Show(
                        this,
                        "Este PDF no contiene campos interactivos AcroForm.\r\n\r\n" +
                        "Si el formulario está dibujado como una página plana, " +
                        "usa «Cubrir y reemplazar texto» para escribir en sus " +
                        "casillas.",
                        "Rellenar formulario",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                IList<PdfAcroFormFieldChange> changes;
                using (var editor = new PdfAcroFormFillForm(formDocument))
                {
                    if (editor.ShowDialog(this) != DialogResult.OK ||
                        !editor.HasChanges)
                    {
                        return;
                    }

                    changes = editor.Changes;
                }

                if (changes == null || changes.Count == 0)
                {
                    return;
                }

                long estimatedOutputBytes = 0;
                try
                {
                    estimatedOutputBytes = checked(
                        Math.Max(0L, formDocument.SourceLength) +
                        (2L * 1024L * 1024L));
                }
                catch (OverflowException)
                {
                    estimatedOutputBytes = long.MaxValue;
                }

                outputPath = editSession.ReserveRevisionPath(
                    estimatedOutputBytes);
                PdfAcroFormSaveResult result = null;
                using (var progress = new PdfBackgroundOperationForm(
                    "Rellenar formulario",
                    "Guardando los campos sin aplanarlos…",
                    delegate
                    {
                        result = PdfAcroFormService.Apply(
                            sourcePath,
                            outputPath,
                            formDocument,
                            changes);
                    }))
                {
                    progress.Run(this);
                }

                if (result == null ||
                    string.IsNullOrWhiteSpace(result.OutputPath) ||
                    !File.Exists(result.OutputPath))
                {
                    throw new InvalidDataException(
                        "La revisión del formulario no se pudo comprobar.");
                }

                ApplyContentRevision(
                    workspace,
                    editSession,
                    sourcePath,
                    result.OutputPath,
                    preferredPageIndex,
                    "Formulario rellenado · " +
                        result.ChangedFieldCount +
                        (result.ChangedFieldCount == 1
                            ? " campo"
                            : " campos"),
                    result.ChangedFieldCount +
                        (result.ChangedFieldCount == 1
                            ? " campo actualizado"
                            : " campos actualizados"));
                outputPath = null;
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrWhiteSpace(outputPath) &&
                    !string.Equals(
                        editSession.CurrentPath,
                        outputPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    editSession.CancelReservedRevision(outputPath);
                }

                AppLog.Write(
                    "No se pudo rellenar el formulario PDF: " + ex);
                ShowPdfProblem(
                    "Rellenar formulario",
                    "No se pudo rellenar el formulario.",
                    "El PDF original no se ha modificado.",
                    ex,
                    sourcePath);
            }
            finally
            {
                contentEditInProgress = false;
                ActivatePendingWorkspaceAfterContentEdit();
                RefreshToolAvailability();
            }
        }

        private void SaveCopyMenuItem_Click(object sender, EventArgs e)
        {
            if (IsPageStructureOperationInProgress)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }

            var workspace = GetLoadedActiveWorkspace();
            if (workspace == null)
            {
                return;
            }

            SaveWorkspaceCopy(workspace);
        }

        private bool SaveWorkspaceCopy(PdfWorkspace workspace)
        {
            if (workspace == null ||
                workspace.IsDisposed ||
                !workspace.IsLoaded ||
                string.IsNullOrWhiteSpace(workspace.ContentPath) ||
                !File.Exists(workspace.ContentPath))
            {
                return false;
            }

            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = workspace.EditSession != null &&
                    workspace.EditSession.HasUnsavedChanges
                        ? "Guardar PDF editado"
                        : "Guardar una copia";
                dialog.Filter = "Documento PDF (*.pdf)|*.pdf";
                dialog.AddExtension = true;
                dialog.DefaultExt = "pdf";
                dialog.OverwritePrompt = true;
                dialog.InitialDirectory = Path.GetDirectoryName(workspace.Path);
                dialog.FileName =
                    Path.GetFileNameWithoutExtension(workspace.Path) +
                    (workspace.EditSession != null &&
                     workspace.EditSession.HasUnsavedChanges
                        ? " - editado.pdf"
                        : " - copia.pdf");

                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return false;
                }

                var targetPath = NormalizePdfPath(dialog.FileName);
                if (string.Equals(targetPath, workspace.Path, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(
                        this,
                        "Elige otro nombre para no sustituir el PDF que está abierto.",
                        "Guardar una copia",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return false;
                }

                UseWaitCursor = true;
                try
                {
                    ReleaseSavedCopyVerificationLease(workspace);
                    string savedFullHash = null;
                    using (var progress = new PdfBackgroundOperationForm(
                        "Guardando PDF",
                        "Creando y verificando la copia…",
                        delegate
                        {
                            savedFullHash =
                                PdfAtomicFileService.SaveCopyWithContentHash(
                                    workspace.ContentPath,
                                    targetPath);
                        }))
                    {
                        progress.Run(this);
                    }

                    workspace.LastSavedPath = targetPath;
                    var savedInfo = new FileInfo(targetPath);
                    workspace.LastSavedLength = savedInfo.Length;
                    workspace.LastSavedWriteUtcTicks =
                        savedInfo.LastWriteTimeUtc.Ticks;
                    workspace.LastSavedFingerprint =
                        PdfAtomicFileService.ComputeContentFingerprint(
                            targetPath);
                    workspace.LastSavedFullHash = savedFullHash;
                    if (workspace.EditSession != null &&
                        !workspace.EditHistoryFaulted)
                    {
                        workspace.EditSession.MarkCurrentRevisionSaved(
                            targetPath);
                    }
                    else if (workspace.EditHistoryFaulted)
                    {
                        workspace.FaultedChangesSaved = true;
                    }

                    RefreshWorkspaceEditState(workspace);
                    AppLog.Write("Copia de PDF guardada: " + targetPath);
                    return true;
                }
                catch (Exception ex)
                {
                    AppLog.Write("No se pudo guardar una copia del PDF: " + ex);
                    ShowPdfProblem(
                        "Guardar una copia",
                        "No se pudo guardar la copia.",
                        "El PDF original no se ha modificado.",
                        ex,
                        targetPath);
                    return false;
                }
                finally
                {
                    UseWaitCursor = false;
                }
            }
        }

        private string PrepareWorkspaceForExternalOperation(
            PdfWorkspace workspace,
            string operationName)
        {
            if (workspace == null || workspace.IsDisposed)
            {
                return null;
            }

            if (workspace.EditSession != null &&
                (workspace.EditSession.HasUnsavedChanges ||
                 (workspace.EditHistoryFaulted &&
                  !workspace.FaultedChangesSaved)))
            {
                documentTabs.SelectedTab = workspace.TabPage;
                ActivateSelectedWorkspace();
                var answer = MessageBox.Show(
                    this,
                    "Este PDF tiene cambios sin guardar.\r\n\r\n" +
                    "Guarda antes una copia para poder " + operationName +
                    " el resultado sin trabajar dentro de la recuperacion temporal.",
                    "Guardar cambios",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Information,
                    MessageBoxDefaultButton.Button1);
                if (answer != DialogResult.OK ||
                    !SaveWorkspaceCopy(workspace))
                {
                    return null;
                }
            }

            if (!string.IsNullOrWhiteSpace(workspace.LastSavedPath) &&
                IsLastSavedCopyUnchanged(workspace, false))
            {
                return workspace.LastSavedPath;
            }

            if (!string.IsNullOrWhiteSpace(workspace.LastSavedPath) &&
                !string.Equals(
                    workspace.ContentPath,
                    workspace.Path,
                    StringComparison.OrdinalIgnoreCase))
            {
                var answer = MessageBox.Show(
                    this,
                    "La copia guardada anteriormente cambió o ya no existe.\r\n\r\n" +
                    "Guarda de nuevo el PDF visible antes de " +
                    operationName + "lo.",
                    "La copia guardada cambió",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button1);
                if (answer != DialogResult.OK ||
                    !SaveWorkspaceCopy(workspace))
                {
                    return null;
                }

                return IsLastSavedCopyUnchanged(workspace, false)
                    ? workspace.LastSavedPath
                    : null;
            }

            return File.Exists(workspace.ContentPath)
                ? workspace.ContentPath
                : null;
        }

        private bool IsLastSavedCopyUnchanged(
            PdfWorkspace workspace,
            bool holdVerificationLease)
        {
            ReleaseSavedCopyVerificationLease(workspace);
            if (!IsLastSavedCopyMetadataAndSamplesUnchanged(workspace))
            {
                return false;
            }

            string actualFullHash = null;
            FileStream verificationLease = null;
            var verificationCompleted = false;
            try
            {
                using (var progress = new PdfBackgroundOperationForm(
                    "Comprobando copia",
                    "Verificando que la copia guardada sigue intacta…",
                    delegate
                    {
                        verificationLease = new FileStream(
                            workspace.LastSavedPath,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.Read,
                            1024 * 1024,
                            FileOptions.SequentialScan);
                        actualFullHash =
                            PdfAtomicFileService.ComputeFullContentHash(
                                verificationLease);
                    }))
                {
                    progress.Run(this);
                }

                verificationCompleted = true;
            }
            catch (Exception ex)
            {
                AppLog.Write(
                    "No se pudo comprobar la copia guardada: " + ex);
                return false;
            }
            finally
            {
                if (verificationLease != null &&
                    (!holdVerificationLease ||
                     !verificationCompleted ||
                     !string.Equals(
                         actualFullHash,
                         workspace.LastSavedFullHash,
                         StringComparison.Ordinal)))
                {
                    verificationLease.Dispose();
                    verificationLease = null;
                }
            }

            if (!string.Equals(
                    actualFullHash,
                    workspace.LastSavedFullHash,
                    StringComparison.Ordinal))
            {
                return false;
            }

            if (holdVerificationLease)
            {
                if (verificationLease == null)
                {
                    return false;
                }

                workspace.LastSavedVerificationLease =
                    verificationLease;
            }

            return true;
        }

        private static void ReleaseSavedCopyVerificationLease(
            PdfWorkspace workspace)
        {
            if (workspace == null ||
                workspace.LastSavedVerificationLease == null)
            {
                return;
            }

            var lease = workspace.LastSavedVerificationLease;
            workspace.LastSavedVerificationLease = null;
            try
            {
                lease.Dispose();
            }
            catch
            {
            }
        }

        private static bool IsLastSavedCopyMetadataAndSamplesUnchanged(
            PdfWorkspace workspace)
        {
            if (workspace == null ||
                string.IsNullOrWhiteSpace(workspace.LastSavedPath) ||
                workspace.LastSavedLength < 0 ||
                workspace.LastSavedWriteUtcTicks < 0 ||
                string.IsNullOrWhiteSpace(
                    workspace.LastSavedFingerprint) ||
                string.IsNullOrWhiteSpace(
                    workspace.LastSavedFullHash))
            {
                return false;
            }

            try
            {
                var info = new FileInfo(workspace.LastSavedPath);
                return info.Exists &&
                    info.Length == workspace.LastSavedLength &&
                    info.LastWriteTimeUtc.Ticks ==
                        workspace.LastSavedWriteUtcTicks &&
                    string.Equals(
                        PdfAtomicFileService.ComputeContentFingerprint(
                            workspace.LastSavedPath),
                        workspace.LastSavedFingerprint,
                        StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        private void PrintMenuItem_Click(object sender, EventArgs e)
        {
            if (IsPageStructureOperationInProgress)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }

            var workspace = GetLoadedActiveWorkspace();
            if (workspace == null)
            {
                return;
            }

            CancelRectangleZoom(workspace);
            try
            {
                // Directo a las opciones: imprimir es lo que se viene a hacer
                // con Ctrl+P. La vista previa sigue a un clic, desde ese mismo
                // cuadro, para quien quiera repasar antes de gastar papel.
                using (var opciones = new PdfPrintOptionsDialog(
                    workspace.Document,
                    workspace.DisplayName,
                    Math.Max(0, workspace.DisplayedPageIndex) + 1))
                {
                    opciones.ShowDialog(this);
                }
            }
            catch (Exception ex)
            {
                AppLog.Write("No se pudo imprimir el PDF: " + ex);
                ShowPdfProblem(
                    "Imprimir",
                    "No se pudo imprimir.",
                    null,
                    ex,
                    activeWorkspace == null ? null : activeWorkspace.ContentPath);
            }
        }

        private void FitActiveDocumentToWidth()
        {
            if (comparisonSurface != null)
            {
                return;
            }

            var workspace = GetLoadedActiveWorkspace();
            if (workspace == null)
            {
                return;
            }

            CancelRectangleZoom(workspace);
            workspace.Viewer.ZoomMode = PdfViewerZoomMode.FitWidth;
            workspace.Viewer.Focus();
        }

        private void ZoomActiveDocument(bool zoomIn)
        {
            if (comparisonSurface != null)
            {
                return;
            }

            var workspace = GetLoadedActiveWorkspace();
            if (workspace == null)
            {
                return;
            }

            CancelRectangleZoom(workspace);
            if (zoomIn)
            {
                workspace.Viewer.Renderer.ZoomIn();
            }
            else
            {
                workspace.Viewer.Renderer.ZoomOut();
            }

            workspace.Viewer.Focus();
        }

        private void RotateActiveDocument(bool clockwise)
        {
            if (comparisonSurface != null)
            {
                return;
            }

            var workspace = GetLoadedActiveWorkspace();
            if (workspace == null)
            {
                return;
            }

            CancelRectangleZoom(workspace);
            if (clockwise)
            {
                workspace.Viewer.Renderer.RotateRight();
            }
            else
            {
                workspace.Viewer.Renderer.RotateLeft();
            }

            UpdatePaperSizeIndicator(
                workspace,
                Math.Max(0, workspace.DisplayedPageIndex));
            workspace.Viewer.Focus();
        }

        private void ActivatePageOrganizer()
        {
            var workspace = GetLoadedActiveWorkspace();
            if (workspace == null ||
                IsPageStructureOperationInProgress)
            {
                return;
            }

            ShowNavigationMode(workspace, false);
            if (workspace.NavigationCollapsed)
            {
                SetNavigationCollapsed(workspace, false);
            }

            workspace.Thumbnails.Focus();
            documentLabel.Text =
                "Organizar · Ctrl/Mayús para seleccionar · " +
                "arrastra para mover · Supr para eliminar";
        }

        private void EditActiveBookmarks()
        {
            var workspace = GetLoadedActiveWorkspace();
            if (workspace == null ||
                workspace.Document == null ||
                workspace.EditSession == null ||
                workspace.EditHistoryFaulted ||
                workspace.IsPasswordProtected ||
                IsPageStructureOperationInProgress)
            {
                return;
            }

            CancelRectangleZoom(workspace);
            var sourcePath = workspace.ContentPath;
            var editSession = workspace.EditSession;
            var preferredPageIndex = Math.Max(
                0,
                Math.Min(
                    workspace.Document.PageCount - 1,
                    workspace.Viewer.Renderer.Page));
            PdfBookmarkDocument bookmarkDocument = null;
            string outputPath = null;
            bookmarkEditInProgress = true;
            RefreshToolAvailability();
            try
            {
                if (workspace.BookmarkDocument != null &&
                    string.Equals(
                        workspace.BookmarkDocument.SourcePath,
                        sourcePath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    bookmarkDocument = workspace.BookmarkDocument;
                }
                else
                {
                    using (var progress = new PdfBackgroundOperationForm(
                        "Editar marcadores",
                        "Leyendo la estructura de marcadores…",
                        delegate
                        {
                            bookmarkDocument =
                                PdfBookmarkService.Load(sourcePath);
                        }))
                    {
                        progress.Run(this);
                    }
                    workspace.BookmarkDocument = bookmarkDocument;
                }

                if (!IsEditContextCurrent(
                        workspace,
                        editSession,
                        sourcePath))
                {
                    throw new InvalidOperationException(
                        "El documento cambió mientras se abría el editor.");
                }
                if (!bookmarkDocument.OpenedWithFullPermissions)
                {
                    throw new UnauthorizedAccessException(
                        "El PDF está protegido y no permite editar " +
                        "sus marcadores.");
                }

                var visibleDestination =
                    CaptureCurrentBookmarkDestination(
                        workspace,
                        bookmarkDocument);
                PdfBookmarkDocument editedDocument;
                bool hasChanges;
                using (var editor = new PdfBookmarkEditorForm(
                    bookmarkDocument,
                    visibleDestination))
                {
                    var answer = editor.ShowDialog(this);
                    hasChanges = editor.HasChanges;
                    editedDocument = editor.EditedDocument;
                    if (answer != DialogResult.OK ||
                        !hasChanges ||
                        editedDocument == null)
                    {
                        return;
                    }
                }

                if (editedDocument.ContainsDigitalSignatures)
                {
                    var answer = MessageBox.Show(
                        this,
                        "Este PDF contiene firmas digitales.\r\n\r\n" +
                        "El original seguirá intacto. La copia conservará " +
                        "la firma previa incrustada, pero constará como una " +
                        "modificación posterior y su estado puede depender " +
                        "de las restricciones de esa firma.\r\n\r\n" +
                        "¿Quieres aplicar los cambios de marcadores?",
                        "Editar marcadores de un PDF firmado",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button2);
                    if (answer != DialogResult.Yes)
                    {
                        return;
                    }
                }

                long estimatedOutputBytes = 0;
                try
                {
                    estimatedOutputBytes =
                        new FileInfo(sourcePath).Length;
                }
                catch
                {
                }

                outputPath = editSession.ReserveRevisionPath(
                    estimatedOutputBytes);
                PdfBookmarkSaveResult result = null;
                using (var progress = new PdfBackgroundOperationForm(
                    "Aplicando marcadores",
                    "Guardando la estructura sin alterar las páginas…",
                    delegate
                    {
                        result = PdfBookmarkService.Save(
                            sourcePath,
                            outputPath,
                            editedDocument);
                    }))
                {
                    progress.Run(this);
                }

                if (result == null ||
                    string.IsNullOrWhiteSpace(result.OutputPath) ||
                    !File.Exists(result.OutputPath))
                {
                    throw new InvalidDataException(
                        "La revisión de marcadores no se pudo comprobar.");
                }
                if (!IsEditContextCurrent(
                        workspace,
                        editSession,
                        sourcePath))
                {
                    throw new InvalidOperationException(
                        "El documento cambió antes de activar los marcadores.");
                }

                ApplyBookmarkRevision(
                    workspace,
                    editSession,
                    sourcePath,
                    result,
                    preferredPageIndex,
                    visibleDestination);
                outputPath = null;
            }
            catch (Exception ex)
            {
                if (IsBookmarkSourceChanged(ex))
                {
                    if (!TryReloadBookmarkSourceAfterChange(
                            workspace,
                            sourcePath,
                            preferredPageIndex))
                    {
                        MarkBookmarkSourceReloadFailure(
                            workspace,
                            editSession);
                    }
                }

                if (!string.IsNullOrWhiteSpace(outputPath) &&
                    !string.Equals(
                        editSession.CurrentPath,
                        outputPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    editSession.CancelReservedRevision(outputPath);
                }

                AppLog.Write(
                    "No se pudo editar la estructura de marcadores: " +
                    ex);
                ShowPdfProblem(
                    "Editar marcadores",
                    "No se pudieron aplicar los marcadores.",
                    "El PDF original no se ha modificado.",
                    ex,
                    sourcePath);
            }
            finally
            {
                bookmarkEditInProgress = false;
                RefreshToolAvailability();
            }
        }

        private static bool IsBookmarkSourceChanged(Exception error)
        {
            var current = error;
            while (current != null)
            {
                if (string.Equals(
                        current.Message,
                        PdfBookmarkService.SourceChangedMessage,
                        StringComparison.Ordinal))
                {
                    return true;
                }

                current = current.InnerException;
            }

            return false;
        }

        private static void InvalidateBookmarkCache(
            PdfWorkspace workspace)
        {
            if (workspace == null || workspace.IsDisposed)
            {
                return;
            }

            workspace.BookmarkDocument = null;
            workspace.BookmarksLoaded = false;
            if (workspace.BookmarksTree != null &&
                !workspace.BookmarksTree.IsDisposed)
            {
                workspace.BookmarksTree.Nodes.Clear();
                workspace.BookmarksTree.Nodes.Add(
                    new TreeNode(
                        "El PDF cambió · vuelve a abrir los marcadores")
                    {
                        ForeColor = BodyColor
                    });
            }
        }

        private bool TryReloadBookmarkSourceAfterChange(
            PdfWorkspace workspace,
            string sourcePath,
            int preferredPageIndex)
        {
            InvalidateBookmarkCache(workspace);
            if (workspace == null ||
                workspace.IsDisposed ||
                string.IsNullOrWhiteSpace(sourcePath) ||
                !File.Exists(sourcePath))
            {
                return false;
            }

            if (!ApplyRevisionToWorkspace(
                    workspace,
                    sourcePath,
                    preferredPageIndex))
            {
                return false;
            }

            return workspace.IsLoaded &&
                !workspace.IsDisposed &&
                string.Equals(
                    workspace.ContentPath,
                    sourcePath,
                    StringComparison.OrdinalIgnoreCase);
        }

        private static void MarkBookmarkSourceReloadFailure(
            PdfWorkspace workspace,
            PdfEditSession editSession)
        {
            if (workspace == null || workspace.IsDisposed)
            {
                return;
            }

            workspace.EditHistoryFaulted = true;
            workspace.FaultedChangesSaved = false;
            workspace.DeleteRecoveryOnClose = false;
            InvalidateBookmarkCache(workspace);
            if (workspace.BookmarksTree != null &&
                !workspace.BookmarksTree.IsDisposed)
            {
                workspace.BookmarksTree.Nodes.Clear();
                workspace.BookmarksTree.Nodes.Add(
                    new TreeNode(
                        "Vista desactualizada · cierra y vuelve a abrir el PDF")
                    {
                        ForeColor = AccentTextColor
                    });
            }

            if (editSession != null)
            {
                try
                {
                    editSession.PreserveRecovery();
                }
                catch
                {
                }
            }
        }

        private void ApplyBookmarkRevision(
            PdfWorkspace workspace,
            PdfEditSession editSession,
            string sourcePath,
            PdfBookmarkSaveResult result,
            int preferredPageIndex,
            PdfBookmarkDestination visibleDestination)
        {
            PdfiumDocument preparedDocument = null;
            PdfEditSession.RevisionCommit revisionCommit = null;
            try
            {
                preparedDocument = PdfDocumentOpenService.Load(result.OutputPath);
                if (preparedDocument.PageCount < 1 ||
                    preparedDocument.PageCount !=
                        workspace.Document.PageCount)
                {
                    throw new InvalidDataException(
                        "La revisión no conserva todas las páginas.");
                }

                revisionCommit = editSession.BeginRevisionCommit(
                    result.OutputPath,
                    "Marcadores editados");
                var documentToApply = preparedDocument;
                preparedDocument = null;
                if (!ApplyRevisionToWorkspace(
                        workspace,
                        result.OutputPath,
                        preferredPageIndex,
                        documentToApply))
                {
                    CompensateFailedRevision(
                        workspace,
                        editSession,
                        sourcePath,
                        revisionCommit);
                    throw new InvalidOperationException(
                        "La copia se creó, pero no pudo activarse " +
                        "con seguridad.");
                }

                revisionCommit.Complete();
                revisionCommit = null;

                try
                {
                    ShowNavigationMode(workspace, true);
                    if (workspace.NavigationCollapsed)
                    {
                        SetNavigationCollapsed(workspace, false);
                    }
                    NavigateToBookmark(workspace, visibleDestination);
                }
                catch (Exception ex)
                {
                    AppLog.Write(
                        "Marcadores aplicados; no se pudo restaurar " +
                        "la navegación: " + ex);
                }

                try
                {
                    editSession.CleanupObsoleteRevisions(
                        workspace.ContentPath);
                }
                catch (Exception ex)
                {
                    AppLog.Write(
                        "Marcadores aplicados; la limpieza se aplazó: " +
                        ex);
                }

                try
                {
                    RefreshWorkspaceEditState(workspace);
                    if (workspace == activeWorkspace)
                    {
                        documentLabel.Text =
                            workspace.DisplayName + " · " +
                            result.BookmarkCount +
                            (result.BookmarkCount == 1
                                ? " marcador actualizado"
                                : " marcadores actualizados");
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Write(
                        "Marcadores aplicados; no se pudo actualizar " +
                        "el estado visual: " + ex);
                }
            }
            catch
            {
                if (preparedDocument != null)
                {
                    preparedDocument.Dispose();
                }

                if (revisionCommit != null &&
                    !revisionCommit.IsFinished)
                {
                    CompensateFailedRevision(
                        workspace,
                        editSession,
                        sourcePath,
                        revisionCommit);
                }

                throw;
            }
        }

        private static bool IsEditContextCurrent(
            PdfWorkspace workspace,
            PdfEditSession editSession,
            string sourcePath)
        {
            return workspace != null &&
                !workspace.IsDisposed &&
                workspace.IsLoaded &&
                ReferenceEquals(workspace.EditSession, editSession) &&
                string.Equals(
                    workspace.ContentPath,
                    sourcePath,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    editSession.CurrentPath,
                    sourcePath,
                    StringComparison.OrdinalIgnoreCase);
        }

        private static void CompensateFailedRevision(
            PdfWorkspace workspace,
            PdfEditSession editSession,
            string sourcePath,
            PdfEditSession.RevisionCommit revisionCommit)
        {
            if (workspace == null ||
                editSession == null ||
                revisionCommit == null ||
                revisionCommit.IsFinished)
            {
                return;
            }

            if (workspace.EditHistoryFaulted)
            {
                revisionCommit.PreserveForRecovery();
                return;
            }

            try
            {
                revisionCommit.Rollback();
                if (!string.Equals(
                        editSession.CurrentPath,
                        sourcePath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "El historial no volvió a la revisión anterior.");
                }
            }
            catch
            {
                workspace.EditHistoryFaulted = true;
                workspace.FaultedChangesSaved = false;
                workspace.DeleteRecoveryOnClose = false;
                try
                {
                    if (!revisionCommit.IsFinished)
                    {
                        revisionCommit.PreserveForRecovery();
                    }

                    editSession.PreserveRecovery();
                }
                catch
                {
                }

                throw;
            }
        }

        private void ApplyContentRevision(
            PdfWorkspace workspace,
            PdfEditSession editSession,
            string sourcePath,
            string outputPath,
            int preferredPageIndex,
            string description,
            string completedStatus)
        {
            if (!IsEditContextCurrent(
                    workspace,
                    editSession,
                    sourcePath))
            {
                throw new InvalidOperationException(
                    "El documento cambió antes de activar la edición.");
            }

            PdfiumDocument preparedDocument = null;
            PdfEditSession.RevisionCommit revisionCommit = null;
            try
            {
                preparedDocument = PdfDocumentOpenService.Load(outputPath);
                if (preparedDocument.PageCount < 1 ||
                    workspace.Document == null ||
                    preparedDocument.PageCount != workspace.Document.PageCount)
                {
                    throw new InvalidDataException(
                        "La revisión no conserva todas las páginas.");
                }

                revisionCommit = editSession.BeginRevisionCommit(
                    outputPath,
                    description);
                var documentToApply = preparedDocument;
                preparedDocument = null;
                if (!ApplyRevisionToWorkspace(
                        workspace,
                        outputPath,
                        preferredPageIndex,
                        documentToApply,
                        false))
                {
                    CompensateFailedRevision(
                        workspace,
                        editSession,
                        sourcePath,
                        revisionCommit);
                    throw new InvalidOperationException(
                        "La copia se creó, pero no pudo activarse con seguridad.");
                }

                revisionCommit.Complete();
                revisionCommit = null;
                try
                {
                    editSession.CleanupObsoleteRevisions(
                        workspace.ContentPath);
                }
                catch (Exception cleanupError)
                {
                    AppLog.Write(
                        "Edición aplicada; la limpieza se aplazó: " +
                        cleanupError);
                }

                RefreshWorkspaceEditState(workspace);
                if (workspace == activeWorkspace &&
                    !string.IsNullOrWhiteSpace(completedStatus))
                {
                    documentLabel.Text =
                        workspace.DisplayName + " · " + completedStatus;
                }
            }
            catch
            {
                if (preparedDocument != null)
                {
                    preparedDocument.Dispose();
                }

                if (revisionCommit != null &&
                    !revisionCommit.IsFinished)
                {
                    CompensateFailedRevision(
                        workspace,
                        editSession,
                        sourcePath,
                        revisionCommit);
                }

                throw;
            }
        }

        private void UndoActiveDocument()
        {
            NavigateEditHistory(true);
        }

        private void RedoActiveDocument()
        {
            NavigateEditHistory(false);
        }

        private void NavigateEditHistory(bool undo)
        {
            var workspace = GetLoadedActiveWorkspace();
            if (workspace == null ||
                workspace.EditSession == null ||
                workspace.EditHistoryFaulted ||
                (undo
                    ? !workspace.EditSession.CanUndo
                    : !workspace.EditSession.CanRedo) ||
                IsPageStructureOperationInProgress)
            {
                return;
            }

            var targetPath = undo
                ? workspace.EditSession.GetUndoPath()
                : workspace.EditSession.GetRedoPath();
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                return;
            }

            PdfiumDocument preparedDocument = null;
            UseWaitCursor = true;
            try
            {
                preparedDocument = PdfDocumentOpenService.Load(targetPath);
                if (preparedDocument.PageCount < 1)
                {
                    throw new InvalidDataException(
                        "La revision no contiene paginas.");
                }
            }
            catch (Exception ex)
            {
                if (preparedDocument != null)
                {
                    preparedDocument.Dispose();
                    preparedDocument = null;
                }

                ShowEditHistoryError(ex);
                return;
            }
            finally
            {
                UseWaitCursor = false;
            }

            string movedPath;
            try
            {
                movedPath = undo
                    ? workspace.EditSession.Undo()
                    : workspace.EditSession.Redo();
            }
            catch (Exception ex)
            {
                preparedDocument.Dispose();
                ShowEditHistoryError(ex);
                return;
            }

            if (!string.Equals(
                    movedPath,
                    targetPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                preparedDocument.Dispose();
                try
                {
                    if (undo)
                    {
                        workspace.EditSession.Redo();
                    }
                    else
                    {
                        workspace.EditSession.Undo();
                    }
                }
                catch
                {
                    workspace.EditHistoryFaulted = true;
                }

                ShowEditHistoryError(
                    new InvalidDataException(
                        "El historial cambió durante la navegación."));
                return;
            }

            if (!ApplyRevisionToWorkspace(
                    workspace,
                    targetPath,
                    workspace.DisplayedPageIndex,
                    preparedDocument))
            {
                if (!workspace.EditHistoryFaulted)
                {
                    try
                    {
                        if (undo)
                        {
                            workspace.EditSession.Redo();
                        }
                        else
                        {
                            workspace.EditSession.Undo();
                        }
                    }
                    catch (Exception compensationError)
                    {
                        workspace.EditHistoryFaulted = true;
                        workspace.FaultedChangesSaved = false;
                        workspace.DeleteRecoveryOnClose = false;
                        try
                        {
                            workspace.EditSession.PreserveRecovery();
                        }
                        catch
                        {
                        }

                        AppLog.Write(
                            "El historial quedo bloqueado tras un fallo doble: " +
                            compensationError);
                        MessageBox.Show(
                            this,
                            "El PDF visible sigue abierto, pero el historial no se " +
                            "puede actualizar con seguridad.\r\n\r\nGuarda una copia " +
                            "antes de continuar.",
                            "Historial protegido",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                    }
                }
            }
            else
            {
                workspace.EditSession.CleanupObsoleteRevisions(
                    workspace.ContentPath);
            }

            RefreshWorkspaceEditState(workspace);
        }

        private void ShowEditHistoryError(Exception error)
        {
            AppLog.Write("No se pudo actualizar el historial de edicion: " + error);
            ShowPdfProblem(
                "Deshacer o rehacer",
                "No se pudo actualizar el historial de edición.",
                "El PDF original no se ha modificado.",
                error,
                activeWorkspace == null ? null : activeWorkspace.ContentPath);
        }

        private bool ApplyRevisionToWorkspace(
            PdfWorkspace workspace,
            string revisionPath,
            int preferredPageIndex)
        {
            return ApplyRevisionToWorkspace(
                workspace,
                revisionPath,
                preferredPageIndex,
                null);
        }

        private bool ApplyRevisionToWorkspace(
            PdfWorkspace workspace,
            string revisionPath,
            int preferredPageIndex,
            PdfiumDocument preparedDocument)
        {
            return ApplyRevisionToWorkspace(
                workspace,
                revisionPath,
                preferredPageIndex,
                preparedDocument,
                true);
        }

        private bool ApplyRevisionToWorkspace(
            PdfWorkspace workspace,
            string revisionPath,
            int preferredPageIndex,
            PdfiumDocument preparedDocument,
            bool showFailureDialog)
        {
            // Ownership of a prepared document is transferred at method entry.
            // Dispose it even when the target becomes invalid between preload
            // and application.
            PdfiumDocument nextDocument = preparedDocument;
            if (workspace == null ||
                workspace.IsDisposed ||
                string.IsNullOrWhiteSpace(revisionPath) ||
                !File.Exists(revisionPath))
            {
                if (nextDocument != null)
                {
                    nextDocument.Dispose();
                }

                return false;
            }

            CancelRectangleZoom(workspace);
            DisposeWorkspaceTextEditSelection(workspace);
            var previousDocument = workspace.Document;
            var previousContentPath = workspace.ContentPath;
            var viewerDetached = false;
            var rollbackFailed = false;
            UseWaitCursor = true;
            try
            {
                // Load first. The current document remains fully usable if the new
                // revision is incomplete or Windows cannot open it.
                if (nextDocument == null)
                {
                    nextDocument = PdfDocumentOpenService.Load(revisionPath);
                }
                if (nextDocument.PageCount < 1)
                {
                    throw new InvalidDataException(
                        "La revision no contiene paginas.");
                }
                // Measurements are intentionally ephemeral and tied to the
                // exact visible revision. Dispose them before replacing the
                // renderer document so stale coordinates can never be shown
                // over another ContentPath.
                DisposeWorkspaceMeasurement(workspace);
                ClearSearchResults(workspace);
                workspace.Thumbnails.ClearDocument();
                workspace.Viewer.Document = null;
                viewerDetached = true;

                workspace.Document = nextDocument;
                nextDocument = null;
                workspace.ContentPath = Path.GetFullPath(revisionPath);
                workspace.Viewer.Document = workspace.Document;
                viewerDetached = false;
                workspace.Viewer.DefaultDocumentName = workspace.DisplayName;
                workspace.Thumbnails.LoadDocument(workspace.Document);
                workspace.BookmarksTree.Nodes.Clear();
                workspace.BookmarksLoaded = false;
                workspace.BookmarkDocument = null;
                workspace.LoadFailed = false;
                workspace.IsLoaded = true;
                workspace.DisplayedPageIndex = -1;

                if (previousDocument != null)
                {
                    previousDocument.Dispose();
                    previousDocument = null;
                }

                try
                {
                    if (workspace.ShowingBookmarks)
                    {
                        PopulateBookmarks(workspace);
                    }

                    var targetPage = Math.Max(
                        0,
                        Math.Min(
                            workspace.Document.PageCount - 1,
                            preferredPageIndex));
                    ScrollToPage(workspace, targetPage);
                    workspace.Thumbnails.SetActivePage(targetPage, true);

                    if (workspace == activeWorkspace)
                    {
                        BindSearchUi(workspace);
                        UpdatePageIndicator(true);
                        workspace.Viewer.Focus();
                    }
                }
                catch (Exception refreshError)
                {
                    // The revision itself is already active and valid. A secondary
                    // navigation refresh must not roll back the document.
                    AppLog.Write(
                        "Revision aplicada; fallo actualizando la vista: " +
                        refreshError);
                }

                return true;
            }
            catch (Exception ex)
            {
                if (previousDocument != null &&
                    (viewerDetached ||
                     workspace.Document != previousDocument))
                {
                    var failedDocument = workspace.Document;
                    try
                    {
                        workspace.Thumbnails.ClearDocument();
                        workspace.Viewer.Document = null;
                        workspace.Document = previousDocument;
                        workspace.ContentPath = previousContentPath;
                        workspace.Viewer.Document = previousDocument;
                        workspace.Thumbnails.LoadDocument(previousDocument);
                        previousDocument = null;
                    }
                    catch (Exception rollbackError)
                    {
                        rollbackFailed = true;
                        workspace.EditHistoryFaulted = true;
                        workspace.FaultedChangesSaved = false;
                        workspace.DeleteRecoveryOnClose = false;
                        if (workspace.EditSession != null)
                        {
                            try
                            {
                                workspace.EditSession.PreserveRecovery();
                            }
                            catch
                            {
                            }
                        }

                        AppLog.Write(
                            "No se pudo restaurar la revision anterior: " +
                            rollbackError);
                    }

                    if (failedDocument != null &&
                        failedDocument != workspace.Document)
                    {
                        failedDocument.Dispose();
                    }
                }

                AppLog.Write(
                    "No se pudo cargar una revision del PDF: " + ex);
                var failureCause = PdfProblemDiagnostics.Describe(
                    ex,
                    revisionPath);
                var failureMessage = rollbackFailed
                    ? "No se pudo completar el cambio ni restaurar la " +
                      "vista anterior con seguridad.\r\n\r\nEl historial " +
                      "se ha bloqueado y las revisiones se conservarán " +
                      "para recuperación.\r\n\r\n" +
                      failureCause
                    : "No se pudo abrir esa revisión.\r\n\r\n" +
                      failureCause +
                      "\r\n\r\nLa revisión anterior sigue abierta.";
                if (!showFailureDialog)
                {
                    // The content-edit caller owns the user-facing error. Throw
                    // the already contextualized message so that a failed swap
                    // produces one dialog, while keeping the same rollback and
                    // recovery behavior above.
                    throw new InvalidOperationException(failureMessage);
                }

                MessageBox.Show(
                    this,
                    failureMessage,
                    rollbackFailed
                        ? "Historial protegido"
                        : "Deshacer o rehacer",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return false;
            }
            finally
            {
                if (nextDocument != null)
                {
                    nextDocument.Dispose();
                }

                UseWaitCursor = false;
            }
        }

        private void RefreshWorkspaceEditState(PdfWorkspace workspace)
        {
            if (workspace == null || workspace.IsDisposed)
            {
                return;
            }

            var dirty = workspace.EditSession != null &&
                (workspace.EditSession.HasUnsavedChanges ||
                 (workspace.EditHistoryFaulted &&
                  !workspace.FaultedChangesSaved));
            // El prefijo de fallo debe sobrevivir a este refresco: antes se
            // reescribía el texto sin él y la marca nunca llegaba a verse.
            workspace.TabPage.Text =
                (workspace.LoadFailed ? "! " : string.Empty) +
                workspace.DisplayName +
                (dirty ? "  •" : string.Empty);
            workspace.TabPage.ToolTipText =
                workspace.Path +
                (workspace.IsPasswordProtected
                    ? "\r\nProtegido con contraseña · solo lectura"
                    : string.Empty) +
                (dirty
                    ? "\r\nCambios protegidos en recuperación automática"
                    : string.Empty);

            if (workspace == activeWorkspace &&
                comparisonSurface == null &&
                !IsMeasurementActive &&
                !IsTextEditSelectionActive)
            {
                documentEyebrowLabel.Text = dirty
                    ? "CAMBIOS SIN GUARDAR"
                    : (workspace.IsPasswordProtected
                        ? "DOCUMENTO PROTEGIDO"
                        : "DOCUMENTO ACTIVO");
                documentLabel.Text = workspace.DisplayName;
                documentLabel.Tag = workspace.ContentPath;
                Text =
                    workspace.DisplayName +
                    (dirty ? " •" : string.Empty) +
                    " - PDF Ligero";
            }

            RefreshToolAvailability();
        }

        private PdfWorkspace CreateWorkspace(string path)
        {
            var workspace = new PdfWorkspace
            {
                Path = path,
                ContentPath = path,
                DisplayName = Path.GetFileName(path),
                EditSession = PdfEditSession.Create(path)
            };

            workspace.TabPage = new TabPage
            {
                Text = workspace.DisplayName,
                ToolTipText = path,
                BackColor = WindowBackgroundColor,
                Padding = Padding.Empty,
                Tag = workspace,
                AllowDrop = true
            };

            workspace.Viewer = new PdfViewer
            {
                Dock = DockStyle.Fill,
                BackColor = WindowBackgroundColor,
                ShowToolbar = false,
                ShowBookmarks = false,
                ZoomMode = PdfViewerZoomMode.FitWidth,
                AllowDrop = true,
                TabStop = true
            };
            workspace.RectangleZoom = new PdfRectangleZoomController(
                workspace.Viewer.Renderer,
                delegate { return CanUseRectangleZoom(workspace); },
                AccentColor,
                AccentTextColor,
                HeaderBackgroundColor);

            workspace.NavigationPanel = new Panel
            {
                Dock = DockStyle.Left,
                Width = ExpandedNavigationWidth,
                BackColor = NavigationBackgroundColor,
                AllowDrop = true
            };

            workspace.NavigationHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 38,
                BackColor = NavigationBackgroundColor,
                AllowDrop = true
            };

            workspace.PagesButton = CreateNavigationButton(
                "\uE8A5",
                "Miniaturas de páginas");
            workspace.PagesButton.Left = 5;
            workspace.PagesButton.Top = 4;
            workspace.PagesButton.Click += delegate { ShowNavigationMode(workspace, false); };

            workspace.BookmarksButton = CreateNavigationButton(
                "\uE735",
                "Marcadores del PDF");
            workspace.BookmarksButton.Left = 40;
            workspace.BookmarksButton.Top = 4;
            workspace.BookmarksButton.Click += delegate { ShowNavigationMode(workspace, true); };

            workspace.EditBookmarksButton = CreateNavigationButton(
                "\uE70F",
                "Editar marcadores");
            workspace.EditBookmarksButton.Left = 75;
            workspace.EditBookmarksButton.Top = 4;
            workspace.EditBookmarksButton.Visible = false;
            workspace.EditBookmarksButton.Click +=
                delegate { EditActiveBookmarks(); };

            workspace.CollapseNavigationButton = CreateNavigationButton(
                "\u2039",
                "Plegar o desplegar panel de páginas");
            workspace.CollapseNavigationButton.Font =
                CreateArchitecturalFont(13.5f, false);
            workspace.CollapseNavigationButton.ForeColor = AccentTextColor;
            workspace.CollapseNavigationButton.Top = 4;
            workspace.CollapseNavigationButton.Click += delegate
            {
                SetNavigationCollapsed(workspace, !workspace.NavigationCollapsed);
            };

            workspace.NavigationHeader.Controls.Add(workspace.PagesButton);
            workspace.NavigationHeader.Controls.Add(workspace.BookmarksButton);
            workspace.NavigationHeader.Controls.Add(
                workspace.EditBookmarksButton);
            workspace.NavigationHeader.Controls.Add(workspace.CollapseNavigationButton);
            workspace.NavigationHeader.Controls.Add(new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = DividerColor
            });
            workspace.NavigationHeader.Resize += delegate
            {
                workspace.CollapseNavigationButton.Left =
                    workspace.NavigationCollapsed
                        ? Math.Max(
                            1,
                            (workspace.NavigationHeader.ClientSize.Width -
                             workspace.CollapseNavigationButton.Width) / 2)
                        : Math.Max(
                            1,
                            workspace.NavigationHeader.ClientSize.Width -
                            workspace.CollapseNavigationButton.Width -
                            3);
            };

            workspace.Thumbnails = new PdfThumbnailList
            {
                Dock = DockStyle.Fill,
                CacheCapacity = 12,
                AllowDrop = true
            };

            workspace.BookmarksTree = new TreeView
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = NavigationBackgroundColor,
                ForeColor = TitleColor,
                Font = CreateUiFont(9f, FontStyle.Regular),
                ItemHeight = 24,
                Indent = 16,
                LineColor = DividerColor,
                ShowLines = false,
                ShowRootLines = false,
                HideSelection = false,
                FullRowSelect = true,
                Visible = false,
                AllowDrop = true
            };

            workspace.NavigationPanel.Controls.Add(workspace.Thumbnails);
            workspace.NavigationPanel.Controls.Add(workspace.BookmarksTree);
            workspace.NavigationPanel.Controls.Add(workspace.NavigationHeader);
            workspace.NavigationPanel.Controls.Add(new Panel
            {
                Dock = DockStyle.Right,
                Width = 1,
                BackColor = DividerColor
            });

            workspace.TabPage.Controls.Add(workspace.Viewer);
            workspace.TabPage.Controls.Add(workspace.NavigationPanel);

            workspace.ScrollHandler = delegate(object sender, ScrollEventArgs e)
            {
                if (workspace == activeWorkspace)
                {
                    UpdatePageIndicator(false);
                }
            };
            workspace.Viewer.Renderer.Scroll += workspace.ScrollHandler;
            workspace.Viewer.Renderer.AllowDrop = true;

            workspace.ThumbnailSelectionHandler = delegate(
                object sender,
                PdfThumbnailPageSelectedEventArgs e)
            {
                ScrollToPage(workspace, e.PageIndex);
                if (workspace == activeWorkspace)
                {
                    UpdatePageIndicator(true);
                }
            };
            workspace.Thumbnails.PageSelected += workspace.ThumbnailSelectionHandler;
            workspace.PdfInsertHandler = delegate(
                object sender,
                PdfFilesInsertRequestedEventArgs e)
            {
                BeginPdfPageInsert(workspace, e);
            };
            workspace.Thumbnails.PdfFilesInsertRequested += workspace.PdfInsertHandler;
            workspace.PageReorderHandler = delegate(
                object sender,
                PdfThumbnailPagesReorderRequestedEventArgs e)
            {
                BeginPdfPageReorder(workspace, e);
            };
            workspace.Thumbnails.PagesReorderRequested +=
                workspace.PageReorderHandler;
            workspace.PageOperationHandler = delegate(
                object sender,
                PdfThumbnailPageOperationRequestedEventArgs e)
            {
                BeginPdfThumbnailPageOperation(workspace, e);
            };
            workspace.Thumbnails.PageOperationRequested +=
                workspace.PageOperationHandler;
            toolTip.SetToolTip(
                workspace.Thumbnails,
                "Ctrl o Mayús para seleccionar varias páginas.\r\n" +
                "Arrastra la selección para moverla · Supr para eliminar.\r\n" +
                "Suelta PDF externos entre páginas para insertarlos.");

            workspace.BookmarkSelectionHandler = delegate(object sender, TreeNodeMouseClickEventArgs e)
            {
                var destination = e.Node == null
                    ? null
                    : e.Node.Tag as PdfBookmarkDestination;
                if (destination == null)
                {
                    return;
                }

                NavigateToBookmark(workspace, destination);
                if (workspace == activeWorkspace)
                {
                    UpdatePageIndicator(true);
                }
            };
            workspace.BookmarksTree.NodeMouseClick += workspace.BookmarkSelectionHandler;

            workspace.TabPage.DragEnter += PdfDragEnter;
            workspace.TabPage.DragDrop += PdfDragDrop;
            workspace.Viewer.DragEnter += PdfDragEnter;
            workspace.Viewer.DragDrop += PdfDragDrop;
            workspace.Viewer.Renderer.DragEnter += PdfDragEnter;
            workspace.Viewer.Renderer.DragDrop += PdfDragDrop;
            workspace.NavigationPanel.DragEnter += PdfDragEnter;
            workspace.NavigationPanel.DragDrop += PdfDragDrop;
            workspace.NavigationHeader.DragEnter += PdfDragEnter;
            workspace.NavigationHeader.DragDrop += PdfDragDrop;
            workspace.BookmarksTree.DragEnter += PdfDragEnter;
            workspace.BookmarksTree.DragDrop += PdfDragDrop;

            ShowNavigationMode(workspace, false);
            SetNavigationCollapsed(workspace, false);
            return workspace;
        }

        /// <summary>
        /// Marca la pestaña como no cargada tras un fallo o una cancelación, con
        /// las guardas necesarias porque el diálogo de contraseña pudo haber
        /// permitido cerrarla mientras estaba abierto.
        /// </summary>
        private static void MarkWorkspaceLoadFailure(
            PdfWorkspace workspace,
            bool cancelledAtPasswordPrompt)
        {
            if (workspace == null || workspace.IsDisposed)
            {
                return;
            }

            workspace.LoadFailed = true;
            workspace.PasswordPromptCancelled = cancelledAtPasswordPrompt;

            if (workspace.TabPage != null && !workspace.TabPage.IsDisposed)
            {
                workspace.TabPage.Text = "! " + workspace.DisplayName;
            }
        }

        /// <summary>
        /// Muestra un fallo de PDF con la causa ya traducida al español. Toda la
        /// aplicación pasa por aquí para que el mismo problema se explique igual
        /// en cualquier herramienta y para que el texto inglés de PDFium o iText
        /// no llegue nunca al usuario.
        /// </summary>
        private void ShowPdfProblem(
            string title,
            string headline,
            string closingNote,
            Exception error,
            string path)
        {
            var report = PdfProblemDiagnostics.Analyze(error, path);
            var message = string.IsNullOrWhiteSpace(headline)
                ? report.Description
                : headline + "\r\n\r\n" + report.Description;

            if (!string.IsNullOrWhiteSpace(report.Advice))
            {
                message += "\r\n\r\n" + report.Advice;
            }

            if (!string.IsNullOrWhiteSpace(closingNote))
            {
                message += "\r\n\r\n" + closingNote;
            }

            MessageBox.Show(
                this,
                message,
                string.IsNullOrWhiteSpace(title) ? "PDF Ligero" : title,
                MessageBoxButtons.OK,
                report.IsPolicyBlock
                    ? MessageBoxIcon.Warning
                    : MessageBoxIcon.Error);
        }

        /// <summary>
        /// Abre el PDF de una pestaña y, si pide contraseña de apertura, la
        /// solicita con el diálogo propio en lugar del formulario en inglés de
        /// PdfiumViewer.
        ///
        /// Devuelve null cuando el usuario cancela o agota los intentos, que no
        /// es un error y no debe mostrar ningún diálogo rojo. El primer intento
        /// se hace sin contraseña, de modo que un PDF normal se abre exactamente
        /// con la misma entrada/salida que antes.
        /// </summary>
        private PdfiumDocument OpenUserPdfDocument(
            string path,
            string displayName,
            out bool openedWithPassword,
            out bool cancelledByUser)
        {
            openedWithPassword = false;
            cancelledByUser = false;

            string password = null;
            var failedAttempts = 0;

            for (var attempt = 0; attempt < MaximumPasswordAttempts; attempt++)
            {
                try
                {
                    var document = PdfDocumentOpenService.Load(path, password);
                    openedWithPassword = !string.IsNullOrEmpty(password);
                    password = null;
                    return document;
                }
                catch (Exception ex)
                {
                    password = null;
                    if (!PdfDocumentOpenService.IsPasswordRequired(ex))
                    {
                        throw;
                    }

                    var restoreWaitCursor = UseWaitCursor;
                    UseWaitCursor = false;
                    try
                    {
                        using (var prompt = new PdfPasswordPromptForm(
                            displayName,
                            failedAttempts > 0))
                        {
                            if (prompt.ShowDialog(this) != DialogResult.OK)
                            {
                                cancelledByUser = true;
                                return null;
                            }

                            password = prompt.Password;
                        }
                    }
                    finally
                    {
                        UseWaitCursor = restoreWaitCursor;
                    }

                    failedAttempts++;
                }
            }

            AppLog.Write(
                "Se agotaron los intentos de contraseña al abrir: " + path);
            cancelledByUser = true;
            return null;
        }

        private bool EnsureWorkspaceLoaded(PdfWorkspace workspace)
        {
            if (workspace == null || workspace.IsDisposed)
            {
                return false;
            }

            if (workspace.IsLoaded)
            {
                return true;
            }

            if (workspace.LoadFailed)
            {
                return false;
            }

            PdfiumDocument nextDocument = null;
            UseWaitCursor = true;
            documentLabel.Text = "Abriendo " + workspace.DisplayName + "...";

            try
            {
                bool openedWithPassword;
                bool cancelledByUser;
                nextDocument = OpenUserPdfDocument(
                    workspace.ContentPath,
                    workspace.DisplayName,
                    out openedWithPassword,
                    out cancelledByUser);

                if (nextDocument == null)
                {
                    MarkWorkspaceLoadFailure(workspace, cancelledByUser);
                    AppLog.Write(
                        "Apertura cancelada en un PDF protegido: " +
                        workspace.Path);
                    return false;
                }

                // El diálogo de contraseña bombea mensajes: la pestaña puede
                // haberse cerrado mientras estaba abierto.
                if (workspace.IsDisposed)
                {
                    nextDocument.Dispose();
                    nextDocument = null;
                    return false;
                }

                workspace.Document = nextDocument;
                nextDocument = null;
                workspace.IsPasswordProtected = openedWithPassword;

                workspace.Viewer.Document = workspace.Document;
                workspace.Viewer.DefaultDocumentName = workspace.DisplayName;
                workspace.Viewer.ZoomMode = PdfViewerZoomMode.FitWidth;
                workspace.Thumbnails.LoadDocument(workspace.Document);
                workspace.IsLoaded = true;

                workspace.DisplayedPageIndex = -1;

                AppLog.Write(
                    "PDF abierto en pestaña: " + workspace.Path +
                    ". Contenido=" + workspace.ContentPath +
                    ". Paginas=" + workspace.Document.PageCount +
                    ". Protegido=" + (openedWithPassword ? "si" : "no"));
                return true;
            }
            catch (Exception ex)
            {
                MarkWorkspaceLoadFailure(workspace, false);
                AppLog.Write(
                    "No se pudo abrir el PDF en una pestaña: " +
                    workspace.Path + ". " + ex);

                ShowPdfProblem(
                    "PDF Ligero",
                    "No se pudo abrir " + workspace.DisplayName + ".",
                    null,
                    ex,
                    workspace.ContentPath);
                return false;
            }
            finally
            {
                if (nextDocument != null)
                {
                    nextDocument.Dispose();
                }

                UseWaitCursor = false;
            }
        }

        private void PopulateBookmarks(PdfWorkspace workspace)
        {
            if (workspace == null ||
                !workspace.IsLoaded ||
                workspace.BookmarksLoaded)
            {
                return;
            }

            workspace.BookmarksTree.BeginUpdate();
            try
            {
                workspace.BookmarksTree.Nodes.Clear();
                try
                {
                    if (workspace.BookmarkDocument == null)
                    {
                        workspace.BookmarkDocument =
                            PdfBookmarkService.Load(
                                workspace.ContentPath);
                    }

                    AddBookmarkNodes(
                        workspace.BookmarksTree.Nodes,
                        workspace.BookmarkDocument.Bookmarks);
                }
                catch (Exception ex)
                {
                    workspace.BookmarkDocument = null;
                    AppLog.Write(
                        "No se pudieron cargar los destinos completos de " +
                        "los marcadores; se usa la navegación básica: " +
                        ex);
                    AddBookmarkNodes(
                        workspace.BookmarksTree.Nodes,
                        workspace.Document.Bookmarks);
                }

                if (workspace.BookmarksTree.Nodes.Count == 0)
                {
                    workspace.BookmarksTree.Nodes.Add(
                        new TreeNode("Este PDF no tiene marcadores")
                        {
                            ForeColor = BodyColor
                        });
                }
            }
            finally
            {
                workspace.BookmarksTree.EndUpdate();
                workspace.BookmarksLoaded = true;
            }
        }

        private static void AddBookmarkNodes(
            TreeNodeCollection target,
            PdfBookmarkCollection bookmarks)
        {
            if (bookmarks == null)
            {
                return;
            }

            foreach (PdfBookmark bookmark in bookmarks)
            {
                var node = new TreeNode(
                    string.IsNullOrWhiteSpace(bookmark.Title)
                        ? "Marcador"
                        : bookmark.Title)
                {
                    Tag = bookmark.PageIndex < 0
                        ? null
                        : new PdfBookmarkDestination(
                            bookmark.PageIndex + 1,
                            null)
                };

                target.Add(node);
                AddBookmarkNodes(node.Nodes, bookmark.Children);
            }
        }

        private static void AddBookmarkNodes(
            TreeNodeCollection target,
            IList<PdfBookmarkNode> bookmarks)
        {
            if (bookmarks == null)
            {
                return;
            }

            foreach (var bookmark in bookmarks)
            {
                var node = new TreeNode(
                    string.IsNullOrWhiteSpace(bookmark.Title)
                        ? "Marcador"
                        : bookmark.Title)
                {
                    Tag = bookmark.Destination
                };

                target.Add(node);
                AddBookmarkNodes(node.Nodes, bookmark.Children);
                if (bookmark.IsOpen && node.Nodes.Count > 0)
                {
                    node.Expand();
                }
            }
        }

        private void DocumentTabs_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (comparisonSurface != null)
            {
                ClosePlanComparison(false);
            }

            if (openingBatch || closingAll)
            {
                return;
            }

            RefreshEmptyState();
            ActivateSelectedWorkspace();
        }

        private void DocumentTabs_TabCloseRequested(
            object sender,
            TabCloseRequestedEventArgs e)
        {
            if (comparisonSurface != null)
            {
                ClosePlanComparison(false);
            }

            var workspace = e.TabPage == null
                ? null
                : e.TabPage.Tag as PdfWorkspace;
            if (workspace == null)
            {
                return;
            }

            DeactivateWorkspaceMeasurement(workspace);
            DeactivateWorkspaceTextEditSelection(workspace);

            if (pageInsertInProgress &&
                pendingPageInsertRequest != null &&
                pendingPageInsertRequest.SourceWorkspace == workspace)
            {
                e.Cancel = true;
                MessageBox.Show(
                    this,
                    "Espera un momento: se está terminando la edición de esta pestaña.",
                    "PDF Ligero",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (pageOrganizationInProgress &&
                pendingPageOrganizationRequest != null &&
                pendingPageOrganizationRequest.SourceWorkspace == workspace)
            {
                e.Cancel = true;
                MessageBox.Show(
                    this,
                    "Espera un momento: se está terminando la organización " +
                    "de páginas de esta pestaña.",
                    "PDF Ligero",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (ocrInProgress &&
                pendingOcrRequest != null &&
                pendingOcrRequest.SourceWorkspace == workspace)
            {
                e.Cancel = true;
                MessageBox.Show(
                    this,
                    "Esta pestaña está ejecutando OCR. Cancélalo primero " +
                    "desde la ventana de progreso.",
                    "OCR y enderezado",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (!ConfirmCloseWorkspace(workspace))
            {
                e.Cancel = true;
            }
        }

        private bool ConfirmCloseWorkspace(PdfWorkspace workspace)
        {
            if (workspace == null || workspace.IsDisposed)
            {
                return true;
            }

            if (workspace.EditSession == null)
            {
                workspace.DeleteRecoveryOnClose = true;
                return true;
            }

            var contentUsesRecoveryCopy = !string.Equals(
                workspace.ContentPath,
                workspace.Path,
                StringComparison.OrdinalIgnoreCase);
            var hasUnbackedChanges =
                (workspace.EditSession.HasUnsavedChanges ||
                 workspace.EditHistoryFaulted) &&
                !workspace.FaultedChangesSaved;
            var savedCopyChanged =
                contentUsesRecoveryCopy &&
                !hasUnbackedChanges &&
                !IsLastSavedCopyUnchanged(workspace, true);

            if (!hasUnbackedChanges && !savedCopyChanged)
            {
                workspace.DeleteRecoveryOnClose = true;
                return true;
            }

            documentTabs.SelectedTab = workspace.TabPage;
            ActivateSelectedWorkspace();
            var answer = MessageBox.Show(
                this,
                (savedCopyChanged
                    ? "La copia guardada de \"" + workspace.DisplayName +
                      "\" fue movida, eliminada o modificada fuera de PDF " +
                      "Ligero.\r\n\r\nEl documento visible sigue protegido " +
                      "temporalmente, pero debes guardarlo de nuevo para no " +
                      "perderlo.\r\n\r\n"
                    : "\"" + workspace.DisplayName +
                      "\" tiene cambios sin guardar.\r\n\r\n") +
                "Sí: guardar una copia y cerrar.\r\n" +
                "No: descartar los cambios.\r\n" +
                "Cancelar: volver al documento.",
                "Cerrar PDF",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button1);

            if (answer == DialogResult.Cancel)
            {
                return false;
            }

            if (answer == DialogResult.Yes &&
                !SaveWorkspaceCopy(workspace))
            {
                return false;
            }

            if (answer == DialogResult.Yes &&
                contentUsesRecoveryCopy &&
                !IsLastSavedCopyUnchanged(workspace, true))
            {
                MessageBox.Show(
                    this,
                    "La copia se creó, pero no se pudo mantener bloqueada " +
                    "hasta completar el cierre.\r\n\r\nLa recuperación se " +
                    "conservará y la pestaña seguirá abierta.",
                    "No se pudo comprobar el cierre",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return false;
            }

            workspace.DeleteRecoveryOnClose = true;
            return true;
        }

        private void ActivateSelectedWorkspace()
        {
            if (activatingWorkspace)
            {
                return;
            }

            activatingWorkspace = true;
            try
            {
                var nextWorkspace = documentTabs.SelectedTab == null
                    ? null
                    : documentTabs.SelectedTab.Tag as PdfWorkspace;

                if (activeWorkspace != nextWorkspace)
                {
                    CancelRectangleZoom(activeWorkspace);
                    DeactivateWorkspaceMeasurement(activeWorkspace);
                    DeactivateWorkspaceTextEditSelection(activeWorkspace);
                }

                activeWorkspace = nextWorkspace;
                if (activeWorkspace == null)
                {
                    BindNoDocumentUi();
                    return;
                }

                EnsureWorkspaceLoaded(activeWorkspace);

                RefreshWorkspaceEditState(activeWorkspace);

                BindSearchUi(activeWorkspace);
                UpdatePageIndicator(true);
                RefreshToolAvailability();

                if (activeWorkspace.IsLoaded)
                {
                    pageSyncTimer.Start();
                    activeWorkspace.Viewer.Focus();
                }
                else
                {
                    pageSyncTimer.Stop();
                }
            }
            finally
            {
                activatingWorkspace = false;

                // El refresco de dentro del try se calcula con la activación aún
                // en curso, y varias herramientas (medición) exigen que no lo
                // esté. Sin este segundo refresco quedaban en gris hasta que
                // otra cosa recalculaba el estado.
                RefreshToolAvailability();
            }
        }

        private void DocumentTabs_TabClosed(
            object sender,
            TabCloseRequestedEventArgs e)
        {
            var workspace = e.TabPage.Tag as PdfWorkspace;
            if (workspace == null)
            {
                return;
            }

            ReleaseWorkspaceResources(workspace);
            RefreshEmptyState();
            if (documentTabs.TabPages.Count == 0)
            {
                BindNoDocumentUi();
            }
            else if (activeWorkspace == null)
            {
                ActivateSelectedWorkspace();
            }

            RefreshToolAvailability();
        }

        private void CloseWorkspace(PdfWorkspace workspace)
        {
            if (workspace == null || workspace.IsDisposed)
            {
                return;
            }

            if (comparisonSurface != null)
            {
                ClosePlanComparison(false);
            }

            if (documentTabs.TabPages.Contains(workspace.TabPage))
            {
                documentTabs.TabPages.Remove(workspace.TabPage);
            }

            ReleaseWorkspaceResources(workspace);

            if (closingAll)
            {
                return;
            }

            if (documentTabs.TabPages.Count == 0)
            {
                BindNoDocumentUi();
            }

            RefreshEmptyState();
            RefreshToolAvailability();
        }

        private void ReleaseWorkspaceResources(PdfWorkspace workspace)
        {
            try
            {
                PrepareWorkspaceRelease(workspace);
            }
            catch (Exception ex)
            {
                // A close must still release the file and the detached tab if a
                // peripheral controller ever fails during its own cleanup.
                workspace.IsDisposed = true;
                workspaceByPath.Remove(workspace.Path);
                workspaces.Remove(workspace);
                if (workspace == activeWorkspace)
                {
                    activeWorkspace = null;
                }

                AppLog.Write(
                    "No se pudo preparar una pestaña individual al cerrar: " +
                    ex);
            }

            try
            {
                workspace.TabPage.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Write(
                    "No se pudo liberar por completo una pestaña al cerrar: " +
                    ex);
            }
            finally
            {
                CompleteWorkspaceRelease(workspace);
            }
        }

        private void PrepareWorkspaceRelease(PdfWorkspace workspace)
        {
            DisposeWorkspaceTextEditSelection(workspace);
            DisposeWorkspaceMeasurement(workspace);
            workspace.IsDisposed = true;
            workspaceByPath.Remove(workspace.Path);
            workspaces.Remove(workspace);

            if (workspace == activeWorkspace)
            {
                activeWorkspace = null;
            }

            if (workspace.RectangleZoom != null)
            {
                workspace.RectangleZoom.Dispose();
                workspace.RectangleZoom = null;
            }

            workspace.Viewer.Renderer.Scroll -= workspace.ScrollHandler;
            workspace.Thumbnails.PageSelected -= workspace.ThumbnailSelectionHandler;
            workspace.Thumbnails.PdfFilesInsertRequested -= workspace.PdfInsertHandler;
            workspace.Thumbnails.PagesReorderRequested -=
                workspace.PageReorderHandler;
            workspace.Thumbnails.PageOperationRequested -=
                workspace.PageOperationHandler;
            workspace.BookmarksTree.NodeMouseClick -= workspace.BookmarkSelectionHandler;
            workspace.TabPage.DragEnter -= PdfDragEnter;
            workspace.TabPage.DragDrop -= PdfDragDrop;
            workspace.Viewer.DragEnter -= PdfDragEnter;
            workspace.Viewer.DragDrop -= PdfDragDrop;
            workspace.Viewer.Renderer.DragEnter -= PdfDragEnter;
            workspace.Viewer.Renderer.DragDrop -= PdfDragDrop;
            workspace.NavigationPanel.DragEnter -= PdfDragEnter;
            workspace.NavigationPanel.DragDrop -= PdfDragDrop;
            workspace.NavigationHeader.DragEnter -= PdfDragEnter;
            workspace.NavigationHeader.DragDrop -= PdfDragDrop;
            workspace.BookmarksTree.DragEnter -= PdfDragEnter;
            workspace.BookmarksTree.DragDrop -= PdfDragDrop;

            toolTip.SetToolTip(workspace.PagesButton, null);
            toolTip.SetToolTip(workspace.BookmarksButton, null);
            toolTip.SetToolTip(workspace.CollapseNavigationButton, null);
            toolTip.SetToolTip(workspace.Thumbnails, null);
            workspace.Thumbnails.ClearDocument();
        }

        private static void CompleteWorkspaceRelease(PdfWorkspace workspace)
        {
            var document = workspace.Document;
            workspace.Document = null;
            if (document != null)
            {
                try
                {
                    document.Dispose();
                }
                catch (Exception ex)
                {
                    AppLog.Write(
                        "No se pudo liberar un documento al cerrar: " + ex);
                }
            }

            var editSession = workspace.EditSession;
            workspace.EditSession = null;
            if (editSession != null)
            {
                try
                {
                    if (workspace.DeleteRecoveryOnClose)
                    {
                        editSession.DeleteRecovery();
                    }
                    else
                    {
                        editSession.PreserveRecovery();
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Write(
                        "No se pudo finalizar Recovery al cerrar: " + ex);
                }
            }

            ReleaseSavedCopyVerificationLease(workspace);
        }

        private void BindNoDocumentUi()
        {
            activeWorkspace = null;
            pageSyncTimer.Stop();
            documentEyebrowLabel.Text = "PDF LIGERO / VISOR";
            documentLabel.Text = "Ningún PDF abierto";
            documentLabel.Tag = null;
            Text = "PDF Ligero";

            suppressSearchTextChanged = true;
            try
            {
                searchTextBox.Text = string.Empty;
            }
            finally
            {
                suppressSearchTextChanged = false;
            }

            searchPanel.Visible = false;
            SetSearchToolActive(false);
            searchStatusLabel.Text = "Escribe y pulsa Enter";
            UpdatePageIndicator(true);
            RefreshToolAvailability();
        }

        private void BindSearchUi(PdfWorkspace workspace)
        {
            suppressSearchTextChanged = true;
            try
            {
                searchTextBox.Text = workspace.SearchInput;
            }
            finally
            {
                suppressSearchTextChanged = false;
            }

            UpdateSearchStatus(workspace);
        }

        private void SearchTextBox_TextChanged(object sender, EventArgs e)
        {
            if (suppressSearchTextChanged)
            {
                return;
            }

            var workspace = activeWorkspace;
            if (workspace == null)
            {
                return;
            }

            workspace.SearchInput = searchTextBox.Text;
            ClearSearchResults(workspace);
            workspace.LastSearchQuery = string.Empty;

            searchStatusLabel.Text = string.IsNullOrWhiteSpace(searchTextBox.Text)
                ? "Escribe y pulsa Enter"
                : "Pulsa Enter para buscar";
        }

        private void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }

            e.Handled = true;
            e.SuppressKeyPress = true;

            var workspace = GetLoadedActiveWorkspace();
            var query = searchTextBox.Text;
            if (workspace == null || string.IsNullOrWhiteSpace(query))
            {
                searchStatusLabel.Text = "Escribe y pulsa Enter";
                return;
            }

            if (workspace.SearchMatches == null ||
                !string.Equals(
                    query,
                    workspace.LastSearchQuery,
                    StringComparison.Ordinal))
            {
                PerformSearch(workspace);
            }
            else
            {
                NavigateSearch(!e.Shift);
            }
        }

        private void ShowSearchPanel()
        {
            if (comparisonSurface != null ||
                IsMeasurementActive ||
                IsTextEditSelectionActive ||
                contentEditInProgress ||
                GetLoadedActiveWorkspace() == null)
            {
                return;
            }

            CancelRectangleZoom(activeWorkspace);
            searchPanel.Visible = true;
            SetSearchToolActive(true);
            PerformLayout();
            searchTextBox.Focus();
            searchTextBox.SelectAll();
        }

        private void CloseSearchPanel()
        {
            var workspace = activeWorkspace;

            suppressSearchTextChanged = true;
            try
            {
                searchTextBox.Text = string.Empty;
            }
            finally
            {
                suppressSearchTextChanged = false;
            }

            if (workspace != null)
            {
                workspace.SearchInput = string.Empty;
                workspace.LastSearchQuery = string.Empty;
                ClearSearchResults(workspace);
            }

            searchStatusLabel.Text = "Escribe y pulsa Enter";
            searchPanel.Visible = false;
            SetSearchToolActive(false);
            if (workspace != null)
            {
                workspace.Viewer.Focus();
            }
        }

        private void SetSearchToolActive(bool active)
        {
            searchToolButton.BackColor = active
                ? AccentTintColor
                : HeaderBackgroundColor;
            searchToolButton.ForeColor = active
                ? AccentTextColor
                : (searchToolButton.Enabled ? TitleColor : MutedColor);
        }

        private void PerformSearch(PdfWorkspace workspace)
        {
            ClearSearchResults(workspace);

            var query = searchTextBox.Text;
            if (!workspace.IsLoaded || string.IsNullOrWhiteSpace(query))
            {
                searchStatusLabel.Text = "Escribe y pulsa Enter";
                return;
            }

            workspace.SearchInput = query;
            workspace.LastSearchQuery = query;
            searchStatusLabel.Text = "Buscando…";
            UseWaitCursor = true;
            searchTextBox.Enabled = false;

            try
            {
                workspace.SearchMatches = workspace.Document.Search(query, false, false);
                workspace.SearchMatchBounds =
                    new Dictionary<int, IList<PdfRectangle>>();

                if (workspace.SearchMatches.Items.Count == 0)
                {
                    searchStatusLabel.Text = "Sin coincidencias";
                    return;
                }

                workspace.CurrentSearchIndex =
                    FindFirstSearchMatchFromCurrentPage(workspace);
                ApplySearchHighlights(workspace);
                ScrollCurrentSearchMatchIntoView(workspace);
                UpdateSearchStatus(workspace);
            }
            catch (Exception ex)
            {
                AppLog.Write("No se pudo buscar texto en el PDF: " + ex);
                ClearSearchResults(workspace);
                searchStatusLabel.Text = "No se pudo buscar";
            }
            finally
            {
                searchTextBox.Enabled = true;
                UseWaitCursor = false;
                if (workspace == activeWorkspace)
                {
                    searchTextBox.Focus();
                }
            }
        }

        private void NavigateSearch(bool forward)
        {
            var workspace = activeWorkspace;
            if (workspace == null ||
                workspace.SearchMatches == null ||
                workspace.SearchMatches.Items.Count == 0)
            {
                return;
            }

            if (forward)
            {
                workspace.CurrentSearchIndex =
                    (workspace.CurrentSearchIndex + 1) %
                    workspace.SearchMatches.Items.Count;
            }
            else
            {
                workspace.CurrentSearchIndex--;
                if (workspace.CurrentSearchIndex < 0)
                {
                    workspace.CurrentSearchIndex =
                        workspace.SearchMatches.Items.Count - 1;
                }
            }

            ApplySearchHighlights(workspace);
            ScrollCurrentSearchMatchIntoView(workspace);
            UpdateSearchStatus(workspace);
        }

        private static int FindFirstSearchMatchFromCurrentPage(
            PdfWorkspace workspace)
        {
            if (workspace.SearchMatches == null ||
                workspace.SearchMatches.Items.Count == 0)
            {
                return -1;
            }

            var currentPage = Math.Max(0, workspace.Viewer.Renderer.Page);
            var pageCount = Math.Max(1, workspace.Document.PageCount);
            var bestIndex = 0;
            var bestOffset = pageCount;

            for (var matchIndex = 0;
                matchIndex < workspace.SearchMatches.Items.Count;
                matchIndex++)
            {
                var matchPage = workspace.SearchMatches.Items[matchIndex].Page;
                var offset = (matchPage - currentPage + pageCount) % pageCount;
                if (offset < bestOffset)
                {
                    bestIndex = matchIndex;
                    bestOffset = offset;
                    if (offset == 0)
                    {
                        break;
                    }
                }
            }

            return bestIndex;
        }

        private static void ApplySearchHighlights(PdfWorkspace workspace)
        {
            workspace.Viewer.Renderer.Markers.Clear();
            if (workspace.SearchMatches == null ||
                workspace.SearchMatchBounds == null ||
                workspace.CurrentSearchIndex < 0 ||
                workspace.CurrentSearchIndex >= workspace.SearchMatches.Items.Count)
            {
                return;
            }

            AddSearchHighlight(workspace, workspace.CurrentSearchIndex, true);

            var currentPage =
                workspace.SearchMatches.Items[workspace.CurrentSearchIndex].Page;
            var highlightedOnPage = 1;
            for (var matchIndex = 0;
                matchIndex < workspace.SearchMatches.Items.Count &&
                highlightedOnPage < MaximumHighlightsOnCurrentPage;
                matchIndex++)
            {
                if (matchIndex == workspace.CurrentSearchIndex ||
                    workspace.SearchMatches.Items[matchIndex].Page != currentPage)
                {
                    continue;
                }

                AddSearchHighlight(workspace, matchIndex, false);
                highlightedOnPage++;
            }
        }

        private static void AddSearchHighlight(
            PdfWorkspace workspace,
            int matchIndex,
            bool isCurrent)
        {
            var fillColor = isCurrent
                ? Color.FromArgb(112, AccentColor)
                : Color.FromArgb(64, AccentColor);
            var borderColor = isCurrent ? AccentTextColor : Color.Transparent;
            var borderWidth = isCurrent ? 1.5f : 0f;

            foreach (var pdfBounds in GetSearchMatchBounds(workspace, matchIndex))
            {
                var bounds = new RectangleF(
                    pdfBounds.Bounds.Left - 1,
                    pdfBounds.Bounds.Top + 1,
                    pdfBounds.Bounds.Width + 2,
                    Math.Max(1f, pdfBounds.Bounds.Height - 2));

                workspace.Viewer.Renderer.Markers.Add(new PdfMarker(
                    pdfBounds.Page,
                    bounds,
                    fillColor,
                    borderColor,
                    borderWidth));
            }
        }

        private static IList<PdfRectangle> GetSearchMatchBounds(
            PdfWorkspace workspace,
            int matchIndex)
        {
            IList<PdfRectangle> bounds;
            if (!workspace.SearchMatchBounds.TryGetValue(matchIndex, out bounds))
            {
                bounds = workspace.Document.GetTextBounds(
                    workspace.SearchMatches.Items[matchIndex].TextSpan) ??
                    new List<PdfRectangle>();
                workspace.SearchMatchBounds[matchIndex] = bounds;
            }

            return bounds;
        }

        private static void ScrollCurrentSearchMatchIntoView(
            PdfWorkspace workspace)
        {
            if (workspace.SearchMatchBounds == null ||
                workspace.CurrentSearchIndex < 0 ||
                workspace.SearchMatches == null ||
                workspace.CurrentSearchIndex >= workspace.SearchMatches.Items.Count)
            {
                return;
            }

            var bounds = GetSearchMatchBounds(
                workspace,
                workspace.CurrentSearchIndex);
            if (bounds.Count > 0)
            {
                workspace.Viewer.Renderer.ScrollIntoView(bounds[0]);
            }
        }

        private void UpdateSearchStatus(PdfWorkspace workspace)
        {
            if (workspace != activeWorkspace)
            {
                return;
            }

            var count = workspace.SearchMatches == null
                ? 0
                : workspace.SearchMatches.Items.Count;
            var hasMatches = count > 0 && workspace.CurrentSearchIndex >= 0;

            searchPreviousButton.Enabled = hasMatches;
            searchNextButton.Enabled = hasMatches;

            if (hasMatches)
            {
                searchStatusLabel.Text =
                    (workspace.CurrentSearchIndex + 1) + " de " + count;
            }
            else if (string.IsNullOrWhiteSpace(workspace.SearchInput))
            {
                searchStatusLabel.Text = "Escribe y pulsa Enter";
            }
            else if (!string.Equals(
                workspace.SearchInput,
                workspace.LastSearchQuery,
                StringComparison.Ordinal))
            {
                searchStatusLabel.Text = "Pulsa Enter para buscar";
            }
            else
            {
                searchStatusLabel.Text = "Sin coincidencias";
            }
        }

        private void ClearSearchResults(PdfWorkspace workspace)
        {
            workspace.SearchMatches = null;
            workspace.SearchMatchBounds = null;
            workspace.CurrentSearchIndex = -1;

            if (workspace.Viewer != null && !workspace.Viewer.IsDisposed)
            {
                workspace.Viewer.Renderer.Markers.Clear();
            }

            if (workspace == activeWorkspace)
            {
                searchPreviousButton.Enabled = false;
                searchNextButton.Enabled = false;
            }
        }

        private void NavigatePage(int offset)
        {
            if (comparisonSurface != null)
            {
                return;
            }

            var workspace = GetLoadedActiveWorkspace();
            if (workspace == null || workspace.Document.PageCount == 0)
            {
                return;
            }

            var currentPage = Math.Max(
                0,
                Math.Min(
                    workspace.Document.PageCount - 1,
                    workspace.Viewer.Renderer.Page));
            var targetPage = Math.Max(
                0,
                Math.Min(
                    workspace.Document.PageCount - 1,
                    currentPage + offset));

            if (targetPage != currentPage)
            {
                ScrollToPage(workspace, targetPage);
            }

            UpdatePageIndicator(true);
            workspace.Viewer.Focus();
        }

        private void CurrentPageTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                CommitPageNumber();

                if (activeWorkspace != null)
                {
                    activeWorkspace.Viewer.Focus();
                }
                return;
            }

            if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                UpdatePageIndicator(true);

                if (activeWorkspace != null)
                {
                    activeWorkspace.Viewer.Focus();
                }
            }
        }

        private void CommitPageNumber()
        {
            if (comparisonSurface != null)
            {
                return;
            }

            var workspace = GetLoadedActiveWorkspace();
            if (workspace == null || workspace.Document.PageCount == 0)
            {
                UpdatePageIndicator(true);
                return;
            }

            int requestedPage;
            if (!int.TryParse(currentPageTextBox.Text.Trim(), out requestedPage))
            {
                UpdatePageIndicator(true);
                return;
            }

            requestedPage = Math.Max(
                1,
                Math.Min(workspace.Document.PageCount, requestedPage));
            ScrollToPage(workspace, requestedPage - 1);
            UpdatePageIndicator(true);
        }

        private static void ScrollToPage(PdfWorkspace workspace, int page)
        {
            if (workspace == null ||
                !workspace.IsLoaded ||
                workspace.Document.PageCount == 0)
            {
                return;
            }

            page = Math.Max(0, Math.Min(workspace.Document.PageCount - 1, page));
            CancelRectangleZoom(workspace);
            workspace.Viewer.Renderer.Page = page;
        }

        private static void NavigateToBookmark(
            PdfWorkspace workspace,
            PdfBookmarkDestination destination)
        {
            if (workspace == null ||
                destination == null ||
                !workspace.IsLoaded ||
                workspace.Document == null ||
                workspace.Document.PageCount < 1)
            {
                return;
            }

            var pageIndex = Math.Max(
                0,
                Math.Min(
                    workspace.Document.PageCount - 1,
                    destination.PageNumber - 1));
            ScrollToPage(workspace, pageIndex);

            try
            {
                workspace.Viewer.Renderer.BeginInvoke(
                    new Action(delegate
                    {
                        if (workspace.IsDisposed ||
                            !workspace.IsLoaded ||
                            workspace.Document == null)
                        {
                            return;
                        }

                        ApplyBookmarkViewport(
                            workspace,
                            destination,
                            pageIndex);
                        workspace.Viewer.Focus();
                    }));
            }
            catch (InvalidOperationException)
            {
                workspace.Viewer.Focus();
            }
        }

        private static void ApplyBookmarkViewport(
            PdfWorkspace workspace,
            PdfBookmarkDestination destination,
            int pageIndex)
        {
            var renderer = workspace.Viewer.Renderer;
            switch (destination.Mode)
            {
                case PdfBookmarkDestinationMode.Fit:
                case PdfBookmarkDestinationMode.FitBoundingBox:
                    renderer.ZoomMode = PdfViewerZoomMode.FitBest;
                    renderer.PerformLayout();
                    return;

                case PdfBookmarkDestinationMode.FitHorizontal:
                case PdfBookmarkDestinationMode
                    .FitBoundingBoxHorizontal:
                    renderer.ZoomMode = PdfViewerZoomMode.FitWidth;
                    renderer.PerformLayout();
                    break;

                case PdfBookmarkDestinationMode.FitVertical:
                case PdfBookmarkDestinationMode
                    .FitBoundingBoxVertical:
                    renderer.ZoomMode = PdfViewerZoomMode.FitHeight;
                    renderer.PerformLayout();
                    break;

                case PdfBookmarkDestinationMode.FitRectangle:
                    if (TryFitBookmarkRectangle(
                            workspace,
                            destination,
                            pageIndex))
                    {
                        return;
                    }
                    break;

                default:
                    if (destination.Zoom.HasValue)
                    {
                        renderer.Zoom = Math.Max(
                            renderer.ZoomMin,
                            Math.Min(
                                renderer.ZoomMax,
                                destination.Zoom.Value));
                        renderer.PerformLayout();
                    }
                    break;
            }

            if (workspace.BookmarkDocument == null)
            {
                return;
            }

            var pdfPoint = PdfBookmarkService.GetPdfPoint(
                workspace.BookmarkDocument,
                destination);
            if (!pdfPoint.HasX && !pdfPoint.HasY)
            {
                return;
            }

            var clientPoint = renderer.PointFromPdf(
                new PdfPoint(
                    pageIndex,
                    new PointF(
                        (float)pdfPoint.X,
                        (float)pdfPoint.Y)));
            var displayLocation = renderer.DisplayRectangle.Location;
            renderer.SetDisplayRectLocation(
                new Point(
                    displayLocation.X +
                        (pdfPoint.HasX ? -clientPoint.X : 0),
                    displayLocation.Y +
                        (pdfPoint.HasY ? -clientPoint.Y : 0)),
                false);
        }

        private static bool TryFitBookmarkRectangle(
            PdfWorkspace workspace,
            PdfBookmarkDestination destination,
            int pageIndex)
        {
            if (workspace.BookmarkDocument == null ||
                !destination.TopPositionPercent.HasValue ||
                !destination.LeftPositionPercent.HasValue ||
                !destination.BottomPositionPercent.HasValue ||
                !destination.RightPositionPercent.HasValue)
            {
                return false;
            }

            var geometry = workspace.BookmarkDocument.GetPageGeometry(
                destination.PageNumber);
            var left = geometry.CropLeft +
                geometry.CropWidth *
                destination.LeftPositionPercent.Value / 100D;
            var right = geometry.CropLeft +
                geometry.CropWidth *
                destination.RightPositionPercent.Value / 100D;
            var top = geometry.CropTop -
                geometry.CropHeight *
                destination.TopPositionPercent.Value / 100D;
            var bottom = geometry.CropTop -
                geometry.CropHeight *
                destination.BottomPositionPercent.Value / 100D;
            var pdfBounds = new PdfRectangle(
                pageIndex,
                RectangleF.FromLTRB(
                    (float)Math.Min(left, right),
                    (float)Math.Min(bottom, top),
                    (float)Math.Max(left, right),
                    (float)Math.Max(bottom, top)));
            var renderer = workspace.Viewer.Renderer;
            var clientBounds = renderer.BoundsFromPdf(pdfBounds);
            if (clientBounds.Width < 2 ||
                clientBounds.Height < 2 ||
                renderer.ClientSize.Width < 2 ||
                renderer.ClientSize.Height < 2)
            {
                return false;
            }

            var zoom = renderer.Zoom *
                Math.Min(
                    (double)renderer.ClientSize.Width /
                        clientBounds.Width,
                    (double)renderer.ClientSize.Height /
                        clientBounds.Height);
            renderer.Zoom = Math.Max(
                renderer.ZoomMin,
                Math.Min(renderer.ZoomMax, zoom));
            renderer.PerformLayout();

            for (var pass = 0; pass < 2; pass++)
            {
                clientBounds = renderer.BoundsFromPdf(pdfBounds);
                var correction = new Point(
                    renderer.ClientSize.Width / 2 -
                        (clientBounds.Left +
                         clientBounds.Width / 2),
                    renderer.ClientSize.Height / 2 -
                        (clientBounds.Top +
                         clientBounds.Height / 2));
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

            return true;
        }

        private static PdfBookmarkDestination
            CaptureCurrentBookmarkDestination(
                PdfWorkspace workspace,
                PdfBookmarkDocument bookmarkDocument)
        {
            if (workspace == null ||
                bookmarkDocument == null ||
                !workspace.IsLoaded ||
                workspace.IsDisposed ||
                workspace.Document == null ||
                workspace.Document.PageCount < 1)
            {
                return null;
            }

            var renderer = workspace.Viewer.Renderer;
            var pageIndex = Math.Max(
                0,
                Math.Min(
                    workspace.Document.PageCount - 1,
                    renderer.Page));
            var pageSize = workspace.Document.PageSizes[pageIndex];
            var pageBounds = renderer.BoundsFromPdf(
                new PdfRectangle(
                    pageIndex,
                    new RectangleF(
                        0F,
                        0F,
                        pageSize.Width,
                        pageSize.Height)));
            var visibleBounds = Rectangle.Intersect(
                pageBounds,
                renderer.ClientRectangle);
            if (visibleBounds.Width > 0 &&
                visibleBounds.Height > 0)
            {
                var x = visibleBounds.Left +
                    (visibleBounds.Width / 2);
                var firstY = Math.Max(
                    visibleBounds.Top + 1,
                    renderer.ClientRectangle.Top + 1);
                var lastY = Math.Max(
                    firstY,
                    visibleBounds.Bottom - 1);
                for (var y = firstY;
                    y <= lastY;
                    y += Math.Max(1, Math.Min(8, lastY - firstY + 1)))
                {
                    var point = renderer.PointToPdf(
                        new Point(x, y));
                    if (!point.IsValid || point.Page != pageIndex)
                    {
                        continue;
                    }

                    var destination =
                        PdfBookmarkService.CreateDestinationFromPdfPoint(
                            bookmarkDocument,
                            pageIndex + 1,
                            point.Location.X,
                            point.Location.Y,
                            null);
                    var top = destination.TopPositionPercent.HasValue &&
                        destination.TopPositionPercent.Value > 0.5D
                            ? destination.TopPositionPercent
                            : null;
                    return new PdfBookmarkDestination(
                        pageIndex + 1,
                        top);
                }
            }

            return new PdfBookmarkDestination(
                pageIndex + 1,
                null);
        }

        private void UpdatePageIndicator(bool forceTextUpdate)
        {
            var workspace = activeWorkspace;
            if (workspace == null ||
                !workspace.IsLoaded ||
                workspace.Document.PageCount == 0)
            {
                currentPageTextBox.Text = string.Empty;
                pageTotalLabel.Text = "/ 0";
                currentPageTextBox.Enabled = false;
                previousPageButton.Enabled = false;
                nextPageButton.Enabled = false;
                paperEyebrowLabel.Visible = false;
                paperSizeLabel.Visible = false;
                toolTip.SetToolTip(paperSizeLabel, null);
                return;
            }

            if (comparisonSurface != null)
            {
                currentPageTextBox.Enabled = false;
                previousPageButton.Enabled = false;
                nextPageButton.Enabled = false;
                return;
            }

            var pageCount = workspace.Document.PageCount;
            var currentPage = Math.Max(
                0,
                Math.Min(pageCount - 1, workspace.Viewer.Renderer.Page));
            if (workspace.RectangleZoom != null)
            {
                workspace.RectangleZoom.NotifyActivePage(currentPage);
            }
            if (workspace.TextEditSelection != null)
            {
                workspace.TextEditSelection.NotifyActivePage(currentPage);
            }
            if (workspace.Measurement != null)
            {
                workspace.Measurement.NotifyActivePage(currentPage);
            }

            if (!forceTextUpdate &&
                currentPage == workspace.DisplayedPageIndex)
            {
                return;
            }

            workspace.DisplayedPageIndex = currentPage;
            if (forceTextUpdate || !currentPageTextBox.Focused)
            {
                currentPageTextBox.Text = (currentPage + 1).ToString();
            }

            pageTotalLabel.Text = "/ " + pageCount;
            currentPageTextBox.Enabled = true;
            previousPageButton.Enabled = currentPage > 0;
            nextPageButton.Enabled = currentPage < pageCount - 1;
            UpdatePaperSizeIndicator(workspace, currentPage);
            workspace.Thumbnails.SetActivePage(currentPage, false);
        }

        private void UpdatePaperSizeIndicator(
            PdfWorkspace workspace,
            int pageIndex)
        {
            if (workspace == null ||
                !workspace.IsLoaded ||
                workspace.Document == null ||
                pageIndex < 0 ||
                pageIndex >= workspace.Document.PageCount ||
                pageIndex >= workspace.Document.PageSizes.Count)
            {
                paperEyebrowLabel.Visible = false;
                paperSizeLabel.Visible = false;
                toolTip.SetToolTip(paperSizeLabel, null);
                return;
            }

            var rotation = workspace.Viewer.Renderer.Rotation;
            var swapWidthAndHeight =
                rotation == PdfRotation.Rotate90 ||
                rotation == PdfRotation.Rotate270;
            var pageInfo = PdfPageSizeFormatter.FromPoints(
                workspace.Document.PageSizes[pageIndex],
                swapWidthAndHeight);
            if (!pageInfo.IsValid)
            {
                paperEyebrowLabel.Visible = false;
                paperSizeLabel.Visible = false;
                toolTip.SetToolTip(paperSizeLabel, null);
                return;
            }

            paperEyebrowLabel.Text =
                "FORMATO / " + pageInfo.OrientationName;
            paperSizeLabel.Text = pageInfo.CompactText;
            paperEyebrowLabel.Visible = true;
            paperSizeLabel.Visible = true;

            var standardName = string.IsNullOrWhiteSpace(
                pageInfo.StandardName)
                    ? "Personalizado"
                    : pageInfo.StandardName;
            toolTip.SetToolTip(
                paperSizeLabel,
                standardName + " " +
                pageInfo.OrientationName.ToLowerInvariant() +
                "\r\n" + pageInfo.MillimetreText +
                "\r\n" + pageInfo.CentimetreText +
                "\r\n" +
                pageInfo.WidthPoints.ToString("0.#") +
                " × " +
                pageInfo.HeightPoints.ToString("0.#") +
                " pt");
        }

        private void ShowNavigationMode(
            PdfWorkspace workspace,
            bool showBookmarks)
        {
            if (workspace == null)
            {
                return;
            }

            if (showBookmarks)
            {
                PopulateBookmarks(workspace);
            }

            workspace.ShowingBookmarks = showBookmarks;
            workspace.Thumbnails.Visible = !showBookmarks &&
                !workspace.NavigationCollapsed;
            workspace.BookmarksTree.Visible = showBookmarks &&
                !workspace.NavigationCollapsed;
            workspace.EditBookmarksButton.Visible =
                showBookmarks && !workspace.NavigationCollapsed;

            workspace.PagesButton.BackColor = showBookmarks
                ? NavigationBackgroundColor
                : AccentTintColor;
            workspace.PagesButton.ForeColor = showBookmarks
                ? BodyColor
                : AccentTextColor;
            workspace.BookmarksButton.BackColor = showBookmarks
                ? AccentTintColor
                : NavigationBackgroundColor;
            workspace.BookmarksButton.ForeColor = showBookmarks
                ? AccentTextColor
                : BodyColor;

        }

        private void SetNavigationCollapsed(
            PdfWorkspace workspace,
            bool collapsed)
        {
            if (workspace == null)
            {
                return;
            }

            if (collapsed && !workspace.NavigationCollapsed)
            {
                workspace.ExpandedNavigationWidth =
                    workspace.NavigationPanel.Width;
            }
            else if (!collapsed && workspace.ExpandedNavigationWidth <= 0)
            {
                workspace.ExpandedNavigationWidth =
                    Math.Max(
                        ExpandedNavigationWidth,
                        workspace.NavigationPanel.Width);
            }

            workspace.NavigationCollapsed = collapsed;
            workspace.NavigationPanel.Width = collapsed
                ? Math.Max(
                    CollapsedNavigationWidth,
                    workspace.CollapseNavigationButton.Width + 6)
                : Math.Max(
                    ExpandedNavigationWidth,
                    workspace.ExpandedNavigationWidth);
            workspace.PagesButton.Visible = !collapsed;
            workspace.BookmarksButton.Visible = !collapsed;
            workspace.EditBookmarksButton.Visible =
                !collapsed && workspace.ShowingBookmarks;
            workspace.CollapseNavigationButton.Text = collapsed
                ? "\u203A"
                : "\u2039";

            workspace.Thumbnails.Visible = !collapsed &&
                !workspace.ShowingBookmarks;
            workspace.BookmarksTree.Visible = !collapsed &&
                workspace.ShowingBookmarks;
            workspace.CollapseNavigationButton.Left = collapsed
                ? Math.Max(
                    1,
                    (workspace.NavigationHeader.ClientSize.Width -
                     workspace.CollapseNavigationButton.Width) / 2)
                : Math.Max(
                    1,
                    workspace.NavigationHeader.ClientSize.Width -
                    workspace.CollapseNavigationButton.Width -
                    3);
        }

        private void BeginPdfPageReorder(
            PdfWorkspace workspace,
            PdfThumbnailPagesReorderRequestedEventArgs e)
        {
            if (!CanStartPageOrganization(workspace) || e == null)
            {
                return;
            }

            var pageCount = workspace.Document.PageCount;
            var selected = NormalizeSelectedPages(
                e.PageIndexes,
                pageCount);
            if (selected.Count == 0)
            {
                return;
            }

            var selectedSet = new HashSet<int>(selected);
            var remaining = Enumerable
                .Range(0, pageCount)
                .Where(index => !selectedSet.Contains(index))
                .ToList();
            var originalBoundary = Math.Max(
                0,
                Math.Min(pageCount, e.InsertionPageIndex));
            var destinationIndex = Math.Max(
                0,
                Math.Min(
                    remaining.Count,
                    originalBoundary -
                    selected.Count(index => index < originalBoundary)));
            var finalOrder = new List<int>(pageCount);
            finalOrder.AddRange(
                remaining.Take(destinationIndex));
            finalOrder.AddRange(selected);
            finalOrder.AddRange(
                remaining.Skip(destinationIndex));

            if (finalOrder.SequenceEqual(
                    Enumerable.Range(0, pageCount)))
            {
                return;
            }

            var activeSourcePage =
                workspace.Thumbnails.SelectedPage;
            var preferredPage = Math.Max(
                0,
                finalOrder.IndexOf(activeSourcePage));
            var selectedResultPages = Enumerable.Range(
                    destinationIndex,
                    selected.Count)
                .ToList();
            BeginPdfPageOrganization(
                workspace,
                finalOrder.Select(index =>
                    new PdfPageOrganizerPage(index + 1, 0))
                    .ToList(),
                preferredPage,
                selectedResultPages,
                selected.Count == 1
                    ? "Página reordenada"
                    : selected.Count + " páginas reordenadas",
                "Reordenando páginas…",
                false);
        }

        private void BeginPdfThumbnailPageOperation(
            PdfWorkspace workspace,
            PdfThumbnailPageOperationRequestedEventArgs e)
        {
            if (!CanStartPageOrganization(workspace) || e == null)
            {
                return;
            }

            var pageCount = workspace.Document.PageCount;
            var selected = NormalizeSelectedPages(
                e.PageIndexes,
                pageCount);
            if (selected.Count == 0)
            {
                return;
            }

            if (e.Operation == PdfThumbnailPageOperation.Delete)
            {
                if (selected.Count >= pageCount)
                {
                    MessageBox.Show(
                        this,
                        "El PDF debe conservar al menos una página.",
                        "Eliminar páginas",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                var answer = MessageBox.Show(
                    this,
                    selected.Count == 1
                        ? "¿Quieres eliminar la página seleccionada?"
                        : "¿Quieres eliminar las " +
                            selected.Count +
                            " páginas seleccionadas?",
                    "Eliminar páginas",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);
                if (answer != DialogResult.Yes)
                {
                    return;
                }

                var selectedSet = new HashSet<int>(selected);
                var finalOrder = Enumerable
                    .Range(0, pageCount)
                    .Where(index => !selectedSet.Contains(index))
                    .ToList();
                var firstDeletedPage = selected[0];
                var preferredPage = Math.Max(
                    0,
                    Math.Min(
                        finalOrder.Count - 1,
                        firstDeletedPage -
                        selected.Count(index =>
                            index < firstDeletedPage)));
                BeginPdfPageOrganization(
                    workspace,
                    finalOrder.Select(index =>
                        new PdfPageOrganizerPage(index + 1, 0))
                        .ToList(),
                    preferredPage,
                    new[] { preferredPage },
                    selected.Count == 1
                        ? "Página eliminada"
                        : selected.Count + " páginas eliminadas",
                    "Eliminando páginas…",
                    false);
                return;
            }

            var clockwiseRotation =
                e.Operation == PdfThumbnailPageOperation.RotateLeft
                    ? -90
                    : 90;
            var pagesToRotate = new HashSet<int>(selected);
            var activePage = selected.Contains(e.ActivePageIndex)
                ? e.ActivePageIndex
                : selected[0];
            BeginPdfPageOrganization(
                workspace,
                Enumerable.Range(0, pageCount)
                    .Select(index =>
                        new PdfPageOrganizerPage(
                            index + 1,
                            pagesToRotate.Contains(index)
                                ? clockwiseRotation
                                : 0))
                    .ToList(),
                activePage,
                selected,
                selected.Count == 1
                    ? "Página girada"
                    : selected.Count + " páginas giradas",
                "Girando páginas…",
                true);
        }

        private bool CanStartPageOrganization(PdfWorkspace workspace)
        {
            if (workspace == null ||
                workspace.IsDisposed ||
                !workspace.IsLoaded ||
                workspace.Document == null ||
                workspace.Document.PageCount < 1 ||
                workspace.EditSession == null ||
                workspace.EditHistoryFaulted ||
                workspace.IsPasswordProtected)
            {
                return false;
            }

            if (IsPageStructureOperationInProgress ||
                pageOrganizerWorker.IsBusy)
            {
                if (workspace == activeWorkspace)
                {
                    documentLabel.Text =
                        "Ya hay otra edición en curso. Espera un momento…";
                }

                System.Media.SystemSounds.Beep.Play();
                return false;
            }

            return true;
        }

        private static List<int> NormalizeSelectedPages(
            IEnumerable<int> pageIndexes,
            int pageCount)
        {
            return (pageIndexes ?? Enumerable.Empty<int>())
                .Where(index =>
                    index >= 0 &&
                    index < pageCount)
                .Distinct()
                .OrderBy(index => index)
                .ToList();
        }

        private void BeginPdfPageOrganization(
            PdfWorkspace workspace,
            IList<PdfPageOrganizerPage> pages,
            int preferredPageIndex,
            IEnumerable<int> resultSelectionIndexes,
            string description,
            string status,
            bool resetVisualRotation)
        {
            if (!CanStartPageOrganization(workspace))
            {
                return;
            }

            long estimatedOutputBytes = 0;
            try
            {
                estimatedOutputBytes =
                    new FileInfo(workspace.ContentPath).Length;
            }
            catch
            {
            }

            var request = new PdfPageOrganizationUiRequest(
                workspace,
                workspace.EditSession,
                workspace.ContentPath,
                pages,
                preferredPageIndex,
                resultSelectionIndexes,
                description,
                status,
                estimatedOutputBytes,
                resetVisualRotation);
            pendingPageOrganizationRequest = request;
            pageOrganizationInProgress = true;
            SetPageOrganizationStatus(request, "Comprobando el PDF…");
            RefreshToolAvailability();
            StartPageOrganizerWorker(
                new PdfPageOrganizationWorkerJob(
                    PdfPageOrganizationWorkerJobKind.Analyze,
                    request));
        }

        private void StartPageOrganizerWorker(
            PdfPageOrganizationWorkerJob job)
        {
            currentPageOrganizationJob = job;
            try
            {
                pageOrganizerWorker.RunWorkerAsync(job);
            }
            catch (Exception ex)
            {
                CancelPageOrganizationRevision(
                    job == null ? null : job.Request);
                FinishPageOrganizationOperation();
                ShowPageOrganizationError(ex);
            }
        }

        private void PageOrganizerWorker_DoWork(
            object sender,
            DoWorkEventArgs e)
        {
            var job = e.Argument as PdfPageOrganizationWorkerJob;
            if (job == null)
            {
                throw new InvalidOperationException(
                    "No se pudo preparar la organización de páginas.");
            }

            if (job.Kind ==
                PdfPageOrganizationWorkerJobKind.Analyze)
            {
                e.Result = PdfPageOrganizerService.Analyze(
                    job.Request.BasePath,
                    job.Request.Pages);
                return;
            }

            e.Result = PdfPageOrganizerService.Organize(
                job.Request.BasePath,
                job.Request.Pages,
                job.Request.OutputPath,
                delegate(PdfPageOrganizerProgress progress)
                {
                    pageOrganizerWorker.ReportProgress(
                        progress.Percentage,
                        progress);
                });
        }

        private void PageOrganizerWorker_ProgressChanged(
            object sender,
            ProgressChangedEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(
                    new ProgressChangedEventHandler(
                        PageOrganizerWorker_ProgressChanged),
                    sender,
                    e);
                return;
            }

            var request = pendingPageOrganizationRequest;
            var progress =
                e.UserState as PdfPageOrganizerProgress;
            if (request == null || progress == null)
            {
                return;
            }

            SetPageOrganizationStatus(
                request,
                request.StatusText + " " +
                progress.Percentage + "% · " +
                progress.Stage);
        }

        private void PageOrganizerWorker_RunWorkerCompleted(
            object sender,
            RunWorkerCompletedEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(
                    new RunWorkerCompletedEventHandler(
                        PageOrganizerWorker_RunWorkerCompleted),
                    sender,
                    e);
                return;
            }

            var job = currentPageOrganizationJob;
            currentPageOrganizationJob = null;
            if (e.Error != null)
            {
                CancelPageOrganizationRevision(
                    job == null ? null : job.Request);
                FinishPageOrganizationOperation();
                ShowPageOrganizationError(e.Error);
                return;
            }

            if (e.Cancelled || job == null)
            {
                CancelPageOrganizationRevision(
                    job == null ? null : job.Request);
                FinishPageOrganizationOperation();
                return;
            }

            var request = job.Request;
            if (!IsCurrentPageOrganizationRequest(request))
            {
                CancelPageOrganizationRevision(request);
                FinishPageOrganizationOperation();
                ShowPageOrganizationError(
                    new InvalidOperationException(
                        "El documento cambió antes de terminar la operación."));
                return;
            }

            if (job.Kind ==
                PdfPageOrganizationWorkerJobKind.Analyze)
            {
                var analysis =
                    e.Result as PdfPageOrganizerAnalysis;
                if (analysis == null)
                {
                    FinishPageOrganizationOperation();
                    ShowPageOrganizationError(
                        new InvalidDataException(
                            "No se pudo comprobar el PDF."));
                    return;
                }

                request.Analysis = analysis;
                if (analysis.DigitalSignaturesWillBeInvalidated)
                {
                    var answer = MessageBox.Show(
                        this,
                        "Este PDF contiene firmas digitales.\r\n\r\n" +
                        "El original seguirá intacto, pero la copia editada " +
                        "ya no conservará la validez de esas firmas y " +
                        "tendrás que firmarla de nuevo.\r\n\r\n" +
                        "¿Quieres continuar?",
                        "Organizar PDF firmado",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button2);
                    if (answer != DialogResult.Yes)
                    {
                        FinishPageOrganizationOperation();
                        return;
                    }
                }

                try
                {
                    request.OutputPath =
                        request.SourceEditSession
                            .ReserveRevisionPath(
                                request.EstimatedOutputBytes);
                }
                catch (Exception ex)
                {
                    FinishPageOrganizationOperation();
                    ShowPageOrganizationError(ex);
                    return;
                }

                SetPageOrganizationStatus(
                    request,
                    request.StatusText);
                StartPageOrganizerWorker(
                    new PdfPageOrganizationWorkerJob(
                        PdfPageOrganizationWorkerJobKind.Organize,
                        request));
                return;
            }

            var result = e.Result as PdfPageOrganizerResult;
            if (result == null ||
                string.IsNullOrWhiteSpace(result.OutputPath) ||
                !File.Exists(result.OutputPath))
            {
                CancelPageOrganizationRevision(request);
                FinishPageOrganizationOperation();
                ShowPageOrganizationError(
                    new InvalidDataException(
                        "La copia organizada no se pudo comprobar."));
                return;
            }

            CompletePageOrganization(request, result);
        }

        private void CompletePageOrganization(
            PdfPageOrganizationUiRequest request,
            PdfPageOrganizerResult result)
        {
            PdfiumDocument preparedDocument = null;
            PdfEditSession.RevisionCommit revisionCommit = null;
            var activationCompleted = false;
            try
            {
                if (!IsCurrentPageOrganizationRequest(request))
                {
                    throw new InvalidOperationException(
                        "El documento cambió antes de activar el resultado.");
                }

                preparedDocument = PdfDocumentOpenService.Load(result.OutputPath);
                if (preparedDocument.PageCount != result.PageCount ||
                    preparedDocument.PageCount < 1)
                {
                    throw new InvalidDataException(
                        "La revisión organizada no contiene las páginas esperadas.");
                }

                revisionCommit =
                    request.SourceEditSession.BeginRevisionCommit(
                    result.OutputPath,
                    request.Description);

                var documentToApply = preparedDocument;
                preparedDocument = null;
                var applied = ApplyRevisionToWorkspace(
                    request.SourceWorkspace,
                    result.OutputPath,
                    request.PreferredPageIndex,
                    documentToApply);
                if (!applied)
                {
                    CompensateFailedPageOrganization(
                        request,
                        revisionCommit);
                    throw new InvalidOperationException(
                        "La copia se creó, pero no pudo activarse con seguridad.");
                }

                revisionCommit.Complete();
                revisionCommit = null;
                activationCompleted = true;
            }
            catch (Exception ex)
            {
                if (preparedDocument != null)
                {
                    preparedDocument.Dispose();
                }

                if (revisionCommit != null &&
                    !revisionCommit.IsFinished)
                {
                    try
                    {
                        CompensateFailedPageOrganization(
                            request,
                            revisionCommit);
                    }
                    catch (Exception compensationError)
                    {
                        AppLog.Write(
                            "No se pudo revertir el commit de organización: " +
                            compensationError);
                    }
                }

                if (!request.SourceWorkspace.EditHistoryFaulted &&
                    !string.Equals(
                        request.SourceEditSession.CurrentPath,
                        result.OutputPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    CancelPageOrganizationRevision(request);
                }

                AppLog.Write(
                    "No se pudo activar la organización de páginas: " +
                    ex);
                ShowPageOrganizationError(ex);
            }
            finally
            {
                if (activationCompleted)
                {
                    RefreshAfterPageOrganization(request);
                }

                FinishPageOrganizationOperation();
            }
        }

        private void CompensateFailedPageOrganization(
            PdfPageOrganizationUiRequest request,
            PdfEditSession.RevisionCommit revisionCommit)
        {
            if (request == null ||
                request.SourceWorkspace == null ||
                revisionCommit == null ||
                revisionCommit.IsFinished)
            {
                return;
            }

            if (request.SourceWorkspace.EditHistoryFaulted)
            {
                try
                {
                    revisionCommit.PreserveForRecovery();
                }
                catch
                {
                }

                return;
            }

            try
            {
                revisionCommit.Rollback();
                if (!string.Equals(
                        request.SourceEditSession.CurrentPath,
                        request.BasePath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "El historial no volvió a la revisión anterior.");
                }
            }
            catch
            {
                request.SourceWorkspace.EditHistoryFaulted = true;
                request.SourceWorkspace.FaultedChangesSaved = false;
                request.SourceWorkspace.DeleteRecoveryOnClose = false;
                try
                {
                    if (!revisionCommit.IsFinished)
                    {
                        revisionCommit.PreserveForRecovery();
                    }

                    request.SourceEditSession.PreserveRecovery();
                }
                catch
                {
                }

                throw;
            }
        }

        private void RefreshAfterPageOrganization(
            PdfPageOrganizationUiRequest request)
        {
            if (request == null ||
                request.SourceWorkspace == null ||
                request.SourceWorkspace.IsDisposed)
            {
                return;
            }

            try
            {
                if (request.ResetVisualRotation)
                {
                    request.SourceWorkspace.Viewer.Renderer.Rotation =
                        PdfRotation.Rotate0;
                }
            }
            catch (Exception ex)
            {
                AppLog.Write(
                    "Organización aplicada; no se pudo reiniciar el giro visual: " +
                    ex);
            }

            try
            {
                request.SourceWorkspace.Thumbnails.SetSelectedPages(
                    request.ResultSelectionIndexes,
                    request.PreferredPageIndex,
                    true);
            }
            catch (Exception ex)
            {
                AppLog.Write(
                    "Organización aplicada; no se pudo restaurar la selección: " +
                    ex);
            }

            try
            {
                request.SourceEditSession.CleanupObsoleteRevisions(
                    request.SourceWorkspace.ContentPath);
            }
            catch (Exception ex)
            {
                AppLog.Write(
                    "Organización aplicada; la limpieza se aplazó: " +
                    ex);
            }

            try
            {
                if (request.SourceWorkspace == activeWorkspace)
                {
                    UpdatePageIndicator(true);
                }

                RefreshWorkspaceEditState(
                    request.SourceWorkspace);
            }
            catch (Exception ex)
            {
                AppLog.Write(
                    "Organización aplicada; no se pudo refrescar la interfaz: " +
                    ex);
            }
        }

        private bool IsCurrentPageOrganizationRequest(
            PdfPageOrganizationUiRequest request)
        {
            return request != null &&
                pageOrganizationInProgress &&
                ReferenceEquals(
                    pendingPageOrganizationRequest,
                    request) &&
                request.SourceWorkspace != null &&
                !request.SourceWorkspace.IsDisposed &&
                ReferenceEquals(
                    request.SourceWorkspace.EditSession,
                    request.SourceEditSession) &&
                string.Equals(
                    request.SourceWorkspace.ContentPath,
                    request.BasePath,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    request.SourceEditSession.CurrentPath,
                    request.BasePath,
                    StringComparison.OrdinalIgnoreCase);
        }

        private static void CancelPageOrganizationRevision(
            PdfPageOrganizationUiRequest request)
        {
            if (request == null ||
                request.SourceEditSession == null ||
                string.IsNullOrWhiteSpace(request.OutputPath))
            {
                return;
            }

            request.SourceEditSession.CancelReservedRevision(
                request.OutputPath);
        }

        private void SetPageOrganizationStatus(
            PdfPageOrganizationUiRequest request,
            string status)
        {
            if (request != null &&
                request.SourceWorkspace == activeWorkspace &&
                !request.SourceWorkspace.IsDisposed)
            {
                documentLabel.Text = status;
            }
        }

        private void FinishPageOrganizationOperation()
        {
            pageOrganizationInProgress = false;
            pendingPageOrganizationRequest = null;
            currentPageOrganizationJob = null;
            foreach (var workspace in workspaces)
            {
                if (!workspace.IsDisposed &&
                    workspace.Thumbnails != null)
                {
                    workspace.Thumbnails.PageOperationsEnabled =
                        !workspace.EditHistoryFaulted;
                }
            }

            if (activeWorkspace != null &&
                !activeWorkspace.IsDisposed)
            {
                RefreshWorkspaceEditState(activeWorkspace);
            }
            else
            {
                RefreshToolAvailability();
            }
        }

        private void ShowPageOrganizationError(Exception error)
        {
            ShowPdfProblem(
                "Organizar páginas",
                "No se pudieron organizar las páginas.",
                "El PDF original no se ha modificado.",
                error,
                activeWorkspace == null ? null : activeWorkspace.ContentPath);
        }

        private void OcrToolButton_Click(object sender, EventArgs e)
        {
            if (ocrInProgress)
            {
                CancelCurrentOcr();
                return;
            }

            var workspace = GetLoadedActiveWorkspace();
            if (workspace == null ||
                workspace.Document == null ||
                workspace.Document.PageCount < 1 ||
                workspace.EditSession == null ||
                workspace.EditHistoryFaulted ||
                workspace.IsPasswordProtected)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }

            if (IsPageStructureOperationInProgress ||
                ocrWorker.IsBusy)
            {
                documentLabel.Text =
                    "Ya hay otra edición en curso. Espera un momento…";
                System.Media.SystemSounds.Beep.Play();
                return;
            }

            var pageCount = workspace.Document.PageCount;
            var currentPageIndex = Math.Max(
                0,
                Math.Min(
                    pageCount - 1,
                    workspace.Viewer.Renderer.Page));
            var selectedPages = NormalizeSelectedPages(
                workspace.Thumbnails.SelectedPages,
                pageCount);
            PdfOcrSettings settings;
            CancelRectangleZoom(workspace);
            using (var options = new PdfOcrOptionsForm(
                pageCount,
                currentPageIndex,
                selectedPages))
            {
                if (options.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                settings = options.Settings;
            }

            long estimatedOutputBytes = 0;
            try
            {
                var sourceLength =
                    new FileInfo(workspace.ContentPath).Length;
                estimatedOutputBytes = sourceLength >
                    (long.MaxValue / 5L) * 4L
                        ? sourceLength
                        : Math.Max(
                            sourceLength,
                            sourceLength + sourceLength / 4L);
            }
            catch
            {
            }

            if (selectedPages.Count == 0)
            {
                selectedPages.Add(currentPageIndex);
            }

            var request = new PdfOcrUiRequest(
                workspace,
                workspace.EditSession,
                workspace.ContentPath,
                settings,
                currentPageIndex,
                selectedPages,
                estimatedOutputBytes);

            pendingOcrRequest = request;
            currentOcrCancellation = new CancellationTokenSource();
            ocrInProgress = true;
            SetOcrStatus(request, "Analizando páginas para OCR…");
            RefreshToolAvailability();
            StartOcrWorker(
                new PdfOcrWorkerJob(
                    PdfOcrWorkerJobKind.Analyze,
                    request,
                    currentOcrCancellation.Token));
        }

        private void StartOcrWorker(PdfOcrWorkerJob job)
        {
            currentOcrJob = job;
            try
            {
                ShowOcrProgress(
                    job.Kind == PdfOcrWorkerJobKind.Analyze
                        ? "Analizando páginas…"
                        : "Aplicando OCR…");
                ocrWorker.RunWorkerAsync(job);
            }
            catch (Exception ex)
            {
                CancelOcrRevision(job == null ? null : job.Request);
                FinishOcrOperation();
                ShowOcrError(ex);
            }
        }

        private void OcrWorker_DoWork(
            object sender,
            DoWorkEventArgs e)
        {
            var job = e.Argument as PdfOcrWorkerJob;
            if (job == null)
            {
                throw new InvalidOperationException(
                    "No se pudo preparar el trabajo OCR.");
            }

            try
            {
                if (job.Kind == PdfOcrWorkerJobKind.Analyze)
                {
                    e.Result = PdfOcrService.Analyze(
                        job.Request.BasePath,
                        job.Request.Settings,
                        delegate(PdfOcrProgress progress)
                        {
                            ReportOcrProgress(job, progress);
                        },
                        job.CancellationToken);
                }
                else
                {
                    e.Result = PdfOcrService.Process(
                        job.Request.BasePath,
                        job.Request.OutputPath,
                        job.Request.Analysis,
                        job.Request.Instructions,
                        job.Request.Settings,
                        delegate(PdfOcrProgress progress)
                        {
                            ReportOcrProgress(job, progress);
                        },
                        job.CancellationToken);
                }

                if (job.CancellationToken.IsCancellationRequested)
                {
                    e.Cancel = true;
                }
            }
            catch (OperationCanceledException)
            {
                e.Cancel = true;
            }
        }

        private void ReportOcrProgress(
            PdfOcrWorkerJob job,
            PdfOcrProgress progress)
        {
            if (job == null ||
                progress == null ||
                job.CancellationToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                ocrWorker.ReportProgress(
                    progress.Percentage,
                    progress);
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void OcrWorker_ProgressChanged(
            object sender,
            ProgressChangedEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(
                    new ProgressChangedEventHandler(
                        OcrWorker_ProgressChanged),
                    sender,
                    e);
                return;
            }

            var request = pendingOcrRequest;
            var progress = e.UserState as PdfOcrProgress;
            if (request == null || progress == null)
            {
                return;
            }

            SetOcrStatus(
                request,
                progress.Stage + " · " + progress.Percentage + "%");
            if (ocrProgressForm != null &&
                !ocrProgressForm.IsDisposed)
            {
                ocrProgressForm.UpdateProgress(progress);
            }
        }

        private void OcrWorker_RunWorkerCompleted(
            object sender,
            RunWorkerCompletedEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(
                    new RunWorkerCompletedEventHandler(
                        OcrWorker_RunWorkerCompleted),
                    sender,
                    e);
                return;
            }

            var job = currentOcrJob;
            currentOcrJob = null;
            CloseOcrProgress();

            if (e.Error != null)
            {
                CancelOcrRevision(job == null ? null : job.Request);
                FinishOcrOperation();
                ShowOcrError(e.Error);
                return;
            }

            if (e.Cancelled ||
                job == null ||
                job.CancellationToken.IsCancellationRequested)
            {
                CancelOcrRevision(job == null ? null : job.Request);
                FinishOcrOperation();
                return;
            }

            var request = job.Request;
            if (!IsCurrentOcrRequest(request))
            {
                CancelOcrRevision(request);
                FinishOcrOperation();
                ShowOcrError(
                    new InvalidOperationException(
                        "El documento cambió antes de terminar el OCR."));
                return;
            }

            if (job.Kind == PdfOcrWorkerJobKind.Analyze)
            {
                ContinueOcrAfterAnalysis(
                    request,
                    e.Result as PdfOcrAnalysis);
                return;
            }

            var result = e.Result as PdfOcrResult;
            if (result == null ||
                string.IsNullOrWhiteSpace(result.OutputPath) ||
                !File.Exists(result.OutputPath))
            {
                CancelOcrRevision(request);
                FinishOcrOperation();
                ShowOcrError(
                    new InvalidDataException(
                        "La copia OCR no se pudo comprobar."));
                return;
            }

            CompleteOcr(request, result);
        }

        private void ContinueOcrAfterAnalysis(
            PdfOcrUiRequest request,
            PdfOcrAnalysis analysis)
        {
            if (analysis == null)
            {
                FinishOcrOperation();
                ShowOcrError(
                    new InvalidDataException(
                        "No se pudo analizar el documento para OCR."));
                return;
            }

            request.Analysis = analysis;
            if (analysis.ContainsXfa)
            {
                FinishOcrOperation();
                ShowOcrError(
                    new NotSupportedException(
                        PdfOcrService.XfaUnsupportedMessage));
                return;
            }

            var defaults =
                PdfOcrService.CreateDefaultInstructions(analysis);
            if (!defaults.Any(instruction => instruction.Process))
            {
                FinishOcrOperation();
                MessageBox.Show(
                    this,
                    "Las páginas elegidas ya contienen texto buscable.\r\n\r\n" +
                    "No se ha creado ninguna revisión y el PDF sigue intacto.",
                    "OCR no necesario",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (analysis.ContainsDigitalSignatures)
            {
                var answer = MessageBox.Show(
                    this,
                    "Este PDF contiene firmas digitales.\r\n\r\n" +
                    "El original seguirá intacto, pero la copia con OCR " +
                    "ya no conservará la validez criptográfica de esas " +
                    "firmas y tendrás que firmarla de nuevo.\r\n\r\n" +
                    "¿Quieres continuar?",
                    "OCR en PDF firmado",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);
                if (answer != DialogResult.Yes)
                {
                    FinishOcrOperation();
                    return;
                }
            }

            IList<PdfOcrPageInstruction> instructions;
            using (var review = new PdfOcrReviewForm(
                request.BasePath,
                analysis))
            {
                if (review.ShowDialog(this) != DialogResult.OK)
                {
                    FinishOcrOperation();
                    return;
                }

                instructions = new List<PdfOcrPageInstruction>(
                    review.Instructions);
            }

            if (!IsCurrentOcrRequest(request))
            {
                FinishOcrOperation();
                ShowOcrError(
                    new InvalidOperationException(
                        "El documento cambió mientras se revisaba el OCR."));
                return;
            }

            if (!instructions.Any(instruction => instruction.Process))
            {
                FinishOcrOperation();
                return;
            }

            request.Instructions = instructions;
            try
            {
                request.OutputPath =
                    request.SourceEditSession.ReserveRevisionPath(
                        request.EstimatedOutputBytes);
            }
            catch (Exception ex)
            {
                FinishOcrOperation();
                ShowOcrError(ex);
                return;
            }

            SetOcrStatus(request, "Aplicando OCR página a página…");
            StartOcrWorker(
                new PdfOcrWorkerJob(
                    PdfOcrWorkerJobKind.Process,
                    request,
                    currentOcrCancellation.Token));
        }

        private void CompleteOcr(
            PdfOcrUiRequest request,
            PdfOcrResult result)
        {
            PdfiumDocument preparedDocument = null;
            PdfEditSession.RevisionCommit revisionCommit = null;
            var activationCompleted = false;
            try
            {
                if (!IsCurrentOcrRequest(request))
                {
                    throw new InvalidOperationException(
                        "El documento cambió antes de activar el OCR.");
                }

                CaptureOcrViewState(request);
                preparedDocument = PdfDocumentOpenService.Load(result.OutputPath);
                if (preparedDocument.PageCount != result.PageCount ||
                    preparedDocument.PageCount < 1)
                {
                    throw new InvalidDataException(
                        "La revisión OCR no contiene las páginas esperadas.");
                }

                revisionCommit =
                    request.SourceEditSession.BeginRevisionCommit(
                        result.OutputPath,
                        "OCR y enderezado");

                var documentToApply = preparedDocument;
                preparedDocument = null;
                if (!ApplyRevisionToWorkspace(
                        request.SourceWorkspace,
                        result.OutputPath,
                        request.PreferredPageIndex,
                        documentToApply))
                {
                    CompensateFailedOcr(request, revisionCommit);
                    throw new InvalidOperationException(
                        "La copia OCR se creó, pero no pudo activarse con seguridad.");
                }

                revisionCommit.Complete();
                revisionCommit = null;
                activationCompleted = true;
            }
            catch (Exception ex)
            {
                if (preparedDocument != null)
                {
                    preparedDocument.Dispose();
                }

                if (revisionCommit != null &&
                    !revisionCommit.IsFinished)
                {
                    try
                    {
                        CompensateFailedOcr(request, revisionCommit);
                    }
                    catch (Exception compensationError)
                    {
                        AppLog.Write(
                            "No se pudo revertir el commit OCR: " +
                            compensationError);
                    }
                }

                if (!request.SourceWorkspace.EditHistoryFaulted &&
                    !string.Equals(
                        request.SourceEditSession.CurrentPath,
                        result.OutputPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    CancelOcrRevision(request);
                }

                AppLog.Write(
                    "No se pudo activar la revisión OCR: " + ex);
                ShowOcrError(ex);
            }
            finally
            {
                if (activationCompleted)
                {
                    RefreshAfterOcr(request, result);
                }

                FinishOcrOperation();
            }
        }

        private static void CaptureOcrViewState(
            PdfOcrUiRequest request)
        {
            if (request == null ||
                request.SourceWorkspace == null ||
                request.SourceWorkspace.IsDisposed ||
                request.SourceWorkspace.Document == null)
            {
                return;
            }

            var pageCount =
                request.SourceWorkspace.Document.PageCount;
            request.PreferredPageIndex = Math.Max(
                0,
                Math.Min(
                    Math.Max(0, pageCount - 1),
                    request.SourceWorkspace.Viewer.Renderer.Page));
            request.OriginalSelectionIndexes = new List<int>(
                request.SourceWorkspace.Thumbnails.SelectedPages ??
                new List<int>());
        }

        private void CompensateFailedOcr(
            PdfOcrUiRequest request,
            PdfEditSession.RevisionCommit revisionCommit)
        {
            if (request == null ||
                request.SourceWorkspace == null ||
                revisionCommit == null ||
                revisionCommit.IsFinished)
            {
                return;
            }

            if (request.SourceWorkspace.EditHistoryFaulted)
            {
                revisionCommit.PreserveForRecovery();
                return;
            }

            try
            {
                revisionCommit.Rollback();
                if (!string.Equals(
                        request.SourceEditSession.CurrentPath,
                        request.BasePath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "El historial no volvió a la revisión anterior.");
                }
            }
            catch
            {
                request.SourceWorkspace.EditHistoryFaulted = true;
                request.SourceWorkspace.FaultedChangesSaved = false;
                request.SourceWorkspace.DeleteRecoveryOnClose = false;
                try
                {
                    if (!revisionCommit.IsFinished)
                    {
                        revisionCommit.PreserveForRecovery();
                    }

                    request.SourceEditSession.PreserveRecovery();
                }
                catch
                {
                }

                throw;
            }
        }

        private void RefreshAfterOcr(
            PdfOcrUiRequest request,
            PdfOcrResult result)
        {
            if (request == null ||
                request.SourceWorkspace == null ||
                request.SourceWorkspace.IsDisposed)
            {
                return;
            }

            try
            {
                if (result.OrientationCorrectionCount > 0)
                {
                    request.SourceWorkspace.Viewer.Renderer.Rotation =
                        PdfRotation.Rotate0;
                }

                var selectedPages = NormalizeSelectedPages(
                    request.OriginalSelectionIndexes,
                    result.PageCount);
                if (selectedPages.Count == 0)
                {
                    selectedPages.Add(request.PreferredPageIndex);
                }

                request.SourceWorkspace.Thumbnails.SetSelectedPages(
                    selectedPages,
                    request.PreferredPageIndex,
                    true);
            }
            catch (Exception ex)
            {
                AppLog.Write(
                    "OCR aplicado; no se pudo restaurar la vista: " + ex);
            }

            try
            {
                request.SourceEditSession.CleanupObsoleteRevisions(
                    request.SourceWorkspace.ContentPath);
            }
            catch (Exception ex)
            {
                AppLog.Write(
                    "OCR aplicado; la limpieza se aplazó: " + ex);
            }

            RefreshWorkspaceEditState(request.SourceWorkspace);
            if (request.SourceWorkspace == activeWorkspace)
            {
                documentLabel.Text = string.Format(
                    "{0} · OCR listo en {1} {2}",
                    request.SourceWorkspace.DisplayName,
                    result.ProcessedPageCount,
                    result.ProcessedPageCount == 1
                        ? "página"
                        : "páginas");
            }
        }

        private bool IsCurrentOcrRequest(PdfOcrUiRequest request)
        {
            return request != null &&
                ocrInProgress &&
                ReferenceEquals(pendingOcrRequest, request) &&
                request.SourceWorkspace != null &&
                !request.SourceWorkspace.IsDisposed &&
                ReferenceEquals(
                    request.SourceWorkspace.EditSession,
                    request.SourceEditSession) &&
                string.Equals(
                    request.SourceWorkspace.ContentPath,
                    request.BasePath,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    request.SourceEditSession.CurrentPath,
                    request.BasePath,
                    StringComparison.OrdinalIgnoreCase);
        }

        private static void CancelOcrRevision(PdfOcrUiRequest request)
        {
            if (request == null ||
                request.SourceEditSession == null ||
                string.IsNullOrWhiteSpace(request.OutputPath))
            {
                return;
            }

            request.SourceEditSession.CancelReservedRevision(
                request.OutputPath);
        }

        private void SetOcrStatus(
            PdfOcrUiRequest request,
            string status)
        {
            if (request != null &&
                request.SourceWorkspace == activeWorkspace &&
                !request.SourceWorkspace.IsDisposed)
            {
                documentLabel.Text = status;
            }
        }

        private void ShowOcrProgress(string stage)
        {
            CloseOcrProgress();
            var progress = new PdfOcrProgressForm(
                "OCR y enderezado",
                stage);
            progress.CancelRequested += OcrProgress_CancelRequested;
            ocrProgressForm = progress;
            progress.Show(this);
            progress.BringToFront();
        }

        private void OcrProgress_CancelRequested(
            object sender,
            EventArgs e)
        {
            CancelCurrentOcr();
        }

        private void CancelCurrentOcr()
        {
            if (!ocrInProgress)
            {
                return;
            }

            if (currentOcrCancellation != null &&
                !currentOcrCancellation.IsCancellationRequested)
            {
                currentOcrCancellation.Cancel();
            }

            if (ocrWorker.IsBusy &&
                ocrWorker.WorkerSupportsCancellation)
            {
                ocrWorker.CancelAsync();
            }

            if (ocrProgressForm != null &&
                !ocrProgressForm.IsDisposed)
            {
                ocrProgressForm.MarkCancelling();
            }

            SetOcrStatus(
                pendingOcrRequest,
                "Cancelando OCR con seguridad…");
        }

        private void CloseOcrProgress()
        {
            var progress = ocrProgressForm;
            ocrProgressForm = null;
            if (progress == null)
            {
                return;
            }

            progress.CancelRequested -= OcrProgress_CancelRequested;
            if (!progress.IsDisposed)
            {
                progress.CompleteAndClose();
                progress.Dispose();
            }
        }

        private void FinishOcrOperation()
        {
            CloseOcrProgress();
            ocrInProgress = false;
            pendingOcrRequest = null;
            currentOcrJob = null;
            if (currentOcrCancellation != null)
            {
                currentOcrCancellation.Dispose();
                currentOcrCancellation = null;
            }

            if (activeWorkspace != null &&
                !activeWorkspace.IsDisposed)
            {
                RefreshWorkspaceEditState(activeWorkspace);
            }
            else
            {
                RefreshToolAvailability();
            }
        }

        private void ShowOcrError(Exception error)
        {
            ShowPdfProblem(
                "OCR y enderezado",
                "No se pudo completar el OCR.",
                "El PDF original no se ha modificado.",
                error,
                activeWorkspace == null ? null : activeWorkspace.ContentPath);
        }

        private void BeginPdfPageInsert(
            PdfWorkspace workspace,
            PdfFilesInsertRequestedEventArgs e)
        {
            if (workspace == null ||
                workspace.IsDisposed ||
                !workspace.IsLoaded ||
                workspace.EditHistoryFaulted ||
                workspace.IsPasswordProtected ||
                e == null)
            {
                return;
            }

            if (IsPageStructureOperationInProgress ||
                pageInsertWorker.IsBusy)
            {
                if (workspace == activeWorkspace)
                {
                    documentLabel.Text =
                        "Ya se está insertando otro PDF. Espera un momento…";
                }

                System.Media.SystemSounds.Beep.Play();
                return;
            }

            var paths = new List<string>();
            foreach (var rawPath in e.PdfFilePaths)
            {
                var path = NormalizePdfPath(rawPath);
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    // Preserve Explorer's order. Repeating a source is intentional:
                    // it means insert that PDF more than once.
                    paths.Add(path);
                }
            }

            if (paths.Count == 0)
            {
                return;
            }

            var insertionIndex = Math.Max(
                0,
                Math.Min(workspace.Document.PageCount, e.InsertionPageIndex));
            var request = new PdfPageInsertRequest(
                workspace,
                workspace.ContentPath,
                paths,
                insertionIndex,
                EstimateCombinedPdfBytes(
                    workspace.ContentPath,
                    paths));

            pendingPageInsertRequest = request;
            pageInsertInProgress = true;
            SetPageInsertStatus(request, "Comprobando los PDF…");
            RefreshToolAvailability();
            StartPageInsertWorker(
                new PdfPageInsertWorkerJob(
                    PdfPageInsertWorkerJobKind.Analyze,
                    request));
        }

        private bool IsMeasurementActive
        {
            get
            {
                return activeWorkspace != null &&
                    !activeWorkspace.IsDisposed &&
                    activeWorkspace.Measurement != null &&
                    activeWorkspace.Measurement.IsActive;
            }
        }

        private bool IsTextEditSelectionActive
        {
            get
            {
                return activeWorkspace != null &&
                    !activeWorkspace.IsDisposed &&
                    activeWorkspace.TextEditSelection != null &&
                    activeWorkspace.TextEditSelection.IsActive;
            }
        }

        private bool IsPageStructureOperationInProgress
        {
            get
            {
                return pageInsertInProgress ||
                    pageOrganizationInProgress ||
                    ocrInProgress ||
                    bookmarkEditInProgress ||
                    contentEditInProgress ||
                    comparisonSurface != null ||
                    IsMeasurementActive ||
                    IsTextEditSelectionActive;
            }
        }

        private static long EstimateCombinedPdfBytes(
            string basePath,
            IEnumerable<string> insertedPaths)
        {
            long total = 0;
            var paths = new[] { basePath }
                .Concat(insertedPaths ?? Enumerable.Empty<string>());
            foreach (var path in paths)
            {
                try
                {
                    var length = new FileInfo(path).Length;
                    total = total > long.MaxValue - length
                        ? long.MaxValue
                        : total + length;
                }
                catch
                {
                }
            }

            return total;
        }

        private void StartPageInsertWorker(PdfPageInsertWorkerJob job)
        {
            currentPageInsertJob = job;
            try
            {
                pageInsertWorker.RunWorkerAsync(job);
            }
            catch (Exception ex)
            {
                CancelPageInsertRevision(job == null ? null : job.Request);
                FinishPageInsertOperation();
                ShowPageInsertError(ex);
            }
        }

        private void PageInsertWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            var job = e.Argument as PdfPageInsertWorkerJob;
            if (job == null)
            {
                throw new InvalidOperationException(
                    "No se pudo preparar la inserción de páginas.");
            }

            if (job.Kind == PdfPageInsertWorkerJobKind.Analyze)
            {
                e.Result = PdfPageInsertService.Analyze(
                    job.Request.BasePath,
                    job.Request.InsertedPaths,
                    job.Request.InsertionIndex);
                return;
            }

            e.Result = PdfPageInsertService.Insert(
                job.Request.BasePath,
                job.Request.InsertedPaths,
                job.Request.InsertionIndex,
                job.Request.OutputPath,
                delegate(PdfPageInsertProgress progress)
                {
                    var percentage = progress.TotalPages <= 0
                        ? 0
                        : (int)Math.Round(
                            progress.CompletedPages * 100d /
                            progress.TotalPages);
                    percentage = Math.Max(0, Math.Min(100, percentage));
                    pageInsertWorker.ReportProgress(percentage, progress);
                });
        }

        private void PageInsertWorker_ProgressChanged(
            object sender,
            ProgressChangedEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(
                    new ProgressChangedEventHandler(
                        PageInsertWorker_ProgressChanged),
                    sender,
                    e);
                return;
            }

            var request = pendingPageInsertRequest;
            var progress = e.UserState as PdfPageInsertProgress;
            if (request == null || progress == null)
            {
                return;
            }

            var sourceName = Path.GetFileName(progress.SourcePath);
            SetPageInsertStatus(
                request,
                string.Format(
                    "Insertando páginas… {0} de {1} · {2}",
                    progress.CompletedPages,
                    progress.TotalPages,
                    sourceName));
        }

        private void PageInsertWorker_RunWorkerCompleted(
            object sender,
            RunWorkerCompletedEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(
                    new RunWorkerCompletedEventHandler(
                        PageInsertWorker_RunWorkerCompleted),
                    sender,
                    e);
                return;
            }

            var job = currentPageInsertJob;
            currentPageInsertJob = null;

            if (e.Error != null)
            {
                CancelPageInsertRevision(job == null ? null : job.Request);
                FinishPageInsertOperation();
                ShowPageInsertError(e.Error);
                return;
            }

            if (e.Cancelled || job == null)
            {
                CancelPageInsertRevision(job == null ? null : job.Request);
                FinishPageInsertOperation();
                return;
            }

            if (job.Kind == PdfPageInsertWorkerJobKind.Analyze)
            {
                var analysis = e.Result as PdfPageInsertAnalysis;
                if (analysis == null)
                {
                    FinishPageInsertOperation();
                    ShowPageInsertError(
                        new InvalidDataException(
                            "No se pudo comprobar el contenido de los PDF."));
                    return;
                }

                job.Request.Analysis = analysis;
                if (analysis.DigitalSignaturesWillBeInvalidated)
                {
                    var answer = MessageBox.Show(
                        this,
                        "El PDF base o uno de los PDF que vas a añadir contiene " +
                        "firmas digitales.\r\n\r\n" +
                        "Los originales seguirán intactos, pero la copia editada " +
                        "no conservará su validez de firma y tendrás que firmarla " +
                        "de nuevo.\r\n\r\n¿Quieres continuar?",
                        "Insertar PDF firmado",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button2);
                    if (answer != DialogResult.Yes)
                    {
                        FinishPageInsertOperation();
                        return;
                    }
                }

                try
                {
                    if (job.Request.SourceWorkspace.EditSession == null)
                    {
                        throw new InvalidOperationException(
                            "La sesion de edicion ya no esta disponible.");
                    }

                    job.Request.OutputPath =
                        job.Request.SourceWorkspace.EditSession
                            .ReserveRevisionPath(
                                job.Request.EstimatedOutputBytes);
                }
                catch (Exception ex)
                {
                    FinishPageInsertOperation();
                    ShowPageInsertError(ex);
                    return;
                }

                SetPageInsertStatus(job.Request, "Insertando páginas…");
                StartPageInsertWorker(
                    new PdfPageInsertWorkerJob(
                        PdfPageInsertWorkerJobKind.Insert,
                        job.Request));
                return;
            }

            var result = e.Result as PdfPageInsertResult;
            if (result == null || !File.Exists(result.OutputPath))
            {
                CancelPageInsertRevision(job.Request);
                FinishPageInsertOperation();
                ShowPageInsertError(
                    new InvalidDataException(
                        "La copia editada no se pudo comprobar."));
                return;
            }

            CompletePageInsert(job.Request, result);
        }

        private void CompletePageInsert(
            PdfPageInsertRequest request,
            PdfPageInsertResult result)
        {
            PdfiumDocument preparedDocument = null;
            PdfEditSession.RevisionCommit revisionCommit = null;
            var activationCompleted = false;
            try
            {
                var resultWorkspace = request.SourceWorkspace;
                if (resultWorkspace == null ||
                    resultWorkspace.IsDisposed ||
                    resultWorkspace.EditSession == null ||
                    !string.Equals(
                        resultWorkspace.ContentPath,
                        request.BasePath,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(
                        resultWorkspace.EditSession.CurrentPath,
                        request.BasePath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "El documento cambió antes de activar las páginas insertadas.");
                }

                preparedDocument = PdfDocumentOpenService.Load(result.OutputPath);
                if (preparedDocument.PageCount != result.PageCount ||
                    preparedDocument.PageCount < 1)
                {
                    throw new InvalidDataException(
                        "La revisión editada no contiene las páginas esperadas.");
                }

                revisionCommit =
                    resultWorkspace.EditSession.BeginRevisionCommit(
                    result.OutputPath,
                    "Páginas insertadas");

                var documentToApply = preparedDocument;
                preparedDocument = null;
                if (!ApplyRevisionToWorkspace(
                        resultWorkspace,
                        result.OutputPath,
                        result.InsertionIndex,
                        documentToApply))
                {
                    CompensateFailedPageInsert(
                        request,
                        revisionCommit);
                    throw new InvalidOperationException(
                        "La copia se creó, pero no pudo activarse con seguridad.");
                }

                revisionCommit.Complete();
                revisionCommit = null;
                activationCompleted = true;
            }
            catch (Exception ex)
            {
                if (preparedDocument != null)
                {
                    preparedDocument.Dispose();
                }

                if (revisionCommit != null &&
                    !revisionCommit.IsFinished)
                {
                    try
                    {
                        CompensateFailedPageInsert(
                            request,
                            revisionCommit);
                    }
                    catch (Exception compensationError)
                    {
                        AppLog.Write(
                            "No se pudo revertir el commit de inserción: " +
                            compensationError);
                    }
                }

                var sourceWorkspace = request == null
                    ? null
                    : request.SourceWorkspace;
                var sourceEditSession = sourceWorkspace == null
                    ? null
                    : sourceWorkspace.EditSession;
                if (sourceWorkspace != null &&
                    sourceEditSession != null &&
                    !sourceWorkspace.EditHistoryFaulted &&
                    !string.Equals(
                        sourceEditSession.CurrentPath,
                        result.OutputPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    CancelPageInsertRevision(request);
                }

                AppLog.Write(
                    "La revision se creo pero no se pudo activar: " + ex);
                ShowPageInsertError(ex);
            }
            finally
            {
                if (activationCompleted)
                {
                    RefreshAfterPageInsert(request);
                }

                FinishPageInsertOperation();
            }
        }

        private void CompensateFailedPageInsert(
            PdfPageInsertRequest request,
            PdfEditSession.RevisionCommit revisionCommit)
        {
            if (request == null ||
                request.SourceWorkspace == null ||
                revisionCommit == null ||
                revisionCommit.IsFinished)
            {
                return;
            }

            if (request.SourceWorkspace.EditHistoryFaulted)
            {
                try
                {
                    revisionCommit.PreserveForRecovery();
                }
                catch
                {
                }

                return;
            }

            try
            {
                revisionCommit.Rollback();
                if (!string.Equals(
                        request.SourceWorkspace.EditSession.CurrentPath,
                        request.BasePath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "El historial no volvió a la revisión anterior.");
                }
            }
            catch
            {
                request.SourceWorkspace.EditHistoryFaulted = true;
                request.SourceWorkspace.FaultedChangesSaved = false;
                request.SourceWorkspace.DeleteRecoveryOnClose = false;
                try
                {
                    if (!revisionCommit.IsFinished)
                    {
                        revisionCommit.PreserveForRecovery();
                    }

                    request.SourceWorkspace.EditSession.PreserveRecovery();
                }
                catch
                {
                }

                throw;
            }
        }

        private void RefreshAfterPageInsert(PdfPageInsertRequest request)
        {
            if (request == null ||
                request.SourceWorkspace == null ||
                request.SourceWorkspace.IsDisposed)
            {
                return;
            }

            try
            {
                request.SourceWorkspace.EditSession.CleanupObsoleteRevisions(
                    request.SourceWorkspace.ContentPath);
            }
            catch (Exception ex)
            {
                AppLog.Write(
                    "Inserción aplicada; la limpieza se aplazó: " +
                    ex);
            }

            try
            {
                documentTabs.SelectedTab =
                    request.SourceWorkspace.TabPage;
                ActivateSelectedWorkspace();
                RefreshWorkspaceEditState(
                    request.SourceWorkspace);
            }
            catch (Exception ex)
            {
                AppLog.Write(
                    "Inserción aplicada; no se pudo refrescar la interfaz: " +
                    ex);
            }
        }

        private static void CancelPageInsertRevision(
            PdfPageInsertRequest request)
        {
            if (request == null ||
                request.SourceWorkspace == null ||
                request.SourceWorkspace.EditSession == null ||
                string.IsNullOrWhiteSpace(request.OutputPath))
            {
                return;
            }

            request.SourceWorkspace.EditSession.CancelReservedRevision(
                request.OutputPath);
        }

        private void SetPageInsertStatus(
            PdfPageInsertRequest request,
            string status)
        {
            if (request != null &&
                request.SourceWorkspace == activeWorkspace &&
                !request.SourceWorkspace.IsDisposed)
            {
                documentLabel.Text = status;
            }
        }

        private void FinishPageInsertOperation()
        {
            pageInsertInProgress = false;
            pendingPageInsertRequest = null;
            currentPageInsertJob = null;

            if (activeWorkspace != null && !activeWorkspace.IsDisposed)
            {
                RefreshWorkspaceEditState(activeWorkspace);
            }
            else if (documentTabs.TabPages.Count == 0)
            {
                documentEyebrowLabel.Text = "PDF LIGERO / VISOR";
                documentLabel.Text = "Ningún PDF abierto";
                documentLabel.Tag = null;
            }
        }

        private void ShowPageInsertError(Exception error)
        {
            ShowPdfProblem(
                "Insertar PDF",
                "No se pudieron insertar las páginas.",
                "Los PDF originales no se han modificado.",
                error,
                activeWorkspace == null ? null : activeWorkspace.ContentPath);
        }

        private void PdfDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = GetDroppedPdfPaths(e.Data).Count > 0
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        }

        private void PdfDragDrop(object sender, DragEventArgs e)
        {
            var paths = GetDroppedPdfPaths(e.Data);
            if (paths.Count > 0)
            {
                OpenPdfTabs(paths);
            }
        }

        private void PdfViewerForm_KeyDown(object sender, KeyEventArgs e)
        {
            var textControlFocused = ActiveControl is TextBoxBase;
            if (e.Control &&
                !e.Shift &&
                !e.Alt &&
                !textControlFocused &&
                e.KeyCode == Keys.E)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                BeginTextEditSelection();
                return;
            }

            if (e.Control &&
                e.Shift &&
                !e.Alt &&
                e.KeyCode == Keys.C)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                CompareToolButton_Click(sender, EventArgs.Empty);
                return;
            }

            if (e.Control &&
                e.Shift &&
                !e.Alt &&
                e.KeyCode == Keys.M)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                MeasureToolButton_Click(sender, EventArgs.Empty);
                return;
            }

            if (e.Control &&
                e.Shift &&
                !e.Alt &&
                e.KeyCode == Keys.E)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                InlineEditToolButton_Click(sender, EventArgs.Empty);
                return;
            }

            if (e.Control &&
                e.Shift &&
                !e.Alt &&
                e.KeyCode == Keys.A)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                AnnotateToolButton_Click(sender, EventArgs.Empty);
                return;
            }

            if (e.Control &&
                !e.Alt &&
                !textControlFocused &&
                e.KeyCode == Keys.Z &&
                !e.Shift)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                UndoActiveDocument();
                return;
            }

            if (e.Control &&
                !e.Alt &&
                !textControlFocused &&
                (e.KeyCode == Keys.Y ||
                 (e.KeyCode == Keys.Z && e.Shift)))
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                RedoActiveDocument();
                return;
            }

            if (e.Control && e.KeyCode == Keys.F)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                ShowSearchPanel();
                return;
            }

            if (e.Control &&
                e.Shift &&
                !e.Alt &&
                !textControlFocused &&
                e.KeyCode == Keys.B)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                EditActiveBookmarks();
                return;
            }

            if (e.KeyCode == Keys.Escape &&
                IsTextEditSelectionActive)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                DeactivateWorkspaceTextEditSelection(activeWorkspace);
                RefreshToolAvailability();
                if (activeWorkspace != null)
                {
                    activeWorkspace.Viewer.Focus();
                }
                return;
            }

            if (e.KeyCode == Keys.Escape &&
                comparisonSurface != null)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                ClosePlanComparison(true);
                return;
            }

            if (e.KeyCode == Keys.Escape &&
                IsMeasurementActive)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                DeactivateWorkspaceMeasurement(activeWorkspace);
                RefreshToolAvailability();
                if (activeWorkspace != null)
                {
                    activeWorkspace.Viewer.Focus();
                }
                return;
            }

            if (e.KeyCode == Keys.Escape && searchPanel.Visible)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                CloseSearchPanel();
                return;
            }

            if (e.KeyCode == Keys.Escape && ocrInProgress)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                CancelCurrentOcr();
                return;
            }

            if (e.Control && e.KeyCode == Keys.O)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                OpenButton_Click(sender, EventArgs.Empty);
                return;
            }

            if (e.Control && e.KeyCode == Keys.W && activeWorkspace != null)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                documentTabs.CloseActiveTab();
                return;
            }

            if (e.Control && e.KeyCode == Keys.P)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                PrintMenuItem_Click(sender, EventArgs.Empty);
                return;
            }

            if (e.Control && e.Shift && e.KeyCode == Keys.S &&
                signToolButton.Enabled)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                SignButton_Click(sender, EventArgs.Empty);
                return;
            }

            if (e.Control && e.KeyCode == Keys.S)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                SaveCopyMenuItem_Click(sender, EventArgs.Empty);
                return;
            }

            if (e.Control && (e.KeyCode == Keys.Add ||
                e.KeyCode == Keys.Oemplus))
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                ZoomActiveDocument(true);
                return;
            }

            if (e.Control && (e.KeyCode == Keys.Subtract ||
                e.KeyCode == Keys.OemMinus))
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                ZoomActiveDocument(false);
            }
        }

        private void PdfViewerForm_FormClosing(
            object sender,
            FormClosingEventArgs e)
        {
            if (comparisonSurface != null)
            {
                ClosePlanComparison(false);
            }

            foreach (var measurementWorkspace in workspaces)
            {
                DeactivateWorkspaceMeasurement(
                    measurementWorkspace);
                DeactivateWorkspaceTextEditSelection(
                    measurementWorkspace);
            }

            if (e.CloseReason == CloseReason.WindowsShutDown ||
                e.CloseReason == CloseReason.TaskManagerClosing)
            {
                closingAll = true;
                CancelCurrentOcr();
                foreach (var workspace in workspaces)
                {
                    workspace.DeleteRecoveryOnClose = false;
                    if (workspace.EditSession != null &&
                        workspace.EditSession.HasUnsavedChanges)
                    {
                        workspace.EditSession.PreserveRecovery();
                    }
                }

                return;
            }

            if (ocrInProgress)
            {
                CancelCurrentOcr();
                e.Cancel = true;
                MessageBox.Show(
                    this,
                    "Se está cancelando el OCR y limpiando sus archivos " +
                    "temporales. Podrás cerrar en cuanto termine la página actual.",
                    "OCR y enderezado",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (IsPageStructureOperationInProgress)
            {
                e.Cancel = true;
                MessageBox.Show(
                    this,
                    "Espera un momento: se está terminando una edición " +
                    "del PDF.",
                    "PDF Ligero",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            foreach (var workspace in workspaces)
            {
                workspace.DeleteRecoveryOnClose = false;
                ReleaseSavedCopyVerificationLease(workspace);
            }

            foreach (var workspace in workspaces.ToList())
            {
                if (!ConfirmCloseWorkspace(workspace))
                {
                    foreach (var pendingWorkspace in workspaces)
                    {
                        pendingWorkspace.DeleteRecoveryOnClose = false;
                        ReleaseSavedCopyVerificationLease(
                            pendingWorkspace);
                    }

                    e.Cancel = true;
                    return;
                }
            }

            // From here on the close cannot be cancelled. Hide the main window
            // before releasing the WinForms/PDF controls so closing several tabs
            // feels immediate, while file handles and recovery data are still
            // released deterministically on the UI thread.
            closingAll = true;
            pageSyncTimer.Stop();
            documentTabs.SuspendLayout();
            SuspendLayout();
            Hide();
        }

        private void PdfViewerForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            closingAll = true;
            if (comparisonSurface != null)
            {
                ClosePlanComparison(false);
            }
            pageSyncTimer.Stop();
            pageSyncTimer.Dispose();

            var closingWorkspaces = workspaces.ToList();
            foreach (var workspace in closingWorkspaces)
            {
                try
                {
                    PrepareWorkspaceRelease(workspace);
                }
                catch (Exception ex)
                {
                    workspace.IsDisposed = true;
                    workspaceByPath.Remove(workspace.Path);
                    workspaces.Remove(workspace);
                    AppLog.Write(
                        "No se pudo preparar una pestaña al cerrar: " + ex);
                }
            }

            try
            {
                // Disposing the parent destroys all viewer HWNDs in one native
                // pass. Doing the same work one TabPage at a time is markedly
                // slower when several documents are open.
                try
                {
                    documentTabs.Dispose();
                }
                catch (Exception ex)
                {
                    AppLog.Write(
                        "No se pudo liberar por completo el contenedor de " +
                        "pestañas: " + ex);
                }
            }
            finally
            {
                // Keep Pdfium documents alive until every renderer is gone, then
                // release files and recovery state even if WinForms disposal
                // were ever to fail part way through.
                foreach (var workspace in closingWorkspaces)
                {
                    CompleteWorkspaceRelease(workspace);
                }
            }

            workspaces.Clear();
            workspaceByPath.Clear();
            contentEditMenu.Dispose();
            moreMenu.Dispose();
            toolTip.Dispose();
            if (!pageInsertWorker.IsBusy)
            {
                pageInsertWorker.Dispose();
            }
            if (!pageOrganizerWorker.IsBusy)
            {
                pageOrganizerWorker.Dispose();
            }
            if (!ocrWorker.IsBusy)
            {
                ocrWorker.Dispose();
            }
        }

        private void RefreshEmptyState()
        {
            var hasTabs = documentTabs.TabPages.Count > 0;
            documentTabs.Visible = hasTabs;
            emptyPanel.Visible = !hasTabs;

            if (hasTabs)
            {
                documentTabs.BringToFront();
            }
            else
            {
                emptyPanel.BringToFront();
            }
        }

        private void RefreshToolAvailability()
        {
            var hasLoadedDocument = activeWorkspace != null &&
                activeWorkspace.IsLoaded &&
                !activeWorkspace.IsDisposed;
            var comparisonActive = comparisonSurface != null;
            var measurementActive = IsMeasurementActive;
            var textEditSelectionActive = IsTextEditSelectionActive;

            // Un PDF abierto con contraseña es de solo lectura: los servicios de
            // edición abren su propio PdfReader y no conocen esa contraseña, así
            // que fallarían uno a uno. Vale más apagarlos y explicarlo.
            var protectedDocument = hasLoadedDocument &&
                activeWorkspace.IsPasswordProtected;
            var canEditDocument = hasLoadedDocument &&
                !IsPageStructureOperationInProgress &&
                !activeWorkspace.EditHistoryFaulted &&
                !protectedDocument;

            searchToolButton.Enabled =
                hasLoadedDocument &&
                !comparisonActive &&
                !measurementActive &&
                !textEditSelectionActive &&
                !contentEditInProgress;
            contentEditToolButton.Enabled =
                textEditSelectionActive || canEditDocument;
            contentEditToolButton.Text =
                textEditSelectionActive ? "\u00D7" : "T";
            contentEditToolButton.AccessibleName =
                textEditSelectionActive
                    ? "Cancelar selección de texto"
                    : "Texto y formularios";
            contentEditToolButton.BackColor = textEditSelectionActive
                ? AccentTintColor
                : HeaderBackgroundColor;
            contentEditToolButton.ForeColor = textEditSelectionActive
                ? AccentTextColor
                : (contentEditToolButton.Enabled
                    ? TitleColor
                    : MutedColor);
            toolTip.SetToolTip(
                contentEditToolButton,
                textEditSelectionActive
                    ? "Cancelar selección de texto (Esc)"
                    : "Texto y formularios (Ctrl+E)");
            if (ocrInProgress)
            {
                ocrToolButton.Enabled = true;
                ocrToolButton.Text = "\u25A0";
                ocrToolButton.AccessibleName = "Cancelar OCR en curso";
                ocrToolButton.BackColor = AccentTintColor;
                ocrToolButton.ForeColor = AccentTextColor;
                toolTip.SetToolTip(
                    ocrToolButton,
                    "Cancelar el OCR en curso (Esc)");
            }
            else
            {
                ocrToolButton.Enabled = canEditDocument;
                ocrToolButton.Text = "OCR";
                ocrToolButton.AccessibleName =
                    "OCR, orientación y enderezado";
                ocrToolButton.BackColor = HeaderBackgroundColor;
                ocrToolButton.ForeColor = ocrToolButton.Enabled
                    ? TitleColor
                    : MutedColor;
                toolTip.SetToolTip(
                    ocrToolButton,
                    "OCR, orientación y enderezado");
            }
            signToolButton.Enabled = !IsPageStructureOperationInProgress &&
                activeWorkspace != null &&
                !activeWorkspace.IsPasswordProtected &&
                File.Exists(activeWorkspace.ContentPath);
            mergeToolButton.Enabled = !IsPageStructureOperationInProgress;
            // La comparación reabre ContentPath en un hilo de fondo con su propia
            // sesión de PDFium, que tampoco conoce la contraseña.
            compareToolButton.Enabled =
                comparisonActive ||
                (hasLoadedDocument &&
                 !protectedDocument &&
                 !measurementActive &&
                 !pageInsertInProgress &&
                 !pageOrganizationInProgress &&
                 !ocrInProgress &&
                 !bookmarkEditInProgress &&
                 !contentEditInProgress &&
                 !textEditSelectionActive);
            compareToolButton.Text =
                comparisonActive ? "\u00D7" : "\u0394";
            compareToolButton.AccessibleName = comparisonActive
                ? "Cerrar comparación de revisiones"
                : "Comparar revisiones";
            compareToolButton.BackColor = comparisonActive
                ? AccentTintColor
                : HeaderBackgroundColor;
            compareToolButton.ForeColor = comparisonActive
                ? AccentTextColor
                : (compareToolButton.Enabled
                    ? TitleColor
                    : MutedColor);
            toolTip.SetToolTip(
                compareToolButton,
                comparisonActive
                    ? "Cerrar comparación (Esc)"
                    : "Comparar revisiones (Ctrl+Mayús+C)");

            measureToolButton.Enabled =
                measurementActive ||
                (hasLoadedDocument &&
                 !comparisonActive &&
                 !pageInsertInProgress &&
                  !pageOrganizationInProgress &&
                  !ocrInProgress &&
                  !bookmarkEditInProgress &&
                  !contentEditInProgress &&
                  !textEditSelectionActive &&
                  !activatingWorkspace &&
                 !closingAll);
            measureToolButton.Text =
                measurementActive ? "\u00D7" : "\u2194";
            measureToolButton.AccessibleName = measurementActive
                ? "Cerrar medición de planos"
                : "Medir distancias, perímetros y áreas";
            measureToolButton.BackColor = measurementActive
                ? AccentTintColor
                : HeaderBackgroundColor;
            measureToolButton.ForeColor = measurementActive
                ? AccentTextColor
                : (measureToolButton.Enabled
                    ? TitleColor
                    : MutedColor);
            toolTip.SetToolTip(
                measureToolButton,
                measurementActive
                    ? "Cerrar medición (Esc)"
                    : "Medir plano (Ctrl+Mayús+M)");

            var inlineEditActive = activeWorkspace != null &&
                activeWorkspace.InlineEdit != null &&
                activeWorkspace.InlineEdit.IsActive;
            inlineEditToolButton.Enabled =
                inlineEditActive ||
                (hasLoadedDocument &&
                 !protectedDocument &&
                 !comparisonActive &&
                 !measurementActive &&
                 !pageInsertInProgress &&
                 !pageOrganizationInProgress &&
                 !ocrInProgress &&
                 !bookmarkEditInProgress &&
                 !contentEditInProgress &&
                 !textEditSelectionActive &&
                 !activatingWorkspace &&
                 !closingAll);
            inlineEditToolButton.Text =
                inlineEditActive ? "\u00D7" : "\uE932";
            inlineEditToolButton.AccessibleName = inlineEditActive
                ? "Cerrar la edición de texto"
                : "Editar el texto de la página";
            inlineEditToolButton.BackColor = inlineEditActive
                ? AccentTintColor
                : HeaderBackgroundColor;
            inlineEditToolButton.ForeColor = inlineEditActive
                ? AccentTextColor
                : (inlineEditToolButton.Enabled ? TitleColor : MutedColor);
            toolTip.SetToolTip(
                inlineEditToolButton,
                inlineEditActive
                    ? "Cerrar la edición de texto (Esc)"
                    : "Editar el texto de la página (Ctrl+Mayús+E)");

            var annotationActive = activeWorkspace != null &&
                activeWorkspace.Annotation != null &&
                activeWorkspace.Annotation.IsActive;
            var annotationPending = annotationActive &&
                activeWorkspace.Annotation.HasPending;
            annotateToolButton.Enabled =
                annotationActive ||
                (hasLoadedDocument &&
                 !protectedDocument &&
                 !comparisonActive &&
                 !measurementActive &&
                 !pageInsertInProgress &&
                 !pageOrganizationInProgress &&
                 !ocrInProgress &&
                 !bookmarkEditInProgress &&
                 !contentEditInProgress &&
                 !textEditSelectionActive &&
                 !activatingWorkspace &&
                 !closingAll);
            annotateToolButton.Text =
                annotationActive ? "\u00D7" : "\uE891";
            annotateToolButton.AccessibleName = annotationActive
                ? "Cerrar la herramienta de anotación"
                : "Anotar: rotulador, subrayador y notas";
            annotateToolButton.BackColor = annotationActive
                ? AccentTintColor
                : HeaderBackgroundColor;
            annotateToolButton.ForeColor = annotationActive
                ? AccentTextColor
                : (annotateToolButton.Enabled
                    ? TitleColor
                    : MutedColor);
            toolTip.SetToolTip(
                annotateToolButton,
                annotationActive
                    ? (annotationPending
                        ? "Cerrar anotación (hay marcas sin guardar)"
                        : "Cerrar anotación")
                    : "Anotar: rotulador, subrayador y notas (Ctrl+Mayús+A)");
            if (annotateMenuItem != null)
            {
                annotateMenuItem.Enabled = annotateToolButton.Enabled;
            }

            undoMenuItem.Enabled = hasLoadedDocument &&
                !IsPageStructureOperationInProgress &&
                !activeWorkspace.EditHistoryFaulted &&
                activeWorkspace.EditSession != null &&
                activeWorkspace.EditSession.CanUndo;
            redoMenuItem.Enabled = hasLoadedDocument &&
                !IsPageStructureOperationInProgress &&
                !activeWorkspace.EditHistoryFaulted &&
                activeWorkspace.EditSession != null &&
                activeWorkspace.EditSession.CanRedo;
            saveCopyMenuItem.Enabled =
                hasLoadedDocument &&
                !IsPageStructureOperationInProgress;
            printMenuItem.Enabled =
                hasLoadedDocument &&
                !IsPageStructureOperationInProgress;
            fitWidthMenuItem.Enabled =
                hasLoadedDocument && !comparisonActive;
            zoomInMenuItem.Enabled =
                hasLoadedDocument && !comparisonActive;
            zoomOutMenuItem.Enabled =
                hasLoadedDocument && !comparisonActive;
            rotateLeftMenuItem.Enabled =
                hasLoadedDocument && !comparisonActive;
            rotateRightMenuItem.Enabled =
                hasLoadedDocument && !comparisonActive;
            ocrMenuItem.Text = ocrInProgress
                ? "Cancelar OCR…"
                : "OCR y enderezado…";
            ocrMenuItem.Enabled = ocrInProgress || canEditDocument;
            organizePagesMenuItem.Enabled = canEditDocument;
            editBookmarksMenuItem.Enabled = canEditDocument;
            compareMenuItem.Text = comparisonActive
                ? "Cerrar comparación              Esc"
                : "Comparar revisiones…      Ctrl+Mayús+C";
            compareMenuItem.Enabled =
                comparisonActive || compareToolButton.Enabled;
            measureMenuItem.Text = measurementActive
                ? "Cerrar medición                    Esc"
                : "Medir plano…                  Ctrl+Mayús+M";
            measureMenuItem.Enabled =
                measurementActive || measureToolButton.Enabled;
            editTextMenuItem.Enabled = canEditDocument;
            fillFormMenuItem.Enabled = editTextMenuItem.Enabled;
            moreEditTextMenuItem.Enabled = editTextMenuItem.Enabled;
            moreFillFormMenuItem.Enabled = fillFormMenuItem.Enabled;

            // WinForms no muestra el tooltip de un control deshabilitado, así que
            // la explicación va en el contenedor, que sí recibe el ratón, y en los
            // elementos de menú, que lo gestiona su ToolStrip.
            var protectedHint = protectedDocument
                ? "PDF protegido con contraseña: solo lectura.\r\n" +
                  "Se puede ver, buscar, imprimir y guardar una copia."
                : null;
            toolTip.SetToolTip(toolRail, protectedHint);
            ocrMenuItem.ToolTipText = protectedHint;
            organizePagesMenuItem.ToolTipText = protectedHint;
            editBookmarksMenuItem.ToolTipText = protectedHint;
            editTextMenuItem.ToolTipText = protectedHint;
            fillFormMenuItem.ToolTipText = protectedHint;
            compareMenuItem.ToolTipText = protectedHint;

            if (comparisonActive)
            {
                previousPageButton.Enabled = false;
                currentPageTextBox.Enabled = false;
                nextPageButton.Enabled = false;
            }

            foreach (var workspace in workspaces)
            {
                if (!workspace.IsDisposed &&
                    workspace.Thumbnails != null)
                {
                    workspace.Thumbnails.PageOperationsEnabled =
                        !IsPageStructureOperationInProgress &&
                        !workspace.EditHistoryFaulted &&
                        !workspace.IsPasswordProtected;
                }

                if (!workspace.IsDisposed &&
                    workspace.EditBookmarksButton != null)
                {
                    workspace.EditBookmarksButton.Enabled =
                        workspace.IsLoaded &&
                        !IsPageStructureOperationInProgress &&
                        !workspace.EditHistoryFaulted &&
                        !workspace.IsPasswordProtected;
                }
            }
        }

        private void RefreshMenuAvailability()
        {
            RefreshToolAvailability();
        }

        private PdfWorkspace GetLoadedActiveWorkspace()
        {
            return activeWorkspace != null &&
                activeWorkspace.IsLoaded &&
                !activeWorkspace.IsDisposed
                ? activeWorkspace
                : null;
        }

        private bool CanUseRectangleZoom(PdfWorkspace workspace)
        {
            return workspace != null &&
                workspace == activeWorkspace &&
                workspace.IsLoaded &&
                !workspace.IsDisposed &&
                workspace.Document != null &&
                !searchPanel.Visible &&
                !IsTextEditSelectionActive &&
                !IsPageStructureOperationInProgress &&
                // Las herramientas que usan el arrastre para lo suyo tienen
                // preferencia: sin esto, el gesto de ampliar por rectangulo se
                // quedaba con el trazo del rotulador o con el subrayado.
                (workspace.Measurement == null ||
                    !workspace.Measurement.IsActive) &&
                (workspace.Annotation == null ||
                    !workspace.Annotation.IsActive) &&
                (workspace.InlineEdit == null ||
                    !workspace.InlineEdit.IsActive) &&
                !activatingWorkspace &&
                !closingAll;
        }

        private static void CancelRectangleZoom(PdfWorkspace workspace)
        {
            if (workspace != null && workspace.RectangleZoom != null)
            {
                workspace.RectangleZoom.Cancel();
            }
        }

        private void HeaderPanel_Resize(object sender, EventArgs e)
        {
            LayoutHeaderControls();
        }

        private void SearchPanel_Resize(object sender, EventArgs e)
        {
            LayoutSearchControls();
        }

        private void EmptyPanel_Resize(object sender, EventArgs e)
        {
            LayoutEmptyMessage();
        }

        private void LayoutHeaderControls()
        {
            const int groupWidth = 169;
            paperEyebrowLabel.Left =
                headerPanel.ClientSize.Width -
                paperEyebrowLabel.Width - 16;
            paperSizeLabel.Left =
                headerPanel.ClientSize.Width -
                paperSizeLabel.Width - 16;

            var desiredGroupLeft = Math.Max(
                340,
                (headerPanel.ClientSize.Width - groupWidth) / 2);
            var groupLeft = Math.Min(
                desiredGroupLeft,
                Math.Max(
                    340,
                    paperSizeLabel.Left - groupWidth - 22));

            previousPageButton.Left = groupLeft;
            previousPageButton.Top = 10;
            currentPageTextBox.Left = previousPageButton.Right + 5;
            currentPageTextBox.Top = 12;
            pageTotalLabel.Left = currentPageTextBox.Right + 5;
            pageTotalLabel.Top = 10;
            nextPageButton.Left = pageTotalLabel.Right + 3;
            nextPageButton.Top = 10;

            documentLabel.Width = Math.Max(
                180,
                previousPageButton.Left - documentLabel.Left - 22);
            documentEyebrowLabel.Width = documentLabel.Width;
        }

        private void LayoutSearchControls()
        {
            searchCloseButton.Left =
                searchPanel.ClientSize.Width - searchCloseButton.Width - 16;
            searchNextButton.Left =
                searchCloseButton.Left - searchNextButton.Width - 7;
            searchPreviousButton.Left =
                searchNextButton.Left - searchPreviousButton.Width - 7;
            searchStatusLabel.Left =
                searchPreviousButton.Left - searchStatusLabel.Width - 12;
            searchTextBox.Width = Math.Max(
                180,
                searchStatusLabel.Left - searchTextBox.Left - 12);
        }

        private void LayoutEmptyMessage()
        {
            var groupTop = Math.Max(
                44,
                (emptyPanel.ClientSize.Height - 172) / 2);

            emptyEyebrowLabel.Left = Math.Max(
                0,
                (emptyPanel.ClientSize.Width - emptyEyebrowLabel.Width) / 2);
            emptyEyebrowLabel.Top = groupTop;
            emptyTitleLabel.Left = Math.Max(
                0,
                (emptyPanel.ClientSize.Width - emptyTitleLabel.Width) / 2);
            emptyTitleLabel.Top = emptyEyebrowLabel.Bottom + 1;
            emptyAccentLine.Left = Math.Max(
                0,
                (emptyPanel.ClientSize.Width - emptyAccentLine.Width) / 2);
            emptyAccentLine.Top = emptyTitleLabel.Bottom + 5;
            emptyBodyLabel.Left = Math.Max(
                0,
                (emptyPanel.ClientSize.Width - emptyBodyLabel.Width) / 2);
            emptyBodyLabel.Top = emptyAccentLine.Bottom + 12;
            emptyOpenButton.Left = Math.Max(
                0,
                (emptyPanel.ClientSize.Width - emptyOpenButton.Width) / 2);
            emptyOpenButton.Top = emptyBodyLabel.Bottom + 12;

            emptyIndexLabel.Visible =
                emptyPanel.ClientSize.Width >= 720 &&
                emptyPanel.ClientSize.Height >= 450;
            emptyIndexLabel.Left = Math.Max(
                0,
                emptyPanel.ClientSize.Width - emptyIndexLabel.Width - 38);
            emptyIndexLabel.Top = Math.Max(
                0,
                emptyPanel.ClientSize.Height - emptyIndexLabel.Height - 24);
        }

        private Button CreateToolButton(
            string glyph,
            string accessibleName,
            EventHandler clickHandler)
        {
            var button = new Button
            {
                Width = 38,
                Height = 38,
                Margin = new Padding(0, 0, 0, 3),
                Text = glyph,
                AccessibleName = accessibleName,
                FlatStyle = FlatStyle.Flat,
                BackColor = HeaderBackgroundColor,
                ForeColor = TitleColor,
                Cursor = Cursors.Hand,
                Font = new Font(
                    "Segoe MDL2 Assets",
                    13.5f,
                    FontStyle.Regular,
                    GraphicsUnit.Point)
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = AccentTintColor;
            button.FlatAppearance.MouseDownBackColor = DividerColor;
            button.MouseEnter += delegate
            {
                if (button.Enabled)
                {
                    button.ForeColor = AccentTextColor;
                }
            };
            button.MouseLeave += delegate
            {
                button.ForeColor = button.Enabled ? TitleColor : MutedColor;
            };
            button.EnabledChanged += delegate
            {
                button.ForeColor = button.Enabled ? TitleColor : MutedColor;
            };
            button.Click += clickHandler;
            toolTip.SetToolTip(button, accessibleName);
            return button;
        }

        private Button CreateNavigationButton(
            string glyph,
            string accessibleName)
        {
            var button = new Button
            {
                Width = 30,
                Height = 30,
                Text = glyph,
                AccessibleName = accessibleName,
                FlatStyle = FlatStyle.Flat,
                BackColor = NavigationBackgroundColor,
                ForeColor = BodyColor,
                Cursor = Cursors.Hand,
                Font = new Font(
                    "Segoe MDL2 Assets",
                    11.5f,
                    FontStyle.Regular,
                    GraphicsUnit.Point)
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = AccentTintColor;
            button.FlatAppearance.MouseDownBackColor = DividerColor;
            toolTip.SetToolTip(button, accessibleName);
            return button;
        }

        private static ToolStripMenuItem AddMenuItem(
            ContextMenuStrip menu,
            string text,
            EventHandler clickHandler)
        {
            var item = new ToolStripMenuItem(text)
            {
                Padding = new Padding(10, 4, 10, 4)
            };
            item.Click += clickHandler;
            menu.Items.Add(item);
            return item;
        }

        private static ToolStripMenuItem CreateDisabledMenuItem(string text)
        {
            return new ToolStripMenuItem(text)
            {
                Enabled = false,
                Padding = new Padding(10, 4, 10, 4)
            };
        }

        private static void StyleButton(Button button, bool primary)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.BorderColor = DividerColor;
            button.BackColor = primary ? PrimaryColor : HeaderBackgroundColor;
            button.ForeColor = primary ? Color.White : TitleColor;
            button.Cursor = Cursors.Hand;
            button.Font = CreateArchitecturalFont(9.25f, true);
            button.FlatAppearance.MouseOverBackColor =
                primary ? PrimaryHoverColor : SecondaryHoverColor;
            button.FlatAppearance.MouseDownBackColor =
                primary ? PrimaryHoverColor : SecondaryHoverColor;
        }

        private static void StyleSearchButton(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = DividerColor;
            button.BackColor = HeaderBackgroundColor;
            button.ForeColor = TitleColor;
            button.Cursor = Cursors.Hand;
            button.Font = CreateUiFont(8.75f, FontStyle.Regular);
            button.FlatAppearance.MouseOverBackColor = AccentTintColor;
            button.FlatAppearance.MouseDownBackColor = SecondaryHoverColor;
        }

        private static void StylePageButton(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = HeaderBackgroundColor;
            button.ForeColor = AccentTextColor;
            button.Cursor = Cursors.Hand;
            button.FlatAppearance.MouseOverBackColor = AccentTintColor;
            button.FlatAppearance.MouseDownBackColor = SecondaryHoverColor;
        }

        private static Font CreateUiFont(float size, FontStyle style)
        {
            Font font;
            if (TryCreateExactFont(
                    "Segoe UI Variable Text",
                    size,
                    style,
                    out font) ||
                TryCreateExactFont(
                    "Segoe UI",
                    size,
                    style,
                    out font))
            {
                return font;
            }

            try
            {
                return new Font(
                    SystemFonts.MessageBoxFont.FontFamily,
                    size,
                    style,
                    GraphicsUnit.Point);
            }
            catch
            {
                return (Font)SystemFonts.MessageBoxFont.Clone();
            }
        }

        private static Font CreateArchitecturalFont(
            float size,
            bool emphasis)
        {
            var candidates = emphasis
                ? new[]
                {
                    "Bahnschrift SemiCondensed",
                    "Bahnschrift SemiLight",
                    "Segoe UI Semibold",
                    "Segoe UI"
                }
                : new[]
                {
                    "Bahnschrift Light SemiCondensed",
                    "Bahnschrift Light Condensed",
                    "Bahnschrift Light",
                    "Segoe UI Semilight",
                    "Segoe UI"
                };

            Font font;
            foreach (var candidate in candidates)
            {
                if (TryCreateExactFont(
                        candidate,
                        size,
                        FontStyle.Regular,
                        out font))
                {
                    return font;
                }
            }

            return CreateUiFont(size, FontStyle.Regular);
        }

        private static bool TryCreateExactFont(
            string familyName,
            float size,
            FontStyle style,
            out Font font)
        {
            font = null;
            try
            {
                var candidate = new Font(
                    familyName,
                    size,
                    style,
                    GraphicsUnit.Point);
                if (string.Equals(
                        candidate.Name,
                        familyName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    font = candidate;
                    return true;
                }

                candidate.Dispose();
            }
            catch
            {
            }

            return false;
        }

        private void ShowMaximumTabsMessage()
        {
            MessageBox.Show(
                this,
                "Puedes tener hasta " + MaximumOpenTabs +
                " PDF abiertos a la vez.",
                "PDF Ligero",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private static string NormalizePdfPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            path = path.Trim().Trim('"');
            if (!string.Equals(
                Path.GetExtension(path),
                ".pdf",
                StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFullPath(path);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static List<string> GetDroppedPdfPaths(IDataObject data)
        {
            if (data == null || !data.GetDataPresent(DataFormats.FileDrop))
            {
                return new List<string>();
            }

            var rawPaths = data.GetData(DataFormats.FileDrop) as string[];
            if (rawPaths == null)
            {
                return new List<string>();
            }

            return rawPaths
                .Select(NormalizePdfPath)
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private sealed class ArchitecturalMenuColorTable : ProfessionalColorTable
        {
            public ArchitecturalMenuColorTable()
            {
                UseSystemColors = false;
            }

            public override Color ToolStripDropDownBackground
            {
                get { return HeaderBackgroundColor; }
            }

            public override Color MenuBorder
            {
                get { return DividerColor; }
            }

            public override Color MenuItemBorder
            {
                get { return AccentColor; }
            }

            public override Color MenuItemSelected
            {
                get { return AccentTintColor; }
            }

            public override Color MenuItemSelectedGradientBegin
            {
                get { return AccentTintColor; }
            }

            public override Color MenuItemSelectedGradientEnd
            {
                get { return AccentTintColor; }
            }

            public override Color MenuItemPressedGradientBegin
            {
                get { return SecondaryHoverColor; }
            }

            public override Color MenuItemPressedGradientEnd
            {
                get { return SecondaryHoverColor; }
            }

            public override Color ImageMarginGradientBegin
            {
                get { return HeaderBackgroundColor; }
            }

            public override Color ImageMarginGradientMiddle
            {
                get { return HeaderBackgroundColor; }
            }

            public override Color ImageMarginGradientEnd
            {
                get { return HeaderBackgroundColor; }
            }

            public override Color SeparatorDark
            {
                get { return DividerColor; }
            }

            public override Color SeparatorLight
            {
                get { return HeaderBackgroundColor; }
            }
        }

        private enum PdfPageInsertWorkerJobKind
        {
            Analyze,
            Insert
        }

        private sealed class PdfPageInsertRequest
        {
            public PdfPageInsertRequest(
                PdfWorkspace sourceWorkspace,
                string basePath,
                IList<string> insertedPaths,
                int insertionIndex,
                long estimatedOutputBytes)
            {
                SourceWorkspace = sourceWorkspace;
                BasePath = basePath;
                InsertedPaths = new List<string>(insertedPaths);
                InsertionIndex = insertionIndex;
                EstimatedOutputBytes = estimatedOutputBytes;
            }

            public PdfWorkspace SourceWorkspace { get; private set; }

            public string BasePath { get; private set; }

            public IList<string> InsertedPaths { get; private set; }

            public int InsertionIndex { get; private set; }

            public long EstimatedOutputBytes { get; private set; }

            public string OutputPath { get; set; }

            public PdfPageInsertAnalysis Analysis { get; set; }
        }

        private sealed class PdfPageInsertWorkerJob
        {
            public PdfPageInsertWorkerJob(
                PdfPageInsertWorkerJobKind kind,
                PdfPageInsertRequest request)
            {
                Kind = kind;
                Request = request;
            }

            public PdfPageInsertWorkerJobKind Kind { get; private set; }

            public PdfPageInsertRequest Request { get; private set; }
        }

        private enum PdfPageOrganizationWorkerJobKind
        {
            Analyze,
            Organize
        }

        private sealed class PdfPageOrganizationUiRequest
        {
            public PdfPageOrganizationUiRequest(
                PdfWorkspace sourceWorkspace,
                PdfEditSession sourceEditSession,
                string basePath,
                IList<PdfPageOrganizerPage> pages,
                int preferredPageIndex,
                IEnumerable<int> resultSelectionIndexes,
                string description,
                string statusText,
                long estimatedOutputBytes,
                bool resetVisualRotation)
            {
                SourceWorkspace = sourceWorkspace;
                SourceEditSession = sourceEditSession;
                BasePath = Path.GetFullPath(basePath);
                Pages = new List<PdfPageOrganizerPage>(pages);
                PreferredPageIndex = preferredPageIndex;
                ResultSelectionIndexes =
                    new List<int>(
                        resultSelectionIndexes ??
                        Enumerable.Empty<int>());
                Description = description;
                StatusText = statusText;
                EstimatedOutputBytes = Math.Max(
                    0,
                    estimatedOutputBytes);
                ResetVisualRotation = resetVisualRotation;
            }

            public PdfWorkspace SourceWorkspace { get; private set; }

            public PdfEditSession SourceEditSession { get; private set; }

            public string BasePath { get; private set; }

            public IList<PdfPageOrganizerPage> Pages { get; private set; }

            public int PreferredPageIndex { get; private set; }

            public IList<int> ResultSelectionIndexes { get; private set; }

            public string Description { get; private set; }

            public string StatusText { get; private set; }

            public long EstimatedOutputBytes { get; private set; }

            public bool ResetVisualRotation { get; private set; }

            public string OutputPath { get; set; }

            public PdfPageOrganizerAnalysis Analysis { get; set; }
        }

        private sealed class PdfPageOrganizationWorkerJob
        {
            public PdfPageOrganizationWorkerJob(
                PdfPageOrganizationWorkerJobKind kind,
                PdfPageOrganizationUiRequest request)
            {
                Kind = kind;
                Request = request;
            }

            public PdfPageOrganizationWorkerJobKind Kind
            {
                get;
                private set;
            }

            public PdfPageOrganizationUiRequest Request
            {
                get;
                private set;
            }
        }

        private enum PdfOcrWorkerJobKind
        {
            Analyze,
            Process
        }

        private sealed class PdfOcrUiRequest
        {
            public PdfOcrUiRequest(
                PdfWorkspace sourceWorkspace,
                PdfEditSession sourceEditSession,
                string basePath,
                PdfOcrSettings settings,
                int preferredPageIndex,
                IEnumerable<int> originalSelectionIndexes,
                long estimatedOutputBytes)
            {
                if (sourceWorkspace == null)
                {
                    throw new ArgumentNullException("sourceWorkspace");
                }

                if (sourceEditSession == null)
                {
                    throw new ArgumentNullException("sourceEditSession");
                }

                SourceWorkspace = sourceWorkspace;
                SourceEditSession = sourceEditSession;
                BasePath = Path.GetFullPath(basePath);
                Settings = (settings ?? new PdfOcrSettings()).Snapshot();
                PreferredPageIndex = Math.Max(0, preferredPageIndex);
                OriginalSelectionIndexes = new List<int>(
                    originalSelectionIndexes ??
                    Enumerable.Empty<int>());
                EstimatedOutputBytes = Math.Max(
                    0,
                    estimatedOutputBytes);
                Instructions = new List<PdfOcrPageInstruction>();
            }

            public PdfWorkspace SourceWorkspace { get; private set; }

            public PdfEditSession SourceEditSession { get; private set; }

            public string BasePath { get; private set; }

            public PdfOcrSettings Settings { get; private set; }

            public int PreferredPageIndex { get; set; }

            public IList<int> OriginalSelectionIndexes { get; set; }

            public long EstimatedOutputBytes { get; private set; }

            public PdfOcrAnalysis Analysis { get; set; }

            public IList<PdfOcrPageInstruction> Instructions
            {
                get;
                set;
            }

            public string OutputPath { get; set; }
        }

        private sealed class PdfOcrWorkerJob
        {
            public PdfOcrWorkerJob(
                PdfOcrWorkerJobKind kind,
                PdfOcrUiRequest request,
                CancellationToken cancellationToken)
            {
                Kind = kind;
                Request = request;
                CancellationToken = cancellationToken;
            }

            public PdfOcrWorkerJobKind Kind { get; private set; }

            public PdfOcrUiRequest Request { get; private set; }

            public CancellationToken CancellationToken
            {
                get;
                private set;
            }
        }

        private sealed class PdfWorkspace
        {
            public string Path;
            public string ContentPath;
            public string DisplayName;
            public string LastSavedPath;
            public long LastSavedLength = -1;
            public long LastSavedWriteUtcTicks = -1;
            public string LastSavedFingerprint = string.Empty;
            public string LastSavedFullHash = string.Empty;
            public FileStream LastSavedVerificationLease;
            public PdfEditSession EditSession;
            public TabPage TabPage;
            public PdfViewer Viewer;
            public PdfiumDocument Document;
            public PdfRectangleZoomController RectangleZoom;
            public PdfTextEditSelectionController TextEditSelection;
            public PdfMeasurementController Measurement;

            public PdfAnnotationController Annotation;

            public PdfInlineTextEditController InlineEdit;
            public Panel NavigationPanel;
            public Panel NavigationHeader;
            public Button PagesButton;
            public Button BookmarksButton;
            public Button EditBookmarksButton;
            public Button CollapseNavigationButton;
            public PdfThumbnailList Thumbnails;
            public TreeView BookmarksTree;
            public PdfBookmarkDocument BookmarkDocument;
            public ScrollEventHandler ScrollHandler;
            public EventHandler<PdfThumbnailPageSelectedEventArgs> ThumbnailSelectionHandler;
            public EventHandler<PdfFilesInsertRequestedEventArgs> PdfInsertHandler;
            public EventHandler<PdfThumbnailPagesReorderRequestedEventArgs>
                PageReorderHandler;
            public EventHandler<PdfThumbnailPageOperationRequestedEventArgs>
                PageOperationHandler;
            public TreeNodeMouseClickEventHandler BookmarkSelectionHandler;
            public PdfMatches SearchMatches;
            public Dictionary<int, IList<PdfRectangle>> SearchMatchBounds;
            public string SearchInput = string.Empty;
            public string LastSearchQuery = string.Empty;
            public int CurrentSearchIndex = -1;
            public int DisplayedPageIndex = -1;
            public int ExpandedNavigationWidth;
            public bool ShowingBookmarks;
            public bool NavigationCollapsed;
            public bool IsLoaded;
            public bool BookmarksLoaded;
            public bool LoadFailed;
            public bool IsDisposed;
            public bool DeleteRecoveryOnClose;
            public bool EditHistoryFaulted;
            public bool FaultedChangesSaved;

            /// <summary>
            /// El documento se abrió con una contraseña de apertura, de modo que
            /// la pestaña es de solo lectura: los servicios de edición abren su
            /// propio PdfReader y no conocen esa contraseña. Nunca se guarda la
            /// contraseña, solo el hecho de que hizo falta.
            /// </summary>
            public bool IsPasswordProtected;

            /// <summary>
            /// El usuario cerró el diálogo de contraseña sin escribirla. Permite
            /// volver a preguntar si abre el mismo archivo otra vez, pero no al
            /// cambiar de pestaña.
            /// </summary>
            public bool PasswordPromptCancelled;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace FirmaAutomatica
{
    /// <summary>
    /// Editor modal y transaccional de la jerarquía de marcadores.
    /// Trabaja siempre sobre una copia del documento recibida: Cancelar no
    /// modifica el modelo del llamador.
    /// </summary>
    internal sealed class PdfBookmarkEditorForm : Form
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

        private readonly PdfBookmarkDocument workingDocument;
        private readonly Func<PdfBookmarkDestination>
            visibleDestinationProvider;
        private readonly ToolTip toolTip;
        private readonly TreeView bookmarkTree;
        private readonly Label treeSummaryLabel;
        private readonly Label selectedTitleLabel;
        private readonly Label destinationStateLabel;
        private readonly NumericUpDown pageNumberInput;
        private readonly CheckBox exactPositionCheckBox;
        private readonly NumericUpDown positionInput;
        private readonly Label percentLabel;
        private readonly Button useVisibleViewButton;
        private readonly Button createButton;
        private readonly Button renameButton;
        private readonly Button deleteButton;
        private readonly Button moveUpButton;
        private readonly Button moveDownButton;
        private readonly Button outdentButton;
        private readonly Button indentButton;
        private readonly Button applyButton;
        private readonly Button cancelButton;
        private readonly Label footerStatusLabel;

        private bool updatingSelectionControls;
        private bool applying;
        private bool hasChanges;
        private PdfBookmarkDocument editedDocument;
        private string selectedNodeId;

        public PdfBookmarkEditorForm(
            PdfBookmarkDocument document)
            : this(
                document,
                (Func<PdfBookmarkDestination>)null)
        {
        }

        public PdfBookmarkEditorForm(
            PdfBookmarkDocument document,
            PdfBookmarkDestination visibleDestination)
            : this(
                document,
                visibleDestination == null
                    ? (Func<PdfBookmarkDestination>)null
                    : delegate
                    {
                        return visibleDestination;
                    })
        {
        }

        public PdfBookmarkEditorForm(
            PdfBookmarkDocument document,
            Func<PdfBookmarkDestination> visibleDestinationProvider)
        {
            if (document == null)
            {
                throw new ArgumentNullException("document");
            }
            if (document.PageCount < 1)
            {
                throw new ArgumentException(
                    "El documento no contiene páginas.",
                    "document");
            }

            workingDocument = PdfBookmarkService.CloneDocument(document);
            this.visibleDestinationProvider =
                visibleDestinationProvider;

            Text = "Editar marcadores - PDF Ligero";
            AppBranding.ApplyWindowIcon(this);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(900, 650);
            MinimumSize = new Size(760, 650);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = WorkspaceColor;
            Font = CreateUiFont(9.25f, FontStyle.Regular);
            KeyPreview = true;
            ShowInTaskbar = false;

            toolTip = new ToolTip
            {
                AutoPopDelay = 9000,
                InitialDelay = 350,
                ReshowDelay = 100,
                ShowAlways = true
            };

            var headerPanel = CreateHeaderPanel();
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

            cancelButton = CreateActionButton("Cancelar", false);
            cancelButton.Width = 100;
            cancelButton.Anchor =
                AnchorStyles.Top | AnchorStyles.Right;
            cancelButton.Top = 15;
            cancelButton.Left =
                footerPanel.ClientSize.Width - 222;
            cancelButton.DialogResult = DialogResult.Cancel;
            cancelButton.AccessibleName =
                "Cancelar la edición de marcadores";

            applyButton = CreateActionButton("Aplicar", true);
            applyButton.Width = 100;
            applyButton.Anchor =
                AnchorStyles.Top | AnchorStyles.Right;
            applyButton.Top = 15;
            applyButton.Left =
                footerPanel.ClientSize.Width - 112;
            applyButton.AccessibleName =
                "Aplicar los cambios de marcadores";
            applyButton.Click += ApplyButton_Click;

            footerStatusLabel = new Label
            {
                Left = 20,
                Top = 14,
                Width = 520,
                Height = 36,
                Anchor =
                    AnchorStyles.Top |
                    AnchorStyles.Left |
                    AnchorStyles.Right,
                ForeColor = document.ContainsDigitalSignatures
                    ? AccentTextColor
                    : MutedColor,
                BackColor = PaperColor,
                Font = CreateUiFont(8.25f, FontStyle.Regular),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Text = document.ContainsDigitalSignatures
                    ? "El PDF contiene firmas digitales. Cambiar sus " +
                      "marcadores puede afectar a su validez."
                    : "Los cambios solo se guardarán al pulsar Aplicar."
            };

            footerPanel.Controls.Add(footerStatusLabel);
            footerPanel.Controls.Add(cancelButton);
            footerPanel.Controls.Add(applyButton);

            var bodyLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(16, 14, 16, 14),
                Margin = Padding.Empty,
                BackColor = WorkspaceColor
            };
            bodyLayout.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 55F));
            bodyLayout.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 45F));
            bodyLayout.RowStyles.Add(
                new RowStyle(SizeType.Percent, 100F));

            var treeSection = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 6, 0),
                BackColor = PaperColor
            };
            treeSection.Controls.Add(new Panel
            {
                Dock = DockStyle.Left,
                Width = 2,
                BackColor = AccentColor
            });
            treeSection.Controls.Add(new Panel
            {
                Dock = DockStyle.Right,
                Width = 1,
                BackColor = DividerColor
            });
            treeSection.Controls.Add(new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = DividerColor
            });

            var treeHeaderPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 76,
                BackColor = PaperColor,
                Padding = new Padding(14, 0, 10, 0)
            };
            var treeCaptionLabel = new Label
            {
                Left = 14,
                Top = 8,
                Width = 175,
                Height = 16,
                Text = "JERARQUÍA",
                ForeColor = AccentTextColor,
                Font = CreateArchitecturalFont(7.25f, true),
                TextAlign = ContentAlignment.MiddleLeft
            };
            treeSummaryLabel = new Label
            {
                Left = 190,
                Top = 8,
                Width = 210,
                Height = 16,
                Anchor =
                    AnchorStyles.Top |
                    AnchorStyles.Left |
                    AnchorStyles.Right,
                ForeColor = MutedColor,
                Font = CreateUiFont(7.75f, FontStyle.Regular),
                TextAlign = ContentAlignment.MiddleRight,
                AutoEllipsis = true
            };

            var toolsPanel = new FlowLayoutPanel
            {
                Left = 14,
                Top = 31,
                Width = 386,
                Height = 34,
                Anchor =
                    AnchorStyles.Top |
                    AnchorStyles.Left |
                    AnchorStyles.Right,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = PaperColor
            };

            createButton = CreateToolButton("+", "Crear marcador");
            renameButton = CreateToolButton("F2", "Renombrar marcador");
            deleteButton = CreateToolButton("×", "Eliminar marcador");
            moveUpButton = CreateToolButton("↑", "Subir entre hermanos");
            moveDownButton = CreateToolButton("↓", "Bajar entre hermanos");
            outdentButton = CreateToolButton(
                "←",
                "Reducir nivel (Alt+Izquierda)");
            indentButton = CreateToolButton(
                "→",
                "Aumentar nivel (Alt+Derecha)");

            createButton.Click += delegate
            {
                CreateBookmark();
            };
            renameButton.Click += delegate
            {
                BeginRename();
            };
            deleteButton.Click += delegate
            {
                DeleteSelectedBookmark();
            };
            moveUpButton.Click += delegate
            {
                MoveSelectedBookmark(-1);
            };
            moveDownButton.Click += delegate
            {
                MoveSelectedBookmark(1);
            };
            outdentButton.Click += delegate
            {
                OutdentSelectedBookmark();
            };
            indentButton.Click += delegate
            {
                IndentSelectedBookmark();
            };

            toolsPanel.Controls.Add(createButton);
            toolsPanel.Controls.Add(renameButton);
            toolsPanel.Controls.Add(deleteButton);
            toolsPanel.Controls.Add(CreateToolSeparator());
            toolsPanel.Controls.Add(moveUpButton);
            toolsPanel.Controls.Add(moveDownButton);
            toolsPanel.Controls.Add(outdentButton);
            toolsPanel.Controls.Add(indentButton);

            treeHeaderPanel.Controls.Add(treeCaptionLabel);
            treeHeaderPanel.Controls.Add(treeSummaryLabel);
            treeHeaderPanel.Controls.Add(toolsPanel);

            bookmarkTree = new TreeView
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = PaperColor,
                ForeColor = TitleColor,
                Font = CreateUiFont(9.25f, FontStyle.Regular),
                FullRowSelect = true,
                HideSelection = false,
                LabelEdit = true,
                ShowLines = true,
                ShowNodeToolTips = true,
                ShowPlusMinus = true,
                ShowRootLines = false,
                ItemHeight = 25,
                Indent = 18,
                DrawMode = TreeViewDrawMode.OwnerDrawText,
                AccessibleName = "Jerarquía de marcadores",
                AccessibleDescription =
                    "Árbol editable. F2 renombra, Supr elimina y " +
                    "Alt con flechas mueve o cambia de nivel."
            };
            bookmarkTree.AfterSelect += BookmarkTree_AfterSelect;
            bookmarkTree.AfterLabelEdit += BookmarkTree_AfterLabelEdit;
            bookmarkTree.DrawNode += BookmarkTree_DrawNode;
            bookmarkTree.NodeMouseDoubleClick +=
                BookmarkTree_NodeMouseDoubleClick;
            bookmarkTree.KeyDown += BookmarkTree_KeyDown;

            var treeHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = PaperColor,
                Padding = new Padding(14, 3, 10, 12)
            };
            treeHost.Controls.Add(bookmarkTree);
            treeSection.Controls.Add(treeHost);
            treeSection.Controls.Add(treeHeaderPanel);

            var detailSection = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(6, 0, 0, 0),
                BackColor = NavigationColor
            };
            detailSection.Controls.Add(new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = DividerColor
            });

            var detailHeaderPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 82,
                BackColor = NavigationColor
            };
            var detailCaptionLabel = new Label
            {
                Left = 18,
                Top = 10,
                Width = 235,
                Height = 16,
                Text = "MARCADOR SELECCIONADO",
                ForeColor = AccentTextColor,
                Font = CreateArchitecturalFont(7.25f, true),
                TextAlign = ContentAlignment.MiddleLeft
            };
            selectedTitleLabel = new Label
            {
                Left = 18,
                Top = 31,
                Width = 300,
                Height = 31,
                Anchor =
                    AnchorStyles.Top |
                    AnchorStyles.Left |
                    AnchorStyles.Right,
                Text = "Ningún marcador seleccionado",
                ForeColor = TitleColor,
                Font = CreateArchitecturalFont(11.25f, false),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
            var detailAccentLine = new Panel
            {
                Left = 18,
                Top = 69,
                Width = 38,
                Height = 2,
                BackColor = AccentColor
            };
            detailHeaderPanel.Controls.Add(detailCaptionLabel);
            detailHeaderPanel.Controls.Add(selectedTitleLabel);
            detailHeaderPanel.Controls.Add(detailAccentLine);

            var destinationPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 220,
                BackColor = PaperColor
            };
            destinationPanel.Controls.Add(new Panel
            {
                Dock = DockStyle.Left,
                Width = 2,
                BackColor = AccentColor
            });
            destinationPanel.Controls.Add(new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = DividerColor
            });

            var destinationCaptionLabel = new Label
            {
                Left = 18,
                Top = 12,
                Width = 220,
                Height = 16,
                Text = "DESTINO",
                ForeColor = AccentTextColor,
                Font = CreateArchitecturalFont(7.25f, true),
                TextAlign = ContentAlignment.MiddleLeft
            };
            destinationStateLabel = new Label
            {
                Left = 18,
                Top = 31,
                Width = 300,
                Height = 36,
                Anchor =
                    AnchorStyles.Top |
                    AnchorStyles.Left |
                    AnchorStyles.Right,
                Text = "Selecciona un marcador para editar su destino.",
                ForeColor = BodyColor,
                BackColor = PaperColor,
                Font = CreateUiFont(8.4f, FontStyle.Regular),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };

            var pageLabel = new Label
            {
                Left = 18,
                Top = 77,
                Width = 92,
                Height = 25,
                Text = "Página",
                ForeColor = TitleColor,
                BackColor = PaperColor,
                Font = CreateUiFont(9f, FontStyle.Regular),
                TextAlign = ContentAlignment.MiddleLeft
            };
            pageNumberInput = new NumericUpDown
            {
                Left = 116,
                Top = 76,
                Width = 88,
                Height = 26,
                Minimum = 1,
                Maximum = workingDocument.PageCount,
                Value = 1,
                TextAlign = HorizontalAlignment.Right,
                ThousandsSeparator = false,
                Font = CreateUiFont(9f, FontStyle.Regular),
                AccessibleName = "Página de destino"
            };
            pageNumberInput.ValueChanged +=
                DestinationControl_ValueChanged;

            exactPositionCheckBox = new CheckBox
            {
                Left = 18,
                Top = 112,
                Width = 188,
                Height = 26,
                Text = "Posición vertical exacta",
                ForeColor = TitleColor,
                BackColor = PaperColor,
                Font = CreateUiFont(8.8f, FontStyle.Regular),
                UseVisualStyleBackColor = false,
                AccessibleDescription =
                    "Desactivado abre la página desde su inicio."
            };
            exactPositionCheckBox.CheckedChanged +=
                ExactPositionCheckBox_CheckedChanged;

            positionInput = new NumericUpDown
            {
                Left = 214,
                Top = 112,
                Width = 76,
                Height = 26,
                Minimum = 0,
                Maximum = 100,
                DecimalPlaces = 1,
                Increment = 0.5M,
                Value = 0,
                TextAlign = HorizontalAlignment.Right,
                Font = CreateUiFont(9f, FontStyle.Regular),
                AccessibleName =
                    "Porcentaje desde el borde superior de la página"
            };
            positionInput.ValueChanged +=
                DestinationControl_ValueChanged;
            percentLabel = new Label
            {
                Left = 294,
                Top = 112,
                Width = 24,
                Height = 26,
                Text = "%",
                ForeColor = BodyColor,
                BackColor = PaperColor,
                Font = CreateUiFont(8.75f, FontStyle.Regular),
                TextAlign = ContentAlignment.MiddleLeft
            };

            useVisibleViewButton = CreateSecondaryWideButton(
                "Usar vista actual");
            useVisibleViewButton.Left = 18;
            useVisibleViewButton.Top = 153;
            useVisibleViewButton.Width = 174;
            useVisibleViewButton.AccessibleName =
                "Usar la página y posición actualmente visibles";
            useVisibleViewButton.Click += UseVisibleViewButton_Click;

            var visibleViewHintLabel = new Label
            {
                Left = 202,
                Top = 151,
                Width = 116,
                Height = 40,
                Anchor =
                    AnchorStyles.Top |
                    AnchorStyles.Left |
                    AnchorStyles.Right,
                Text = "Vista capturada al abrir.",
                ForeColor = MutedColor,
                BackColor = PaperColor,
                Font = CreateUiFont(7.65f, FontStyle.Regular),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };

            var destinationFootnoteLabel = new Label
            {
                Left = 18,
                Top = 194,
                Width = 300,
                Height = 24,
                Anchor =
                    AnchorStyles.Top |
                    AnchorStyles.Left |
                    AnchorStyles.Right,
                Text = "0 % es el borde superior; 100 %, el inferior.",
                ForeColor = MutedColor,
                BackColor = PaperColor,
                Font = CreateUiFont(7.75f, FontStyle.Regular),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };

            destinationPanel.Controls.Add(destinationCaptionLabel);
            destinationPanel.Controls.Add(destinationStateLabel);
            destinationPanel.Controls.Add(pageLabel);
            destinationPanel.Controls.Add(pageNumberInput);
            destinationPanel.Controls.Add(exactPositionCheckBox);
            destinationPanel.Controls.Add(positionInput);
            destinationPanel.Controls.Add(percentLabel);
            destinationPanel.Controls.Add(useVisibleViewButton);
            destinationPanel.Controls.Add(visibleViewHintLabel);
            destinationPanel.Controls.Add(destinationFootnoteLabel);

            var helpPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = NavigationColor,
                Padding = new Padding(18, 12, 18, 8)
            };
            var helpCaptionLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 15,
                Text = "ATAJOS",
                ForeColor = AccentTextColor,
                Font = CreateArchitecturalFont(7.25f, true),
                TextAlign = ContentAlignment.MiddleLeft
            };
            var helpTextLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 84,
                Padding = new Padding(0, 8, 0, 0),
                Text =
                    "Insert / Ctrl+N   Crear\n" +
                    "Doble clic / F2   Renombrar\n" +
                    "Supr              Eliminar\n" +
                    "Alt + ↑ / ↓       Ordenar\n" +
                    "Alt + ← / →       Cambiar nivel",
                ForeColor = BodyColor,
                Font = CreateUiFont(8.15f, FontStyle.Regular),
                TextAlign = ContentAlignment.TopLeft
            };
            var reversibleLabel = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 24,
                Text = "Puedes cancelar toda la edición sin alterar el PDF.",
                ForeColor = MutedColor,
                Font = CreateUiFont(7.9f, FontStyle.Regular),
                TextAlign = ContentAlignment.BottomLeft,
                AutoEllipsis = true
            };
            helpPanel.Controls.Add(reversibleLabel);
            helpPanel.Controls.Add(helpTextLabel);
            helpPanel.Controls.Add(helpCaptionLabel);

            detailSection.Controls.Add(helpPanel);
            detailSection.Controls.Add(destinationPanel);
            detailSection.Controls.Add(detailHeaderPanel);

            bodyLayout.Controls.Add(treeSection, 0, 0);
            bodyLayout.Controls.Add(detailSection, 1, 0);

            Controls.Add(bodyLayout);
            Controls.Add(footerPanel);
            Controls.Add(headerPanel);

            AcceptButton = applyButton;
            CancelButton = cancelButton;

            SetToolTips();
            RebuildTree(null);
            UpdateSelectionControls();
        }

        public PdfBookmarkDocument EditedDocument
        {
            get
            {
                return editedDocument;
            }
        }

        public bool HasChanges
        {
            get
            {
                return hasChanges;
            }
        }

        public string SelectedNodeId
        {
            get
            {
                if (bookmarkTree.SelectedNode != null)
                {
                    return bookmarkTree.SelectedNode.Tag as string;
                }

                return selectedNodeId;
            }
        }

        internal TreeView BookmarkTreeForTesting
        {
            get
            {
                return bookmarkTree;
            }
        }

        internal NumericUpDown PageNumberInputForTesting
        {
            get
            {
                return pageNumberInput;
            }
        }

        internal NumericUpDown PositionInputForTesting
        {
            get
            {
                return positionInput;
            }
        }

        internal CheckBox ExactPositionCheckBoxForTesting
        {
            get
            {
                return exactPositionCheckBox;
            }
        }

        internal Button ApplyButtonForTesting
        {
            get
            {
                return applyButton;
            }
        }

        private Panel CreateHeaderPanel()
        {
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
                Top = 8,
                Width = 320,
                Height = 15,
                Text = "DOCUMENTO / MARCADORES",
                ForeColor = AccentTextColor,
                Font = CreateArchitecturalFont(7.5f, true),
                TextAlign = ContentAlignment.MiddleLeft
            };
            var titleLabel = new Label
            {
                Left = 20,
                Top = 24,
                Width = 520,
                Height = 28,
                Text = "Estructura y navegación",
                ForeColor = TitleColor,
                Font = CreateArchitecturalFont(13f, false),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
            var accentLine = new Panel
            {
                Left = 20,
                Top = 58,
                Width = 42,
                Height = 2,
                BackColor = AccentColor
            };
            var phaseLabel = new Label
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Left = 820,
                Top = 10,
                Width = 58,
                Height = 42,
                Text = "05",
                ForeColor = DividerColor,
                Font = CreateArchitecturalFont(23f, false),
                TextAlign = ContentAlignment.MiddleRight
            };

            headerPanel.Controls.Add(eyebrowLabel);
            headerPanel.Controls.Add(titleLabel);
            headerPanel.Controls.Add(accentLine);
            headerPanel.Controls.Add(phaseLabel);
            return headerPanel;
        }

        private void SetToolTips()
        {
            toolTip.SetToolTip(
                createButton,
                "Crear después del marcador seleccionado (Insert)");
            toolTip.SetToolTip(
                renameButton,
                "Editar el título (doble clic o F2)");
            toolTip.SetToolTip(
                deleteButton,
                "Quitar el marcador y sus hijos (Supr)");
            toolTip.SetToolTip(
                moveUpButton,
                "Subir dentro del mismo nivel (Alt+Arriba)");
            toolTip.SetToolTip(
                moveDownButton,
                "Bajar dentro del mismo nivel (Alt+Abajo)");
            toolTip.SetToolTip(
                outdentButton,
                "Mover un nivel a la izquierda (Alt+Izquierda)");
            toolTip.SetToolTip(
                indentButton,
                "Convertir en hijo del marcador anterior (Alt+Derecha)");
            toolTip.SetToolTip(
                exactPositionCheckBox,
                "Guarda un punto vertical concreto en vez del inicio");
            toolTip.SetToolTip(
                useVisibleViewButton,
                "Usar la página y posición visibles al abrir el editor");
            toolTip.SetToolTip(
                applyButton,
                "Aceptar esta estructura y continuar (Ctrl+Enter)");
        }

        private void RebuildTree(string selectedId)
        {
            bookmarkTree.BeginUpdate();
            try
            {
                bookmarkTree.Nodes.Clear();
                AddTreeNodes(
                    bookmarkTree.Nodes,
                    workingDocument.Bookmarks);

                if (!string.IsNullOrEmpty(selectedId))
                {
                    var selectedNode = FindTreeNode(
                        bookmarkTree.Nodes,
                        selectedId);
                    if (selectedNode != null)
                    {
                        bookmarkTree.SelectedNode = selectedNode;
                        selectedNode.EnsureVisible();
                    }
                }
                else if (bookmarkTree.Nodes.Count > 0)
                {
                    bookmarkTree.SelectedNode = bookmarkTree.Nodes[0];
                }
            }
            finally
            {
                bookmarkTree.EndUpdate();
            }

            var total = CountBookmarks(workingDocument.Bookmarks);
            treeSummaryLabel.Text = total == 1
                ? "1 marcador"
                : total.ToString(CultureInfo.CurrentCulture) +
                  " marcadores";
            UpdateSelectionControls();
        }

        private static void AddTreeNodes(
            TreeNodeCollection target,
            IList<PdfBookmarkNode> source)
        {
            if (source == null)
            {
                return;
            }

            foreach (var bookmark in source)
            {
                var title = NormalizeTitle(bookmark.Title);
                var treeNode = new TreeNode(title)
                {
                    Name = bookmark.Id ?? string.Empty,
                    Tag = bookmark.Id,
                    ToolTipText = GetDestinationSummary(bookmark)
                };
                target.Add(treeNode);
                AddTreeNodes(treeNode.Nodes, bookmark.Children);
                if (bookmark.IsOpen)
                {
                    treeNode.Expand();
                }
            }
        }

        private static TreeNode FindTreeNode(
            TreeNodeCollection nodes,
            string id)
        {
            foreach (TreeNode node in nodes)
            {
                if (string.Equals(
                    node.Tag as string,
                    id,
                    StringComparison.Ordinal))
                {
                    return node;
                }

                var child = FindTreeNode(node.Nodes, id);
                if (child != null)
                {
                    return child;
                }
            }

            return null;
        }

        private void BookmarkTree_AfterSelect(
            object sender,
            TreeViewEventArgs e)
        {
            UpdateSelectionControls();
        }

        private void BookmarkTree_DrawNode(
            object sender,
            DrawTreeNodeEventArgs e)
        {
            if (e.Node == null)
            {
                return;
            }

            var selected =
                (e.State & TreeNodeStates.Selected) != 0;
            var rowBounds = new Rectangle(
                e.Bounds.X,
                e.Bounds.Y,
                Math.Max(
                    1,
                    bookmarkTree.ClientSize.Width -
                    e.Bounds.X),
                e.Bounds.Height);
            using (var backgroundBrush = new SolidBrush(
                selected ? AccentTintColor : PaperColor))
            {
                e.Graphics.FillRectangle(
                    backgroundBrush,
                    rowBounds);
            }
            TextRenderer.DrawText(
                e.Graphics,
                e.Node.Text,
                bookmarkTree.Font,
                rowBounds,
                selected ? AccentTextColor : TitleColor,
                TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);

            if (selected &&
                bookmarkTree.Focused &&
                (e.State & TreeNodeStates.Focused) != 0)
            {
                ControlPaint.DrawFocusRectangle(
                    e.Graphics,
                    rowBounds,
                    AccentTextColor,
                    AccentTintColor);
            }
        }

        private void BookmarkTree_AfterLabelEdit(
            object sender,
            NodeLabelEditEventArgs e)
        {
            if (e.Node == null || e.Label == null)
            {
                return;
            }

            var title = e.Label.Trim();
            if (title.Length == 0)
            {
                e.CancelEdit = true;
                footerStatusLabel.Text =
                    "El título del marcador no puede quedar vacío.";
                footerStatusLabel.ForeColor = AccentTextColor;
                return;
            }

            // TreeView aplicaría después la cadena original de e.Label. Se
            // cancela esa asignación para conservar la versión normalizada.
            e.CancelEdit = true;
            var id = e.Node.Tag as string;
            try
            {
                var location = FindLocation(
                    workingDocument.Bookmarks,
                    null,
                    id);
                if (location != null &&
                    string.Equals(
                        location.Node.Title,
                        title,
                        StringComparison.Ordinal))
                {
                    e.Node.Text = title;
                    selectedTitleLabel.Text = title;
                    return;
                }

                PdfBookmarkService.Rename(
                    workingDocument,
                    id,
                    title);
                hasChanges = true;
                e.Node.Text = title;
                selectedTitleLabel.Text = title;
                footerStatusLabel.Text =
                    "Título actualizado. Falta pulsar Aplicar.";
                footerStatusLabel.ForeColor = MutedColor;
            }
            catch (Exception ex)
            {
                e.CancelEdit = true;
                ShowMutationError(
                    "No se pudo renombrar el marcador.",
                    ex);
            }
        }

        private void BookmarkTree_NodeMouseDoubleClick(
            object sender,
            TreeNodeMouseClickEventArgs e)
        {
            if (e.Button != MouseButtons.Left || e.Node == null)
            {
                return;
            }

            bookmarkTree.SelectedNode = e.Node;
            e.Node.BeginEdit();
        }

        private void BookmarkTree_KeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Insert)
            {
                CreateBookmark();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.F2)
            {
                BeginRename();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Delete)
            {
                DeleteSelectedBookmark();
                e.Handled = true;
            }
            else if (e.Alt && e.KeyCode == Keys.Up)
            {
                MoveSelectedBookmark(-1);
                e.Handled = true;
            }
            else if (e.Alt && e.KeyCode == Keys.Down)
            {
                MoveSelectedBookmark(1);
                e.Handled = true;
            }
            else if (e.Alt && e.KeyCode == Keys.Left)
            {
                OutdentSelectedBookmark();
                e.Handled = true;
            }
            else if (e.Alt && e.KeyCode == Keys.Right)
            {
                IndentSelectedBookmark();
                e.Handled = true;
            }

            if (e.Handled)
            {
                e.SuppressKeyPress = true;
            }
        }

        protected override bool ProcessCmdKey(
            ref Message msg,
            Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.N))
            {
                CreateBookmark();
                return true;
            }
            if (keyData == (Keys.Control | Keys.Enter))
            {
                ApplyChanges();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void CreateBookmark()
        {
            FinishPendingRename();

            var selected = GetSelectedLocation();
            var parentId = selected == null ||
                selected.Parent == null
                    ? null
                    : selected.Parent.Id;
            var index = selected == null
                ? workingDocument.Bookmarks.Count
                : selected.Index + 1;
            var destination = GetSafeVisibleDestination();
            if (destination == null)
            {
                destination = selected != null &&
                    selected.Node.Destination != null
                        ? CloneDestination(
                            selected.Node.Destination)
                        : new PdfBookmarkDestination(1, null);
            }

            try
            {
                var created = PdfBookmarkService.Create(
                    workingDocument,
                    parentId,
                    index,
                    "Nuevo marcador",
                    destination);
                hasChanges = true;
                RebuildTree(created.Id);
                BeginRename();
            }
            catch (Exception ex)
            {
                ShowMutationError(
                    "No se pudo crear el marcador.",
                    ex);
            }
        }

        private void BeginRename()
        {
            if (bookmarkTree.SelectedNode == null)
            {
                return;
            }

            bookmarkTree.Focus();
            bookmarkTree.SelectedNode.BeginEdit();
        }

        private void FinishPendingRename()
        {
            if (bookmarkTree.LabelEdit &&
                bookmarkTree.SelectedNode != null)
            {
                bookmarkTree.SelectedNode.EndEdit(false);
            }
        }

        private void DeleteSelectedBookmark()
        {
            FinishPendingRename();
            var selected = GetSelectedLocation();
            if (selected == null)
            {
                return;
            }

            string nextId = null;
            if (selected.Index + 1 < selected.Siblings.Count)
            {
                nextId = selected.Siblings[selected.Index + 1].Id;
            }
            else if (selected.Index > 0)
            {
                nextId = selected.Siblings[selected.Index - 1].Id;
            }
            else if (selected.Parent != null)
            {
                nextId = selected.Parent.Id;
            }

            try
            {
                PdfBookmarkService.Delete(
                    workingDocument,
                    selected.Node.Id);
                hasChanges = true;
                RebuildTree(nextId);
                footerStatusLabel.Text =
                    "Marcador eliminado en esta edición. " +
                    "Cancelar aún descarta el cambio.";
                footerStatusLabel.ForeColor = MutedColor;
            }
            catch (Exception ex)
            {
                ShowMutationError(
                    "No se pudo eliminar el marcador.",
                    ex);
            }
        }

        private void MoveSelectedBookmark(int direction)
        {
            FinishPendingRename();
            var selected = GetSelectedLocation();
            if (selected == null)
            {
                return;
            }

            var targetIndex = selected.Index + direction;
            if (targetIndex < 0 ||
                targetIndex >= selected.Siblings.Count)
            {
                return;
            }

            var parentId = selected.Parent == null
                ? null
                : selected.Parent.Id;
            MoveBookmark(
                selected.Node.Id,
                parentId,
                targetIndex,
                direction < 0
                    ? "No se pudo subir el marcador."
                    : "No se pudo bajar el marcador.");
        }

        private void OutdentSelectedBookmark()
        {
            FinishPendingRename();
            var selected = GetSelectedLocation();
            if (selected == null || selected.Parent == null)
            {
                return;
            }

            var parentLocation = FindLocation(
                workingDocument.Bookmarks,
                null,
                selected.Parent.Id);
            if (parentLocation == null)
            {
                return;
            }

            var newParentId = parentLocation.Parent == null
                ? null
                : parentLocation.Parent.Id;
            MoveBookmark(
                selected.Node.Id,
                newParentId,
                parentLocation.Index + 1,
                "No se pudo reducir el nivel del marcador.");
        }

        private void IndentSelectedBookmark()
        {
            FinishPendingRename();
            var selected = GetSelectedLocation();
            if (selected == null || selected.Index < 1)
            {
                return;
            }

            var newParent =
                selected.Siblings[selected.Index - 1];
            MoveBookmark(
                selected.Node.Id,
                newParent.Id,
                newParent.Children.Count,
                "No se pudo aumentar el nivel del marcador.");
        }

        private void MoveBookmark(
            string id,
            string parentId,
            int index,
            string errorMessage)
        {
            try
            {
                PdfBookmarkService.Move(
                    workingDocument,
                    id,
                    parentId,
                    index);
                hasChanges = true;
                RebuildTree(id);
            }
            catch (Exception ex)
            {
                ShowMutationError(errorMessage, ex);
            }
        }

        private void UpdateSelectionControls()
        {
            var selected = GetSelectedLocation();
            var hasSelection = selected != null;
            var canEditDestination = hasSelection &&
                selected.Node.IsDestinationEditable;

            renameButton.Enabled = hasSelection;
            deleteButton.Enabled = hasSelection;
            moveUpButton.Enabled =
                hasSelection && selected.Index > 0;
            moveDownButton.Enabled =
                hasSelection &&
                selected.Index + 1 < selected.Siblings.Count;
            outdentButton.Enabled =
                hasSelection && selected.Parent != null;
            indentButton.Enabled =
                hasSelection && selected.Index > 0;

            selectedTitleLabel.Text = hasSelection
                ? NormalizeTitle(selected.Node.Title)
                : "Ningún marcador seleccionado";

            updatingSelectionControls = true;
            try
            {
                pageNumberInput.Enabled = canEditDestination;
                exactPositionCheckBox.Enabled =
                    canEditDestination;
                useVisibleViewButton.Enabled =
                    canEditDestination &&
                    visibleDestinationProvider != null;

                if (!hasSelection)
                {
                    destinationStateLabel.Text =
                        "Selecciona un marcador para editar su destino.";
                    exactPositionCheckBox.Checked = false;
                    positionInput.Value = 0;
                }
                else if (!canEditDestination)
                {
                    destinationStateLabel.Text =
                        "Este marcador usa una acción externa o avanzada. " +
                        "Se conservará sin cambios.";
                    exactPositionCheckBox.Checked = false;
                    positionInput.Value = 0;
                }
                else
                {
                    var destination = selected.Node.Destination;
                    if (destination == null)
                    {
                        destinationStateLabel.Text =
                            "Sin destino. Elige una página o usa la vista actual.";
                        pageNumberInput.Value = 1;
                        exactPositionCheckBox.Checked = false;
                        positionInput.Value = 0;
                    }
                    else
                    {
                        destinationStateLabel.Text =
                            GetDestinationSummary(selected.Node);
                        pageNumberInput.Value = ClampDecimal(
                            destination.PageNumber,
                            pageNumberInput.Minimum,
                            pageNumberInput.Maximum);
                        exactPositionCheckBox.Checked =
                            destination.TopPositionPercent.HasValue;
                        positionInput.Value =
                            destination.TopPositionPercent.HasValue
                                ? ClampDecimal(
                                    destination
                                        .TopPositionPercent
                                        .Value,
                                    positionInput.Minimum,
                                    positionInput.Maximum)
                                : 0;
                    }
                }

                positionInput.Enabled =
                    canEditDestination &&
                    exactPositionCheckBox.Checked;
                percentLabel.Enabled = positionInput.Enabled;
            }
            finally
            {
                updatingSelectionControls = false;
            }
        }

        private void ExactPositionCheckBox_CheckedChanged(
            object sender,
            EventArgs e)
        {
            positionInput.Enabled =
                exactPositionCheckBox.Enabled &&
                exactPositionCheckBox.Checked;
            percentLabel.Enabled = positionInput.Enabled;
            CommitDestinationFromControls(false);
        }

        private void DestinationControl_ValueChanged(
            object sender,
            EventArgs e)
        {
            CommitDestinationFromControls(
                ReferenceEquals(sender, pageNumberInput));
        }

        private void CommitDestinationFromControls(
            bool preserveRawDestination)
        {
            if (updatingSelectionControls)
            {
                return;
            }

            var selected = GetSelectedLocation();
            if (selected == null ||
                !selected.Node.IsDestinationEditable)
            {
                return;
            }

            var destination = preserveRawDestination
                ? CreateDestinationWithPage(
                    selected.Node.Destination,
                    (int)pageNumberInput.Value)
                : CreateDestinationFromControls(
                    selected.Node.Destination);
            if (AreEquivalent(
                selected.Node.Destination,
                destination))
            {
                destinationStateLabel.Text =
                    GetDestinationSummary(selected.Node);
                return;
            }

            SetSelectedDestination(selected, destination);
        }

        private PdfBookmarkDestination CreateDestinationWithPage(
            PdfBookmarkDestination current,
            int pageNumber)
        {
            if (current == null)
            {
                return new PdfBookmarkDestination(
                    pageNumber,
                    exactPositionCheckBox.Checked
                        ? (double?)positionInput.Value
                        : null);
            }

            return PdfBookmarkDestination.FromPdf(
                pageNumber,
                current.Mode,
                current.TopPositionPercent,
                current.LeftPositionPercent,
                current.BottomPositionPercent,
                current.RightPositionPercent,
                current.Zoom);
        }

        private void UseVisibleViewButton_Click(
            object sender,
            EventArgs e)
        {
            var destination = GetSafeVisibleDestination();
            if (destination == null)
            {
                footerStatusLabel.Text =
                    "No se pudo recuperar la vista actual.";
                footerStatusLabel.ForeColor = AccentTextColor;
                return;
            }

            updatingSelectionControls = true;
            try
            {
                pageNumberInput.Value = ClampDecimal(
                    destination.PageNumber,
                    pageNumberInput.Minimum,
                    pageNumberInput.Maximum);
                exactPositionCheckBox.Checked =
                    destination.TopPositionPercent.HasValue;
                positionInput.Value =
                    destination.TopPositionPercent.HasValue
                        ? ClampDecimal(
                            destination.TopPositionPercent.Value,
                            positionInput.Minimum,
                            positionInput.Maximum)
                        : 0;
                positionInput.Enabled =
                    exactPositionCheckBox.Checked;
                percentLabel.Enabled = positionInput.Enabled;
            }
            finally
            {
                updatingSelectionControls = false;
            }

            var selected = GetSelectedLocation();
            if (selected == null ||
                !selected.Node.IsDestinationEditable)
            {
                UpdateSelectionControls();
                return;
            }

            SetSelectedDestination(selected, destination);
        }

        private PdfBookmarkDestination GetSafeVisibleDestination()
        {
            if (visibleDestinationProvider == null)
            {
                return null;
            }

            try
            {
                var destination = visibleDestinationProvider();
                if (destination == null ||
                    destination.PageNumber < 1 ||
                    destination.PageNumber >
                        workingDocument.PageCount)
                {
                    return null;
                }
                if (destination.TopPositionPercent.HasValue &&
                    (destination.TopPositionPercent.Value < 0 ||
                     destination.TopPositionPercent.Value > 100))
                {
                    return null;
                }

                return CloneDestination(destination);
            }
            catch
            {
                return null;
            }
        }

        private PdfBookmarkDestination CreateDestinationFromControls(
            PdfBookmarkDestination current)
        {
            var pageNumber = (int)pageNumberInput.Value;
            var top = exactPositionCheckBox.Checked
                ? (double?)positionInput.Value
                : null;
            if (current == null)
            {
                return new PdfBookmarkDestination(
                    pageNumber,
                    top);
            }

            var topChanged = !Nullable.Equals(
                current.TopPositionPercent,
                top);
            if (!topChanged)
            {
                return PdfBookmarkDestination.FromPdf(
                    pageNumber,
                    current.Mode,
                    current.TopPositionPercent,
                    current.LeftPositionPercent,
                    current.BottomPositionPercent,
                    current.RightPositionPercent,
                    current.Zoom);
            }

            if (DestinationModeSupportsTop(current.Mode) &&
                (current.Mode !=
                    PdfBookmarkDestinationMode.FitRectangle ||
                 top.HasValue))
            {
                return PdfBookmarkDestination.FromPdf(
                    pageNumber,
                    current.Mode,
                    top,
                    current.LeftPositionPercent,
                    current.BottomPositionPercent,
                    current.RightPositionPercent,
                    current.Zoom);
            }

            // El control expresa una posición vertical. Los modos Fit/FitV
            // no pueden representarla y FitR no admite coordenadas vacías;
            // solo una edición explícita convierte esos casos a XYZ.
            return PdfBookmarkDestination.FromPdf(
                pageNumber,
                PdfBookmarkDestinationMode.Xyz,
                top,
                current.LeftPositionPercent,
                null,
                null,
                current.Mode == PdfBookmarkDestinationMode.Xyz
                    ? current.Zoom
                    : null);
        }

        private void SetSelectedDestination(
            ModelLocation selected,
            PdfBookmarkDestination destination)
        {
            try
            {
                PdfBookmarkService.SetDestination(
                    workingDocument,
                    selected.Node.Id,
                    destination);
                hasChanges = true;
                destinationStateLabel.Text =
                    GetDestinationSummary(selected.Node);
                footerStatusLabel.Text =
                    "Destino actualizado. Falta pulsar Aplicar.";
                footerStatusLabel.ForeColor = MutedColor;
            }
            catch (Exception ex)
            {
                ShowMutationError(
                    "No se pudo cambiar el destino.",
                    ex);
                UpdateSelectionControls();
            }
        }

        private static bool DestinationModeSupportsTop(
            PdfBookmarkDestinationMode mode)
        {
            return mode == PdfBookmarkDestinationMode.Xyz ||
                mode == PdfBookmarkDestinationMode.FitHorizontal ||
                mode ==
                    PdfBookmarkDestinationMode.FitBoundingBoxHorizontal ||
                mode == PdfBookmarkDestinationMode.FitRectangle;
        }

        private void ApplyButton_Click(
            object sender,
            EventArgs e)
        {
            ApplyChanges();
        }

        private void ApplyChanges()
        {
            if (applying)
            {
                return;
            }

            applying = true;
            try
            {
                FinishPendingRename();
                string validationMessage;
                if (!ValidateBookmarks(
                    workingDocument.Bookmarks,
                    workingDocument.PageCount,
                    out validationMessage))
                {
                    footerStatusLabel.Text = validationMessage;
                    footerStatusLabel.ForeColor = AccentTextColor;
                    return;
                }

                editedDocument = workingDocument;
                selectedNodeId = bookmarkTree.SelectedNode == null
                    ? null
                    : bookmarkTree.SelectedNode.Tag as string;
                DialogResult = DialogResult.OK;
                Close();
            }
            finally
            {
                applying = false;
            }
        }

        private ModelLocation GetSelectedLocation()
        {
            if (bookmarkTree.SelectedNode == null)
            {
                return null;
            }

            return FindLocation(
                workingDocument.Bookmarks,
                null,
                bookmarkTree.SelectedNode.Tag as string);
        }

        private static ModelLocation FindLocation(
            IList<PdfBookmarkNode> siblings,
            PdfBookmarkNode parent,
            string id)
        {
            if (siblings == null || string.IsNullOrEmpty(id))
            {
                return null;
            }

            for (var index = 0; index < siblings.Count; index++)
            {
                var node = siblings[index];
                if (string.Equals(
                    node.Id,
                    id,
                    StringComparison.Ordinal))
                {
                    return new ModelLocation(
                        node,
                        parent,
                        siblings,
                        index);
                }

                var child = FindLocation(
                    node.Children,
                    node,
                    id);
                if (child != null)
                {
                    return child;
                }
            }

            return null;
        }

        private static bool ValidateBookmarks(
            IList<PdfBookmarkNode> bookmarks,
            int pageCount,
            out string message)
        {
            if (bookmarks != null)
            {
                foreach (var bookmark in bookmarks)
                {
                    if (bookmark == null)
                    {
                        message =
                            "La jerarquía contiene un marcador no válido.";
                        return false;
                    }
                    if (string.IsNullOrWhiteSpace(bookmark.Title))
                    {
                        message =
                            "Todos los marcadores necesitan un título.";
                        return false;
                    }
                    if (bookmark.Destination != null &&
                        bookmark.IsDestinationEditable &&
                        (bookmark.Destination.PageNumber < 1 ||
                         bookmark.Destination.PageNumber > pageCount))
                    {
                        message =
                            "Hay un marcador con una página fuera del PDF.";
                        return false;
                    }
                    if (!ValidateBookmarks(
                        bookmark.Children,
                        pageCount,
                        out message))
                    {
                        return false;
                    }
                }
            }

            message = null;
            return true;
        }

        private static int CountBookmarks(
            IList<PdfBookmarkNode> bookmarks)
        {
            if (bookmarks == null)
            {
                return 0;
            }

            var count = 0;
            foreach (var bookmark in bookmarks)
            {
                count++;
                count += CountBookmarks(bookmark.Children);
            }
            return count;
        }

        private static string GetDestinationSummary(
            PdfBookmarkNode bookmark)
        {
            if (bookmark == null)
            {
                return string.Empty;
            }
            if (!bookmark.IsDestinationEditable)
            {
                return "Acción externa o avanzada";
            }
            if (bookmark.Destination == null)
            {
                return "Sin destino";
            }

            var summary = "Página " +
                bookmark.Destination.PageNumber
                    .ToString(CultureInfo.CurrentCulture);
            if (bookmark.Destination
                .TopPositionPercent.HasValue)
            {
                summary += " · " +
                    bookmark.Destination
                        .TopPositionPercent
                        .Value
                        .ToString(
                            "0.#",
                            CultureInfo.CurrentCulture) +
                    " % desde arriba";
            }
            else
            {
                summary += " · inicio de página";
            }
            return summary;
        }

        private static PdfBookmarkDestination CloneDestination(
            PdfBookmarkDestination destination)
        {
            if (destination == null)
            {
                return null;
            }

            return destination.Clone();
        }

        private static bool AreEquivalent(
            PdfBookmarkDestination left,
            PdfBookmarkDestination right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }
            if (left == null || right == null)
            {
                return false;
            }

            return left.PageNumber == right.PageNumber &&
                left.Mode == right.Mode &&
                Nullable.Equals(
                    left.TopPositionPercent,
                    right.TopPositionPercent) &&
                Nullable.Equals(
                    left.LeftPositionPercent,
                    right.LeftPositionPercent) &&
                Nullable.Equals(
                    left.BottomPositionPercent,
                    right.BottomPositionPercent) &&
                Nullable.Equals(
                    left.RightPositionPercent,
                    right.RightPositionPercent) &&
                Nullable.Equals(left.Zoom, right.Zoom);
        }

        private static string NormalizeTitle(string title)
        {
            return string.IsNullOrWhiteSpace(title)
                ? "Marcador"
                : title.Trim();
        }

        private static decimal ClampDecimal(
            double value,
            decimal minimum,
            decimal maximum)
        {
            decimal decimalValue;
            try
            {
                decimalValue = (decimal)value;
            }
            catch
            {
                decimalValue = minimum;
            }

            if (decimalValue < minimum)
            {
                return minimum;
            }
            if (decimalValue > maximum)
            {
                return maximum;
            }
            return decimalValue;
        }

        private void ShowMutationError(
            string message,
            Exception exception)
        {
            footerStatusLabel.Text = message;
            footerStatusLabel.ForeColor = AccentTextColor;
            MessageBox.Show(
                this,
                message + Environment.NewLine +
                Environment.NewLine +
                exception.Message,
                AppBranding.ApplicationName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        protected override void OnFormClosed(
            FormClosedEventArgs e)
        {
            toolTip.Dispose();
            base.OnFormClosed(e);
        }

        private static Panel CreateToolSeparator()
        {
            return new Panel
            {
                Width = 1,
                Height = 24,
                Margin = new Padding(3, 4, 4, 0),
                BackColor = DividerColor
            };
        }

        private static Button CreateToolButton(
            string text,
            string accessibleName)
        {
            var button = new Button
            {
                Width = 36,
                Height = 30,
                Margin = new Padding(0, 0, 4, 0),
                Padding = Padding.Empty,
                Text = text,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Font = CreateArchitecturalFont(
                    text == "F2" ? 7.5f : 12f,
                    true),
                BackColor = PaperColor,
                ForeColor = TitleColor,
                AccessibleName = accessibleName
            };
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = DividerColor;
            button.FlatAppearance.MouseOverBackColor =
                AccentTintColor;
            button.FlatAppearance.MouseDownBackColor =
                DividerColor;
            return button;
        }

        private static Button CreateSecondaryWideButton(
            string text)
        {
            var button = new Button
            {
                Height = 34,
                Text = text,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Font = CreateArchitecturalFont(8.5f, true),
                BackColor = PaperColor,
                ForeColor = TitleColor
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

        private sealed class ModelLocation
        {
            public ModelLocation(
                PdfBookmarkNode node,
                PdfBookmarkNode parent,
                IList<PdfBookmarkNode> siblings,
                int index)
            {
                Node = node;
                Parent = parent;
                Siblings = siblings;
                Index = index;
            }

            public PdfBookmarkNode Node { get; private set; }

            public PdfBookmarkNode Parent { get; private set; }

            public IList<PdfBookmarkNode> Siblings
            {
                get;
                private set;
            }

            public int Index { get; private set; }
        }
    }
}

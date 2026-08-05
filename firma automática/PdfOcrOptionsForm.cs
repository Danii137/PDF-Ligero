using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace FirmaAutomatica
{
    internal sealed class PdfOcrOptionsForm : Form
    {
        private static readonly Color PaperColor =
            Color.FromArgb(250, 249, 247);
        private static readonly Color WorkspaceColor =
            Color.FromArgb(239, 238, 235);
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

        private readonly int pageCount;
        private readonly int currentPageIndex;
        private readonly List<int> selectedPageIndexes;

        private readonly RadioButton currentPageRadioButton;
        private readonly RadioButton selectedPagesRadioButton;
        private readonly RadioButton allPagesRadioButton;
        private readonly Label scopeSummaryLabel;
        private readonly CheckBox skipTextCheckBox;
        private readonly CheckBox autoOrientCheckBox;
        private readonly CheckBox autoDeskewCheckBox;
        private readonly Button continueButton;
        private readonly Button cancelButton;

        private PdfOcrSettings settings;

        public PdfOcrOptionsForm(
            int pageCount,
            int currentPageIndex,
            IEnumerable<int> selectedPageIndexes)
        {
            ValidateDocumentRange(pageCount, currentPageIndex);

            this.pageCount = pageCount;
            this.currentPageIndex = currentPageIndex;
            this.selectedPageIndexes = NormalizeSelectedPages(
                pageCount,
                selectedPageIndexes);

            Text = "OCR y alineación - PDF Ligero";
            AppBranding.ApplyWindowIcon(this);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(492, 512);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = WorkspaceColor;
            Font = CreateUiFont(9.25f, FontStyle.Regular);

            var headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 76,
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
                Width = 350,
                Height = 15,
                Text = "OCR / TEXTO Y ALINEACIÓN",
                ForeColor = AccentTextColor,
                Font = CreateArchitecturalFont(7.5f, true),
                TextAlign = ContentAlignment.MiddleLeft
            };
            var titleLabel = new Label
            {
                Left = 20,
                Top = 24,
                Width = 410,
                Height = 28,
                Text = "Preparar páginas buscables",
                ForeColor = TitleColor,
                Font = CreateArchitecturalFont(13f, false),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
            var accentLine = new Panel
            {
                Left = 20,
                Top = 61,
                Width = 42,
                Height = 2,
                BackColor = AccentColor
            };
            var phaseLabel = new Label
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Left = 427,
                Top = 12,
                Width = 44,
                Height = 40,
                Text = "04",
                ForeColor = DividerColor,
                Font = CreateArchitecturalFont(23f, false),
                TextAlign = ContentAlignment.MiddleRight
            };

            headerPanel.Controls.Add(eyebrowLabel);
            headerPanel.Controls.Add(titleLabel);
            headerPanel.Controls.Add(accentLine);
            headerPanel.Controls.Add(phaseLabel);

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
            cancelButton.Width = 96;
            cancelButton.Left = 278;
            cancelButton.Top = 15;
            cancelButton.DialogResult = DialogResult.Cancel;
            cancelButton.AccessibleName = "Cancelar OCR";

            continueButton = CreateActionButton("Analizar", true);
            continueButton.Width = 96;
            continueButton.Left = 384;
            continueButton.Top = 15;
            continueButton.AccessibleName = "Analizar las páginas elegidas";
            continueButton.AccessibleDescription =
                "Acepta estas opciones. El análisis se realizará después, " +
                "fuera de este diálogo.";
            continueButton.Click += ContinueButton_Click;

            footerPanel.Controls.Add(cancelButton);
            footerPanel.Controls.Add(continueButton);

            var bodyPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(20, 14, 20, 14),
                BackColor = WorkspaceColor
            };

            var contentLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = WorkspaceColor,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            contentLayout.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 100F));
            contentLayout.RowStyles.Add(
                new RowStyle(SizeType.Absolute, 150F));
            contentLayout.RowStyles.Add(
                new RowStyle(SizeType.Absolute, 161F));
            contentLayout.RowStyles.Add(
                new RowStyle(SizeType.Percent, 100F));

            var scopePanel = CreateSectionPanel();
            var scopeCaptionLabel = CreateSectionCaption(
                "ÁMBITO / PÁGINAS");
            currentPageRadioButton = CreateScopeRadioButton(
                "&Página actual · " +
                (currentPageIndex + 1).ToString());
            selectedPagesRadioButton = CreateScopeRadioButton(
                "Páginas &seleccionadas · " +
                this.selectedPageIndexes.Count.ToString());
            allPagesRadioButton = CreateScopeRadioButton(
                "&Todo el documento · " +
                pageCount.ToString() + " páginas");

            var scopeOptions = new FlowLayoutPanel
            {
                Left = 15,
                Top = 31,
                Width = 410,
                Height = 82,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = PaperColor,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            scopeOptions.Controls.Add(currentPageRadioButton);
            scopeOptions.Controls.Add(selectedPagesRadioButton);
            scopeOptions.Controls.Add(allPagesRadioButton);

            selectedPagesRadioButton.Visible =
                this.selectedPageIndexes.Count > 1;
            if (this.selectedPageIndexes.Count > 1)
            {
                selectedPagesRadioButton.Checked = true;
            }
            else
            {
                currentPageRadioButton.Checked = true;
            }

            scopeSummaryLabel = new Label
            {
                Left = 15,
                Top = 119,
                Width = 410,
                Height = 20,
                ForeColor = MutedColor,
                Font = CreateArchitecturalFont(7.75f, false),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                AccessibleName = "Resumen del ámbito OCR"
            };

            currentPageRadioButton.CheckedChanged += ScopeChanged;
            selectedPagesRadioButton.CheckedChanged += ScopeChanged;
            allPagesRadioButton.CheckedChanged += ScopeChanged;

            scopePanel.Controls.Add(scopeCaptionLabel);
            scopePanel.Controls.Add(scopeOptions);
            scopePanel.Controls.Add(scopeSummaryLabel);

            var optionsPanel = CreateSectionPanel();
            var optionsCaptionLabel = CreateSectionCaption(
                "RECONOCIMIENTO / AJUSTES");
            var languageLabel = new Label
            {
                Left = 15,
                Top = 31,
                Width = 410,
                Height = 22,
                Text = "IDIOMA   Español + inglés   ·   spa + eng",
                ForeColor = BodyColor,
                Font = CreateArchitecturalFont(8.25f, true),
                TextAlign = ContentAlignment.MiddleLeft,
                AccessibleName = "Idioma de reconocimiento: español e inglés"
            };

            skipTextCheckBox = CreateOptionCheckBox(
                "&Omitir páginas que ya tienen texto útil");
            skipTextCheckBox.Top = 58;
            skipTextCheckBox.Checked = true;
            skipTextCheckBox.AccessibleDescription =
                "Evita reprocesar páginas que ya se pueden buscar.";

            autoOrientCheckBox = CreateOptionCheckBox(
                "Detectar &orientación automáticamente");
            autoOrientCheckBox.Top = 87;
            autoOrientCheckBox.Checked = true;
            autoOrientCheckBox.AccessibleDescription =
                "Detecta giros de 90, 180 o 270 grados.";

            autoDeskewCheckBox = CreateOptionCheckBox(
                "Corregir &inclinaciones leves");
            autoDeskewCheckBox.Top = 116;
            autoDeskewCheckBox.Checked = true;
            autoDeskewCheckBox.AccessibleDescription =
                "Endereza ligeramente páginas escaneadas que estén torcidas.";

            skipTextCheckBox.CheckedChanged += OptionChanged;
            autoOrientCheckBox.CheckedChanged += OptionChanged;
            autoDeskewCheckBox.CheckedChanged += OptionChanged;

            optionsPanel.Controls.Add(optionsCaptionLabel);
            optionsPanel.Controls.Add(languageLabel);
            optionsPanel.Controls.Add(skipTextCheckBox);
            optionsPanel.Controls.Add(autoOrientCheckBox);
            optionsPanel.Controls.Add(autoDeskewCheckBox);

            var informationLabel = new Label
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(2, 10, 2, 0),
                Text =
                    "El análisis es local y no modifica el PDF original. " +
                    "Podrás revisar la orientación antes de aplicar los cambios.",
                ForeColor = MutedColor,
                Font = CreateUiFont(8.5f, FontStyle.Regular),
                TextAlign = ContentAlignment.TopLeft,
                AutoEllipsis = true,
                AccessibleName = "Información sobre el OCR"
            };

            contentLayout.Controls.Add(scopePanel, 0, 0);
            contentLayout.Controls.Add(optionsPanel, 0, 1);
            contentLayout.Controls.Add(informationLabel, 0, 2);
            bodyPanel.Controls.Add(contentLayout);

            Controls.Add(bodyPanel);
            Controls.Add(footerPanel);
            Controls.Add(headerPanel);

            AcceptButton = continueButton;
            CancelButton = cancelButton;

            UpdateScopeSummary();
            PdfOcrSettings initialSettings;
            string validationMessage;
            if (!TryBuildSettings(
                    out initialSettings,
                    out validationMessage))
            {
                throw new InvalidOperationException(validationMessage);
            }

            settings = initialSettings;
        }

        public PdfOcrSettings Settings
        {
            get
            {
                return settings == null
                    ? null
                    : settings.Snapshot();
            }
        }

        private static void ValidateDocumentRange(
            int pageCount,
            int currentPageIndex)
        {
            if (pageCount < 1)
            {
                throw new ArgumentOutOfRangeException(
                    "pageCount",
                    "El documento debe contener al menos una página.");
            }

            if (currentPageIndex < 0 ||
                currentPageIndex >= pageCount)
            {
                throw new ArgumentOutOfRangeException(
                    "currentPageIndex",
                    "La página actual no pertenece al documento.");
            }
        }

        private static List<int> NormalizeSelectedPages(
            int pageCount,
            IEnumerable<int> pageIndexes)
        {
            var normalized = new List<int>();
            foreach (var pageIndex in pageIndexes ??
                Enumerable.Empty<int>())
            {
                if (pageIndex < 0 || pageIndex >= pageCount)
                {
                    throw new ArgumentOutOfRangeException(
                        "selectedPageIndexes",
                        "La selección contiene una página que no " +
                        "pertenece al documento.");
                }

                if (!normalized.Contains(pageIndex))
                {
                    normalized.Add(pageIndex);
                }
            }

            normalized.Sort();
            return normalized;
        }

        private void ContinueButton_Click(object sender, EventArgs e)
        {
            PdfOcrSettings validatedSettings;
            string validationMessage;
            if (!TryBuildSettings(
                    out validatedSettings,
                    out validationMessage))
            {
                MessageBox.Show(
                    this,
                    validationMessage,
                    "Opciones de OCR",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            settings = validatedSettings;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void ScopeChanged(object sender, EventArgs e)
        {
            UpdateScopeSummary();
            UpdateSettingsIfValid();
        }

        private void OptionChanged(object sender, EventArgs e)
        {
            UpdateSettingsIfValid();
        }

        private void UpdateScopeSummary()
        {
            if (scopeSummaryLabel == null)
            {
                return;
            }

            if (allPagesRadioButton.Checked)
            {
                scopeSummaryLabel.Text =
                    "Se analizarán las " + pageCount.ToString() +
                    " páginas del documento.";
                return;
            }

            if (selectedPageIndexes.Count > 1 &&
                selectedPagesRadioButton.Checked)
            {
                scopeSummaryLabel.Text =
                    "Se analizarán " +
                    selectedPageIndexes.Count.ToString() +
                    " páginas seleccionadas.";
                return;
            }

            scopeSummaryLabel.Text =
                "Se analizará únicamente la página " +
                (currentPageIndex + 1).ToString() + ".";
        }

        private void UpdateSettingsIfValid()
        {
            PdfOcrSettings updatedSettings;
            string validationMessage;
            if (TryBuildSettings(
                    out updatedSettings,
                    out validationMessage))
            {
                settings = updatedSettings;
            }
        }

        private bool TryBuildSettings(
            out PdfOcrSettings result,
            out string validationMessage)
        {
            result = null;
            validationMessage = string.Empty;

            ICollection<int> pages;
            if (allPagesRadioButton.Checked)
            {
                // The service contract uses null to represent the complete PDF.
                pages = null;
            }
            else if (selectedPageIndexes.Count > 1 &&
                     selectedPagesRadioButton.Checked)
            {
                if (selectedPageIndexes.Count < 2)
                {
                    validationMessage =
                        "Selecciona al menos dos páginas o elige otro ámbito.";
                    return false;
                }

                pages = selectedPageIndexes
                    .Select(index => index + 1)
                    .ToList();
            }
            else if (currentPageRadioButton.Checked)
            {
                pages = new List<int> { currentPageIndex + 1 };
            }
            else
            {
                validationMessage =
                    "Elige qué páginas quieres analizar.";
                return false;
            }

            if (pages != null &&
                (pages.Count == 0 ||
                 pages.Any(page => page < 1 || page > pageCount) ||
                 pages.Distinct().Count() != pages.Count))
            {
                validationMessage =
                    "El ámbito contiene páginas no válidas.";
                return false;
            }

            var nextSettings = new PdfOcrSettings
            {
                Language = "spa+eng",
                AutoOrient = autoOrientCheckBox.Checked,
                AutoDeskew = autoDeskewCheckBox.Checked,
                ReprocessPagesWithText = !skipTextCheckBox.Checked,
                SelectedPages = pages
            };

            result = nextSettings.Snapshot();
            return true;
        }

        private static Panel CreateSectionPanel()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 9),
                BackColor = PaperColor
            };
            panel.Controls.Add(new Panel
            {
                Dock = DockStyle.Left,
                Width = 2,
                BackColor = AccentColor
            });
            panel.Controls.Add(new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = DividerColor
            });
            return panel;
        }

        private static Label CreateSectionCaption(string text)
        {
            return new Label
            {
                Left = 15,
                Top = 8,
                Width = 410,
                Height = 16,
                Text = text,
                ForeColor = AccentTextColor,
                Font = CreateArchitecturalFont(7.25f, true),
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private static RadioButton CreateScopeRadioButton(string text)
        {
            return new RadioButton
            {
                Width = 405,
                Height = 26,
                Margin = Padding.Empty,
                Padding = new Padding(2, 0, 0, 0),
                Text = text,
                ForeColor = TitleColor,
                BackColor = PaperColor,
                Font = CreateUiFont(9.1f, FontStyle.Regular),
                UseVisualStyleBackColor = false,
                AutoCheck = true
            };
        }

        private static CheckBox CreateOptionCheckBox(string text)
        {
            return new CheckBox
            {
                Left = 15,
                Width = 410,
                Height = 26,
                Text = text,
                ForeColor = TitleColor,
                BackColor = PaperColor,
                Font = CreateUiFont(9.1f, FontStyle.Regular),
                UseVisualStyleBackColor = false
            };
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
    }
}

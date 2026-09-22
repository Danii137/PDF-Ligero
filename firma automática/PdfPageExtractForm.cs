using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace FirmaAutomatica
{
    /// <summary>Que se hace con las paginas elegidas.</summary>
    internal enum PdfPageExtractMode
    {
        /// <summary>Un PDF nuevo con las paginas indicadas.</summary>
        Extraer,

        /// <summary>Varios PDF, de tantas paginas cada uno.</summary>
        Dividir
    }

    /// <summary>
    /// Elige que paginas se sacan a otro PDF, o en cuantos trozos se parte el
    /// documento. No escribe nada: solo recoge la decision.
    /// </summary>
    internal sealed class PdfPageExtractForm : Form
    {
        private static readonly Color PaperColor =
            Color.FromArgb(250, 249, 247);
        private static readonly Color WorkspaceColor =
            Color.FromArgb(239, 238, 235);
        private static readonly Color DividerColor =
            Color.FromArgb(211, 209, 204);
        private static readonly Color TitleColor =
            Color.FromArgb(31, 31, 29);
        private static readonly Color MutedColor =
            Color.FromArgb(139, 136, 130);
        private static readonly Color AccentColor =
            Color.FromArgb(238, 91, 61);
        private static readonly Color AccentTextColor =
            Color.FromArgb(185, 68, 45);
        private static readonly Color AccentTintColor =
            Color.FromArgb(251, 236, 231);

        private readonly int pageCount;
        private readonly int currentPageNumber;
        private readonly List<int> selectedPageNumbers;

        private readonly RadioButton rangeRadioButton;
        private readonly RadioButton currentPageRadioButton;
        private readonly RadioButton selectedPagesRadioButton;
        private readonly RadioButton splitRadioButton;
        private readonly TextBox rangeTextBox;
        private readonly NumericUpDown splitSizeInput;
        private readonly Label summaryLabel;
        private readonly Button acceptButton;
        private readonly Button cancelButton;

        public PdfPageExtractForm(
            int pageCount,
            int currentPageNumber,
            IEnumerable<int> selectedPageNumbers)
        {
            if (pageCount < 1)
            {
                throw new ArgumentOutOfRangeException(
                    "pageCount",
                    "El documento debe tener al menos una página.");
            }

            this.pageCount = pageCount;
            this.currentPageNumber = Math.Max(
                1,
                Math.Min(pageCount, currentPageNumber));
            this.selectedPageNumbers = NormalizeSelection(
                pageCount,
                selectedPageNumbers);

            Text = "Extraer o dividir páginas - PDF Ligero";
            AppBranding.ApplyWindowIcon(this);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(492, 452);
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
            headerPanel.Controls.Add(new Label
            {
                Left = 20,
                Top = 8,
                Width = 350,
                Height = 15,
                Text = "PÁGINAS / EXTRAER Y DIVIDIR",
                ForeColor = AccentTextColor,
                Font = CreateArchitecturalFont(7.5f, true),
                TextAlign = ContentAlignment.MiddleLeft
            });
            headerPanel.Controls.Add(new Label
            {
                Left = 20,
                Top = 24,
                Width = 410,
                Height = 28,
                Text = "Sacar páginas a otro PDF",
                ForeColor = TitleColor,
                Font = CreateArchitecturalFont(13f, false),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            });
            headerPanel.Controls.Add(new Panel
            {
                Left = 20,
                Top = 61,
                Width = 42,
                Height = 2,
                BackColor = AccentColor
            });

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
            cancelButton.Left = 278;
            cancelButton.Top = 15;
            cancelButton.DialogResult = DialogResult.Cancel;

            acceptButton = CreateActionButton("Crear", true);
            acceptButton.Left = 384;
            acceptButton.Top = 15;
            acceptButton.AccessibleName = "Crear los PDF";
            acceptButton.Click += AcceptButton_Click;

            footerPanel.Controls.Add(cancelButton);
            footerPanel.Controls.Add(acceptButton);

            var bodyPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(20, 14, 20, 14),
                BackColor = WorkspaceColor
            };

            var panel = CreateSectionPanel();
            panel.Controls.Add(CreateSectionCaption("QUÉ SE SACA"));

            rangeRadioButton = CreateRadioButton(
                "&Páginas:",
                34);
            rangeRadioButton.Width = 84;
            rangeRadioButton.Checked = true;

            rangeTextBox = new TextBox
            {
                Left = 103,
                Top = 33,
                Width = 322,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = PaperColor,
                ForeColor = TitleColor,
                Font = CreateUiFont(9.1f, FontStyle.Regular),
                Text = pageCount == 1
                    ? "1"
                    : "1-" + pageCount.ToString(CultureInfo.CurrentCulture),
                AccessibleName = "Páginas que se sacan",
                AccessibleDescription =
                    "Admite 3, 1-5, o 1-5, 8, 11-13."
            };
            rangeTextBox.TextChanged += OptionChanged;
            rangeTextBox.Enter += delegate
            {
                rangeRadioButton.Checked = true;
            };

            var rangeHintLabel = new Label
            {
                Left = 103,
                Top = 60,
                Width = 322,
                Height = 18,
                Text = "Por ejemplo: 3   ·   1-5   ·   1-5, 8, 11-13",
                ForeColor = MutedColor,
                Font = CreateArchitecturalFont(7.75f, false),
                TextAlign = ContentAlignment.MiddleLeft
            };

            currentPageRadioButton = CreateRadioButton(
                "Página &actual · " +
                this.currentPageNumber.ToString(CultureInfo.CurrentCulture),
                86);

            selectedPagesRadioButton = CreateRadioButton(
                "Páginas &seleccionadas en las miniaturas · " +
                this.selectedPageNumbers.Count.ToString(
                    CultureInfo.CurrentCulture),
                115);
            selectedPagesRadioButton.Enabled =
                this.selectedPageNumbers.Count > 0;

            splitRadioButton = CreateRadioButton(
                "&Dividir en archivos de",
                150);
            splitRadioButton.Width = 160;

            splitSizeInput = new NumericUpDown
            {
                Left = 268,
                Top = 148,
                Width = 62,
                Minimum = 1,
                Maximum = Math.Max(1, pageCount),
                Value = 1,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = PaperColor,
                ForeColor = TitleColor,
                Font = CreateUiFont(9.1f, FontStyle.Regular),
                AccessibleName = "Páginas por archivo"
            };
            splitSizeInput.ValueChanged += OptionChanged;
            splitSizeInput.Enter += delegate
            {
                splitRadioButton.Checked = true;
            };

            panel.Controls.Add(new Label
            {
                Left = 336,
                Top = 151,
                Width = 92,
                Height = 20,
                Text = "páginas",
                ForeColor = TitleColor,
                Font = CreateUiFont(9.1f, FontStyle.Regular),
                TextAlign = ContentAlignment.MiddleLeft
            });

            summaryLabel = new Label
            {
                Left = 15,
                Top = 184,
                Width = 410,
                Height = 34,
                ForeColor = MutedColor,
                Font = CreateArchitecturalFont(7.75f, false),
                TextAlign = ContentAlignment.TopLeft,
                AccessibleName = "Resumen de lo que se va a crear"
            };

            rangeRadioButton.CheckedChanged += OptionChanged;
            currentPageRadioButton.CheckedChanged += OptionChanged;
            selectedPagesRadioButton.CheckedChanged += OptionChanged;
            splitRadioButton.CheckedChanged += OptionChanged;

            panel.Controls.Add(rangeRadioButton);
            panel.Controls.Add(rangeTextBox);
            panel.Controls.Add(rangeHintLabel);
            panel.Controls.Add(currentPageRadioButton);
            panel.Controls.Add(selectedPagesRadioButton);
            panel.Controls.Add(splitRadioButton);
            panel.Controls.Add(splitSizeInput);
            panel.Controls.Add(summaryLabel);

            var informationLabel = new Label
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(2, 10, 2, 0),
                Text =
                    "El PDF abierto no se modifica: las páginas se copian a " +
                    "archivos nuevos, sin rasterizar ni recomprimir las " +
                    "imágenes.",
                ForeColor = MutedColor,
                Font = CreateUiFont(8.5f, FontStyle.Regular),
                TextAlign = ContentAlignment.TopLeft
            };

            var contentLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = WorkspaceColor,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            contentLayout.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 100F));
            contentLayout.RowStyles.Add(
                new RowStyle(SizeType.Absolute, 230F));
            contentLayout.RowStyles.Add(
                new RowStyle(SizeType.Percent, 100F));
            contentLayout.Controls.Add(panel, 0, 0);
            contentLayout.Controls.Add(informationLabel, 0, 1);
            bodyPanel.Controls.Add(contentLayout);

            Controls.Add(bodyPanel);
            Controls.Add(footerPanel);
            Controls.Add(headerPanel);

            AcceptButton = acceptButton;
            CancelButton = cancelButton;
            UpdateSummary();
        }

        /// <summary>Que se pidio hacer.</summary>
        public PdfPageExtractMode Mode
        {
            get
            {
                return splitRadioButton.Checked
                    ? PdfPageExtractMode.Dividir
                    : PdfPageExtractMode.Extraer;
            }
        }

        /// <summary>Paginas elegidas, en orden. Vacio al dividir.</summary>
        public IList<int> Pages
        {
            get { return ResolvePages(); }
        }

        /// <summary>Paginas por archivo al dividir.</summary>
        public int PagesPerFile
        {
            get { return (int)splitSizeInput.Value; }
        }

        private IList<int> ResolvePages()
        {
            if (splitRadioButton.Checked)
            {
                return new List<int>();
            }

            if (currentPageRadioButton.Checked)
            {
                return new List<int> { currentPageNumber };
            }

            if (selectedPagesRadioButton.Checked)
            {
                return new List<int>(selectedPageNumbers);
            }

            return PdfPageRangeParser.Resolve(
                PdfPageSelectionKind.Range,
                rangeTextBox.Text,
                pageCount,
                currentPageNumber);
        }

        private void OptionChanged(object sender, EventArgs e)
        {
            UpdateSummary();
        }

        private void UpdateSummary()
        {
            if (summaryLabel == null)
            {
                return;
            }

            rangeTextBox.Enabled = rangeRadioButton.Checked;
            splitSizeInput.Enabled = splitRadioButton.Checked;

            if (splitRadioButton.Checked)
            {
                var porArchivo = (int)splitSizeInput.Value;
                var archivos = (pageCount + porArchivo - 1) / porArchivo;
                summaryLabel.Text = archivos == 1
                    ? "Saldría un solo archivo: no hay nada que dividir."
                    : "Se crearán " +
                        archivos.ToString(CultureInfo.CurrentCulture) +
                        " archivos junto al original.";
                acceptButton.Enabled = archivos > 1;
                return;
            }

            var paginas = ResolvePages();
            if (paginas.Count == 0)
            {
                summaryLabel.Text =
                    "Escribe qué páginas quieres sacar.";
                acceptButton.Enabled = false;
                return;
            }

            summaryLabel.Text = paginas.Count == 1
                ? "Se creará un PDF con la página " +
                    paginas[0].ToString(CultureInfo.CurrentCulture) + "."
                : "Se creará un PDF con " +
                    paginas.Count.ToString(CultureInfo.CurrentCulture) +
                    " páginas de las " +
                    pageCount.ToString(CultureInfo.CurrentCulture) + ".";
            acceptButton.Enabled = true;
        }

        private void AcceptButton_Click(object sender, EventArgs e)
        {
            if (!splitRadioButton.Checked && ResolvePages().Count == 0)
            {
                MessageBox.Show(
                    this,
                    "Escribe qué páginas quieres sacar.",
                    "Extraer páginas",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        private static List<int> NormalizeSelection(
            int pageCount,
            IEnumerable<int> selection)
        {
            var normalizadas = new List<int>();
            if (selection == null)
            {
                return normalizadas;
            }

            foreach (var pagina in selection)
            {
                if (pagina >= 1 &&
                    pagina <= pageCount &&
                    !normalizadas.Contains(pagina))
                {
                    normalizadas.Add(pagina);
                }
            }

            normalizadas.Sort();
            return normalizadas;
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

        private static RadioButton CreateRadioButton(string text, int top)
        {
            return new RadioButton
            {
                Left = 15,
                Top = top,
                Width = 410,
                Height = 26,
                Text = text,
                ForeColor = TitleColor,
                BackColor = PaperColor,
                Font = CreateUiFont(9.1f, FontStyle.Regular),
                UseVisualStyleBackColor = false
            };
        }

        private static Button CreateActionButton(string text, bool primary)
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

        private static Font CreateArchitecturalFont(float size, bool semibold)
        {
            var style = semibold ? FontStyle.Bold : FontStyle.Regular;
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

        private static Font CreateUiFont(float size, FontStyle style)
        {
            try
            {
                return new Font("Segoe UI", size, style, GraphicsUnit.Point);
            }
            catch
            {
                return new Font(
                    FontFamily.GenericSansSerif,
                    size,
                    style,
                    GraphicsUnit.Point);
            }
        }
    }
}

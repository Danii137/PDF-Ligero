using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace FirmaAutomatica
{
    /// <summary>
    /// Que paginas se exportan como imagen, a que resolucion y en que
    /// formato.
    /// </summary>
    internal sealed class PdfExportImagesForm : Form
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

        private readonly RadioButton allPagesRadioButton;
        private readonly RadioButton currentPageRadioButton;
        private readonly RadioButton rangeRadioButton;
        private readonly TextBox rangeTextBox;
        private readonly ComboBox formatSelector;
        private readonly ComboBox dpiSelector;
        private readonly Label summaryLabel;
        private readonly Button acceptButton;

        public PdfExportImagesForm(int pageCount, int currentPageNumber)
        {
            if (pageCount < 1)
            {
                throw new ArgumentOutOfRangeException("pageCount");
            }

            this.pageCount = pageCount;
            this.currentPageNumber = Math.Max(
                1,
                Math.Min(pageCount, currentPageNumber));

            Text = "Exportar como imagen - PDF Ligero";
            AppBranding.ApplyWindowIcon(this);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(492, 428);
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
                Text = "EXPORTAR / IMAGEN",
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
                Text = "Sacar las páginas como imagen",
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

            var cancelButton = CreateActionButton("Cancelar", false);
            cancelButton.Left = 278;
            cancelButton.Top = 15;
            cancelButton.DialogResult = DialogResult.Cancel;

            acceptButton = CreateActionButton("Exportar", true);
            acceptButton.Left = 384;
            acceptButton.Top = 15;
            acceptButton.DialogResult = DialogResult.OK;

            footerPanel.Controls.Add(cancelButton);
            footerPanel.Controls.Add(acceptButton);

            var bodyPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(20, 14, 20, 14),
                BackColor = WorkspaceColor
            };

            var scopePanel = CreateSectionPanel();
            scopePanel.Controls.Add(CreateSectionCaption("PÁGINAS"));

            allPagesRadioButton = CreateRadioButton(
                "&Todas · " +
                pageCount.ToString(CultureInfo.CurrentCulture),
                30);
            allPagesRadioButton.Checked = true;
            currentPageRadioButton = CreateRadioButton(
                "Página &actual · " +
                this.currentPageNumber.ToString(CultureInfo.CurrentCulture),
                58);
            rangeRadioButton = CreateRadioButton("&Estas:", 86);
            rangeRadioButton.Width = 74;

            rangeTextBox = new TextBox
            {
                Left = 93,
                Top = 85,
                Width = 332,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = PaperColor,
                ForeColor = TitleColor,
                Font = CreateUiFont(9.1f, FontStyle.Regular),
                Text = "1-" + pageCount.ToString(CultureInfo.CurrentCulture),
                AccessibleName = "Páginas que se exportan"
            };
            rangeTextBox.Enter += delegate
            {
                rangeRadioButton.Checked = true;
            };

            scopePanel.Controls.Add(allPagesRadioButton);
            scopePanel.Controls.Add(currentPageRadioButton);
            scopePanel.Controls.Add(rangeRadioButton);
            scopePanel.Controls.Add(rangeTextBox);

            var optionsPanel = CreateSectionPanel();
            optionsPanel.Controls.Add(CreateSectionCaption("IMAGEN"));

            optionsPanel.Controls.Add(CreateFieldCaption("FORMATO", 31));
            formatSelector = CreateFieldSelector(31);
            formatSelector.Items.Add("PNG · sin pérdida, para planos");
            formatSelector.Items.Add("JPG · más ligero, para fotos");
            formatSelector.SelectedIndex = 0;
            formatSelector.AccessibleName = "Formato de imagen";

            optionsPanel.Controls.Add(CreateFieldCaption("RESOLUCIÓN", 63));
            dpiSelector = CreateFieldSelector(63);
            dpiSelector.Items.Add("150 ppp · para ver en pantalla");
            dpiSelector.Items.Add("300 ppp · para imprimir");
            dpiSelector.Items.Add("600 ppp · para ampliar detalles");
            dpiSelector.SelectedIndex = 1;
            dpiSelector.AccessibleName = "Resolución de la imagen";

            optionsPanel.Controls.Add(formatSelector);
            optionsPanel.Controls.Add(dpiSelector);

            summaryLabel = new Label
            {
                Left = 15,
                Top = 96,
                Width = 410,
                Height = 20,
                ForeColor = MutedColor,
                Font = CreateArchitecturalFont(7.75f, false),
                TextAlign = ContentAlignment.MiddleLeft
            };
            optionsPanel.Controls.Add(summaryLabel);

            allPagesRadioButton.CheckedChanged += OptionChanged;
            currentPageRadioButton.CheckedChanged += OptionChanged;
            rangeRadioButton.CheckedChanged += OptionChanged;
            rangeTextBox.TextChanged += OptionChanged;
            dpiSelector.SelectedIndexChanged += OptionChanged;

            var informationLabel = new Label
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(2, 8, 2, 0),
                Text =
                    "Una imagen por página, en una carpeta que eliges. El " +
                    "PDF no se modifica.",
                ForeColor = MutedColor,
                Font = CreateUiFont(8.5f, FontStyle.Regular),
                TextAlign = ContentAlignment.TopLeft
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
                new RowStyle(SizeType.Absolute, 124F));
            contentLayout.RowStyles.Add(
                new RowStyle(SizeType.Absolute, 130F));
            contentLayout.RowStyles.Add(
                new RowStyle(SizeType.Percent, 100F));
            contentLayout.Controls.Add(scopePanel, 0, 0);
            contentLayout.Controls.Add(optionsPanel, 0, 1);
            contentLayout.Controls.Add(informationLabel, 0, 2);
            bodyPanel.Controls.Add(contentLayout);

            Controls.Add(bodyPanel);
            Controls.Add(footerPanel);
            Controls.Add(headerPanel);

            AcceptButton = acceptButton;
            CancelButton = cancelButton;
            UpdateSummary();
        }

        public IList<int> Pages
        {
            get
            {
                if (currentPageRadioButton.Checked)
                {
                    return new List<int> { currentPageNumber };
                }

                if (rangeRadioButton.Checked)
                {
                    return PdfPageRangeParser.Resolve(
                        PdfPageSelectionKind.Range,
                        rangeTextBox.Text,
                        pageCount,
                        currentPageNumber);
                }

                var todas = new List<int>();
                for (var pagina = 1; pagina <= pageCount; pagina++)
                {
                    todas.Add(pagina);
                }

                return todas;
            }
        }

        public PdfExportImageFormat Format
        {
            get
            {
                return formatSelector.SelectedIndex == 1
                    ? PdfExportImageFormat.Jpeg
                    : PdfExportImageFormat.Png;
            }
        }

        public int Dpi
        {
            get
            {
                switch (dpiSelector.SelectedIndex)
                {
                    case 0:
                        return 150;

                    case 2:
                        return 600;

                    default:
                        return 300;
                }
            }
        }

        public int JpegQuality
        {
            get { return 88; }
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
            var paginas = Pages;
            if (paginas.Count == 0)
            {
                summaryLabel.Text = "Escribe qué páginas quieres exportar.";
                acceptButton.Enabled = false;
                return;
            }

            summaryLabel.Text = paginas.Count == 1
                ? "Saldrá 1 imagen a " +
                    Dpi.ToString(CultureInfo.CurrentCulture) + " ppp."
                : "Saldrán " +
                    paginas.Count.ToString(CultureInfo.CurrentCulture) +
                    " imágenes a " +
                    Dpi.ToString(CultureInfo.CurrentCulture) + " ppp.";
            acceptButton.Enabled = true;
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

        private static Label CreateFieldCaption(string text, int top)
        {
            return new Label
            {
                Left = 15,
                Top = top + 4,
                Width = 96,
                Height = 20,
                Text = text,
                ForeColor = AccentTextColor,
                Font = CreateArchitecturalFont(7.5f, true),
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private static ComboBox CreateFieldSelector(int top)
        {
            return new ComboBox
            {
                Left = 113,
                Top = top,
                Width = 312,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = PaperColor,
                ForeColor = TitleColor,
                Font = CreateUiFont(9.1f, FontStyle.Regular)
            };
        }

        private static RadioButton CreateRadioButton(string text, int top)
        {
            return new RadioButton
            {
                Left = 15,
                Top = top,
                Width = 410,
                Height = 24,
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

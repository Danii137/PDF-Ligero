using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace FirmaAutomatica
{
    /// <summary>
    /// Marca de agua y numeracion. Se enseña como quedara el numero de una
    /// hoja concreta, porque las plantillas con llaves no se entienden hasta
    /// que se ven resueltas.
    /// </summary>
    internal sealed class PdfStampForm : Form
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
        private readonly string fileName;

        private readonly CheckBox watermarkCheckBox;
        private readonly ComboBox watermarkTextBox;
        private readonly TrackBar opacityBar;
        private readonly Label opacityLabel;
        private readonly CheckBox numberCheckBox;
        private readonly ComboBox numberFormatBox;
        private readonly ComboBox positionSelector;
        private readonly NumericUpDown firstPageInput;
        private readonly Label previewLabel;
        private readonly Button acceptButton;

        public PdfStampForm(int pageCount, string fileName)
        {
            if (pageCount < 1)
            {
                throw new ArgumentOutOfRangeException("pageCount");
            }

            this.pageCount = pageCount;
            this.fileName = fileName ?? string.Empty;

            Text = "Marca de agua y numeración - PDF Ligero";
            AppBranding.ApplyWindowIcon(this);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(492, 546);
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
                Text = "PÁGINAS / MARCA Y NUMERACIÓN",
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
                Text = "Marcar y numerar las hojas",
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

            acceptButton = CreateActionButton("Aplicar", true);
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

            // --- Marca de agua -------------------------------------------
            var watermarkPanel = CreateSectionPanel();
            watermarkPanel.Controls.Add(
                CreateSectionCaption("MARCA DE AGUA"));

            watermarkCheckBox = new CheckBox
            {
                Left = 15,
                Top = 30,
                Width = 410,
                Height = 24,
                Text = "Poner una &marca en diagonal",
                ForeColor = TitleColor,
                BackColor = PaperColor,
                Font = CreateUiFont(9.1f, FontStyle.Regular),
                UseVisualStyleBackColor = false
            };

            watermarkTextBox = new ComboBox
            {
                Left = 34,
                Top = 58,
                Width = 391,
                DropDownStyle = ComboBoxStyle.DropDown,
                FlatStyle = FlatStyle.Flat,
                BackColor = PaperColor,
                ForeColor = TitleColor,
                Font = CreateUiFont(9.1f, FontStyle.Regular),
                AccessibleName = "Texto de la marca de agua"
            };
            watermarkTextBox.Items.AddRange(new object[]
            {
                "BORRADOR",
                "COPIA",
                "NO VÁLIDO PARA OBRA",
                "PENDIENTE DE VISADO",
                "CONFIDENCIAL"
            });
            watermarkTextBox.Text = "BORRADOR";

            var opacityCaption = new Label
            {
                Left = 34,
                Top = 90,
                Width = 96,
                Height = 20,
                Text = "SE VE",
                ForeColor = AccentTextColor,
                Font = CreateArchitecturalFont(7.5f, true),
                TextAlign = ContentAlignment.MiddleLeft
            };

            opacityBar = new TrackBar
            {
                Left = 96,
                Top = 86,
                Width = 250,
                Minimum = 3,
                Maximum = 45,
                Value = 12,
                TickFrequency = 7,
                BackColor = PaperColor,
                AccessibleName = "Cuánto se ve la marca de agua"
            };
            opacityLabel = new Label
            {
                Left = 352,
                Top = 90,
                Width = 73,
                Height = 20,
                ForeColor = MutedColor,
                Font = CreateArchitecturalFont(7.75f, false),
                TextAlign = ContentAlignment.MiddleLeft
            };

            watermarkPanel.Controls.Add(watermarkCheckBox);
            watermarkPanel.Controls.Add(watermarkTextBox);
            watermarkPanel.Controls.Add(opacityCaption);
            watermarkPanel.Controls.Add(opacityBar);
            watermarkPanel.Controls.Add(opacityLabel);

            // --- Numeracion ----------------------------------------------
            var numberPanel = CreateSectionPanel();
            numberPanel.Controls.Add(CreateSectionCaption("NUMERACIÓN"));

            numberCheckBox = new CheckBox
            {
                Left = 15,
                Top = 30,
                Width = 410,
                Height = 24,
                Text = "&Numerar las hojas",
                ForeColor = TitleColor,
                BackColor = PaperColor,
                Font = CreateUiFont(9.1f, FontStyle.Regular),
                UseVisualStyleBackColor = false,
                Checked = true
            };

            numberFormatBox = new ComboBox
            {
                Left = 34,
                Top = 58,
                Width = 391,
                DropDownStyle = ComboBoxStyle.DropDown,
                FlatStyle = FlatStyle.Flat,
                BackColor = PaperColor,
                ForeColor = TitleColor,
                Font = CreateUiFont(9.1f, FontStyle.Regular),
                AccessibleName = "Formato del número de página"
            };
            numberFormatBox.Items.AddRange(new object[]
            {
                "Página {n} de {total}",
                "{n}",
                "{n} / {total}",
                "{archivo} · hoja {n} de {total}"
            });
            numberFormatBox.Text = "Página {n} de {total}";

            positionSelector = new ComboBox
            {
                Left = 34,
                Top = 90,
                Width = 226,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = PaperColor,
                ForeColor = TitleColor,
                Font = CreateUiFont(9.1f, FontStyle.Regular),
                AccessibleName = "Dónde va el número"
            };
            positionSelector.Items.AddRange(new object[]
            {
                "Pie, a la derecha",
                "Pie, centrado",
                "Pie, a la izquierda",
                "Cabecera, a la derecha",
                "Cabecera, centrada",
                "Cabecera, a la izquierda"
            });
            positionSelector.SelectedIndex = 0;

            var firstPageCaption = new Label
            {
                Left = 268,
                Top = 92,
                Width = 84,
                Height = 20,
                Text = "DESDE LA HOJA",
                ForeColor = AccentTextColor,
                Font = CreateArchitecturalFont(7.5f, true),
                TextAlign = ContentAlignment.MiddleLeft
            };

            firstPageInput = new NumericUpDown
            {
                Left = 358,
                Top = 89,
                Width = 67,
                Minimum = 1,
                Maximum = pageCount,
                Value = 1,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = PaperColor,
                ForeColor = TitleColor,
                Font = CreateUiFont(9.1f, FontStyle.Regular),
                AccessibleName = "Primera hoja numerada",
                AccessibleDescription =
                    "La portada de una memoria no suele llevar número."
            };

            previewLabel = new Label
            {
                Left = 34,
                Top = 122,
                Width = 391,
                Height = 20,
                ForeColor = MutedColor,
                Font = CreateArchitecturalFont(7.75f, false),
                TextAlign = ContentAlignment.MiddleLeft,
                AccessibleName = "Cómo quedará el número"
            };

            numberPanel.Controls.Add(numberCheckBox);
            numberPanel.Controls.Add(numberFormatBox);
            numberPanel.Controls.Add(positionSelector);
            numberPanel.Controls.Add(firstPageCaption);
            numberPanel.Controls.Add(firstPageInput);
            numberPanel.Controls.Add(previewLabel);

            watermarkCheckBox.CheckedChanged += OptionChanged;
            watermarkTextBox.TextChanged += OptionChanged;
            opacityBar.ValueChanged += OptionChanged;
            numberCheckBox.CheckedChanged += OptionChanged;
            numberFormatBox.TextChanged += OptionChanged;
            positionSelector.SelectedIndexChanged += OptionChanged;
            firstPageInput.ValueChanged += OptionChanged;

            var informationLabel = new Label
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(2, 8, 2, 0),
                Text =
                    "La marca va debajo del contenido, para no tapar el " +
                    "texto ni las líneas. Se crea una copia: el PDF abierto " +
                    "no se modifica.",
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
                new RowStyle(SizeType.Absolute, 130F));
            contentLayout.RowStyles.Add(
                new RowStyle(SizeType.Absolute, 162F));
            contentLayout.RowStyles.Add(
                new RowStyle(SizeType.Percent, 100F));
            contentLayout.Controls.Add(watermarkPanel, 0, 0);
            contentLayout.Controls.Add(numberPanel, 0, 1);
            contentLayout.Controls.Add(informationLabel, 0, 2);
            bodyPanel.Controls.Add(contentLayout);

            Controls.Add(bodyPanel);
            Controls.Add(footerPanel);
            Controls.Add(headerPanel);

            AcceptButton = acceptButton;
            CancelButton = cancelButton;
            UpdatePreview();
        }

        public PdfStampSettings Settings
        {
            get
            {
                return new PdfStampSettings
                {
                    WatermarkText = watermarkCheckBox.Checked
                        ? watermarkTextBox.Text
                        : string.Empty,
                    WatermarkOpacityPercent = opacityBar.Value,
                    NumberFormat = numberCheckBox.Checked
                        ? numberFormatBox.Text
                        : string.Empty,
                    NumberPosition = (PdfNumberPosition)
                        Math.Max(0, positionSelector.SelectedIndex),
                    FirstNumberedPage = (int)firstPageInput.Value,
                    StartNumberingAt = 1
                };
            }
        }

        private void OptionChanged(object sender, EventArgs e)
        {
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            if (previewLabel == null)
            {
                return;
            }

            watermarkTextBox.Enabled = watermarkCheckBox.Checked;
            opacityBar.Enabled = watermarkCheckBox.Checked;
            numberFormatBox.Enabled = numberCheckBox.Checked;
            positionSelector.Enabled = numberCheckBox.Checked;
            firstPageInput.Enabled = numberCheckBox.Checked;

            opacityLabel.Text =
                opacityBar.Value.ToString(CultureInfo.CurrentCulture) + " %";

            var ajustes = Settings;
            var hayMarca = ajustes.HasWatermark;
            var hayNumeros = ajustes.HasNumbering;
            acceptButton.Enabled = hayMarca || hayNumeros;

            if (!hayNumeros)
            {
                previewLabel.Text = hayMarca
                    ? "Sin numeración."
                    : "Marca ni número: no hay nada que aplicar.";
                return;
            }

            // Se enseña la ultima hoja, que es donde se ve si las cuentas
            // cuadran cuando la numeracion no empieza en la primera.
            var muestra = PdfStampService.Preview(
                ajustes,
                pageCount,
                pageCount,
                fileName);
            previewLabel.Text = "En la última hoja se leerá:  " + muestra;
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

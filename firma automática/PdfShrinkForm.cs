using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace FirmaAutomatica
{
    /// <summary>
    /// Cuanto se aprieta al reducir el tamaño. Tres opciones con nombre de lo
    /// que se va a hacer con el archivo, no de parametros tecnicos.
    /// </summary>
    internal sealed class PdfShrinkForm : Form
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

        private readonly RadioButton correoRadioButton;
        private readonly RadioButton equilibradoRadioButton;
        private readonly RadioButton calidadRadioButton;

        public PdfShrinkForm(long currentBytes)
        {
            Text = "Reducir el tamaño - PDF Ligero";
            AppBranding.ApplyWindowIcon(this);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(492, 388);
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
                Text = "ARCHIVO / REDUCIR TAMAÑO",
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
                Text = "Dejarlo ligero para enviarlo",
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

            var acceptButton = CreateActionButton("Reducir", true);
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

            var panel = CreateSectionPanel();
            panel.Controls.Add(CreateSectionCaption("TAMAÑO ACTUAL"));
            panel.Controls.Add(new Label
            {
                Left = 15,
                Top = 30,
                Width = 410,
                Height = 24,
                Text = DescribeSize(currentBytes),
                ForeColor = TitleColor,
                Font = CreateArchitecturalFont(11f, false),
                TextAlign = ContentAlignment.MiddleLeft
            });

            correoRadioButton = AddOption(
                panel,
                "Para &enviar por correo",
                "Imágenes a 150 ppp. Se lee bien en pantalla.",
                62);
            equilibradoRadioButton = AddOption(
                panel,
                "E&quilibrado",
                "Imágenes a 200 ppp. Vale para imprimir en A4.",
                112);
            equilibradoRadioButton.Checked = true;
            calidadRadioButton = AddOption(
                panel,
                "&Conservar calidad",
                "Imágenes a 300 ppp. Solo quita lo que sobra de más.",
                162);

            var informationLabel = new Label
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(2, 10, 2, 0),
                Text =
                    "Solo se tocan las imágenes que van sobradas de " +
                    "resolución. El texto y las líneas de los planos se " +
                    "copian tal cual. El PDF abierto no se modifica: la " +
                    "copia reducida es un archivo aparte.",
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
                new RowStyle(SizeType.Absolute, 218F));
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
        }

        public PdfShrinkSettings Settings
        {
            get
            {
                if (correoRadioButton.Checked)
                {
                    return new PdfShrinkSettings
                    {
                        TargetImageDpi = 150,
                        JpegQuality = 72
                    };
                }

                if (calidadRadioButton.Checked)
                {
                    return new PdfShrinkSettings
                    {
                        TargetImageDpi = 300,
                        JpegQuality = 88
                    };
                }

                return new PdfShrinkSettings
                {
                    TargetImageDpi = 200,
                    JpegQuality = 80
                };
            }
        }

        public static string DescribeSize(long bytes)
        {
            if (bytes >= 1024L * 1024L)
            {
                return (bytes / 1024D / 1024D).ToString(
                    "0.0",
                    CultureInfo.CurrentCulture) + " MB";
            }

            if (bytes >= 1024L)
            {
                return (bytes / 1024D).ToString(
                    "0",
                    CultureInfo.CurrentCulture) + " kB";
            }

            return bytes.ToString(CultureInfo.CurrentCulture) + " bytes";
        }

        /// <summary>
        /// Una opcion con su explicacion debajo, en gris pequeño: lo que
        /// distingue "equilibrado" de "para enviar por correo" no cabe en el
        /// nombre.
        /// </summary>
        private static RadioButton AddOption(
            Panel panel,
            string text,
            string description,
            int top)
        {
            var radio = new RadioButton
            {
                Left = 15,
                Top = top,
                Width = 410,
                Height = 24,
                Text = text,
                ForeColor = TitleColor,
                BackColor = PaperColor,
                Font = CreateUiFont(9.4f, FontStyle.Regular),
                UseVisualStyleBackColor = false,
                AccessibleDescription = description
            };
            panel.Controls.Add(radio);
            panel.Controls.Add(new Label
            {
                Left = 34,
                Top = top + 23,
                Width = 391,
                Height = 18,
                Text = description,
                ForeColor = MutedColor,
                BackColor = PaperColor,
                Font = CreateArchitecturalFont(7.75f, false),
                TextAlign = ContentAlignment.MiddleLeft
            });
            return radio;
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

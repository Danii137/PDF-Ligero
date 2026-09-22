using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace FirmaAutomatica
{
    /// <summary>
    /// Los archivos que lleva dentro el PDF: verlos, sacarlos y añadir otros.
    /// </summary>
    internal sealed class PdfAttachmentsForm : Form
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

        private readonly string pdfPath;
        private readonly ListView list;
        private readonly Button extractButton;
        private readonly Label emptyLabel;

        private IList<PdfAttachmentInfo> attachments;

        public PdfAttachmentsForm(string pdfPath, string documentName)
        {
            if (string.IsNullOrWhiteSpace(pdfPath))
            {
                throw new ArgumentNullException("pdfPath");
            }

            this.pdfPath = pdfPath;

            Text = "Archivos adjuntos - PDF Ligero";
            AppBranding.ApplyWindowIcon(this);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(600, 440);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(520, 360);
            MaximizeBox = true;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = WorkspaceColor;
            Font = CreateUiFont(9.25f, FontStyle.Regular);

            var headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 84,
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
                Width = 420,
                Height = 15,
                Text = "DOCUMENTO / ADJUNTOS",
                ForeColor = AccentTextColor,
                Font = CreateArchitecturalFont(7.5f, true),
                TextAlign = ContentAlignment.MiddleLeft
            });
            headerPanel.Controls.Add(new Label
            {
                Left = 20,
                Top = 24,
                Width = 540,
                Height = 26,
                Text = "Archivos que lleva dentro",
                ForeColor = TitleColor,
                Font = CreateArchitecturalFont(13f, false),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            });
            headerPanel.Controls.Add(new Label
            {
                Left = 20,
                Top = 50,
                Width = 540,
                Height = 20,
                Text = documentName ?? string.Empty,
                ForeColor = MutedColor,
                Font = CreateArchitecturalFont(8f, false),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            });
            headerPanel.Controls.Add(new Panel
            {
                Left = 20,
                Top = 72,
                Width = 42,
                Height = 2,
                BackColor = AccentColor
            });

            var footerPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 60,
                BackColor = PaperColor
            };
            footerPanel.Controls.Add(new Panel
            {
                Dock = DockStyle.Top,
                Height = 1,
                BackColor = DividerColor
            });

            extractButton = CreateActionButton("Guardar fuera", false);
            extractButton.Width = 124;
            extractButton.Left = 20;
            extractButton.Top = 13;
            extractButton.Enabled = false;
            extractButton.Click += delegate { ExtractSelected(); };

            var attachButton = CreateActionButton("Adjuntar…", false);
            attachButton.Width = 112;
            attachButton.Left = 152;
            attachButton.Top = 13;
            attachButton.Click += delegate { ChooseFilesToAttach(); };
            footerPanel.Controls.Add(attachButton);

            var closeButton = CreateActionButton("Cerrar", true);
            closeButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            closeButton.Left = ClientSize.Width - 116;
            closeButton.Top = 13;
            closeButton.DialogResult = DialogResult.OK;

            footerPanel.Controls.Add(extractButton);
            footerPanel.Controls.Add(closeButton);

            var bodyPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(20, 14, 20, 14),
                BackColor = WorkspaceColor
            };

            list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                HideSelection = false,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = PaperColor,
                ForeColor = TitleColor,
                Font = CreateUiFont(9f, FontStyle.Regular),
                AccessibleName = "Archivos adjuntos"
            };
            list.Columns.Add("Archivo", 250);
            list.Columns.Add("Tamaño", 90);
            list.Columns.Add("Modificado", 130);
            list.SelectedIndexChanged += delegate
            {
                extractButton.Enabled = list.SelectedItems.Count > 0;
            };
            list.DoubleClick += delegate { ExtractSelected(); };

            emptyLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text =
                    "Este PDF no lleva ningún archivo dentro.\r\n\r\n" +
                    "Puedes meter uno con el botón de abajo: el DWG, la hoja " +
                    "de cálculo o el justificante viajan entonces con el " +
                    "documento.",
                ForeColor = MutedColor,
                Font = CreateUiFont(9.25f, FontStyle.Regular),
                TextAlign = ContentAlignment.TopLeft,
                Visible = false
            };

            bodyPanel.Controls.Add(list);
            bodyPanel.Controls.Add(emptyLabel);

            Controls.Add(bodyPanel);
            Controls.Add(footerPanel);
            Controls.Add(headerPanel);

            AcceptButton = closeButton;
            CancelButton = closeButton;
            Reload();
        }

        private void Reload()
        {
            attachments = PdfAttachmentService.List(pdfPath);
            list.BeginUpdate();
            try
            {
                list.Items.Clear();
                foreach (var adjunto in attachments)
                {
                    var fila = new ListViewItem(adjunto.Name);
                    fila.SubItems.Add(adjunto.DescribeLength());
                    fila.SubItems.Add(
                        adjunto.Modified.HasValue
                            ? adjunto.Modified.Value.ToString(
                                "d 'de' MMMM 'de' yyyy",
                                CultureInfo.CurrentCulture)
                            : string.Empty);
                    fila.ToolTipText = adjunto.Description;
                    list.Items.Add(fila);
                }
            }
            finally
            {
                list.EndUpdate();
            }

            var hay = attachments.Count > 0;
            list.Visible = hay;
            emptyLabel.Visible = !hay;
            extractButton.Enabled = false;
        }

        /// <summary>
        /// Archivos que se han elegido para meter dentro del PDF. El propio
        /// cuadro no toca el documento: quien lo hace es el visor, que sabe
        /// crear una revision recuperable.
        /// </summary>
        public IList<string> FilesToAttach { get; private set; }

        private void ChooseFilesToAttach()
        {
            using (var abrir = new OpenFileDialog())
            {
                abrir.Title = "Archivos que meter dentro del PDF";
                abrir.Filter = "Todos los archivos (*.*)|*.*";
                abrir.Multiselect = true;
                if (abrir.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                FilesToAttach = new List<string>(abrir.FileNames);
            }

            DialogResult = DialogResult.Yes;
            Close();
        }

        private void ExtractSelected()
        {
            if (list.SelectedItems.Count == 0)
            {
                return;
            }

            var indice = list.SelectedItems[0].Index;
            if (indice < 0 || indice >= attachments.Count)
            {
                return;
            }

            var adjunto = attachments[indice];
            using (var guardar = new SaveFileDialog())
            {
                guardar.Title = "Guardar el adjunto fuera del PDF";
                guardar.FileName = adjunto.Name;
                guardar.OverwritePrompt = true;
                var extension = Path.GetExtension(adjunto.Name);
                guardar.Filter = string.IsNullOrEmpty(extension)
                    ? "Todos los archivos (*.*)|*.*"
                    : "Archivos " + extension + "|*" + extension +
                        "|Todos los archivos (*.*)|*.*";
                if (guardar.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    if (!PdfAttachmentService.Extract(
                            pdfPath,
                            adjunto.Name,
                            guardar.FileName))
                    {
                        MessageBox.Show(
                            this,
                            "Ese adjunto ya no está dentro del documento.",
                            "Archivos adjuntos",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        Reload();
                        return;
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Write("No se pudo sacar el adjunto: " + ex);
                    MessageBox.Show(
                        this,
                        "No se pudo guardar el adjunto.\r\n\r\n" +
                        ex.GetBaseException().Message,
                        "Archivos adjuntos",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
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

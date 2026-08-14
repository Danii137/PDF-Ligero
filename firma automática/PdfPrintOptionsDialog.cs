using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.Globalization;
using System.Windows.Forms;
using PdfiumViewer;

namespace FirmaAutomatica
{
    /// <summary>
    /// Opciones de impresion propias: impresora, papel, color, doble cara,
    /// copias y que paginas.
    ///
    /// Se hace aqui y no con el cuadro de Windows porque ese no deja elegir
    /// paginas sueltas ni solo pares o impares, que es justo lo que hace falta
    /// para reimprimir una memoria sin gastar el resto.
    ///
    /// Solo se ofrece lo que la impresora elegida admite: si no imprime en
    /// color o no tiene doble cara, esas opciones se deshabilitan en vez de
    /// mandar un trabajo que saldria mal.
    /// </summary>
    internal sealed class PdfPrintOptionsDialog : Form
    {
        private static readonly Color SurfaceColor =
            Color.FromArgb(250, 249, 247);
        private static readonly Color FieldColor = Color.White;
        private static readonly Color DividerColor =
            Color.FromArgb(211, 209, 204);
        private static readonly Color TitleColor = Color.FromArgb(31, 31, 29);
        private static readonly Color BodyColor = Color.FromArgb(96, 94, 90);
        private static readonly Color MutedColor =
            Color.FromArgb(139, 136, 130);
        private static readonly Color AccentColor =
            Color.FromArgb(238, 91, 61);
        private static readonly Color AccentTextColor =
            Color.FromArgb(185, 68, 45);

        private readonly PdfDocument document;
        private readonly string displayName;
        private readonly int currentPageNumber;

        private readonly ComboBox printerSelector;
        private readonly ComboBox paperSelector;
        private readonly ComboBox orientationSelector;
        private readonly ComboBox colorSelector;
        private readonly ComboBox duplexSelector;
        private readonly NumericUpDown copiesInput;
        private readonly ComboBox pagesSelector;
        private readonly TextBox rangeInput;
        private readonly Label summaryLabel;
        private readonly Button printButton;
        private readonly ToolTip toolTip;

        private bool loading;

        public PdfPrintOptionsDialog(
            PdfDocument document,
            string displayName,
            int currentPageNumber)
        {
            if (document == null)
            {
                throw new ArgumentNullException("document");
            }

            this.document = document;
            this.displayName = displayName ?? "Documento";
            this.currentPageNumber = Math.Max(1, currentPageNumber);

            Text = "Imprimir";
            AppBranding.ApplyWindowIcon(this);
            ClientSize = new Size(560, 486);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = SurfaceColor;
            ForeColor = TitleColor;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);

            toolTip = new ToolTip
            {
                InitialDelay = 280,
                ShowAlways = true
            };

            var eyebrow = new Label
            {
                Left = 18,
                Top = 14,
                Width = 320,
                Height = 15,
                Text = "IMPRIMIR / OPCIONES",
                ForeColor = AccentTextColor,
                Font = new Font("Bahnschrift", 7.5f, FontStyle.Bold)
            };

            var title = new Label
            {
                Left = 18,
                Top = 30,
                Width = 420,
                Height = 28,
                Text = "Imprimir el documento",
                ForeColor = TitleColor,
                Font = new Font("Bahnschrift", 14f, FontStyle.Regular)
            };

            var accent = new Panel
            {
                Left = 18,
                Top = 62,
                Width = 34,
                Height = 2,
                BackColor = AccentColor
            };

            var pageCountLabel = new Label
            {
                Left = 340,
                Top = 34,
                Width = 202,
                Height = 18,
                Text = document.PageCount.ToString(CultureInfo.CurrentCulture) +
                    (document.PageCount == 1 ? " página" : " páginas"),
                ForeColor = MutedColor,
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular)
            };

            printerSelector = CreateCombo(18, 96, 524);
            paperSelector = CreateCombo(18, 154, 254);
            orientationSelector = CreateCombo(288, 154, 254);
            colorSelector = CreateCombo(18, 212, 254);
            duplexSelector = CreateCombo(288, 212, 254);

            copiesInput = new NumericUpDown
            {
                Left = 18,
                Top = 270,
                Width = 100,
                Height = 26,
                Minimum = 1M,
                Maximum = 99M,
                Value = 1M,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = FieldColor,
                ForeColor = TitleColor,
                AccessibleName = "Número de copias"
            };

            pagesSelector = CreateCombo(134, 270, 180);
            pagesSelector.Items.AddRange(new object[]
            {
                "Todas las páginas",
                "Solo la página actual",
                "Intervalo concreto",
                "Solo las impares",
                "Solo las pares"
            });
            pagesSelector.SelectedIndex = 0;
            pagesSelector.SelectedIndexChanged += delegate { RefreshSummary(); };

            rangeInput = new TextBox
            {
                Left = 330,
                Top = 270,
                Width = 212,
                Height = 26,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = FieldColor,
                ForeColor = TitleColor,
                Enabled = false,
                AccessibleName = "Intervalo de páginas"
            };
            rangeInput.TextChanged += delegate { RefreshSummary(); };
            toolTip.SetToolTip(
                rangeInput,
                "Por ejemplo: 1-5, 8, 11-13");

            summaryLabel = new Label
            {
                Left = 18,
                Top = 318,
                Width = 524,
                Height = 34,
                ForeColor = AccentTextColor,
                BackColor = Color.FromArgb(251, 236, 231),
                Padding = new Padding(9, 0, 9, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular)
            };

            var noteLabel = new Label
            {
                Left = 18,
                Top = 360,
                Width = 524,
                Height = 46,
                Text =
                    "Solo se ofrecen las opciones que admite la impresora " +
                    "elegida. El PDF no se modifica al imprimir.",
                ForeColor = BodyColor,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular)
            };

            var cancelButton = new Button
            {
                Left = 330,
                Top = 428,
                Width = 100,
                Height = 32,
                Text = "Cancelar",
                DialogResult = DialogResult.Cancel,
                FlatStyle = FlatStyle.Flat,
                BackColor = SurfaceColor,
                ForeColor = TitleColor
            };
            cancelButton.FlatAppearance.BorderColor = DividerColor;

            printButton = new Button
            {
                Left = 442,
                Top = 428,
                Width = 100,
                Height = 32,
                Text = "Imprimir",
                FlatStyle = FlatStyle.Flat,
                BackColor = TitleColor,
                ForeColor = Color.White
            };
            printButton.FlatAppearance.BorderSize = 0;
            printButton.Click += PrintButton_Click;

            Controls.Add(eyebrow);
            Controls.Add(title);
            Controls.Add(accent);
            Controls.Add(pageCountLabel);
            Controls.Add(CreateSectionLabel("IMPRESORA", 18, 78, 200));
            Controls.Add(printerSelector);
            Controls.Add(CreateSectionLabel("TAMAÑO DE PAPEL", 18, 136, 200));
            Controls.Add(paperSelector);
            Controls.Add(CreateSectionLabel("ORIENTACIÓN", 288, 136, 200));
            Controls.Add(orientationSelector);
            Controls.Add(CreateSectionLabel("COLOR", 18, 194, 200));
            Controls.Add(colorSelector);
            Controls.Add(CreateSectionLabel("DOBLE CARA", 288, 194, 200));
            Controls.Add(duplexSelector);
            Controls.Add(CreateSectionLabel("COPIAS", 18, 252, 100));
            Controls.Add(copiesInput);
            Controls.Add(CreateSectionLabel("PÁGINAS", 134, 252, 180));
            Controls.Add(pagesSelector);
            Controls.Add(CreateSectionLabel("INTERVALO", 330, 252, 200));
            Controls.Add(rangeInput);
            Controls.Add(summaryLabel);
            Controls.Add(noteLabel);
            Controls.Add(cancelButton);
            Controls.Add(printButton);

            AcceptButton = printButton;
            CancelButton = cancelButton;

            LoadPrinters();
            RefreshSummary();
        }

        private static Label CreateSectionLabel(
            string texto,
            int left,
            int top,
            int width)
        {
            return new Label
            {
                Left = left,
                Top = top,
                Width = width,
                Height = 15,
                Text = texto,
                ForeColor = MutedColor,
                Font = new Font("Bahnschrift", 7.5f, FontStyle.Bold)
            };
        }

        private ComboBox CreateCombo(int left, int top, int width)
        {
            return new ComboBox
            {
                Left = left,
                Top = top,
                Width = width,
                Height = 26,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = FieldColor,
                ForeColor = TitleColor
            };
        }

        private void LoadPrinters()
        {
            loading = true;
            try
            {
                var predeterminada = new PrinterSettings().PrinterName;
                foreach (string impresora in PrinterSettings.InstalledPrinters)
                {
                    printerSelector.Items.Add(impresora);
                }

                if (printerSelector.Items.Count == 0)
                {
                    printerSelector.Enabled = false;
                    printButton.Enabled = false;
                    return;
                }

                var indice = printerSelector.Items.IndexOf(predeterminada);
                printerSelector.SelectedIndex = indice >= 0 ? indice : 0;
                printerSelector.SelectedIndexChanged +=
                    delegate { LoadPrinterCapabilities(); };
            }
            finally
            {
                loading = false;
            }

            LoadPrinterCapabilities();
        }

        /// <summary>
        /// Rellena papel, color y doble cara con lo que admite la impresora
        /// elegida, y deshabilita lo que no.
        /// </summary>
        private void LoadPrinterCapabilities()
        {
            if (loading)
            {
                return;
            }

            loading = true;
            try
            {
                var settings = CreateSettings();

                paperSelector.Items.Clear();
                foreach (PaperSize papel in settings.PaperSizes)
                {
                    paperSelector.Items.Add(papel.PaperName);
                }

                if (paperSelector.Items.Count == 0)
                {
                    paperSelector.Items.Add("Predeterminado");
                }

                var papelActual = settings.DefaultPageSettings.PaperSize;
                var indicePapel = papelActual == null
                    ? -1
                    : paperSelector.Items.IndexOf(papelActual.PaperName);
                paperSelector.SelectedIndex = indicePapel >= 0 ? indicePapel : 0;

                orientationSelector.Items.Clear();
                orientationSelector.Items.AddRange(new object[]
                {
                    "Vertical",
                    "Horizontal"
                });
                orientationSelector.SelectedIndex =
                    settings.DefaultPageSettings.Landscape ? 1 : 0;

                colorSelector.Items.Clear();
                if (settings.SupportsColor)
                {
                    colorSelector.Items.AddRange(new object[]
                    {
                        "Color",
                        "Blanco y negro"
                    });
                    colorSelector.SelectedIndex =
                        settings.DefaultPageSettings.Color ? 0 : 1;
                    colorSelector.Enabled = true;
                }
                else
                {
                    colorSelector.Items.Add("Blanco y negro");
                    colorSelector.SelectedIndex = 0;
                    colorSelector.Enabled = false;
                }

                duplexSelector.Items.Clear();
                if (settings.CanDuplex)
                {
                    duplexSelector.Items.AddRange(new object[]
                    {
                        "Una cara",
                        "Doble cara (giro largo)",
                        "Doble cara (giro corto)"
                    });
                    duplexSelector.SelectedIndex = 0;
                    duplexSelector.Enabled = true;
                }
                else
                {
                    duplexSelector.Items.Add("Una cara");
                    duplexSelector.SelectedIndex = 0;
                    duplexSelector.Enabled = false;
                }

                copiesInput.Maximum = Math.Max(1, settings.MaximumCopies);
            }
            catch (Exception)
            {
                // Una impresora que no responde no debe tumbar el cuadro: se
                // deja lo que haya y se avisa al intentar imprimir.
            }
            finally
            {
                loading = false;
                RefreshSummary();
            }
        }

        private PrinterSettings CreateSettings()
        {
            var settings = new PrinterSettings();
            if (printerSelector.SelectedItem != null)
            {
                settings.PrinterName = printerSelector.SelectedItem.ToString();
            }

            return settings;
        }

        private PdfPageSelectionKind SelectedKind()
        {
            switch (pagesSelector.SelectedIndex)
            {
                case 1:
                    return PdfPageSelectionKind.Current;
                case 2:
                    return PdfPageSelectionKind.Range;
                case 3:
                    return PdfPageSelectionKind.Odd;
                case 4:
                    return PdfPageSelectionKind.Even;
                default:
                    return PdfPageSelectionKind.All;
            }
        }

        private void RefreshSummary()
        {
            var kind = SelectedKind();
            rangeInput.Enabled = kind == PdfPageSelectionKind.Range;

            var paginas = PdfPageRangeParser.Resolve(
                kind,
                rangeInput.Text,
                document.PageCount,
                currentPageNumber);

            summaryLabel.Text = PdfPageRangeParser.Describe(
                paginas,
                document.PageCount);
            printButton.Enabled = paginas.Count > 0 &&
                printerSelector.Items.Count > 0;
        }

        private void PrintButton_Click(object sender, EventArgs e)
        {
            var paginas = PdfPageRangeParser.Resolve(
                SelectedKind(),
                rangeInput.Text,
                document.PageCount,
                currentPageNumber);
            if (paginas.Count == 0)
            {
                return;
            }

            try
            {
                UseWaitCursor = true;
                using (var trabajo = new PdfSelectedPagesPrintDocument(
                    document,
                    paginas))
                {
                    var settings = CreateSettings();
                    settings.Copies = (short)copiesInput.Value;

                    if (colorSelector.Enabled)
                    {
                        settings.DefaultPageSettings.Color =
                            colorSelector.SelectedIndex == 0;
                    }

                    if (duplexSelector.Enabled)
                    {
                        settings.Duplex = duplexSelector.SelectedIndex == 1
                            ? Duplex.Vertical
                            : (duplexSelector.SelectedIndex == 2
                                ? Duplex.Horizontal
                                : Duplex.Simplex);
                    }

                    settings.DefaultPageSettings.Landscape =
                        orientationSelector.SelectedIndex == 1;

                    var papel = BuscarPapel(settings);
                    if (papel != null)
                    {
                        settings.DefaultPageSettings.PaperSize = papel;
                    }

                    trabajo.DocumentName = displayName;
                    trabajo.PrinterSettings = settings;
                    trabajo.DefaultPageSettings = settings.DefaultPageSettings;
                    trabajo.Print();
                }

                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                AppLog.Write("No se pudo imprimir: " + ex);
                MessageBox.Show(
                    this,
                    "No se pudo imprimir.\r\n\r\n" +
                    ex.GetBaseException().Message,
                    "PDF Ligero",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                UseWaitCursor = false;
            }
        }

        private PaperSize BuscarPapel(PrinterSettings settings)
        {
            if (paperSelector.SelectedItem == null)
            {
                return null;
            }

            var nombre = paperSelector.SelectedItem.ToString();
            foreach (PaperSize papel in settings.PaperSizes)
            {
                if (string.Equals(papel.PaperName, nombre, StringComparison.Ordinal))
                {
                    return papel;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Trabajo de impresion de una lista concreta de paginas.
    ///
    /// El documento de impresion que trae PdfiumViewer imprime seguido desde una
    /// pagina hasta otra, asi que no vale para "solo las impares" ni para
    /// "1-5, 8, 11-13". Aqui se lleva la lista y se dibuja pagina a pagina.
    /// </summary>
    internal sealed class PdfSelectedPagesPrintDocument : PrintDocument
    {
        private readonly PdfDocument document;
        private readonly IList<int> pages;
        private int siguiente;

        public PdfSelectedPagesPrintDocument(
            PdfDocument document,
            IList<int> pages)
        {
            if (document == null)
            {
                throw new ArgumentNullException("document");
            }
            if (pages == null || pages.Count == 0)
            {
                throw new ArgumentException(
                    "No hay paginas que imprimir.",
                    "pages");
            }

            this.document = document;
            this.pages = pages;
            OriginAtMargins = false;
        }

        protected override void OnBeginPrint(PrintEventArgs e)
        {
            siguiente = 0;
            base.OnBeginPrint(e);
        }

        protected override void OnPrintPage(PrintPageEventArgs e)
        {
            base.OnPrintPage(e);
            if (siguiente >= pages.Count)
            {
                e.HasMorePages = false;
                return;
            }

            var pagina = pages[siguiente++] - 1;
            var area = e.PageBounds;

            document.Render(
                pagina,
                e.Graphics,
                e.Graphics.DpiX,
                e.Graphics.DpiY,
                area,
                PdfRenderFlags.ForPrinting | PdfRenderFlags.Annotations);

            e.HasMorePages = siguiente < pages.Count;
        }
    }
}

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FirmaAutomatica
{
    /// <summary>
    /// Values selected in the controlled text editor. The model deliberately
    /// contains no iTextSharp types, so the UI stays independent from the PDF
    /// writer and can be exercised without opening a document.
    /// </summary>
    internal sealed class PdfTextEditDialogState
    {
        public const string HelveticaFontName = "Helvetica";
        public const string TimesFontName = "Times";
        public const string CourierFontName = "Courier";

        public PdfTextEditDialogState()
        {
            Text = string.Empty;
            BaseFontName = HelveticaFontName;
            FontSizePoints = 11M;
            AutoFit = true;
            Alignment = HorizontalAlignment.Left;
            CoverBackground = true;
            TextColor = Color.Black;
            CoverColor = Color.White;
            DetectedFontName = string.Empty;
            DetectedDescription = string.Empty;
        }

        public string Text { get; set; }

        public string BaseFontName { get; set; }

        /// <summary>
        /// Fuente que tenia el texto seleccionado, vacia si no se pudo leer.
        /// Se ofrece como una opcion mas del desplegable, la primera.
        /// </summary>
        public string DetectedFontName { get; set; }

        /// <summary>Resumen de la tipografia detectada, para enseñarlo.</summary>
        public string DetectedDescription { get; set; }

        public bool Bold { get; set; }

        public bool Italic { get; set; }

        /// <summary>
        /// La persona dejo puesta la fuente detectada, asi que hay que
        /// intentar escribir con ella.
        /// </summary>
        public bool UsesDetectedFont
        {
            get
            {
                return !string.IsNullOrEmpty(DetectedFontName) &&
                    string.Equals(
                        BaseFontName,
                        DetectedFontName,
                        StringComparison.Ordinal);
            }
        }

        public decimal FontSizePoints { get; set; }

        public bool AutoFit { get; set; }

        public HorizontalAlignment Alignment { get; set; }

        public bool CoverBackground { get; set; }

        public Color TextColor { get; set; }

        public Color CoverColor { get; set; }

        public PdfTextEditDialogState Clone()
        {
            return new PdfTextEditDialogState
            {
                Text = Text ?? string.Empty,
                DetectedFontName = DetectedFontName ?? string.Empty,
                DetectedDescription = DetectedDescription ?? string.Empty,
                BaseFontName = NormalizeSelectedFont(BaseFontName),
                Bold = Bold,
                Italic = Italic,
                FontSizePoints = ClampFontSize(FontSizePoints),
                AutoFit = AutoFit,
                Alignment = NormalizeAlignment(Alignment),
                CoverBackground = CoverBackground,
                TextColor = NormalizeColor(TextColor, Color.Black),
                CoverColor = NormalizeColor(CoverColor, Color.White)
            };
        }

        /// <summary>
        /// La fuente detectada es un valor valido mas del desplegable, ademas de
        /// las tres genericas.
        /// </summary>
        private string NormalizeSelectedFont(string value)
        {
            if (!string.IsNullOrEmpty(DetectedFontName) &&
                string.Equals(
                    value,
                    DetectedFontName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return DetectedFontName;
            }

            return NormalizeBaseFontName(value);
        }

        internal static string NormalizeBaseFontName(string value)
        {
            if (string.Equals(
                    value,
                    TimesFontName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return TimesFontName;
            }

            if (string.Equals(
                    value,
                    CourierFontName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return CourierFontName;
            }

            return HelveticaFontName;
        }

        internal static decimal ClampFontSize(decimal value)
        {
            return Math.Max(4M, Math.Min(144M, value));
        }

        internal static HorizontalAlignment NormalizeAlignment(
            HorizontalAlignment value)
        {
            return value == HorizontalAlignment.Center ||
                value == HorizontalAlignment.Right
                ? value
                : HorizontalAlignment.Left;
        }

        internal static Color NormalizeColor(
            Color value,
            Color fallback)
        {
            if (value.IsEmpty)
            {
                value = fallback;
            }

            return Color.FromArgb(
                255,
                value.R,
                value.G,
                value.B);
        }
    }

    /// <summary>
    /// Compact, architectural editor for one visual text replacement.
    /// Rendering and saving are intentionally left to the caller.
    /// </summary>
    internal sealed class PdfTextEditDialog : Form
    {
        private static readonly Color SurfaceColor =
            Color.FromArgb(250, 249, 247);
        private static readonly Color FieldColor = Color.White;
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

        private readonly TextBox textInput;
        private readonly ComboBox fontSelector;
        private readonly Label detectedStyleLabel;
        private readonly CheckBox boldCheckBox;
        private readonly CheckBox italicCheckBox;
        private readonly NumericUpDown fontSizeInput;
        private readonly CheckBox autoFitCheckBox;
        private readonly ComboBox alignmentSelector;
        private readonly CheckBox coverBackgroundCheckBox;
        private readonly Button textColorButton;
        private readonly Button coverColorButton;
        private readonly TextEditPreview preview;
        private readonly Label validationLabel;
        private readonly Button applyButton;
        private readonly Button cancelButton;
        private readonly ToolTip toolTip;

        public PdfTextEditDialog(PdfTextEditDialogState initialState)
            : this(initialState, string.Empty)
        {
        }

        public PdfTextEditDialog(
            PdfTextEditDialogState initialState,
            string contextText)
        {
            var state = (initialState ?? new PdfTextEditDialogState())
                .Clone();

            Text = "Editar texto";
            AppBranding.ApplyWindowIcon(this);
            ClientSize = new Size(640, 530);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = SurfaceColor;
            ForeColor = TitleColor;
            Font = CreateUiFont(9.25f, FontStyle.Regular);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);

            toolTip = new ToolTip
            {
                AutoPopDelay = 5000,
                InitialDelay = 300,
                ReshowDelay = 80,
                ShowAlways = true
            };

            var eyebrowLabel = new Label
            {
                Left = 18,
                Top = 14,
                Width = 300,
                Height = 15,
                Text = "EDITAR / TEXTO",
                ForeColor = AccentTextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = CreateArchitecturalFont(7.5f, FontStyle.Bold)
            };
            var titleLabel = new Label
            {
                Left = 18,
                Top = 28,
                Width = 430,
                Height = 28,
                Text = "Cubrir y escribir en la zona",
                ForeColor = TitleColor,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = CreateArchitecturalFont(15f, FontStyle.Regular)
            };
            var contextLabel = new Label
            {
                Left = 452,
                Top = 17,
                Width = 168,
                Height = 30,
                Text = string.IsNullOrWhiteSpace(contextText)
                    ? "UNA REVISIÓN"
                    : contextText.ToUpperInvariant(),
                ForeColor = MutedColor,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.TopRight,
                Font = CreateArchitecturalFont(7.25f, FontStyle.Bold)
            };
            var accentLine = new Panel
            {
                Left = 18,
                Top = 58,
                Width = 42,
                Height = 2,
                BackColor = AccentColor
            };

            var textLabel = CreateSectionLabel("TEXTO", 18, 72, 250);
            textInput = new TextBox
            {
                Left = 18,
                Top = 91,
                Width = 602,
                Height = 112,
                Multiline = true,
                AcceptsReturn = true,
                AcceptsTab = false,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = FieldColor,
                ForeColor = TitleColor,
                Font = CreateUiFont(10f, FontStyle.Regular),
                MaxLength = 12000,
                Text = state.Text ?? string.Empty,
                AccessibleName = "Texto de reemplazo"
            };
            textInput.TextChanged += EditorValueChanged;

            var fontLabel = CreateSectionLabel("FUENTE", 18, 215, 160);
            fontSelector = new ComboBox
            {
                Left = 18,
                Top = 234,
                Width = 158,
                Height = 28,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = FieldColor,
                ForeColor = TitleColor,
                AccessibleName = "Fuente del texto"
            };
            // La fuente detectada en el PDF va la primera, para que el reemplazo
            // se parezca al resto de la pagina sin tener que elegir nada.
            if (!string.IsNullOrEmpty(state.DetectedFontName))
            {
                fontSelector.Items.Add(state.DetectedFontName);
            }
            fontSelector.Items.AddRange(new object[]
            {
                PdfTextEditDialogState.HelveticaFontName,
                PdfTextEditDialogState.TimesFontName,
                PdfTextEditDialogState.CourierFontName
            });
            fontSelector.SelectedItem = state.BaseFontName;
            if (fontSelector.SelectedItem == null)
            {
                fontSelector.SelectedItem =
                    PdfTextEditDialogState.NormalizeBaseFontName(
                        state.BaseFontName);
            }
            fontSelector.SelectedIndexChanged += EditorValueChanged;

            var sizeLabel = CreateSectionLabel(
                "TAMAÑO / PT",
                190,
                215,
                105);
            fontSizeInput = new NumericUpDown
            {
                Left = 190,
                Top = 234,
                Width = 92,
                Height = 28,
                Minimum = 4M,
                Maximum = 144M,
                DecimalPlaces = 1,
                Increment = 0.5M,
                Value = PdfTextEditDialogState.ClampFontSize(
                    state.FontSizePoints),
                TextAlign = HorizontalAlignment.Right,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = FieldColor,
                ForeColor = TitleColor,
                AccessibleName = "Tamaño de letra en puntos"
            };
            fontSizeInput.ValueChanged += EditorValueChanged;

            autoFitCheckBox = new CheckBox
            {
                Left = 294,
                Top = 236,
                Width = 116,
                Height = 25,
                Text = "Ajuste automático",
                Checked = state.AutoFit,
                ForeColor = BodyColor,
                FlatStyle = FlatStyle.Flat,
                AccessibleName = "Ajustar el texto a la zona"
            };
            autoFitCheckBox.CheckedChanged += EditorValueChanged;
            toolTip.SetToolTip(
                autoFitCheckBox,
                "Reduce el tamaño si hace falta; el valor indicado es el máximo.");

            var alignmentLabel = CreateSectionLabel(
                "ALINEACIÓN",
                424,
                215,
                196);
            alignmentSelector = new ComboBox
            {
                Left = 424,
                Top = 234,
                Width = 196,
                Height = 28,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = FieldColor,
                ForeColor = TitleColor,
                AccessibleName = "Alineación horizontal"
            };
            alignmentSelector.Items.AddRange(new object[]
            {
                "Izquierda",
                "Centro",
                "Derecha"
            });
            alignmentSelector.SelectedIndex =
                AlignmentToIndex(state.Alignment);
            alignmentSelector.SelectedIndexChanged += EditorValueChanged;

            // Tipografia leida del propio PDF. Se enseña siempre que se haya
            // podido detectar, aunque despues se elija otra fuente a mano.
            detectedStyleLabel = new Label
            {
                Left = 18,
                Top = 266,
                Width = 602,
                Height = 15,
                ForeColor = MutedColor,
                Font = CreateUiFont(8f, FontStyle.Regular),
                Text = string.IsNullOrEmpty(state.DetectedDescription)
                    ? "No se ha podido leer la tipografía del texto original."
                    : "Detectado: " + state.DetectedDescription,
                AccessibleName = "Tipografía detectada en el texto original"
            };

            boldCheckBox = new CheckBox
            {
                Left = 18,
                Top = 288,
                Width = 84,
                Height = 22,
                Text = "Negrita",
                Checked = state.Bold,
                ForeColor = BodyColor,
                FlatStyle = FlatStyle.Flat,
                AccessibleName = "Texto en negrita"
            };
            boldCheckBox.CheckedChanged += EditorValueChanged;

            italicCheckBox = new CheckBox
            {
                Left = 108,
                Top = 288,
                Width = 84,
                Height = 22,
                Text = "Cursiva",
                Checked = state.Italic,
                ForeColor = BodyColor,
                FlatStyle = FlatStyle.Flat,
                AccessibleName = "Texto en cursiva"
            };
            italicCheckBox.CheckedChanged += EditorValueChanged;

            coverBackgroundCheckBox = new CheckBox
            {
                Left = 18,
                Top = 317,
                Width = 360,
                Height = 24,
                Text = "Cubrir primero la zona con fondo blanco",
                Checked = state.CoverBackground,
                ForeColor = BodyColor,
                FlatStyle = FlatStyle.Flat,
                AccessibleName = "Cubrir el contenido anterior"
            };
            coverBackgroundCheckBox.CheckedChanged += EditorValueChanged;

            var textColorLabel = CreateSectionLabel(
                "TEXTO",
                385,
                320,
                43);
            textColorButton = CreateColorButton(
                431,
                316,
                PdfTextEditDialogState.NormalizeColor(
                    state.TextColor,
                    Color.Black),
                "Color del texto");
            textColorButton.Click += TextColorButton_Click;

            var coverColorLabel = CreateSectionLabel(
                "FONDO",
                475,
                320,
                48);
            coverColorButton = CreateColorButton(
                526,
                316,
                PdfTextEditDialogState.NormalizeColor(
                    state.CoverColor,
                    Color.White),
                "Color de cobertura");
            coverColorButton.Click += CoverColorButton_Click;

            var previewLabel = CreateSectionLabel(
                "VISTA PREVIA DEL ESTILO",
                18,
                351,
                300);
            preview = new TextEditPreview
            {
                Left = 18,
                Top = 371,
                Width = 602,
                Height = 68,
                BackColor = FieldColor,
                AccessibleName = "Vista previa del texto"
            };

            var safetyLabel = new Label
            {
                Left = 18,
                Top = 448,
                Width = 602,
                Height = 34,
                Text =
                    "EDICIÓN VISUAL  ·  El texto anterior puede seguir dentro " +
                    "del PDF. Esta herramienta no es una redacción segura.",
                ForeColor = AccentTextColor,
                BackColor = AccentTintColor,
                Padding = new Padding(9, 0, 9, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Font = CreateArchitecturalFont(7.5f, FontStyle.Bold)
            };

            validationLabel = new Label
            {
                Left = 18,
                Top = 491,
                Width = 340,
                Height = 25,
                Text = string.Empty,
                ForeColor = MutedColor,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };

            cancelButton = new Button
            {
                Left = 412,
                Top = 488,
                Width = 92,
                Height = 30,
                Text = "Cancelar",
                DialogResult = DialogResult.Cancel,
                AccessibleName = "Cancelar la edición"
            };
            StyleButton(cancelButton, false);

            applyButton = new Button
            {
                Left = 512,
                Top = 488,
                Width = 108,
                Height = 30,
                Text = "Aplicar al PDF",
                AccessibleName = "Aplicar el texto al PDF"
            };
            StyleButton(applyButton, true);
            applyButton.Click += ApplyButton_Click;

            Controls.Add(eyebrowLabel);
            Controls.Add(titleLabel);
            Controls.Add(contextLabel);
            Controls.Add(accentLine);
            Controls.Add(textLabel);
            Controls.Add(textInput);
            Controls.Add(fontLabel);
            Controls.Add(fontSelector);
            Controls.Add(sizeLabel);
            Controls.Add(fontSizeInput);
            Controls.Add(autoFitCheckBox);
            Controls.Add(alignmentLabel);
            Controls.Add(alignmentSelector);
            Controls.Add(detectedStyleLabel);
            Controls.Add(boldCheckBox);
            Controls.Add(italicCheckBox);
            Controls.Add(coverBackgroundCheckBox);
            Controls.Add(textColorLabel);
            Controls.Add(textColorButton);
            Controls.Add(coverColorLabel);
            Controls.Add(coverColorButton);
            Controls.Add(previewLabel);
            Controls.Add(preview);
            Controls.Add(safetyLabel);
            Controls.Add(validationLabel);
            Controls.Add(cancelButton);
            Controls.Add(applyButton);

            AcceptButton = applyButton;
            CancelButton = cancelButton;

            UpdatePreviewAndValidation();
            Shown += delegate
            {
                textInput.Focus();
                textInput.SelectAll();
            };
        }

        public PdfTextEditDialogState Result { get; private set; }

        internal TextBox TextInputForTesting
        {
            get { return textInput; }
        }

        internal ComboBox FontSelectorForTesting
        {
            get { return fontSelector; }
        }

        internal NumericUpDown FontSizeInputForTesting
        {
            get { return fontSizeInput; }
        }

        internal CheckBox AutoFitCheckBoxForTesting
        {
            get { return autoFitCheckBox; }
        }

        internal ComboBox AlignmentSelectorForTesting
        {
            get { return alignmentSelector; }
        }

        internal CheckBox CoverBackgroundCheckBoxForTesting
        {
            get { return coverBackgroundCheckBox; }
        }

        internal Button TextColorButtonForTesting
        {
            get { return textColorButton; }
        }

        internal Button CoverColorButtonForTesting
        {
            get { return coverColorButton; }
        }

        internal Control PreviewForTesting
        {
            get { return preview; }
        }

        internal Button ApplyButtonForTesting
        {
            get { return applyButton; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                textInput.TextChanged -= EditorValueChanged;
                fontSelector.SelectedIndexChanged -= EditorValueChanged;
                fontSizeInput.ValueChanged -= EditorValueChanged;
                autoFitCheckBox.CheckedChanged -= EditorValueChanged;
                boldCheckBox.CheckedChanged -= EditorValueChanged;
                italicCheckBox.CheckedChanged -= EditorValueChanged;
                alignmentSelector.SelectedIndexChanged -=
                    EditorValueChanged;
                coverBackgroundCheckBox.CheckedChanged -=
                    EditorValueChanged;
                textColorButton.Click -= TextColorButton_Click;
                coverColorButton.Click -= CoverColorButton_Click;
                applyButton.Click -= ApplyButton_Click;
                toolTip.Dispose();
            }

            base.Dispose(disposing);
        }

        private void EditorValueChanged(object sender, EventArgs e)
        {
            UpdatePreviewAndValidation();
        }

        private void TextColorButton_Click(object sender, EventArgs e)
        {
            SelectColor(
                textColorButton,
                "Color del texto de reemplazo");
        }

        private void CoverColorButton_Click(object sender, EventArgs e)
        {
            SelectColor(
                coverColorButton,
                "Color que cubrirá la zona anterior");
        }

        private void SelectColor(Button target, string title)
        {
            using (var dialog = new ColorDialog
            {
                Color = target.BackColor,
                FullOpen = true,
                AnyColor = true
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                target.BackColor = PdfTextEditDialogState.NormalizeColor(
                    dialog.Color,
                    target.BackColor);
                toolTip.SetToolTip(
                    target,
                    title + " · " + ColorToHex(target.BackColor));
                UpdatePreviewAndValidation();
            }
        }

        private void ApplyButton_Click(object sender, EventArgs e)
        {
            if (!CanApply())
            {
                System.Media.SystemSounds.Beep.Play();
                textInput.Focus();
                return;
            }

            Result = CaptureState();
            DialogResult = DialogResult.OK;
            Close();
        }

        private void UpdatePreviewAndValidation()
        {
            var canApply = CanApply();
            applyButton.Enabled = canApply;
            validationLabel.Text = canApply
                ? (autoFitCheckBox.Checked
                    ? "El tamaño indicado actuará como máximo."
                    : "Se conservará el tamaño indicado.")
                : "Escribe texto o activa la cobertura del fondo.";
            validationLabel.ForeColor = canApply
                ? MutedColor
                : AccentTextColor;
            preview.State = CaptureState();
            preview.Invalidate();
            coverColorButton.Enabled = coverBackgroundCheckBox.Checked;
        }

        private bool CanApply()
        {
            return coverBackgroundCheckBox.Checked ||
                !string.IsNullOrEmpty(textInput.Text);
        }

        private PdfTextEditDialogState CaptureState()
        {
            return new PdfTextEditDialogState
            {
                Text = textInput.Text ?? string.Empty,
                BaseFontName =
                    PdfTextEditDialogState.NormalizeBaseFontName(
                        fontSelector.SelectedItem as string),
                FontSizePoints = fontSizeInput.Value,
                AutoFit = autoFitCheckBox.Checked,
                Bold = boldCheckBox.Checked,
                Italic = italicCheckBox.Checked,
                Alignment = IndexToAlignment(
                    alignmentSelector.SelectedIndex),
                CoverBackground = coverBackgroundCheckBox.Checked,
                TextColor = PdfTextEditDialogState.NormalizeColor(
                    textColorButton.BackColor,
                    Color.Black),
                CoverColor = PdfTextEditDialogState.NormalizeColor(
                    coverColorButton.BackColor,
                    Color.White)
            };
        }

        private Button CreateColorButton(
            int left,
            int top,
            Color color,
            string accessibleName)
        {
            var button = new Button
            {
                Left = left,
                Top = top,
                Width = 30,
                Height = 26,
                Text = string.Empty,
                FlatStyle = FlatStyle.Flat,
                BackColor = color,
                Cursor = Cursors.Hand,
                TabStop = true,
                AccessibleName = accessibleName,
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderColor = DividerColor;
            button.FlatAppearance.BorderSize = 1;
            toolTip.SetToolTip(
                button,
                accessibleName + " · " + ColorToHex(color));
            return button;
        }

        private static string ColorToHex(Color color)
        {
            return "#" +
                color.R.ToString("X2") +
                color.G.ToString("X2") +
                color.B.ToString("X2");
        }

        private static Label CreateSectionLabel(
            string text,
            int left,
            int top,
            int width)
        {
            return new Label
            {
                Left = left,
                Top = top,
                Width = width,
                Height = 16,
                Text = text,
                ForeColor = MutedColor,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = CreateArchitecturalFont(7.25f, FontStyle.Bold)
            };
        }

        private static int AlignmentToIndex(HorizontalAlignment value)
        {
            return value == HorizontalAlignment.Center
                ? 1
                : value == HorizontalAlignment.Right
                    ? 2
                    : 0;
        }

        private static HorizontalAlignment IndexToAlignment(int index)
        {
            return index == 1
                ? HorizontalAlignment.Center
                : index == 2
                    ? HorizontalAlignment.Right
                    : HorizontalAlignment.Left;
        }

        private static void StyleButton(Button button, bool primary)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.Cursor = Cursors.Hand;
            button.Font = CreateArchitecturalFont(8.75f, FontStyle.Bold);
            button.BackColor = primary ? TitleColor : SurfaceColor;
            button.ForeColor = primary ? Color.White : TitleColor;
            button.FlatAppearance.BorderColor = primary
                ? TitleColor
                : DividerColor;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = primary
                ? Color.FromArgb(57, 58, 54)
                : Color.FromArgb(239, 237, 233);
            button.FlatAppearance.MouseDownBackColor = primary
                ? Color.FromArgb(15, 15, 14)
                : DividerColor;
        }

        private static Font CreateUiFont(float size, FontStyle style)
        {
            try
            {
                return new Font(
                    "Segoe UI",
                    size,
                    style,
                    GraphicsUnit.Point);
            }
            catch
            {
                return new Font(
                    SystemFonts.MessageBoxFont.FontFamily,
                    size,
                    style,
                    GraphicsUnit.Point);
            }
        }

        private static Font CreateArchitecturalFont(
            float size,
            FontStyle style)
        {
            try
            {
                return new Font(
                    "Bahnschrift Light",
                    size,
                    style,
                    GraphicsUnit.Point);
            }
            catch
            {
                return CreateUiFont(size, style);
            }
        }

        private sealed class TextEditPreview : Control
        {
            public TextEditPreview()
            {
                SetStyle(
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.ResizeRedraw |
                    ControlStyles.UserPaint,
                    true);
                State = new PdfTextEditDialogState();
            }

            public PdfTextEditDialogState State { get; set; }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                var state = State ?? new PdfTextEditDialogState();
                var outer = ClientRectangle;
                if (outer.Width < 4 || outer.Height < 4)
                {
                    return;
                }

                using (var border = new Pen(DividerColor, 1f))
                {
                    e.Graphics.DrawRectangle(
                        border,
                        outer.Left,
                        outer.Top,
                        outer.Width - 1,
                        outer.Height - 1);
                }

                var content = Rectangle.Inflate(outer, -10, -8);
                if (state.CoverBackground)
                {
                    using (var background = new SolidBrush(
                        PdfTextEditDialogState.NormalizeColor(
                            state.CoverColor,
                            Color.White)))
                    {
                        e.Graphics.FillRectangle(background, content);
                    }
                }
                else
                {
                    using (var background = new LinearGradientBrush(
                        content,
                        Color.FromArgb(244, 243, 240),
                        Color.White,
                        30f))
                    {
                        e.Graphics.FillRectangle(background, content);
                    }
                }

                var text = state.Text ?? string.Empty;
                if (text.Length == 0)
                {
                    using (var emptyBrush = new SolidBrush(MutedColor))
                    using (var emptyFont = CreateUiFont(
                        8.5f,
                        FontStyle.Italic))
                    {
                        e.Graphics.DrawString(
                            state.CoverBackground
                                ? "La zona quedará cubierta."
                                : "Escribe para ver el resultado.",
                            emptyFont,
                            emptyBrush,
                            content.Location);
                    }

                    return;
                }

                using (var format = CreateStringFormat(state.Alignment))
                using (var textBrush = new SolidBrush(
                    PdfTextEditDialogState.NormalizeColor(
                        state.TextColor,
                        Color.Black)))
                using (var previewFont = CreatePreviewFont(
                    e.Graphics,
                    state,
                    text,
                    content.Size,
                    format))
                {
                    e.Graphics.TextRenderingHint =
                        System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    e.Graphics.DrawString(
                        text,
                        previewFont,
                        textBrush,
                        content,
                        format);
                }
            }

            private static StringFormat CreateStringFormat(
                HorizontalAlignment alignment)
            {
                var format = new StringFormat(
                    StringFormatFlags.LineLimit);
                format.Alignment = alignment == HorizontalAlignment.Center
                    ? StringAlignment.Center
                    : alignment == HorizontalAlignment.Right
                        ? StringAlignment.Far
                        : StringAlignment.Near;
                format.LineAlignment = StringAlignment.Near;
                format.Trimming = StringTrimming.EllipsisWord;
                return format;
            }

            private static Font CreatePreviewFont(
                Graphics graphics,
                PdfTextEditDialogState state,
                string text,
                Size availableSize,
                StringFormat format)
            {
                var familyName = string.Equals(
                        state.BaseFontName,
                        PdfTextEditDialogState.TimesFontName,
                        StringComparison.Ordinal)
                    ? "Times New Roman"
                    : string.Equals(
                        state.BaseFontName,
                        PdfTextEditDialogState.CourierFontName,
                        StringComparison.Ordinal)
                        ? "Courier New"
                        : "Arial";
                var maximum = (float)
                    PdfTextEditDialogState.ClampFontSize(
                        state.FontSizePoints);
                var size = maximum;

                if (state.AutoFit)
                {
                    while (size > 4f)
                    {
                        using (var candidate = CreateFont(
                            familyName,
                            size))
                        {
                            var measured = graphics.MeasureString(
                                text,
                                candidate,
                                new SizeF(
                                    Math.Max(1, availableSize.Width),
                                    Math.Max(1, availableSize.Height)),
                                format);
                            if (measured.Width <= availableSize.Width + 0.5f &&
                                measured.Height <= availableSize.Height + 0.5f)
                            {
                                break;
                            }
                        }

                        size -= size > 12f ? 1f : 0.5f;
                    }
                }

                return CreateFont(familyName, Math.Max(4f, size));
            }

            private static Font CreateFont(string familyName, float size)
            {
                try
                {
                    return new Font(
                        familyName,
                        size,
                        FontStyle.Regular,
                        GraphicsUnit.Point);
                }
                catch
                {
                    return CreateUiFont(size, FontStyle.Regular);
                }
            }
        }
    }
}

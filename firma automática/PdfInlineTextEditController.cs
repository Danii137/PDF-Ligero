using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using PdfiumViewer;

namespace FirmaAutomatica
{
    /// <summary>Lo que se ha pedido cambiar en una linea del documento.</summary>
    internal sealed class PdfInlineEditRequest
    {
        internal PdfInlineEditRequest(
            PdfTextBlock block,
            string newText,
            string fontName,
            float fontSizePoints,
            Color color,
            bool bold,
            bool italic,
            bool formatChanged)
        {
            Block = block;
            NewText = newText ?? string.Empty;
            FontName = fontName ?? string.Empty;
            FontSizePoints = fontSizePoints;
            Color = color;
            Bold = bold;
            Italic = italic;
            FormatChanged = formatChanged;
        }

        public PdfTextBlock Block { get; private set; }

        public string NewText { get; private set; }

        public string FontName { get; private set; }

        public float FontSizePoints { get; private set; }

        public Color Color { get; private set; }

        public bool Bold { get; private set; }

        public bool Italic { get; private set; }

        /// <summary>
        /// Se ha cambiado algo mas que el texto. Obliga a reescribir con una
        /// fuente del sistema, porque sustituir la cadena en el sitio
        /// conservaria el formato anterior.
        /// </summary>
        public bool FormatChanged { get; private set; }
    }

    /// <summary>
    /// Editor de texto sobre la propia pagina.
    ///
    /// Funciona como se espera de un editor de PDF: se enciende la herramienta,
    /// las lineas de texto se recuadran, se pincha una y se escribe encima, con
    /// una barra para cambiar fuente, tamano, color, negrita y cursiva.
    ///
    /// El truco para que se vea "en la pagina" sin escribir un motor de
    /// composicion es superponer un cuadro de texto normal de Windows,
    /// colocado sobre la linea y con su misma tipografia aproximada. Lo que se
    /// guarda despues no lo dibuja este cuadro, sino
    /// PdfDirectTextEditService, que sustituye el texto de verdad.
    /// </summary>
    internal sealed class PdfInlineTextEditController : IMessageFilter, IDisposable
    {
        private const int WmLButtonDown = 0x0201;
        private const int WmMouseMove = 0x0200;
        private const int WmKeyDown = 0x0100;

        private static readonly Color OutlineColor = Color.FromArgb(150, 150, 146);
        private static readonly Color HoverColor = Color.FromArgb(238, 91, 61);
        private static readonly Color SurfaceColor = Color.FromArgb(250, 249, 247);
        private static readonly Color DividerColor = Color.FromArgb(211, 209, 204);
        private static readonly Color TitleColor = Color.FromArgb(31, 31, 29);
        private static readonly Color MutedColor = Color.FromArgb(139, 136, 130);

        private static readonly string[] Families = new[]
        {
            "Arial", "Calibri", "Cambria", "Consolas", "Courier New",
            "Georgia", "Segoe UI", "Tahoma", "Times New Roman", "Verdana"
        };

        private readonly PdfRenderer renderer;
        private readonly Func<bool> canEdit;
        private readonly Action<string> reportStatus;
        private readonly Func<int, IList<PdfTextBlock>> loadBlocks;
        private readonly Dictionary<int, InlineEditMarker> pageMarkers =
            new Dictionary<int, InlineEditMarker>();
        private readonly Dictionary<int, IList<PdfTextBlock>> blocksByPage =
            new Dictionary<int, IList<PdfTextBlock>>();

        private TextBox editor;
        private Panel formatBar;
        private ComboBox fontSelector;
        private NumericUpDown sizeInput;
        private Button colorButton;
        private Button boldButton;
        private Button italicButton;
        private Button applyButton;
        private Button cancelButton;
        private Label hintLabel;
        private ToolTip barTip;
        private PdfRendererCursorOverride cursorOverride;

        private bool disposed;
        private bool active;
        private PdfTextBlock hovered;
        private PdfTextBlock editing;
        private Color currentColor = Color.Black;
        private bool currentBold;
        private bool currentItalic;
        private string originalFont = string.Empty;
        private float originalSize;
        private Color originalColor = Color.Black;
        private bool originalBold;
        private bool originalItalic;

        public PdfInlineTextEditController(
            PdfRenderer renderer,
            Func<bool> canEdit,
            Func<int, IList<PdfTextBlock>> loadBlocks,
            Action<string> reportStatus)
        {
            if (renderer == null)
            {
                throw new ArgumentNullException("renderer");
            }
            if (loadBlocks == null)
            {
                throw new ArgumentNullException("loadBlocks");
            }

            this.renderer = renderer;
            this.canEdit = canEdit;
            this.loadBlocks = loadBlocks;
            this.reportStatus = reportStatus;

            // Igual que en la anotacion: WM_SETCURSOR se envia y no se
            // encola, asi que hay que engancharse al visor para imponerlo.
            cursorOverride = new PdfRendererCursorOverride(
                renderer,
                delegate
                {
                    // Mientras la herramienta este encendida, cursor de texto en
                    // toda la pagina: es lo que se espera de un editor, y no
                    // depende de acertar si hay una linea justo debajo.
                    return active ? Cursors.IBeam : null;
                });

            renderer.Disposed += Renderer_Disposed;
            renderer.Scroll += Renderer_ViewportChanged;
            renderer.ZoomChanged += Renderer_ViewportChanged;
            renderer.SizeChanged += Renderer_ViewportChanged;
            Application.AddMessageFilter(this);
        }

        public bool IsActive
        {
            get { return active; }
        }

        public bool IsEditing
        {
            get { return editing != null; }
        }

        /// <summary>Se ha pedido guardar un cambio en una linea.</summary>
        public event EventHandler<PdfInlineEditEventArgs> EditRequested;

        public void Activate()
        {
            if (disposed || active)
            {
                return;
            }

            active = true;
            renderer.Cursor = Cursors.IBeam;
            Refresh();
            Report("Pincha una línea de texto para reescribirla.");
        }

        public void Deactivate()
        {
            if (disposed || !active)
            {
                return;
            }

            // Se aplica lo escrito, no se tira: cerrar la herramienta despues de
            // corregir algo tiene que guardarlo. Para descartar esta Esc, que
            // cancela sin aplicar, y Ctrl+Z para deshacer despues.
            CommitEditing();
            active = false;
            hovered = null;
            renderer.Cursor = Cursors.Default;
            Refresh();
        }

        /// <summary>Olvida lo leido: hay que releerlo tras cambiar el documento.</summary>
        public void InvalidateBlocks()
        {
            blocksByPage.Clear();
            hovered = null;
            CancelEditing();
            Refresh();
        }

        public bool PreFilterMessage(ref Message message)
        {
            if (disposed || !active)
            {
                return false;
            }

            if (message.Msg == WmKeyDown &&
                (int)message.WParam == (int)Keys.Escape &&
                editing != null)
            {
                CancelEditing();
                return true;
            }

            if (!renderer.IsHandleCreated ||
                message.HWnd != renderer.Handle)
            {
                return false;
            }

            var location = renderer.PointToClient(Cursor.Position);
            if (message.Msg == WmMouseMove)
            {
                return HandleMouseMove(location);
            }
            if (message.Msg == WmLButtonDown)
            {
                return HandleMouseDown(location);
            }

            return false;
        }

        private bool HandleMouseMove(Point location)
        {
            if (editing != null)
            {
                return false;
            }

            var bloque = FindBlock(location);
            if (bloque == hovered)
            {
                return false;
            }

            hovered = bloque;
            renderer.Cursor = bloque == null ? Cursors.Default : Cursors.IBeam;
            Refresh();
            return false;
        }

        private bool HandleMouseDown(Point location)
        {
            if (canEdit != null && !canEdit())
            {
                return false;
            }

            var bloque = FindBlock(location);
            if (bloque == null)
            {
                // Pinchar fuera confirma lo que se estuviera escribiendo, que es
                // lo que espera cualquiera que venga de un procesador de textos.
                if (editing != null)
                {
                    CommitEditing();
                    return true;
                }

                return false;
            }

            if (bloque.FromOcr)
            {
                // Cambiar la capa del OCR no cambiaria la pagina: lo que se ve
                // es la imagen escaneada, y el texto va invisible debajo.
                Report(
                    "Esta línea viene del OCR: se puede subrayar y copiar, " +
                    "pero no reescribir, porque lo que se ve es la imagen.");
                return true;
            }

            var punto = renderer.PointToPdf(location);
            BeginEditing(bloque, punto.Location.X);
            return true;
        }

        private PdfTextBlock FindBlock(Point location)
        {
            var punto = renderer.PointToPdf(location);
            if (punto.Page < 0)
            {
                return null;
            }

            var pagina = punto.Page + 1;
            var bloques = EnsureBlocks(pagina);
            foreach (var bloque in bloques)
            {
                if (bloque.Contains(punto.Location.X, punto.Location.Y))
                {
                    return bloque;
                }
            }

            return null;
        }

        private IList<PdfTextBlock> EnsureBlocks(int pageNumber)
        {
            IList<PdfTextBlock> bloques;
            if (blocksByPage.TryGetValue(pageNumber, out bloques))
            {
                return bloques;
            }

            try
            {
                bloques = loadBlocks(pageNumber) ?? new List<PdfTextBlock>();
            }
            catch (Exception)
            {
                bloques = new List<PdfTextBlock>();
            }

            blocksByPage[pageNumber] = bloques;
            EnsureMarker(pageNumber - 1);
            return bloques;
        }

        private void BeginEditing(PdfTextBlock bloque, float xDondeSePincho)
        {
            CancelEditing();
            editing = bloque;

            originalFont = bloque.Style == null
                ? string.Empty
                : bloque.Style.FontName;
            originalSize = bloque.Style == null ? 11F : bloque.Style.FontSizePoints;
            originalColor = bloque.Style == null ? Color.Black : bloque.Style.Color;
            originalBold = bloque.Style != null && bloque.Style.Bold;
            originalItalic = bloque.Style != null && bloque.Style.Italic;

            currentColor = originalColor;
            currentBold = originalBold;
            currentItalic = originalItalic;

            EnsureEditor();
            editor.Text = bloque.Text;
            editor.ForeColor = currentColor;
            ApplyEditorFont();
            PositionEditor();
            editor.Visible = true;
            editor.BringToFront();
            editor.Focus();

            // El cursor se coloca donde se pincho, como en cualquier editor de
            // texto. Seleccionarlo todo haria que la primera tecla borrase la
            // linea entera, que no es lo que se espera al ir a corregir algo.
            var indice = bloque.NearestCharacterIndex(xDondeSePincho);
            editor.SelectionStart = Math.Max(
                0,
                Math.Min(editor.TextLength, indice));
            editor.SelectionLength = 0;

            EnsureFormatBar();
            fontSelector.Text = string.IsNullOrEmpty(originalFont)
                ? "Arial"
                : originalFont;
            sizeInput.Value = (decimal)Math.Max(
                4F,
                Math.Min(144F, originalSize <= 0.5F ? 11F : originalSize));
            colorButton.BackColor = currentColor;
            RefreshStyleButtons();
            formatBar.Visible = true;
            formatBar.BringToFront();
            PositionFormatBar();

            Refresh();
            Report("Escribe el texto y pulsa Aplicar, o Esc para dejarlo.");
        }

        private void CancelEditing()
        {
            if (editing == null)
            {
                return;
            }

            editing = null;
            if (editor != null)
            {
                editor.Visible = false;
            }
            if (formatBar != null)
            {
                formatBar.Visible = false;
            }

            renderer.Focus();
            Refresh();
        }

        private void CommitEditing()
        {
            if (editing == null)
            {
                return;
            }

            var bloque = editing;
            var texto = editor.Text ?? string.Empty;
            var fuente = (fontSelector.Text ?? string.Empty).Trim();
            var tamano = (float)sizeInput.Value;

            var formatoCambiado =
                !string.Equals(fuente, originalFont, StringComparison.OrdinalIgnoreCase) ||
                Math.Abs(tamano - originalSize) > 0.05F ||
                currentColor.ToArgb() != originalColor.ToArgb() ||
                currentBold != originalBold ||
                currentItalic != originalItalic;

            var sinCambios = !formatoCambiado &&
                string.Equals(texto, bloque.Text, StringComparison.Ordinal);

            CancelEditing();

            if (sinCambios)
            {
                Report("No has cambiado nada.");
                return;
            }
            if (string.IsNullOrWhiteSpace(texto))
            {
                Report("El texto no puede quedarse vacío.");
                return;
            }

            var handler = EditRequested;
            if (handler != null)
            {
                handler(
                    this,
                    new PdfInlineEditEventArgs(
                        new PdfInlineEditRequest(
                            bloque,
                            texto,
                            fuente,
                            tamano,
                            currentColor,
                            currentBold,
                            currentItalic,
                            formatoCambiado)));
            }
        }

        private void EnsureEditor()
        {
            if (editor != null)
            {
                return;
            }

            // Sin bordes y del color del papel: la idea es que no parezca un
            // formulario encima del PDF, sino que se escriba en el documento.
            // Se conserva un TextBox de verdad para tener cursor, seleccion,
            // teclas de edicion y portapapeles sin reimplementarlos.
            editor = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = Color.White,
                Multiline = false,
                Visible = false,
                AccessibleName = "Texto de la línea"
            };
            editor.KeyDown += Editor_KeyDown;
            renderer.Controls.Add(editor);
        }

        private void Editor_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                CommitEditing();
                return;
            }

            if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                CancelEditing();
            }
        }

        /// <summary>
        /// Da al cuadro de texto exactamente la misma letra que tiene el
        /// documento en pantalla, para que al pinchar una linea no cambie nada:
        /// solo aparece el cursor de escritura.
        ///
        /// No se calcula desde los puntos del PDF, que obliga a acertar con la
        /// conversion a pixeles y con las metricas de cada fuente. Se parte de
        /// lo que ya se sabe medido: el alto en pixeles que ocupa esa linea en
        /// pantalla, que es justo la distancia entre su ascendente y su
        /// descendente. Con las metricas de la familia se despeja el tamano que
        /// reproduce ese alto.
        /// </summary>
        private void ApplyEditorFont()
        {
            if (editor == null)
            {
                return;
            }

            var familia = string.IsNullOrEmpty(originalFont) ? "Arial" : originalFont;
            if (fontSelector != null && !string.IsNullOrWhiteSpace(fontSelector.Text))
            {
                familia = fontSelector.Text.Trim();
            }

            var puntos = sizeInput == null
                ? originalSize
                : (float)sizeInput.Value;
            if (puntos <= 0.5F)
            {
                puntos = 11F;
            }

            var estilo = FontStyle.Regular;
            if (currentBold)
            {
                estilo |= FontStyle.Bold;
            }
            if (currentItalic)
            {
                estilo |= FontStyle.Italic;
            }

            var pixeles = CalcularTamanoEnPixeles(familia, estilo, puntos);

            try
            {
                editor.Font = new Font(
                    familia,
                    pixeles,
                    estilo,
                    GraphicsUnit.Pixel);
            }
            catch (Exception)
            {
                // Una familia que Windows no reconozca no debe impedir editar.
                editor.Font = new Font(
                    FontFamily.GenericSansSerif,
                    pixeles,
                    estilo,
                    GraphicsUnit.Pixel);
            }
        }

        /// <summary>
        /// Tamano en pixeles que hace que el texto del cuadro ocupe lo mismo que
        /// el de la pagina.
        ///
        /// El alto del recuadro de la linea, llevado a pixeles de pantalla, es
        /// la distancia entre la ascendente y la descendente. En las metricas de
        /// una familia esa distancia vale (ascendente + descendente) / em, asi
        /// que el tamano buscado es el alto dividido por esa proporcion.
        /// </summary>
        private float CalcularTamanoEnPixeles(
            string familia,
            FontStyle estilo,
            float puntosPedidos)
        {
            if (editing == null)
            {
                return Math.Max(6F, puntosPedidos);
            }

            var caja = renderer.BoundsFromPdf(
                new PdfiumViewer.PdfRectangle(
                    editing.PageNumber - 1,
                    editing.Bounds));
            var altoEnPantalla = (float)caja.Height;
            if (altoEnPantalla < 2F)
            {
                return Math.Max(6F, puntosPedidos);
            }

            // Si se ha cambiado el tamano a mano, se escala en la misma
            // proporcion respecto al que tenia el original.
            if (originalSize > 0.5F && puntosPedidos > 0.5F)
            {
                altoEnPantalla *= puntosPedidos / originalSize;
            }

            var proporcion = 1.2F;
            try
            {
                var family = new FontFamily(familia);
                var estiloMetricas = family.IsStyleAvailable(estilo)
                    ? estilo
                    : FontStyle.Regular;
                var em = (float)family.GetEmHeight(estiloMetricas);
                if (em > 0F)
                {
                    proporcion =
                        (family.GetCellAscent(estiloMetricas) +
                         family.GetCellDescent(estiloMetricas)) / em;
                }
            }
            catch (Exception)
            {
                proporcion = 1.2F;
            }

            if (proporcion < 0.5F || proporcion > 2.5F)
            {
                proporcion = 1.2F;
            }

            return Math.Max(6F, altoEnPantalla / proporcion);
        }

        private void PositionEditor()
        {
            if (editor == null || editing == null)
            {
                return;
            }

            var bounds = renderer.BoundsFromPdf(
                new PdfiumViewer.PdfRectangle(
                    editing.PageNumber - 1,
                    editing.Bounds));

            // El cuadro se centra sobre la linea original para que las letras
            // caigan donde estaban. Se le da holgura a la derecha porque el
            // texto nuevo casi siempre es mas largo que el que habia.
            var alto = editor.PreferredHeight;
            var centro = bounds.Top + (bounds.Height / 2);
            var ancho = Math.Max(
                60,
                Math.Min(
                    renderer.ClientSize.Width - bounds.Left - 12,
                    bounds.Width + 220));
            editor.SetBounds(
                bounds.Left,
                centro - (alto / 2),
                ancho,
                alto);
        }

        private void EnsureFormatBar()
        {
            if (formatBar != null)
            {
                return;
            }

            barTip = new ToolTip();
            barTip.InitialDelay = 250;
            barTip.ShowAlways = true;

            formatBar = new Panel
            {
                Width = 470,
                Height = 40,
                BackColor = SurfaceColor,
                BorderStyle = BorderStyle.FixedSingle,
                Visible = false
            };

            fontSelector = new ComboBox
            {
                Left = 8,
                Top = 8,
                Width = 132,
                Height = 24,
                DropDownStyle = ComboBoxStyle.DropDown,
                FlatStyle = FlatStyle.Flat,
                AccessibleName = "Fuente"
            };
            fontSelector.Items.AddRange(Families);
            fontSelector.TextChanged += delegate
            {
                ApplyEditorFont();
                PositionEditor();
            };
            formatBar.Controls.Add(fontSelector);

            sizeInput = new NumericUpDown
            {
                Left = 146,
                Top = 8,
                Width = 58,
                Height = 24,
                Minimum = 4M,
                Maximum = 144M,
                DecimalPlaces = 1,
                Increment = 0.5M,
                Value = 11M,
                BorderStyle = BorderStyle.FixedSingle,
                AccessibleName = "Tamaño en puntos"
            };
            sizeInput.ValueChanged += delegate
            {
                ApplyEditorFont();
                PositionEditor();
            };
            formatBar.Controls.Add(sizeInput);

            boldButton = CreateBarButton("N", "Negrita", 212, 8);
            boldButton.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            boldButton.Click += delegate
            {
                currentBold = !currentBold;
                RefreshStyleButtons();
                ApplyEditorFont();
                PositionEditor();
            };

            italicButton = CreateBarButton("C", "Cursiva", 244, 8);
            italicButton.Font = new Font("Segoe UI", 9F, FontStyle.Italic);
            italicButton.Click += delegate
            {
                currentItalic = !currentItalic;
                RefreshStyleButtons();
                ApplyEditorFont();
                PositionEditor();
            };

            colorButton = new Button
            {
                Left = 278,
                Top = 8,
                Width = 26,
                Height = 24,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Black,
                TabStop = false,
                Cursor = Cursors.Hand,
                AccessibleName = "Color del texto"
            };
            colorButton.FlatAppearance.BorderColor = DividerColor;
            colorButton.Click += ColorButton_Click;
            barTip.SetToolTip(colorButton, "Color del texto");
            formatBar.Controls.Add(colorButton);

            cancelButton = new Button
            {
                Left = 314,
                Top = 8,
                Width = 70,
                Height = 24,
                Text = "Cancelar",
                FlatStyle = FlatStyle.Flat,
                BackColor = SurfaceColor,
                ForeColor = TitleColor,
                TabStop = false,
                Cursor = Cursors.Hand
            };
            cancelButton.FlatAppearance.BorderColor = DividerColor;
            cancelButton.Click += delegate { CancelEditing(); };
            formatBar.Controls.Add(cancelButton);

            applyButton = new Button
            {
                Left = 390,
                Top = 8,
                Width = 70,
                Height = 24,
                Text = "Aplicar",
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(31, 31, 29),
                ForeColor = Color.White,
                TabStop = false,
                Cursor = Cursors.Hand
            };
            applyButton.FlatAppearance.BorderSize = 0;
            applyButton.Click += delegate { CommitEditing(); };
            formatBar.Controls.Add(applyButton);

            hintLabel = new Label { Visible = false };
            formatBar.Controls.Add(hintLabel);

            renderer.Controls.Add(formatBar);
        }

        private Button CreateBarButton(
            string texto,
            string descripcion,
            int left,
            int top)
        {
            var boton = new Button
            {
                Left = left,
                Top = top,
                Width = 28,
                Height = 24,
                Text = texto,
                FlatStyle = FlatStyle.Flat,
                BackColor = SurfaceColor,
                ForeColor = TitleColor,
                TabStop = false,
                Cursor = Cursors.Hand,
                AccessibleName = descripcion
            };
            boton.FlatAppearance.BorderColor = DividerColor;
            barTip.SetToolTip(boton, descripcion);
            formatBar.Controls.Add(boton);
            return boton;
        }

        private void ColorButton_Click(object sender, EventArgs e)
        {
            using (var dialogo = new ColorDialog())
            {
                dialogo.Color = currentColor;
                dialogo.FullOpen = true;
                if (dialogo.ShowDialog(renderer.FindForm()) != DialogResult.OK)
                {
                    return;
                }

                currentColor = dialogo.Color;
                colorButton.BackColor = currentColor;
                if (editor != null)
                {
                    editor.ForeColor = currentColor;
                }
            }
        }

        private void RefreshStyleButtons()
        {
            MarkToggled(boldButton, currentBold);
            MarkToggled(italicButton, currentItalic);
        }

        private static void MarkToggled(Button boton, bool activo)
        {
            if (boton == null)
            {
                return;
            }

            boton.BackColor = activo
                ? Color.FromArgb(251, 236, 231)
                : SurfaceColor;
            boton.FlatAppearance.BorderColor = activo
                ? HoverColor
                : DividerColor;
            boton.FlatAppearance.BorderSize = activo ? 2 : 1;
        }

        private void PositionFormatBar()
        {
            if (formatBar == null || editor == null)
            {
                return;
            }

            // Encima de la linea si cabe; si no, debajo, para no taparla.
            var izquierda = Math.Max(
                6,
                Math.Min(
                    editor.Left,
                    renderer.ClientSize.Width - formatBar.Width - 6));
            var arriba = editor.Top - formatBar.Height - 6;
            if (arriba < 6)
            {
                arriba = editor.Bottom + 6;
            }

            formatBar.Left = izquierda;
            formatBar.Top = Math.Min(
                arriba,
                Math.Max(6, renderer.ClientSize.Height - formatBar.Height - 6));
        }

        private void Renderer_ViewportChanged(object sender, EventArgs e)
        {
            if (disposed || editing == null)
            {
                return;
            }

            ApplyEditorFont();
            PositionEditor();
            PositionFormatBar();
        }

        private void EnsureMarker(int pageIndex)
        {
            if (pageIndex < 0 || pageMarkers.ContainsKey(pageIndex))
            {
                return;
            }

            var marker = new InlineEditMarker(this, pageIndex);
            pageMarkers.Add(pageIndex, marker);
            renderer.Markers.Add(marker);
        }

        private void Refresh()
        {
            if (disposed || !renderer.IsHandleCreated)
            {
                return;
            }

            try
            {
                renderer.Invalidate();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void Report(string mensaje)
        {
            if (reportStatus != null)
            {
                reportStatus(mensaje);
            }
        }

        /// <summary>
        /// Recuadra las lineas de la pagina para que se vea que hay editable, al
        /// modo de los editores de PDF conocidos.
        /// </summary>
        private void DrawPage(
            PdfRenderer targetRenderer,
            Graphics graphics,
            int pageIndex)
        {
            if (!active)
            {
                return;
            }

            IList<PdfTextBlock> bloques;
            if (!blocksByPage.TryGetValue(pageIndex + 1, out bloques))
            {
                return;
            }

            var anterior = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            try
            {
                using (var suave = new Pen(Color.FromArgb(70, OutlineColor), 1f))
                {
                    suave.DashStyle = DashStyle.Dot;
                    foreach (var bloque in bloques)
                    {
                        if (bloque == editing)
                        {
                            continue;
                        }

                        var caja = Inflar(targetRenderer.BoundsFromPdf(
                            new PdfiumViewer.PdfRectangle(pageIndex, bloque.Bounds)));
                        if (caja.Width < 2 || caja.Height < 2)
                        {
                            continue;
                        }

                        graphics.DrawRectangle(suave, caja);
                    }
                }

                // La linea que se esta editando se marca con una linea de
                // acento debajo, no con un recuadro: asi se sabe donde estas
                // sin que parezca un cuadro de dialogo sobre el documento.
                if (editing != null &&
                    editing.PageNumber - 1 == pageIndex &&
                    editor != null &&
                    editor.Visible)
                {
                    using (var acento = new Pen(HoverColor, 1.6f))
                    {
                        graphics.DrawLine(
                            acento,
                            editor.Left,
                            editor.Bottom + 1,
                            editor.Right,
                            editor.Bottom + 1);
                    }
                }

                if (hovered != null &&
                    hovered.PageNumber - 1 == pageIndex &&
                    hovered != editing)
                {
                    var caja = Inflar(targetRenderer.BoundsFromPdf(
                        new PdfiumViewer.PdfRectangle(pageIndex, hovered.Bounds)));
                    using (var relleno = new SolidBrush(
                        Color.FromArgb(22, HoverColor)))
                    using (var borde = new Pen(HoverColor, 1.4f))
                    {
                        graphics.FillRectangle(relleno, caja);
                        graphics.DrawRectangle(borde, caja);
                    }
                }
            }
            finally
            {
                graphics.SmoothingMode = anterior;
            }
        }

        private static Rectangle Inflar(Rectangle caja)
        {
            return new Rectangle(
                caja.Left - 2,
                caja.Top - 2,
                caja.Width + 4,
                caja.Height + 4);
        }

        private void Renderer_Disposed(object sender, EventArgs e)
        {
            Dispose();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Application.RemoveMessageFilter(this);

            if (cursorOverride != null)
            {
                cursorOverride.Dispose();
                cursorOverride = null;
            }

            try
            {
                foreach (var marker in pageMarkers.Values)
                {
                    renderer.Markers.Remove(marker);
                }

                renderer.Scroll -= Renderer_ViewportChanged;
                renderer.ZoomChanged -= Renderer_ViewportChanged;
                renderer.SizeChanged -= Renderer_ViewportChanged;
                renderer.Disposed -= Renderer_Disposed;
            }
            catch (ObjectDisposedException)
            {
            }

            pageMarkers.Clear();
            blocksByPage.Clear();
        }

        private sealed class InlineEditMarker : IPdfMarker
        {
            private readonly PdfInlineTextEditController owner;
            private readonly int page;

            public InlineEditMarker(PdfInlineTextEditController owner, int page)
            {
                this.owner = owner;
                this.page = page;
            }

            public int Page
            {
                get { return page; }
            }

            public void Draw(PdfRenderer pdfRenderer, Graphics graphics)
            {
                if (!owner.disposed)
                {
                    owner.DrawPage(pdfRenderer, graphics, page);
                }
            }
        }
    }

    internal sealed class PdfInlineEditEventArgs : EventArgs
    {
        public PdfInlineEditEventArgs(PdfInlineEditRequest request)
        {
            Request = request;
        }

        public PdfInlineEditRequest Request { get; private set; }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using PdfiumViewer;

namespace FirmaAutomatica
{
    /// <summary>
    /// Herramienta de anotacion: rotulador a mano alzada, subrayador y notas.
    ///
    /// Sigue el mismo patron que la medicion y el zoom por rectangulo. Un
    /// IMessageFilter permite quedarse solo con los gestos propios y dejar
    /// intactos los clics normales, la rueda y las barras de desplazamiento,
    /// porque PdfiumViewer usa el boton izquierdo para desplazar la pagina.
    ///
    /// Las marcas se dibujan aqui, no las dibuja PDFium: se comprobo que ese
    /// motor no pinta anotaciones y su version esta congelada. Ver
    /// build/validation-annotations/README.md.
    /// </summary>
    internal sealed class PdfAnnotationController : IMessageFilter, IDisposable
    {
        private const int WmKeyDown = 0x0100;
        private const int WmLButtonDown = 0x0201;
        private const int WmMouseMove = 0x0200;
        private const int WmLButtonUp = 0x0202;


        // Distancia minima entre puntos guardados. Sin ella un trazo lento
        // acumula miles de puntos casi iguales y el PDF engorda sin motivo.
        private const float MinimumPointDistance = 1.2F;

        private readonly PdfRenderer renderer;
        private readonly Func<bool> canAnnotate;
        private readonly Action<string> reportStatus;
        private readonly Func<string> resolveAuthor;
        private readonly Func<int, IList<PdfTextBlock>> loadBlocks;
        private readonly Dictionary<int, IList<PdfTextBlock>> blocksByPage =
            new Dictionary<int, IList<PdfTextBlock>>();
        private readonly Dictionary<int, AnnotationPageMarker> pageMarkers =
            new Dictionary<int, AnnotationPageMarker>();

        private readonly PdfAnnotationBatch pending = new PdfAnnotationBatch();
        private readonly List<PdfAnnotationItem> saved =
            new List<PdfAnnotationItem>();

        private static readonly Color[] Palette = new[]
        {
            Color.FromArgb(238, 91, 61),
            Color.FromArgb(31, 31, 29),
            Color.FromArgb(214, 40, 40),
            Color.FromArgb(46, 125, 50),
            Color.FromArgb(21, 101, 192),
            Color.FromArgb(255, 179, 0)
        };

        private Panel toolbar;
        private Button inkButton;
        private Button highlightButton;
        private Button noteButton;
        private Button undoButton;
        private Button saveButton;
        private Label pendingLabel;
        private readonly List<Button> colorButtons = new List<Button>();
        private readonly List<Button> widthButtons = new List<Button>();
        private ToolTip toolbarTip;
        private PdfRendererCursorOverride cursorOverride;

        private bool disposed;
        private bool active;
        private bool dragging;
        private PdfAnnotationItem current;
        private PointF lastPoint;
        private PointF highlightAnchor;
        private int currentPage = -1;

        public PdfAnnotationController(
            PdfRenderer renderer,
            Func<bool> canAnnotate,
            Func<string> resolveAuthor,
            Action<string> reportStatus,
            Func<int, IList<PdfTextBlock>> loadBlocks)
        {
            if (renderer == null)
            {
                throw new ArgumentNullException("renderer");
            }

            this.renderer = renderer;
            this.canAnnotate = canAnnotate;
            this.resolveAuthor = resolveAuthor;
            this.reportStatus = reportStatus;
            this.loadBlocks = loadBlocks;

            Tool = PdfAnnotationKind.Ink;
            InkColor = Color.FromArgb(238, 91, 61);
            HighlightColor = Color.FromArgb(255, 214, 64);
            NoteColor = Color.FromArgb(72, 133, 197);
            WidthPoints = 2F;

            // El cursor se impone enganchandose al visor: los filtros de
            // mensajes no ven WM_SETCURSOR porque se envia, no se encola.
            cursorOverride = new PdfRendererCursorOverride(
                renderer,
                delegate { return active ? CursorDeLaHerramienta() : null; });

            renderer.Disposed += Renderer_Disposed;
            Application.AddMessageFilter(this);
        }

        public PdfAnnotationKind Tool { get; set; }

        public Color InkColor { get; set; }

        public Color HighlightColor { get; set; }

        public Color NoteColor { get; set; }

        public float WidthPoints { get; set; }

        public bool IsActive
        {
            get { return active; }
        }

        public PdfAnnotationBatch Pending
        {
            get { return pending; }
        }

        public bool HasPending
        {
            get { return pending.HasPending; }
        }

        public event EventHandler PendingChanged;

        /// <summary>La persona ha pedido guardar las marcas en el PDF.</summary>
        public event EventHandler SaveRequested;

        /// <summary>Enciende la herramienta y toma el raton de la pagina.</summary>
        public void Activate()
        {
            if (disposed || active)
            {
                return;
            }

            active = true;
            AplicarCursor();
            EnsureToolbar();
            toolbar.Visible = true;
            toolbar.BringToFront();
            PositionToolbar();
            EnsureMarkersForPendingPages();
            RefreshToolbar();
            Refresh();
        }

        public void Deactivate()
        {
            if (disposed || !active)
            {
                return;
            }

            CancelCurrent();
            active = false;
            renderer.Cursor = Cursors.Default;
            if (toolbar != null)
            {
                toolbar.Visible = false;
            }

            Refresh();
        }

        /// <summary>
        /// Carga las marcas que ya tiene el documento, para poder dibujarlas.
        /// </summary>
        public void LoadExisting(string pdfPath)
        {
            saved.Clear();
            pageMarkers.Clear();
            blocksByPage.Clear();
            foreach (var item in PdfAnnotationService.Read(pdfPath))
            {
                saved.Add(item);
            }

            EnsureMarkersForPendingPages();
            Refresh();
        }

        /// <summary>Vacia lo pendiente, tras guardarlo o al descartarlo.</summary>
        public void ClearPending()
        {
            pending.Clear();
            CancelCurrent();
            RaisePendingChanged();
            Refresh();
        }

        public bool UndoLast()
        {
            if (!pending.UndoLast())
            {
                return false;
            }

            RaisePendingChanged();
            Refresh();
            return true;
        }

        public bool PreFilterMessage(ref Message message)
        {
            if (disposed || !active)
            {
                return false;
            }

            if (message.Msg == WmKeyDown &&
                (int)message.WParam == (int)Keys.Escape &&
                dragging)
            {
                CancelCurrent();
                return true;
            }

            // Durante el arrastre el raton puede salirse del visor; se sigue
            // atendiendo para no cortar el trazo a medias.
            if (dragging && message.HWnd != renderer.Handle)
            {
                var capturado = renderer.PointToClient(Cursor.Position);
                if (message.Msg == WmMouseMove)
                {
                    return HandleMouseMove(capturado);
                }
                if (message.Msg == WmLButtonUp)
                {
                    return HandleMouseUp(capturado);
                }
            }

            if (!renderer.IsHandleCreated ||
                message.HWnd != renderer.Handle)
            {
                return false;
            }

            var location = renderer.PointToClient(Cursor.Position);
            if (message.Msg == WmLButtonDown)
            {
                return HandleMouseDown(location);
            }
            if (message.Msg == WmMouseMove)
            {
                return HandleMouseMove(location);
            }
            if (message.Msg == WmLButtonUp)
            {
                return HandleMouseUp(location);
            }

            return false;
        }

        private bool HandleMouseDown(Point location)
        {
            if (canAnnotate != null && !canAnnotate())
            {
                return false;
            }

            var punto = renderer.PointToPdf(location);
            if (punto.Page < 0)
            {
                return false;
            }

            if (Tool == PdfAnnotationKind.Note)
            {
                CreateNote(punto);
                return true;
            }

            current = new PdfAnnotationItem(Tool, punto.Page + 1);
            current.Author = ResolveAuthorName();
            current.WidthPoints = WidthPoints;

            if (Tool == PdfAnnotationKind.Ink)
            {
                current.Color = InkColor;
                current.BeginStroke();
                current.AddPoint(punto.Location);
            }
            else
            {
                current.Color = HighlightColor;
                current.Opacity = 0.4F;
                current.Area = new RectangleF(
                    punto.Location.X,
                    punto.Location.Y,
                    0F,
                    0F);
                highlightAnchor = punto.Location;
            }

            lastPoint = punto.Location;
            currentPage = punto.Page;
            dragging = true;
            EnsureMarker(punto.Page);
            return true;
        }

        private bool HandleMouseMove(Point location)
        {
            if (!dragging || current == null)
            {
                return false;
            }

            var punto = renderer.PointToPdf(location);

            // Si el raton se va a otra pagina se ignora: una marca pertenece a
            // una sola pagina.
            if (punto.Page != currentPage)
            {
                return true;
            }

            if (Tool == PdfAnnotationKind.Ink)
            {
                var dx = punto.Location.X - lastPoint.X;
                var dy = punto.Location.Y - lastPoint.Y;
                if ((dx * dx) + (dy * dy) <
                    MinimumPointDistance * MinimumPointDistance)
                {
                    return true;
                }

                current.AddPoint(punto.Location);
                lastPoint = punto.Location;
            }
            else
            {
                ActualizarSubrayado(punto.Location);
            }

            Refresh();
            return true;
        }

        private bool HandleMouseUp(Point location)
        {
            if (!dragging || current == null)
            {
                return false;
            }

            dragging = false;
            if (Tool == PdfAnnotationKind.Ink)
            {
                current.DropEmptyStrokes();
            }

            if (!current.IsEmpty())
            {
                pending.Add(current);
                RaisePendingChanged();
                Report(current.Describe() + " añadido");
            }

            current = null;
            Refresh();
            return true;
        }

        /// <summary>
        /// Rehace los tramos subrayados entre el punto de anclaje y el actual.
        ///
        /// Sigue al texto, como una seleccion de un procesador de textos: la
        /// primera linea desde donde se empezo hasta su final, las de en medio
        /// enteras y la ultima hasta donde va el raton. Si no hay texto debajo
        /// se recurre al rectangulo suelto de antes, para poder marcar tambien
        /// sobre un plano o una imagen.
        /// </summary>
        private void ActualizarSubrayado(PointF actual)
        {
            current.Quads.Clear();

            var bloques = EnsureBlocks(currentPage + 1);
            var desde = LocalizarPunto(bloques, highlightAnchor);
            var hasta = LocalizarPunto(bloques, actual);

            if (desde == null || hasta == null)
            {
                current.Area = FromCorners(highlightAnchor, actual);
                return;
            }

            var a = desde;
            var b = hasta;
            if (a.Linea > b.Linea ||
                (a.Linea == b.Linea && a.Caracter > b.Caracter))
            {
                a = hasta;
                b = desde;
            }

            for (var i = a.Linea; i <= b.Linea && i < bloques.Count; i++)
            {
                var bloque = bloques[i];
                var inicio = i == a.Linea ? a.Caracter : 0;
                var fin = i == b.Linea
                    ? b.Caracter
                    : bloque.CharacterBounds.Count;
                var tramo = bloque.SpanBounds(inicio, fin);
                if (tramo.Width > 0.01F && tramo.Height > 0.01F)
                {
                    current.Quads.Add(tramo);
                }
            }

            current.Area = current.Quads.Count > 0
                ? current.GetBounds()
                : FromCorners(highlightAnchor, actual);
        }

        /// <summary>Linea y caracter que hay bajo un punto de la pagina.</summary>
        private PosicionEnTexto LocalizarPunto(
            IList<PdfTextBlock> bloques,
            PointF punto)
        {
            if (bloques == null || bloques.Count == 0)
            {
                return null;
            }

            for (var i = 0; i < bloques.Count; i++)
            {
                if (bloques[i].Contains(punto.X, punto.Y))
                {
                    return new PosicionEnTexto(
                        i,
                        bloques[i].NearestCharacterIndex(punto.X));
                }
            }

            // Fuera de una linea se toma la mas cercana en vertical, que es lo
            // que hace cualquier seleccion de texto cuando el raton se sale.
            var mejor = -1;
            var distancia = float.MaxValue;
            for (var i = 0; i < bloques.Count; i++)
            {
                var centro = bloques[i].Bounds.Top +
                    (bloques[i].Bounds.Height / 2F);
                var d = Math.Abs(centro - punto.Y);
                if (d < distancia)
                {
                    distancia = d;
                    mejor = i;
                }
            }

            if (mejor < 0 || distancia > 40F)
            {
                return null;
            }

            return new PosicionEnTexto(
                mejor,
                bloques[mejor].NearestCharacterIndex(punto.X));
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
                bloques = loadBlocks == null
                    ? new List<PdfTextBlock>()
                    : (loadBlocks(pageNumber) ?? new List<PdfTextBlock>());
            }
            catch (Exception)
            {
                bloques = new List<PdfTextBlock>();
            }

            blocksByPage[pageNumber] = bloques;
            return bloques;
        }

        private sealed class PosicionEnTexto
        {
            public PosicionEnTexto(int linea, int caracter)
            {
                Linea = linea;
                Caracter = caracter;
            }

            public int Linea { get; private set; }

            public int Caracter { get; private set; }
        }

        private void CreateNote(PdfPoint punto)
        {
            using (var dialogo = new PdfNoteDialog(string.Empty))
            {
                if (dialogo.ShowDialog(renderer.FindForm()) !=
                        DialogResult.OK ||
                    string.IsNullOrWhiteSpace(dialogo.Note))
                {
                    return;
                }

                var nota = new PdfAnnotationItem(
                    PdfAnnotationKind.Note,
                    punto.Page + 1);
                nota.Color = NoteColor;
                nota.Author = ResolveAuthorName();
                nota.Contents = dialogo.Note.Trim();
                nota.Area = new RectangleF(
                    punto.Location.X,
                    punto.Location.Y,
                    18F,
                    18F);

                pending.Add(nota);
                EnsureMarker(punto.Page);
                RaisePendingChanged();
                Report("Nota añadida");
                Refresh();
            }
        }

        private void CancelCurrent()
        {
            dragging = false;
            current = null;
            Refresh();
        }

        private string ResolveAuthorName()
        {
            if (resolveAuthor == null)
            {
                return Environment.UserName;
            }

            var nombre = resolveAuthor();
            return string.IsNullOrWhiteSpace(nombre)
                ? Environment.UserName
                : nombre;
        }

        private static RectangleF FromCorners(PointF a, PointF b)
        {
            return new RectangleF(
                Math.Min(a.X, b.X),
                Math.Min(a.Y, b.Y),
                Math.Abs(b.X - a.X),
                Math.Abs(b.Y - a.Y));
        }

        private void EnsureMarkersForPendingPages()
        {
            foreach (var item in saved)
            {
                EnsureMarker(item.PageNumber - 1);
            }

            foreach (var item in pending.Items)
            {
                EnsureMarker(item.PageNumber - 1);
            }
        }

        private void EnsureMarker(int pageIndex)
        {
            if (pageIndex < 0 || pageMarkers.ContainsKey(pageIndex))
            {
                return;
            }

            // Al aplicar una revision el visor recarga el documento y su lista
            // de marcadores puede haberse quedado sin asignar. Tocarla a ciegas
            // reventaba justo despues de guardar, cuando el trabajo ya estaba
            // hecho.
            var marker = new AnnotationPageMarker(this, pageIndex);
            try
            {
                if (renderer.IsDisposed || renderer.Markers == null)
                {
                    return;
                }

                renderer.Markers.Add(marker);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (NullReferenceException)
            {
                return;
            }

            pageMarkers.Add(pageIndex, marker);
        }

        /// <summary>
        /// Barra flotante sobre la pagina. Va aqui y no en el rail de la
        /// derecha porque el rail ya esta lleno, y porque las opciones solo
        /// tienen sentido mientras se anota.
        /// </summary>
        private void EnsureToolbar()
        {
            if (toolbar != null)
            {
                return;
            }

            toolbarTip = new ToolTip();
            toolbarTip.InitialDelay = 260;
            toolbarTip.ReshowDelay = 80;
            toolbarTip.ShowAlways = true;

            toolbar = new Panel();
            toolbar.Width = 336;
            toolbar.Height = 76;
            toolbar.BackColor = Color.FromArgb(250, 249, 247);
            toolbar.BorderStyle = BorderStyle.FixedSingle;
            toolbar.Visible = false;

            inkButton = CreateToolbarButton("\uE891", "Rotulador", 8, 8);
            inkButton.Click += delegate { SelectTool(PdfAnnotationKind.Ink); };

            highlightButton = CreateToolbarButton(
                "\uE7E6",
                "Subrayador",
                44,
                8);
            highlightButton.Click += delegate
            {
                SelectTool(PdfAnnotationKind.Highlight);
            };

            noteButton = CreateToolbarButton("\uE90A", "Nota", 80, 8);
            noteButton.Click += delegate
            {
                SelectTool(PdfAnnotationKind.Note);
            };

            var separador = new Panel();
            separador.Left = 118;
            separador.Top = 12;
            separador.Width = 1;
            separador.Height = 22;
            separador.BackColor = Color.FromArgb(211, 209, 204);
            toolbar.Controls.Add(separador);

            for (var i = 0; i < Palette.Length; i++)
            {
                var boton = new Button();
                boton.Left = 128 + (i * 26);
                boton.Top = 10;
                boton.Width = 22;
                boton.Height = 22;
                boton.FlatStyle = FlatStyle.Flat;
                boton.BackColor = Palette[i];
                boton.TabStop = false;
                boton.Cursor = Cursors.Hand;
                boton.FlatAppearance.BorderSize = 1;
                boton.FlatAppearance.BorderColor =
                    Color.FromArgb(211, 209, 204);
                boton.AccessibleName = "Color de la marca";
                var elegido = Palette[i];
                boton.Click += delegate { SelectColor(elegido); };
                toolbarTip.SetToolTip(boton, "Usar este color");
                colorButtons.Add(boton);
                toolbar.Controls.Add(boton);
            }

            var grosores = new[] { 1F, 2F, 4F };
            var etiquetas = new[] { "Fino", "Medio", "Grueso" };
            for (var i = 0; i < grosores.Length; i++)
            {
                var boton = CreateToolbarButton(
                    new string('\u2014', 1),
                    etiquetas[i],
                    8 + (i * 36),
                    42);
                boton.Font = new Font("Segoe UI", 6.5F + (i * 2F));
                var grosor = grosores[i];
                boton.Click += delegate { SelectWidth(grosor); };
                widthButtons.Add(boton);
            }

            undoButton = CreateToolbarButton(
                "\u21B6",
                "Deshacer la ultima marca",
                128,
                42);
            undoButton.Font = new Font("Segoe UI", 11F);
            undoButton.Click += delegate { UndoLast(); };

            saveButton = new Button();
            saveButton.Left = 168;
            saveButton.Top = 42;
            saveButton.Width = 160;
            saveButton.Height = 26;
            saveButton.Text = "Guardar marcas";
            saveButton.FlatStyle = FlatStyle.Flat;
            saveButton.BackColor = Color.FromArgb(31, 31, 29);
            saveButton.ForeColor = Color.White;
            saveButton.TabStop = false;
            saveButton.Cursor = Cursors.Hand;
            saveButton.FlatAppearance.BorderSize = 0;
            saveButton.AccessibleName = "Guardar las marcas en el PDF";
            saveButton.Click += delegate
            {
                var handler = SaveRequested;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            };
            toolbar.Controls.Add(saveButton);

            pendingLabel = new Label();
            pendingLabel.Left = 128;
            pendingLabel.Top = 10;
            pendingLabel.Width = 200;
            pendingLabel.Height = 0;
            pendingLabel.Visible = false;
            toolbar.Controls.Add(pendingLabel);

            renderer.Controls.Add(toolbar);
            renderer.SizeChanged += delegate { PositionToolbar(); };
        }

        private Button CreateToolbarButton(
            string texto,
            string descripcion,
            int left,
            int top)
        {
            var boton = new Button();
            boton.Left = left;
            boton.Top = top;
            boton.Width = 30;
            boton.Height = 26;
            boton.Text = texto;
            boton.Font = new Font("Segoe MDL2 Assets", 10.5F);
            boton.FlatStyle = FlatStyle.Flat;
            boton.BackColor = Color.FromArgb(250, 249, 247);
            boton.ForeColor = Color.FromArgb(31, 31, 29);
            boton.TabStop = false;
            boton.Cursor = Cursors.Hand;
            boton.FlatAppearance.BorderColor = Color.FromArgb(211, 209, 204);
            boton.AccessibleName = descripcion;
            toolbarTip.SetToolTip(boton, descripcion);
            toolbar.Controls.Add(boton);
            return boton;
        }

        private void PositionToolbar()
        {
            if (toolbar == null)
            {
                return;
            }

            toolbar.Left = Math.Max(8, renderer.ClientSize.Width - toolbar.Width - 20);
            toolbar.Top = 12;
        }

        private void SelectTool(PdfAnnotationKind tool)
        {
            Tool = tool;
            CancelCurrent();
            AplicarCursor();
            RefreshToolbar();
            renderer.Focus();
        }

        /// <summary>
        /// Cada herramienta lleva su cursor: el subrayador trabaja sobre texto,
        /// asi que usa el mismo que cualquier seleccion de texto.
        /// </summary>
        private void AplicarCursor()
        {
            renderer.Cursor = CursorDeLaHerramienta();
        }

        private Cursor CursorDeLaHerramienta()
        {
            if (!active)
            {
                return Cursors.Default;
            }
            if (Tool == PdfAnnotationKind.Highlight)
            {
                return Cursors.IBeam;
            }
            if (Tool == PdfAnnotationKind.Note)
            {
                return Cursors.Hand;
            }

            return Cursors.Cross;
        }

        private void SelectColor(Color color)
        {
            if (Tool == PdfAnnotationKind.Highlight)
            {
                HighlightColor = color;
            }
            else if (Tool == PdfAnnotationKind.Note)
            {
                NoteColor = color;
            }
            else
            {
                InkColor = color;
            }

            RefreshToolbar();
            renderer.Focus();
        }

        private void SelectWidth(float width)
        {
            WidthPoints = width;
            RefreshToolbar();
            renderer.Focus();
        }

        private void RefreshToolbar()
        {
            if (toolbar == null)
            {
                return;
            }

            MarkSelected(inkButton, Tool == PdfAnnotationKind.Ink);
            MarkSelected(highlightButton, Tool == PdfAnnotationKind.Highlight);
            MarkSelected(noteButton, Tool == PdfAnnotationKind.Note);

            var activo = CurrentColor();
            foreach (var boton in colorButtons)
            {
                boton.FlatAppearance.BorderSize =
                    boton.BackColor.ToArgb() == activo.ToArgb() ? 3 : 1;
                boton.FlatAppearance.BorderColor =
                    boton.BackColor.ToArgb() == activo.ToArgb()
                        ? Color.FromArgb(31, 31, 29)
                        : Color.FromArgb(211, 209, 204);
            }

            var grosores = new[] { 1F, 2F, 4F };
            for (var i = 0; i < widthButtons.Count; i++)
            {
                MarkSelected(
                    widthButtons[i],
                    Math.Abs(WidthPoints - grosores[i]) < 0.01F);
                // El grosor no se aplica a las notas, que son un icono fijo.
                widthButtons[i].Enabled = Tool != PdfAnnotationKind.Note;
            }

            undoButton.Enabled = pending.HasPending;
            saveButton.Enabled = pending.HasPending;
            saveButton.Text = pending.HasPending
                ? "Guardar " + pending.Describe()
                : "Guardar marcas";
        }

        private Color CurrentColor()
        {
            if (Tool == PdfAnnotationKind.Highlight)
            {
                return HighlightColor;
            }

            return Tool == PdfAnnotationKind.Note ? NoteColor : InkColor;
        }

        private static void MarkSelected(Button boton, bool seleccionado)
        {
            if (boton == null)
            {
                return;
            }

            boton.BackColor = seleccionado
                ? Color.FromArgb(251, 236, 231)
                : Color.FromArgb(250, 249, 247);
            boton.FlatAppearance.BorderColor = seleccionado
                ? Color.FromArgb(238, 91, 61)
                : Color.FromArgb(211, 209, 204);
            boton.FlatAppearance.BorderSize = seleccionado ? 2 : 1;
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
            catch (NullReferenceException)
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

        private void RaisePendingChanged()
        {
            RefreshToolbar();
            var handler = PendingChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Dibuja todas las marcas de una pagina: las que ya estaban en el
        /// documento, las pendientes de guardar y la que se esta trazando.
        /// </summary>
        private void DrawPage(
            PdfRenderer targetRenderer,
            Graphics graphics,
            int pageIndex)
        {
            var anterior = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            try
            {
                foreach (var item in saved)
                {
                    if (item.PageNumber - 1 == pageIndex)
                    {
                        DrawItem(targetRenderer, graphics, item, false);
                    }
                }

                foreach (var item in pending.Items)
                {
                    if (item.PageNumber - 1 == pageIndex)
                    {
                        DrawItem(targetRenderer, graphics, item, true);
                    }
                }

                if (current != null && currentPage == pageIndex)
                {
                    DrawItem(targetRenderer, graphics, current, true);
                }
            }
            finally
            {
                graphics.SmoothingMode = anterior;
            }
        }

        private static void DrawItem(
            PdfRenderer targetRenderer,
            Graphics graphics,
            PdfAnnotationItem item,
            bool pendiente)
        {
            if (item.Kind == PdfAnnotationKind.Ink)
            {
                DrawInk(targetRenderer, graphics, item);
                return;
            }

            var bounds = ToClientRectangle(targetRenderer, item);
            if (bounds.Width < 1 || bounds.Height < 1)
            {
                return;
            }

            if (item.Kind == PdfAnnotationKind.Highlight)
            {
                using (var brocha = new SolidBrush(
                    Color.FromArgb(
                        (int)(Math.Max(0.1F, item.Opacity) * 255F),
                        item.Color)))
                {
                    if (item.Quads.Count == 0)
                    {
                        graphics.FillRectangle(brocha, bounds);
                        return;
                    }

                    // Un rectangulo por linea subrayada, no uno que englobe
                    // los margenes de todas.
                    foreach (var tramo in item.Quads)
                    {
                        var caja = targetRenderer.BoundsFromPdf(
                            new PdfiumViewer.PdfRectangle(
                                item.PageNumber - 1,
                                tramo));
                        if (caja.Width > 0 && caja.Height > 0)
                        {
                            graphics.FillRectangle(brocha, caja);
                        }
                    }
                }

                return;
            }

            DrawNoteIcon(graphics, bounds, item, pendiente);
        }

        private static void DrawInk(
            PdfRenderer targetRenderer,
            Graphics graphics,
            PdfAnnotationItem item)
        {
            var grosor = Math.Max(
                1F,
                item.WidthPoints * (float)targetRenderer.Zoom);
            using (var lapiz = new Pen(item.Color, grosor))
            {
                lapiz.StartCap = LineCap.Round;
                lapiz.EndCap = LineCap.Round;
                lapiz.LineJoin = LineJoin.Round;

                foreach (var stroke in item.Strokes)
                {
                    if (stroke.Count < 2)
                    {
                        continue;
                    }

                    var puntos = new Point[stroke.Count];
                    for (var i = 0; i < stroke.Count; i++)
                    {
                        puntos[i] = targetRenderer.PointFromPdf(
                            new PdfPoint(item.PageNumber - 1, stroke[i]));
                    }

                    graphics.DrawLines(lapiz, puntos);
                }
            }
        }

        private static void DrawNoteIcon(
            Graphics graphics,
            Rectangle bounds,
            PdfAnnotationItem item,
            bool pendiente)
        {
            // Tamaño fijo en pantalla: una nota debe verse igual a cualquier
            // zoom, como un chincheta sobre el plano.
            var lado = 18;
            var icono = new Rectangle(
                bounds.Left,
                bounds.Top - lado,
                lado,
                lado);

            using (var relleno = new SolidBrush(item.Color))
            using (var borde = new Pen(
                Color.FromArgb(90, 0, 0, 0),
                pendiente ? 1.6f : 1f))
            {
                graphics.FillEllipse(relleno, icono);
                graphics.DrawEllipse(borde, icono);
            }

            using (var trazo = new Pen(Color.White, 1.4f))
            {
                for (var linea = 0; linea < 3; linea++)
                {
                    var y = icono.Top + 5 + (linea * 4);
                    graphics.DrawLine(
                        trazo,
                        icono.Left + 4,
                        y,
                        icono.Right - 4,
                        y);
                }
            }
        }

        private static Rectangle ToClientRectangle(
            PdfRenderer targetRenderer,
            PdfAnnotationItem item)
        {
            return targetRenderer.BoundsFromPdf(
                new PdfiumViewer.PdfRectangle(
                    item.PageNumber - 1,
                    item.Area));
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
                if (!renderer.IsDisposed && renderer.Markers != null)
                {
                    foreach (var marker in pageMarkers.Values)
                    {
                        renderer.Markers.Remove(marker);
                    }
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (NullReferenceException)
            {
            }

            pageMarkers.Clear();
            pending.Clear();
            saved.Clear();
            renderer.Disposed -= Renderer_Disposed;
        }

        private sealed class AnnotationPageMarker : IPdfMarker
        {
            private readonly PdfAnnotationController owner;
            private readonly int page;

            public AnnotationPageMarker(
                PdfAnnotationController owner,
                int page)
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
}

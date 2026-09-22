using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Windows.Forms;
using PdfiumViewer;

namespace FirmaAutomatica
{
    /// <summary>
    /// Seleccionar texto con el raton y copiarlo, como en cualquier visor.
    ///
    /// Trabaja en modo neutro, sin herramienta encendida: arrastrar sobre una
    /// linea de texto la selecciona. Donde no hay texto no se entromete, y el
    /// zoom por rectangulo sigue funcionando como siempre; de eso se encarga
    /// <see cref="HasTextAt"/>, que el visor consulta antes de empezar el
    /// gesto del zoom.
    ///
    /// La seleccion se queda en la pagina donde empezo. Arrastrar de una
    /// pagina a otra es raro al copiar una referencia, un parrafo o un
    /// cajetin, y permitirlo obligaria a leer paginas enteras mientras se
    /// mueve el raton.
    /// </summary>
    internal sealed class PdfTextSelectionController : IMessageFilter, IDisposable
    {
        private const int WmLButtonDown = 0x0201;
        private const int WmMouseMove = 0x0200;
        private const int WmLButtonUp = 0x0202;
        private const int WmKeyDown = 0x0100;
        private const int WmLButtonDblClk = 0x0203;

        private static readonly Color SelectionColor =
            Color.FromArgb(238, 91, 61);

        private readonly PdfRenderer renderer;
        private readonly Func<bool> canSelect;
        private readonly Func<int, IList<PdfTextBlock>> loadBlocks;
        private readonly Action<string> reportStatus;
        private readonly Dictionary<int, IList<PdfTextBlock>> blocksByPage =
            new Dictionary<int, IList<PdfTextBlock>>();
        private readonly Dictionary<int, SelectionMarker> pageMarkers =
            new Dictionary<int, SelectionMarker>();
        private readonly List<RectangleF> selectionQuads =
            new List<RectangleF>();

        private PdfRendererCursorOverride cursorOverride;
        private bool disposed;
        private bool dragging;
        private int selectionPage = -1;
        private PosicionEnTexto anchor;
        private PosicionEnTexto head;
        private string selectedText = string.Empty;

        public PdfTextSelectionController(
            PdfRenderer renderer,
            Func<bool> canSelect,
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
            this.canSelect = canSelect;
            this.loadBlocks = loadBlocks;
            this.reportStatus = reportStatus;

            // Sobre texto, cursor de texto. Es lo unico que anuncia que se
            // puede seleccionar, y ademas avisa de cuando el arrastre va a
            // seleccionar en vez de hacer zoom por rectangulo.
            cursorOverride = new PdfRendererCursorOverride(
                renderer,
                delegate
                {
                    if (disposed || !PuedeSeleccionar())
                    {
                        return null;
                    }

                    if (dragging)
                    {
                        return Cursors.IBeam;
                    }

                    return HasTextAt(renderer.PointToClient(Cursor.Position))
                        ? Cursors.IBeam
                        : null;
                });

            renderer.Disposed += Renderer_Disposed;
            Application.AddMessageFilter(this);
        }

        /// <summary>Hay texto seleccionado ahora mismo.</summary>
        public bool HasSelection
        {
            get { return selectedText.Length > 0; }
        }

        /// <summary>Texto seleccionado, con un salto por renglon.</summary>
        public string SelectedText
        {
            get { return selectedText; }
        }

        /// <summary>
        /// Hay una linea de texto bajo ese punto del visor. El zoom por
        /// rectangulo lo consulta para cederle el arrastre.
        /// </summary>
        public bool HasTextAt(Point clientLocation)
        {
            if (disposed || !PuedeSeleccionar())
            {
                return false;
            }

            try
            {
                var punto = renderer.PointToPdf(clientLocation);
                if (punto.Page < 0)
                {
                    return false;
                }

                var bloques = EnsureBlocks(punto.Page + 1);
                foreach (var bloque in bloques)
                {
                    if (bloque.Contains(punto.Location.X, punto.Location.Y))
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
                return false;
            }

            return false;
        }

        /// <summary>
        /// Las lineas guardadas dejan de valer: el documento ha cambiado de
        /// revision o de pagina.
        /// </summary>
        public void InvalidateBlocks()
        {
            blocksByPage.Clear();
            ClearSelection();
        }

        public void ClearSelection()
        {
            if (selectionQuads.Count == 0 && selectedText.Length == 0)
            {
                return;
            }

            selectionQuads.Clear();
            selectedText = string.Empty;
            selectionPage = -1;
            anchor = null;
            head = null;
            Refresh();
        }

        /// <summary>Selecciona todo el texto de la pagina que se esta viendo.</summary>
        public bool SelectCurrentPage()
        {
            if (disposed || !PuedeSeleccionar())
            {
                return false;
            }

            var pagina = renderer.Page + 1;
            var bloques = EnsureBlocks(pagina);
            if (bloques.Count == 0)
            {
                Report("Esta página no tiene texto que seleccionar.");
                return false;
            }

            selectionPage = pagina;
            anchor = new PosicionEnTexto(0, 0);
            head = new PosicionEnTexto(
                bloques.Count - 1,
                bloques[bloques.Count - 1].CharacterBounds.Count);
            RebuildSelection();
            Report(
                "Seleccionada la página " +
                pagina.ToString(System.Globalization.CultureInfo.CurrentCulture) +
                " · Ctrl+C para copiar.");
            return true;
        }

        /// <summary>Copia lo seleccionado al portapapeles.</summary>
        public bool CopySelection()
        {
            if (disposed || !HasSelection)
            {
                Report("No hay texto seleccionado.");
                return false;
            }

            try
            {
                // SetText con cadena vacia lanza, y el portapapeles puede
                // estar tomado por otro programa: ninguna de las dos cosas
                // puede tumbar el visor.
                Clipboard.SetText(selectedText);
                var caracteres = selectedText.Length;
                Report(
                    caracteres == 1
                        ? "Copiado 1 carácter."
                        : "Copiados " +
                            caracteres.ToString(
                                System.Globalization.CultureInfo.CurrentCulture) +
                            " caracteres.");
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Write("No se pudo copiar al portapapeles: " + ex);
                Report(
                    "No se pudo copiar: otro programa tiene tomado el " +
                    "portapapeles.");
                return false;
            }
        }

        public bool PreFilterMessage(ref Message message)
        {
            if (disposed)
            {
                return false;
            }

            if (message.Msg == WmKeyDown && HasSelection)
            {
                var tecla = (Keys)(int)message.WParam;
                if (tecla == Keys.Escape)
                {
                    ClearSelection();
                    return true;
                }
            }

            if (!renderer.IsHandleCreated ||
                message.HWnd != renderer.Handle)
            {
                return false;
            }

            var location = renderer.PointToClient(Cursor.Position);
            switch (message.Msg)
            {
                case WmLButtonDown:
                    return HandleMouseDown(location);

                case WmLButtonDblClk:
                    return HandleDoubleClick(location);

                case WmMouseMove:
                    return HandleMouseMove(location);

                case WmLButtonUp:
                    return HandleMouseUp(location);

                default:
                    return false;
            }
        }

        private bool HandleMouseDown(Point location)
        {
            if (!PuedeSeleccionar())
            {
                return false;
            }

            var posicion = LocalizarEnPantalla(location, true);
            if (posicion == null)
            {
                // Pinchar fuera del texto quita la seleccion, pero no se
                // consume el clic: ahi manda el zoom por rectangulo.
                ClearSelection();
                return false;
            }

            var punto = renderer.PointToPdf(location);
            selectionPage = punto.Page + 1;
            anchor = posicion;
            head = posicion;
            dragging = true;
            renderer.Capture = true;
            selectionQuads.Clear();
            selectedText = string.Empty;
            Refresh();
            return true;
        }

        private bool HandleDoubleClick(Point location)
        {
            if (!PuedeSeleccionar())
            {
                return false;
            }

            var posicion = LocalizarEnPantalla(location, true);
            if (posicion == null)
            {
                return false;
            }

            var punto = renderer.PointToPdf(location);
            var pagina = punto.Page + 1;
            var bloques = EnsureBlocks(pagina);
            if (posicion.Linea < 0 || posicion.Linea >= bloques.Count)
            {
                return false;
            }

            // Doble clic selecciona la palabra, como en cualquier lector.
            var bloque = bloques[posicion.Linea];
            var texto = bloque.Text ?? string.Empty;
            var indice = Math.Max(
                0,
                Math.Min(texto.Length - 1, posicion.Caracter));
            if (texto.Length == 0)
            {
                return false;
            }

            var inicio = indice;
            while (inicio > 0 && !EsSeparador(texto[inicio - 1]))
            {
                inicio--;
            }

            var fin = indice;
            while (fin < texto.Length && !EsSeparador(texto[fin]))
            {
                fin++;
            }

            if (fin <= inicio)
            {
                return false;
            }

            selectionPage = pagina;
            anchor = new PosicionEnTexto(posicion.Linea, inicio);
            head = new PosicionEnTexto(posicion.Linea, fin);
            dragging = false;
            RebuildSelection();
            return true;
        }

        private static bool EsSeparador(char caracter)
        {
            return char.IsWhiteSpace(caracter) ||
                caracter == ',' ||
                caracter == ';' ||
                caracter == ':' ||
                caracter == '(' ||
                caracter == ')' ||
                caracter == '"';
        }

        private bool HandleMouseMove(Point location)
        {
            if (!dragging)
            {
                return false;
            }

            var posicion = LocalizarEnPantalla(location, false);
            if (posicion != null)
            {
                head = posicion;
                RebuildSelection();
            }

            // Se consume el arrastre entero para que el visor no desplace la
            // pagina por debajo mientras se selecciona.
            return true;
        }

        private bool HandleMouseUp(Point location)
        {
            if (!dragging)
            {
                return false;
            }

            dragging = false;
            renderer.Capture = false;
            if (HasSelection)
            {
                Report(
                    "Texto seleccionado · Ctrl+C para copiar, Esc para " +
                    "soltarlo.");
            }

            return true;
        }

        /// <summary>
        /// Linea y caracter bajo un punto del visor, dentro de la pagina donde
        /// empezo la seleccion.
        /// </summary>
        private PosicionEnTexto LocalizarEnPantalla(
            Point location,
            bool exigirTextoDebajo)
        {
            PdfPoint punto;
            try
            {
                punto = renderer.PointToPdf(location);
            }
            catch (Exception)
            {
                return null;
            }

            if (punto.Page < 0)
            {
                return null;
            }

            var pagina = punto.Page + 1;
            if (selectionPage > 0 && pagina != selectionPage)
            {
                // Salirse a otra pagina no amplia la seleccion: se queda en el
                // borde de la que se estaba leyendo.
                return null;
            }

            var bloques = EnsureBlocks(pagina);
            if (bloques.Count == 0)
            {
                return null;
            }

            for (var i = 0; i < bloques.Count; i++)
            {
                if (bloques[i].Contains(punto.Location.X, punto.Location.Y))
                {
                    return new PosicionEnTexto(
                        i,
                        bloques[i].NearestCharacterIndex(punto.Location.X));
                }
            }

            if (exigirTextoDebajo)
            {
                return null;
            }

            // Arrastrando ya, el raton se sale del renglon constantemente: se
            // toma la linea mas cercana, que es lo que hace cualquier
            // seleccion de texto.
            var mejor = -1;
            var distancia = float.MaxValue;
            for (var i = 0; i < bloques.Count; i++)
            {
                var centro = bloques[i].Bounds.Top +
                    (bloques[i].Bounds.Height / 2F);
                var d = Math.Abs(centro - punto.Location.Y);
                if (d < distancia)
                {
                    distancia = d;
                    mejor = i;
                }
            }

            if (mejor < 0)
            {
                return null;
            }

            return new PosicionEnTexto(
                mejor,
                bloques[mejor].NearestCharacterIndex(punto.Location.X));
        }

        /// <summary>
        /// Rehace los tramos marcados y el texto entre el ancla y el extremo:
        /// la primera linea desde donde se empezo, las de en medio enteras y
        /// la ultima hasta donde va el raton.
        /// </summary>
        private void RebuildSelection()
        {
            selectionQuads.Clear();
            selectedText = string.Empty;
            if (anchor == null || head == null || selectionPage < 1)
            {
                Refresh();
                return;
            }

            var bloques = EnsureBlocks(selectionPage);
            if (bloques.Count == 0)
            {
                Refresh();
                return;
            }

            var a = anchor;
            var b = head;
            if (a.Linea > b.Linea ||
                (a.Linea == b.Linea && a.Caracter > b.Caracter))
            {
                a = head;
                b = anchor;
            }

            var texto = new StringBuilder();
            for (var i = a.Linea; i <= b.Linea && i < bloques.Count; i++)
            {
                if (i < 0)
                {
                    continue;
                }

                var bloque = bloques[i];
                var total = bloque.CharacterBounds.Count;
                var inicio = i == a.Linea ? a.Caracter : 0;
                var fin = i == b.Linea ? b.Caracter : total;
                inicio = Math.Max(0, Math.Min(total, inicio));
                fin = Math.Max(0, Math.Min(total, fin));
                if (fin <= inicio)
                {
                    continue;
                }

                var tramo = bloque.SpanBounds(inicio, fin);
                if (tramo.Width > 0.01F && tramo.Height > 0.01F)
                {
                    selectionQuads.Add(tramo);
                }

                var contenido = bloque.Text ?? string.Empty;
                var desde = Math.Max(0, Math.Min(contenido.Length, inicio));
                var hasta = Math.Max(desde, Math.Min(contenido.Length, fin));
                if (hasta > desde)
                {
                    if (texto.Length > 0)
                    {
                        texto.Append("\r\n");
                    }

                    texto.Append(contenido.Substring(desde, hasta - desde));
                }
            }

            selectedText = texto.ToString();
            EnsureMarker(selectionPage - 1);
            Refresh();
        }

        private bool PuedeSeleccionar()
        {
            if (disposed)
            {
                return false;
            }

            try
            {
                return canSelect == null || canSelect();
            }
            catch (Exception)
            {
                return false;
            }
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

        private void EnsureMarker(int pageIndex)
        {
            if (pageIndex < 0 || pageMarkers.ContainsKey(pageIndex))
            {
                return;
            }

            var marker = new SelectionMarker(this, pageIndex);
            pageMarkers[pageIndex] = marker;
            try
            {
                renderer.Markers.Add(marker);
            }
            catch (Exception)
            {
                pageMarkers.Remove(pageIndex);
            }
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
            catch (Exception)
            {
            }
        }

        private void DrawPage(
            PdfRenderer targetRenderer,
            Graphics graphics,
            int pageIndex)
        {
            if (selectionQuads.Count == 0 ||
                selectionPage - 1 != pageIndex)
            {
                return;
            }

            var anterior = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.None;
            try
            {
                // Relleno translucido, como la seleccion de cualquier lector:
                // deja leer el texto que hay debajo.
                using (var relleno = new SolidBrush(
                    Color.FromArgb(70, SelectionColor)))
                {
                    foreach (var tramo in selectionQuads)
                    {
                        var caja = targetRenderer.BoundsFromPdf(
                            new PdfiumViewer.PdfRectangle(pageIndex, tramo));
                        if (caja.Width < 1 || caja.Height < 1)
                        {
                            continue;
                        }

                        graphics.FillRectangle(relleno, caja);
                    }
                }
            }
            finally
            {
                graphics.SmoothingMode = anterior;
            }
        }

        private void Report(string mensaje)
        {
            if (reportStatus != null && !string.IsNullOrEmpty(mensaje))
            {
                reportStatus(mensaje);
            }
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

                renderer.Disposed -= Renderer_Disposed;
            }
            catch (Exception)
            {
            }

            pageMarkers.Clear();
            blocksByPage.Clear();
            selectionQuads.Clear();
        }

        /// <summary>Una posicion dentro del texto de una pagina.</summary>
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

        private sealed class SelectionMarker : IPdfMarker
        {
            private readonly PdfTextSelectionController owner;
            private readonly int page;

            public SelectionMarker(
                PdfTextSelectionController owner,
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

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.Globalization;
using System.IO;
using System.Threading;
using PdfiumViewer;

namespace FirmaAutomatica
{
    /// <summary>
    /// Envuelve el documento que ve el visor para que navegar y hacer zoom no
    /// se quede parado esperando a PDFium.
    ///
    /// EL PROBLEMA, MEDIDO. PdfRenderer guarda la imagen de cada pagina, pero
    /// la tira en cuanto cambia el zoom o entra una pagina nueva, y la vuelve
    /// a pedir en el hilo de la interfaz. Con los planos de un proyecto —A1 y
    /// A0— cada rasterizacion cuesta entre 220 y 350 ms: un paso de zoom con
    /// dos hojas a la vista se iba a 620 ms y pasar de pagina a 300. Eso es
    /// lo que se nota como que el programa va a tirones.
    ///
    /// LO QUE HACEN LOS VISORES RAPIDOS, y lo que se hace aqui: no bloquear
    /// nunca. Al pedir una pagina a un tamaño que no esta hecho, se devuelve
    /// al instante la ultima imagen que haya de esa pagina, reescalada —se ve
    /// un momento mas blanda, como en cualquier visor— y se encarga la
    /// version buena a un hilo aparte. Cuando esta lista se sustituye y se
    /// repinta, ya nitida.
    ///
    /// Ademas se van preparando las paginas de al lado mientras no se toca
    /// nada, asi que avanzar por el documento las encuentra ya hechas.
    ///
    /// PDFIUM NO ES SEGURO ENTRE HILOS, y el contexto del proyecto lo deja
    /// dicho. Todas las llamadas pasan por un mismo candado, y el hilo de
    /// preparacion solo entra cuando la interfaz lleva un rato quieta: un
    /// trabajo especulativo no puede hacer esperar a quien mueve la rueda.
    /// </summary>
    internal sealed class PdfFluidDocument : IPdfDocument
    {
        /// <summary>
        /// Paginas guardadas. Una hoja A1 a tamaño de pantalla ronda los 2 MB,
        /// asi que doce caben de sobra y cubren el ir y venir por un proyecto.
        /// </summary>
        private const int MaximumCachedPages = 12;

        /// <summary>
        /// Lo que tiene que llevar quieta la interfaz para que el hilo de
        /// preparacion se atreva a ocupar PDFium.
        ///
        /// Tiene que ser mas largo que una rafaga de zoom: mientras se esta
        /// tocando, preparar paginas de al lado solo quita sitio. Leyendo una
        /// hoja se pasa de sobra.
        /// </summary>
        private const int IdleBeforeBackgroundMs = 350;

        /// <summary>
        /// Diferencia de tamaño por debajo de la cual no merece la pena
        /// rehacer la imagen: reescalar un 3 % no se ve.
        /// </summary>
        private const double AcceptableScaleSlack = 0.03D;

        /// <summary>
        /// Por encima de esto, montar la imagen reescalada cuesta tanto como
        /// pedirsela a PDFium, asi que no se usa el apaño. Dos megapixeles y
        /// medio cubren una pantalla grande con holgura.
        /// </summary>
        private const long MaximumScaledPixels = 2500000L;

        private readonly IPdfDocument inner;
        private readonly object pdfiumLock = new object();
        private readonly object stateLock = new object();
        private readonly Dictionary<int, PageRender> cache =
            new Dictionary<int, PageRender>();
        private readonly Dictionary<int, RenderRequest> pending =
            new Dictionary<int, RenderRequest>();
        private readonly AutoResetEvent workSignal = new AutoResetEvent(false);

        private Thread worker;
        private volatile bool disposed;
        private int currentPage;
        private long lastUiRenderTicks;

        /// <param name="onPageRefined">
        /// Se llama, desde el hilo de preparacion, cuando una pagina ya esta
        /// disponible a su tamaño exacto. El visor debe tirar su copia de esa
        /// pagina y repintar.
        /// </param>
        public PdfFluidDocument(
            IPdfDocument inner,
            Action<int> onPageRefined)
        {
            if (inner == null)
            {
                throw new ArgumentNullException("inner");
            }

            this.inner = inner;
            PageRefined = onPageRefined;
        }

        /// <summary>Aviso de que una pagina ya esta a su tamaño exacto.</summary>
        public Action<int> PageRefined { get; set; }

        /// <summary>
        /// Cuenta que se ha decidido en cada peticion. Solo lo usan los
        /// bancos de pruebas; en la aplicacion va a null y no cuesta nada.
        /// </summary>
        public Action<string> Trace { get; set; }

        private void Say(string que)
        {
            var traza = Trace;
            if (traza != null)
            {
                try
                {
                    traza(que);
                }
                catch (Exception)
                {
                }
            }
        }

        /// <summary>Veces que se ha llamado de verdad a PDFium. Para pruebas.</summary>
        public int RasterizedCount { get; private set; }

        /// <summary>Veces servidas escalando lo que ya habia. Para pruebas.</summary>
        public int ScaledCount { get; private set; }

        /// <summary>Veces servidas con la imagen exacta. Para pruebas.</summary>
        public int ExactCount { get; private set; }

        public int CachedPageCount
        {
            get
            {
                lock (stateLock)
                {
                    return cache.Count;
                }
            }
        }

        /// <summary>Espera a que no quede trabajo pendiente. Para pruebas.</summary>
        public bool WaitForIdle(int milliseconds)
        {
            var limite = DateTime.UtcNow.AddMilliseconds(milliseconds);
            while (DateTime.UtcNow < limite)
            {
                lock (stateLock)
                {
                    if (pending.Count == 0)
                    {
                        return true;
                    }
                }

                Thread.Sleep(20);
            }

            return false;
        }

        // --- El render que pide el visor --------------------------------

        public Image Render(
            int page,
            int width,
            int height,
            float dpiX,
            float dpiY,
            PdfRotation rotate,
            PdfRenderFlags flags)
        {
            return Serve(page, width, height, dpiX, dpiY, rotate, flags);
        }

        public Image Render(
            int page,
            int width,
            int height,
            float dpiX,
            float dpiY,
            PdfRenderFlags flags)
        {
            return Serve(
                page, width, height, dpiX, dpiY, PdfRotation.Rotate0, flags);
        }

        private Image Serve(
            int page,
            int width,
            int height,
            float dpiX,
            float dpiY,
            PdfRotation rotate,
            PdfRenderFlags flags)
        {
            if (width <= 0 || height <= 0 || disposed)
            {
                return RasterizeNow(
                    page, width, height, dpiX, dpiY, rotate, flags);
            }

            currentPage = page;
            Interlocked.Exchange(
                ref lastUiRenderTicks, DateTime.UtcNow.Ticks);

            PageRender guardada;
            lock (stateLock)
            {
                cache.TryGetValue(page, out guardada);
            }

            if (guardada != null &&
                guardada.Matches(rotate, flags) &&
                guardada.CloseEnough(width, height))
            {
                var exacta = guardada.Extract(width, height);
                if (exacta != null)
                {
                    ExactCount++;
                    Say("exacta p" + page + " " + width + "x" + height);
                    RequestNeighbours(page, width, dpiX, dpiY, rotate, flags);
                    return exacta;
                }
            }

            // El apaño reescalado solo compensa mientras sea barato. Por
            // encima de unos pocos megapixeles, montar la imagen cuesta tanto
            // como rasterizarla —medido en un A0 muy ampliado—, y entonces
            // mas vale hacerla bien a la primera.
            var caro = (long)width * height > MaximumScaledPixels;

            if (!caro && guardada != null && guardada.Matches(rotate, flags))
            {
                // Hay algo de esta pagina, a otro tamaño: se devuelve ya,
                // reescalado, y se encarga la version buena. Es el momento
                // "un pelin blando" que tienen todos los visores y que dura
                // lo que tarde el hilo de atras.
                var aproximada = guardada.Extract(width, height);
                if (aproximada != null)
                {
                    ScaledCount++;
                    Say("escalada p" + page + " " + width + "x" + height +
                        " desde " + guardada.Width + "x" + guardada.Height);
                    // Solo la version buena de ESTA pagina. Las vecinas se
                    // dejan para cuando la vista se asiente: mientras se
                    // mueve el zoom, prepararlas solo quita sitio.
                    RequestExact(
                        page, width, height, dpiX, dpiY, rotate, flags);
                    return aproximada;
                }
            }

            // Primera vez que se ve esta pagina: no hay de donde sacar nada,
            // asi que toca rasterizar aqui mismo. Es lo que evita el
            // preparador con las paginas de al lado.
            Say("RASTERIZA p" + page + " " + width + "x" + height +
                (guardada == null
                    ? " (no habia nada)"
                    : caro
                        ? " (demasiado grande para apañar)"
                        : " (habia pero no encaja: rot/flags)"));
            var recien = RasterizeAndStore(
                page, width, height, dpiX, dpiY, rotate, flags);
            RequestNeighbours(page, width, dpiX, dpiY, rotate, flags);
            if (recien != null)
            {
                return recien;
            }

            return RasterizeNow(
                page, width, height, dpiX, dpiY, rotate, flags);
        }

        private Image RasterizeAndStore(
            int page,
            int width,
            int height,
            float dpiX,
            float dpiY,
            PdfRotation rotate,
            PdfRenderFlags flags)
        {
            Image cruda;
            try
            {
                lock (pdfiumLock)
                {
                    RasterizedCount++;
                    cruda = inner.Render(
                        page, width, height, dpiX, dpiY, rotate, flags);
                }
            }
            catch (Exception ex)
            {
                AppLog.Write(
                    "No se pudo rasterizar la página " + page + ": " + ex);
                return null;
            }

            if (cruda == null)
            {
                return null;
            }

            var entrada = new PageRender(cruda, rotate, flags);
            Store(page, entrada);
            return entrada.Extract(width, height);
        }

        private Image RasterizeNow(
            int page,
            int width,
            int height,
            float dpiX,
            float dpiY,
            PdfRotation rotate,
            PdfRenderFlags flags)
        {
            lock (pdfiumLock)
            {
                RasterizedCount++;
                return inner.Render(
                    page, width, height, dpiX, dpiY, rotate, flags);
            }
        }

        private void Store(int page, PageRender entrada)
        {
            PageRender anterior = null;
            var sobrantes = new List<PageRender>();
            lock (stateLock)
            {
                if (cache.TryGetValue(page, out anterior))
                {
                    sobrantes.Add(anterior);
                }

                cache[page] = entrada;

                while (cache.Count > MaximumCachedPages)
                {
                    var peor = -1;
                    var peorDistancia = -1;
                    foreach (var par in cache)
                    {
                        var distancia = Math.Abs(par.Key - currentPage);
                        if (distancia > peorDistancia)
                        {
                            peorDistancia = distancia;
                            peor = par.Key;
                        }
                    }

                    if (peor < 0)
                    {
                        break;
                    }

                    sobrantes.Add(cache[peor]);
                    cache.Remove(peor);
                }
            }

            foreach (var sobrante in sobrantes)
            {
                sobrante.Dispose();
            }
        }

        // --- Trabajo de fondo -------------------------------------------

        private void RequestExact(
            int page,
            int width,
            int height,
            float dpiX,
            float dpiY,
            PdfRotation rotate,
            PdfRenderFlags flags)
        {
            Enqueue(
                new RenderRequest(
                    page, width, height, dpiX, dpiY, rotate, flags, true));
        }

        /// <summary>
        /// Encarga las paginas de al lado al mismo aumento. El tamaño se
        /// deduce de la escala, no de los pixeles: las hojas de un mismo
        /// documento pueden medir distinto.
        /// </summary>
        private void RequestNeighbours(
            int page,
            int width,
            float dpiX,
            float dpiY,
            PdfRotation rotate,
            PdfRenderFlags flags)
        {
            double escala;
            try
            {
                var tamanos = inner.PageSizes;
                if (page < 0 || page >= tamanos.Count)
                {
                    return;
                }

                var ancho = tamanos[page].Width;
                if (ancho <= 0.01F)
                {
                    return;
                }

                escala = width / ancho;
            }
            catch (Exception)
            {
                return;
            }

            var total = inner.PageCount;
            foreach (var vecina in new[] { page + 1, page - 1, page + 2 })
            {
                if (vecina < 0 || vecina >= total)
                {
                    continue;
                }

                lock (stateLock)
                {
                    PageRender ya;
                    if (cache.TryGetValue(vecina, out ya) &&
                        ya.Matches(rotate, flags))
                    {
                        continue;
                    }
                }

                try
                {
                    var tamano = inner.PageSizes[vecina];
                    var ancho = Math.Max(
                        1, (int)Math.Round(tamano.Width * escala));
                    var alto = Math.Max(
                        1, (int)Math.Round(tamano.Height * escala));
                    Enqueue(
                        new RenderRequest(
                            vecina, ancho, alto, dpiX, dpiY,
                            rotate, flags, false));
                }
                catch (Exception)
                {
                }
            }
        }

        private void Enqueue(RenderRequest peticion)
        {
            if (disposed)
            {
                return;
            }

            lock (stateLock)
            {
                // Una peticion por pagina: manda la ultima. Mientras se mueve
                // la rueda del zoom llegan muchos tamaños intermedios y no
                // tiene sentido rasterizarlos todos.
                pending[peticion.Page] = peticion;
            }

            EnsureWorker();
            workSignal.Set();
        }

        private void EnsureWorker()
        {
            if (worker != null || disposed)
            {
                return;
            }

            lock (stateLock)
            {
                if (worker != null)
                {
                    return;
                }

                worker = new Thread(WorkerLoop);
                worker.IsBackground = true;
                worker.Name = "PDF Ligero - preparar paginas";
                worker.Priority = ThreadPriority.BelowNormal;
                worker.Start();
            }
        }

        private void WorkerLoop()
        {
            while (!disposed)
            {
                try
                {
                    workSignal.WaitOne(400);
                    if (disposed)
                    {
                        return;
                    }

                    var quieta = new TimeSpan(
                        DateTime.UtcNow.Ticks -
                        Interlocked.Read(ref lastUiRenderTicks));
                    if (quieta.TotalMilliseconds < IdleBeforeBackgroundMs)
                    {
                        Thread.Sleep(IdleBeforeBackgroundMs);
                        workSignal.Set();
                        continue;
                    }

                    var peticion = TakeNext();
                    if (peticion == null)
                    {
                        continue;
                    }

                    Process(peticion);
                }
                catch (Exception ex)
                {
                    AppLog.Write(
                        "El preparador de páginas ha fallado: " + ex);
                }
            }
        }

        /// <summary>
        /// Coge la siguiente, empezando por la pagina que se esta mirando y
        /// siguiendo por la mas cercana.
        /// </summary>
        private RenderRequest TakeNext()
        {
            lock (stateLock)
            {
                if (pending.Count == 0)
                {
                    return null;
                }

                var mejor = -1;
                var mejorDistancia = int.MaxValue;
                foreach (var par in pending)
                {
                    var distancia = Math.Abs(par.Key - currentPage);
                    // Lo que se esta viendo tiene preferencia absoluta.
                    if (par.Value.Exact)
                    {
                        distancia -= 1000;
                    }

                    if (distancia < mejorDistancia)
                    {
                        mejorDistancia = distancia;
                        mejor = par.Key;
                    }
                }

                if (mejor < 0)
                {
                    return null;
                }

                var peticion = pending[mejor];
                pending.Remove(mejor);
                return peticion;
            }
        }

        private void Process(RenderRequest peticion)
        {
            lock (stateLock)
            {
                PageRender ya;
                if (cache.TryGetValue(peticion.Page, out ya) &&
                    ya.Matches(peticion.Rotate, peticion.Flags) &&
                    ya.CloseEnough(peticion.Width, peticion.Height))
                {
                    return;
                }
            }

            Image cruda;
            try
            {
                lock (pdfiumLock)
                {
                    RasterizedCount++;
                    cruda = inner.Render(
                        peticion.Page,
                        peticion.Width,
                        peticion.Height,
                        peticion.DpiX,
                        peticion.DpiY,
                        peticion.Rotate,
                        peticion.Flags);
                }
            }
            catch (Exception ex)
            {
                AppLog.Write(
                    "No se pudo preparar la página " + peticion.Page +
                    ": " + ex);
                return;
            }

            if (cruda == null || disposed)
            {
                if (cruda != null)
                {
                    cruda.Dispose();
                }

                return;
            }

            Store(
                peticion.Page,
                new PageRender(cruda, peticion.Rotate, peticion.Flags));

            var aviso = PageRefined;
            if (aviso != null)
            {
                try
                {
                    aviso(peticion.Page);
                }
                catch (Exception ex)
                {
                    AppLog.Write(
                        "No se pudo refrescar la página preparada: " + ex);
                }
            }
        }

        /// <summary>
        /// Tira lo guardado. El visor lo llama cuando el documento cambia de
        /// revision, para no enseñar paginas de antes.
        /// </summary>
        public void ClearCache()
        {
            var sobrantes = new List<PageRender>();
            lock (stateLock)
            {
                foreach (var par in cache)
                {
                    sobrantes.Add(par.Value);
                }

                cache.Clear();
                pending.Clear();
            }

            foreach (var sobrante in sobrantes)
            {
                sobrante.Dispose();
            }
        }

        // --- El resto se delega tal cual --------------------------------

        public int PageCount { get { return inner.PageCount; } }

        public PdfBookmarkCollection Bookmarks { get { return inner.Bookmarks; } }

        public IList<SizeF> PageSizes { get { return inner.PageSizes; } }

        public Image Render(int page, float dpiX, float dpiY, bool forPrinting)
        {
            lock (pdfiumLock)
            {
                return inner.Render(page, dpiX, dpiY, forPrinting);
            }
        }

        public Image Render(int page, float dpiX, float dpiY, PdfRenderFlags flags)
        {
            lock (pdfiumLock)
            {
                return inner.Render(page, dpiX, dpiY, flags);
            }
        }

        public Image Render(
            int page, int width, int height, float dpiX, float dpiY,
            bool forPrinting)
        {
            lock (pdfiumLock)
            {
                return inner.Render(
                    page, width, height, dpiX, dpiY, forPrinting);
            }
        }

        public void Render(
            int page, Graphics graphics, float dpiX, float dpiY,
            Rectangle bounds, bool forPrinting)
        {
            lock (pdfiumLock)
            {
                inner.Render(
                    page, graphics, dpiX, dpiY, bounds, forPrinting);
            }
        }

        public void Render(
            int page, Graphics graphics, float dpiX, float dpiY,
            Rectangle bounds, PdfRenderFlags flags)
        {
            lock (pdfiumLock)
            {
                inner.Render(page, graphics, dpiX, dpiY, bounds, flags);
            }
        }

        public PdfMatches Search(string text, bool matchCase, bool wholeWord)
        {
            lock (pdfiumLock)
            {
                return inner.Search(text, matchCase, wholeWord);
            }
        }

        public PdfMatches Search(
            string text, bool matchCase, bool wholeWord, int page)
        {
            lock (pdfiumLock)
            {
                return inner.Search(text, matchCase, wholeWord, page);
            }
        }

        public PdfMatches Search(
            string text, bool matchCase, bool wholeWord,
            int startPage, int endPage)
        {
            lock (pdfiumLock)
            {
                return inner.Search(
                    text, matchCase, wholeWord, startPage, endPage);
            }
        }

        public PrintDocument CreatePrintDocument()
        {
            return inner.CreatePrintDocument();
        }

        public PrintDocument CreatePrintDocument(PdfPrintMode printMode)
        {
            return inner.CreatePrintDocument(printMode);
        }

        public PrintDocument CreatePrintDocument(PdfPrintSettings settings)
        {
            return inner.CreatePrintDocument(settings);
        }

        public PdfPageLinks GetPageLinks(int page, Size size)
        {
            lock (pdfiumLock)
            {
                return inner.GetPageLinks(page, size);
            }
        }

        public void DeletePage(int page)
        {
            ClearCache();
            lock (pdfiumLock)
            {
                inner.DeletePage(page);
            }
        }

        public void RotatePage(int page, PdfRotation rotation)
        {
            ClearCache();
            lock (pdfiumLock)
            {
                inner.RotatePage(page, rotation);
            }
        }

        public PdfInformation GetInformation()
        {
            lock (pdfiumLock)
            {
                return inner.GetInformation();
            }
        }

        public string GetPdfText(int page)
        {
            lock (pdfiumLock)
            {
                return inner.GetPdfText(page);
            }
        }

        public string GetPdfText(PdfTextSpan textSpan)
        {
            lock (pdfiumLock)
            {
                return inner.GetPdfText(textSpan);
            }
        }

        public IList<PdfRectangle> GetTextBounds(PdfTextSpan textSpan)
        {
            lock (pdfiumLock)
            {
                return inner.GetTextBounds(textSpan);
            }
        }

        public PointF PointToPdf(int page, Point point)
        {
            return inner.PointToPdf(page, point);
        }

        public Point PointFromPdf(int page, PointF point)
        {
            return inner.PointFromPdf(page, point);
        }

        public RectangleF RectangleToPdf(int page, Rectangle rect)
        {
            return inner.RectangleToPdf(page, rect);
        }

        public Rectangle RectangleFromPdf(int page, RectangleF rect)
        {
            return inner.RectangleFromPdf(page, rect);
        }

        public void Save(string path)
        {
            lock (pdfiumLock)
            {
                inner.Save(path);
            }
        }

        public void Save(Stream stream)
        {
            lock (pdfiumLock)
            {
                inner.Save(stream);
            }
        }

        /// <summary>
        /// Suelta solo lo suyo. El documento de dentro NO se cierra aqui: lo
        /// abre y lo cierra la pestaña, que lo usa tambien para buscar, medir
        /// e imprimir.
        /// </summary>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            PageRefined = null;
            try
            {
                workSignal.Set();
            }
            catch (Exception)
            {
            }

            var hilo = worker;
            if (hilo != null)
            {
                try
                {
                    hilo.Join(1500);
                }
                catch (Exception)
                {
                }
            }

            ClearCache();

            try
            {
                workSignal.Close();
            }
            catch (Exception)
            {
            }
        }

        /// <summary>Una pagina ya rasterizada, con el tamaño que tiene.</summary>
        private sealed class PageRender
        {
            private readonly object bitmapLock = new object();
            private readonly PdfRotation rotate;
            private readonly PdfRenderFlags flags;
            private Bitmap bitmap;

            public PageRender(
                Image imagen,
                PdfRotation rotate,
                PdfRenderFlags flags)
            {
                this.rotate = rotate;
                this.flags = flags;
                bitmap = imagen as Bitmap;
                if (bitmap == null)
                {
                    using (imagen)
                    {
                        bitmap = new Bitmap(imagen);
                    }
                }

                Width = bitmap.Width;
                Height = bitmap.Height;
            }

            public int Width { get; private set; }

            public int Height { get; private set; }

            public bool Matches(PdfRotation otra, PdfRenderFlags otras)
            {
                return rotate == otra && flags == otras;
            }

            /// <summary>
            /// El tamaño guardado sirve tal cual para el que se pide.
            /// </summary>
            public bool CloseEnough(int width, int height)
            {
                if (Width == width && Height == height)
                {
                    return true;
                }

                if (Width <= 0 || width <= 0)
                {
                    return false;
                }

                var diferencia = Math.Abs(Width - width) / (double)Width;
                return diferencia <= AcceptableScaleSlack;
            }

            public Bitmap Extract(int width, int height)
            {
                lock (bitmapLock)
                {
                    if (bitmap == null)
                    {
                        return null;
                    }

                    try
                    {
                        var copia = new Bitmap(
                            width, height, PixelFormat.Format32bppArgb);
                        using (var graphics = Graphics.FromImage(copia))
                        {
                            graphics.CompositingMode =
                                CompositingMode.SourceCopy;
                            if (width == Width && height == Height)
                            {
                                graphics.DrawImageUnscaled(bitmap, 0, 0);
                            }
                            else
                            {
                                // Interpolacion barata a proposito: esta
                                // imagen es un apaño que dura lo que tarde el
                                // hilo de atras en traer la buena, y con
                                // bicubica de calidad el reescalado a tamaños
                                // grandes costaba mas que rasterizar de nuevo
                                // —medido: 455 ms en un A0 muy ampliado—, que
                                // es justo lo que se queria evitar.
                                graphics.InterpolationMode =
                                    InterpolationMode.Bilinear;
                                graphics.PixelOffsetMode =
                                    PixelOffsetMode.Half;
                                graphics.DrawImage(
                                    bitmap,
                                    new Rectangle(0, 0, width, height),
                                    new Rectangle(0, 0, Width, Height),
                                    GraphicsUnit.Pixel);
                            }
                        }

                        return copia;
                    }
                    catch (Exception)
                    {
                        return null;
                    }
                }
            }

            public void Dispose()
            {
                lock (bitmapLock)
                {
                    if (bitmap != null)
                    {
                        bitmap.Dispose();
                        bitmap = null;
                    }
                }
            }
        }

        private sealed class RenderRequest
        {
            public RenderRequest(
                int page,
                int width,
                int height,
                float dpiX,
                float dpiY,
                PdfRotation rotate,
                PdfRenderFlags flags,
                bool exact)
            {
                Page = page;
                Width = width;
                Height = height;
                DpiX = dpiX;
                DpiY = dpiY;
                Rotate = rotate;
                Flags = flags;
                Exact = exact;
            }

            public int Page { get; private set; }

            public int Width { get; private set; }

            public int Height { get; private set; }

            public float DpiX { get; private set; }

            public float DpiY { get; private set; }

            public PdfRotation Rotate { get; private set; }

            public PdfRenderFlags Flags { get; private set; }

            /// <summary>
            /// La pide el visor para lo que se esta viendo, no el preparador.
            /// </summary>
            public bool Exact { get; private set; }
        }
    }
}

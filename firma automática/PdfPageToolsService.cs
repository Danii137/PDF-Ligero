using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Threading;
using iTextSharp.text.pdf;
using PdfiumViewer;
using IoPath = System.IO.Path;
using PdfiumDocument = PdfiumViewer.PdfDocument;

namespace FirmaAutomatica
{
    /// <summary>Como se recorta la pagina.</summary>
    internal enum PdfCropMode
    {
        /// <summary>Se ajusta a lo que hay dibujado, quitando el blanco.</summary>
        AlContenido,

        /// <summary>Se quita un margen fijo por los cuatro lados.</summary>
        MargenFijo
    }

    internal sealed class PdfCropResult
    {
        public PdfCropResult(
            string outputPath,
            int croppedPageCount,
            int pageCount)
        {
            OutputPath = outputPath;
            CroppedPageCount = croppedPageCount;
            PageCount = pageCount;
        }

        public string OutputPath { get; private set; }

        public int CroppedPageCount { get; private set; }

        public int PageCount { get; private set; }
    }

    /// <summary>
    /// Operaciones sueltas sobre paginas: duplicar, meter una hoja en blanco
    /// y recortar margenes.
    ///
    /// Duplicar y la hoja en blanco se apoyan en los servicios que ya existen
    /// —organizar e insertar— en vez de escribir PDFs por su cuenta. Recortar
    /// solo cambia la caja de recorte: el contenido sigue entero debajo, asi
    /// que es reversible y no se pierde nada.
    /// </summary>
    internal static class PdfPageToolsService
    {
        /// <summary>
        /// Resolucion a la que se mira la pagina para saber donde acaba el
        /// blanco. No hace falta mas: se busca el borde del dibujo, no leerlo.
        /// </summary>
        private const int ContentScanDpi = 72;

        /// <summary>
        /// Un pixel se considera dibujado por debajo de este valor. Deja
        /// pasar el gris del papel escaneado sin comerse las lineas finas.
        /// </summary>
        private const int ContentThreshold = 238;

        /// <summary>
        /// Repite una pagina justo detras de si misma.
        ///
        /// Se hace en dos pasos —sacar esa pagina a un PDF suelto y volver a
        /// meterla— porque el organizador se niega, con razon, a que una
        /// pagina de origen aparezca dos veces en la misma reorganizacion:
        /// tendria que decidir a cual de las dos copias van los marcadores y
        /// los enlaces que apuntan a ella.
        /// </summary>
        public static void DuplicatePage(
            string sourcePdfPath,
            string outputPath,
            int pageNumber,
            CancellationToken cancellationToken)
        {
            var source = IoPath.GetFullPath(sourcePdfPath);
            int paginas;
            using (var reader = new PdfReader(source, (byte[])null, true))
            {
                paginas = reader.NumberOfPages;
            }

            if (pageNumber < 1 || pageNumber > paginas)
            {
                throw new ArgumentOutOfRangeException("pageNumber");
            }

            cancellationToken.ThrowIfCancellationRequested();
            var suelta = IoPath.Combine(
                IoPath.GetTempPath(),
                "pdfligero-copia-" +
                Guid.NewGuid().ToString("N") + ".pdf");
            try
            {
                PdfPageOrganizerService.Organize(
                    source,
                    new List<PdfPageOrganizerPage>
                    {
                        new PdfPageOrganizerPage(pageNumber, 0)
                    },
                    suelta,
                    null,
                    cancellationToken);

                PdfPageInsertService.Insert(
                    source,
                    new List<string> { suelta },
                    pageNumber,
                    outputPath,
                    null);
            }
            finally
            {
                TryDelete(suelta);
            }
        }

        /// <summary>
        /// Mete una hoja en blanco delante de la pagina indicada, del mismo
        /// tamaño y orientacion que ella.
        /// </summary>
        public static void InsertBlankPage(
            string sourcePdfPath,
            string outputPath,
            int beforePageNumber,
            CancellationToken cancellationToken)
        {
            var source = IoPath.GetFullPath(sourcePdfPath);
            iTextSharp.text.Rectangle tamano;
            int paginas;
            int rotacion;
            using (var reader = new PdfReader(source, (byte[])null, true))
            {
                paginas = reader.NumberOfPages;
                var referencia = Math.Max(
                    1,
                    Math.Min(paginas, beforePageNumber));
                tamano = reader.GetPageSize(referencia);
                rotacion = reader.GetPageRotation(referencia);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var blanca = IoPath.Combine(
                IoPath.GetTempPath(),
                "pdfligero-blanca-" +
                Guid.NewGuid().ToString("N") + ".pdf");
            try
            {
                CreateBlankPdf(blanca, tamano, rotacion);
                PdfPageInsertService.Insert(
                    source,
                    new List<string> { blanca },
                    Math.Max(0, beforePageNumber - 1),
                    outputPath,
                    null);
            }
            finally
            {
                TryDelete(blanca);
            }
        }

        /// <summary>
        /// Recorta las paginas cambiando su caja de recorte. El contenido no
        /// se toca: lo recortado sigue ahi y se puede volver atras.
        /// </summary>
        public static PdfCropResult CropPages(
            string sourcePdfPath,
            string outputPath,
            IList<int> pages,
            PdfCropMode mode,
            float marginMillimetres,
            Action<string> reportStage,
            CancellationToken cancellationToken)
        {
            var source = IoPath.GetFullPath(sourcePdfPath);
            if (!File.Exists(source))
            {
                throw new FileNotFoundException(
                    "El PDF ya no está en su sitio.",
                    source);
            }

            var target = IoPath.GetFullPath(outputPath);
            var margenPuntos = Math.Max(0F, marginMillimetres) * 72F / 25.4F;
            var afectadas = new HashSet<int>();
            if (pages != null)
            {
                foreach (var pagina in pages)
                {
                    afectadas.Add(pagina);
                }
            }

            var temporaryPath = IoPath.Combine(
                IoPath.GetDirectoryName(target),
                "." + IoPath.GetFileNameWithoutExtension(target) +
                "." + Guid.NewGuid().ToString("N") + ".recortar.tmp");

            var recortadas = 0;
            var total = 0;
            try
            {
                Dictionary<int, RectangleF> cajas = null;
                if (mode == PdfCropMode.AlContenido)
                {
                    Report(reportStage, "Buscando el contenido…");
                    cajas = MeasureContentBoxes(
                        source,
                        afectadas,
                        cancellationToken);
                }

                PdfReader reader = null;
                PdfStamper stamper = null;
                FileStream output = null;
                try
                {
                    reader = new PdfReader(source, (byte[])null, true);
                    total = reader.NumberOfPages;
                    output = new FileStream(
                        temporaryPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        1024 * 1024,
                        FileOptions.SequentialScan);
                    stamper = new PdfStamper(reader, output);
                    stamper.Writer.CloseStream = false;

                    for (var pagina = 1; pagina <= total; pagina++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (afectadas.Count > 0 && !afectadas.Contains(pagina))
                        {
                            continue;
                        }

                        var media = reader.GetPageSize(pagina);
                        var actual = reader.GetCropBox(pagina) ?? media;
                        iTextSharp.text.Rectangle nueva;
                        if (mode == PdfCropMode.AlContenido)
                        {
                            RectangleF contenido;
                            if (cajas == null ||
                                !cajas.TryGetValue(pagina, out contenido))
                            {
                                continue;
                            }

                            nueva = new iTextSharp.text.Rectangle(
                                contenido.Left - margenPuntos,
                                contenido.Top - margenPuntos,
                                contenido.Right + margenPuntos,
                                contenido.Bottom + margenPuntos);
                        }
                        else
                        {
                            nueva = new iTextSharp.text.Rectangle(
                                actual.Left + margenPuntos,
                                actual.Bottom + margenPuntos,
                                actual.Right - margenPuntos,
                                actual.Top - margenPuntos);
                        }

                        var acotada = Clamp(nueva, actual);
                        if (acotada == null)
                        {
                            // Recortar tanto que no quedaria pagina: se deja
                            // como estaba en vez de dejarla en nada.
                            continue;
                        }

                        var caja = new PdfArray(new float[]
                        {
                            acotada.Left,
                            acotada.Bottom,
                            acotada.Right,
                            acotada.Top
                        });
                        reader.GetPageN(pagina).Put(PdfName.CROPBOX, caja);
                        recortadas++;
                    }

                    stamper.Close();
                    stamper = null;
                    reader = null;
                    output.Flush(true);
                    output.Dispose();
                    output = null;
                }
                finally
                {
                    if (stamper != null)
                    {
                        try
                        {
                            stamper.Close();
                        }
                        catch (Exception)
                        {
                        }
                    }
                    else if (reader != null)
                    {
                        reader.Close();
                    }

                    if (output != null)
                    {
                        output.Dispose();
                    }
                }

                ValidateWrittenPdf(temporaryPath, total);
                File.Move(temporaryPath, target);
                return new PdfCropResult(target, recortadas, total);
            }
            catch
            {
                TryDelete(temporaryPath);
                throw;
            }
        }

        /// <summary>
        /// Donde acaba lo dibujado en cada pagina, en coordenadas de PDF.
        /// Se rasteriza a baja resolucion y se busca el primer pixel que no
        /// es papel por cada lado.
        /// </summary>
        private static Dictionary<int, RectangleF> MeasureContentBoxes(
            string path,
            ICollection<int> pages,
            CancellationToken cancellationToken)
        {
            var cajas = new Dictionary<int, RectangleF>();
            using (var document = PdfDocumentOpenService.Load(path))
            {
                for (var indice = 0; indice < document.PageCount; indice++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var pagina = indice + 1;
                    if (pages != null &&
                        pages.Count > 0 &&
                        !pages.Contains(pagina))
                    {
                        continue;
                    }

                    try
                    {
                        var tamano = document.PageSizes[indice];
                        var ancho = Math.Max(
                            1,
                            (int)Math.Ceiling(
                                tamano.Width * ContentScanDpi / 72D));
                        var alto = Math.Max(
                            1,
                            (int)Math.Ceiling(
                                tamano.Height * ContentScanDpi / 72D));
                        using (var render = document.Render(
                            indice,
                            ancho,
                            alto,
                            ContentScanDpi,
                            ContentScanDpi,
                            PdfRenderFlags.Annotations |
                            PdfRenderFlags.LimitImageCacheSize))
                        {
                            using (var bitmap = new Bitmap(
                                ancho,
                                alto,
                                PixelFormat.Format24bppRgb))
                            {
                                using (var graphics =
                                    Graphics.FromImage(bitmap))
                                {
                                    graphics.Clear(Color.White);
                                    graphics.DrawImage(
                                        render,
                                        new System.Drawing.Rectangle(
                                            0,
                                            0,
                                            ancho,
                                            alto));
                                }

                                var caja = FindInkBounds(bitmap);
                                if (caja.IsEmpty)
                                {
                                    continue;
                                }

                                // De pixeles a puntos, con el origen abajo.
                                var escalaX = tamano.Width / ancho;
                                var escalaY = tamano.Height / alto;
                                cajas[pagina] = new RectangleF(
                                    caja.Left * escalaX,
                                    tamano.Height - (caja.Bottom * escalaY),
                                    caja.Width * escalaX,
                                    caja.Height * escalaY);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Una pagina que no se puede rasterizar se queda sin
                        // recortar; el resto del documento sigue.
                        AppLog.Write(
                            "No se pudo medir el contenido de la página " +
                            pagina + ": " + ex);
                    }
                }
            }

            return cajas;
        }

        private static System.Drawing.Rectangle FindInkBounds(Bitmap bitmap)
        {
            var datos = bitmap.LockBits(
                new System.Drawing.Rectangle(
                    0,
                    0,
                    bitmap.Width,
                    bitmap.Height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format24bppRgb);
            try
            {
                var stride = datos.Stride;
                var bytes = Math.Abs(stride) * datos.Height;
                var buffer = new byte[bytes];
                System.Runtime.InteropServices.Marshal.Copy(
                    datos.Scan0,
                    buffer,
                    0,
                    bytes);

                var izquierda = bitmap.Width;
                var derecha = -1;
                var arriba = bitmap.Height;
                var abajo = -1;
                for (var y = 0; y < bitmap.Height; y++)
                {
                    var fila = y * stride;
                    for (var x = 0; x < bitmap.Width; x++)
                    {
                        var i = fila + (x * 3);
                        if (buffer[i] >= ContentThreshold &&
                            buffer[i + 1] >= ContentThreshold &&
                            buffer[i + 2] >= ContentThreshold)
                        {
                            continue;
                        }

                        if (x < izquierda)
                        {
                            izquierda = x;
                        }

                        if (x > derecha)
                        {
                            derecha = x;
                        }

                        if (y < arriba)
                        {
                            arriba = y;
                        }

                        if (y > abajo)
                        {
                            abajo = y;
                        }
                    }
                }

                if (derecha < izquierda || abajo < arriba)
                {
                    return System.Drawing.Rectangle.Empty;
                }

                return new System.Drawing.Rectangle(
                    izquierda,
                    arriba,
                    derecha - izquierda + 1,
                    abajo - arriba + 1);
            }
            finally
            {
                bitmap.UnlockBits(datos);
            }
        }

        /// <summary>
        /// La caja nueva no puede salirse de la que ya habia ni quedarse en
        /// nada. Devuelve null si el recorte no tiene sentido.
        /// </summary>
        private static iTextSharp.text.Rectangle Clamp(
            iTextSharp.text.Rectangle nueva,
            iTextSharp.text.Rectangle limite)
        {
            var izquierda = Math.Max(nueva.Left, limite.Left);
            var abajo = Math.Max(nueva.Bottom, limite.Bottom);
            var derecha = Math.Min(nueva.Right, limite.Right);
            var arriba = Math.Min(nueva.Top, limite.Top);

            // Menos de un centimetro de lado no es una pagina.
            const float MinimoPuntos = 28F;
            if (derecha - izquierda < MinimoPuntos ||
                arriba - abajo < MinimoPuntos)
            {
                return null;
            }

            return new iTextSharp.text.Rectangle(
                izquierda,
                abajo,
                derecha,
                arriba);
        }

        private static void CreateBlankPdf(
            string path,
            iTextSharp.text.Rectangle size,
            int rotation)
        {
            using (var document = new iTextSharp.text.Document(size))
            {
                using (var stream = new FileStream(
                    path,
                    FileMode.Create,
                    FileAccess.Write))
                {
                    var writer = PdfWriter.GetInstance(document, stream);
                    document.Open();
                    // Una pagina sin nada dentro: iText necesita que se le
                    // diga que hay una, aunque quede vacia.
                    writer.PageEmpty = false;
                    document.NewPage();
                    document.Close();
                    writer.Close();
                }
            }

            if (rotation % 360 == 0)
            {
                return;
            }

            var girada = path + ".rot";
            using (var reader = new PdfReader(path))
            {
                reader.GetPageN(1).Put(
                    PdfName.ROTATE,
                    new PdfNumber(rotation));
                using (var stream = new FileStream(
                    girada,
                    FileMode.Create,
                    FileAccess.Write))
                {
                    var stamper = new PdfStamper(reader, stream);
                    stamper.Close();
                }
            }

            File.Delete(path);
            File.Move(girada, path);
        }

        private static void ValidateWrittenPdf(string path, int pageCount)
        {
            if (!File.Exists(path) || new FileInfo(path).Length <= 0)
            {
                throw new InvalidDataException(
                    "La copia recortada está vacía.");
            }

            using (var reader = new PdfReader(path))
            {
                if (reader.NumberOfPages != pageCount)
                {
                    throw new InvalidDataException(
                        "La copia recortada no conserva todas las páginas.");
                }
            }

            using (var document = PdfDocumentOpenService.Load(path))
            {
                if (document.PageCount != pageCount)
                {
                    throw new InvalidDataException(
                        "PDFium no puede abrir la copia recortada.");
                }
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
            }
        }

        private static void Report(Action<string> reportStage, string stage)
        {
            if (reportStage != null && !string.IsNullOrEmpty(stage))
            {
                reportStage(stage);
            }
        }
    }
}

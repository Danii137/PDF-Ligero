using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Threading;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
using IoPath = System.IO.Path;
using PdfMatrix = iTextSharp.text.pdf.parser.Matrix;

namespace FirmaAutomatica
{
    /// <summary>Cuanto se aprieta al reducir el tamaño.</summary>
    internal sealed class PdfShrinkSettings
    {
        public PdfShrinkSettings()
        {
            TargetImageDpi = 200;
            JpegQuality = 80;
        }

        /// <summary>
        /// Resolucion a la que se dejan las imagenes, medida sobre el tamaño
        /// al que se dibujan en la pagina. Una imagen que ya esta por debajo
        /// no se toca.
        /// </summary>
        public int TargetImageDpi { get; set; }

        /// <summary>Calidad JPEG, de 1 a 100.</summary>
        public int JpegQuality { get; set; }

        public PdfShrinkSettings Snapshot()
        {
            return new PdfShrinkSettings
            {
                TargetImageDpi = Math.Max(72, Math.Min(400, TargetImageDpi)),
                JpegQuality = Math.Max(35, Math.Min(95, JpegQuality))
            };
        }
    }

    internal sealed class PdfShrinkResult
    {
        public PdfShrinkResult(
            string outputPath,
            long originalBytes,
            long resultBytes,
            int recompressedImages,
            int totalImages,
            bool digitalSignaturesInvalidated)
        {
            OutputPath = outputPath;
            OriginalBytes = originalBytes;
            ResultBytes = resultBytes;
            RecompressedImages = recompressedImages;
            TotalImages = totalImages;
            DigitalSignaturesInvalidated = digitalSignaturesInvalidated;
        }

        public string OutputPath { get; private set; }

        public long OriginalBytes { get; private set; }

        public long ResultBytes { get; private set; }

        public int RecompressedImages { get; private set; }

        public int TotalImages { get; private set; }

        public bool DigitalSignaturesInvalidated { get; private set; }

        /// <summary>Porcentaje de lo que se ha quitado. Cero si no bajo.</summary>
        public int SavedPercent
        {
            get
            {
                if (OriginalBytes <= 0 || ResultBytes >= OriginalBytes)
                {
                    return 0;
                }

                return (int)Math.Round(
                    (OriginalBytes - ResultBytes) * 100D / OriginalBytes);
            }
        }

        public bool GotSmaller
        {
            get { return ResultBytes > 0 && ResultBytes < OriginalBytes; }
        }
    }

    /// <summary>
    /// Reduce el tamaño de un PDF para poder enviarlo.
    ///
    /// Hace dos cosas: bajar la resolucion de las imagenes que van muy por
    /// encima de lo que se ve en pantalla o en papel, y apretar la estructura
    /// del archivo. Lo vectorial —las lineas de un plano, el texto— no se
    /// toca: ahi no hay nada que quitar sin estropearlo.
    ///
    /// Se deja en paz todo lo que no se pueda rehacer con garantias:
    /// mascaras, transparencias, imagenes de un bit y las que ya estan por
    /// debajo de la resolucion pedida.
    /// </summary>
    internal static class PdfShrinkService
    {
        public const string DigitalSignatureInvalidationWarning =
            "La copia reducida ya no conserva la validez criptografica de " +
            "las firmas digitales del documento original.";

        public const string XfaUnsupportedMessage =
            "Los formularios XFA no se pueden reducir de forma segura. " +
            "Guarda antes una copia PDF normal del formulario.";

        /// <summary>Lado minimo para que merezca la pena tocar una imagen.</summary>
        private const int MinimumInterestingSide = 200;

        /// <summary>
        /// Margen por encima del objetivo antes de rehacer una imagen. Bajar
        /// de 230 a 200 ppp no compensa la perdida de calidad.
        /// </summary>
        private const double DpiSlack = 1.25D;

        public static string SuggestOutputPath(string sourcePdfPath)
        {
            var source = IoPath.GetFullPath(sourcePdfPath);
            var directory = IoPath.GetDirectoryName(source);
            var baseName = IoPath.GetFileNameWithoutExtension(source);
            var candidate = IoPath.Combine(
                directory,
                baseName + " reducido.pdf");
            var suffix = 2;
            while (File.Exists(candidate))
            {
                candidate = IoPath.Combine(
                    directory,
                    baseName + " reducido (" +
                    suffix.ToString(CultureInfo.InvariantCulture) + ").pdf");
                suffix++;
            }

            return candidate;
        }

        public static PdfShrinkResult Shrink(
            string sourcePdfPath,
            string outputPath,
            PdfShrinkSettings settings,
            Action<string> reportStage,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = IoPath.GetFullPath(sourcePdfPath);
            if (!File.Exists(source))
            {
                throw new FileNotFoundException(
                    "El PDF ya no está en su sitio.",
                    source);
            }

            var target = IoPath.GetFullPath(outputPath);
            if (string.Equals(
                    source,
                    target,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException(
                    "El original no se sobrescribe: elige otro nombre.");
            }

            var effective = (settings ?? new PdfShrinkSettings()).Snapshot();
            var originalBytes = new FileInfo(source).Length;
            var temporaryPath = IoPath.Combine(
                IoPath.GetDirectoryName(target),
                "." + IoPath.GetFileNameWithoutExtension(target) +
                "." + Guid.NewGuid().ToString("N") + ".reducir.tmp");

            var recomprimidas = 0;
            var totales = 0;
            var firmas = false;
            try
            {
                PdfReader reader = null;
                try
                {
                    reader = new PdfReader(source, (byte[])null, true);
                    var acroForm = reader.Catalog.GetAsDict(PdfName.ACROFORM);
                    if (acroForm != null &&
                        acroForm.Get(PdfName.XFA) != null)
                    {
                        throw new NotSupportedException(
                            XfaUnsupportedMessage);
                    }

                    var campos = reader.AcroFields;
                    firmas = campos != null &&
                        campos.GetSignatureNames().Count > 0;

                    Report(reportStage, "Midiendo las imágenes…");
                    var dibujadas = MeasureDrawnImages(
                        reader,
                        cancellationToken);

                    Report(reportStage, "Rehaciendo las imágenes…");
                    recomprimidas = RecompressImages(
                        reader,
                        dibujadas,
                        effective,
                        out totales,
                        cancellationToken);

                    Report(reportStage, "Apretando el archivo…");
                    reader.RemoveUnusedObjects();

                    using (var output = new FileStream(
                        temporaryPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        1024 * 1024,
                        FileOptions.SequentialScan))
                    {
                        var stamper = new PdfStamper(reader, output);
                        stamper.Writer.CloseStream = false;
                        // Los objetos en flujos comprimidos ahorran bastante
                        // en documentos con muchas paginas.
                        stamper.SetFullCompression();
                        stamper.Close();
                        reader = null;
                        output.Flush(true);
                    }
                }
                finally
                {
                    if (reader != null)
                    {
                        reader.Close();
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                Report(reportStage, "Comprobando la copia…");
                ValidateWrittenPdf(temporaryPath, source);

                var resultBytes = new FileInfo(temporaryPath).Length;
                if (resultBytes >= originalBytes)
                {
                    // Un PDF ya apretado puede salir mas grande. Devolver algo
                    // peor que el original no ayuda: se dice y no se deja
                    // nada en disco.
                    TryDelete(temporaryPath);
                    return new PdfShrinkResult(
                        null,
                        originalBytes,
                        resultBytes,
                        recomprimidas,
                        totales,
                        firmas);
                }

                File.Move(temporaryPath, target);
                return new PdfShrinkResult(
                    target,
                    originalBytes,
                    resultBytes,
                    recomprimidas,
                    totales,
                    firmas);
            }
            catch
            {
                TryDelete(temporaryPath);
                throw;
            }
        }

        /// <summary>
        /// Tamaño en puntos al que se dibuja cada imagen, por objeto. Una
        /// misma imagen puede salir en varias paginas: vale la mayor, que es
        /// la que manda para no estropear la mas grande.
        /// </summary>
        private static Dictionary<int, SizeF> MeasureDrawnImages(
            PdfReader reader,
            CancellationToken cancellationToken)
        {
            var medidas = new Dictionary<int, SizeF>();
            var parser = new PdfReaderContentParser(reader);
            for (var pagina = 1; pagina <= reader.NumberOfPages; pagina++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    parser.ProcessContent(
                        pagina,
                        new DrawnImageListener(medidas));
                }
                catch (Exception)
                {
                    // Una pagina que no se puede recorrer no impide reducir
                    // el resto: sus imagenes simplemente no se tocan.
                }
            }

            return medidas;
        }

        private static int RecompressImages(
            PdfReader reader,
            Dictionary<int, SizeF> drawnSizes,
            PdfShrinkSettings settings,
            out int totalImages,
            CancellationToken cancellationToken)
        {
            var rehechas = 0;
            totalImages = 0;
            for (var numero = 1; numero < reader.XrefSize; numero++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var objeto = reader.GetPdfObject(numero);
                if (objeto == null || !objeto.IsStream())
                {
                    continue;
                }

                var stream = objeto as PRStream;
                if (stream == null)
                {
                    continue;
                }

                var subtipo = stream.GetAsName(PdfName.SUBTYPE);
                if (subtipo == null || !PdfName.IMAGE.Equals(subtipo))
                {
                    continue;
                }

                totalImages++;
                SizeF dibujada;
                if (!drawnSizes.TryGetValue(numero, out dibujada))
                {
                    // Sin saber a que tamaño se dibuja no se puede decidir
                    // cuanta resolucion sobra.
                    continue;
                }

                if (TryRecompress(stream, dibujada, settings))
                {
                    rehechas++;
                }
            }

            return rehechas;
        }

        private static bool TryRecompress(
            PRStream stream,
            SizeF drawnSize,
            PdfShrinkSettings settings)
        {
            try
            {
                if (!IsSafeToRecompress(stream))
                {
                    return false;
                }

                var anchoPixeles = GetInt(stream, PdfName.WIDTH);
                var altoPixeles = GetInt(stream, PdfName.HEIGHT);
                if (anchoPixeles < MinimumInterestingSide &&
                    altoPixeles < MinimumInterestingSide)
                {
                    return false;
                }

                var anchoPulgadas = drawnSize.Width / 72F;
                var altoPulgadas = drawnSize.Height / 72F;
                if (anchoPulgadas <= 0.01F || altoPulgadas <= 0.01F)
                {
                    return false;
                }

                var dpiActual = Math.Max(
                    anchoPixeles / anchoPulgadas,
                    altoPixeles / altoPulgadas);
                if (dpiActual <= settings.TargetImageDpi * DpiSlack)
                {
                    return false;
                }

                var escala = settings.TargetImageDpi / dpiActual;
                var anchoNuevo = Math.Max(
                    1,
                    (int)Math.Round(anchoPixeles * escala));
                var altoNuevo = Math.Max(
                    1,
                    (int)Math.Round(altoPixeles * escala));
                if (anchoNuevo >= anchoPixeles && altoNuevo >= altoPixeles)
                {
                    return false;
                }

                var original = new PdfImageObject(stream);
                using (var imagen = original.GetDrawingImage())
                {
                    if (imagen == null)
                    {
                        return false;
                    }

                    byte[] jpeg;
                    using (var reducida = new Bitmap(
                        anchoNuevo,
                        altoNuevo,
                        PixelFormat.Format24bppRgb))
                    {
                        using (var graphics = Graphics.FromImage(reducida))
                        {
                            graphics.Clear(Color.White);
                            graphics.InterpolationMode =
                                InterpolationMode.HighQualityBicubic;
                            graphics.PixelOffsetMode =
                                PixelOffsetMode.HighQuality;
                            graphics.SmoothingMode = SmoothingMode.HighQuality;
                            graphics.DrawImage(
                                imagen,
                                new System.Drawing.Rectangle(
                                    0,
                                    0,
                                    anchoNuevo,
                                    altoNuevo));
                        }

                        jpeg = EncodeJpeg(reducida, settings.JpegQuality);
                    }

                    if (jpeg == null || jpeg.Length == 0)
                    {
                        return false;
                    }

                    // Si el resultado no es mas pequeño, no se cambia nada:
                    // se habria perdido calidad a cambio de nada.
                    var originalLength = GetRawLength(stream);
                    if (originalLength > 0 && jpeg.Length >= originalLength)
                    {
                        return false;
                    }

                    ReplaceWithJpeg(stream, jpeg, anchoNuevo, altoNuevo);
                    return true;
                }
            }
            catch (Exception)
            {
                // Una imagen que no se puede rehacer se deja como estaba. El
                // documento tiene que salir entero de aqui.
                return false;
            }
        }

        /// <summary>
        /// Solo se rehace lo que se puede rehacer sin estropear nada.
        /// </summary>
        private static bool IsSafeToRecompress(PRStream stream)
        {
            // Mascaras y transparencias: al pasar a JPEG se perderian.
            if (stream.Get(PdfName.SMASK) != null ||
                stream.Get(PdfName.MASK) != null)
            {
                return false;
            }

            var imageMask = stream.GetAsBoolean(PdfName.IMAGEMASK);
            if (imageMask != null && imageMask.BooleanValue)
            {
                return false;
            }

            // Un bit por componente es texto escaneado en blanco y negro:
            // JPEG lo emborrona y ademas ocupa mas.
            var bits = GetInt(stream, PdfName.BITSPERCOMPONENT);
            if (bits > 0 && bits < 8)
            {
                return false;
            }

            if (stream.Get(PdfName.DECODE) != null)
            {
                return false;
            }

            // JPX y JBIG2 no se reconstruyen con System.Drawing.
            var filtro = stream.Get(PdfName.FILTER);
            if (filtro != null)
            {
                var texto = filtro.ToString();
                if (texto.IndexOf("JPX", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    texto.IndexOf("JBIG2", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    texto.IndexOf("CCITT", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static void ReplaceWithJpeg(
            PRStream stream,
            byte[] jpeg,
            int width,
            int height)
        {
            stream.Clear();
            stream.SetData(jpeg, false, PRStream.NO_COMPRESSION);
            stream.Put(PdfName.TYPE, PdfName.XOBJECT);
            stream.Put(PdfName.SUBTYPE, PdfName.IMAGE);
            stream.Put(PdfName.FILTER, PdfName.DCTDECODE);
            stream.Put(PdfName.WIDTH, new PdfNumber(width));
            stream.Put(PdfName.HEIGHT, new PdfNumber(height));
            stream.Put(PdfName.BITSPERCOMPONENT, new PdfNumber(8));
            stream.Put(PdfName.COLORSPACE, PdfName.DEVICERGB);
            stream.Put(PdfName.LENGTH, new PdfNumber(jpeg.Length));
        }

        private static byte[] EncodeJpeg(Bitmap bitmap, int quality)
        {
            ImageCodecInfo codec = null;
            foreach (var candidato in ImageCodecInfo.GetImageEncoders())
            {
                if (candidato.FormatID == ImageFormat.Jpeg.Guid)
                {
                    codec = candidato;
                    break;
                }
            }

            if (codec == null)
            {
                return null;
            }

            using (var parameters = new EncoderParameters(1))
            {
                using (var parameter = new EncoderParameter(
                    System.Drawing.Imaging.Encoder.Quality,
                    (long)quality))
                {
                    parameters.Param[0] = parameter;
                    using (var memoria = new MemoryStream())
                    {
                        bitmap.Save(memoria, codec, parameters);
                        return memoria.ToArray();
                    }
                }
            }
        }

        private static int GetInt(PRStream stream, PdfName name)
        {
            var numero = stream.GetAsNumber(name);
            return numero == null ? 0 : numero.IntValue;
        }

        private static int GetRawLength(PRStream stream)
        {
            try
            {
                return stream.Length;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static void ValidateWrittenPdf(
            string path,
            string sourcePath)
        {
            if (!File.Exists(path) || new FileInfo(path).Length <= 0)
            {
                throw new InvalidDataException(
                    "La copia reducida está vacía.");
            }

            int paginasOrigen;
            using (var reader = new PdfReader(
                sourcePath,
                (byte[])null,
                true))
            {
                paginasOrigen = reader.NumberOfPages;
            }

            using (var reader = new PdfReader(path))
            {
                if (reader.NumberOfPages != paginasOrigen)
                {
                    throw new InvalidDataException(
                        "La copia reducida no conserva todas las páginas.");
                }
            }

            using (var document = PdfDocumentOpenService.Load(path))
            {
                if (document.PageCount != paginasOrigen)
                {
                    throw new InvalidDataException(
                        "PDFium no puede abrir la copia reducida.");
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

        /// <summary>
        /// Anota a que tamaño se dibuja cada imagen recorriendo el contenido
        /// de la pagina, que es la unica forma de saber cuanta resolucion
        /// sobra de verdad.
        /// </summary>
        private sealed class DrawnImageListener : IRenderListener
        {
            private readonly Dictionary<int, SizeF> medidas;

            public DrawnImageListener(Dictionary<int, SizeF> medidas)
            {
                this.medidas = medidas;
            }

            public void BeginTextBlock()
            {
            }

            public void EndTextBlock()
            {
            }

            public void RenderText(TextRenderInfo renderInfo)
            {
            }

            public void RenderImage(ImageRenderInfo renderInfo)
            {
                if (renderInfo == null)
                {
                    return;
                }

                try
                {
                    var referencia = renderInfo.GetRef();
                    if (referencia == null)
                    {
                        // Imagen incrustada en el propio flujo: no tiene
                        // objeto propio que sustituir.
                        return;
                    }

                    var matriz = renderInfo.GetImageCTM();
                    // Las dos primeras filas de la matriz son los vectores de
                    // la imagen en la pagina; su longitud es el tamaño real,
                    // incluso si esta girada.
                    var ancho = (float)Math.Sqrt(
                        (matriz[PdfMatrix.I11] * matriz[PdfMatrix.I11]) +
                        (matriz[PdfMatrix.I12] * matriz[PdfMatrix.I12]));
                    var alto = (float)Math.Sqrt(
                        (matriz[PdfMatrix.I21] * matriz[PdfMatrix.I21]) +
                        (matriz[PdfMatrix.I22] * matriz[PdfMatrix.I22]));
                    if (ancho <= 0F || alto <= 0F)
                    {
                        return;
                    }

                    var numero = referencia.Number;
                    SizeF anterior;
                    if (medidas.TryGetValue(numero, out anterior))
                    {
                        medidas[numero] = new SizeF(
                            Math.Max(anterior.Width, ancho),
                            Math.Max(anterior.Height, alto));
                    }
                    else
                    {
                        medidas[numero] = new SizeF(ancho, alto);
                    }
                }
                catch (Exception)
                {
                }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using iTextSharp.text.pdf;
using PdfiumViewer;
using IoPath = System.IO.Path;
using PdfiumDocument = PdfiumViewer.PdfDocument;

namespace FirmaAutomatica
{
    /// <summary>Formato de imagen al exportar paginas.</summary>
    internal enum PdfExportImageFormat
    {
        Png,
        Jpeg
    }

    internal sealed class PdfExportResult
    {
        private readonly List<string> outputPaths;

        public PdfExportResult(
            IEnumerable<string> outputPaths,
            int pageCount)
        {
            this.outputPaths = new List<string>(
                outputPaths ?? new string[0]);
            PageCount = pageCount;
        }

        public IList<string> OutputPaths
        {
            get { return outputPaths; }
        }

        public int PageCount { get; private set; }
    }

    /// <summary>
    /// Sacar el documento fuera del PDF: las paginas como imagen y el texto
    /// como archivo de texto.
    ///
    /// El texto sale en orden de lectura, tal como lo entrega iText, y con un
    /// separador por pagina para no perder de vista donde empieza cada una.
    /// Si el PDF es un escaneo sin OCR no hay texto que sacar, y se dice.
    /// </summary>
    internal static class PdfExportService
    {
        /// <summary>
        /// Tope de pixeles por pagina. Un A0 a 300 ppp son 130 millones de
        /// pixeles y no cabe en memoria; se baja la resolucion antes que
        /// fallar.
        /// </summary>
        private const long MaximumPixelsPerPage = 40000000L;

        public static string SuggestImageDirectory(string sourcePdfPath)
        {
            var source = IoPath.GetFullPath(sourcePdfPath);
            var directory = IoPath.GetDirectoryName(source);
            var baseName = IoPath.GetFileNameWithoutExtension(source);
            var candidate = IoPath.Combine(directory, baseName + " imágenes");
            var suffix = 2;
            while (Directory.Exists(candidate) || File.Exists(candidate))
            {
                candidate = IoPath.Combine(
                    directory,
                    baseName + " imágenes (" +
                    suffix.ToString(CultureInfo.InvariantCulture) + ")");
                suffix++;
            }

            return candidate;
        }

        public static string SuggestTextPath(string sourcePdfPath)
        {
            var source = IoPath.GetFullPath(sourcePdfPath);
            var directory = IoPath.GetDirectoryName(source);
            var baseName = IoPath.GetFileNameWithoutExtension(source);
            var candidate = IoPath.Combine(directory, baseName + ".txt");
            var suffix = 2;
            while (File.Exists(candidate))
            {
                candidate = IoPath.Combine(
                    directory,
                    baseName + " (" +
                    suffix.ToString(CultureInfo.InvariantCulture) + ").txt");
                suffix++;
            }

            return candidate;
        }

        /// <summary>
        /// Exporta las paginas indicadas como imagenes sueltas, una por
        /// archivo.
        /// </summary>
        public static PdfExportResult ExportImages(
            string sourcePdfPath,
            IList<int> pages,
            string outputDirectory,
            int dpi,
            PdfExportImageFormat format,
            int jpegQuality,
            Action<string> reportStage,
            CancellationToken cancellationToken)
        {
            if (pages == null || pages.Count == 0)
            {
                throw new ArgumentException(
                    "No se ha indicado ninguna pagina.",
                    "pages");
            }

            var source = IoPath.GetFullPath(sourcePdfPath);
            if (!File.Exists(source))
            {
                throw new FileNotFoundException(
                    "El PDF ya no está en su sitio.",
                    source);
            }

            Directory.CreateDirectory(outputDirectory);
            var resolucion = Math.Max(48, Math.Min(600, dpi));
            var calidad = Math.Max(40, Math.Min(100, jpegQuality));
            var extension = format == PdfExportImageFormat.Jpeg
                ? ".jpg"
                : ".png";
            var baseName = IoPath.GetFileNameWithoutExtension(source);
            var creados = new List<string>();

            using (var document = PdfDocumentOpenService.Load(source))
            {
                foreach (var pagina in pages)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (pagina < 1 || pagina > document.PageCount)
                    {
                        continue;
                    }

                    Report(
                        reportStage,
                        "Exportando la página " +
                        pagina.ToString(CultureInfo.CurrentCulture) + "…");

                    var destino = EnsureFreePath(
                        outputDirectory,
                        baseName + " " +
                        pagina.ToString("000", CultureInfo.InvariantCulture),
                        extension);
                    using (var bitmap = RenderPage(
                        document,
                        pagina - 1,
                        resolucion))
                    {
                        if (format == PdfExportImageFormat.Jpeg)
                        {
                            SaveJpeg(bitmap, destino, calidad);
                        }
                        else
                        {
                            bitmap.Save(destino, ImageFormat.Png);
                        }
                    }

                    creados.Add(destino);
                }
            }

            return new PdfExportResult(creados, creados.Count);
        }

        /// <summary>
        /// Saca el texto del documento a un archivo. Devuelve un resultado
        /// vacio si el PDF no tiene texto que sacar.
        /// </summary>
        public static PdfExportResult ExportText(
            string sourcePdfPath,
            string outputPath,
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

            var texto = new StringBuilder();
            var paginas = 0;
            var conTexto = 0;
            PdfReader reader = null;
            try
            {
                reader = new PdfReader(source, (byte[])null, true);
                paginas = reader.NumberOfPages;
                for (var pagina = 1; pagina <= paginas; pagina++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Report(
                        reportStage,
                        "Leyendo la página " +
                        pagina.ToString(CultureInfo.CurrentCulture) + "…");

                    var contenido = string.Empty;
                    try
                    {
                        contenido = iTextSharp.text.pdf.parser
                            .PdfTextExtractor.GetTextFromPage(reader, pagina);
                    }
                    catch (Exception ex)
                    {
                        // Una pagina ilegible no puede tirar la exportacion
                        // entera: se anota y se sigue.
                        AppLog.Write(
                            "No se pudo leer el texto de la página " +
                            pagina + ": " + ex);
                        contenido = string.Empty;
                    }

                    if (!string.IsNullOrWhiteSpace(contenido))
                    {
                        conTexto++;
                    }

                    if (pagina > 1)
                    {
                        texto.AppendLine();
                    }

                    texto.AppendLine(
                        "--- Página " +
                        pagina.ToString(CultureInfo.CurrentCulture) +
                        " ---");
                    texto.AppendLine(contenido ?? string.Empty);
                }
            }
            finally
            {
                if (reader != null)
                {
                    try
                    {
                        reader.Close();
                    }
                    catch (Exception)
                    {
                    }
                }
            }

            if (conTexto == 0)
            {
                // Un escaneo sin OCR no tiene texto: escribir un archivo con
                // solo los separadores seria engañar.
                return new PdfExportResult(new string[0], 0);
            }

            // UTF-8 con BOM: es lo que abre bien el Bloc de notas y Excel en
            // Windows sin preguntar por la codificacion.
            File.WriteAllText(outputPath, texto.ToString(), new UTF8Encoding(true));
            return new PdfExportResult(new[] { outputPath }, paginas);
        }

        private static Bitmap RenderPage(
            PdfiumDocument document,
            int pageIndex,
            int dpi)
        {
            var pageSize = document.PageSizes[pageIndex];
            var width = Math.Max(
                1,
                (int)Math.Ceiling(pageSize.Width * dpi / 72D));
            var height = Math.Max(
                1,
                (int)Math.Ceiling(pageSize.Height * dpi / 72D));
            var pixels = (long)width * height;
            var resolucion = dpi;
            if (pixels > MaximumPixelsPerPage)
            {
                var escala = Math.Sqrt(
                    MaximumPixelsPerPage / (double)pixels);
                resolucion = Math.Max(48, (int)Math.Floor(dpi * escala));
                width = Math.Max(
                    1,
                    (int)Math.Ceiling(pageSize.Width * resolucion / 72D));
                height = Math.Max(
                    1,
                    (int)Math.Ceiling(pageSize.Height * resolucion / 72D));
            }

            using (var rendered = document.Render(
                pageIndex,
                width,
                height,
                resolucion,
                resolucion,
                PdfRenderFlags.Annotations |
                PdfRenderFlags.LcdText |
                PdfRenderFlags.LimitImageCacheSize))
            {
                var bitmap = new Bitmap(
                    width,
                    height,
                    PixelFormat.Format24bppRgb);
                bitmap.SetResolution(resolucion, resolucion);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    // Fondo blanco: un PNG con transparencia de una pagina de
                    // PDF se ve negro en media aplicacion.
                    graphics.Clear(Color.White);
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.DrawImage(
                        rendered,
                        new System.Drawing.Rectangle(0, 0, width, height));
                }

                return bitmap;
            }
        }

        private static void SaveJpeg(
            Bitmap bitmap,
            string path,
            int quality)
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
                bitmap.Save(path, ImageFormat.Jpeg);
                return;
            }

            using (var parameters = new EncoderParameters(1))
            {
                using (var parameter = new EncoderParameter(
                    System.Drawing.Imaging.Encoder.Quality,
                    (long)quality))
                {
                    parameters.Param[0] = parameter;
                    bitmap.Save(path, codec, parameters);
                }
            }
        }

        private static string EnsureFreePath(
            string directory,
            string baseName,
            string extension)
        {
            var limpio = baseName;
            foreach (var invalido in IoPath.GetInvalidFileNameChars())
            {
                limpio = limpio.Replace(invalido, '_');
            }

            var candidato = IoPath.Combine(directory, limpio + extension);
            var suffix = 2;
            while (File.Exists(candidato))
            {
                candidato = IoPath.Combine(
                    directory,
                    limpio + " (" +
                    suffix.ToString(CultureInfo.InvariantCulture) + ")" +
                    extension);
                suffix++;
            }

            return candidato;
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

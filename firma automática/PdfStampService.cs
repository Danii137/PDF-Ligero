using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using iTextSharp.text.pdf;
using IoPath = System.IO.Path;

namespace FirmaAutomatica
{
    /// <summary>Donde se pone la numeracion en la hoja.</summary>
    internal enum PdfNumberPosition
    {
        PieDerecha,
        PieCentro,
        PieIzquierda,
        CabeceraDerecha,
        CabeceraCentro,
        CabeceraIzquierda
    }

    /// <summary>
    /// Que se estampa sobre las paginas: una marca de agua en diagonal, una
    /// numeracion al pie, o las dos cosas.
    /// </summary>
    internal sealed class PdfStampSettings
    {
        public PdfStampSettings()
        {
            WatermarkText = string.Empty;
            WatermarkOpacityPercent = 12;
            NumberFormat = "Página {n} de {total}";
            NumberPosition = PdfNumberPosition.PieDerecha;
            NumberFontSizePoints = 9F;
            FirstNumberedPage = 1;
            StartNumberingAt = 1;
        }

        /// <summary>Texto en diagonal. Vacio para no poner marca.</summary>
        public string WatermarkText { get; set; }

        /// <summary>Que se vea de 1 a 100. Poco, o tapa el documento.</summary>
        public int WatermarkOpacityPercent { get; set; }

        /// <summary>
        /// Plantilla de la numeracion; admite {n}, {total} y {archivo}.
        /// Vacia para no numerar.
        /// </summary>
        public string NumberFormat { get; set; }

        public PdfNumberPosition NumberPosition { get; set; }

        public float NumberFontSizePoints { get; set; }

        /// <summary>
        /// Primera hoja que lleva numero. Las portadas no suelen llevarlo.
        /// </summary>
        public int FirstNumberedPage { get; set; }

        /// <summary>Numero que se le pone a esa primera hoja.</summary>
        public int StartNumberingAt { get; set; }

        public bool HasWatermark
        {
            get { return !string.IsNullOrWhiteSpace(WatermarkText); }
        }

        public bool HasNumbering
        {
            get { return !string.IsNullOrWhiteSpace(NumberFormat); }
        }

        public PdfStampSettings Snapshot()
        {
            return new PdfStampSettings
            {
                WatermarkText = (WatermarkText ?? string.Empty).Trim(),
                WatermarkOpacityPercent = Math.Max(
                    3,
                    Math.Min(60, WatermarkOpacityPercent)),
                NumberFormat = (NumberFormat ?? string.Empty).Trim(),
                NumberPosition = NumberPosition,
                NumberFontSizePoints = Math.Max(
                    5F,
                    Math.Min(24F, NumberFontSizePoints)),
                FirstNumberedPage = Math.Max(1, FirstNumberedPage),
                StartNumberingAt = Math.Max(1, StartNumberingAt)
            };
        }
    }

    internal sealed class PdfStampResult
    {
        public PdfStampResult(
            string outputPath,
            int pageCount,
            int numberedPageCount,
            bool digitalSignaturesInvalidated)
        {
            OutputPath = outputPath;
            PageCount = pageCount;
            NumberedPageCount = numberedPageCount;
            DigitalSignaturesInvalidated = digitalSignaturesInvalidated;
        }

        public string OutputPath { get; private set; }

        public int PageCount { get; private set; }

        public int NumberedPageCount { get; private set; }

        public bool DigitalSignaturesInvalidated { get; private set; }
    }

    /// <summary>
    /// Marca de agua y numeracion de paginas.
    ///
    /// Se escriben encima del contenido en una revision nueva, sin rasterizar
    /// nada: el documento sigue siendo el mismo, con dos capas mas. El
    /// original se abre solo para leer.
    /// </summary>
    internal static class PdfStampService
    {
        public const string DigitalSignatureInvalidationWarning =
            "Estampar sobre el documento invalida la validez criptografica " +
            "de sus firmas digitales.";

        public const string XfaUnsupportedMessage =
            "Los formularios XFA no se pueden estampar de forma segura. " +
            "Guarda antes una copia PDF normal del formulario.";

        public static string SuggestOutputPath(string sourcePdfPath)
        {
            var source = IoPath.GetFullPath(sourcePdfPath);
            var directory = IoPath.GetDirectoryName(source);
            var baseName = IoPath.GetFileNameWithoutExtension(source);
            var candidate = IoPath.Combine(
                directory,
                baseName + " marcado.pdf");
            var suffix = 2;
            while (File.Exists(candidate))
            {
                candidate = IoPath.Combine(
                    directory,
                    baseName + " marcado (" +
                    suffix.ToString(CultureInfo.InvariantCulture) + ").pdf");
                suffix++;
            }

            return candidate;
        }

        /// <summary>
        /// Como queda la numeracion de una pagina concreta, para poder
        /// enseñarlo antes de aplicarlo.
        /// </summary>
        public static string Preview(
            PdfStampSettings settings,
            int pageNumber,
            int pageCount,
            string fileName)
        {
            var effective = (settings ?? new PdfStampSettings()).Snapshot();
            if (!effective.HasNumbering)
            {
                return string.Empty;
            }

            return FormatNumber(
                effective,
                pageNumber,
                pageCount,
                fileName);
        }

        public static PdfStampResult Stamp(
            string sourcePdfPath,
            string outputPath,
            PdfStampSettings settings,
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

            var effective = (settings ?? new PdfStampSettings()).Snapshot();
            if (!effective.HasWatermark && !effective.HasNumbering)
            {
                throw new ArgumentException(
                    "No se ha pedido ni marca de agua ni numeracion.",
                    "settings");
            }

            var fileName = IoPath.GetFileNameWithoutExtension(source);
            var temporaryPath = IoPath.Combine(
                IoPath.GetDirectoryName(target),
                "." + IoPath.GetFileNameWithoutExtension(target) +
                "." + Guid.NewGuid().ToString("N") + ".marcar.tmp");

            var numeradas = 0;
            var paginas = 0;
            var firmas = false;
            try
            {
                PdfReader reader = null;
                PdfStamper stamper = null;
                FileStream output = null;
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
                    paginas = reader.NumberOfPages;

                    output = new FileStream(
                        temporaryPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        1024 * 1024,
                        FileOptions.SequentialScan);
                    stamper = new PdfStamper(reader, output);
                    stamper.Writer.CloseStream = false;

                    var fuente = BaseFont.CreateFont(
                        BaseFont.HELVETICA,
                        BaseFont.CP1252,
                        BaseFont.NOT_EMBEDDED);

                    for (var pagina = 1; pagina <= paginas; pagina++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        Report(
                            reportStage,
                            "Marcando la página " +
                            pagina.ToString(CultureInfo.CurrentCulture) +
                            "…");

                        var tamano = reader.GetPageSizeWithRotation(pagina);
                        if (effective.HasWatermark)
                        {
                            DrawWatermark(
                                stamper,
                                pagina,
                                tamano,
                                fuente,
                                effective);
                        }

                        if (effective.HasNumbering &&
                            pagina >= effective.FirstNumberedPage)
                        {
                            DrawNumber(
                                stamper,
                                pagina,
                                tamano,
                                fuente,
                                effective,
                                paginas,
                                fileName);
                            numeradas++;
                        }
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

                cancellationToken.ThrowIfCancellationRequested();
                Report(reportStage, "Comprobando la copia…");
                ValidateWrittenPdf(temporaryPath, paginas);

                File.Move(temporaryPath, target);
                return new PdfStampResult(
                    target,
                    paginas,
                    numeradas,
                    firmas);
            }
            catch
            {
                TryDelete(temporaryPath);
                throw;
            }
        }

        /// <summary>
        /// La marca va debajo del contenido, no encima: asi no tapa el texto
        /// ni las lineas del plano, que es de lo que se queja todo el mundo
        /// con las marcas de agua.
        /// </summary>
        private static void DrawWatermark(
            PdfStamper stamper,
            int pageNumber,
            iTextSharp.text.Rectangle pageSize,
            BaseFont font,
            PdfStampSettings settings)
        {
            var content = stamper.GetUnderContent(pageNumber);
            var texto = settings.WatermarkText;
            var diagonal = Math.Sqrt(
                (pageSize.Width * pageSize.Width) +
                (pageSize.Height * pageSize.Height));

            // El cuerpo sale de la diagonal: la marca ocupa about el 80% del
            // ancho util sea cual sea el formato, de un A4 a un A0.
            var anchoUnitario = font.GetWidthPoint(texto, 1F);
            var cuerpo = anchoUnitario <= 0.01F
                ? 48F
                : (float)(diagonal * 0.72D / anchoUnitario);
            cuerpo = Math.Max(8F, Math.Min(400F, cuerpo));

            var estado = new PdfGState();
            estado.FillOpacity =
                settings.WatermarkOpacityPercent / 100F;
            content.SaveState();
            content.SetGState(estado);
            content.SetColorFill(iTextSharp.text.BaseColor.BLACK);
            content.BeginText();
            content.SetFontAndSize(font, cuerpo);
            var angulo = (float)(Math.Atan2(
                pageSize.Height,
                pageSize.Width) * 180D / Math.PI);
            content.ShowTextAligned(
                PdfContentByte.ALIGN_CENTER,
                texto,
                pageSize.Width / 2F,
                (pageSize.Height / 2F) - (cuerpo * 0.35F),
                angulo);
            content.EndText();
            content.RestoreState();
        }

        private static void DrawNumber(
            PdfStamper stamper,
            int pageNumber,
            iTextSharp.text.Rectangle pageSize,
            BaseFont font,
            PdfStampSettings settings,
            int pageCount,
            string fileName)
        {
            var texto = FormatNumber(
                settings,
                pageNumber,
                pageCount,
                fileName);
            if (texto.Length == 0)
            {
                return;
            }

            var margen = 28F;
            var esCabecera =
                settings.NumberPosition == PdfNumberPosition.CabeceraCentro ||
                settings.NumberPosition == PdfNumberPosition.CabeceraDerecha ||
                settings.NumberPosition == PdfNumberPosition.CabeceraIzquierda;
            var y = esCabecera
                ? pageSize.Height - margen
                : margen;

            int alineacion;
            float x;
            switch (settings.NumberPosition)
            {
                case PdfNumberPosition.PieIzquierda:
                case PdfNumberPosition.CabeceraIzquierda:
                    alineacion = PdfContentByte.ALIGN_LEFT;
                    x = margen;
                    break;

                case PdfNumberPosition.PieCentro:
                case PdfNumberPosition.CabeceraCentro:
                    alineacion = PdfContentByte.ALIGN_CENTER;
                    x = pageSize.Width / 2F;
                    break;

                default:
                    alineacion = PdfContentByte.ALIGN_RIGHT;
                    x = pageSize.Width - margen;
                    break;
            }

            var content = stamper.GetOverContent(pageNumber);
            content.SaveState();
            content.SetColorFill(iTextSharp.text.BaseColor.BLACK);
            content.BeginText();
            content.SetFontAndSize(font, settings.NumberFontSizePoints);
            content.ShowTextAligned(alineacion, texto, x, y, 0F);
            content.EndText();
            content.RestoreState();
        }

        private static string FormatNumber(
            PdfStampSettings settings,
            int pageNumber,
            int pageCount,
            string fileName)
        {
            // El numero que se imprime no tiene por que ser el de la hoja: una
            // memoria empieza a contar despues de la portada.
            var numero = settings.StartNumberingAt +
                (pageNumber - settings.FirstNumberedPage);
            var total = pageCount - settings.FirstNumberedPage +
                settings.StartNumberingAt;
            var texto = settings.NumberFormat;
            texto = texto.Replace(
                "{n}",
                numero.ToString(CultureInfo.CurrentCulture));
            texto = texto.Replace(
                "{total}",
                Math.Max(numero, total).ToString(CultureInfo.CurrentCulture));
            texto = texto.Replace("{archivo}", fileName ?? string.Empty);
            return texto;
        }

        private static void ValidateWrittenPdf(string path, int pageCount)
        {
            if (!File.Exists(path) || new FileInfo(path).Length <= 0)
            {
                throw new InvalidDataException(
                    "La copia marcada está vacía.");
            }

            using (var reader = new PdfReader(path))
            {
                if (reader.NumberOfPages != pageCount)
                {
                    throw new InvalidDataException(
                        "La copia marcada no conserva todas las páginas.");
                }
            }

            using (var document = PdfDocumentOpenService.Load(path))
            {
                if (document.PageCount != pageCount)
                {
                    throw new InvalidDataException(
                        "PDFium no puede abrir la copia marcada.");
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

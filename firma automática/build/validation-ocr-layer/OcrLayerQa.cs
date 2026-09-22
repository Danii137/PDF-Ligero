using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using FirmaAutomatica;
using iTextSharp.text.pdf;
using IoPath = System.IO.Path;

namespace OcrLayerQa
{
    /// <summary>
    /// Comprueba la geometria de la capa de texto invisible que deja el OCR.
    ///
    /// Esto es lo que quedo sin verificar cuando se arreglaron las "franjas
    /// verticales del subrayado": el subrayador toma el alto de cada renglon
    /// de esta capa, y si la capa declara renglones de media pagina, pinta
    /// franjas de media pagina. Se mide con la misma clase que usa el
    /// subrayador, PdfTextBlockLocator, y no con una lectura aparte.
    /// </summary>
    internal static class Program
    {
        // Ningun renglon de un documento real pasa de una pulgada.
        private const float AltoMaximoPuntos = 72F;

        private static int Main(string[] args)
        {
            try
            {
                if (args.Length < 1)
                {
                    Console.Error.WriteLine(
                        "Uso: OcrLayerQa <pdf con capa OCR>");
                    return 2;
                }

                var path = IoPath.GetFullPath(args[0]);
                if (!File.Exists(path))
                {
                    Console.Error.WriteLine("No existe: " + path);
                    return 2;
                }

                var fallos = 0;
                var renglones = 0;
                var ocrRenglones = 0;
                var altoMaximo = 0F;
                var altoMaximoPagina = 0;
                string altoMaximoTexto = null;
                var cuerpoMaximo = 0F;
                var altos = new List<float>();

                using (var reader = new PdfReader(path))
                {
                    var alturaPagina =
                        reader.GetPageSizeWithRotation(1).Height;
                    for (var pagina = 1;
                        pagina <= reader.NumberOfPages;
                        pagina++)
                    {
                        var bloques = PdfTextBlockLocator.Locate(
                            reader,
                            pagina);
                        foreach (var bloque in bloques)
                        {
                            renglones++;
                            if (bloque.FromOcr)
                            {
                                ocrRenglones++;
                            }

                            var alto = bloque.Bounds.Height;
                            altos.Add(alto);
                            if (alto > altoMaximo)
                            {
                                altoMaximo = alto;
                                altoMaximoPagina = pagina;
                                altoMaximoTexto = bloque.Text;
                            }

                            if (bloque.Style != null &&
                                bloque.Style.FontSizePoints > cuerpoMaximo)
                            {
                                cuerpoMaximo = bloque.Style.FontSizePoints;
                            }
                        }
                    }

                    Console.WriteLine(
                        "Paginas: " +
                        reader.NumberOfPages.ToString(
                            CultureInfo.InvariantCulture) +
                        "   alto de pagina: " +
                        alturaPagina.ToString(
                            "0.0", CultureInfo.InvariantCulture) +
                        " pt");
                }

                altos.Sort();
                var mediana = altos.Count == 0
                    ? 0F
                    : altos[altos.Count / 2];

                Console.WriteLine(
                    "Renglones: " +
                    renglones.ToString(CultureInfo.InvariantCulture) +
                    "   de ellos marcados como OCR: " +
                    ocrRenglones.ToString(CultureInfo.InvariantCulture));
                Console.WriteLine(
                    "Alto de renglon: mediana " +
                    mediana.ToString("0.00", CultureInfo.InvariantCulture) +
                    " pt   maximo " +
                    altoMaximo.ToString("0.00", CultureInfo.InvariantCulture) +
                    " pt");
                Console.WriteLine(
                    "Cuerpo de letra maximo: " +
                    cuerpoMaximo.ToString(
                        "0.00", CultureInfo.InvariantCulture) + " pt");
                if (altoMaximoTexto != null)
                {
                    Console.WriteLine(
                        "Renglon mas alto (pagina " +
                        altoMaximoPagina.ToString(
                            CultureInfo.InvariantCulture) + "): \"" +
                        Recortar(altoMaximoTexto) + "\"");
                }

                if (renglones == 0)
                {
                    Console.Error.WriteLine(
                        "FAIL: no se localizo ningun renglon de texto.");
                    fallos++;
                }

                if (altoMaximo > AltoMaximoPuntos)
                {
                    Console.Error.WriteLine(
                        "FAIL: hay un renglon de " +
                        altoMaximo.ToString(
                            "0.0", CultureInfo.InvariantCulture) +
                        " pt; el subrayador pintaria una franja.");
                    fallos++;
                }

                if (cuerpoMaximo > AltoMaximoPuntos)
                {
                    Console.Error.WriteLine(
                        "FAIL: cuerpo de letra de " +
                        cuerpoMaximo.ToString(
                            "0.0", CultureInfo.InvariantCulture) + " pt.");
                    fallos++;
                }

                fallos += ComprobarCasoPatologico(
                    IoPath.GetDirectoryName(path));

                if (fallos == 0)
                {
                    Console.WriteLine(
                        "PASS: la capa de texto del OCR tiene renglones de " +
                        "tamano normal.");
                    return 0;
                }

                return 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: " + ex);
                return 3;
            }
        }

        /// <summary>
        /// Reproduce lo que hacia el OCR antes: una palabra corta cuya caja
        /// mide media pagina, que acababa declarada como una letra de 400
        /// puntos. Es el caso que pintaba franjas amarillas de arriba abajo.
        /// Si el guardia del localizador desaparece, esta comprobacion falla.
        /// </summary>
        private static int ComprobarCasoPatologico(string directorio)
        {
            var path = IoPath.Combine(
                directorio,
                "capa-ocr-metricas-desmesuradas.pdf");
            using (var document = new iTextSharp.text.Document(
                iTextSharp.text.PageSize.A4))
            {
                using (var stream = new FileStream(
                    path,
                    FileMode.Create,
                    FileAccess.Write))
                {
                    var writer = PdfWriter.GetInstance(document, stream);
                    document.Open();
                    var content = writer.DirectContent;
                    var font = BaseFont.CreateFont(
                        BaseFont.HELVETICA,
                        BaseFont.CP1252,
                        BaseFont.NOT_EMBEDDED);
                    content.BeginText();
                    content.SetTextRenderingMode(
                        PdfContentByte.TEXT_RENDER_MODE_INVISIBLE);
                    content.SetFontAndSize(font, 400F);
                    content.SetTextMatrix(50F, 400F);
                    content.ShowText("obra");
                    content.EndText();
                    document.Close();
                    writer.Close();
                }
            }

            var alto = 0F;
            using (var reader = new PdfReader(path))
            {
                foreach (var bloque in PdfTextBlockLocator.Locate(reader, 1))
                {
                    if (bloque.Bounds.Height > alto)
                    {
                        alto = bloque.Bounds.Height;
                    }
                }
            }

            Console.WriteLine(
                "Caso patologico (letra declarada de 400 pt): renglon de " +
                alto.ToString("0.0", CultureInfo.InvariantCulture) + " pt");
            if (alto > AltoMaximoPuntos)
            {
                Console.Error.WriteLine(
                    "FAIL: la metrica disparatada pasa sin acotar; " +
                    "volverian las franjas del subrayador.");
                return 1;
            }

            return 0;
        }

        private static string Recortar(string texto)
        {
            var limpio = (texto ?? string.Empty).Replace("\r", " ")
                .Replace("\n", " ").Trim();
            return limpio.Length <= 60
                ? limpio
                : limpio.Substring(0, 57) + "...";
        }
    }
}

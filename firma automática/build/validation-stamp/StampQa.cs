using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using FirmaAutomatica;
using iTextSharp.text;
using iTextSharp.text.pdf;
using IoPath = System.IO.Path;

namespace StampQa
{
    /// <summary>
    /// Marca de agua y numeracion de hojas.
    ///
    /// Se comprueba leyendo el texto del resultado: el numero tiene que estar
    /// escrito en la hoja que toca y con la cuenta que toca, incluso cuando la
    /// numeracion no empieza en la primera pagina.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                var directorio = args.Length > 0
                    ? IoPath.GetFullPath(args[0])
                    : IoPath.Combine(
                        IoPath.GetTempPath(),
                        "qa-marcar-pdf");
                if (Directory.Exists(directorio))
                {
                    Directory.Delete(directorio, true);
                }

                Directory.CreateDirectory(directorio);
                var origen = IoPath.Combine(directorio, "memoria.pdf");
                CrearFixture(origen, 8);
                var huella = Hash(origen);

                var fallos = 0;
                fallos += ComprobarNumeracionSimple(origen, directorio);
                fallos += ComprobarNumeracionDesdeLaSegunda(
                    origen,
                    directorio);
                fallos += ComprobarMarcaDeAgua(origen, directorio);
                fallos += ComprobarSinNadaQueHacer(origen, directorio);
                fallos += ComprobarOriginalIntacto(origen, huella);

                if (fallos == 0)
                {
                    Console.WriteLine(
                        "PASS: marca de agua y numeración funcionan.");
                    return 0;
                }

                Console.Error.WriteLine(
                    "FAIL: " +
                    fallos.ToString(CultureInfo.InvariantCulture) +
                    " comprobaciones.");
                return 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: " + ex);
                return 3;
            }
        }

        private static int ComprobarNumeracionSimple(
            string origen,
            string directorio)
        {
            var destino = IoPath.Combine(directorio, "numerada.pdf");
            var resultado = PdfStampService.Stamp(
                origen,
                destino,
                new PdfStampSettings
                {
                    WatermarkText = string.Empty,
                    NumberFormat = "Pagina {n} de {total}",
                    NumberPosition = PdfNumberPosition.PieDerecha
                },
                null,
                CancellationToken.None);

            Console.WriteLine(
                "Numeracion simple: " +
                resultado.NumberedPageCount.ToString(
                    CultureInfo.InvariantCulture) + " de " +
                resultado.PageCount.ToString(CultureInfo.InvariantCulture) +
                " hojas numeradas.");

            var fallos = 0;
            if (resultado.NumberedPageCount != 8)
            {
                Console.Error.WriteLine(
                    "FAIL: no se numeraron las 8 hojas.");
                fallos++;
            }

            using (var reader = new PdfReader(resultado.OutputPath))
            {
                for (var pagina = 1; pagina <= 8; pagina++)
                {
                    var texto = LeerTexto(reader, pagina);
                    var esperado = "Pagina " +
                        pagina.ToString(CultureInfo.InvariantCulture) +
                        " de 8";
                    if (texto.IndexOf(esperado, StringComparison.Ordinal) < 0)
                    {
                        Console.Error.WriteLine(
                            "FAIL: la hoja " +
                            pagina.ToString(CultureInfo.InvariantCulture) +
                            " no dice \"" + esperado + "\".");
                        fallos++;
                    }
                }
            }

            return fallos;
        }

        private static int ComprobarNumeracionDesdeLaSegunda(
            string origen,
            string directorio)
        {
            // El caso de una memoria: la portada no lleva numero, y la hoja
            // siguiente es la 1 de 7.
            var destino = IoPath.Combine(directorio, "sin portada.pdf");
            var resultado = PdfStampService.Stamp(
                origen,
                destino,
                new PdfStampSettings
                {
                    WatermarkText = string.Empty,
                    NumberFormat = "{n} / {total}",
                    NumberPosition = PdfNumberPosition.PieCentro,
                    FirstNumberedPage = 2,
                    StartNumberingAt = 1
                },
                null,
                CancellationToken.None);

            Console.WriteLine(
                "Numeracion desde la hoja 2: " +
                resultado.NumberedPageCount.ToString(
                    CultureInfo.InvariantCulture) + " hojas numeradas.");

            var fallos = 0;
            if (resultado.NumberedPageCount != 7)
            {
                Console.Error.WriteLine(
                    "FAIL: se esperaban 7 hojas numeradas.");
                fallos++;
            }

            using (var reader = new PdfReader(resultado.OutputPath))
            {
                var portada = LeerTexto(reader, 1);
                if (portada.IndexOf("/ 7", StringComparison.Ordinal) >= 0)
                {
                    Console.Error.WriteLine(
                        "FAIL: la portada lleva numero.");
                    fallos++;
                }

                var segunda = LeerTexto(reader, 2);
                if (segunda.IndexOf("1 / 7", StringComparison.Ordinal) < 0)
                {
                    Console.Error.WriteLine(
                        "FAIL: la hoja 2 no dice \"1 / 7\".");
                    fallos++;
                }

                var ultima = LeerTexto(reader, 8);
                if (ultima.IndexOf("7 / 7", StringComparison.Ordinal) < 0)
                {
                    Console.Error.WriteLine(
                        "FAIL: la ultima hoja no dice \"7 / 7\".");
                    fallos++;
                }
            }

            return fallos;
        }

        private static int ComprobarMarcaDeAgua(
            string origen,
            string directorio)
        {
            var destino = IoPath.Combine(directorio, "con marca.pdf");
            var resultado = PdfStampService.Stamp(
                origen,
                destino,
                new PdfStampSettings
                {
                    WatermarkText = "BORRADOR",
                    WatermarkOpacityPercent = 14,
                    NumberFormat = string.Empty
                },
                null,
                CancellationToken.None);

            var fallos = 0;
            using (var reader = new PdfReader(resultado.OutputPath))
            {
                if (reader.NumberOfPages != 8)
                {
                    Console.Error.WriteLine(
                        "FAIL: la copia marcada ha perdido hojas.");
                    fallos++;
                }

                for (var pagina = 1; pagina <= reader.NumberOfPages; pagina++)
                {
                    var texto = LeerTexto(reader, pagina);
                    if (texto.IndexOf(
                            "BORRADOR",
                            StringComparison.Ordinal) < 0)
                    {
                        Console.Error.WriteLine(
                            "FAIL: falta la marca en la hoja " +
                            pagina.ToString(CultureInfo.InvariantCulture) +
                            ".");
                        fallos++;
                    }

                    // El contenido original tiene que seguir ahi: la marca se
                    // pone debajo, no lo sustituye.
                    if (texto.IndexOf(
                            "Memoria descriptiva",
                            StringComparison.Ordinal) < 0)
                    {
                        Console.Error.WriteLine(
                            "FAIL: la hoja " +
                            pagina.ToString(CultureInfo.InvariantCulture) +
                            " ha perdido su contenido.");
                        fallos++;
                    }
                }
            }

            Console.WriteLine(
                "Marca de agua: presente en las 8 hojas, sin tapar el texto.");
            return fallos;
        }

        private static int ComprobarSinNadaQueHacer(
            string origen,
            string directorio)
        {
            var destino = IoPath.Combine(directorio, "nada.pdf");
            try
            {
                PdfStampService.Stamp(
                    origen,
                    destino,
                    new PdfStampSettings
                    {
                        WatermarkText = string.Empty,
                        NumberFormat = string.Empty
                    },
                    null,
                    CancellationToken.None);
            }
            catch (ArgumentException)
            {
                Console.WriteLine(
                    "Sin marca ni numeros: rechazado, como debe ser.");
                return 0;
            }

            Console.Error.WriteLine(
                "FAIL: ha creado una copia sin marcar nada.");
            return 1;
        }

        private static int ComprobarOriginalIntacto(
            string origen,
            string huella)
        {
            if (!string.Equals(
                    Hash(origen),
                    huella,
                    StringComparison.Ordinal))
            {
                Console.Error.WriteLine(
                    "FAIL: el PDF original ha cambiado.");
                return 1;
            }

            Console.WriteLine("Original intacto por SHA-256.");
            return 0;
        }

        private static string LeerTexto(PdfReader reader, int pagina)
        {
            return iTextSharp.text.pdf.parser.PdfTextExtractor
                .GetTextFromPage(reader, pagina);
        }

        private static void CrearFixture(string path, int paginas)
        {
            using (var document = new Document(PageSize.A4, 56F, 56F, 56F, 56F))
            {
                using (var stream = new FileStream(
                    path,
                    FileMode.Create,
                    FileAccess.Write))
                {
                    var writer = PdfWriter.GetInstance(document, stream);
                    document.Open();
                    var fuente = FontFactory.GetFont(
                        FontFactory.HELVETICA,
                        12F);
                    for (var pagina = 1; pagina <= paginas; pagina++)
                    {
                        if (pagina > 1)
                        {
                            document.NewPage();
                        }

                        document.Add(
                            new Paragraph(
                                "Memoria descriptiva, hoja " +
                                pagina.ToString(CultureInfo.InvariantCulture),
                                fuente));
                    }

                    document.Close();
                    writer.Close();
                }
            }
        }

        private static string Hash(string path)
        {
            using (var sha = SHA256.Create())
            {
                using (var stream = File.OpenRead(path))
                {
                    return BitConverter.ToString(sha.ComputeHash(stream))
                        .Replace("-", string.Empty);
                }
            }
        }
    }
}

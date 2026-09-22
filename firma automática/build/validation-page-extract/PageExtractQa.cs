using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using FirmaAutomatica;
using iTextSharp.text;
using iTextSharp.text.pdf;
using IoPath = System.IO.Path;

namespace PageExtractQa
{
    /// <summary>
    /// Extraer paginas a un PDF nuevo y partir un documento en varios.
    ///
    /// Lo que importa aqui es que salgan las paginas que se pidieron, en su
    /// orden, y que el original quede intacto byte a byte.
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
                        "qa-extraer-paginas");
                if (Directory.Exists(directorio))
                {
                    Directory.Delete(directorio, true);
                }

                Directory.CreateDirectory(directorio);
                var origen = IoPath.Combine(
                    directorio,
                    "proyecto básico - ejecución.pdf");
                CrearFixture(origen, 12);
                var huellaOriginal = Hash(origen);

                var fallos = 0;
                fallos += ComprobarExtraccionSeguida(origen, directorio);
                fallos += ComprobarExtraccionSalteada(origen, directorio);
                fallos += ComprobarDivision(origen, directorio);
                fallos += ComprobarOriginalIntacto(origen, huellaOriginal);

                if (fallos == 0)
                {
                    Console.WriteLine(
                        "PASS: extraer y dividir páginas funciona.");
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

        private static int ComprobarExtraccionSeguida(
            string origen,
            string directorio)
        {
            var paginas = new List<int> { 4, 5, 6 };
            var destino = IoPath.Combine(directorio, "extracto seguido.pdf");
            var resultado = PdfPageExtractService.Extract(
                origen,
                paginas,
                destino,
                null,
                CancellationToken.None);

            Console.WriteLine(
                "Extraccion 4-6: " +
                resultado.ExtractedPageCount.ToString(
                    CultureInfo.InvariantCulture) + " paginas -> " +
                IoPath.GetFileName(resultado.OutputPaths[0]));
            return ComprobarContenido(
                resultado.OutputPaths[0],
                paginas,
                "extraccion seguida");
        }

        private static int ComprobarExtraccionSalteada(
            string origen,
            string directorio)
        {
            // Salteadas y desordenadas: el servicio debe respetar el orden
            // que se pide, porque asi se rehace un documento a la carta.
            var paginas = new List<int> { 9, 2, 11 };
            var destino = IoPath.Combine(directorio, "extracto salteado.pdf");
            var resultado = PdfPageExtractService.Extract(
                origen,
                paginas,
                destino,
                null,
                CancellationToken.None);

            Console.WriteLine(
                "Extraccion 9, 2, 11: " +
                resultado.ExtractedPageCount.ToString(
                    CultureInfo.InvariantCulture) + " paginas.");
            return ComprobarContenido(
                resultado.OutputPaths[0],
                paginas,
                "extraccion salteada");
        }

        private static int ComprobarDivision(
            string origen,
            string directorio)
        {
            var salida = IoPath.Combine(directorio, "division");
            Directory.CreateDirectory(salida);
            var resultado = PdfPageExtractService.Split(
                origen,
                5,
                salida,
                null,
                CancellationToken.None);

            Console.WriteLine(
                "Division en partes de 5: " +
                resultado.OutputPaths.Count.ToString(
                    CultureInfo.InvariantCulture) + " archivos, " +
                resultado.ExtractedPageCount.ToString(
                    CultureInfo.InvariantCulture) + " paginas en total.");

            var fallos = 0;
            // 12 paginas de 5 en 5: 5 + 5 + 2, y ni una pagina perdida.
            if (resultado.OutputPaths.Count != 3)
            {
                Console.Error.WriteLine(
                    "FAIL: se esperaban 3 archivos.");
                fallos++;
            }

            if (resultado.ExtractedPageCount != 12)
            {
                Console.Error.WriteLine(
                    "FAIL: se han perdido paginas al dividir.");
                fallos++;
            }

            var esperadas = new[]
            {
                new List<int> { 1, 2, 3, 4, 5 },
                new List<int> { 6, 7, 8, 9, 10 },
                new List<int> { 11, 12 }
            };
            for (var i = 0;
                i < esperadas.Length && i < resultado.OutputPaths.Count;
                i++)
            {
                fallos += ComprobarContenido(
                    resultado.OutputPaths[i],
                    esperadas[i],
                    "parte " +
                    (i + 1).ToString(CultureInfo.InvariantCulture));
            }

            return fallos;
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

        /// <summary>
        /// Cada pagina del fixture lleva escrito su numero: leyendo el texto
        /// del resultado se sabe exactamente que paginas salieron y en que
        /// orden.
        /// </summary>
        private static int ComprobarContenido(
            string path,
            IList<int> esperadas,
            string caso)
        {
            var fallos = 0;
            using (var reader = new PdfReader(path))
            {
                if (reader.NumberOfPages != esperadas.Count)
                {
                    Console.Error.WriteLine(
                        "FAIL (" + caso + "): " +
                        reader.NumberOfPages.ToString(
                            CultureInfo.InvariantCulture) +
                        " paginas en vez de " +
                        esperadas.Count.ToString(
                            CultureInfo.InvariantCulture) + ".");
                    return 1;
                }

                for (var i = 0; i < esperadas.Count; i++)
                {
                    var texto = iTextSharp.text.pdf.parser.PdfTextExtractor
                        .GetTextFromPage(reader, i + 1);
                    var marca = "PAGINA " +
                        esperadas[i].ToString(CultureInfo.InvariantCulture) +
                        " DE 12";
                    if (texto.IndexOf(marca, StringComparison.Ordinal) < 0)
                    {
                        Console.Error.WriteLine(
                            "FAIL (" + caso + "): la hoja " +
                            (i + 1).ToString(CultureInfo.InvariantCulture) +
                            " no es la pagina " +
                            esperadas[i].ToString(
                                CultureInfo.InvariantCulture) + ".");
                        fallos++;
                    }
                }
            }

            return fallos;
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
                        18F);
                    for (var pagina = 1; pagina <= paginas; pagina++)
                    {
                        if (pagina > 1)
                        {
                            document.NewPage();
                        }

                        document.Add(
                            new Paragraph(
                                "PAGINA " +
                                pagina.ToString(CultureInfo.InvariantCulture) +
                                " DE " +
                                paginas.ToString(CultureInfo.InvariantCulture),
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

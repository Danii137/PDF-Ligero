using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using FirmaAutomatica;
using iTextSharp.text;
using iTextSharp.text.pdf;
using IoPath = System.IO.Path;

namespace PageToolsQa
{
    /// <summary>
    /// Duplicar una pagina, meter una hoja en blanco y recortar margenes.
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
                        "qa-herramientas-pagina");
                if (Directory.Exists(directorio))
                {
                    Directory.Delete(directorio, true);
                }

                Directory.CreateDirectory(directorio);

                var fallos = 0;
                fallos += ComprobarDuplicar(directorio);
                fallos += ComprobarHojaEnBlanco(directorio);
                fallos += ComprobarRecorte(directorio);
                fallos += ComprobarRecorteImposible(directorio);

                if (fallos == 0)
                {
                    Console.WriteLine(
                        "PASS: duplicar, hoja en blanco y recortar funcionan.");
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

        private static int ComprobarDuplicar(string directorio)
        {
            var origen = IoPath.Combine(directorio, "original.pdf");
            CrearNumerado(origen, 4);
            var huella = Hash(origen);
            var destino = IoPath.Combine(directorio, "duplicado.pdf");
            PdfPageToolsService.DuplicatePage(
                origen,
                destino,
                2,
                CancellationToken.None);

            var fallos = 0;
            using (var reader = new PdfReader(destino))
            {
                Console.WriteLine(
                    "Duplicar la 2: " +
                    reader.NumberOfPages.ToString(
                        CultureInfo.InvariantCulture) + " paginas.");
                if (reader.NumberOfPages != 5)
                {
                    Console.Error.WriteLine(
                        "FAIL: se esperaban 5 paginas.");
                    return 1;
                }

                // El orden tiene que ser 1, 2, 2, 3, 4.
                var esperado = new[] { 1, 2, 2, 3, 4 };
                for (var i = 0; i < esperado.Length; i++)
                {
                    var texto = iTextSharp.text.pdf.parser.PdfTextExtractor
                        .GetTextFromPage(reader, i + 1);
                    var marca = "HOJA " +
                        esperado[i].ToString(CultureInfo.InvariantCulture);
                    if (texto.IndexOf(marca, StringComparison.Ordinal) < 0)
                    {
                        Console.Error.WriteLine(
                            "FAIL: la hoja " +
                            (i + 1).ToString(CultureInfo.InvariantCulture) +
                            " deberia ser la " + marca + ".");
                        fallos++;
                    }
                }
            }

            if (!string.Equals(Hash(origen), huella, StringComparison.Ordinal))
            {
                Console.Error.WriteLine("FAIL: el original ha cambiado.");
                fallos++;
            }

            return fallos;
        }

        private static int ComprobarHojaEnBlanco(string directorio)
        {
            var origen = IoPath.Combine(directorio, "para blanca.pdf");
            CrearNumerado(origen, 3);
            var destino = IoPath.Combine(directorio, "con blanca.pdf");
            PdfPageToolsService.InsertBlankPage(
                origen,
                destino,
                2,
                CancellationToken.None);

            var fallos = 0;
            using (var reader = new PdfReader(destino))
            {
                Console.WriteLine(
                    "Hoja en blanco antes de la 2: " +
                    reader.NumberOfPages.ToString(
                        CultureInfo.InvariantCulture) + " paginas.");
                if (reader.NumberOfPages != 4)
                {
                    Console.Error.WriteLine(
                        "FAIL: se esperaban 4 paginas.");
                    return 1;
                }

                var blanca = iTextSharp.text.pdf.parser.PdfTextExtractor
                    .GetTextFromPage(reader, 2);
                if (!string.IsNullOrWhiteSpace(blanca))
                {
                    Console.Error.WriteLine(
                        "FAIL: la hoja 2 no esta en blanco.");
                    fallos++;
                }

                // Y tiene el tamaño de las demas, no un A4 por defecto.
                var tamanoBlanca = reader.GetPageSize(2);
                var tamanoOtra = reader.GetPageSize(1);
                if (Math.Abs(tamanoBlanca.Width - tamanoOtra.Width) > 1F ||
                    Math.Abs(tamanoBlanca.Height - tamanoOtra.Height) > 1F)
                {
                    Console.Error.WriteLine(
                        "FAIL: la hoja en blanco mide " +
                        tamanoBlanca.Width.ToString(
                            "0", CultureInfo.InvariantCulture) + "x" +
                        tamanoBlanca.Height.ToString(
                            "0", CultureInfo.InvariantCulture) +
                        " y las demas " +
                        tamanoOtra.Width.ToString(
                            "0", CultureInfo.InvariantCulture) + "x" +
                        tamanoOtra.Height.ToString(
                            "0", CultureInfo.InvariantCulture) + ".");
                    fallos++;
                }

                var siguiente = iTextSharp.text.pdf.parser.PdfTextExtractor
                    .GetTextFromPage(reader, 3);
                if (siguiente.IndexOf(
                        "HOJA 2",
                        StringComparison.Ordinal) < 0)
                {
                    Console.Error.WriteLine(
                        "FAIL: la hoja original 2 no viene detras.");
                    fallos++;
                }
            }

            return fallos;
        }

        private static int ComprobarRecorte(string directorio)
        {
            // Una pagina A4 con el dibujo metido en el centro: al recortar al
            // contenido tiene que quedarse holgadamente mas pequeña.
            var origen = IoPath.Combine(directorio, "con margenes.pdf");
            CrearConMargenes(origen);
            var destino = IoPath.Combine(directorio, "recortado.pdf");
            var resultado = PdfPageToolsService.CropPages(
                origen,
                destino,
                null,
                PdfCropMode.AlContenido,
                5F,
                null,
                CancellationToken.None);

            var fallos = 0;
            using (var reader = new PdfReader(destino))
            {
                var caja = reader.GetCropBox(1);
                var media = reader.GetPageSize(1);
                Console.WriteLine(
                    "Recorte al contenido: de " +
                    media.Width.ToString("0", CultureInfo.InvariantCulture) +
                    "x" +
                    media.Height.ToString("0", CultureInfo.InvariantCulture) +
                    " a " +
                    caja.Width.ToString("0", CultureInfo.InvariantCulture) +
                    "x" +
                    caja.Height.ToString("0", CultureInfo.InvariantCulture) +
                    " puntos.");

                if (resultado.CroppedPageCount != 1)
                {
                    Console.Error.WriteLine(
                        "FAIL: no se recorto la pagina.");
                    fallos++;
                }

                // El dibujo ocupa 200x300 puntos en el centro de un A4; con 5
                // mm de margen la caja debe rondar 228x328 y desde luego
                // quedar por debajo de la mitad del alto del A4.
                if (caja.Height > media.Height * 0.55F)
                {
                    Console.Error.WriteLine(
                        "FAIL: apenas ha recortado.");
                    fallos++;
                }

                if (caja.Width < 180F || caja.Height < 280F)
                {
                    Console.Error.WriteLine(
                        "FAIL: ha recortado de mas y se come el dibujo.");
                    fallos++;
                }
            }

            return fallos;
        }

        private static int ComprobarRecorteImposible(string directorio)
        {
            // Un margen fijo mas grande que la propia pagina: no se puede
            // dejar la hoja en nada, asi que se queda como estaba.
            var origen = IoPath.Combine(directorio, "para recorte bestia.pdf");
            CrearNumerado(origen, 1);
            var destino = IoPath.Combine(directorio, "recorte bestia.pdf");
            var resultado = PdfPageToolsService.CropPages(
                origen,
                destino,
                null,
                PdfCropMode.MargenFijo,
                200F,
                null,
                CancellationToken.None);

            if (resultado.CroppedPageCount != 0)
            {
                Console.Error.WriteLine(
                    "FAIL: ha recortado la pagina hasta dejarla en nada.");
                return 1;
            }

            using (var reader = new PdfReader(destino))
            {
                if (reader.NumberOfPages != 1)
                {
                    Console.Error.WriteLine(
                        "FAIL: se ha perdido la pagina.");
                    return 1;
                }
            }

            Console.WriteLine(
                "Recorte imposible: la pagina se queda como estaba.");
            return 0;
        }

        private static void CrearNumerado(string path, int paginas)
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
                                "HOJA " +
                                pagina.ToString(CultureInfo.InvariantCulture),
                                fuente));
                    }

                    document.Close();
                    writer.Close();
                }
            }
        }

        private static void CrearConMargenes(string path)
        {
            using (var document = new Document(PageSize.A4, 0F, 0F, 0F, 0F))
            {
                using (var stream = new FileStream(
                    path,
                    FileMode.Create,
                    FileAccess.Write))
                {
                    var writer = PdfWriter.GetInstance(document, stream);
                    document.Open();
                    var content = writer.DirectContent;
                    content.SetColorStroke(BaseColor.BLACK);
                    content.SetLineWidth(2F);
                    // Un rectangulo de 200x300 centrado en el A4.
                    var x = (PageSize.A4.Width - 200F) / 2F;
                    var y = (PageSize.A4.Height - 300F) / 2F;
                    content.Rectangle(x, y, 200F, 300F);
                    content.Stroke();
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

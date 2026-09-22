using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using FirmaAutomatica;
using iTextSharp.text;
using iTextSharp.text.pdf;
using IoPath = System.IO.Path;

namespace ExportQa
{
    /// <summary>
    /// Exportar paginas como imagen y el texto del documento.
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
                        "qa-exportar-pdf");
                if (Directory.Exists(directorio))
                {
                    Directory.Delete(directorio, true);
                }

                Directory.CreateDirectory(directorio);
                var origen = IoPath.Combine(directorio, "memoria.pdf");
                CrearFixture(origen, 4);

                var fallos = 0;
                fallos += ComprobarImagenes(origen, directorio);
                fallos += ComprobarTexto(origen, directorio);
                fallos += ComprobarPdfSinTexto(directorio);

                if (fallos == 0)
                {
                    Console.WriteLine("PASS: exportar funciona.");
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

        private static int ComprobarImagenes(
            string origen,
            string directorio)
        {
            var salida = IoPath.Combine(directorio, "imagenes");
            var paginas = new List<int> { 1, 3 };
            var resultado = PdfExportService.ExportImages(
                origen,
                paginas,
                salida,
                150,
                PdfExportImageFormat.Png,
                88,
                null,
                CancellationToken.None);

            var fallos = 0;
            Console.WriteLine(
                "Imagenes: " +
                resultado.OutputPaths.Count.ToString(
                    CultureInfo.InvariantCulture) + " archivos.");

            if (resultado.OutputPaths.Count != 2)
            {
                Console.Error.WriteLine(
                    "FAIL: se esperaban 2 imagenes.");
                return 1;
            }

            foreach (var path in resultado.OutputPaths)
            {
                if (!File.Exists(path))
                {
                    Console.Error.WriteLine("FAIL: falta " + path);
                    fallos++;
                    continue;
                }

                using (var imagen = System.Drawing.Image.FromFile(path))
                {
                    // A4 a 150 ppp son unos 1240 x 1754 pixeles.
                    if (imagen.Width < 1100 || imagen.Height < 1600)
                    {
                        Console.Error.WriteLine(
                            "FAIL: " + IoPath.GetFileName(path) +
                            " sale a " +
                            imagen.Width.ToString(
                                CultureInfo.InvariantCulture) + "x" +
                            imagen.Height.ToString(
                                CultureInfo.InvariantCulture) +
                            ", muy por debajo de 150 ppp en A4.");
                        fallos++;
                    }

                    // Ni todo blanco ni todo negro: tiene que haberse
                    // dibujado el texto de la pagina.
                    if (!TieneContenido(path))
                    {
                        Console.Error.WriteLine(
                            "FAIL: " + IoPath.GetFileName(path) +
                            " ha salido en blanco.");
                        fallos++;
                    }
                }
            }

            return fallos;
        }

        private static bool TieneContenido(string path)
        {
            using (var bitmap = new Bitmap(path))
            {
                var oscuros = 0;
                for (var y = 0; y < bitmap.Height; y += 4)
                {
                    for (var x = 0; x < bitmap.Width; x += 4)
                    {
                        var color = bitmap.GetPixel(x, y);
                        if (color.R < 160 && color.G < 160 && color.B < 160)
                        {
                            oscuros++;
                            if (oscuros > 40)
                            {
                                return true;
                            }
                        }
                    }
                }

                return false;
            }
        }

        private static int ComprobarTexto(string origen, string directorio)
        {
            var destino = IoPath.Combine(directorio, "memoria.txt");
            var resultado = PdfExportService.ExportText(
                origen,
                destino,
                null,
                CancellationToken.None);

            if (resultado.OutputPaths.Count != 1)
            {
                Console.Error.WriteLine("FAIL: no se escribio el texto.");
                return 1;
            }

            var texto = File.ReadAllText(destino, Encoding.UTF8);
            Console.WriteLine(
                "Texto: " +
                texto.Length.ToString(CultureInfo.InvariantCulture) +
                " caracteres, " +
                resultado.PageCount.ToString(CultureInfo.InvariantCulture) +
                " paginas.");

            var fallos = 0;
            for (var pagina = 1; pagina <= 4; pagina++)
            {
                var marca = "Hoja " +
                    pagina.ToString(CultureInfo.InvariantCulture) +
                    " con acentos: memoria descriptiva";
                if (texto.IndexOf(marca, StringComparison.Ordinal) < 0)
                {
                    Console.Error.WriteLine(
                        "FAIL: falta el texto de la pagina " +
                        pagina.ToString(CultureInfo.InvariantCulture) + ".");
                    fallos++;
                }
            }

            // El orden de las paginas importa al pegar el texto en otro sitio.
            var primera = texto.IndexOf("Hoja 1", StringComparison.Ordinal);
            var ultima = texto.IndexOf("Hoja 4", StringComparison.Ordinal);
            if (primera < 0 || ultima < 0 || ultima < primera)
            {
                Console.Error.WriteLine(
                    "FAIL: las paginas salen desordenadas.");
                fallos++;
            }

            return fallos;
        }

        private static int ComprobarPdfSinTexto(string directorio)
        {
            // Un escaneo sin OCR: no hay texto que sacar y no se debe crear
            // un archivo con solo los separadores de pagina.
            var escaneo = IoPath.Combine(directorio, "escaneo sin ocr.pdf");
            CrearSoloImagen(escaneo);
            var destino = IoPath.Combine(directorio, "escaneo.txt");
            var resultado = PdfExportService.ExportText(
                escaneo,
                destino,
                null,
                CancellationToken.None);

            if (resultado.OutputPaths.Count != 0)
            {
                Console.Error.WriteLine(
                    "FAIL: ha escrito un .txt de un PDF sin texto.");
                return 1;
            }

            if (File.Exists(destino))
            {
                Console.Error.WriteLine(
                    "FAIL: ha dejado un archivo vacio en disco.");
                return 1;
            }

            Console.WriteLine(
                "Escaneo sin OCR: no se escribe nada, como debe ser.");
            return 0;
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
                        20F);
                    for (var pagina = 1; pagina <= paginas; pagina++)
                    {
                        if (pagina > 1)
                        {
                            document.NewPage();
                        }

                        for (var linea = 0; linea < 12; linea++)
                        {
                            document.Add(
                                new Paragraph(
                                    "Hoja " +
                                    pagina.ToString(
                                        CultureInfo.InvariantCulture) +
                                    " con acentos: memoria descriptiva",
                                    fuente));
                        }
                    }

                    document.Close();
                    writer.Close();
                }
            }
        }

        private static void CrearSoloImagen(string path)
        {
            using (var bitmap = new Bitmap(
                600,
                850,
                System.Drawing.Imaging.PixelFormat.Format24bppRgb))
            {
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.White);
                    using (var lapiz = new Pen(Color.Black, 4F))
                    {
                        graphics.DrawRectangle(lapiz, 50, 50, 500, 750);
                    }
                }

                using (var document = new Document(PageSize.A4, 0F, 0F, 0F, 0F))
                {
                    using (var stream = new FileStream(
                        path,
                        FileMode.Create,
                        FileAccess.Write))
                    {
                        var writer = PdfWriter.GetInstance(document, stream);
                        document.Open();
                        var imagen = iTextSharp.text.Image.GetInstance(
                            bitmap,
                            System.Drawing.Imaging.ImageFormat.Png);
                        imagen.SetAbsolutePosition(0F, 0F);
                        imagen.ScaleAbsolute(
                            PageSize.A4.Width,
                            PageSize.A4.Height);
                        document.Add(imagen);
                        document.Close();
                        writer.Close();
                    }
                }
            }
        }
    }
}

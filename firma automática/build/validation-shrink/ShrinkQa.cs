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

namespace ShrinkQa
{
    /// <summary>
    /// Reducir el tamaño de un PDF con planos escaneados.
    ///
    /// El caso real es el de siempre: un plano escaneado a mucha resolucion
    /// que no cabe en un correo. Se comprueba que baja de verdad, que no se
    /// pierden paginas, que lo que ya esta apretado no se estropea y que el
    /// original no se toca.
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
                        "qa-reducir-pdf");
                if (Directory.Exists(directorio))
                {
                    Directory.Delete(directorio, true);
                }

                Directory.CreateDirectory(directorio);

                var fallos = 0;
                fallos += ComprobarEscaneoGrande(directorio);
                fallos += ComprobarDocumentoDeTexto(directorio);
                fallos += ComprobarNoSobrescribeElOriginal(directorio);

                if (fallos == 0)
                {
                    Console.WriteLine("PASS: reducir el tamaño funciona.");
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

        private static int ComprobarEscaneoGrande(string directorio)
        {
            var origen = IoPath.Combine(
                directorio,
                "plano escaneado 300 ppp.pdf");
            CrearEscaneo(origen, 2, 2480, 3508);
            var huella = Hash(origen);
            var destino = IoPath.Combine(directorio, "plano reducido.pdf");

            var ajustes = new PdfShrinkSettings
            {
                TargetImageDpi = 150,
                JpegQuality = 75
            };
            var resultado = PdfShrinkService.Shrink(
                origen,
                destino,
                ajustes,
                null,
                CancellationToken.None);

            Console.WriteLine(
                "Escaneo: " + Mb(resultado.OriginalBytes) + " -> " +
                Mb(resultado.ResultBytes) + "  (" +
                resultado.SavedPercent.ToString(CultureInfo.InvariantCulture) +
                "% menos, " +
                resultado.RecompressedImages.ToString(
                    CultureInfo.InvariantCulture) + " de " +
                resultado.TotalImages.ToString(CultureInfo.InvariantCulture) +
                " imagenes rehechas)");

            var fallos = 0;
            if (!resultado.GotSmaller)
            {
                Console.Error.WriteLine(
                    "FAIL: el escaneo no se ha reducido.");
                return 1;
            }

            if (resultado.RecompressedImages < 1)
            {
                Console.Error.WriteLine(
                    "FAIL: no se rehizo ninguna imagen.");
                fallos++;
            }

            // Un escaneo a 300 ppp bajado a 150 tiene que quedarse holgado por
            // debajo de la mitad; si no, algo no se esta tocando.
            if (resultado.SavedPercent < 50)
            {
                Console.Error.WriteLine(
                    "FAIL: solo se ha quitado el " +
                    resultado.SavedPercent.ToString(
                        CultureInfo.InvariantCulture) + "%.");
                fallos++;
            }

            using (var reader = new PdfReader(resultado.OutputPath))
            {
                if (reader.NumberOfPages != 2)
                {
                    Console.Error.WriteLine(
                        "FAIL: la copia reducida ha perdido paginas.");
                    fallos++;
                }
            }

            // Las imagenes tienen que haber bajado de verdad de resolucion.
            var ladoMaximo = MayorLadoDeImagen(resultado.OutputPath);
            Console.WriteLine(
                "Lado mayor de imagen tras reducir: " +
                ladoMaximo.ToString(CultureInfo.InvariantCulture) + " px");
            if (ladoMaximo >= 3508)
            {
                Console.Error.WriteLine(
                    "FAIL: las imagenes conservan su resolucion.");
                fallos++;
            }

            if (!string.Equals(Hash(origen), huella, StringComparison.Ordinal))
            {
                Console.Error.WriteLine("FAIL: el original ha cambiado.");
                fallos++;
            }

            return fallos;
        }

        private static int ComprobarDocumentoDeTexto(string directorio)
        {
            // Un PDF de solo texto no tiene nada que quitar. Lo que no puede
            // pasar es que salga algo mas grande que el original.
            var origen = IoPath.Combine(directorio, "memoria solo texto.pdf");
            CrearTexto(origen, 6);
            var destino = IoPath.Combine(directorio, "memoria reducida.pdf");
            var resultado = PdfShrinkService.Shrink(
                origen,
                destino,
                new PdfShrinkSettings(),
                null,
                CancellationToken.None);

            Console.WriteLine(
                "Solo texto: " + Mb(resultado.OriginalBytes) + " -> " +
                (resultado.GotSmaller
                    ? Mb(resultado.ResultBytes) + " (" +
                        resultado.SavedPercent.ToString(
                            CultureInfo.InvariantCulture) + "% menos)"
                    : "no se puede reducir mas"));

            if (!resultado.GotSmaller)
            {
                // Correcto: se avisa y no se deja nada en disco.
                if (File.Exists(destino))
                {
                    Console.Error.WriteLine(
                        "FAIL: ha dejado una copia que no reduce nada.");
                    return 1;
                }

                return 0;
            }

            if (resultado.ResultBytes >= resultado.OriginalBytes)
            {
                Console.Error.WriteLine(
                    "FAIL: la copia es mas grande que el original.");
                return 1;
            }

            return 0;
        }

        private static int ComprobarNoSobrescribeElOriginal(string directorio)
        {
            var origen = IoPath.Combine(directorio, "no tocar.pdf");
            CrearTexto(origen, 2);
            try
            {
                PdfShrinkService.Shrink(
                    origen,
                    origen,
                    new PdfShrinkSettings(),
                    null,
                    CancellationToken.None);
            }
            catch (IOException)
            {
                Console.WriteLine(
                    "Sobrescribir el original: rechazado, como debe ser.");
                return 0;
            }

            Console.Error.WriteLine(
                "FAIL: ha aceptado sobrescribir el original.");
            return 1;
        }

        private static int MayorLadoDeImagen(string path)
        {
            var mayor = 0;
            using (var reader = new PdfReader(path))
            {
                for (var numero = 1; numero < reader.XrefSize; numero++)
                {
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

                    var ancho = stream.GetAsNumber(PdfName.WIDTH);
                    var alto = stream.GetAsNumber(PdfName.HEIGHT);
                    if (ancho != null)
                    {
                        mayor = Math.Max(mayor, ancho.IntValue);
                    }

                    if (alto != null)
                    {
                        mayor = Math.Max(mayor, alto.IntValue);
                    }
                }
            }

            return mayor;
        }

        /// <summary>
        /// Un escaneo creible: ruido suave, que ni se comprime a nada ni es
        /// tan aleatorio que el JPEG no pueda con el.
        /// </summary>
        private static void CrearEscaneo(
            string path,
            int paginas,
            int ancho,
            int alto)
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
                    using (var bitmap = CrearBitmapEscaneado(ancho, alto))
                    {
                        for (var pagina = 1; pagina <= paginas; pagina++)
                        {
                            if (pagina > 1)
                            {
                                document.NewPage();
                            }

                            var imagen = iTextSharp.text.Image.GetInstance(
                                bitmap,
                                System.Drawing.Imaging.ImageFormat.Png);
                            imagen.SetAbsolutePosition(0F, 0F);
                            imagen.ScaleAbsolute(
                                PageSize.A4.Width,
                                PageSize.A4.Height);
                            document.Add(imagen);
                        }
                    }

                    document.Close();
                    writer.Close();
                }
            }
        }

        private static Bitmap CrearBitmapEscaneado(int ancho, int alto)
        {
            // 24 bits sin canal alfa, como cualquier escaner. Con 32 bits
            // iText añade una mascara de transparencia y el servicio deja la
            // imagen en paz, con razon: al pasarla a JPEG se perderia.
            var bitmap = new Bitmap(
                ancho,
                alto,
                System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            var azar = new Random(20260922);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                // Papel: claro y casi uniforme, como sale de un escaner.
                graphics.Clear(Color.FromArgb(248, 246, 241));

                // Trazado del plano: lineas rectas negras.
                using (var lapiz = new Pen(Color.FromArgb(35, 35, 35), 3F))
                {
                    for (var i = 0; i < 26; i++)
                    {
                        var x = azar.Next(ancho);
                        var y = azar.Next(alto);
                        graphics.DrawRectangle(
                            lapiz,
                            x,
                            y,
                            azar.Next(120, 900),
                            azar.Next(90, 700));
                    }
                }

                // Rotulacion y cajetin: renglones cortos de texto.
                using (var brocha = new SolidBrush(Color.FromArgb(45, 45, 45)))
                {
                    for (var i = 0; i < 700; i++)
                    {
                        graphics.FillRectangle(
                            brocha,
                            azar.Next(ancho - 200),
                            azar.Next(alto - 20),
                            azar.Next(40, 190),
                            azar.Next(7, 13));
                    }
                }

                // Motas sueltas de suciedad del cristal.
                using (var mota = new SolidBrush(
                    Color.FromArgb(70, 120, 115, 105)))
                {
                    for (var i = 0; i < 900; i++)
                    {
                        graphics.FillEllipse(
                            mota,
                            azar.Next(ancho),
                            azar.Next(alto),
                            azar.Next(2, 6),
                            azar.Next(2, 6));
                    }
                }
            }

            // Grano del escaner: variacion suave pixel a pixel por toda la
            // hoja. Es lo que hace que un escaneo pese megas —Flate no puede
            // con el ruido— y a la vez lo que un JPEG absorbe sin problema.
            // Sin esto el fixture se comprime a nada y no se parece a lo que
            // llega de un escaner de verdad.
            AplicarGrano(bitmap, azar, 11);

            return bitmap;
        }

        private static void AplicarGrano(
            Bitmap bitmap,
            Random azar,
            int amplitud)
        {
            var area = new System.Drawing.Rectangle(
                0,
                0,
                bitmap.Width,
                bitmap.Height);
            var datos = bitmap.LockBits(
                area,
                System.Drawing.Imaging.ImageLockMode.ReadWrite,
                System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            try
            {
                var bytes = Math.Abs(datos.Stride) * datos.Height;
                var buffer = new byte[bytes];
                System.Runtime.InteropServices.Marshal.Copy(
                    datos.Scan0,
                    buffer,
                    0,
                    bytes);
                for (var i = 0; i < buffer.Length; i++)
                {
                    var valor = buffer[i] + azar.Next(-amplitud, amplitud + 1);
                    buffer[i] = (byte)Math.Max(0, Math.Min(255, valor));
                }

                System.Runtime.InteropServices.Marshal.Copy(
                    buffer,
                    0,
                    datos.Scan0,
                    bytes);
            }
            finally
            {
                bitmap.UnlockBits(datos);
            }
        }

        private static void CrearTexto(string path, int paginas)
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
                        11F);
                    for (var pagina = 1; pagina <= paginas; pagina++)
                    {
                        if (pagina > 1)
                        {
                            document.NewPage();
                        }

                        for (var linea = 0; linea < 40; linea++)
                        {
                            document.Add(
                                new Paragraph(
                                    "Memoria descriptiva, hoja " +
                                    pagina.ToString(
                                        CultureInfo.InvariantCulture) +
                                    ", renglon " +
                                    linea.ToString(
                                        CultureInfo.InvariantCulture) + ".",
                                    fuente));
                        }
                    }

                    document.Close();
                    writer.Close();
                }
            }
        }

        private static string Mb(long bytes)
        {
            return (bytes / 1024D / 1024D).ToString(
                "0.00",
                CultureInfo.InvariantCulture) + " MB";
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

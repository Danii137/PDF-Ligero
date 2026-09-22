using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using FirmaAutomatica;
using iTextSharp.text;
using iTextSharp.text.pdf;
using IoPath = System.IO.Path;

namespace AttachmentsQa
{
    /// <summary>
    /// Archivos adjuntos de un PDF: verlos, sacarlos y añadirlos.
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
                        "qa-adjuntos-pdf");
                if (Directory.Exists(directorio))
                {
                    Directory.Delete(directorio, true);
                }

                Directory.CreateDirectory(directorio);

                var fallos = 0;
                fallos += ComprobarSinAdjuntos(directorio);
                fallos += ComprobarCicloCompleto(directorio);

                if (fallos == 0)
                {
                    Console.WriteLine("PASS: los adjuntos funcionan.");
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

        private static int ComprobarSinAdjuntos(string directorio)
        {
            var pdf = IoPath.Combine(directorio, "sin adjuntos.pdf");
            CrearPdf(pdf, 2);
            var lista = PdfAttachmentService.List(pdf);
            if (lista.Count != 0)
            {
                Console.Error.WriteLine(
                    "FAIL: se inventa adjuntos donde no los hay.");
                return 1;
            }

            Console.WriteLine("Sin adjuntos: la lista sale vacia.");
            return 0;
        }

        private static int ComprobarCicloCompleto(string directorio)
        {
            var pdf = IoPath.Combine(directorio, "proyecto.pdf");
            CrearPdf(pdf, 3);
            var huella = Hash(pdf);

            // Dos archivos, uno con tilde en el nombre y contenido binario,
            // que es como llegan los DWG.
            var texto = IoPath.Combine(directorio, "mediciones.csv");
            File.WriteAllText(
                texto,
                "partida;medición;importe\r\nCimentación;12,40;1.240,00\r\n",
                new UTF8Encoding(true));

            var binario = IoPath.Combine(directorio, "planta baja.dwg");
            var datos = new byte[4096];
            new Random(7).NextBytes(datos);
            File.WriteAllBytes(binario, datos);

            var conAdjuntos = IoPath.Combine(directorio, "con adjuntos.pdf");
            PdfAttachmentService.Add(
                pdf,
                conAdjuntos,
                new List<string> { texto, binario },
                CancellationToken.None);

            var fallos = 0;
            var lista = PdfAttachmentService.List(conAdjuntos);
            Console.WriteLine(
                "Adjuntados: " +
                lista.Count.ToString(CultureInfo.InvariantCulture) +
                " archivos.");

            if (lista.Count != 2)
            {
                Console.Error.WriteLine(
                    "FAIL: se esperaban 2 adjuntos.");
                return 1;
            }

            var nombres = new List<string>();
            foreach (var adjunto in lista)
            {
                nombres.Add(adjunto.Name);
            }

            if (!nombres.Contains("mediciones.csv") ||
                !nombres.Contains("planta baja.dwg"))
            {
                Console.Error.WriteLine(
                    "FAIL: los nombres no cuadran: " +
                    string.Join(", ", nombres.ToArray()));
                fallos++;
            }

            // Sacarlos y comparar byte a byte: si el contenido no vuelve
            // igual, el adjunto no sirve para nada.
            var sacado = IoPath.Combine(directorio, "sacado.dwg");
            if (!PdfAttachmentService.Extract(
                    conAdjuntos,
                    "planta baja.dwg",
                    sacado))
            {
                Console.Error.WriteLine("FAIL: no se pudo sacar el DWG.");
                return fallos + 1;
            }

            if (!string.Equals(
                    Hash(binario),
                    Hash(sacado),
                    StringComparison.Ordinal))
            {
                Console.Error.WriteLine(
                    "FAIL: el archivo sacado no es igual al original.");
                fallos++;
            }
            else
            {
                Console.WriteLine(
                    "Sacado y comparado: identico por SHA-256.");
            }

            var sacadoTexto = IoPath.Combine(directorio, "sacado.csv");
            if (PdfAttachmentService.Extract(
                    conAdjuntos,
                    "mediciones.csv",
                    sacadoTexto))
            {
                var recuperado = File.ReadAllText(
                    sacadoTexto,
                    Encoding.UTF8);
                if (recuperado.IndexOf(
                        "Cimentación",
                        StringComparison.Ordinal) < 0)
                {
                    Console.Error.WriteLine(
                        "FAIL: el CSV pierde los acentos al volver.");
                    fallos++;
                }
            }
            else
            {
                Console.Error.WriteLine("FAIL: no se pudo sacar el CSV.");
                fallos++;
            }

            // Pedir uno que no esta no puede reventar.
            var inexistente = IoPath.Combine(directorio, "nada.bin");
            if (PdfAttachmentService.Extract(
                    conAdjuntos,
                    "no existe.dwg",
                    inexistente))
            {
                Console.Error.WriteLine(
                    "FAIL: dice haber sacado un adjunto inexistente.");
                fallos++;
            }

            using (var reader = new PdfReader(conAdjuntos))
            {
                if (reader.NumberOfPages != 3)
                {
                    Console.Error.WriteLine(
                        "FAIL: se han perdido paginas al adjuntar.");
                    fallos++;
                }
            }

            if (!string.Equals(Hash(pdf), huella, StringComparison.Ordinal))
            {
                Console.Error.WriteLine("FAIL: el original ha cambiado.");
                fallos++;
            }

            return fallos;
        }

        private static void CrearPdf(string path, int paginas)
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
                        14F);
                    for (var pagina = 1; pagina <= paginas; pagina++)
                    {
                        if (pagina > 1)
                        {
                            document.NewPage();
                        }

                        document.Add(
                            new Paragraph(
                                "Hoja " +
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

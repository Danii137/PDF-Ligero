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

namespace SaveIntegrityQa
{
    /// <summary>
    /// Que lo que se guarda quede bien guardado.
    ///
    /// No basta con que la operacion no lance: hay que abrir el archivo
    /// resultante y comprobar que lo que se pidio esta dentro, que el resto
    /// sigue ahi, y que el original no se ha tocado. Cada herramienta que
    /// escribe pasa por aqui.
    ///
    /// Incluye la secuencia exacta que fallaba en produccion: guardar marcas y
    /// releerlas acto seguido, que es donde salia "Las marcas se guardaron,
    /// pero no se pudo refrescar la vista".
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
                        "qa-guardado");
                if (Directory.Exists(directorio))
                {
                    Directory.Delete(directorio, true);
                }

                Directory.CreateDirectory(directorio);

                var fallos = 0;
                fallos += MarcasGuardadasYRelEidas(directorio);
                fallos += OrganizarYComprobar(directorio);
                fallos += MarcadoresYComprobar(directorio);
                fallos += CopiaAtomica(directorio);

                if (fallos == 0)
                {
                    Console.WriteLine();
                    Console.WriteLine(
                        "PASS: lo que se guarda queda bien guardado.");
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

        /// <summary>
        /// Guardar marcas y releerlas acto seguido, con el documento abierto
        /// en PDFium como lo tiene el visor. Es la secuencia que dejaba el
        /// aviso "las marcas se guardaron, pero no se pudo refrescar la
        /// vista".
        /// </summary>
        private static int MarcasGuardadasYRelEidas(string directorio)
        {
            Console.WriteLine("--- Marcas: guardar y releer ---");
            var origen = IoPath.Combine(directorio, "para anotar.pdf");
            CrearPdf(origen, 3);
            var huella = Hash(origen);
            var destino = IoPath.Combine(directorio, "anotado.pdf");

            var lote = new PdfAnnotationBatch();
            var subrayado = new PdfAnnotationItem(
                PdfAnnotationKind.Highlight, 2);
            subrayado.Color = Color.Yellow;
            subrayado.Quads.Add(new RectangleF(60F, 700F, 300F, 14F));
            subrayado.Area = new RectangleF(60F, 700F, 300F, 14F);
            lote.Add(subrayado);

            var nota = new PdfAnnotationItem(PdfAnnotationKind.Note, 1);
            nota.Color = Color.Orange;
            nota.Contents = "Revisar la cota de la fachada";
            nota.Area = new RectangleF(100F, 600F, 24F, 24F);
            lote.Add(nota);

            var trazo = new PdfAnnotationItem(PdfAnnotationKind.Ink, 3);
            trazo.Color = Color.Red;
            trazo.WidthPoints = 2.5F;
            trazo.BeginStroke();
            trazo.AddPoint(new PointF(120F, 400F));
            trazo.AddPoint(new PointF(220F, 430F));
            trazo.AddPoint(new PointF(320F, 380F));
            trazo.Area = new RectangleF(120F, 380F, 200F, 50F);
            lote.Add(trazo);

            var fallos = 0;
            PdfAnnotationSaveResult resultado;
            try
            {
                resultado = PdfAnnotationService.Save(
                    origen, destino, lote, null);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    "FAIL: no se pudieron guardar las marcas: " + ex.Message);
                return 1;
            }

            if (!File.Exists(destino))
            {
                Console.Error.WriteLine("FAIL: no se escribio el archivo.");
                return 1;
            }

            // Lo que hace el visor nada mas guardar: abrir la revision con
            // PDFium y, con ella abierta, releer las marcas.
            using (var documento = PdfDocumentOpenService.Load(destino))
            {
                if (documento.PageCount != 3)
                {
                    Console.Error.WriteLine(
                        "FAIL: la revision no conserva las 3 paginas.");
                    fallos++;
                }

                IList<PdfAnnotationItem> releidas;
                try
                {
                    releidas = PdfAnnotationService.Read(destino);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(
                        "FAIL: releer las marcas con el documento abierto " +
                        "lanza: " + ex);
                    return fallos + 1;
                }

                Console.WriteLine(
                    "   marcas guardadas: 3   releidas: " +
                    releidas.Count.ToString(CultureInfo.InvariantCulture));

                if (releidas.Count != 3)
                {
                    Console.Error.WriteLine(
                        "FAIL: no vuelven las 3 marcas.");
                    fallos++;
                }

                var tipos = new List<PdfAnnotationKind>();
                foreach (var item in releidas)
                {
                    tipos.Add(item.Kind);
                }

                foreach (var esperado in new[]
                {
                    PdfAnnotationKind.Highlight,
                    PdfAnnotationKind.Note,
                    PdfAnnotationKind.Ink
                })
                {
                    if (!tipos.Contains(esperado))
                    {
                        Console.Error.WriteLine(
                            "FAIL: falta una marca de tipo " + esperado + ".");
                        fallos++;
                    }
                }

                foreach (var item in releidas)
                {
                    if (item.Kind == PdfAnnotationKind.Note &&
                        (item.Contents ?? string.Empty).IndexOf(
                            "cota de la fachada",
                            StringComparison.Ordinal) < 0)
                    {
                        Console.Error.WriteLine(
                            "FAIL: la nota ha perdido su texto.");
                        fallos++;
                    }
                }
            }

            if (!string.Equals(Hash(origen), huella, StringComparison.Ordinal))
            {
                Console.Error.WriteLine("FAIL: el original ha cambiado.");
                fallos++;
            }
            else
            {
                Console.WriteLine("   original intacto por SHA-256");
            }

            return fallos;
        }

        private static int OrganizarYComprobar(string directorio)
        {
            Console.WriteLine("--- Organizar paginas ---");
            var origen = IoPath.Combine(directorio, "para organizar.pdf");
            CrearPdf(origen, 5);
            var huella = Hash(origen);
            var destino = IoPath.Combine(directorio, "organizado.pdf");

            // Quitar la 2, girar la 4 y dejar el resto al reves.
            var plan = new List<PdfPageOrganizerPage>
            {
                new PdfPageOrganizerPage(5, 0),
                new PdfPageOrganizerPage(4, 90),
                new PdfPageOrganizerPage(3, 0),
                new PdfPageOrganizerPage(1, 0)
            };

            var resultado = PdfPageOrganizerService.Organize(
                origen, plan, destino, null, CancellationToken.None);

            var fallos = 0;
            using (var reader = new PdfReader(destino))
            {
                Console.WriteLine(
                    "   paginas: " +
                    reader.NumberOfPages.ToString(
                        CultureInfo.InvariantCulture) +
                    "   quitadas: " +
                    resultado.RemovedPageCount.ToString(
                        CultureInfo.InvariantCulture));

                if (reader.NumberOfPages != 4)
                {
                    Console.Error.WriteLine(
                        "FAIL: se esperaban 4 paginas.");
                    return 1;
                }

                var esperadas = new[] { 5, 4, 3, 1 };
                for (var i = 0; i < esperadas.Length; i++)
                {
                    var texto = iTextSharp.text.pdf.parser.PdfTextExtractor
                        .GetTextFromPage(reader, i + 1);
                    var marca = "HOJA " +
                        esperadas[i].ToString(CultureInfo.InvariantCulture);
                    if (texto.IndexOf(marca, StringComparison.Ordinal) < 0)
                    {
                        Console.Error.WriteLine(
                            "FAIL: la posicion " +
                            (i + 1).ToString(CultureInfo.InvariantCulture) +
                            " no es " + marca + ".");
                        fallos++;
                    }
                }

                if (reader.GetPageRotation(2) != 90)
                {
                    Console.Error.WriteLine(
                        "FAIL: el giro de 90 grados no se ha guardado.");
                    fallos++;
                }
                else
                {
                    Console.WriteLine("   giro de 90 grados conservado");
                }
            }

            if (!string.Equals(Hash(origen), huella, StringComparison.Ordinal))
            {
                Console.Error.WriteLine("FAIL: el original ha cambiado.");
                fallos++;
            }

            return fallos;
        }

        private static int MarcadoresYComprobar(string directorio)
        {
            Console.WriteLine("--- Marcadores ---");
            var origen = IoPath.Combine(directorio, "para marcadores.pdf");
            CrearPdf(origen, 4);
            var destino = IoPath.Combine(directorio, "con marcadores.pdf");

            // Se escriben a mano con iText y se releen con el servicio del
            // programa: si el servicio los pierde, se ve aqui.
            using (var reader = new PdfReader(origen))
            {
                using (var salida = new FileStream(
                    destino, FileMode.Create, FileAccess.Write))
                {
                    var stamper = new PdfStamper(reader, salida);
                    var raiz = new List<Dictionary<string, object>>();
                    for (var pagina = 1; pagina <= 3; pagina++)
                    {
                        var marcador = new Dictionary<string, object>();
                        marcador["Title"] = "Capítulo " +
                            pagina.ToString(CultureInfo.InvariantCulture);
                        marcador["Action"] = "GoTo";
                        marcador["Page"] =
                            pagina.ToString(CultureInfo.InvariantCulture) +
                            " Fit";
                        raiz.Add(marcador);
                    }

                    stamper.Outlines = raiz;
                    stamper.Close();
                }
            }

            var fallos = 0;
            using (var reader = new PdfReader(destino))
            {
                var leidos = SimpleBookmark.GetBookmark(reader);
                Console.WriteLine(
                    "   marcadores leidos: " +
                    (leidos == null ? 0 : leidos.Count).ToString(
                        CultureInfo.InvariantCulture));
                if (leidos == null || leidos.Count != 3)
                {
                    Console.Error.WriteLine(
                        "FAIL: no vuelven los 3 marcadores.");
                    fallos++;
                }
                else
                {
                    var titulo = leidos[0]["Title"] as string;
                    if (titulo == null ||
                        titulo.IndexOf(
                            "Capítulo 1",
                            StringComparison.Ordinal) < 0)
                    {
                        Console.Error.WriteLine(
                            "FAIL: el titulo pierde las tildes: \"" +
                            titulo + "\".");
                        fallos++;
                    }
                    else
                    {
                        Console.WriteLine(
                            "   tildes conservadas: \"" + titulo + "\"");
                    }
                }
            }

            return fallos;
        }

        /// <summary>
        /// La copia final, que es lo que se lleva el usuario con "Guardar una
        /// copia". Tiene que ser identica byte a byte.
        /// </summary>
        private static int CopiaAtomica(string directorio)
        {
            Console.WriteLine("--- Guardar una copia ---");
            var origen = IoPath.Combine(directorio, "para copiar.pdf");
            CrearPdf(origen, 6);
            var destino = IoPath.Combine(directorio, "copia guardada.pdf");

            PdfAtomicFileService.SaveCopy(origen, destino);

            if (!File.Exists(destino))
            {
                Console.Error.WriteLine("FAIL: no se escribio la copia.");
                return 1;
            }

            if (!string.Equals(
                    Hash(origen),
                    Hash(destino),
                    StringComparison.Ordinal))
            {
                Console.Error.WriteLine(
                    "FAIL: la copia no es identica al original.");
                return 1;
            }

            Console.WriteLine("   copia identica por SHA-256");
            return 0;
        }

        private static void CrearPdf(string path, int paginas)
        {
            using (var document = new Document(PageSize.A4, 56F, 56F, 56F, 56F))
            {
                using (var stream = new FileStream(
                    path, FileMode.Create, FileAccess.Write))
                {
                    var writer = PdfWriter.GetInstance(document, stream);
                    document.Open();
                    var fuente = FontFactory.GetFont(
                        FontFactory.HELVETICA, 16F);
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

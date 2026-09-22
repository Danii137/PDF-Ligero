using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using FirmaAutomatica;
using iTextSharp.text;
using iTextSharp.text.pdf;
using IoPath = System.IO.Path;

namespace TextSelectionQa
{
    /// <summary>
    /// Comprueba que se puede seleccionar el texto de una pagina y copiarlo.
    ///
    /// Monta el visor real fuera de pantalla, con su PdfRenderer, y usa el
    /// controlador tal cual lo usa la aplicacion. Lo que no cubre es el
    /// arrastre con el raton, que necesita la ventana en pantalla: para eso
    /// esta arrastrar-seleccion.ps1.
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                var directorio = args.Length > 0
                    ? IoPath.GetFullPath(args[0])
                    : IoPath.Combine(
                        IoPath.GetTempPath(),
                        "qa-seleccion-texto");
                Directory.CreateDirectory(directorio);
                var pdf = IoPath.Combine(directorio, "memoria de prueba.pdf");
                var lineas = CrearFixture(pdf);

                Application.EnableVisualStyles();
                var fallos = 0;
                using (var form = new Form())
                {
                    form.Size = new System.Drawing.Size(900, 700);
                    // Fuera de la pantalla: hace falta una ventana real para
                    // que el renderer calcule coordenadas, pero no molestar.
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new System.Drawing.Point(-3000, -3000);
                    form.ShowInTaskbar = false;

                    var viewer = new PdfiumViewer.PdfViewer
                    {
                        Dock = DockStyle.Fill,
                        ShowToolbar = false,
                        ShowBookmarks = false
                    };
                    form.Controls.Add(viewer);
                    form.Show();
                    Application.DoEvents();

                    using (var documento = PdfiumViewer.PdfDocument.Load(pdf))
                    {
                        viewer.Document = documento;
                        Application.DoEvents();

                        var mensajes = new List<string>();
                        using (var seleccion = new PdfTextSelectionController(
                            viewer.Renderer,
                            delegate { return true; },
                            delegate(int pagina)
                            {
                                return LeerLineas(pdf, pagina);
                            },
                            delegate(string mensaje)
                            {
                                mensajes.Add(mensaje);
                            }))
                        {
                            fallos += ComprobarSeleccionDePagina(
                                seleccion,
                                lineas);
                            fallos += ComprobarCopiado(seleccion, lineas);
                            fallos += ComprobarLimpiar(seleccion);
                            fallos += ComprobarSinSeleccion(seleccion);
                        }

                        viewer.Document = null;
                    }

                    form.Hide();
                }

                if (fallos == 0)
                {
                    Console.WriteLine(
                        "PASS: seleccionar y copiar texto funciona.");
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

        private static int ComprobarSeleccionDePagina(
            PdfTextSelectionController seleccion,
            IList<string> lineas)
        {
            if (!seleccion.SelectCurrentPage())
            {
                Console.Error.WriteLine(
                    "FAIL: no se pudo seleccionar la pagina.");
                return 1;
            }

            if (!seleccion.HasSelection)
            {
                Console.Error.WriteLine(
                    "FAIL: la pagina quedo sin seleccion.");
                return 1;
            }

            var texto = seleccion.SelectedText;
            Console.WriteLine(
                "Seleccion de pagina: " +
                texto.Length.ToString(CultureInfo.InvariantCulture) +
                " caracteres, " +
                texto.Split('\n').Length.ToString(
                    CultureInfo.InvariantCulture) + " renglones.");

            var fallos = 0;
            foreach (var linea in lineas)
            {
                if (texto.IndexOf(linea, StringComparison.Ordinal) < 0)
                {
                    Console.Error.WriteLine(
                        "FAIL: falta el renglon \"" + linea + "\".");
                    fallos++;
                }
            }

            // El orden importa: copiar un parrafo y que salga desordenado no
            // sirve de nada.
            var posicion = -1;
            foreach (var linea in lineas)
            {
                var indice = texto.IndexOf(linea, StringComparison.Ordinal);
                if (indice >= 0 && indice < posicion)
                {
                    Console.Error.WriteLine(
                        "FAIL: el renglon \"" + linea +
                        "\" sale fuera de orden.");
                    fallos++;
                }

                if (indice >= 0)
                {
                    posicion = indice;
                }
            }

            return fallos;
        }

        private static int ComprobarCopiado(
            PdfTextSelectionController seleccion,
            IList<string> lineas)
        {
            if (!seleccion.CopySelection())
            {
                Console.Error.WriteLine("FAIL: no se pudo copiar.");
                return 1;
            }

            // El portapapeles de Windows se sirve por OLE y necesita que el
            // hilo atienda mensajes para completar la entrega. La aplicacion
            // real tiene su bucle de mensajes; esta prueba no, asi que hay
            // que bombear a mano antes de leer. Sin esto el portapapeles sale
            // vacio aunque la copia haya ido bien.
            Application.DoEvents();

            string portapapeles;
            try
            {
                portapapeles = Clipboard.ContainsText()
                    ? Clipboard.GetText()
                    : string.Empty;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    "FAIL: no se pudo leer el portapapeles: " + ex.Message);
                return 1;
            }

            if (!string.Equals(
                    portapapeles,
                    seleccion.SelectedText,
                    StringComparison.Ordinal))
            {
                Console.Error.WriteLine(
                    "FAIL: el portapapeles no coincide con lo seleccionado.");
                Console.Error.WriteLine(
                    "   seleccionado (" +
                    seleccion.SelectedText.Length.ToString(
                        CultureInfo.InvariantCulture) + "): \"" +
                    Recortar(seleccion.SelectedText) + "\"");
                Console.Error.WriteLine(
                    "   portapapeles (" +
                    portapapeles.Length.ToString(
                        CultureInfo.InvariantCulture) + "): \"" +
                    Recortar(portapapeles) + "\"");
                return 1;
            }

            Console.WriteLine(
                "Portapapeles: " +
                portapapeles.Length.ToString(CultureInfo.InvariantCulture) +
                " caracteres, coinciden.");
            return 0;
        }

        private static int ComprobarLimpiar(
            PdfTextSelectionController seleccion)
        {
            seleccion.ClearSelection();
            if (seleccion.HasSelection)
            {
                Console.Error.WriteLine(
                    "FAIL: la seleccion no se solto.");
                return 1;
            }

            return 0;
        }

        private static int ComprobarSinSeleccion(
            PdfTextSelectionController seleccion)
        {
            // Copiar sin nada seleccionado no puede reventar ni dejar basura.
            if (seleccion.CopySelection())
            {
                Console.Error.WriteLine(
                    "FAIL: dice haber copiado sin seleccion.");
                return 1;
            }

            return 0;
        }

        private static string Recortar(string texto)
        {
            var limpio = (texto ?? string.Empty)
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
            return limpio.Length <= 90
                ? limpio
                : limpio.Substring(0, 87) + "...";
        }

        private static IList<PdfTextBlock> LeerLineas(
            string pdf,
            int pagina)
        {
            PdfReader reader = null;
            try
            {
                reader = new PdfReader(pdf);
                return PdfTextBlockLocator.Locate(reader, pagina);
            }
            finally
            {
                if (reader != null)
                {
                    reader.Close();
                }
            }
        }

        private static IList<string> CrearFixture(string path)
        {
            var lineas = new List<string>
            {
                "MEMORIA DESCRIPTIVA",
                "Referencia catastral 1234567VK4713S",
                "Superficie construida 148,35 m2",
                "Presupuesto de ejecucion material"
            };

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
                    foreach (var linea in lineas)
                    {
                        document.Add(new Paragraph(linea, fuente));
                    }

                    document.Close();
                    writer.Close();
                }
            }

            return lineas;
        }
    }
}

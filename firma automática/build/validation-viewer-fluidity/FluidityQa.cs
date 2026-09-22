using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using FirmaAutomatica;
using PdfiumViewer;
using IoPath = System.IO.Path;

namespace FluidityQa
{
    /// <summary>
    /// Mide lo que cuesta un repintado del visor real al desplazarse y al
    /// hacer zoom, con y sin la envoltura que cachea a doble resolucion.
    ///
    /// Es la prueba que justifica el cambio: sin numeros, "va mas fluido" es
    /// una opinion.
    /// </summary>
    internal static class Program
    {
        /// <summary>Con /diag se imprime que decide cada peticion.</summary>
        public static bool Diagnostico;

        private static int Main(string[] args)
        {
            try
            {
                var reales = new List<string>();
                foreach (var a in args)
                {
                    if (string.Equals(a, "/diag", StringComparison.OrdinalIgnoreCase))
                    {
                        Diagnostico = true;
                    }
                    else
                    {
                        reales.Add(a);
                    }
                }

                var path = reales.Count > 0
                    ? IoPath.GetFullPath(reales[0])
                    : null;
                var propio = false;
                if (path == null || !File.Exists(path))
                {
                    path = IoPath.Combine(
                        IoPath.GetTempPath(),
                        "qa-fluidez-planos.pdf");
                    CrearFixture(path, 8);
                    propio = true;
                }

                Console.WriteLine(
                    "Documento: " + IoPath.GetFileName(path) +
                    (propio ? "   (fixture propio)" : "   (documento real)"));
                Console.WriteLine();

                Application.EnableVisualStyles();
                var sin = Medir(path, false);
                var con = Medir(path, true);

                Console.WriteLine();
                Console.WriteLine(
                    "                            SIN cache    CON cache");
                var fallos = 0;
                // Lo que se exige de cada cosa:
                //
                // Pasar de pagina tiene que ir MUCHO mejor: es donde entra la
                // preparacion de las hojas de al lado, y es lo que mas se usa
                // con un juego de planos.
                //
                // Del zoom no se promete velocidad: a mucho aumento, el coste
                // es montar una imagen de varios megapixeles y eso no se
                // quita. Lo que se exige es no empeorar. Lo que arregla el
                // zoom es el anclaje, que se comprueba aparte.
                fallos += Comparar("Pasar de pagina", sin.Pagina, con.Pagina, 3D);
                fallos += Comparar("Acercar", sin.Acercar, con.Acercar, 0.85D);
                fallos += Comparar("Alejar", sin.Alejar, con.Alejar, 0.85D);
                fallos += ComprobarAnclaje(path);
                Console.WriteLine();
                Console.WriteLine(
                    "Llamadas reales a PDFium con cache: " +
                    con.Rasterizados.ToString(CultureInfo.InvariantCulture) +
                    "   servidas escalando: " +
                    con.Escalados.ToString(CultureInfo.InvariantCulture));

                if (con.Escalados < 1)
                {
                    Console.Error.WriteLine(
                        "FAIL: no se ha servido ni una sola vista desde el " +
                        "maestro; la cache no esta entrando.");
                    fallos++;
                }

                if (fallos == 0)
                {
                    Console.WriteLine();
                    Console.WriteLine(
                        "PASS: el visor va mas suelto con la cache.");
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
        /// Al hacer zoom hay que quedarse en la misma hoja. Sin esto, acercar
        /// cuatro veces desde la hoja 12 de un juego de planos te dejaba en la
        /// 25: es lo que hacia el zoom erratico, y ademas lento, porque cada
        /// paso aterrizaba en una hoja sin rasterizar.
        /// </summary>
        private static int ComprobarAnclaje(string path)
        {
            using (var form = new Form())
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(40, 40);
                form.Size = new Size(1180, 860);
                form.ShowInTaskbar = false;
                var renderer = new PdfRenderer { Dock = DockStyle.Fill };
                form.Controls.Add(renderer);
                form.Show();
                form.Activate();
                Application.DoEvents();
                System.Threading.Thread.Sleep(300);

                var fallos = 0;
                using (var real = PdfDocument.Load(path))
                {
                    renderer.Load(real);
                    renderer.ZoomMode = PdfViewerZoomMode.FitWidth;
                    renderer.Page = Math.Min(11, real.PageCount - 1);
                    renderer.Refresh();
                    Application.DoEvents();
                    var partida = renderer.Page;

                    // Como estaba antes: cuatro pasos sin anclar.
                    for (var i = 0; i < 4; i++)
                    {
                        renderer.ZoomIn();
                        renderer.Refresh();
                        Application.DoEvents();
                    }

                    var sinAnclar = renderer.Page;

                    renderer.ZoomMode = PdfViewerZoomMode.FitWidth;
                    renderer.Page = partida;
                    renderer.Refresh();
                    Application.DoEvents();

                    for (var i = 0; i < 4; i++)
                    {
                        PdfZoomAnchor.Step(
                            renderer,
                            PdfZoomAnchor.CenterOf(renderer),
                            true);
                        renderer.Refresh();
                        Application.DoEvents();
                    }

                    var anclado = renderer.Page;

                    Console.WriteLine();
                    Console.WriteLine(
                        "  Anclaje del zoom: partiendo de la hoja " +
                        (partida + 1).ToString(CultureInfo.InvariantCulture) +
                        " y acercando 4 pasos ->  sin anclar: hoja " +
                        (sinAnclar + 1).ToString(CultureInfo.InvariantCulture) +
                        "   anclado: hoja " +
                        (anclado + 1).ToString(CultureInfo.InvariantCulture));

                    if (anclado != partida)
                    {
                        Console.Error.WriteLine(
                            "     FAIL: el zoom anclado tambien se ha ido de " +
                            "la hoja.");
                        fallos++;
                    }
                }

                form.Hide();
                return fallos;
            }
        }

        private static int Comparar(
            string titulo,
            double sin,
            double con,
            double mejoraMinima)
        {
            var veces = con <= 0.01D ? 999D : sin / con;
            Console.WriteLine(
                "  {0,-24} {1,7:0} ms   {2,7:0} ms    x{3:0.0} mas rapido",
                titulo,
                sin,
                con,
                veces);

            if (veces < mejoraMinima)
            {
                Console.Error.WriteLine(
                    "     FAIL: se esperaba al menos x" +
                    mejoraMinima.ToString("0", CultureInfo.InvariantCulture) +
                    " en " + titulo + ".");
                return 1;
            }

            return 0;
        }

        private static Resultado Medir(string path, bool conCache)
        {
            var resultado = new Resultado();
            using (var form = new Form())
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(40, 40);
                form.Size = new Size(1180, 860);
                form.ShowInTaskbar = false;
                var renderer = new PdfRenderer { Dock = DockStyle.Fill };
                form.Controls.Add(renderer);
                form.Show();
                form.Activate();
                Application.DoEvents();
                System.Threading.Thread.Sleep(400);
                Application.DoEvents();

                using (var real = PdfDocument.Load(path))
                {
                    PdfFluidDocument envoltura = null;
                    IPdfDocument aUsar = real;
                    if (conCache)
                    {
                        envoltura = new PdfFluidDocument(
                            real,
                            delegate(int pagina)
                            {
                                // Igual que en el visor: se olvida la copia
                                // guardada y se repinta, ya con la buena.
                                if (renderer.IsDisposed)
                                {
                                    return;
                                }

                                try
                                {
                                    renderer.BeginInvoke(
                                        new Action(delegate
                                        {
                                            PdfRendererCacheAccess
                                                .ForgetPageImage(
                                                    renderer, pagina);
                                            renderer.Invalidate();
                                        }));
                                }
                                catch (Exception)
                                {
                                }
                            });
                        envoltura.Trace = delegate(string linea)
                        {
                            if (Diagnostico)
                            {
                                Console.WriteLine("        " + linea);
                            }
                        };
                        aUsar = envoltura;
                    }

                    try
                    {
                        renderer.Load(aUsar);
                        renderer.ZoomMode = PdfViewerZoomMode.FitWidth;
                        renderer.Refresh();
                        Application.DoEvents();

                        // Se calienta: en el uso real el documento ya lleva
                        // un rato abierto cuando se empieza a navegar.
                        for (var i = 0; i < 3; i++)
                        {
                            renderer.Page = Math.Min(
                                real.PageCount - 1,
                                renderer.Page + 1);
                            renderer.Refresh();
                            Application.DoEvents();
                        }

                        System.Threading.Thread.Sleep(500);
                        Application.DoEvents();

                        // Leer una hoja y pasar a la siguiente: lo que se
                        // hace de verdad con un juego de planos.
                        resultado.Pagina = MedianaDe(
                            8,
                            renderer,
                            delegate
                            {
                                renderer.Page =
                                    (renderer.Page + 1) % real.PageCount;
                            },
                            1200);

                        Fase(envoltura, "tras pasar paginas");
                        resultado.Acercar = MedianaDe(
                            6,
                            renderer,
                            delegate
                            {
                                PdfZoomAnchor.Step(
                                    renderer,
                                    PdfZoomAnchor.CenterOf(renderer),
                                    true);
                            });
                        Fase(envoltura, "tras acercar");

                        resultado.Alejar = MedianaDe(
                            6,
                            renderer,
                            delegate
                            {
                                PdfZoomAnchor.Step(
                                    renderer,
                                    PdfZoomAnchor.CenterOf(renderer),
                                    false);
                            });
                        Fase(envoltura, "tras alejar");

                        if (envoltura != null)
                        {
                            resultado.Rasterizados = envoltura.RasterizedCount;
                            resultado.Escalados =
                                envoltura.ScaledCount + envoltura.ExactCount;
                        }
                    }
                    finally
                    {
                        if (envoltura != null)
                        {
                            envoltura.Dispose();
                        }
                    }
                }

                form.Hide();
            }

            Console.WriteLine(
                (conCache ? "CON cache:  " : "SIN cache:  ") +
                "pagina " + resultado.Pagina.ToString(
                    "0", CultureInfo.InvariantCulture) +
                " ms   acercar " + resultado.Acercar.ToString(
                    "0", CultureInfo.InvariantCulture) +
                " ms   alejar " + resultado.Alejar.ToString(
                    "0", CultureInfo.InvariantCulture) + " ms");
            return resultado;
        }

        private static int ultimoRaster;
        private static int ultimoEscalado;
        private static int ultimoExacto;

        private static void Fase(PdfFluidDocument envoltura, string titulo)
        {
            if (envoltura == null)
            {
                return;
            }

            Console.WriteLine(
                "      [" + titulo + "] rasterizados +" +
                (envoltura.RasterizedCount - ultimoRaster) +
                "   escalados +" +
                (envoltura.ScaledCount - ultimoEscalado) +
                "   exactos +" +
                (envoltura.ExactCount - ultimoExacto) +
                "   en cache " + envoltura.CachedPageCount);
            ultimoRaster = envoltura.RasterizedCount;
            ultimoEscalado = envoltura.ScaledCount;
            ultimoExacto = envoltura.ExactCount;
        }

        private static double MedianaDe(
            int pasos,
            PdfRenderer renderer,
            Action accion)
        {
            return MedianaDe(pasos, renderer, accion, 0);
        }

        /// <param name="pausaMs">
        /// Lo que se queda uno mirando antes de la siguiente accion. Con cero
        /// se mide el peor caso —dar a siguiente sin parar—; con algo de
        /// pausa se mide lo que pasa de verdad al leer un documento.
        /// </param>
        private static double MedianaDe(
            int pasos,
            PdfRenderer renderer,
            Action accion,
            int pausaMs)
        {
            var tiempos = new List<double>();
            for (var i = 0; i < pasos; i++)
            {
                // Se mide lo que se siente: desde que se pide la accion
                // hasta que hay algo pintado en pantalla. El refinado que
                // llega despues por el hilo de fondo NO se cuenta, porque
                // nadie lo espera mirando: la pagina ya esta a la vista.
                var reloj = Stopwatch.StartNew();
                accion();
                renderer.Refresh();
                reloj.Stop();
                tiempos.Add(reloj.Elapsed.TotalMilliseconds);

                // Fuera del cronometro: se deja que el refinado entre y que
                // el preparador avance, como pasaria mientras se lee.
                Application.DoEvents();
                for (var espera = 0; espera < pausaMs; espera += 40)
                {
                    System.Threading.Thread.Sleep(40);
                    Application.DoEvents();
                }
            }

            tiempos.Sort();
            return tiempos[tiempos.Count / 2];
        }

        /// <summary>
        /// Un documento parecido a un juego de planos: hojas grandes con
        /// mucho trazo, que es lo que hace cara la rasterizacion.
        /// </summary>
        private static void CrearFixture(string path, int paginas)
        {
            var A1 = new iTextSharp.text.Rectangle(1684F, 2384F);
            using (var document = new iTextSharp.text.Document(A1, 20F, 20F, 20F, 20F))
            {
                using (var stream = new FileStream(
                    path, FileMode.Create, FileAccess.Write))
                {
                    var writer = iTextSharp.text.pdf.PdfWriter.GetInstance(
                        document, stream);
                    document.Open();
                    var azar = new Random(11);
                    for (var pagina = 1; pagina <= paginas; pagina++)
                    {
                        if (pagina > 1)
                        {
                            document.NewPage();
                        }

                        var content = writer.DirectContent;
                        content.SetLineWidth(0.6F);
                        for (var i = 0; i < 2500; i++)
                        {
                            content.MoveTo(
                                (float)azar.NextDouble() * 1684F,
                                (float)azar.NextDouble() * 2384F);
                            content.LineTo(
                                (float)azar.NextDouble() * 1684F,
                                (float)azar.NextDouble() * 2384F);
                        }

                        content.Stroke();
                    }

                    document.Close();
                    writer.Close();
                }
            }
        }

        private sealed class Resultado
        {
            public double Pagina;
            public double Acercar;
            public double Alejar;
            public int Rasterizados;
            public int Escalados;
        }
    }
}

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

namespace SmoothZoomQa
{
    /// <summary>
    /// El zoom con la rueda: cuanto tarda en responder cada muesca, cuanto
    /// dura el peor fotograma de la animacion, cuanto cuesta el render final
    /// y si al soltar queda bajo el puntero lo mismo que habia.
    ///
    /// Se compara con el zoom por pasos de antes, que rasterizaba en cada
    /// muesca.
    /// </summary>
    internal static class Program
    {
        private const int Muescas = 5;

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                var path = args.Length > 0 ? IoPath.GetFullPath(args[0]) : null;
                if (path == null || !File.Exists(path))
                {
                    Console.Error.WriteLine(
                        "Uso: SmoothZoomQa <pdf de planos>");
                    return 2;
                }

                Application.EnableVisualStyles();
                Console.WriteLine("Documento: " + IoPath.GetFileName(path));
                Console.WriteLine();

                var antes = MedirPasos(path);
                var ahora = MedirGesto(path);

                Console.WriteLine();
                Console.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} muescas de Ctrl+rueda:",
                    Muescas));
                Console.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "  ANTES  cada muesca congelaba {0:0} ms   total {1:0} ms",
                    antes.PorMuesca, antes.Total));
                Console.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "  AHORA  cada muesca responde en {0:0.0} ms   peor fotograma {1:0} ms   render final {2:0} ms",
                    ahora.PorMuesca, ahora.PeorFotograma, ahora.RenderFinal));
                Console.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "  Foto inicial del visor: {0:0} ms   escala final {1:0} %   punto bajo el puntero: error {2:0} px",
                    ahora.Foto, ahora.EscalaFinal * 100, ahora.ErrorAnclaje));

                var fallos = 0;
                // Una muesca tiene que responder en menos de un fotograma a
                // 60 Hz, o no se percibe como inmediata.
                if (ahora.PorMuesca > 16D)
                {
                    Console.Error.WriteLine("FAIL: una muesca tarda mas de 16 ms.");
                    fallos++;
                }

                // Y la animacion no puede dar tirones: ningun fotograma por
                // encima de 50 ms.
                if (ahora.PeorFotograma > 50D)
                {
                    Console.Error.WriteLine("FAIL: la animacion tiene fotogramas de mas de 50 ms.");
                    fallos++;
                }

                if (ahora.ErrorAnclaje > 3D)
                {
                    Console.Error.WriteLine(
                        "FAIL: al soltar, lo que habia bajo el puntero se ha movido.");
                    fallos++;
                }

                var esperada = ahora.EscalaInicial * Math.Pow(1.2D, Muescas);
                if (Math.Abs(ahora.EscalaFinal - Math.Min(esperada, ahora.EscalaMaxima)) >
                    0.02D * esperada)
                {
                    Console.Error.WriteLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "FAIL: la escala final es {0:0.0} % y se esperaba {1:0.0} %.",
                        ahora.EscalaFinal * 100,
                        Math.Min(esperada, ahora.EscalaMaxima) * 100));
                    fallos++;
                }

                Console.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "  Alejar las mismas muescas: escala {0:0.0} % (partida {1:0.0} %)   error bajo el puntero {2:0} px",
                    ahora.EscalaVuelta * 100, ahora.EscalaInicial * 100, ahora.ErrorVuelta));
                if (Math.Abs(ahora.EscalaVuelta - ahora.EscalaInicial) >
                    0.02D * ahora.EscalaInicial)
                {
                    Console.Error.WriteLine("FAIL: alejar no vuelve a la escala de partida.");
                    fallos++;
                }

                if (ahora.ErrorVuelta > 3D)
                {
                    Console.Error.WriteLine("FAIL: al alejar, lo que habia bajo el puntero se ha movido.");
                    fallos++;
                }

                if (!ahora.FotoConContenido)
                {
                    Console.Error.WriteLine("FAIL: la foto del visor ha salido en blanco.");
                    fallos++;
                }

                fallos += RuedaDirectaYCursor(path);

                if (fallos == 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("PASS: el zoom con la rueda es fluido y aterriza donde debe.");
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

        private sealed class Resultado
        {
            public double PorMuesca;
            public double Total;
            public double PeorFotograma;
            public double RenderFinal;
            public double Foto;
            public double ErrorAnclaje;
            public double EscalaInicial;
            public double EscalaFinal;
            public double EscalaMaxima;
            public bool FotoConContenido;
            public double EscalaVuelta;
            public double ErrorVuelta;
        }

        private static Form CrearVentana(out PdfViewer viewer)
        {
            var form = new Form
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(40, 40),
                Size = new Size(1180, 860),
                ShowInTaskbar = false
            };
            viewer = new PdfViewer
            {
                Dock = DockStyle.Fill,
                ShowToolbar = false,
                ShowBookmarks = false
            };
            form.Controls.Add(viewer);
            form.Show();
            Application.DoEvents();
            return form;
        }

        private static Point PuntoSobreHoja(PdfRenderer r)
        {
            // Algo a la derecha y abajo del centro, que caiga sobre una hoja.
            for (var y = r.ClientSize.Height * 2 / 3; y > 20; y -= 20)
            {
                var p = new Point(r.ClientSize.Width / 2 + 60, y);
                var enPdf = PdfZoomAnchor.PointToPdfExact(r, p);
                if (enPdf.IsValid && enPdf.Page >= 0)
                {
                    return p;
                }
            }

            return new Point(r.ClientSize.Width / 2, r.ClientSize.Height / 2);
        }

        /// <summary>Como era antes: un paso real y anclado por muesca.</summary>
        private static Resultado MedirPasos(string path)
        {
            var resultado = new Resultado();
            PdfViewer viewer;
            using (var form = CrearVentana(out viewer))
            using (var real = PdfDocument.Load(path))
            using (var fluido = new PdfFluidDocument(real, null))
            {
                viewer.Document = fluido;
                viewer.ZoomMode = PdfViewerZoomMode.FitWidth;
                viewer.Renderer.Refresh();
                Application.DoEvents();
                var r = viewer.Renderer;
                var p = PuntoSobreHoja(r);
                var reloj = Stopwatch.StartNew();
                for (var i = 0; i < Muescas; i++)
                {
                    PdfZoomAnchor.Step(r, p, true);
                    r.Refresh();
                    Application.DoEvents();
                }

                reloj.Stop();
                resultado.Total = reloj.Elapsed.TotalMilliseconds;
                resultado.PorMuesca = resultado.Total / Muescas;
                viewer.Document = null;
            }

            return resultado;
        }

        /// <summary>Ahora: el gesto con la foto reescalada y un render al final.</summary>
        private static Resultado MedirGesto(string path)
        {
            var resultado = new Resultado();
            PdfViewer viewer;
            using (var form = CrearVentana(out viewer))
            using (var real = PdfDocument.Load(path))
            using (var fluido = new PdfFluidDocument(real, null))
            {
                viewer.Document = fluido;
                viewer.ZoomMode = PdfViewerZoomMode.FitWidth;
                viewer.Renderer.Refresh();
                Application.DoEvents();
                var r = viewer.Renderer;
                var p = PuntoSobreHoja(r);

                // La foto que toma el gesto es la de DrawToBitmap: se comprueba
                // aqui, ANTES del gesto, que no sale en blanco. Hacerlo entre
                // muescas metia 300 ms de recorrer pixeles y cortaba el gesto.
                resultado.FotoConContenido = FotoConContenido(r);
                resultado.EscalaInicial = PdfZoomAnchor.RealScale(r);
                resultado.EscalaMaxima =
                    resultado.EscalaInicial / r.Zoom * r.ZoomMax;
                var bajoPuntero = PdfZoomAnchor.PointToPdfExact(r, p);

                using (var gesto = new PdfSmoothZoomController(r, null))
                {
                    var arranque = Stopwatch.StartNew();
                    gesto.Trace = delegate(string que)
                    {
                        Console.WriteLine(string.Format(
                            CultureInfo.InvariantCulture,
                            "   [{0,5} ms] {1}   escala {2:0.0} %",
                            arranque.ElapsedMilliseconds,
                            que,
                            PdfZoomAnchor.RealScale(r) * 100));
                    };
                    // Primera muesca: incluye la foto del visor.
                    var reloj = Stopwatch.StartNew();
                    gesto.Wheel(p, 120);
                    reloj.Stop();
                    resultado.Foto = reloj.Elapsed.TotalMilliseconds;

                    var muescas = new List<double>();
                    var fotogramas = new List<double>();
                    for (var i = 1; i < Muescas; i++)
                    {
                        // Una muesca cada 60 ms, que es girar la rueda deprisa.
                        var hasta = Stopwatch.StartNew();
                        while (hasta.ElapsedMilliseconds < 60)
                        {
                            var f = Stopwatch.StartNew();
                            Application.DoEvents();
                            f.Stop();
                            fotogramas.Add(f.Elapsed.TotalMilliseconds);
                            if (f.Elapsed.TotalMilliseconds > 30D)
                            {
                                Console.WriteLine(string.Format(
                                    CultureInfo.InvariantCulture,
                                    "   fotograma lento: {0:0} ms (entre muescas)",
                                    f.Elapsed.TotalMilliseconds));
                            }

                            System.Threading.Thread.Sleep(4);
                        }

                        Console.WriteLine(string.Format(
                            CultureInfo.InvariantCulture,
                            "   [{0,5} ms] muesca {1}   gesto activo: {2}",
                            arranque.ElapsedMilliseconds, i + 1, gesto.IsActive));
                        var m = Stopwatch.StartNew();
                        gesto.Wheel(p, 120);
                        m.Stop();
                        muescas.Add(m.Elapsed.TotalMilliseconds);
                    }

                    // Dejar que la animacion llegue y que se asiente, sin
                    // contar el render final como fotograma.
                    var espera = Stopwatch.StartNew();
                    while (gesto.IsActive && espera.ElapsedMilliseconds < 5000)
                    {
                        var f = Stopwatch.StartNew();
                        Application.DoEvents();
                        f.Stop();
                        if (gesto.IsActive)
                        {
                            fotogramas.Add(f.Elapsed.TotalMilliseconds);
                        }
                        else
                        {
                            resultado.RenderFinal = f.Elapsed.TotalMilliseconds;
                        }

                        System.Threading.Thread.Sleep(4);
                    }

                    muescas.Sort();
                    resultado.PorMuesca = muescas.Count == 0
                        ? 0D
                        : muescas[muescas.Count / 2];
                    // El fotograma en el que se asienta el gesto contiene el
                    // render final; se ha apartado arriba.
                    fotogramas.Sort();
                    resultado.PeorFotograma = fotogramas.Count == 0
                        ? 0D
                        : fotogramas[fotogramas.Count - 1];
                }

                Application.DoEvents();
                resultado.EscalaFinal = PdfZoomAnchor.RealScale(r);
                if (bajoPuntero.IsValid && bajoPuntero.Page >= 0)
                {
                    var ahora = r.PointFromPdf(bajoPuntero);
                    resultado.ErrorAnclaje =
                        Math.Abs(ahora.X - p.X) + Math.Abs(ahora.Y - p.Y);
                }

                // Y de vuelta: alejar las mismas muescas tiene que dejar la
                // escala de partida y el mismo punto bajo el puntero.
                var bajoPuntero2 = PdfZoomAnchor.PointToPdfExact(r, p);
                using (var gesto = new PdfSmoothZoomController(r, null))
                {
                    for (var i = 0; i < Muescas; i++)
                    {
                        gesto.Wheel(p, -120);
                        var hasta = Stopwatch.StartNew();
                        while (hasta.ElapsedMilliseconds < 60)
                        {
                            Application.DoEvents();
                            System.Threading.Thread.Sleep(4);
                        }
                    }

                    var espera = Stopwatch.StartNew();
                    while (gesto.IsActive && espera.ElapsedMilliseconds < 5000)
                    {
                        Application.DoEvents();
                        System.Threading.Thread.Sleep(4);
                    }
                }

                Application.DoEvents();
                resultado.EscalaVuelta = PdfZoomAnchor.RealScale(r);
                if (bajoPuntero2.IsValid && bajoPuntero2.Page >= 0)
                {
                    var ahora = r.PointFromPdf(bajoPuntero2);
                    resultado.ErrorVuelta =
                        Math.Abs(ahora.X - p.X) + Math.Abs(ahora.Y - p.Y);
                }

                viewer.Document = null;
            }

            return resultado;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(
            IntPtr ventana, int mensaje, IntPtr w, IntPtr l);

        /// <summary>
        /// Con el raton de verdad, PdfiumViewer se adelanta: su propio filtro
        /// de rueda —instalado al crear el visor, antes que el nuestro— manda
        /// la rueda directamente a la ventana del visor y se la come. Asi
        /// llega aqui. Tiene que acabar en el zoom suave, no en el de
        /// PdfiumViewer, que no mira el puntero.
        ///
        /// Y sobre la hoja tiene que verse la flecha, no la mano.
        /// </summary>
        private static int RuedaDirectaYCursor(string path)
        {
            var fallos = 0;
            PdfViewer viewer;
            using (var form = CrearVentana(out viewer))
            using (var real = PdfDocument.Load(path))
            using (var fluido = new PdfFluidDocument(real, null))
            {
                viewer.Document = fluido;
                viewer.ZoomMode = PdfViewerZoomMode.FitWidth;
                Application.DoEvents();
                var r = viewer.Renderer;
                var escalaVista = 0D;
                using (new PdfViewNavigationController(
                    r,
                    delegate { return true; },
                    null,
                    null,
                    null,
                    null,
                    delegate(double escala) { escalaVista = escala; }))
                {
                    var zoomAntes = r.Zoom;
                    var p = r.PointToScreen(new Point(
                        r.ClientSize.Width / 3,
                        r.ClientSize.Height / 3));
                    var w = (120L << 16) | 0x0008L;
                    var l = ((long)(p.Y & 0xFFFF) << 16) | (long)(p.X & 0xFFFF);
                    SendMessage(r.Handle, 0x020A, (IntPtr)w, (IntPtr)l);
                    Application.DoEvents();

                    if (escalaVista <= 0D)
                    {
                        Console.Error.WriteLine(
                            "FAIL: la rueda enviada directa al visor no llega al zoom suave.");
                        fallos++;
                    }
                    else if (Math.Abs(r.Zoom - zoomAntes) > 0.0001D)
                    {
                        Console.Error.WriteLine(
                            "FAIL: la rueda directa la ha atendido tambien el zoom de PdfiumViewer.");
                        fallos++;
                    }
                    else
                    {
                        Console.WriteLine(
                            "  Rueda enviada directa al visor (como hace PdfiumViewer): la atiende el zoom suave");
                    }

                    var campo = typeof(PdfRenderer).BaseType.GetField(
                        "PanCursor",
                        System.Reflection.BindingFlags.Static |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Public);
                    var cursor = campo == null ? null : campo.GetValue(null);
                    if (!ReferenceEquals(cursor, Cursors.Default))
                    {
                        Console.Error.WriteLine(
                            "FAIL: sobre la hoja sigue saliendo la mano.");
                        fallos++;
                    }
                    else
                    {
                        Console.WriteLine("  Cursor sobre la hoja: flecha normal");
                    }
                }

                viewer.Document = null;
            }

            return fallos;
        }

        private static bool FotoConContenido(PdfRenderer r)
        {
            using (var bmp = new Bitmap(r.ClientSize.Width, r.ClientSize.Height))
            {
                r.DrawToBitmap(bmp, new Rectangle(Point.Empty, bmp.Size));
                var oscuros = 0;
                for (var y = 0; y < bmp.Height; y += 7)
                {
                    for (var x = 0; x < bmp.Width; x += 7)
                    {
                        var c = bmp.GetPixel(x, y);
                        if (c.R < 120 && c.G < 120 && c.B < 120)
                        {
                            oscuros++;
                        }
                    }
                }

                return oscuros > 50;
            }
        }
    }
}

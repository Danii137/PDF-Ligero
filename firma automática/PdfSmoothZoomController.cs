using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PdfiumViewer;

namespace FirmaAutomatica
{
    /// <summary>
    /// Zoom con la rueda sin tirones.
    ///
    /// EL PROBLEMA, MEDIDO. Cada paso de rueda cambiaba el aumento del visor,
    /// y el visor rasterizaba de nuevo las hojas a la vista: con planos A1,
    /// entre 350 y 560 ms por paso y con la interfaz congelada. Girar la rueda
    /// cinco veces eran casi tres segundos a saltos.
    ///
    /// LO QUE HACEN LOS VISORES RAPIDOS. Mientras se gira la rueda no se
    /// rasteriza nada. Se toma una foto de lo que hay en pantalla y se
    /// reescala esa foto alrededor del puntero, animada, que es instantaneo.
    /// Cuando la rueda para, se aplica el aumento de verdad una sola vez y se
    /// cambia la foto por las hojas nitidas.
    ///
    /// COMO SE HACE. Durante el gesto el visor NO se toca: sigue exactamente
    /// como estaba, tapado por una capa que pinta la foto. Eso permite, al
    /// terminar, preguntarle al visor —aun en su estado original— que punto
    /// del PDF habia debajo, y llevar ese punto justo al sitio donde lo
    /// dejo la animacion. Asi lo que se ve al soltar coincide con lo que se
    /// veia en la foto.
    /// </summary>
    internal sealed class PdfSmoothZoomController : IDisposable
    {
        /// <summary>Aumento por cada muesca de la rueda.</summary>
        private const double FactorPerNotch = 1.2D;

        /// <summary>
        /// Lo que tiene que estar quieta la rueda para dar el gesto por
        /// terminado y rasterizar de verdad.
        /// </summary>
        private const int SettleMilliseconds = 220;

        /// <summary>
        /// Fraccion del camino que recorre la animacion en cada fotograma.
        /// Con 0,35 a 60 fps llega en unos 120 ms, que se percibe inmediato y
        /// a la vez suave.
        /// </summary>
        private const double AnimationStep = 0.35D;

        private readonly PdfRenderer renderer;
        private readonly Action<double> scaleChanged;
        private readonly Timer frameTimer;
        private readonly Timer settleTimer;

        private ZoomOverlay overlay;
        private Bitmap snapshot;

        // La foto como mapa de bits de GDI, en un DC propio, para pintar cada
        // fotograma con StretchBlt. Ver PaintOverlay.
        private IntPtr snapshotHbitmap = IntPtr.Zero;
        private IntPtr snapshotDc = IntPtr.Zero;
        private IntPtr previousDcObject = IntPtr.Zero;
        private bool active;
        private bool disposed;

        // Correspondencia foto -> pantalla: punto en pantalla =
        // traslacion + punto en la foto * escala.
        private double shownScale = 1D;
        private double shownX;
        private double shownY;
        private double targetScale = 1D;
        private double targetX;
        private double targetY;

        // Escala real al empezar y limites permitidos, en escala real.
        private double startRealScale;
        private double minRealScale;
        private double maxRealScale;
        private Point lastPointer;

        public PdfSmoothZoomController(
            PdfRenderer renderer,
            Action<double> scaleChanged)
        {
            if (renderer == null)
            {
                throw new ArgumentNullException("renderer");
            }

            this.renderer = renderer;
            this.scaleChanged = scaleChanged;

            frameTimer = new Timer { Interval = 15 };
            frameTimer.Tick += FrameTimer_Tick;
            settleTimer = new Timer { Interval = SettleMilliseconds };
            settleTimer.Tick += SettleTimer_Tick;
            renderer.Disposed += delegate { Dispose(); };
            renderer.SizeChanged += delegate
            {
                Commit();
            };
        }

        /// <summary>Solo para los bancos de pruebas: que ha pasado y cuando.</summary>
        public Action<string> Trace { get; set; }

        private void Say(string que)
        {
            var t = Trace;
            if (t != null)
            {
                t(que);
            }
        }

        /// <summary>Hay un gesto de zoom en marcha.</summary>
        public bool IsActive
        {
            get { return active; }
        }

        /// <summary>
        /// Una muesca (o fraccion, en un panel tactil) de Ctrl+rueda con el
        /// puntero en ese punto del visor.
        /// </summary>
        public void Wheel(Point pointer, int delta)
        {
            if (disposed || delta == 0 || renderer.Document == null)
            {
                return;
            }

            if (!active)
            {
                Say("empieza gesto");
            }

            if (!active && !Begin())
            {
                Say("no se pudo tomar la foto: paso real");
                // Si no se pudo tomar la foto, se hace como antes: un paso
                // real, anclado. Mas lento pero correcto.
                PdfZoomAnchor.Step(renderer, pointer, delta > 0);
                return;
            }

            lastPointer = pointer;
            var factor = Math.Pow(FactorPerNotch, delta / 120D);

            // Limites en escala real: no se puede prometer con la foto un
            // aumento que el visor luego no dara.
            var realActual = startRealScale * targetScale;
            var realDeseada = Math.Max(
                minRealScale,
                Math.Min(maxRealScale, realActual * factor));
            factor = realDeseada / realActual;
            if (Math.Abs(factor - 1D) < 0.0001D)
            {
                RestartSettle();
                return;
            }

            // Escalar la correspondencia alrededor del puntero: el punto de
            // la foto que esta bajo el puntero se queda bajo el puntero.
            targetX = pointer.X + ((targetX - pointer.X) * factor);
            targetY = pointer.Y + ((targetY - pointer.Y) * factor);
            targetScale *= factor;

            if (scaleChanged != null)
            {
                scaleChanged(startRealScale * targetScale);
            }

            if (!frameTimer.Enabled)
            {
                frameTimer.Start();
            }

            RestartSettle();
        }

        /// <summary>
        /// Termina el gesto ya: aplica el aumento y quita la foto. Se llama
        /// al hacer cualquier otra cosa con el visor mientras dura.
        /// </summary>
        public void Commit()
        {
            if (!active || disposed)
            {
                return;
            }

            // Se da por terminado ANTES de tocar el visor. Aplicar el zoom
            // hace aparecer o desaparecer las barras de desplazamiento, eso
            // cambia el tamaño del visor y vuelve a pedir que se termine el
            // gesto: sin esto se aplicaba dos veces, y aterrizaba en otra
            // escala y lejos del puntero. Medido: 106 % en vez de 88 %.
            active = false;
            frameTimer.Stop();
            settleTimer.Stop();
            Say("se aplica el zoom");

            // El visor sigue en su estado original. El punto de la foto que
            // ha quedado bajo el puntero es, en el visor, el mismo punto de
            // pantalla sin transformar: se le pide al visor que lo lleve al
            // sitio donde lo ha dejado la animacion.
            var escala = targetScale;
            var enFoto = new Point(
                (int)Math.Round((lastPointer.X - targetX) / escala),
                (int)Math.Round((lastPointer.Y - targetY) / escala));

            // La foto se queda puesta con el estado final mientras el visor
            // rasteriza, para que no se vea ni un fotograma intermedio.
            shownScale = targetScale;
            shownX = targetX;
            shownY = targetY;
            if (overlay != null)
            {
                overlay.Refresh();
            }

            try
            {
                PdfZoomAnchor.MoveKeepingPoint(
                    renderer,
                    enFoto,
                    lastPointer,
                    startRealScale * escala);

                // Se rasteriza por debajo de la foto: DrawToBitmap hace que
                // el visor pinte —y guarde— las hojas nuevas sin que se vean
                // a medias.
                using (var descarte = new Bitmap(
                    Math.Max(1, renderer.ClientSize.Width),
                    Math.Max(1, renderer.ClientSize.Height)))
                {
                    renderer.DrawToBitmap(
                        descarte,
                        new Rectangle(Point.Empty, descarte.Size));
                }
            }
            catch (Exception ex)
            {
                AppLog.Write("No se pudo aplicar el zoom: " + ex);
            }
            finally
            {
                End();
            }

            if (scaleChanged != null)
            {
                scaleChanged(PdfZoomAnchor.RealScale(renderer));
            }
        }

        private bool Begin()
        {
            var tamano = renderer.ClientSize;
            if (tamano.Width < 2 || tamano.Height < 2 ||
                !renderer.IsHandleCreated || !renderer.Visible)
            {
                return false;
            }

            startRealScale = PdfZoomAnchor.RealScale(renderer);
            if (startRealScale <= 0D || renderer.Zoom <= 0D)
            {
                return false;
            }

            // Los limites del visor son de Zoom relativo; se pasan a escala
            // real con la misma regla de tres que usa el resto.
            var real1 = startRealScale / renderer.Zoom;
            minRealScale = renderer.ZoomMin * real1;
            maxRealScale = renderer.ZoomMax * real1;

            try
            {
                // Pixeles premultiplicados: es el formato nativo de GDI+ para
                // componer, y reescalar en el va varias veces mas rapido que
                // en ARGB normal.
                snapshot = new Bitmap(
                    tamano.Width,
                    tamano.Height,
                    System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                renderer.DrawToBitmap(
                    snapshot,
                    new Rectangle(Point.Empty, tamano));
                snapshotHbitmap = snapshot.GetHbitmap();
                snapshotDc = CreateCompatibleDC(IntPtr.Zero);
                previousDcObject = SelectObject(snapshotDc, snapshotHbitmap);
            }
            catch (Exception ex)
            {
                AppLog.Write("No se pudo tomar la foto del visor: " + ex);
                ReleaseGdiSnapshot();
                if (snapshot != null)
                {
                    snapshot.Dispose();
                    snapshot = null;
                }

                return false;
            }

            shownScale = targetScale = 1D;
            shownX = targetX = 0D;
            shownY = targetY = 0D;

            // La capa va en el contenedor del visor, como hermana por encima,
            // y NO dentro del visor. El visor es un control con desplazamiento:
            // al meterle una hija en (0,0) se desplazaba solo para dejarla a la
            // vista —medido: 7 px, y con zoom alto serian cientos—, con lo que
            // el zoom aterrizaba lejos del puntero, y encima repintaba por
            // debajo con un render de 300 ms que cortaba el gesto.
            var contenedor = renderer.Parent;
            if (contenedor == null)
            {
                snapshot.Dispose();
                snapshot = null;
                return false;
            }

            if (overlay == null || overlay.Parent != contenedor)
            {
                if (overlay != null)
                {
                    overlay.Dispose();
                }

                overlay = new ZoomOverlay(this);
                overlay.BackColor = renderer.BackColor;
                contenedor.Controls.Add(overlay);
            }

            overlay.Bounds = renderer.Bounds;
            overlay.Visible = true;
            overlay.BringToFront();
            overlay.Refresh();
            active = true;
            return true;
        }

        private void End()
        {
            active = false;
            if (overlay != null)
            {
                overlay.Visible = false;
            }

            ReleaseGdiSnapshot();
            if (snapshot != null)
            {
                snapshot.Dispose();
                snapshot = null;
            }
        }

        private void ReleaseGdiSnapshot()
        {
            if (snapshotDc != IntPtr.Zero)
            {
                if (previousDcObject != IntPtr.Zero)
                {
                    SelectObject(snapshotDc, previousDcObject);
                }

                DeleteDC(snapshotDc);
                snapshotDc = IntPtr.Zero;
                previousDcObject = IntPtr.Zero;
            }

            if (snapshotHbitmap != IntPtr.Zero)
            {
                DeleteObject(snapshotHbitmap);
                snapshotHbitmap = IntPtr.Zero;
            }
        }

        private void RestartSettle()
        {
            settleTimer.Stop();
            settleTimer.Start();
        }

        private void FrameTimer_Tick(object sender, EventArgs e)
        {
            if (!active)
            {
                frameTimer.Stop();
                return;
            }

            // La escala se interpola en logaritmos: acercar y alejar se
            // sienten igual de rapidos.
            var logMostrada = Math.Log(shownScale);
            var logObjetivo = Math.Log(targetScale);
            logMostrada += (logObjetivo - logMostrada) * AnimationStep;
            shownScale = Math.Exp(logMostrada);
            shownX += (targetX - shownX) * AnimationStep;
            shownY += (targetY - shownY) * AnimationStep;

            var llegado =
                Math.Abs(logObjetivo - logMostrada) < 0.002D &&
                Math.Abs(targetX - shownX) < 0.5D &&
                Math.Abs(targetY - shownY) < 0.5D;
            if (llegado)
            {
                shownScale = targetScale;
                shownX = targetX;
                shownY = targetY;
                frameTimer.Stop();
            }

            if (overlay != null)
            {
                overlay.Invalidate();
            }
        }

        private void SettleTimer_Tick(object sender, EventArgs e)
        {
            settleTimer.Stop();
            Commit();
        }

        private void PaintOverlay(Graphics graphics, Size tamano)
        {
            graphics.Clear(renderer.BackColor);
            if (snapshot == null || snapshotDc == IntPtr.Zero)
            {
                return;
            }

            // Solo el trozo de la foto que cae dentro de la ventana: al
            // acercar, la mayor parte de la foto escalada queda fuera.
            var origen = new RectangleF(
                (float)(-shownX / shownScale),
                (float)(-shownY / shownScale),
                (float)(tamano.Width / shownScale),
                (float)(tamano.Height / shownScale));
            origen.Intersect(
                new RectangleF(0F, 0F, snapshot.Width, snapshot.Height));
            if (origen.Width <= 0F || origen.Height <= 0F)
            {
                return;
            }

            // El destino se calcula a partir del origen YA redondeado, para
            // que la correspondencia foto -> pantalla sea exacta y la imagen
            // no tiemble un pixel arriba y abajo entre fotogramas.
            var sx = (int)Math.Floor(origen.X);
            var sy = (int)Math.Floor(origen.Y);
            var sw = Math.Max(1, (int)Math.Ceiling(origen.Right) - sx);
            var sh = Math.Max(1, (int)Math.Ceiling(origen.Bottom) - sy);
            var dx = (int)Math.Round(shownX + (sx * shownScale));
            var dy = (int)Math.Round(shownY + (sy * shownScale));
            var dw = (int)Math.Round(sw * shownScale);
            var dh = (int)Math.Round(sh * shownScale);

            // GDI clasico y no GDI+: medido sobre una ventana de 1147x800,
            // StretchBlt con HALFTONE pinta un fotograma en 2,5 ms y el
            // DrawImage de GDI+ en 66 ms. Con 66 ms los fotogramas se
            // amontonaban, la rueda dejaba de atenderse y el gesto se cortaba
            // a cada muesca.
            var hdc = graphics.GetHdc();
            try
            {
                SetStretchBltMode(hdc, Halftone);
                SetBrushOrgEx(hdc, 0, 0, IntPtr.Zero);
                StretchBlt(
                    hdc, dx, dy, dw, dh,
                    snapshotDc, sx, sy, sw, sh,
                    SrcCopy);
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }
        }

        private const int Halftone = 4;
        private const int SrcCopy = 0x00CC0020;

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr objeto);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr objeto);

        [DllImport("gdi32.dll")]
        private static extern int SetStretchBltMode(IntPtr hdc, int modo);

        [DllImport("gdi32.dll")]
        private static extern bool SetBrushOrgEx(
            IntPtr hdc, int x, int y, IntPtr anterior);

        [DllImport("gdi32.dll")]
        private static extern bool StretchBlt(
            IntPtr destino, int dx, int dy, int dw, int dh,
            IntPtr origen, int sx, int sy, int sw, int sh,
            int operacion);

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            frameTimer.Stop();
            settleTimer.Stop();
            frameTimer.Dispose();
            settleTimer.Dispose();
            End();
            if (overlay != null)
            {
                try
                {
                    overlay.Dispose();
                }
                catch (Exception)
                {
                }

                overlay = null;
            }
        }

        /// <summary>
        /// La capa que tapa el visor durante el gesto y pinta la foto.
        /// </summary>
        private sealed class ZoomOverlay : Control
        {
            private readonly PdfSmoothZoomController owner;

            public ZoomOverlay(PdfSmoothZoomController owner)
            {
                this.owner = owner;
                SetStyle(
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.UserPaint |
                    ControlStyles.Opaque,
                    true);
                TabStop = false;
                Visible = false;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                owner.PaintOverlay(e.Graphics, ClientSize);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                // Cualquier clic durante el gesto lo termina: el clic va a
                // lo que hay debajo, que tiene que estar ya en su sitio.
                owner.Commit();
                base.OnMouseDown(e);
            }
        }
    }
}

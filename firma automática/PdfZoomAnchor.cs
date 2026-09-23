using System;
using System.Drawing;
using PdfiumViewer;

namespace FirmaAutomatica
{
    /// <summary>
    /// Hace zoom sin perder el sitio.
    ///
    /// EL PROBLEMA. PdfRenderer.ZoomIn/ZoomOut cambian el aumento y dejan el
    /// desplazamiento donde estaba, medido en pixeles. Como el documento
    /// entero se hace mas alto, esos mismos pixeles caen mucho mas adelante:
    /// medido sobre un juego de planos, acercar cuatro veces desde la hoja 12
    /// te dejaba en la 25. Es lo que hace que el zoom se sienta erratico, y
    /// ademas lo que lo hace lento, porque cada paso aterriza en una hoja que
    /// no se habia visto y hay que rasterizarla entera.
    ///
    /// LO QUE HACEN LOS VISORES BUENOS. El punto que hay bajo el raton —o el
    /// centro de la ventana, si el zoom viene de un boton o del teclado— se
    /// queda quieto. Uno se acerca a lo que esta mirando.
    ///
    /// COMO SE HACE. Antes de tocar el aumento se anota a que punto del
    /// documento corresponde el punto de anclaje. Despues se vuelve a
    /// preguntar donde ha ido a parar ese mismo punto del documento y se
    /// corrige el desplazamiento por la diferencia.
    /// </summary>
    internal static class PdfZoomAnchor
    {
        /// <summary>Pasos de aumento, de menos a mas.</summary>
        private static readonly double[] Stops = new[]
        {
            0.05D, 0.10D, 0.15D, 0.20D, 0.25D, 0.33D, 0.40D, 0.50D,
            0.67D, 0.75D, 1.00D, 1.25D, 1.50D, 2.00D, 3.00D, 4.00D,
            6.00D, 8.00D
        };

        /// <summary>
        /// Acerca o aleja un paso dejando quieto el punto indicado, en
        /// coordenadas del visor. Los pasos son de escala REAL: el 100 % es
        /// el tamaño del papel.
        /// </summary>
        public static void Step(
            PdfRenderer renderer,
            Point anchorClientPoint,
            bool zoomIn)
        {
            if (renderer == null || renderer.Document == null)
            {
                return;
            }

            var real = RealScale(renderer);
            if (real <= 0D)
            {
                return;
            }

            var destino = NextStop(real, zoomIn);
            if (Math.Abs(destino - real) < 0.0001D)
            {
                return;
            }

            SetRealScale(renderer, anchorClientPoint, destino);
        }

        /// <summary>
        /// Escala a la que se ve el documento en pantalla, con 1 = tamaño real
        /// del papel.
        ///
        /// OJO: no es PdfRenderer.Zoom. En PdfiumViewer el Zoom es relativo
        /// al ajuste —ancho o pagina entera—, asi que un "Zoom 1" enseña un A3
        /// al 25 % de su tamaño en un portatil normal. Se descubrio midiendo:
        /// el indicador decia 100 % con la hoja a un cuarto. Aqui se mide lo
        /// que ocupa de verdad la pagina en pantalla contra lo que mide en
        /// papel a la resolucion del monitor.
        /// </summary>
        public static double RealScale(PdfRenderer renderer)
        {
            if (renderer == null || renderer.Document == null)
            {
                return 0D;
            }

            try
            {
                var pagina = Math.Max(
                    0,
                    Math.Min(
                        renderer.Document.PageCount - 1,
                        renderer.Page));
                var tamano = renderer.Document.PageSizes[pagina];
                if (tamano.Width <= 0.01F)
                {
                    return 0D;
                }

                var caja = renderer.BoundsFromPdf(
                    new PdfRectangle(
                        pagina,
                        new RectangleF(0F, 0F, tamano.Width, tamano.Height)));
                var enPantalla = Math.Max(
                    Math.Abs(caja.Width),
                    Math.Abs(caja.Height));
                // Con la pagina girada 90 grados el ancho en pantalla es su
                // alto en papel: se compara el lado largo con el lado largo.
                var enPapel = Math.Max(tamano.Width, tamano.Height) /
                    72D * ScreenDpi(renderer);
                return enPapel <= 0.01D ? 0D : enPantalla / enPapel;
            }
            catch (Exception)
            {
                return 0D;
            }
        }

        /// <summary>
        /// Lleva la vista a una escala real concreta —1 = tamaño del papel—
        /// sin perder el sitio.
        /// </summary>
        public static void SetRealScale(
            PdfRenderer renderer,
            Point anchorClientPoint,
            double realScale)
        {
            if (renderer == null || renderer.Document == null)
            {
                return;
            }

            var actual = RealScale(renderer);
            if (actual <= 0D || renderer.Zoom <= 0D)
            {
                return;
            }

            // La escala real es proporcional al Zoom, asi que basta una regla
            // de tres.
            var zoom = renderer.Zoom * realScale / actual;
            ApplyKeepingAnchor(renderer, anchorClientPoint, zoom);
        }

        /// <summary>Lleva a un Zoom relativo concreto sin perder el sitio.</summary>
        public static void SetZoom(
            PdfRenderer renderer,
            Point anchorClientPoint,
            double zoom)
        {
            if (renderer == null || renderer.Document == null)
            {
                return;
            }

            ApplyKeepingAnchor(renderer, anchorClientPoint, zoom);
        }

        private static float cachedDpi;

        private static float ScreenDpi(PdfRenderer renderer)
        {
            if (cachedDpi > 0F)
            {
                return cachedDpi;
            }

            try
            {
                using (var graphics = renderer.CreateGraphics())
                {
                    cachedDpi = graphics.DpiX;
                }
            }
            catch (Exception)
            {
                cachedDpi = 96F;
            }

            if (cachedDpi <= 0F)
            {
                cachedDpi = 96F;
            }

            return cachedDpi;
        }

        /// <summary>El centro de la ventana, para el zoom de menu o teclado.</summary>
        public static Point CenterOf(PdfRenderer renderer)
        {
            if (renderer == null)
            {
                return Point.Empty;
            }

            return new Point(
                renderer.ClientSize.Width / 2,
                renderer.ClientSize.Height / 2);
        }

        /// <summary>
        /// Cambia a una escala real y lleva el punto del documento que ahora
        /// esta bajo <paramref name="before"/> hasta <paramref name="after"/>.
        /// Es lo que necesita el zoom suave: la animacion deja un punto en un
        /// sitio y el visor tiene que acabar enseñando lo mismo.
        /// </summary>
        public static void MoveKeepingPoint(
            PdfRenderer renderer,
            Point before,
            Point after,
            double realScale)
        {
            if (renderer == null || renderer.Document == null)
            {
                return;
            }

            var actual = RealScale(renderer);
            if (actual <= 0D || renderer.Zoom <= 0D)
            {
                return;
            }

            ApplyMapping(
                renderer,
                before,
                after,
                renderer.Zoom * realScale / actual);
        }

        private static void ApplyKeepingAnchor(
            PdfRenderer renderer,
            Point anchorClientPoint,
            double zoom)
        {
            ApplyMapping(renderer, anchorClientPoint, anchorClientPoint, zoom);
        }

        private static void ApplyMapping(
            PdfRenderer renderer,
            Point anchorClientPoint,
            Point targetClientPoint,
            double zoom)
        {
            var limitado = Math.Max(
                renderer.ZoomMin,
                Math.Min(renderer.ZoomMax, zoom));

            PdfPoint antes;
            try
            {
                antes = renderer.PointToPdf(anchorClientPoint);
            }
            catch (Exception)
            {
                antes = new PdfPoint();
            }

            // Posicion del raton dentro del documento entero, en proporcion.
            // Sirve cuando el raton no esta sobre ninguna hoja: el fondo gris
            // que rodea las paginas o el hueco entre ellas. Antes, en ese caso
            // se renunciaba al anclaje y el zoom se iba a la esquina superior
            // izquierda; con planos en ajuste al ancho, casi media ventana es
            // fondo gris, asi que pasaba a menudo.
            var areaAntes = renderer.DisplayRectangle;
            var fraccionX = areaAntes.Width <= 0
                ? 0.5D
                : (anchorClientPoint.X - areaAntes.X) / (double)areaAntes.Width;
            var fraccionY = areaAntes.Height <= 0
                ? 0.5D
                : (anchorClientPoint.Y - areaAntes.Y) / (double)areaAntes.Height;

            renderer.Zoom = limitado;

            if (!antes.IsValid || antes.Page < 0)
            {
                try
                {
                    var areaDespues = renderer.DisplayRectangle;
                    renderer.SetDisplayRectLocation(
                        new Point(
                            (int)Math.Round(
                                targetClientPoint.X -
                                (fraccionX * areaDespues.Width)),
                            (int)Math.Round(
                                targetClientPoint.Y -
                                (fraccionY * areaDespues.Height))));
                }
                catch (Exception ex)
                {
                    AppLog.Write(
                        "No se pudo mantener el punto al hacer zoom: " + ex);
                }

                return;
            }

            try
            {
                // Dos pasadas. Mover la vista puede hacer aparecer o
                // desaparecer una barra de desplazamiento, el visor se
                // reajusta y el punto se corre unos pixeles —medido: 13 px al
                // terminar un zoom con la rueda—. La segunda pasada corrige
                // ese resto.
                for (var pasada = 0; pasada < 2; pasada++)
                {
                    var despues = renderer.PointFromPdf(antes);
                    var dx = despues.X - targetClientPoint.X;
                    var dy = despues.Y - targetClientPoint.Y;
                    if (dx == 0 && dy == 0)
                    {
                        return;
                    }

                    var area = renderer.DisplayRectangle;
                    renderer.SetDisplayRectLocation(
                        new Point(area.X - dx, area.Y - dy));
                }
            }
            catch (Exception ex)
            {
                AppLog.Write(
                    "No se pudo mantener el punto al hacer zoom: " + ex);
            }
        }

        /// <summary>
        /// Siguiente paso de la escala. Se usan pasos con nombre —50 %, 75 %,
        /// 100 %, 150 %— en vez de multiplicar por un factor fijo, porque son
        /// los que espera cualquiera que venga de otro visor y porque hacen
        /// que el 100 % se pueda alcanzar de verdad.
        /// </summary>
        private static double NextStop(double actual, bool zoomIn)
        {
            if (zoomIn)
            {
                foreach (var parada in Stops)
                {
                    if (parada > actual + 0.001D)
                    {
                        return parada;
                    }
                }

                return Stops[Stops.Length - 1];
            }

            for (var i = Stops.Length - 1; i >= 0; i--)
            {
                if (Stops[i] < actual - 0.001D)
                {
                    return Stops[i];
                }
            }

            return Stops[0];
        }
    }
}

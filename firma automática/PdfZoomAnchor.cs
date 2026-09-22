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
            0.10D, 0.15D, 0.25D, 0.33D, 0.50D, 0.67D, 0.75D,
            1.00D, 1.25D, 1.50D, 2.00D, 3.00D, 4.00D, 6.00D, 8.00D
        };

        /// <summary>
        /// Acerca o aleja un paso dejando quieto el punto indicado, en
        /// coordenadas del visor.
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

            var actual = renderer.Zoom;
            var destino = NextStop(actual, zoomIn);
            if (Math.Abs(destino - actual) < 0.0001D)
            {
                return;
            }

            ApplyKeepingAnchor(renderer, anchorClientPoint, destino);
        }

        /// <summary>Lleva a un aumento concreto sin perder el sitio.</summary>
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

        private static void ApplyKeepingAnchor(
            PdfRenderer renderer,
            Point anchorClientPoint,
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
                renderer.Zoom = limitado;
                return;
            }

            renderer.Zoom = limitado;

            if (!antes.IsValid || antes.Page < 0)
            {
                // El raton estaba en el hueco entre paginas: no hay punto del
                // documento al que agarrarse, y con cambiar el aumento basta.
                return;
            }

            try
            {
                var despues = renderer.PointFromPdf(antes);
                var dx = despues.X - anchorClientPoint.X;
                var dy = despues.Y - anchorClientPoint.Y;
                if (dx == 0 && dy == 0)
                {
                    return;
                }

                var area = renderer.DisplayRectangle;
                renderer.SetDisplayRectLocation(
                    new Point(area.X - dx, area.Y - dy));
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

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;

namespace FirmaAutomatica
{
    /// <summary>Que se esta dibujando.</summary>
    internal enum PdfAnnotationKind
    {
        /// <summary>Trazo a mano alzada, como un rotulador.</summary>
        Ink,

        /// <summary>Marca translucida sobre un texto, como un fluorescente.</summary>
        Highlight,

        /// <summary>Nota anclada a un punto de la pagina.</summary>
        Note
    }

    /// <summary>
    /// Una marca sobre una pagina, en coordenadas de PDF.
    ///
    /// Las coordenadas se guardan en espacio de pagina y no en pixeles de
    /// pantalla, para que la marca siga en su sitio al ampliar, desplazar o
    /// girar la vista.
    /// </summary>
    internal sealed class PdfAnnotationItem
    {
        private readonly List<List<PointF>> strokes = new List<List<PointF>>();

        public PdfAnnotationItem(PdfAnnotationKind kind, int pageNumber)
        {
            if (pageNumber < 1)
            {
                throw new ArgumentOutOfRangeException("pageNumber");
            }

            Kind = kind;
            PageNumber = pageNumber;
            Color = Color.FromArgb(238, 91, 61);
            WidthPoints = 2F;
            Opacity = 1F;
            Contents = string.Empty;
            Author = string.Empty;
        }

        public PdfAnnotationKind Kind { get; private set; }

        public int PageNumber { get; private set; }

        public Color Color { get; set; }

        public float WidthPoints { get; set; }

        /// <summary>Opacidad de 0 a 1. El subrayador va translucido.</summary>
        public float Opacity { get; set; }

        /// <summary>Texto de la nota; vacio en trazos y subrayados.</summary>
        public string Contents { get; set; }

        public string Author { get; set; }

        /// <summary>
        /// Trazos del rotulador. Cada uno es la secuencia de puntos de un
        /// arrastre del raton, sin levantar el boton.
        /// </summary>
        public IList<List<PointF>> Strokes
        {
            get { return strokes; }
        }

        /// <summary>Zona marcada por el subrayador o punto de la nota.</summary>
        public RectangleF Area { get; set; }

        public void BeginStroke()
        {
            strokes.Add(new List<PointF>());
        }

        public void AddPoint(PointF point)
        {
            if (strokes.Count == 0)
            {
                BeginStroke();
            }

            strokes[strokes.Count - 1].Add(point);
        }

        /// <summary>Un trazo de un solo punto no dibuja nada util.</summary>
        public void DropEmptyStrokes()
        {
            for (var i = strokes.Count - 1; i >= 0; i--)
            {
                if (strokes[i].Count < 2)
                {
                    strokes.RemoveAt(i);
                }
            }
        }

        public bool IsEmpty()
        {
            if (Kind == PdfAnnotationKind.Ink)
            {
                return strokes.Count == 0;
            }
            if (Kind == PdfAnnotationKind.Highlight)
            {
                return Area.Width <= 0.01F || Area.Height <= 0.01F;
            }

            return string.IsNullOrWhiteSpace(Contents);
        }

        /// <summary>
        /// Rectangulo que envuelve la marca, en coordenadas de pagina. Es lo que
        /// va en /Rect, y se ensancha con el grosor del trazo para que no quede
        /// recortado.
        /// </summary>
        public RectangleF GetBounds()
        {
            if (Kind != PdfAnnotationKind.Ink)
            {
                return Area;
            }

            var hayPuntos = false;
            float minX = 0F, minY = 0F, maxX = 0F, maxY = 0F;
            foreach (var stroke in strokes)
            {
                foreach (var punto in stroke)
                {
                    if (!hayPuntos)
                    {
                        minX = maxX = punto.X;
                        minY = maxY = punto.Y;
                        hayPuntos = true;
                        continue;
                    }

                    minX = Math.Min(minX, punto.X);
                    maxX = Math.Max(maxX, punto.X);
                    minY = Math.Min(minY, punto.Y);
                    maxY = Math.Max(maxY, punto.Y);
                }
            }

            if (!hayPuntos)
            {
                return RectangleF.Empty;
            }

            var margen = Math.Max(1F, WidthPoints);
            return new RectangleF(
                minX - margen,
                minY - margen,
                (maxX - minX) + (2F * margen),
                (maxY - minY) + (2F * margen));
        }

        public string Describe()
        {
            if (Kind == PdfAnnotationKind.Note)
            {
                var texto = (Contents ?? string.Empty).Replace(
                    Environment.NewLine,
                    " ");
                if (texto.Length > 60)
                {
                    texto = texto.Substring(0, 57) + "...";
                }

                return "Nota: " + texto;
            }

            if (Kind == PdfAnnotationKind.Highlight)
            {
                return "Subrayado";
            }

            return "Trazo (" +
                strokes.Count.ToString(CultureInfo.CurrentCulture) +
                (strokes.Count == 1 ? " linea)" : " lineas)");
        }
    }

    /// <summary>
    /// Marcas pendientes de escribir de un documento.
    ///
    /// Se acumulan en memoria y se vuelcan al PDF de una sola vez: escribir una
    /// revision por trazo generaria decenas de versiones y seria lento.
    /// </summary>
    internal sealed class PdfAnnotationBatch
    {
        private readonly List<PdfAnnotationItem> items =
            new List<PdfAnnotationItem>();

        public IList<PdfAnnotationItem> Items
        {
            get { return items; }
        }

        public bool HasPending
        {
            get { return items.Count > 0; }
        }

        public void Add(PdfAnnotationItem item)
        {
            if (item == null)
            {
                throw new ArgumentNullException("item");
            }
            if (item.IsEmpty())
            {
                return;
            }

            items.Add(item);
        }

        /// <summary>Deshace la ultima marca; devuelve si habia algo que deshacer.</summary>
        public bool UndoLast()
        {
            if (items.Count == 0)
            {
                return false;
            }

            items.RemoveAt(items.Count - 1);
            return true;
        }

        public void Clear()
        {
            items.Clear();
        }

        public string Describe()
        {
            if (items.Count == 0)
            {
                return "Sin marcas pendientes";
            }

            var trazos = 0;
            var subrayados = 0;
            var notas = 0;
            foreach (var item in items)
            {
                if (item.Kind == PdfAnnotationKind.Ink)
                {
                    trazos++;
                }
                else if (item.Kind == PdfAnnotationKind.Highlight)
                {
                    subrayados++;
                }
                else
                {
                    notas++;
                }
            }

            var partes = new List<string>();
            if (trazos > 0)
            {
                partes.Add(Pluralizar(trazos, "trazo", "trazos"));
            }
            if (subrayados > 0)
            {
                partes.Add(Pluralizar(subrayados, "subrayado", "subrayados"));
            }
            if (notas > 0)
            {
                partes.Add(Pluralizar(notas, "nota", "notas"));
            }

            return string.Join(", ", partes.ToArray());
        }

        private static string Pluralizar(
            int cantidad,
            string singular,
            string plural)
        {
            return cantidad.ToString(CultureInfo.CurrentCulture) + " " +
                (cantidad == 1 ? singular : plural);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;

namespace FirmaAutomatica
{
    /// <summary>
    /// Un trozo de texto editable de una pagina: una linea, con su recuadro y
    /// su tipografia.
    ///
    /// Se trabaja por lineas y no por parrafos porque una linea se corresponde
    /// con un conjunto concreto de operadores del flujo de contenido, que es lo
    /// que sabe sustituir PdfDirectTextEditService. Un parrafo obligaria a
    /// recomponer el reparto entre lineas, que el formato PDF no hace solo.
    /// </summary>
    internal sealed class PdfTextBlock
    {
        internal PdfTextBlock(
            int pageNumber,
            string text,
            RectangleF bounds,
            float baselineY,
            PdfTextStyle style)
        {
            PageNumber = pageNumber;
            Text = text ?? string.Empty;
            Bounds = bounds;
            BaselineY = baselineY;
            Style = style;
        }

        public int PageNumber { get; private set; }

        public string Text { get; private set; }

        /// <summary>Recuadro en coordenadas de pagina, origen abajo.</summary>
        public RectangleF Bounds { get; private set; }

        public float BaselineY { get; private set; }

        public PdfTextStyle Style { get; private set; }

        public bool Contains(float x, float y)
        {
            return x >= Bounds.Left &&
                x <= Bounds.Right &&
                y >= Bounds.Top &&
                y <= Bounds.Bottom;
        }
    }

    /// <summary>
    /// Encuentra las lineas de texto de una pagina para poder pincharlas y
    /// reescribirlas, al modo de los editores de PDF al uso.
    /// </summary>
    internal static class PdfTextBlockLocator
    {
        // Dos fragmentos pertenecen a la misma linea si sus lineas base estan
        // mas cerca que esta fraccion del cuerpo de letra.
        private const float BaselineToleranceRatio = 0.35F;

        // Separacion horizontal, en anchos de espacio, a partir de la cual se
        // considera que empieza otra columna y no la misma linea.
        private const float ColumnGapInSpaces = 6F;

        private const int InvisibleRenderMode = 3;

        /// <summary>
        /// Lineas de texto de la pagina, de arriba abajo. Nunca lanza: si la
        /// pagina no se puede analizar se devuelve una lista vacia y la
        /// herramienta simplemente no ofrece nada que editar.
        /// </summary>
        public static IList<PdfTextBlock> Locate(PdfReader reader, int pageNumber)
        {
            var bloques = new List<PdfTextBlock>();
            if (reader == null ||
                pageNumber < 1 ||
                pageNumber > reader.NumberOfPages)
            {
                return bloques;
            }

            try
            {
                var listener = new RecolectorDeLineas();
                var parser = new PdfReaderContentParser(reader);
                parser.ProcessContent(pageNumber, listener);
                return listener.Construir(pageNumber);
            }
            catch (Exception)
            {
                return bloques;
            }
        }

        private sealed class Fragmento
        {
            public string Texto;
            public float Izquierda;
            public float Derecha;
            public float Base;
            public float Arriba;
            public float Abajo;
            public float Tamano;
            public float AnchoEspacio;
            public string Fuente;
            public string FuenteBruta;
            public bool Negrita;
            public bool Cursiva;
            public bool Incrustada;
            public bool Subconjunto;
            public Color Color;
        }

        private sealed class RecolectorDeLineas : IRenderListener
        {
            private readonly List<Fragmento> fragmentos = new List<Fragmento>();

            public void BeginTextBlock()
            {
            }

            public void EndTextBlock()
            {
            }

            public void RenderImage(ImageRenderInfo renderInfo)
            {
            }

            public void RenderText(TextRenderInfo renderInfo)
            {
                if (renderInfo == null ||
                    renderInfo.GetTextRenderMode() == InvisibleRenderMode)
                {
                    return;
                }

                var texto = renderInfo.GetText();
                if (string.IsNullOrEmpty(texto))
                {
                    return;
                }

                var linea = renderInfo.GetBaseline();
                var inicio = linea.GetStartPoint();
                var fin = linea.GetEndPoint();
                var ascendente = renderInfo.GetAscentLine().GetStartPoint();
                var descendente = renderInfo.GetDescentLine().GetStartPoint();

                var fuente = renderInfo.GetFont();
                var bruto = fuente == null
                    ? string.Empty
                    : (fuente.PostscriptFontName ?? string.Empty);
                var limpio = PdfTextStyleProbe.CleanSubsetPrefix(bruto);

                var fragmento = new Fragmento();
                fragmento.Texto = texto;
                fragmento.Izquierda = Math.Min(inicio[Vector.I1], fin[Vector.I1]);
                fragmento.Derecha = Math.Max(inicio[Vector.I1], fin[Vector.I1]);
                fragmento.Base = inicio[Vector.I2];
                fragmento.Arriba = Math.Max(
                    ascendente[Vector.I2],
                    descendente[Vector.I2]);
                fragmento.Abajo = Math.Min(
                    ascendente[Vector.I2],
                    descendente[Vector.I2]);
                fragmento.Tamano = PdfTextStyleProbe.MeasureFontSize(renderInfo);
                fragmento.AnchoEspacio = renderInfo.GetSingleSpaceWidth();
                fragmento.FuenteBruta = bruto;
                fragmento.Fuente = PdfTextStyleProbe.CleanFamilyName(limpio);
                fragmento.Negrita = PdfTextStyleProbe.LooksBold(limpio, fuente);
                fragmento.Cursiva = PdfTextStyleProbe.LooksItalic(limpio, fuente);
                fragmento.Incrustada = PdfTextStyleProbe.IsEmbedded(fuente);
                fragmento.Subconjunto = PdfTextStyleProbe.IsSubset(bruto);
                fragmento.Color = PdfTextStyleProbe.ToColor(
                    renderInfo.GetFillColor());
                fragmentos.Add(fragmento);
            }

            /// <summary>
            /// Agrupa los fragmentos en lineas: misma linea base y sin saltos
            /// horizontales grandes, que delatan otra columna.
            /// </summary>
            public IList<PdfTextBlock> Construir(int pageNumber)
            {
                var bloques = new List<PdfTextBlock>();
                if (fragmentos.Count == 0)
                {
                    return bloques;
                }

                var ordenados = fragmentos
                    .OrderByDescending(f => f.Base)
                    .ThenBy(f => f.Izquierda)
                    .ToList();

                var actual = new List<Fragmento>();
                foreach (var fragmento in ordenados)
                {
                    if (actual.Count == 0)
                    {
                        actual.Add(fragmento);
                        continue;
                    }

                    var anterior = actual[actual.Count - 1];
                    var tolerancia = Math.Max(
                        0.6F,
                        Math.Max(anterior.Tamano, fragmento.Tamano) *
                            BaselineToleranceRatio);
                    var mismaLinea =
                        Math.Abs(fragmento.Base - anterior.Base) <= tolerancia;
                    var hueco = fragmento.Izquierda - anterior.Derecha;
                    var espacio = Math.Max(1F, anterior.AnchoEspacio);
                    var seguido = hueco <= espacio * ColumnGapInSpaces;

                    if (mismaLinea && seguido)
                    {
                        actual.Add(fragmento);
                        continue;
                    }

                    AgregarBloque(bloques, actual, pageNumber);
                    actual = new List<Fragmento> { fragmento };
                }

                AgregarBloque(bloques, actual, pageNumber);
                return bloques;
            }

            private static void AgregarBloque(
                List<PdfTextBlock> bloques,
                List<Fragmento> linea,
                int pageNumber)
            {
                if (linea.Count == 0)
                {
                    return;
                }

                var texto = new StringBuilder();
                foreach (var fragmento in linea)
                {
                    // Se reconstruyen los espacios que el PDF expresa como
                    // saltos de posicion en vez de como caracteres.
                    if (texto.Length > 0)
                    {
                        var anterior = linea[linea.IndexOf(fragmento) - 1];
                        var hueco = fragmento.Izquierda - anterior.Derecha;
                        if (hueco > Math.Max(0.6F, anterior.AnchoEspacio * 0.45F) &&
                            !texto.ToString().EndsWith(" ", StringComparison.Ordinal) &&
                            !fragmento.Texto.StartsWith(" ", StringComparison.Ordinal))
                        {
                            texto.Append(' ');
                        }
                    }

                    texto.Append(fragmento.Texto);
                }

                var contenido = texto.ToString();
                if (contenido.Trim().Length == 0)
                {
                    return;
                }

                var izquierda = linea.Min(f => f.Izquierda);
                var derecha = linea.Max(f => f.Derecha);
                var abajo = linea.Min(f => f.Abajo);
                var arriba = linea.Max(f => f.Arriba);
                if (derecha - izquierda < 0.5F || arriba - abajo < 0.5F)
                {
                    return;
                }

                // El estilo del bloque es el del fragmento que mas texto aporta.
                var dominante = linea
                    .OrderByDescending(f => f.Texto.Trim().Length)
                    .First();
                var total = linea.Sum(f => f.Texto.Trim().Length);
                var mezcla = linea.Any(f =>
                    !string.Equals(f.Fuente, dominante.Fuente, StringComparison.Ordinal) ||
                    f.Negrita != dominante.Negrita ||
                    f.Cursiva != dominante.Cursiva ||
                    Math.Abs(f.Tamano - dominante.Tamano) > 0.5F);

                var estilo = new PdfTextStyle(
                    dominante.Fuente,
                    dominante.FuenteBruta,
                    (float)Math.Round(dominante.Tamano, 2),
                    dominante.Color,
                    dominante.Negrita,
                    dominante.Cursiva,
                    dominante.Incrustada,
                    dominante.Subconjunto,
                    mezcla,
                    total);

                bloques.Add(new PdfTextBlock(
                    pageNumber,
                    contenido,
                    new RectangleF(
                        izquierda,
                        abajo,
                        derecha - izquierda,
                        arriba - abajo),
                    dominante.Base,
                    estilo));
            }
        }
    }
}

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
        private readonly List<RectangleF> characterBounds =
            new List<RectangleF>();

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

        /// <summary>
        /// La linea es la capa de texto invisible que deja el OCR sobre una
        /// imagen escaneada. Se puede seleccionar y subrayar, pero cambiar su
        /// texto no cambiaria lo que se ve: lo visible es la imagen.
        /// </summary>
        public bool FromOcr { get; internal set; }

        /// <summary>
        /// Rectangulo de cada caracter de la linea, en el orden en que se leen.
        /// Lo usa el subrayador para marcar exactamente el texto que se
        /// arrastra, en vez de un recuadro suelto.
        /// </summary>
        public IList<RectangleF> CharacterBounds
        {
            get { return characterBounds; }
        }

        /// <summary>Indice del caracter mas cercano a una x de la linea.</summary>
        public int NearestCharacterIndex(float x)
        {
            if (characterBounds.Count == 0)
            {
                return 0;
            }

            for (var i = 0; i < characterBounds.Count; i++)
            {
                var caja = characterBounds[i];
                if (x <= caja.Left + (caja.Width / 2F))
                {
                    return i;
                }
            }

            return characterBounds.Count;
        }

        /// <summary>
        /// Rectangulo que envuelve un tramo de caracteres. Es lo que se subraya
        /// de una linea.
        /// </summary>
        public RectangleF SpanBounds(int desde, int hasta)
        {
            var inicio = Math.Max(0, Math.Min(desde, hasta));
            var fin = Math.Min(characterBounds.Count, Math.Max(desde, hasta));
            if (inicio >= fin)
            {
                return RectangleF.Empty;
            }

            var izquierda = float.MaxValue;
            var derecha = float.MinValue;
            for (var i = inicio; i < fin; i++)
            {
                izquierda = Math.Min(izquierda, characterBounds[i].Left);
                derecha = Math.Max(derecha, characterBounds[i].Right);
            }

            if (derecha <= izquierda)
            {
                return RectangleF.Empty;
            }

            return new RectangleF(
                izquierda,
                Bounds.Top,
                derecha - izquierda,
                Bounds.Height);
        }

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

        // Tope de altura de un renglon, en puntos. Una pulgada es de sobra para
        // el titulo mas grande, y descarta las metricas disparatadas que a veces
        // declara la capa invisible del OCR.
        private const float MaximumLineHeightPoints = 72F;

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
            public readonly List<RectangleF> Caracteres = new List<RectangleF>();
            public bool Invisible;
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
                if (renderInfo == null)
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
                fragmento.Tamano = PdfTextStyleProbe.MeasureFontSize(renderInfo);

                // El alto de la linea sale de las lineas de ascendente y
                // descendente, pero la capa invisible que deja el OCR declara a
                // veces metricas desmesuradas: cajas de varios centimetros para
                // una letra. Creerselas producia recuadros tan altos como la
                // pagina, y el subrayador pintaba franjas verticales en vez de
                // seguir al renglon.
                //
                // Cuando el alto declarado no guarda relacion con el cuerpo de
                // letra medido, se reconstruye alrededor de la linea base con
                // las proporciones normales de una tipografia.
                var arriba = Math.Max(
                    ascendente[Vector.I2],
                    descendente[Vector.I2]);
                var abajo = Math.Min(
                    ascendente[Vector.I2],
                    descendente[Vector.I2]);
                // El cuerpo medido sale de esas mismas metricas, asi que puede
                // venir igual de inflado: se acota antes de usarlo. Ningun
                // renglon de un documento pasa de una pulgada de alto.
                var cuerpo = fragmento.Tamano;
                if (cuerpo <= 0.5F || cuerpo > MaximumLineHeightPoints)
                {
                    cuerpo = 11F;
                }

                if (arriba - abajo > cuerpo * 3F ||
                    arriba - abajo < cuerpo * 0.3F ||
                    arriba - abajo > MaximumLineHeightPoints)
                {
                    arriba = fragmento.Base + (cuerpo * 0.78F);
                    abajo = fragmento.Base - (cuerpo * 0.22F);
                    fragmento.Tamano = cuerpo;
                }

                fragmento.Arriba = arriba;
                fragmento.Abajo = abajo;
                fragmento.AnchoEspacio = renderInfo.GetSingleSpaceWidth();
                fragmento.FuenteBruta = bruto;
                fragmento.Fuente = PdfTextStyleProbe.CleanFamilyName(limpio);
                fragmento.Negrita = PdfTextStyleProbe.LooksBold(limpio, fuente);
                fragmento.Cursiva = PdfTextStyleProbe.LooksItalic(limpio, fuente);
                fragmento.Incrustada = PdfTextStyleProbe.IsEmbedded(fuente);
                fragmento.Subconjunto = PdfTextStyleProbe.IsSubset(bruto);
                fragmento.Color = PdfTextStyleProbe.ToColor(
                    renderInfo.GetFillColor());
                // El OCR deja el texto en modo invisible, debajo de la imagen
                // escaneada. Antes se descartaba y por eso, tras pasar el OCR,
                // la herramienta no encontraba nada que subrayar ni editar.
                fragmento.Invisible =
                    renderInfo.GetTextRenderMode() == InvisibleRenderMode;
                RecogerCaracteres(renderInfo, fragmento);
                fragmentos.Add(fragmento);
            }

            /// <summary>
            /// Rectangulo de cada caracter del fragmento. iText los da uno a
            /// uno, y es lo que permite seleccionar texto por la mitad de una
            /// palabra.
            /// </summary>
            private static void RecogerCaracteres(
                TextRenderInfo renderInfo,
                Fragmento fragmento)
            {
                try
                {
                    foreach (var caracter in renderInfo.GetCharacterRenderInfos())
                    {
                        var linea = caracter.GetBaseline();
                        var a = linea.GetStartPoint();
                        var b = linea.GetEndPoint();
                        var arriba = caracter.GetAscentLine().GetStartPoint();
                        var abajo = caracter.GetDescentLine().GetStartPoint();

                        var izquierda = Math.Min(a[Vector.I1], b[Vector.I1]);
                        var derecha = Math.Max(a[Vector.I1], b[Vector.I1]);
                        if (derecha - izquierda <= 0F)
                        {
                            // Un caracter sin avance debe ocupar sitio igual,
                            // para que los indices sigan cuadrando con el texto.
                            derecha = izquierda + 0.01F;
                        }

                        var alto = Math.Abs(arriba[Vector.I2] - abajo[Vector.I2]);
                        fragmento.Caracteres.Add(new RectangleF(
                            izquierda,
                            Math.Min(arriba[Vector.I2], abajo[Vector.I2]),
                            derecha - izquierda,
                            alto));
                    }
                }
                catch (Exception)
                {
                    fragmento.Caracteres.Clear();
                }
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

                var cajas = ReunirCaracteres(linea);

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

                var bloque = new PdfTextBlock(
                    pageNumber,
                    contenido,
                    new RectangleF(
                        izquierda,
                        abajo,
                        derecha - izquierda,
                        arriba - abajo),
                    dominante.Base,
                    estilo);
                bloque.FromOcr = linea.All(f => f.Invisible);
                foreach (var caja in cajas)
                {
                    bloque.CharacterBounds.Add(caja);
                }

                bloques.Add(bloque);
            }

            /// <summary>
            /// Rectangulos de la linea en el mismo orden que su texto,
            /// incluidos los espacios que el PDF expresa como saltos de
            /// posicion y no como caracteres.
            /// </summary>
            private static List<RectangleF> ReunirCaracteres(
                List<Fragmento> linea)
            {
                var cajas = new List<RectangleF>();
                for (var indice = 0; indice < linea.Count; indice++)
                {
                    var fragmento = linea[indice];
                    if (indice > 0)
                    {
                        var anterior = linea[indice - 1];
                        var hueco = fragmento.Izquierda - anterior.Derecha;
                        if (hueco > Math.Max(0.6F, anterior.AnchoEspacio * 0.45F) &&
                            !anterior.Texto.EndsWith(" ", StringComparison.Ordinal) &&
                            !fragmento.Texto.StartsWith(" ", StringComparison.Ordinal))
                        {
                            cajas.Add(new RectangleF(
                                anterior.Derecha,
                                Math.Min(anterior.Abajo, fragmento.Abajo),
                                Math.Max(0.01F, hueco),
                                Math.Max(anterior.Arriba, fragmento.Arriba) -
                                    Math.Min(anterior.Abajo, fragmento.Abajo)));
                        }
                    }

                    if (fragmento.Caracteres.Count == fragmento.Texto.Length)
                    {
                        cajas.AddRange(fragmento.Caracteres);
                        continue;
                    }

                    // Si iText no dio un rectangulo por caracter, se reparte el
                    // ancho del fragmento: basta de sobra para subrayar.
                    var ancho = fragmento.Texto.Length == 0
                        ? 0F
                        : (fragmento.Derecha - fragmento.Izquierda) /
                            fragmento.Texto.Length;
                    for (var i = 0; i < fragmento.Texto.Length; i++)
                    {
                        cajas.Add(new RectangleF(
                            fragmento.Izquierda + (ancho * i),
                            fragmento.Abajo,
                            Math.Max(0.01F, ancho),
                            fragmento.Arriba - fragmento.Abajo));
                    }
                }

                return cajas;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using iTextSharp.text;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
using PdfRectangle = iTextSharp.text.Rectangle;

namespace FirmaAutomatica
{
    /// <summary>
    /// Tipografia detectada en una seleccion: la fuente real del PDF, su
    /// tamano, su color y su estilo.
    /// </summary>
    internal sealed class PdfTextStyle
    {
        internal PdfTextStyle(
            string fontName,
            string rawFontName,
            float fontSizePoints,
            Color color,
            bool bold,
            bool italic,
            bool embedded,
            bool subset,
            bool mixed,
            int sampledCharacters)
        {
            FontName = fontName ?? string.Empty;
            RawFontName = rawFontName ?? string.Empty;
            FontSizePoints = fontSizePoints;
            Color = color;
            Bold = bold;
            Italic = italic;
            Embedded = embedded;
            Subset = subset;
            Mixed = mixed;
            SampledCharacters = sampledCharacters;
        }

        /// <summary>Nombre limpio, sin prefijo de subconjunto ni sufijo de estilo.</summary>
        public string FontName { get; private set; }

        /// <summary>Nombre tal cual aparece en el PDF, con prefijo si lo tiene.</summary>
        public string RawFontName { get; private set; }

        public float FontSizePoints { get; private set; }

        public Color Color { get; private set; }

        public bool Bold { get; private set; }

        public bool Italic { get; private set; }

        /// <summary>La fuente viaja incrustada en el PDF.</summary>
        public bool Embedded { get; private set; }

        /// <summary>
        /// La fuente incrustada es un subconjunto: solo trae los glifos que el
        /// documento ya usaba, asi que NO sirve para escribir texto nuevo.
        /// </summary>
        public bool Subset { get; private set; }

        /// <summary>La seleccion mezclaba varias tipografias.</summary>
        public bool Mixed { get; private set; }

        public int SampledCharacters { get; private set; }

        /// <summary>Resumen legible para enseñar en el dialogo.</summary>
        public string Describe()
        {
            var texto = new StringBuilder();
            texto.Append(
                string.IsNullOrEmpty(FontName) ? "desconocida" : FontName);
            texto.Append("  ");
            texto.Append(
                FontSizePoints.ToString("0.#", CultureInfo.CurrentCulture));
            texto.Append(" pt");

            if (Bold)
            {
                texto.Append(" - negrita");
            }
            if (Italic)
            {
                texto.Append(" - cursiva");
            }
            if (!IsBlack(Color))
            {
                texto.Append(" - ");
                texto.Append(DescribeColor(Color));
            }
            if (Mixed)
            {
                texto.Append("   (la seleccion mezcla estilos)");
            }

            return texto.ToString();
        }

        private static bool IsBlack(Color color)
        {
            return color.R < 16 && color.G < 16 && color.B < 16;
        }

        private static string DescribeColor(Color color)
        {
            if (color.R == color.G && color.G == color.B)
            {
                var porcentaje = (int)Math.Round(
                    (255 - color.R) * 100D / 255D);
                return "gris " +
                    porcentaje.ToString(CultureInfo.CurrentCulture) + "%";
            }

            return "RGB " +
                color.R.ToString(CultureInfo.CurrentCulture) + "," +
                color.G.ToString(CultureInfo.CurrentCulture) + "," +
                color.B.ToString(CultureInfo.CurrentCulture);
        }
    }

    /// <summary>
    /// Lee la tipografia real del texto que cae dentro de una seleccion.
    ///
    /// Existe porque hasta ahora la edicion visual solo extraia una cadena y la
    /// persona tenia que elegir la fuente a mano entre tres genericas.
    ///
    /// iText no expone el tamano de fuente en TextRenderInfo, asi que se deduce
    /// de la separacion entre las lineas de ascendente y descendente: ya vienen
    /// transformadas al espacio de usuario, de modo que el calculo vale tambien
    /// en paginas giradas.
    /// </summary>
    internal sealed class PdfTextStyleProbe : IRenderListener
    {
        // Los subconjuntos que incrustan Word y compania llevan seis letras
        // mayusculas y un mas: "ABCDEF+Calibri".
        private static readonly Regex SubsetPrefix =
            new Regex("^[A-Z]{6}\\+", RegexOptions.CultureInvariant);

        // Modo de renderizado 3: texto invisible. Es lo que deja el OCR debajo
        // de la imagen escaneada; su tipografia no es la que se ve.
        private const int InvisibleRenderMode = 3;

        // Sufijos de estilo y de fundicion que llevan los nombres PostScript.
        // Sin quitarlos, Arial se llama "ArialMT" y Times New Roman
        // "TimesNewRomanPSMT", que ni se enseñan bien ni sirven para buscar el
        // archivo de la fuente en Windows.
        private static readonly string[] StyleSuffixes = new[]
        {
            "SemiBold", "Bold", "Italic", "Oblique", "Regular",
            "Light", "Medium", "Black", "Heavy",
            "MT", "PS", "Std", "Pro"
        };

        // Longitud minima que debe quedar tras recortar, para no dejar el
        // nombre en nada al encadenar recortes.
        private const int MinimumFamilyLength = 3;

        private readonly PdfRectangle region;
        private readonly List<Fragmento> visibles = new List<Fragmento>();
        private readonly List<Fragmento> invisibles = new List<Fragmento>();

        private PdfTextStyleProbe(PdfRectangle region)
        {
            if (region == null)
            {
                throw new ArgumentNullException("region");
            }

            this.region = region;
        }

        /// <summary>
        /// Devuelve el estilo dominante de la seleccion, o null si no hay texto.
        ///
        /// No propaga excepciones a proposito: detectar la tipografia es una
        /// mejora, no un requisito, y un fallo aqui no debe impedir editar.
        /// </summary>
        public static PdfTextStyle Detect(
            PdfReader reader,
            PdfTextEditRegion region)
        {
            if (reader == null)
            {
                throw new ArgumentNullException("reader");
            }
            if (region == null)
            {
                throw new ArgumentNullException("region");
            }

            try
            {
                var transform = PdfTextPageTransform.Create(
                    reader,
                    region.PageNumber);
                var probe = new PdfTextStyleProbe(
                    transform.GetRawRectangle(region));
                var parser = new PdfReaderContentParser(reader);
                parser.ProcessContent(region.PageNumber, probe);
                return probe.ResolveDominant();
            }
            catch (Exception)
            {
                return null;
            }
        }

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
            if (string.IsNullOrEmpty(texto) || texto.Trim().Length == 0)
            {
                return;
            }
            if (!IsInsideRegion(renderInfo))
            {
                return;
            }

            var fragmento = Describe(renderInfo, texto);
            if (fragmento == null)
            {
                return;
            }

            if (renderInfo.GetTextRenderMode() == InvisibleRenderMode)
            {
                invisibles.Add(fragmento);
            }
            else
            {
                visibles.Add(fragmento);
            }
        }

        private bool IsInsideRegion(TextRenderInfo renderInfo)
        {
            var baseline = renderInfo.GetBaseline();
            var inicio = baseline.GetStartPoint();
            var fin = baseline.GetEndPoint();
            var x = (inicio[Vector.I1] + fin[Vector.I1]) / 2F;
            var y = (inicio[Vector.I2] + fin[Vector.I2]) / 2F;

            return x >= region.Left &&
                x <= region.Right &&
                y >= region.Bottom &&
                y <= region.Top;
        }

        private static Fragmento Describe(
            TextRenderInfo renderInfo,
            string texto)
        {
            var font = renderInfo.GetFont();
            if (font == null)
            {
                return null;
            }

            var bruto = font.PostscriptFontName ?? string.Empty;
            var subconjunto = SubsetPrefix.IsMatch(bruto);
            var limpio = SubsetPrefix.Replace(bruto, string.Empty);

            var fragmento = new Fragmento();
            fragmento.Longitud = texto.Trim().Length;
            fragmento.RawFontName = bruto;
            fragmento.FontName = CleanFamilyName(limpio);
            fragmento.Subset = subconjunto;
            fragmento.Embedded = IsEmbedded(font);
            fragmento.Bold = LooksBold(limpio, font);
            fragmento.Italic = LooksItalic(limpio, font);
            fragmento.Size = MeasureFontSize(renderInfo, font);
            fragmento.Color = ToColor(renderInfo.GetFillColor());
            return fragmento;
        }

        /// <summary>
        /// TextRenderInfo no da el tamano de fuente, asi que se calcula: la
        /// distancia entre la linea de ascendente y la de descendente ya viene
        /// escalada por la matriz de texto, y el descriptor de la fuente dice
        /// cuanto mide esa distancia para un cuerpo de 1 punto.
        /// </summary>
        private static float MeasureFontSize(
            TextRenderInfo renderInfo,
            DocumentFont font)
        {
            var ascendente = renderInfo.GetAscentLine().GetStartPoint();
            var descendente = renderInfo.GetDescentLine().GetStartPoint();
            var alto = ascendente.Subtract(descendente).Length;
            if (alto <= 0.01F)
            {
                return 0F;
            }

            var porUnidad = 0F;
            try
            {
                porUnidad =
                    font.GetFontDescriptor(BaseFont.ASCENT, 1F) -
                    font.GetFontDescriptor(BaseFont.DESCENT, 1F);
            }
            catch (Exception)
            {
                porUnidad = 0F;
            }

            // Sin descriptor fiable se recurre a la proporcion habitual entre el
            // alto ascendente-descendente y el cuerpo de la letra.
            if (porUnidad < 0.5F || porUnidad > 2.5F)
            {
                porUnidad = 1.2F;
            }

            return alto / porUnidad;
        }

        private static bool IsEmbedded(DocumentFont font)
        {
            try
            {
                var diccionario = font.FontDictionary;
                if (diccionario == null)
                {
                    return false;
                }

                var descriptor = FindDescriptor(diccionario);
                if (descriptor == null)
                {
                    return false;
                }

                return descriptor.Get(PdfName.FONTFILE) != null ||
                    descriptor.Get(PdfName.FONTFILE2) != null ||
                    descriptor.Get(PdfName.FONTFILE3) != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// En las fuentes Type0 el descriptor no cuelga del diccionario de la
        /// fuente, sino de su fuente descendiente.
        /// </summary>
        private static PdfDictionary FindDescriptor(PdfDictionary fontDictionary)
        {
            if (fontDictionary == null)
            {
                return null;
            }

            var directo = fontDictionary.GetAsDict(PdfName.FONTDESCRIPTOR);
            if (directo != null)
            {
                return directo;
            }

            var descendientes = fontDictionary.GetAsArray(
                PdfName.DESCENDANTFONTS);
            if (descendientes == null || descendientes.Size == 0)
            {
                return null;
            }

            var primero = descendientes.GetAsDict(0);
            if (primero == null)
            {
                return null;
            }

            return primero.GetAsDict(PdfName.FONTDESCRIPTOR);
        }

        private static bool LooksBold(string nombre, DocumentFont font)
        {
            if (ContainsToken(nombre, "bold") ||
                ContainsToken(nombre, "black") ||
                ContainsToken(nombre, "heavy") ||
                ContainsToken(nombre, "semibold"))
            {
                return true;
            }

            try
            {
                // Un StemV alto delata el trazo grueso. 120 es el umbral que en
                // la practica separa una regular de una negrita.
                var descriptor = FindDescriptor(font.FontDictionary);
                if (descriptor != null)
                {
                    var stem = descriptor.GetAsNumber(PdfName.STEMV);
                    if (stem != null && stem.FloatValue >= 120F)
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
            }

            return false;
        }

        private static bool LooksItalic(string nombre, DocumentFont font)
        {
            if (ContainsToken(nombre, "italic") ||
                ContainsToken(nombre, "oblique"))
            {
                return true;
            }

            try
            {
                return Math.Abs(
                    font.GetFontDescriptor(BaseFont.ITALICANGLE, 1F)) > 0.5F;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool ContainsToken(string nombre, string token)
        {
            return nombre != null &&
                nombre.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Deja el nombre de familia: quita el sufijo de estilo que sigue a la
        /// coma o al guion, que es como los PDF escriben "Calibri-Bold" o
        /// "Arial,BoldItalic".
        /// </summary>
        internal static string CleanFamilyName(string nombre)
        {
            if (string.IsNullOrEmpty(nombre))
            {
                return string.Empty;
            }

            var corte = nombre.IndexOfAny(new[] { ',', '-' });
            var familia = corte > 0 ? nombre.Substring(0, corte) : nombre;

            var recortado = true;
            while (recortado)
            {
                recortado = false;
                foreach (var sufijo in StyleSuffixes)
                {
                    if (familia.Length - sufijo.Length >= MinimumFamilyLength &&
                        familia.EndsWith(
                            sufijo,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        familia = familia.Substring(
                            0,
                            familia.Length - sufijo.Length);
                        recortado = true;
                    }
                }
            }

            return familia.Trim();
        }

        private static Color ToColor(BaseColor color)
        {
            if (color == null)
            {
                return Color.Black;
            }

            return Color.FromArgb(color.R, color.G, color.B);
        }

        /// <summary>
        /// Agrupa por estilo y devuelve el que mas caracteres cubre. Se pesa por
        /// numero de caracteres y no de fragmentos porque un titulo suelto no
        /// debe ganarle a un parrafo entero.
        /// </summary>
        private PdfTextStyle ResolveDominant()
        {
            var muestras = visibles.Count > 0 ? visibles : invisibles;
            if (muestras.Count == 0)
            {
                return null;
            }

            var grupos = muestras
                .GroupBy(f => new
                {
                    f.FontName,
                    f.Bold,
                    f.Italic,
                    Tamano = (int)Math.Round(f.Size * 10D),
                    Color = f.Color.ToArgb()
                })
                .Select(g => new
                {
                    Peso = g.Sum(f => f.Longitud),
                    Muestra = g.First(),
                    Tamano = g.Average(f => (double)f.Size)
                })
                .OrderByDescending(g => g.Peso)
                .ToList();

            var ganador = grupos[0];
            var total = muestras.Sum(f => f.Longitud);

            return new PdfTextStyle(
                ganador.Muestra.FontName,
                ganador.Muestra.RawFontName,
                (float)Math.Round(ganador.Tamano, 2),
                ganador.Muestra.Color,
                ganador.Muestra.Bold,
                ganador.Muestra.Italic,
                ganador.Muestra.Embedded,
                ganador.Muestra.Subset,
                ganador.Peso < total,
                total);
        }

        private sealed class Fragmento
        {
            public int Longitud;
            public string FontName;
            public string RawFontName;
            public bool Subset;
            public bool Embedded;
            public bool Bold;
            public bool Italic;
            public float Size;
            public Color Color;
        }
    }
}

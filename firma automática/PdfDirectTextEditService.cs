using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
using PdfRectangle = iTextSharp.text.Rectangle;

namespace FirmaAutomatica
{
    /// <summary>Como se puede sustituir el texto de una seleccion.</summary>
    internal enum PdfDirectEditMode
    {
        /// <summary>No se puede: hay que recurrir a cubrir y escribir encima.</summary>
        NotPossible,

        /// <summary>
        /// La fuente incrustada tiene todos los glifos del texto nuevo, asi que
        /// se sustituye la cadena en el sitio y no cambia nada mas.
        /// </summary>
        InPlace,

        /// <summary>
        /// La fuente incrustada no llega, asi que se borra el texto antiguo del
        /// flujo y el nuevo se escribe con una fuente completa del sistema.
        /// </summary>
        RewriteWithSystemFont
    }

    /// <summary>Lo que se puede hacer con una seleccion concreta.</summary>
    internal sealed class PdfDirectTextCapability
    {
        internal PdfDirectTextCapability(
            PdfDirectEditMode mode,
            string reason,
            string originalText,
            string missingCharacters)
        {
            Mode = mode;
            Reason = reason ?? string.Empty;
            OriginalText = originalText ?? string.Empty;
            MissingCharacters = missingCharacters ?? string.Empty;
        }

        public PdfDirectEditMode Mode { get; private set; }

        /// <summary>Por que no se puede, o con que salvedad se puede.</summary>
        public string Reason { get; private set; }

        public string OriginalText { get; private set; }

        /// <summary>Caracteres que la fuente incrustada no tiene.</summary>
        public string MissingCharacters { get; private set; }

        public bool CanReplace
        {
            get { return Mode != PdfDirectEditMode.NotPossible; }
        }
    }

    /// <summary>
    /// Sustituye de verdad el texto de un PDF, en vez de taparlo.
    ///
    /// COMO FUNCIONA
    ///
    /// El flujo de contenido de una pagina se recorre dos veces. La primera con
    /// el procesador de iText, que dice que texto dibuja cada fragmento y donde;
    /// la segunda troceando el flujo en operadores. Ambos recorridos visitan los
    /// fragmentos en el mismo orden, asi que se casan por indice y se sabe que
    /// cadena hay que tocar. Si los dos recuentos no coinciden —porque el texto
    /// venga dentro de un XObject, por ejemplo— se rechaza la operacion en vez
    /// de arriesgarse.
    ///
    /// EL LIMITE REAL: LOS SUBCONJUNTOS
    ///
    /// Word y compania incrustan las fuentes en subconjuntos que solo llevan los
    /// glifos que el documento ya usaba. Medido en un PDF de Word corriente, la
    /// fuente del cuerpo no tenia ni los digitos 0 y 2 a 9. Escribir con ella un
    /// numero nuevo daria huecos en blanco.
    ///
    /// Por eso hay dos vias, y las dos borran el texto anterior de verdad:
    ///
    ///   InPlace                si la fuente incrustada cubre el texto nuevo, se
    ///                          sustituye la cadena y no cambia nada mas;
    ///   RewriteWithSystemFont  si no llega, se borra el texto del flujo y el
    ///                          nuevo se escribe con una fuente completa del
    ///                          sistema, en el mismo sitio y tamano.
    /// </summary>
    internal static class PdfDirectTextEditService
    {
        public const string NoTextMessage =
            "La zona seleccionada no contiene texto que se pueda sustituir.";

        public const string UnsupportedLayoutMessage =
            "El texto de esta pagina no esta escrito directamente en ella " +
            "(puede venir de un objeto reutilizado), y sustituirlo podria " +
            "dañar el documento.";

        /// <summary>
        /// Estudia que se puede hacer con la seleccion y el texto propuesto.
        /// No modifica nada.
        /// </summary>
        public static PdfDirectTextCapability Analyze(
            PdfReader reader,
            PdfTextEditRegion region,
            string replacementText)
        {
            if (reader == null)
            {
                throw new ArgumentNullException("reader");
            }
            if (region == null)
            {
                throw new ArgumentNullException("region");
            }

            Seleccion seleccion;
            try
            {
                seleccion = Localizar(reader, region);
            }
            catch (Exception ex)
            {
                return new PdfDirectTextCapability(
                    PdfDirectEditMode.NotPossible,
                    ex.GetBaseException().Message,
                    string.Empty,
                    string.Empty);
            }

            if (seleccion == null || seleccion.Indices.Count == 0)
            {
                return new PdfDirectTextCapability(
                    PdfDirectEditMode.NotPossible,
                    NoTextMessage,
                    string.Empty,
                    string.Empty);
            }

            if (!seleccion.RecuentosCoinciden)
            {
                return new PdfDirectTextCapability(
                    PdfDirectEditMode.NotPossible,
                    UnsupportedLayoutMessage,
                    seleccion.Texto,
                    string.Empty);
            }

            var faltan = GlifosQueFaltan(seleccion.Fuente, replacementText);
            if (faltan.Length == 0)
            {
                return new PdfDirectTextCapability(
                    PdfDirectEditMode.InPlace,
                    "Se sustituye el texto conservando su misma fuente " +
                    "incrustada.",
                    seleccion.Texto,
                    string.Empty);
            }

            return new PdfDirectTextCapability(
                PdfDirectEditMode.RewriteWithSystemFont,
                "La fuente incrustada en el PDF no trae todos los caracteres " +
                "nuevos, asi que el texto se reescribe con la misma fuente " +
                "instalada en Windows.",
                seleccion.Texto,
                faltan);
        }

        /// <summary>
        /// Sustituye el texto y devuelve el modo que se acabo usando.
        ///
        /// El PdfStamper debe venir en modo incremental, igual que en el resto
        /// de la aplicacion. El texto nuevo se escribe aqui solo en el modo
        /// RewriteWithSystemFont; en el otro basta con cambiar la cadena.
        /// </summary>
        public static PdfDirectEditMode Apply(
            PdfReader reader,
            PdfStamper stamper,
            PdfTextEditRegion region,
            PdfTextReplacement replacement,
            PdfTextPageTransform transform,
            out string appliedFontName)
        {
            appliedFontName = string.Empty;
            var seleccion = Localizar(reader, region);
            if (seleccion == null || seleccion.Indices.Count == 0)
            {
                throw new InvalidOperationException(NoTextMessage);
            }
            if (!seleccion.RecuentosCoinciden)
            {
                throw new InvalidOperationException(UnsupportedLayoutMessage);
            }

            var texto = replacement.ReplacementText ?? string.Empty;
            var faltan = GlifosQueFaltan(seleccion.Fuente, texto);
            var enSitio = faltan.Length == 0 && !replacement.ForceSystemFont;

            byte[] nuevaCadena = null;
            if (enSitio && texto.Length > 0)
            {
                nuevaCadena = seleccion.Fuente.ConvertToBytes(texto);
            }

            var contenido = Reescribir(
                reader,
                region.PageNumber,
                seleccion.Indices,
                seleccion.PrimerIndice,
                nuevaCadena);
            reader.SetPageContent(region.PageNumber, contenido);

            if (enSitio)
            {
                appliedFontName = seleccion.Fuente.PostscriptFontName;
                return PdfDirectEditMode.InPlace;
            }

            // El texto antiguo ya no esta; el nuevo se dibuja con una fuente
            // completa, en la linea base y el tamano que tenia el original.
            EscribirConFuenteDelSistema(
                stamper,
                region,
                replacement,
                seleccion,
                transform,
                out appliedFontName);
            return PdfDirectEditMode.RewriteWithSystemFont;
        }

        private static void EscribirConFuenteDelSistema(
            PdfStamper stamper,
            PdfTextEditRegion region,
            PdfTextReplacement replacement,
            Seleccion seleccion,
            PdfTextPageTransform transform,
            out string appliedFontName)
        {
            var texto = replacement.ReplacementText ?? string.Empty;
            var resolved = PdfUnicodeFontResolver.Create(
                replacement.FontFamily,
                replacement.Bold,
                replacement.Italic,
                texto,
                replacement.PreferredFontName);
            appliedFontName = resolved.DisplayName;

            var canvas = stamper.GetOverContent(region.PageNumber);
            canvas.SaveState();
            try
            {
                canvas.SetColorFill(
                    new iTextSharp.text.BaseColor(
                        replacement.TextColor.R,
                        replacement.TextColor.G,
                        replacement.TextColor.B));
                // El tamano pedido manda; el del original solo es el
                // respaldo cuando no se indica ninguno.
                var tamano = replacement.FontSizePoints > 0.5F
                    ? replacement.FontSizePoints
                    : seleccion.TamanoFuente;
                canvas.BeginText();
                canvas.SetFontAndSize(resolved.BaseFont, tamano);
                canvas.SetTextMatrix(seleccion.OrigenX, seleccion.OrigenY);
                canvas.ShowText(texto);
                canvas.EndText();
            }
            finally
            {
                canvas.RestoreState();
            }
        }

        /// <summary>
        /// Recorre el flujo y devuelve uno nuevo con las cadenas sustituidas.
        /// Todo lo que no sea una de esas cadenas se vuelve a escribir igual.
        /// </summary>
        private static byte[] Reescribir(
            PdfReader reader,
            int pageNumber,
            HashSet<int> indices,
            int indicePrimero,
            byte[] nuevaCadena)
        {
            var contenido = reader.GetPageContent(pageNumber);
            var parser = CrearParser(contenido);
            var salida = new MemoryStream(contenido.Length + 256);
            var operandos = new List<PdfObject>();
            var chunk = 0;

            while (parser.Parse(operandos).Count > 0)
            {
                var operador = operandos[operandos.Count - 1].ToString();

                if (operador == "Tj" || operador == "'" || operador == "\"")
                {
                    var posicion = operandos.Count - 2;
                    var indice = chunk++;
                    if (indices.Contains(indice))
                    {
                        operandos[posicion] = new PdfString(
                            indice == indicePrimero && nuevaCadena != null
                                ? nuevaCadena
                                : new byte[0]);
                    }
                }
                else if (operador == "TJ")
                {
                    var array = operandos[operandos.Count - 2] as PdfArray;
                    if (array != null)
                    {
                        var reemplazo = new PdfArray();
                        for (var i = 0; i < array.Size; i++)
                        {
                            var elemento = array.GetPdfObject(i);
                            if (!(elemento is PdfString))
                            {
                                // Los numeros son ajustes de separacion. Se
                                // conservan salvo que acompañen a texto borrado.
                                reemplazo.Add(elemento);
                                continue;
                            }

                            var indice = chunk++;
                            if (!indices.Contains(indice))
                            {
                                reemplazo.Add(elemento);
                                continue;
                            }

                            if (indice == indicePrimero && nuevaCadena != null)
                            {
                                reemplazo.Add(new PdfString(nuevaCadena));
                            }
                        }

                        operandos[operandos.Count - 2] = reemplazo;
                    }
                }

                var lista = new List<PdfObject>();
                for (var i = 0; i < operandos.Count - 1; i++)
                {
                    lista.Add(operandos[i]);
                }

                PdfContentStreamWriter.WriteOperation(salida, lista, operador);
            }

            return salida.ToArray();
        }

        private static Seleccion Localizar(
            PdfReader reader,
            PdfTextEditRegion region)
        {
            var transform = PdfTextPageTransform.Create(
                reader,
                region.PageNumber);
            var rectangulo = transform.GetRawRectangle(region);

            var listener = new LocalizadorDeFragmentos(rectangulo);
            var parser = new PdfReaderContentParser(reader);
            parser.ProcessContent(region.PageNumber, listener);

            if (listener.Seleccionados.Count == 0)
            {
                return null;
            }

            var seleccion = new Seleccion();
            seleccion.Indices = new HashSet<int>(listener.Seleccionados);
            seleccion.PrimerIndice = int.MaxValue;
            foreach (var indice in listener.Seleccionados)
            {
                if (indice < seleccion.PrimerIndice)
                {
                    seleccion.PrimerIndice = indice;
                }
            }

            seleccion.Texto = listener.TextoSeleccionado.ToString();
            seleccion.Fuente = listener.PrimeraFuente;
            seleccion.TamanoFuente = listener.PrimerTamano;
            seleccion.OrigenX = listener.PrimerOrigenX;
            seleccion.OrigenY = listener.PrimerOrigenY;
            seleccion.RecuentosCoinciden =
                listener.TotalFragmentos ==
                ContarCadenasEnFlujo(reader, region.PageNumber);

            return seleccion.Fuente == null ? null : seleccion;
        }

        /// <summary>
        /// Cuenta las cadenas que dibujan texto en el flujo de la pagina. Si no
        /// coincide con lo que vio el procesador, hay texto en sitios que este
        /// reescritor no toca, y la operacion se rechaza.
        /// </summary>
        private static int ContarCadenasEnFlujo(PdfReader reader, int pageNumber)
        {
            var parser = CrearParser(reader.GetPageContent(pageNumber));
            var total = 0;
            var operandos = new List<PdfObject>();

            while (parser.Parse(operandos).Count > 0)
            {
                var operador = operandos[operandos.Count - 1].ToString();
                if (operador == "Tj" || operador == "'" || operador == "\"")
                {
                    total++;
                }
                else if (operador == "TJ")
                {
                    var array = operandos[operandos.Count - 2] as PdfArray;
                    if (array != null)
                    {
                        for (var i = 0; i < array.Size; i++)
                        {
                            if (array.GetPdfObject(i) is PdfString)
                            {
                                total++;
                            }
                        }
                    }
                }
            }

            return total;
        }

        private static PdfContentParser CrearParser(byte[] contenido)
        {
            return new PdfContentParser(
                new PRTokeniser(
                    new RandomAccessFileOrArray(
                        new iTextSharp.text.io.RandomAccessSourceFactory()
                            .CreateSource(contenido))));
        }

        /// <summary>
        /// Caracteres del texto nuevo que la fuente incrustada no tiene. Es el
        /// motivo por el que muchas sustituciones no pueden hacerse en el sitio.
        /// </summary>
        private static string GlifosQueFaltan(DocumentFont fuente, string texto)
        {
            if (fuente == null || string.IsNullOrEmpty(texto))
            {
                return string.Empty;
            }

            var faltan = new StringBuilder();
            var vistos = new HashSet<char>();
            foreach (var caracter in texto)
            {
                if (char.IsControl(caracter) || vistos.Contains(caracter))
                {
                    continue;
                }

                vistos.Add(caracter);
                var existe = false;
                try
                {
                    existe = fuente.CharExists(caracter);
                }
                catch (Exception)
                {
                    existe = false;
                }

                if (!existe)
                {
                    faltan.Append(caracter);
                }
            }

            return faltan.ToString();
        }

        private sealed class Seleccion
        {
            public HashSet<int> Indices;
            public int PrimerIndice;
            public string Texto;
            public DocumentFont Fuente;
            public float TamanoFuente;
            public float OrigenX;
            public float OrigenY;
            public bool RecuentosCoinciden;
        }

        private sealed class LocalizadorDeFragmentos : IRenderListener
        {
            private readonly PdfRectangle region;

            public readonly List<int> Seleccionados = new List<int>();
            public readonly StringBuilder TextoSeleccionado = new StringBuilder();
            public DocumentFont PrimeraFuente;
            public float PrimerTamano;
            public float PrimerOrigenX;
            public float PrimerOrigenY;
            public int TotalFragmentos;

            public LocalizadorDeFragmentos(PdfRectangle region)
            {
                this.region = region;
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
                var indice = TotalFragmentos++;
                if (renderInfo == null)
                {
                    return;
                }

                var baseline = renderInfo.GetBaseline();
                var inicio = baseline.GetStartPoint();
                var fin = baseline.GetEndPoint();
                var x = (inicio[Vector.I1] + fin[Vector.I1]) / 2F;
                var y = (inicio[Vector.I2] + fin[Vector.I2]) / 2F;

                if (x < region.Left || x > region.Right ||
                    y < region.Bottom || y > region.Top)
                {
                    return;
                }

                Seleccionados.Add(indice);
                TextoSeleccionado.Append(renderInfo.GetText());

                if (PrimeraFuente == null)
                {
                    PrimeraFuente = renderInfo.GetFont();
                    PrimerOrigenX = inicio[Vector.I1];
                    PrimerOrigenY = inicio[Vector.I2];
                    PrimerTamano = MedirTamano(renderInfo);
                }
            }

            private static float MedirTamano(TextRenderInfo renderInfo)
            {
                var ascendente = renderInfo.GetAscentLine().GetStartPoint();
                var descendente = renderInfo.GetDescentLine().GetStartPoint();
                var alto = ascendente.Subtract(descendente).Length;
                if (alto <= 0.01F)
                {
                    return 11F;
                }

                var porUnidad = 0F;
                try
                {
                    var fuente = renderInfo.GetFont();
                    porUnidad =
                        fuente.GetFontDescriptor(BaseFont.ASCENT, 1F) -
                        fuente.GetFontDescriptor(BaseFont.DESCENT, 1F);
                }
                catch (Exception)
                {
                    porUnidad = 0F;
                }

                if (porUnidad < 0.5F || porUnidad > 2.5F)
                {
                    porUnidad = 1.2F;
                }

                return alto / porUnidad;
            }
        }
    }
}

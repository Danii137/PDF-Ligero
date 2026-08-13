using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;

namespace FirmaAutomatica
{
    /// <summary>
    /// Segunda prueba de viabilidad de la edicion directa.
    ///
    /// Para poder sustituir el texto hay que saber QUE operador del flujo lo
    /// dibuja. La idea es recorrer el flujo dos veces: una con el procesador de
    /// iText, que da el texto y la posicion de cada fragmento, y otra
    /// troceando el flujo en operadores. Si ambas recorren los fragmentos en el
    /// mismo orden, se pueden casar por indice.
    ///
    /// Esto comprueba justo eso: que el numero de fragmentos que ve el
    /// procesador coincide con el numero de cadenas que hay en los operadores
    /// de texto del flujo.
    /// </summary>
    internal static class ViabilidadQa
    {
        private static int fallos;

        private static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("Uso: ViabilidadQa <pdf>");
                return 2;
            }

            var pdf = args[0];
            if (!File.Exists(pdf))
            {
                Console.WriteLine("No existe: " + pdf);
                return 2;
            }

            var reader = new PdfReader(pdf);
            try
            {
                for (var page = 1; page <= reader.NumberOfPages; page++)
                {
                    Comprobar(reader, page);
                }
            }
            finally
            {
                reader.Close();
            }

            Console.WriteLine();
            Console.WriteLine(fallos == 0 ? "RESULTADO: PASS" : "RESULTADO: FALLA");
            return fallos == 0 ? 0 : 1;
        }

        private static void Comprobar(PdfReader reader, int page)
        {
            var listener = new ContadorDeFragmentos();
            var parser = new PdfReaderContentParser(reader);
            parser.ProcessContent(page, listener);

            var enFlujo = ContarCadenasEnFlujo(reader, page);

            var coincide = listener.Fragmentos.Count == enFlujo;
            if (!coincide)
            {
                fallos++;
            }

            Console.WriteLine(
                "Pagina {0}: procesador {1} fragmentos, flujo {2} cadenas  {3}",
                page,
                listener.Fragmentos.Count,
                enFlujo,
                coincide ? "OK" : "NO COINCIDE");

            if (listener.Fragmentos.Count > 0)
            {
                var muestra = listener.Fragmentos[0];
                Console.WriteLine(
                    "   primer fragmento: '{0}'",
                    muestra.Length > 40 ? muestra.Substring(0, 40) + "..." : muestra);
            }
        }

        /// <summary>
        /// Cuenta las cadenas que dibujan texto: el operando de Tj, ' y ", y
        /// cada cadena dentro del array de TJ.
        /// </summary>
        private static int ContarCadenasEnFlujo(PdfReader reader, int page)
        {
            var contenido = reader.GetPageContent(page);
            var parser = new PdfContentParser(
                new PRTokeniser(
                    new RandomAccessFileOrArray(
                        new iTextSharp.text.io.RandomAccessSourceFactory()
                            .CreateSource(contenido))));

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

        private sealed class ContadorDeFragmentos : IRenderListener
        {
            public readonly List<string> Fragmentos = new List<string>();

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
                Fragmentos.Add(renderInfo.GetText());
            }
        }
    }
}

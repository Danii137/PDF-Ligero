using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using iTextSharp.text.pdf;

namespace FirmaAutomatica
{
    /// <summary>
    /// Vuelve a escribir un flujo de contenido a partir de sus operadores.
    ///
    /// Se necesita para la edicion directa: hay que sustituir las cadenas de
    /// texto de unos operadores concretos y dejar el resto exactamente igual.
    ///
    /// Todas las cadenas se escriben en hexadecimal. Es mas largo que la forma
    /// literal, pero evita de raiz los problemas de escapado con parentesis,
    /// barras y bytes no imprimibles, que en un flujo de contenido son
    /// frecuentes.
    /// </summary>
    internal static class PdfContentStreamWriter
    {
        public static void WriteOperand(MemoryStream salida, PdfObject objeto)
        {
            if (objeto == null)
            {
                Write(salida, "null");
                return;
            }

            if (objeto.IsString())
            {
                WriteHexString(salida, ((PdfString)objeto).GetBytes());
                return;
            }

            if (objeto.IsArray())
            {
                Write(salida, "[");
                var array = (PdfArray)objeto;
                for (var i = 0; i < array.Size; i++)
                {
                    if (i > 0)
                    {
                        Write(salida, " ");
                    }

                    WriteOperand(salida, array.GetPdfObject(i));
                }

                Write(salida, "]");
                return;
            }

            if (objeto.IsDictionary())
            {
                Write(salida, "<<");
                var diccionario = (PdfDictionary)objeto;
                foreach (var clave in diccionario.Keys)
                {
                    Write(salida, " ");
                    WriteOperand(salida, clave);
                    Write(salida, " ");
                    WriteOperand(salida, diccionario.Get(clave));
                }

                Write(salida, " >>");
                return;
            }

            if (objeto.IsNumber())
            {
                // ToString de PdfNumber respeta el texto original del PDF, que
                // es lo que interesa para no alterar nada por redondeos.
                Write(salida, objeto.ToString());
                return;
            }

            // Nombres, booleanos, null y operadores se escriben tal cual.
            Write(salida, objeto.ToString());
        }

        public static void WriteHexString(MemoryStream salida, byte[] bytes)
        {
            Write(salida, "<");
            if (bytes != null)
            {
                var hex = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes)
                {
                    hex.Append(b.ToString("X2", CultureInfo.InvariantCulture));
                }

                Write(salida, hex.ToString());
            }

            Write(salida, ">");
        }

        public static void Write(MemoryStream salida, string texto)
        {
            var bytes = PdfEncodings.ConvertToBytes(texto, null);
            salida.Write(bytes, 0, bytes.Length);
        }

        /// <summary>
        /// Escribe una lista de operandos seguida de su operador, con el
        /// espaciado que espera un flujo de contenido.
        /// </summary>
        public static void WriteOperation(
            MemoryStream salida,
            IList<PdfObject> operandos,
            string operador)
        {
            for (var i = 0; i < operandos.Count; i++)
            {
                WriteOperand(salida, operandos[i]);
                Write(salida, " ");
            }

            Write(salida, operador);
            Write(salida, "\n");
        }
    }
}

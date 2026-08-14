using System;
using System.Collections.Generic;
using System.Globalization;

namespace FirmaAutomatica
{
    /// <summary>Que paginas se van a imprimir.</summary>
    internal enum PdfPageSelectionKind
    {
        All,
        Current,
        Range,
        Odd,
        Even
    }

    /// <summary>
    /// Traduce lo que se escribe en el cuadro de paginas a la lista de paginas
    /// que hay que imprimir.
    ///
    /// Se admite lo de siempre: "3", "1-5", "1-5, 8, 11-13", y tambien
    /// intervalos al reves ("9-4"), que se entienden igual.
    /// </summary>
    internal static class PdfPageRangeParser
    {
        /// <summary>
        /// Paginas a imprimir, en orden y sin repetir. Devuelve lista vacia si
        /// lo escrito no selecciona ninguna pagina valida.
        /// </summary>
        public static IList<int> Resolve(
            PdfPageSelectionKind kind,
            string rangeText,
            int pageCount,
            int currentPageNumber)
        {
            var paginas = new List<int>();
            if (pageCount < 1)
            {
                return paginas;
            }

            if (kind == PdfPageSelectionKind.Current)
            {
                var actual = Math.Max(1, Math.Min(pageCount, currentPageNumber));
                paginas.Add(actual);
                return paginas;
            }

            if (kind == PdfPageSelectionKind.Range)
            {
                return ParseRange(rangeText, pageCount);
            }

            for (var pagina = 1; pagina <= pageCount; pagina++)
            {
                if (kind == PdfPageSelectionKind.Odd && pagina % 2 == 0)
                {
                    continue;
                }
                if (kind == PdfPageSelectionKind.Even && pagina % 2 != 0)
                {
                    continue;
                }

                paginas.Add(pagina);
            }

            return paginas;
        }

        /// <summary>
        /// Descripcion breve de lo seleccionado, para poder comprobarlo antes de
        /// mandar el trabajo a la impresora.
        /// </summary>
        public static string Describe(IList<int> paginas, int pageCount)
        {
            if (paginas == null || paginas.Count == 0)
            {
                return "No hay ninguna página seleccionada.";
            }

            if (paginas.Count == pageCount)
            {
                return "Se imprimirán las " +
                    pageCount.ToString(CultureInfo.CurrentCulture) +
                    " páginas.";
            }

            if (paginas.Count == 1)
            {
                return "Se imprimirá la página " +
                    paginas[0].ToString(CultureInfo.CurrentCulture) + ".";
            }

            return "Se imprimirán " +
                paginas.Count.ToString(CultureInfo.CurrentCulture) +
                " páginas de " +
                pageCount.ToString(CultureInfo.CurrentCulture) + ".";
        }

        private static IList<int> ParseRange(string texto, int pageCount)
        {
            var elegidas = new List<int>();
            var vistas = new HashSet<int>();
            if (string.IsNullOrWhiteSpace(texto))
            {
                return elegidas;
            }

            foreach (var trozo in texto.Split(',', ';'))
            {
                var limpio = trozo.Trim();
                if (limpio.Length == 0)
                {
                    continue;
                }

                var guion = limpio.IndexOf('-');
                if (guion < 0)
                {
                    AgregarPagina(limpio, pageCount, elegidas, vistas);
                    continue;
                }

                var desde = LeerNumero(limpio.Substring(0, guion));
                var hasta = LeerNumero(limpio.Substring(guion + 1));
                if (desde <= 0 || hasta <= 0)
                {
                    continue;
                }

                var inicio = Math.Max(1, Math.Min(desde, hasta));
                var fin = Math.Min(pageCount, Math.Max(desde, hasta));
                for (var pagina = inicio; pagina <= fin; pagina++)
                {
                    if (vistas.Add(pagina))
                    {
                        elegidas.Add(pagina);
                    }
                }
            }

            elegidas.Sort();
            return elegidas;
        }

        private static void AgregarPagina(
            string texto,
            int pageCount,
            List<int> elegidas,
            HashSet<int> vistas)
        {
            var pagina = LeerNumero(texto);
            if (pagina >= 1 && pagina <= pageCount && vistas.Add(pagina))
            {
                elegidas.Add(pagina);
            }
        }

        private static int LeerNumero(string texto)
        {
            int valor;
            return int.TryParse(
                (texto ?? string.Empty).Trim(),
                NumberStyles.Integer,
                CultureInfo.CurrentCulture,
                out valor)
                ? valor
                : -1;
        }
    }
}

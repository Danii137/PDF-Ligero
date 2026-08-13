using System;
using System.Collections.Generic;

namespace FirmaAutomatica
{
    /// <summary>
    /// Traduce el nombre de fuente que trae un PDF al archivo instalado en
    /// Windows.
    ///
    /// Hace falta porque la fuente incrustada en el PDF casi nunca sirve para
    /// escribir: los PDF de Word incrustan subconjuntos, que solo contienen los
    /// glifos que el documento ya usaba. Al escribir texto nuevo hay que cargar
    /// la fuente completa del sistema, y para eso hay que saber que archivo le
    /// corresponde.
    ///
    /// Los nombres se comparan normalizados (sin espacios, guiones ni comas y
    /// sin distinguir mayusculas), porque un mismo tipo aparece escrito de
    /// muchas formas: "Times New Roman", "TimesNewRomanPSMT", "Times-Roman".
    /// </summary>
    internal static class PdfSystemFontCatalog
    {
        private sealed class Familia
        {
            public Familia(
                string displayName,
                string regular,
                string bold,
                string italic,
                string boldItalic)
            {
                DisplayName = displayName;
                Regular = regular;
                Bold = bold ?? regular;
                Italic = italic ?? regular;
                BoldItalic = boldItalic ?? (bold ?? regular);
            }

            public string DisplayName { get; private set; }

            public string Regular { get; private set; }

            public string Bold { get; private set; }

            public string Italic { get; private set; }

            public string BoldItalic { get; private set; }

            public string Pick(bool bold, bool italic)
            {
                if (bold && italic)
                {
                    return BoldItalic;
                }
                if (bold)
                {
                    return Bold;
                }
                if (italic)
                {
                    return Italic;
                }

                return Regular;
            }
        }

        private static readonly Dictionary<string, Familia> Catalogo =
            BuildCatalogo();

        /// <summary>
        /// Devuelve el archivo de fuente que corresponde al nombre, o null si no
        /// esta en el catalogo. Devolver null no es un error: el llamante sigue
        /// con sus candidatos genericos de siempre.
        /// </summary>
        public static string ResolveFileName(
            string fontName,
            bool bold,
            bool italic,
            out string displayName)
        {
            displayName = null;
            if (string.IsNullOrEmpty(fontName))
            {
                return null;
            }

            Familia familia;
            if (!Catalogo.TryGetValue(Normalize(fontName), out familia))
            {
                return null;
            }

            displayName = familia.DisplayName;
            return familia.Pick(bold, italic);
        }

        private static readonly HashSet<string> Serifas =
            new HashSet<string>(StringComparer.Ordinal)
            {
                Normalize("Times New Roman"), Normalize("TimesNewRomanPS"),
                Normalize("Times"), Normalize("Georgia"), Normalize("Cambria"),
                Normalize("Constantia"), Normalize("Garamond"),
                Normalize("Book Antiqua"), Normalize("Palatino Linotype"),
                Normalize("Palatino"), Normalize("Century Schoolbook"),
                Normalize("Bookman Old Style"), Normalize("Rockwell")
            };

        private static readonly HashSet<string> Monoespaciadas =
            new HashSet<string>(StringComparer.Ordinal)
            {
                Normalize("Consolas"), Normalize("Courier New"),
                Normalize("Courier"), Normalize("Lucida Console"),
                Normalize("Cascadia Mono"), Normalize("Cascadia Code"),
                Normalize("Menlo"), Normalize("Monaco")
            };

        /// <summary>
        /// Familia generica a la que se parece un tipo, para que el respaldo se
        /// parezca lo maximo posible cuando la fuente exacta no este instalada.
        /// </summary>
        public static PdfTextEditFontFamily GuessFamily(string fontName)
        {
            var normalizado = Normalize(fontName);
            if (Serifas.Contains(normalizado))
            {
                return PdfTextEditFontFamily.Serif;
            }
            if (Monoespaciadas.Contains(normalizado))
            {
                return PdfTextEditFontFamily.Monospace;
            }

            return PdfTextEditFontFamily.SansSerif;
        }

        /// <summary>Nombre para enseñar, si el tipo esta catalogado.</summary>
        public static string ResolveDisplayName(string fontName)
        {
            if (string.IsNullOrEmpty(fontName))
            {
                return null;
            }

            Familia familia;
            if (!Catalogo.TryGetValue(Normalize(fontName), out familia))
            {
                return null;
            }

            return familia.DisplayName;
        }

        internal static string Normalize(string nombre)
        {
            if (string.IsNullOrEmpty(nombre))
            {
                return string.Empty;
            }

            var limpio = new System.Text.StringBuilder(nombre.Length);
            foreach (var caracter in nombre)
            {
                if (char.IsLetterOrDigit(caracter))
                {
                    limpio.Append(char.ToLowerInvariant(caracter));
                }
            }

            return limpio.ToString();
        }

        private static Dictionary<string, Familia> BuildCatalogo()
        {
            var catalogo = new Dictionary<string, Familia>(
                StringComparer.Ordinal);

            Action<string[], Familia> registrar = delegate(
                string[] alias,
                Familia familia)
            {
                foreach (var nombre in alias)
                {
                    catalogo[Normalize(nombre)] = familia;
                }
            };

            registrar(
                new[] { "Arial", "ArialMT", "Helvetica", "Arial Unicode MS" },
                new Familia(
                    "Arial",
                    "arial.ttf",
                    "arialbd.ttf",
                    "ariali.ttf",
                    "arialbi.ttf"));

            registrar(
                new[] { "Arial Narrow", "ArialNarrow" },
                new Familia(
                    "Arial Narrow",
                    "arialn.ttf",
                    "arialnb.ttf",
                    "arialni.ttf",
                    "arialnbi.ttf"));

            registrar(
                new[] { "Calibri" },
                new Familia(
                    "Calibri",
                    "calibri.ttf",
                    "calibrib.ttf",
                    "calibrii.ttf",
                    "calibriz.ttf"));

            registrar(
                new[] { "Cambria" },
                new Familia(
                    "Cambria",
                    "cambria.ttc,0",
                    "cambriab.ttf",
                    "cambriai.ttf",
                    "cambriaz.ttf"));

            registrar(
                new[] { "Candara" },
                new Familia(
                    "Candara",
                    "candara.ttf",
                    "candarab.ttf",
                    "candarai.ttf",
                    "candaraz.ttf"));

            registrar(
                new[] { "Consolas" },
                new Familia(
                    "Consolas",
                    "consola.ttf",
                    "consolab.ttf",
                    "consolai.ttf",
                    "consolaz.ttf"));

            registrar(
                new[] { "Constantia" },
                new Familia(
                    "Constantia",
                    "constan.ttf",
                    "constanb.ttf",
                    "constani.ttf",
                    "constanz.ttf"));

            registrar(
                new[] { "Corbel" },
                new Familia(
                    "Corbel",
                    "corbel.ttf",
                    "corbelb.ttf",
                    "corbeli.ttf",
                    "corbelz.ttf"));

            registrar(
                new[] { "Courier New", "CourierNew", "Courier" },
                new Familia(
                    "Courier New",
                    "cour.ttf",
                    "courbd.ttf",
                    "couri.ttf",
                    "courbi.ttf"));

            registrar(
                new[] { "Georgia" },
                new Familia(
                    "Georgia",
                    "georgia.ttf",
                    "georgiab.ttf",
                    "georgiai.ttf",
                    "georgiaz.ttf"));

            registrar(
                new[] { "Segoe UI", "SegoeUI" },
                new Familia(
                    "Segoe UI",
                    "segoeui.ttf",
                    "segoeuib.ttf",
                    "segoeuii.ttf",
                    "segoeuiz.ttf"));

            registrar(
                new[] { "Tahoma" },
                new Familia(
                    "Tahoma",
                    "tahoma.ttf",
                    "tahomabd.ttf",
                    null,
                    null));

            registrar(
                new[]
                {
                    "Times New Roman", "TimesNewRoman", "Times",
                    "TimesNewRomanPS"
                },
                new Familia(
                    "Times New Roman",
                    "times.ttf",
                    "timesbd.ttf",
                    "timesi.ttf",
                    "timesbi.ttf"));

            registrar(
                new[] { "Trebuchet MS", "TrebuchetMS" },
                new Familia(
                    "Trebuchet MS",
                    "trebuc.ttf",
                    "trebucbd.ttf",
                    "trebucit.ttf",
                    "trebucbi.ttf"));

            registrar(
                new[] { "Verdana" },
                new Familia(
                    "Verdana",
                    "verdana.ttf",
                    "verdanab.ttf",
                    "verdanai.ttf",
                    "verdanaz.ttf"));

            registrar(
                new[] { "Palatino Linotype", "PalatinoLinotype", "Palatino" },
                new Familia(
                    "Palatino Linotype",
                    "pala.ttf",
                    "palab.ttf",
                    "palai.ttf",
                    "palabi.ttf"));

            registrar(
                new[] { "Book Antiqua", "BookAntiqua" },
                new Familia(
                    "Book Antiqua",
                    "bkant.ttf",
                    null,
                    null,
                    null));

            registrar(
                new[] { "Century Gothic", "CenturyGothic" },
                new Familia(
                    "Century Gothic",
                    "gothic.ttf",
                    "gothicb.ttf",
                    "gothici.ttf",
                    "gothicbi.ttf"));

            registrar(
                new[] { "Garamond" },
                new Familia(
                    "Garamond",
                    "gara.ttf",
                    "garabd.ttf",
                    "garait.ttf",
                    null));

            registrar(
                new[] { "Franklin Gothic Medium", "FranklinGothicMedium" },
                new Familia(
                    "Franklin Gothic Medium",
                    "framd.ttf",
                    null,
                    "framdit.ttf",
                    null));

            registrar(
                new[] { "Comic Sans MS", "ComicSansMS" },
                new Familia(
                    "Comic Sans MS",
                    "comic.ttf",
                    "comicbd.ttf",
                    "comici.ttf",
                    "comicz.ttf"));

            registrar(
                new[] { "Impact" },
                new Familia("Impact", "impact.ttf", null, null, null));

            registrar(
                new[] { "Lucida Console", "LucidaConsole" },
                new Familia("Lucida Console", "lucon.ttf", null, null, null));

            registrar(
                new[] { "Lucida Sans Unicode", "LucidaSansUnicode" },
                new Familia(
                    "Lucida Sans Unicode",
                    "l_10646.ttf",
                    null,
                    null,
                    null));

            registrar(
                new[] { "Microsoft Sans Serif", "MicrosoftSansSerif" },
                new Familia(
                    "Microsoft Sans Serif",
                    "micross.ttf",
                    null,
                    null,
                    null));

            registrar(
                new[] { "Bahnschrift" },
                new Familia("Bahnschrift", "bahnschrift.ttf", null, null, null));

            return catalogo;
        }
    }
}

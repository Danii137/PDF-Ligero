using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;

namespace FirmaAutomatica
{
    internal sealed class PdfPageExtractResult
    {
        private readonly List<string> outputPaths;

        public PdfPageExtractResult(
            IEnumerable<string> outputPaths,
            int extractedPageCount,
            int sourcePageCount,
            bool digitalSignaturesInvalidated)
        {
            this.outputPaths = new List<string>(
                outputPaths ?? new string[0]);
            ExtractedPageCount = extractedPageCount;
            SourcePageCount = sourcePageCount;
            DigitalSignaturesInvalidated = digitalSignaturesInvalidated;
        }

        /// <summary>Archivos creados, en orden.</summary>
        public IList<string> OutputPaths
        {
            get { return outputPaths; }
        }

        public int ExtractedPageCount { get; private set; }

        public int SourcePageCount { get; private set; }

        public bool DigitalSignaturesInvalidated { get; private set; }

        public string DigitalSignatureWarning
        {
            get
            {
                return DigitalSignaturesInvalidated
                    ? PdfPageOrganizerService
                        .DigitalSignatureInvalidationWarning
                    : string.Empty;
            }
        }
    }

    /// <summary>
    /// Sacar paginas a un PDF nuevo y partir un documento en varios.
    ///
    /// No escribe PDFs por su cuenta: monta la lista de paginas y se la pasa a
    /// <see cref="PdfPageOrganizerService"/>, que ya sabe copiar paginas sin
    /// rasterizar ni recomprimir imagenes, conservar los marcadores y
    /// comprobar el resultado antes de darlo por bueno. El original se abre
    /// solo para leer y nunca se toca.
    /// </summary>
    internal static class PdfPageExtractService
    {
        /// <summary>Nombre propuesto para las paginas que se sacan.</summary>
        public static string SuggestExtractPath(
            string sourcePdfPath,
            IList<int> pages)
        {
            var source = Path.GetFullPath(sourcePdfPath);
            var directory = Path.GetDirectoryName(source);
            var baseName = Path.GetFileNameWithoutExtension(source);
            var sufijo = DescribePagesForFileName(pages);
            return EnsureFreePath(
                directory,
                baseName + " " + sufijo);
        }

        /// <summary>
        /// Saca las paginas indicadas a un PDF nuevo, en el orden en que se
        /// piden.
        /// </summary>
        public static PdfPageExtractResult Extract(
            string sourcePdfPath,
            IList<int> pages,
            string outputPath,
            Action<string> reportStage,
            CancellationToken cancellationToken)
        {
            if (pages == null || pages.Count == 0)
            {
                throw new ArgumentException(
                    "No se ha indicado ninguna pagina.",
                    "pages");
            }

            cancellationToken.ThrowIfCancellationRequested();
            Report(reportStage, "Sacando las páginas…");
            var resultado = PdfPageOrganizerService.Organize(
                sourcePdfPath,
                BuildPlan(pages),
                outputPath,
                null,
                cancellationToken);

            return new PdfPageExtractResult(
                new[] { resultado.OutputPath },
                resultado.PageCount,
                resultado.SourcePageCount,
                resultado.DigitalSignaturesInvalidated);
        }

        /// <summary>
        /// Parte el documento en archivos de <paramref name="pagesPerFile"/>
        /// paginas. El ultimo lleva lo que quede.
        /// </summary>
        public static PdfPageExtractResult Split(
            string sourcePdfPath,
            int pagesPerFile,
            string outputDirectory,
            Action<string> reportStage,
            CancellationToken cancellationToken)
        {
            if (pagesPerFile < 1)
            {
                throw new ArgumentOutOfRangeException(
                    "pagesPerFile",
                    "Cada archivo debe llevar al menos una pagina.");
            }

            var source = Path.GetFullPath(sourcePdfPath);
            if (!File.Exists(source))
            {
                throw new FileNotFoundException(
                    "El PDF ya no está en su sitio.",
                    source);
            }

            var directory = string.IsNullOrWhiteSpace(outputDirectory)
                ? Path.GetDirectoryName(source)
                : Path.GetFullPath(outputDirectory);
            if (!Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException(
                    "La carpeta de destino no existe: " + directory);
            }

            int pageCount;
            using (var reader = new iTextSharp.text.pdf.PdfReader(
                source,
                (byte[])null,
                true))
            {
                pageCount = reader.NumberOfPages;
            }

            if (pageCount < 1)
            {
                throw new InvalidDataException(
                    "El PDF no contiene páginas.");
            }

            var baseName = Path.GetFileNameWithoutExtension(source);
            var creados = new List<string>();
            var totalPaginas = 0;
            var firmasInvalidadas = false;
            var parte = 0;
            for (var primera = 1; primera <= pageCount; primera += pagesPerFile)
            {
                cancellationToken.ThrowIfCancellationRequested();
                parte++;
                var ultima = Math.Min(pageCount, primera + pagesPerFile - 1);
                var paginas = new List<int>();
                for (var pagina = primera; pagina <= ultima; pagina++)
                {
                    paginas.Add(pagina);
                }

                Report(
                    reportStage,
                    "Escribiendo la parte " +
                    parte.ToString(CultureInfo.CurrentCulture) + "…");

                var destino = EnsureFreePath(
                    directory,
                    baseName + " parte " +
                    parte.ToString("00", CultureInfo.InvariantCulture) +
                    " (" + DescribeRange(primera, ultima) + ")");
                var resultado = PdfPageOrganizerService.Organize(
                    source,
                    BuildPlan(paginas),
                    destino,
                    null,
                    cancellationToken);
                creados.Add(resultado.OutputPath);
                totalPaginas += resultado.PageCount;
                firmasInvalidadas = firmasInvalidadas ||
                    resultado.DigitalSignaturesInvalidated;
            }

            return new PdfPageExtractResult(
                creados,
                totalPaginas,
                pageCount,
                firmasInvalidadas);
        }

        private static IList<PdfPageOrganizerPage> BuildPlan(
            IList<int> pages)
        {
            var plan = new List<PdfPageOrganizerPage>();
            foreach (var pagina in pages)
            {
                // Sin giro: las paginas salen como estan en el original.
                plan.Add(new PdfPageOrganizerPage(pagina, 0));
            }

            return plan;
        }

        /// <summary>
        /// Trozo de nombre que describe lo que se ha sacado: "pagina 7",
        /// "paginas 3-9" o "9 paginas" cuando la seleccion es salteada.
        /// </summary>
        private static string DescribePagesForFileName(IList<int> pages)
        {
            if (pages == null || pages.Count == 0)
            {
                return "extracto";
            }

            if (pages.Count == 1)
            {
                return "pagina " +
                    pages[0].ToString(CultureInfo.InvariantCulture);
            }

            var seguidas = true;
            for (var i = 1; i < pages.Count; i++)
            {
                if (pages[i] != pages[i - 1] + 1)
                {
                    seguidas = false;
                    break;
                }
            }

            if (seguidas)
            {
                return "paginas " +
                    DescribeRange(pages[0], pages[pages.Count - 1]);
            }

            return pages.Count.ToString(CultureInfo.InvariantCulture) +
                " paginas";
        }

        private static string DescribeRange(int primera, int ultima)
        {
            return primera.ToString(CultureInfo.InvariantCulture) +
                "-" + ultima.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Ruta libre en esa carpeta. No se pisa nunca un archivo existente:
        /// lo que hay en disco es de alguien.
        /// </summary>
        private static string EnsureFreePath(
            string directory,
            string baseName)
        {
            var limpio = SanitizeFileName(baseName);
            var candidato = Path.Combine(directory, limpio + ".pdf");
            var sufijo = 2;
            while (File.Exists(candidato))
            {
                candidato = Path.Combine(
                    directory,
                    limpio + " (" +
                    sufijo.ToString(CultureInfo.InvariantCulture) + ").pdf");
                sufijo++;
            }

            return candidato;
        }

        private static string SanitizeFileName(string name)
        {
            var limpio = name ?? "extracto";
            foreach (var invalido in Path.GetInvalidFileNameChars())
            {
                limpio = limpio.Replace(invalido, '_');
            }

            limpio = limpio.Trim();
            return limpio.Length == 0 ? "extracto" : limpio;
        }

        private static void Report(Action<string> reportStage, string stage)
        {
            if (reportStage != null && !string.IsNullOrEmpty(stage))
            {
                reportStage(stage);
            }
        }
    }
}

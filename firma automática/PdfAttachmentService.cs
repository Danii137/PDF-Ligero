using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using iTextSharp.text.pdf;
using IoPath = System.IO.Path;

namespace FirmaAutomatica
{
    /// <summary>Un archivo adjunto dentro del PDF.</summary>
    internal sealed class PdfAttachmentInfo
    {
        public PdfAttachmentInfo(
            string name,
            string description,
            long length,
            DateTime? modified)
        {
            Name = name ?? string.Empty;
            Description = description ?? string.Empty;
            Length = length;
            Modified = modified;
        }

        public string Name { get; private set; }

        public string Description { get; private set; }

        /// <summary>Tamaño en bytes, o -1 si el PDF no lo declara.</summary>
        public long Length { get; private set; }

        public DateTime? Modified { get; private set; }

        public string DescribeLength()
        {
            if (Length < 0)
            {
                return "tamaño desconocido";
            }

            if (Length >= 1024L * 1024L)
            {
                return (Length / 1024D / 1024D).ToString(
                    "0.0",
                    CultureInfo.CurrentCulture) + " MB";
            }

            if (Length >= 1024L)
            {
                return (Length / 1024D).ToString(
                    "0",
                    CultureInfo.CurrentCulture) + " kB";
            }

            return Length.ToString(CultureInfo.CurrentCulture) + " bytes";
        }
    }

    /// <summary>
    /// Archivos adjuntos de un PDF: verlos, sacarlos y añadirlos.
    ///
    /// En documentacion oficial es normal que el PDF lleve dentro el DWG, la
    /// hoja de calculo o el justificante. Hasta ahora esos archivos estaban
    /// ahi sin que el programa dijera nada, asi que se perdian.
    /// </summary>
    internal static class PdfAttachmentService
    {
        public const string XfaUnsupportedMessage =
            "Los formularios XFA no se pueden modificar de forma segura. " +
            "Guarda antes una copia PDF normal del formulario.";

        /// <summary>Lo que lleva dentro el documento.</summary>
        public static IList<PdfAttachmentInfo> List(string pdfPath)
        {
            var adjuntos = new List<PdfAttachmentInfo>();
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
            {
                return adjuntos;
            }

            PdfReader reader = null;
            try
            {
                reader = new PdfReader(pdfPath, (byte[])null, true);
                foreach (var especificacion in EnumerateSpecs(reader))
                {
                    adjuntos.Add(Describe(especificacion));
                }
            }
            catch (Exception ex)
            {
                AppLog.Write("No se pudieron leer los adjuntos: " + ex);
            }
            finally
            {
                if (reader != null)
                {
                    try
                    {
                        reader.Close();
                    }
                    catch (Exception)
                    {
                    }
                }
            }

            return adjuntos;
        }

        /// <summary>
        /// Saca un adjunto a disco. Devuelve false si ya no esta dentro del
        /// documento.
        /// </summary>
        public static bool Extract(
            string pdfPath,
            string attachmentName,
            string outputPath)
        {
            PdfReader reader = null;
            try
            {
                reader = new PdfReader(pdfPath, (byte[])null, true);
                foreach (var especificacion in EnumerateSpecs(reader))
                {
                    var nombre = ReadName(especificacion);
                    if (!string.Equals(
                            nombre,
                            attachmentName,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var flujo = especificacion.GetAsDict(PdfName.EF);
                    if (flujo == null)
                    {
                        return false;
                    }

                    var contenido = flujo.GetAsStream(PdfName.F) as PRStream;
                    if (contenido == null)
                    {
                        return false;
                    }

                    var bytes = PdfReader.GetStreamBytes(contenido);
                    File.WriteAllBytes(outputPath, bytes);
                    return true;
                }
            }
            finally
            {
                if (reader != null)
                {
                    try
                    {
                        reader.Close();
                    }
                    catch (Exception)
                    {
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Añade archivos al PDF y escribe una copia nueva. El original no se
        /// toca.
        /// </summary>
        public static void Add(
            string sourcePdfPath,
            string outputPath,
            IList<string> filesToAttach,
            CancellationToken cancellationToken)
        {
            if (filesToAttach == null || filesToAttach.Count == 0)
            {
                throw new ArgumentException(
                    "No se ha indicado ningun archivo.",
                    "filesToAttach");
            }

            var source = IoPath.GetFullPath(sourcePdfPath);
            var target = IoPath.GetFullPath(outputPath);
            if (string.Equals(
                    source,
                    target,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException(
                    "El original no se sobrescribe: elige otro nombre.");
            }

            var temporaryPath = IoPath.Combine(
                IoPath.GetDirectoryName(target),
                "." + IoPath.GetFileNameWithoutExtension(target) +
                "." + Guid.NewGuid().ToString("N") + ".adjuntar.tmp");

            try
            {
                PdfReader reader = null;
                PdfStamper stamper = null;
                FileStream output = null;
                try
                {
                    reader = new PdfReader(source, (byte[])null, true);
                    var acroForm = reader.Catalog.GetAsDict(PdfName.ACROFORM);
                    if (acroForm != null &&
                        acroForm.Get(PdfName.XFA) != null)
                    {
                        throw new NotSupportedException(
                            XfaUnsupportedMessage);
                    }

                    var paginas = reader.NumberOfPages;
                    output = new FileStream(
                        temporaryPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        1024 * 1024,
                        FileOptions.SequentialScan);
                    stamper = new PdfStamper(reader, output);
                    stamper.Writer.CloseStream = false;

                    foreach (var archivo in filesToAttach)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!File.Exists(archivo))
                        {
                            continue;
                        }

                        var nombre = IoPath.GetFileName(archivo);
                        stamper.AddFileAttachment(
                            "Adjuntado con PDF Ligero",
                            File.ReadAllBytes(archivo),
                            nombre,
                            nombre);
                    }

                    stamper.Close();
                    stamper = null;
                    reader = null;
                    output.Flush(true);
                    output.Dispose();
                    output = null;

                    ValidateWrittenPdf(temporaryPath, paginas);
                }
                finally
                {
                    if (stamper != null)
                    {
                        try
                        {
                            stamper.Close();
                        }
                        catch (Exception)
                        {
                        }
                    }
                    else if (reader != null)
                    {
                        reader.Close();
                    }

                    if (output != null)
                    {
                        output.Dispose();
                    }
                }

                File.Move(temporaryPath, target);
            }
            catch
            {
                TryDelete(temporaryPath);
                throw;
            }
        }

        /// <summary>
        /// Recorre los adjuntos del documento. Estan en dos sitios segun como
        /// se hicieran: el arbol EmbeddedFiles del catalogo y las anotaciones
        /// de tipo FileAttachment ancladas a una pagina.
        /// </summary>
        private static IEnumerable<PdfDictionary> EnumerateSpecs(
            PdfReader reader)
        {
            var vistos = new List<PdfDictionary>();

            var nombres = reader.Catalog.GetAsDict(PdfName.NAMES);
            if (nombres != null)
            {
                var incrustados = nombres.GetAsDict(PdfName.EMBEDDEDFILES);
                if (incrustados != null)
                {
                    var arbol = PdfNameTree.ReadTree(incrustados);
                    if (arbol != null)
                    {
                        foreach (var clave in arbol.Keys)
                        {
                            var especificacion =
                                PdfReader.GetPdfObject(arbol[clave])
                                    as PdfDictionary;
                            if (especificacion != null)
                            {
                                vistos.Add(especificacion);
                            }
                        }
                    }
                }
            }

            for (var pagina = 1; pagina <= reader.NumberOfPages; pagina++)
            {
                var hoja = reader.GetPageN(pagina);
                if (hoja == null)
                {
                    continue;
                }

                var anotaciones = hoja.GetAsArray(PdfName.ANNOTS);
                if (anotaciones == null)
                {
                    continue;
                }

                for (var i = 0; i < anotaciones.Size; i++)
                {
                    var anotacion = anotaciones.GetAsDict(i);
                    if (anotacion == null)
                    {
                        continue;
                    }

                    var subtipo = anotacion.GetAsName(PdfName.SUBTYPE);
                    if (subtipo == null ||
                        !PdfName.FILEATTACHMENT.Equals(subtipo))
                    {
                        continue;
                    }

                    var especificacion = anotacion.GetAsDict(PdfName.FS);
                    if (especificacion != null &&
                        !vistos.Contains(especificacion))
                    {
                        vistos.Add(especificacion);
                    }
                }
            }

            return vistos;
        }

        private static PdfAttachmentInfo Describe(
            PdfDictionary especificacion)
        {
            var nombre = ReadName(especificacion);
            var descripcion = ReadString(especificacion, PdfName.DESC);

            long tamano = -1;
            DateTime? modificado = null;
            var flujo = especificacion.GetAsDict(PdfName.EF);
            if (flujo != null)
            {
                var contenido = flujo.GetAsStream(PdfName.F);
                if (contenido != null)
                {
                    var parametros = contenido.GetAsDict(PdfName.PARAMS);
                    if (parametros != null)
                    {
                        var size = parametros.GetAsNumber(PdfName.SIZE);
                        if (size != null)
                        {
                            tamano = size.LongValue;
                        }

                        var fecha = parametros.GetAsString(PdfName.MODDATE);
                        if (fecha != null)
                        {
                            try
                            {
                                modificado = PdfDate.Decode(fecha.ToString());
                            }
                            catch (Exception)
                            {
                            }
                        }
                    }
                }
            }

            return new PdfAttachmentInfo(
                nombre,
                descripcion,
                tamano,
                modificado);
        }

        private static string ReadName(PdfDictionary especificacion)
        {
            // UF es el nombre en Unicode y manda sobre F, que puede venir en
            // una codificacion antigua y estropear las tildes.
            var unicode = ReadString(especificacion, PdfName.UF);
            if (!string.IsNullOrEmpty(unicode))
            {
                return unicode;
            }

            return ReadString(especificacion, PdfName.F);
        }

        private static string ReadString(
            PdfDictionary diccionario,
            PdfName clave)
        {
            var valor = diccionario.GetAsString(clave);
            return valor == null
                ? string.Empty
                : valor.ToUnicodeString();
        }

        private static void ValidateWrittenPdf(string path, int pageCount)
        {
            if (!File.Exists(path) || new FileInfo(path).Length <= 0)
            {
                throw new InvalidDataException(
                    "La copia con adjuntos está vacía.");
            }

            using (var reader = new PdfReader(path))
            {
                if (reader.NumberOfPages != pageCount)
                {
                    throw new InvalidDataException(
                        "La copia con adjuntos no conserva todas las " +
                        "páginas.");
                }
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
            }
        }
    }
}

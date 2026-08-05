using System;
using System.IO;

namespace FirmaAutomatica
{
    /// <summary>
    /// Causa homogenea de un fallo al abrir o procesar un PDF.
    /// </summary>
    internal enum PdfProblemKind
    {
        Unknown = 0,
        FileMissing,
        FileLocked,
        AccessDenied,
        PasswordProtected,
        RestrictedPermissions,
        UnsupportedSecurity,
        Damaged,
        NotPdf,
        DynamicForm
    }

    /// <summary>
    /// Diagnostico ya resuelto de un fallo, listo para mostrar.
    /// </summary>
    internal sealed class PdfProblemReport
    {
        public PdfProblemReport(
            PdfProblemKind kind,
            string description,
            string advice,
            bool isPolicyBlock)
        {
            Kind = kind;
            Description = description;
            Advice = advice;
            IsPolicyBlock = isPolicyBlock;
        }

        public PdfProblemKind Kind { get; private set; }

        /// <summary>Frase explicativa en espanol. Nunca el texto original de PDFium o iText.</summary>
        public string Description { get; private set; }

        /// <summary>Que puede hacer el usuario, o null si no hay nada util que anadir.</summary>
        public string Advice { get; private set; }

        /// <summary>true cuando es un bloqueo deliberado y no un fallo real del archivo.</summary>
        public bool IsPolicyBlock { get; private set; }
    }

    /// <summary>
    /// Traduce cualquier fallo de PDF a una causa y un texto unicos para toda la
    /// aplicacion.
    ///
    /// PDFium e iText informan en ingles. Los servicios propios envuelven esos
    /// errores conservando el original como InnerException, de modo que mostrar
    /// GetBaseException().Message devolvia siempre el texto ingles mas interno.
    /// Aqui se recorre la cadena buscando primero las excepciones ajenas, que son
    /// las unicas que identifican la causa sin ambiguedad, y se responde con texto
    /// propio.
    ///
    /// Solo se ejecuta dentro de bloques catch que ya existian: no anade ningun
    /// coste a la apertura normal de un PDF.
    /// </summary>
    internal static class PdfProblemDiagnostics
    {
        private const int SharingViolationHResult = unchecked((int)0x80070020);
        private const int LockViolationHResult = unchecked((int)0x80070021);

        public static PdfProblemReport Analyze(Exception error, string path)
        {
            if (error == null)
            {
                return new PdfProblemReport(
                    PdfProblemKind.Unknown,
                    "No se pudo completar la operación por un error desconocido.",
                    null,
                    false);
            }

            var foreign = FindForeignException(error);
            var kind = Classify(error, path);

            // Con una excepcion ajena en la cadena, su mensaje esta en ingles y no
            // debe llegar al usuario: se responde siempre con texto propio.
            if (foreign != null)
            {
                return new PdfProblemReport(
                    kind,
                    DescribeKind(kind),
                    AdviseKind(kind),
                    IsPolicyBlock(kind));
            }

            // Sin excepcion ajena, el mensaje lo escribio esta aplicacion o el
            // sistema operativo: ya esta en espanol y suele ser mas preciso.
            var message = FindDescriptiveMessage(error);
            return new PdfProblemReport(
                kind,
                string.IsNullOrWhiteSpace(message) ? DescribeKind(kind) : message,
                null,
                IsPolicyBlock(kind));
        }

        /// <summary>Atajo para los motores, que solo necesitan la frase.</summary>
        public static string Describe(Exception error, string path)
        {
            return Analyze(error, path).Description;
        }

        public static PdfProblemKind Classify(Exception error, string path)
        {
            if (error == null)
            {
                return PdfProblemKind.Unknown;
            }

            // Paso 1: excepciones ajenas. Identifican la causa sin ambiguedad, asi
            // que mandan aunque esten envueltas en otra propia.
            var current = error;
            while (current != null)
            {
                var foreignKind = ClassifyForeign(current);
                if (foreignKind != PdfProblemKind.Unknown)
                {
                    return foreignKind;
                }

                current = current.InnerException;
            }

            // Paso 2: tipos propios y del sistema, de fuera hacia dentro.
            current = error;
            while (current != null)
            {
                var ownKind = ClassifyOwn(current);
                if (ownKind != PdfProblemKind.Unknown)
                {
                    return ownKind;
                }

                current = current.InnerException;
            }

            // Paso 3: solo cuando nada se reconocio se mira el archivo.
            return ClassifyByPath(path);
        }

        public static bool IsPolicyBlock(PdfProblemKind kind)
        {
            return kind == PdfProblemKind.PasswordProtected ||
                kind == PdfProblemKind.RestrictedPermissions ||
                kind == PdfProblemKind.UnsupportedSecurity ||
                kind == PdfProblemKind.DynamicForm;
        }

        public static string DescribeKind(PdfProblemKind kind)
        {
            switch (kind)
            {
                case PdfProblemKind.PasswordProtected:
                    return "Este PDF pide una contraseña de apertura.";
                case PdfProblemKind.RestrictedPermissions:
                    return "Este PDF tiene restricciones de permisos que impiden " +
                        "modificarlo con seguridad.";
                case PdfProblemKind.UnsupportedSecurity:
                    return "Este PDF usa un esquema de cifrado que PDF Ligero no admite.";
                case PdfProblemKind.Damaged:
                    return "El archivo no tiene una estructura PDF válida o está incompleto.";
                case PdfProblemKind.NotPdf:
                    return "El archivo no es un PDF.";
                case PdfProblemKind.FileMissing:
                    return "El archivo ya no está en su ubicación.";
                case PdfProblemKind.FileLocked:
                    return "Otro programa está usando el archivo.";
                case PdfProblemKind.AccessDenied:
                    return "Windows no permite leer el archivo.";
                case PdfProblemKind.DynamicForm:
                    return "El PDF contiene un formulario dinámico XFA.";
                default:
                    return "No se pudo interpretar el PDF.";
            }
        }

        public static string AdviseKind(PdfProblemKind kind)
        {
            switch (kind)
            {
                case PdfProblemKind.PasswordProtected:
                    return "Ábrelo con su contraseña en la aplicación de origen y " +
                        "guarda una copia sin protección.";
                case PdfProblemKind.RestrictedPermissions:
                    return "Guarda una copia sin restricciones desde la aplicación " +
                        "que lo generó.";
                case PdfProblemKind.UnsupportedSecurity:
                    return "Guarda una copia sin cifrar desde la aplicación que lo generó.";
                case PdfProblemKind.Damaged:
                    return "Comprueba que la copia esté completa. Si procede de una " +
                        "descarga o de una unidad de red, vuelve a obtenerla.";
                case PdfProblemKind.FileMissing:
                    return "Puede haberse movido, renombrado o eliminado desde otra ventana.";
                case PdfProblemKind.FileLocked:
                    return "Ciérralo en la otra aplicación y vuelve a intentarlo.";
                case PdfProblemKind.AccessDenied:
                    return "Comprueba los permisos de la carpeta o vuelve a conectar " +
                        "la unidad de red.";
                case PdfProblemKind.DynamicForm:
                    return "Guarda antes una copia como PDF normal.";
                default:
                    return null;
            }
        }

        /// <summary>
        /// Primera excepcion de PDFium o iText de la cadena. Su mensaje esta en
        /// ingles y no debe mostrarse.
        /// </summary>
        private static Exception FindForeignException(Exception error)
        {
            var current = error;
            while (current != null)
            {
                if (IsForeign(current))
                {
                    return current;
                }

                current = current.InnerException;
            }

            return null;
        }

        private static bool IsForeign(Exception error)
        {
            if (error is PdfiumViewer.PdfException)
            {
                return true;
            }

            var typeName = error.GetType().FullName;
            return typeName != null && typeName.StartsWith(
                "iTextSharp.",
                StringComparison.Ordinal);
        }

        private static PdfProblemKind ClassifyForeign(Exception error)
        {
            var pdfiumError = error as PdfiumViewer.PdfException;
            if (pdfiumError != null)
            {
                switch (pdfiumError.Error)
                {
                    case PdfiumViewer.PdfError.PasswordProtected:
                        return PdfProblemKind.PasswordProtected;
                    case PdfiumViewer.PdfError.UnsupportedSecurityScheme:
                        return PdfProblemKind.UnsupportedSecurity;
                    case PdfiumViewer.PdfError.InvalidFormat:
                    case PdfiumViewer.PdfError.PageNotFound:
                        return PdfProblemKind.Damaged;
                    case PdfiumViewer.PdfError.CannotOpenFile:
                        // PDFium no distingue entre ausente, bloqueado y sin permiso.
                        return PdfProblemKind.FileMissing;
                    default:
                        return PdfProblemKind.Damaged;
                }
            }

            // BadPasswordException y InvalidPdfException derivan de IOException, de
            // modo que hay que reconocerlas antes que cualquier caso generico de E/S.
            if (error is iTextSharp.text.exceptions.BadPasswordException)
            {
                return PdfProblemKind.PasswordProtected;
            }

            if (error is iTextSharp.text.exceptions.InvalidPdfException)
            {
                // UnsupportedPdfException hereda de InvalidPdfException.
                return PdfProblemKind.Damaged;
            }

            return PdfProblemKind.Unknown;
        }

        private static PdfProblemKind ClassifyOwn(Exception error)
        {
            if (error is UnauthorizedAccessException)
            {
                // En esta aplicacion UnauthorizedAccessException significa siempre
                // "bloqueado por proteccion del PDF". El acceso denegado real de
                // Windows llega como IOException o Win32Exception.
                return PdfProblemKind.RestrictedPermissions;
            }

            if (error is NotSupportedException)
            {
                // Los servicios solo lanzan NotSupportedException para XFA.
                return PdfProblemKind.DynamicForm;
            }

            if (error is FileNotFoundException ||
                error is DirectoryNotFoundException)
            {
                return PdfProblemKind.FileMissing;
            }

            if (error is InvalidDataException ||
                error is EndOfStreamException)
            {
                return PdfProblemKind.Damaged;
            }

            var ioError = error as IOException;
            if (ioError != null)
            {
                var hResult = ioError.HResult;
                if (hResult == SharingViolationHResult ||
                    hResult == LockViolationHResult)
                {
                    return PdfProblemKind.FileLocked;
                }

                return PdfProblemKind.Unknown;
            }

            return PdfProblemKind.Unknown;
        }

        private static PdfProblemKind ClassifyByPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return PdfProblemKind.Unknown;
            }

            try
            {
                if (!File.Exists(path))
                {
                    return PdfProblemKind.FileMissing;
                }

                if (!string.Equals(
                        Path.GetExtension(path),
                        ".pdf",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return PdfProblemKind.NotPdf;
                }
            }
            catch (ArgumentException)
            {
                // Ruta con caracteres invalidos: no aporta nada mas que el fallo.
            }

            return PdfProblemKind.Unknown;
        }

        /// <summary>
        /// Mensaje mas util de una cadena que no contiene excepciones ajenas. Se
        /// prefiere el mas externo porque es el que anade contexto de la operacion.
        /// </summary>
        private static string FindDescriptiveMessage(Exception error)
        {
            var current = error;
            while (current != null)
            {
                var message = current.Message;
                if (!string.IsNullOrWhiteSpace(message) &&
                    !IsDefaultRuntimeMessage(current, message))
                {
                    return message.Trim();
                }

                current = current.InnerException;
            }

            return null;
        }

        /// <summary>
        /// Detecta los mensajes que el propio runtime genera cuando nadie indico
        /// uno: no explican nada al usuario.
        /// </summary>
        private static bool IsDefaultRuntimeMessage(Exception error, string message)
        {
            var typeName = error.GetType().Name;
            return message.IndexOf(typeName, StringComparison.Ordinal) >= 0 &&
                message.IndexOf("Exception", StringComparison.Ordinal) >= 0;
        }
    }
}

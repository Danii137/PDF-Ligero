using System;
using System.IO;
using PdfiumDocument = PdfiumViewer.PdfDocument;

namespace FirmaAutomatica
{
    /// <summary>
    /// Punto unico de apertura de documentos con PDFium.
    ///
    /// Existe por dos motivos concretos, ambos comprobados sobre PdfiumViewer
    /// 2.13:
    ///
    /// 1. Todas las sobrecargas de PdfDocument.Load salvo
    ///    Load(IWin32Window, Stream, string) se compilan sin ninguna clausula de
    ///    manejo de excepciones. Si la carga falla, el FileStream que abrieron
    ///    internamente queda vivo con FileShare.Read hasta que muere el proceso, y
    ///    el usuario no puede borrar ni renombrar su PDF. Aqui el stream es propio
    ///    y se cierra siempre en el camino de fallo.
    ///
    /// 2. Load(IWin32Window, ...) muestra el formulario de contrasena propio de
    ///    PdfiumViewer, en ingles y sin la identidad visual de la aplicacion. La
    ///    aplicacion pide la contrasena con PdfPasswordPromptForm y reintenta con
    ///    este metodo.
    ///
    /// El coste frente a la ruta anterior es el mismo: un FileStream en lugar del
    /// File.OpenRead que la libreria hacia por dentro.
    /// </summary>
    internal static class PdfDocumentOpenService
    {
        public static PdfiumDocument Load(string path)
        {
            return Load(path, null);
        }

        public static PdfiumDocument Load(string path, string password)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException(
                    "Se necesita la ruta del PDF que se va a abrir.",
                    "path");
            }

            FileStream stream = null;
            try
            {
                stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);

                var document = PdfiumDocument.Load(stream, password);

                // A partir de aqui el stream pertenece al documento, que lo cierra
                // en su propio Dispose.
                stream = null;
                return document;
            }
            finally
            {
                if (stream != null)
                {
                    stream.Dispose();
                }
            }
        }

        /// <summary>
        /// true cuando el fallo se debe a que el PDF pide una contrasena de
        /// apertura y, por tanto, tiene sentido pedirsela al usuario y reintentar.
        /// </summary>
        public static bool IsPasswordRequired(Exception error)
        {
            var current = error;
            while (current != null)
            {
                var pdfiumError = current as PdfiumViewer.PdfException;
                if (pdfiumError != null)
                {
                    return pdfiumError.Error ==
                        PdfiumViewer.PdfError.PasswordProtected;
                }

                current = current.InnerException;
            }

            return false;
        }
    }
}

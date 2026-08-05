using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using iTextSharp.text;
using iTextSharp.text.pdf;

namespace FirmaAutomatica
{
    /// <summary>
    /// QA aislado del endurecimiento de la fase 9.
    ///
    /// Comprueba dos cosas sobre PDFs problematicos:
    ///
    /// 1. PdfProblemDiagnostics clasifica cada causa correctamente y nunca deja
    ///    escapar el texto ingles de PDFium o de iText.
    /// 2. PdfDocumentOpenService no deja el archivo bloqueado cuando la apertura
    ///    falla. Es la prueba de la fuga de handles de PdfiumViewer: si el stream
    ///    quedara vivo, el fixture no se podria borrar.
    /// </summary>
    internal static class HardeningEngineQa
    {
        private static string runDirectory;
        private static readonly List<string> Report = new List<string>();

        [STAThread]
        public static int Main()
        {
            runDirectory = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "run-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(runDirectory);

            try
            {
                RunClassificationChecks();
                RunOpenServiceChecks();
                RunHandleReleaseChecks();
                RunMessageLanguageChecks();

                Report.Insert(0, "RESULTADO=PASS");
                WriteReport();
                foreach (var line in Report)
                {
                    Console.WriteLine(line);
                }

                Console.WriteLine("RUN=" + runDirectory);
                return 0;
            }
            catch (Exception ex)
            {
                Report.Insert(0, "RESULTADO=FAIL");
                Report.Add(ex.ToString());
                WriteReport();
                Console.Error.WriteLine(string.Join(Environment.NewLine, Report));
                Console.Error.WriteLine("RUN=" + runDirectory);
                return 1;
            }
        }

        // ----------------------------------------------------------------- //
        // Clasificacion
        // ----------------------------------------------------------------- //

        private static void RunClassificationChecks()
        {
            // Excepciones ajenas puras.
            AssertKind(
                new iTextSharp.text.exceptions.BadPasswordException(
                    "Bad user password. Password is not provided or is incorrect."),
                null,
                PdfProblemKind.PasswordProtected,
                "BadPasswordException suelta");

            AssertKind(
                new iTextSharp.text.exceptions.InvalidPdfException(
                    "PDF header signature not found."),
                null,
                PdfProblemKind.Damaged,
                "InvalidPdfException suelta");

            AssertKind(
                new iTextSharp.text.exceptions.UnsupportedPdfException(
                    "The document was reused."),
                null,
                PdfProblemKind.Damaged,
                "UnsupportedPdfException hereda de InvalidPdfException");

            // El caso que motiva toda la clase: la excepcion ajena envuelta en una
            // propia. GetBaseException() habria devuelto el texto ingles.
            AssertKind(
                new UnauthorizedAccessException(
                    "El PDF esta protegido con contrasena.",
                    new iTextSharp.text.exceptions.BadPasswordException(
                        "Bad user password.")),
                null,
                PdfProblemKind.PasswordProtected,
                "BadPasswordException envuelta en UnauthorizedAccessException");

            // Doble envoltura, como en PdfTextEditService.Analyze.
            AssertKind(
                new InvalidDataException(
                    "No se pudo analizar el PDF para editar texto: Bad user password.",
                    new UnauthorizedAccessException(
                        "El PDF esta protegido con contrasena.",
                        new iTextSharp.text.exceptions.BadPasswordException(
                            "Bad user password."))),
                null,
                PdfProblemKind.PasswordProtected,
                "BadPasswordException con doble envoltura");

            // Bloqueos de politica propios, sin excepcion ajena dentro.
            AssertKind(
                new UnauthorizedAccessException(
                    "Los formularios PDF protegidos con contraseña todavía no se " +
                    "pueden guardar con Recovery de forma segura."),
                null,
                PdfProblemKind.RestrictedPermissions,
                "UnauthorizedAccessException de politica");

            AssertKind(
                new NotSupportedException("Los formularios XFA no se pueden rellenar."),
                null,
                PdfProblemKind.DynamicForm,
                "NotSupportedException es siempre XFA");

            // Sistema de archivos.
            AssertKind(
                new FileNotFoundException("No se encuentra el PDF."),
                null,
                PdfProblemKind.FileMissing,
                "FileNotFoundException");

            AssertKind(
                new DirectoryNotFoundException("La carpeta de destino ya no existe."),
                null,
                PdfProblemKind.FileMissing,
                "DirectoryNotFoundException");

            AssertKind(
                new EndOfStreamException("El PDF cambió mientras se comprobaba."),
                null,
                PdfProblemKind.Damaged,
                "EndOfStreamException");

            AssertKind(null, null, PdfProblemKind.Unknown, "excepcion nula");

            // Ruta: solo se consulta cuando nada se reconocio.
            var missingPath = Path.Combine(runDirectory, "no-existe.pdf");
            AssertKind(
                new Exception("algo raro"),
                missingPath,
                PdfProblemKind.FileMissing,
                "respaldo por ruta inexistente");

            var notPdfPath = Path.Combine(runDirectory, "no-es-pdf.txt");
            File.WriteAllText(notPdfPath, "texto");
            AssertKind(
                new Exception("algo raro"),
                notPdfPath,
                PdfProblemKind.NotPdf,
                "respaldo por extension distinta de .pdf");

            Report.Add("Clasificacion=PASS");
        }

        // ----------------------------------------------------------------- //
        // Apertura real con PDFium
        // ----------------------------------------------------------------- //

        private static void RunOpenServiceChecks()
        {
            var normalPath = Path.Combine(runDirectory, "normal.pdf");
            CreatePlainPdf(normalPath, 2);
            using (var document = PdfDocumentOpenService.Load(normalPath))
            {
                Assert(
                    document.PageCount == 2,
                    "el PDF normal no se abrio con sus dos paginas");
            }

            Assert(
                CanDeleteWhileKeepingFile(normalPath),
                "el PDF normal quedo bloqueado tras abrirlo y cerrarlo");

            // Cifrado con contrasena de usuario.
            var encryptedPath = Path.Combine(runDirectory, "con-contrasena.pdf");
            CreateEncryptedPdf(encryptedPath, "usuario", "propietario");

            var passwordError = CaptureFailure(encryptedPath, null);
            Assert(
                PdfDocumentOpenService.IsPasswordRequired(passwordError),
                "no se detecto que el PDF pide contrasena");
            AssertKind(
                passwordError,
                encryptedPath,
                PdfProblemKind.PasswordProtected,
                "PDF cifrado sin contrasena");

            using (var document = PdfDocumentOpenService.Load(encryptedPath, "usuario"))
            {
                Assert(
                    document.PageCount == 1,
                    "la contrasena correcta no abrio el documento");
            }

            var wrongPasswordError = CaptureFailure(encryptedPath, "incorrecta");
            Assert(
                PdfDocumentOpenService.IsPasswordRequired(wrongPasswordError),
                "una contrasena incorrecta deberia seguir pidiendo contrasena");

            // Solo contrasena de propietario: PDFium lo abre sin preguntar.
            var ownerOnlyPath = Path.Combine(runDirectory, "solo-propietario.pdf");
            CreateEncryptedPdf(ownerOnlyPath, string.Empty, "propietario");
            using (var document = PdfDocumentOpenService.Load(ownerOnlyPath))
            {
                Assert(
                    document.PageCount == 1,
                    "el PDF con solo contrasena de propietario deberia abrirse");
            }

            Report.Add("Apertura=PASS");
        }

        // ----------------------------------------------------------------- //
        // Liberacion del archivo tras un fallo (la fuga de PdfiumViewer)
        // ----------------------------------------------------------------- //

        private static void RunHandleReleaseChecks()
        {
            var cases = new List<KeyValuePair<string, PdfProblemKind>>();

            var notAPdfPath = Path.Combine(runDirectory, "corrupto.pdf");
            File.WriteAllText(notAPdfPath, "Esto no es un PDF.");
            cases.Add(new KeyValuePair<string, PdfProblemKind>(
                notAPdfPath, PdfProblemKind.Damaged));

            var truncatedPath = Path.Combine(runDirectory, "truncado.pdf");
            CreateTruncatedPdf(truncatedPath);
            cases.Add(new KeyValuePair<string, PdfProblemKind>(
                truncatedPath, PdfProblemKind.Damaged));

            var encryptedTruncatedPath = Path.Combine(
                runDirectory,
                "cifrado-truncado.pdf");
            CreateEncryptedTruncatedPdf(encryptedTruncatedPath);
            cases.Add(new KeyValuePair<string, PdfProblemKind>(
                encryptedTruncatedPath, PdfProblemKind.Damaged));

            var renamedImagePath = Path.Combine(runDirectory, "imagen-renombrada.pdf");
            File.WriteAllBytes(
                renamedImagePath,
                new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13 });
            cases.Add(new KeyValuePair<string, PdfProblemKind>(
                renamedImagePath, PdfProblemKind.Damaged));

            foreach (var problematic in cases)
            {
                var path = problematic.Key;
                var name = Path.GetFileName(path);

                var error = CaptureFailure(path, null);
                Assert(
                    error != null,
                    "abrir " + name + " deberia haber fallado");
                AssertKind(error, path, problematic.Value, name);

                // La prueba clave: si PdfiumViewer hubiera dejado el FileStream
                // abierto, este borrado lanzaria IOException.
                Assert(
                    CanDeleteWhileKeepingFile(path),
                    name + " quedo bloqueado tras el fallo de apertura");
            }

            // Un PDF cifrado tampoco debe quedar bloqueado tras varios intentos
            // fallidos seguidos, que es el caso real del usuario tecleando mal.
            var encryptedPath = Path.Combine(runDirectory, "con-contrasena.pdf");
            for (var attempt = 0; attempt < 5; attempt++)
            {
                CaptureFailure(encryptedPath, "intento-" + attempt);
            }

            Assert(
                CanDeleteWhileKeepingFile(encryptedPath),
                "el PDF cifrado quedo bloqueado tras cinco contrasenas erroneas");

            // Archivo inexistente y archivo bloqueado por otro handle.
            var missingPath = Path.Combine(runDirectory, "desaparecido.pdf");
            AssertKind(
                CaptureFailure(missingPath, null),
                missingPath,
                PdfProblemKind.FileMissing,
                "PDF inexistente");

            var lockedPath = Path.Combine(runDirectory, "bloqueado.pdf");
            CreatePlainPdf(lockedPath, 1);
            using (new FileStream(
                lockedPath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None))
            {
                var lockedError = CaptureFailure(lockedPath, null);
                Assert(
                    lockedError != null,
                    "abrir un PDF bloqueado por otro proceso deberia fallar");
                AssertKind(
                    lockedError,
                    lockedPath,
                    PdfProblemKind.FileLocked,
                    "PDF en uso por otro programa");
            }

            Report.Add("LiberacionDeArchivo=PASS");
        }

        // ----------------------------------------------------------------- //
        // Ningun texto ingles llega al usuario
        // ----------------------------------------------------------------- //

        private static void RunMessageLanguageChecks()
        {
            var foreignFragments = new[]
            {
                "Bad user password",
                "PDF header signature not found",
                "Password required or incorrect password",
                "File not in PDF format or corrupted",
                "File not found or could not be opened",
                "Unsupported security scheme",
                "Page not found or content error",
                "Unknown error"
            };

            var samples = new List<Exception>
            {
                new iTextSharp.text.exceptions.BadPasswordException(
                    "Bad user password. Password is not provided or is incorrect."),
                new iTextSharp.text.exceptions.InvalidPdfException(
                    "PDF header signature not found."),
                new UnauthorizedAccessException(
                    "El PDF esta protegido con contrasena.",
                    new iTextSharp.text.exceptions.BadPasswordException(
                        "Bad user password.")),
                new InvalidDataException(
                    "No se pudo incorporar \"x.pdf\": Bad user password.",
                    new iTextSharp.text.exceptions.BadPasswordException(
                        "Bad user password."))
            };

            var encryptedPath = Path.Combine(runDirectory, "idioma-cifrado.pdf");
            CreateEncryptedPdf(encryptedPath, "usuario", "propietario");
            samples.Add(CaptureFailure(encryptedPath, null));

            var brokenPath = Path.Combine(runDirectory, "idioma-roto.pdf");
            File.WriteAllText(brokenPath, "Esto no es un PDF.");
            samples.Add(CaptureFailure(brokenPath, null));

            foreach (var sample in samples)
            {
                var report = PdfProblemDiagnostics.Analyze(sample, null);
                Assert(
                    !string.IsNullOrWhiteSpace(report.Description),
                    "el diagnostico se quedo sin descripcion");

                var text = report.Description +
                    " " +
                    (report.Advice == null ? string.Empty : report.Advice);
                foreach (var fragment in foreignFragments)
                {
                    Assert(
                        text.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) < 0,
                        "el texto mostrado contiene el mensaje ingles \"" +
                        fragment + "\": " + text);
                }
            }

            // Los bloqueos de politica se marcan como tales para que la interfaz
            // use aviso y no error.
            Assert(
                PdfProblemDiagnostics.IsPolicyBlock(PdfProblemKind.PasswordProtected) &&
                PdfProblemDiagnostics.IsPolicyBlock(PdfProblemKind.RestrictedPermissions) &&
                PdfProblemDiagnostics.IsPolicyBlock(PdfProblemKind.UnsupportedSecurity) &&
                PdfProblemDiagnostics.IsPolicyBlock(PdfProblemKind.DynamicForm),
                "un bloqueo de politica no se marco como tal");
            Assert(
                !PdfProblemDiagnostics.IsPolicyBlock(PdfProblemKind.Damaged) &&
                !PdfProblemDiagnostics.IsPolicyBlock(PdfProblemKind.FileMissing) &&
                !PdfProblemDiagnostics.IsPolicyBlock(PdfProblemKind.FileLocked),
                "un fallo real se marco como bloqueo de politica");

            // Un mensaje propio, sin excepcion ajena, se conserva tal cual: es mas
            // preciso que cualquier texto generico.
            var ownMessage = "Los formularios XFA no se pueden rellenar de forma " +
                "segura en esta versión.";
            var ownReport = PdfProblemDiagnostics.Analyze(
                new NotSupportedException(ownMessage),
                null);
            Assert(
                ownReport.Description == ownMessage,
                "se perdio el mensaje propio del servicio: " + ownReport.Description);

            Report.Add("Idioma=PASS");
        }

        // ----------------------------------------------------------------- //
        // Utilidades
        // ----------------------------------------------------------------- //

        private static Exception CaptureFailure(string path, string password)
        {
            try
            {
                using (var document = PdfDocumentOpenService.Load(path, password))
                {
                    return null;
                }
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        /// <summary>
        /// Comprueba que el archivo se puede sustituir, que es lo que un usuario
        /// intentaria desde el Explorador, y lo deja como estaba.
        /// </summary>
        private static bool CanDeleteWhileKeepingFile(string path)
        {
            var backup = path + ".copia";
            try
            {
                File.Copy(path, backup, true);
                File.Delete(path);
                File.Move(backup, path);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            finally
            {
                if (File.Exists(backup) && File.Exists(path))
                {
                    try
                    {
                        File.Delete(backup);
                    }
                    catch (IOException)
                    {
                    }
                }
            }
        }

        private static void CreatePlainPdf(string path, int pageCount)
        {
            using (var output = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var document = new Document(PageSize.A4))
            {
                PdfWriter.GetInstance(document, output);
                document.Open();
                for (var page = 0; page < pageCount; page++)
                {
                    if (page > 0)
                    {
                        document.NewPage();
                    }

                    document.Add(new Paragraph("Página " + (page + 1)));
                }

                document.Close();
            }
        }

        private static void CreateEncryptedPdf(
            string path,
            string userPassword,
            string ownerPassword)
        {
            using (var output = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var document = new Document(PageSize.A4))
            {
                var writer = PdfWriter.GetInstance(document, output);
                writer.SetEncryption(
                    Encoding.ASCII.GetBytes(userPassword),
                    Encoding.ASCII.GetBytes(ownerPassword),
                    PdfWriter.ALLOW_PRINTING,
                    PdfWriter.ENCRYPTION_AES_128);
                document.Open();
                document.Add(new Paragraph("Documento protegido"));
                document.Close();
            }
        }

        private static void CreateTruncatedPdf(string path)
        {
            var completePath = path + ".completo";
            CreatePlainPdf(completePath, 3);
            var bytes = File.ReadAllBytes(completePath);
            File.Delete(completePath);

            var keep = (int)(bytes.Length * 0.6);
            var truncated = new byte[keep];
            Array.Copy(bytes, truncated, keep);
            File.WriteAllBytes(path, truncated);
        }

        private static void CreateEncryptedTruncatedPdf(string path)
        {
            var completePath = path + ".completo";
            CreateEncryptedPdf(completePath, "usuario", "propietario");
            var bytes = File.ReadAllBytes(completePath);
            File.Delete(completePath);

            var keep = (int)(bytes.Length * 0.6);
            var truncated = new byte[keep];
            Array.Copy(bytes, truncated, keep);
            File.WriteAllBytes(path, truncated);
        }

        private static void AssertKind(
            Exception error,
            string path,
            PdfProblemKind expected,
            string description)
        {
            var actual = PdfProblemDiagnostics.Classify(error, path);
            Assert(
                actual == expected,
                description + ": se esperaba " + expected + " y se obtuvo " + actual);
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException("Fallo QA: " + message);
            }
        }

        private static void WriteReport()
        {
            File.WriteAllLines(
                Path.Combine(runDirectory, "qa-report.txt"),
                Report,
                Encoding.UTF8);
        }
    }
}

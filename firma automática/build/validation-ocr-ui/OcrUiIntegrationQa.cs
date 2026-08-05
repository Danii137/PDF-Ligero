using System;
using System.Collections;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using iTextSharp.text.pdf;
using LocationTextExtractionStrategy =
    iTextSharp.text.pdf.parser.LocationTextExtractionStrategy;
using PdfTextExtractor =
    iTextSharp.text.pdf.parser.PdfTextExtractor;

namespace FirmaAutomatica
{
    internal static class OcrUiIntegrationQa
    {
        private static bool optionsAccepted;
        private static bool reviewAccepted;
        private static bool reviewCaptured;
        private static bool progressCaptured;
        private static DateTime reviewSeenUtc;
        private static string captureDirectory;

        [STAThread]
        private static int Main(string[] args)
        {
            if (args == null || args.Length < 2)
            {
                Console.Error.WriteLine(
                    "Uso: OcrUiIntegrationQa.exe <carpeta> <fixture.pdf>");
                return 2;
            }

            var validationRoot = Path.GetFullPath(args[0]);
            var fixture = Path.GetFullPath(args[1]);
            Directory.CreateDirectory(validationRoot);
            captureDirectory = Path.Combine(validationRoot, "captures");
            Directory.CreateDirectory(captureDirectory);
            var recoveryRoot = Path.Combine(validationRoot, "recovery");
            if (Directory.Exists(recoveryRoot))
            {
                Directory.Delete(recoveryRoot, true);
            }

            Directory.CreateDirectory(recoveryRoot);
            Environment.SetEnvironmentVariable(
                PdfEditSession.RecoveryRootOverrideEnvironmentVariable,
                recoveryRoot);

            var sourcePath = Path.Combine(
                validationRoot,
                "integracion OCR - Málaga.pdf");
            File.Copy(fixture, sourcePath, true);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            PdfViewerForm viewer = null;
            var report = new StringBuilder();
            try
            {
                viewer = new PdfViewerForm(new[] { sourcePath });
                viewer.Width = 1100;
                viewer.Height = 760;
                viewer.Show();
                viewer.Activate();

                PumpUntil(
                    delegate
                    {
                        var workspace = GetField(viewer, "activeWorkspace");
                        return workspace != null &&
                            (bool)GetField(workspace, "IsLoaded");
                    },
                    20000,
                    "El visor no terminó de abrir el fixture.");

                var ocrButton = FindControl<Button>(
                    viewer,
                    delegate(Button button)
                    {
                        return string.Equals(
                            button.AccessibleName,
                            "OCR, orientación y enderezado",
                            StringComparison.Ordinal);
                    });
                Assert(
                    ocrButton != null && ocrButton.Enabled,
                    "El botón OCR no está disponible.");

                using (var automationTimer = new System.Windows.Forms.Timer())
                {
                    automationTimer.Interval = 100;
                    automationTimer.Tick += AutomationTimer_Tick;
                    automationTimer.Start();
                    ocrButton.PerformClick();

                    PumpUntil(
                        delegate
                        {
                            return !(bool)GetField(viewer, "ocrInProgress");
                        },
                        150000,
                        "El flujo OCR de interfaz no terminó.");
                    automationTimer.Stop();
                }

                Assert(optionsAccepted, "No se pudo aceptar Opciones OCR.");
                Assert(reviewAccepted, "No se pudo aceptar la revisión OCR.");

                var activeWorkspace = GetField(viewer, "activeWorkspace");
                var resultPath =
                    (string)GetField(activeWorkspace, "ContentPath");
                Assert(
                    !string.Equals(
                        sourcePath,
                        resultPath,
                        StringComparison.OrdinalIgnoreCase),
                    "El resultado OCR no se activó como revisión.");
                Assert(File.Exists(resultPath), "La revisión OCR no existe.");

                var editSession =
                    (PdfEditSession)GetField(activeWorkspace, "EditSession");
                Assert(
                    editSession.HasUnsavedChanges,
                    "La revisión OCR no entró en el historial.");
                Assert(
                    CountMeaningfulCharacters(
                        ExtractPageText(resultPath, 1)) > 100,
                    "La página 1 no quedó buscable.");
                Assert(
                    CountMeaningfulCharacters(
                        ExtractPageText(resultPath, 2)) > 100,
                    "La página 2 no quedó buscable.");

                Invoke(viewer, "ShowSearchPanel");
                var searchTextBox =
                    (TextBox)GetField(viewer, "searchTextBox");
                searchTextBox.Text = "ARQUITECTURA";
                Application.DoEvents();
                Invoke(viewer, "PerformSearch", activeWorkspace);
                var matches = GetField(activeWorkspace, "SearchMatches");
                Assert(
                    GetCollectionCount(
                        GetProperty(matches, "Items")) > 0,
                    "Ctrl+F no encuentra el texto añadido por OCR.");

                CaptureForm(
                    viewer,
                    Path.Combine(
                        captureDirectory,
                        "03-resultado-ocr-y-busqueda.png"));

                Invoke(viewer, "UndoActiveDocument");
                activeWorkspace = GetField(viewer, "activeWorkspace");
                Assert(
                    string.Equals(
                        sourcePath,
                        (string)GetField(activeWorkspace, "ContentPath"),
                        StringComparison.OrdinalIgnoreCase),
                    "Deshacer no volvió al PDF anterior.");

                Invoke(viewer, "RedoActiveDocument");
                activeWorkspace = GetField(viewer, "activeWorkspace");
                Assert(
                    !string.Equals(
                        sourcePath,
                        (string)GetField(activeWorkspace, "ContentPath"),
                        StringComparison.OrdinalIgnoreCase),
                    "Rehacer no restauró la revisión OCR.");

                report.AppendLine("PASS: integración OCR de interfaz.");
                report.AppendLine("Opciones -> análisis -> revisión -> aplicar: PASS");
                report.AppendLine("Búsqueda Ctrl+F sobre capa OCR: PASS");
                report.AppendLine("Deshacer/Rehacer de la revisión OCR: PASS");
                report.AppendLine("Preview en segundo plano: PASS");
                report.AppendLine(
                    "Captura revisión: " +
                    (reviewCaptured ? "PASS" : "NO CAPTURADA"));
                report.AppendLine(
                    "Captura progreso: " +
                    (progressCaptured ? "PASS" : "NO CAPTURADA"));
                report.AppendLine("Resultado: " + resultPath);
                File.WriteAllText(
                    Path.Combine(validationRoot, "qa-report.txt"),
                    report.ToString(),
                    Encoding.UTF8);
                Console.Write(report.ToString());
                return 0;
            }
            catch (Exception ex)
            {
                report.AppendLine("FAIL: " + ex);
                File.WriteAllText(
                    Path.Combine(validationRoot, "qa-report.txt"),
                    report.ToString(),
                    Encoding.UTF8);
                Console.Error.Write(report.ToString());
                return 1;
            }
            finally
            {
                if (viewer != null)
                {
                    viewer.Dispose();
                }
            }
        }

        private static void AutomationTimer_Tick(
            object sender,
            EventArgs e)
        {
            foreach (Form form in Application.OpenForms.Cast<Form>().ToArray())
            {
                var options = form as PdfOcrOptionsForm;
                if (options != null && !optionsAccepted)
                {
                    var allPages = FindControl<RadioButton>(
                        options,
                        delegate(RadioButton radio)
                        {
                            return radio.Text.IndexOf(
                                "Todo el documento",
                                StringComparison.OrdinalIgnoreCase) >= 0;
                        });
                    if (allPages != null)
                    {
                        allPages.Checked = true;
                    }

                    var analyze = FindControl<Button>(
                        options,
                        delegate(Button button)
                        {
                            return string.Equals(
                                button.Text,
                                "Analizar",
                                StringComparison.Ordinal);
                        });
                    if (analyze != null && analyze.Enabled)
                    {
                        optionsAccepted = true;
                        analyze.PerformClick();
                    }

                    continue;
                }

                var review = form as PdfOcrReviewForm;
                if (review != null && !reviewAccepted)
                {
                    if (reviewSeenUtc == default(DateTime))
                    {
                        reviewSeenUtc = DateTime.UtcNow;
                    }

                    if ((DateTime.UtcNow - reviewSeenUtc).TotalMilliseconds <
                        900D)
                    {
                        continue;
                    }

                    if (!reviewCaptured)
                    {
                        CaptureForm(
                            review,
                            Path.Combine(
                                captureDirectory,
                                "02-revision-ocr.png"));
                        reviewCaptured = true;
                    }

                    var apply = FindControl<Button>(
                        review,
                        delegate(Button button)
                        {
                            return string.Equals(
                                button.Text,
                                "Aplicar",
                                StringComparison.Ordinal);
                        });
                    if (apply != null && apply.Enabled)
                    {
                        reviewAccepted = true;
                        apply.PerformClick();
                    }

                    continue;
                }

                var progress = form as PdfOcrProgressForm;
                if (progress != null && !progressCaptured)
                {
                    CaptureForm(
                        progress,
                        Path.Combine(
                            captureDirectory,
                            "01-progreso-ocr.png"));
                    progressCaptured = true;
                }
            }
        }

        private static void PumpUntil(
            Func<bool> condition,
            int timeoutMilliseconds,
            string failureMessage)
        {
            var stopwatch = Stopwatch.StartNew();
            while (!condition())
            {
                Application.DoEvents();
                Thread.Sleep(20);
                if (stopwatch.ElapsedMilliseconds > timeoutMilliseconds)
                {
                    throw new TimeoutException(failureMessage);
                }
            }

            Application.DoEvents();
        }

        private static T FindControl<T>(
            Control parent,
            Func<T, bool> predicate)
            where T : Control
        {
            foreach (Control child in parent.Controls)
            {
                var typed = child as T;
                if (typed != null && predicate(typed))
                {
                    return typed;
                }

                var nested = FindControl(child, predicate);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private static void CaptureForm(Form form, string path)
        {
            if (form == null ||
                form.IsDisposed ||
                form.Width < 1 ||
                form.Height < 1)
            {
                return;
            }

            using (var bitmap = new Bitmap(
                form.Width,
                form.Height))
            {
                form.DrawToBitmap(
                    bitmap,
                    new Rectangle(
                        Point.Empty,
                        form.Size));
                bitmap.Save(
                    path,
                    System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        private static string ExtractPageText(
            string path,
            int pageNumber)
        {
            using (var reader = new PdfReader(path))
            {
                return PdfTextExtractor.GetTextFromPage(
                    reader,
                    pageNumber,
                    new LocationTextExtractionStrategy()) ??
                    string.Empty;
            }
        }

        private static int CountMeaningfulCharacters(string value)
        {
            return (value ?? string.Empty).Count(
                character =>
                    !char.IsWhiteSpace(character) &&
                    !char.IsControl(character));
        }

        private static object GetField(object instance, string name)
        {
            Assert(instance != null, "No existe el objeto para leer " + name);
            var field = instance.GetType().GetField(
                name,
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);
            Assert(field != null, "No existe el campo " + name);
            return field.GetValue(instance);
        }

        private static object GetProperty(object instance, string name)
        {
            Assert(
                instance != null,
                "No existe el objeto para leer la propiedad " + name);
            var property = instance.GetType().GetProperty(
                name,
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);
            Assert(property != null, "No existe la propiedad " + name);
            return property.GetValue(instance, null);
        }

        private static int GetCollectionCount(object value)
        {
            var collection = value as ICollection;
            if (collection != null)
            {
                return collection.Count;
            }

            var count = GetProperty(value, "Count");
            return Convert.ToInt32(count);
        }

        private static object Invoke(
            object instance,
            string name,
            params object[] arguments)
        {
            var method = instance.GetType().GetMethod(
                name,
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);
            Assert(method != null, "No existe el método " + name);
            return method.Invoke(instance, arguments);
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidDataException(message);
            }
        }
    }
}

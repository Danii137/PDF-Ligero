using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace FirmaAutomatica
{
    internal static class Program
    {
        private const int MaximumBatchFiles = 50;

        [STAThread]
        private static void Main(string[] args)
        {
            try
            {
                AppLog.Write("Inicio. Args=" + string.Join(" | ", args ?? new string[0]));
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                var launchArguments = (args ?? new string[0]).ToList();
                var mergeMode = launchArguments.Any(argument =>
                    string.Equals(argument, "--merge", StringComparison.OrdinalIgnoreCase));
                var openMode = launchArguments.Count == 0 ||
                    string.Equals(launchArguments[0], "--open", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(launchArguments[0], "--view", StringComparison.OrdinalIgnoreCase);

                if (mergeMode)
                {
                    var mergeArguments = launchArguments
                        .Where(argument => !string.Equals(argument, "--merge", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    var mergeFiles = SingleInstanceCoordinator.CollectInvocationFiles(mergeArguments, "Merge");
                    if (mergeFiles == null)
                    {
                        AppLog.Write("Instancia secundaria de combinacion. Salida silenciosa.");
                        return;
                    }

                    RunMergeFiles(mergeFiles);
                    return;
                }

                if (openMode)
                {
                    var openArguments = launchArguments
                        .Where(argument =>
                            !string.Equals(argument, "--open", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(argument, "--view", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    var initialPdfs = CliArgumentParser.Parse(openArguments);
                    AppLog.Write(
                        "Iniciando visor. PDFs iniciales=" +
                        string.Join(" | ", initialPdfs));

                    ViewerInstanceBroker viewerBroker;
                    if (!ViewerInstanceBroker.TryStart(initialPdfs, out viewerBroker))
                    {
                        AppLog.Write(
                            "Invocacion enviada a la instancia existente del visor.");
                        return;
                    }

                    using (viewerBroker)
                    using (var viewerForm = new PdfViewerForm(initialPdfs))
                    {
                        var viewerWindowHandle = viewerForm.Handle;
                        AppLog.Write(
                            "Handle del visor preparado: " +
                            viewerWindowHandle.ToInt64());
                        viewerBroker.PdfPathsReceived += delegate(
                            IReadOnlyList<string> receivedPaths)
                        {
                            try
                            {
                                viewerForm.BeginInvoke(new Action(delegate
                                {
                                    if (viewerForm.IsClosingForViewerRequests)
                                    {
                                        return;
                                    }

                                    if (receivedPaths != null &&
                                        receivedPaths.Count > 0)
                                    {
                                        viewerForm.OpenPdfTabs(receivedPaths);
                                    }

                                    if (viewerForm.WindowState ==
                                        FormWindowState.Minimized)
                                    {
                                        viewerForm.WindowState =
                                            FormWindowState.Normal;
                                    }

                                    viewerForm.Show();
                                    viewerForm.BringToFront();
                                    viewerForm.Activate();
                                }));
                            }
                            catch (InvalidOperationException)
                            {
                            }
                        };

                        Application.Run(viewerForm);
                    }
                    return;
                }

                var signingArguments = launchArguments
                    .Where(argument => !string.Equals(argument, "--sign", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var pdfFiles = SingleInstanceCoordinator.CollectInvocationFiles(signingArguments, "Sign");
                if (pdfFiles == null)
                {
                    AppLog.Write("Instancia secundaria. Salida silenciosa.");
                    return;
                }

                RunSigningFiles(pdfFiles);
            }
            catch (Exception ex)
            {
                AppLog.Write("Excepcion: " + ex);
                MessageBox.Show(
                    ex.Message,
                    "Error en PDF Ligero",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        internal static void RunMergeFiles(IReadOnlyList<string> pdfFiles)
        {
            try
            {
                var validFiles = pdfFiles == null
                    ? new List<string>()
                    : pdfFiles
                        .Where(File.Exists)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(MaximumBatchFiles + 1)
                        .ToList();

                if (validFiles.Count > MaximumBatchFiles)
                {
                    MessageBox.Show(
                        string.Format(
                            "Has seleccionado mas de {0} PDFs. Selecciona un maximo de {0} documentos por tanda.",
                            MaximumBatchFiles),
                        "PDF Ligero",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                AppLog.Write("Mostrando combinador para " + validFiles.Count + " PDF(s).");
                using (var mergeForm = new PdfMergeForm(validFiles))
                {
                    mergeForm.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                AppLog.Write("Excepcion durante la combinacion: " + ex);
                MessageBox.Show(
                    ex.Message,
                    "Error al combinar",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        internal static void RunSigningFiles(IReadOnlyList<string> pdfFiles)
        {
            if (pdfFiles == null)
            {
                return;
            }

            try
            {
                if (pdfFiles.Count == 0)
                {
                    AppLog.Write("Sin PDFs validos tras parseo.");
                    MessageBox.Show(
                        "Selecciona uno o varios PDF desde el Explorador y usa \"Firmar PDFs\".",
                        "PDF Ligero",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                if (pdfFiles.Count > MaximumBatchFiles)
                {
                    AppLog.Write("Lote rechazado por superar el maximo permitido. Total=" + pdfFiles.Count);
                    MessageBox.Show(
                        string.Format(
                            "Has seleccionado {0} PDFs.{1}{1}Por estabilidad, el maximo por tanda es de {2} documentos. Selecciona 50 o menos y vuelve a intentarlo.",
                            pdfFiles.Count,
                            Environment.NewLine,
                            MaximumBatchFiles),
                        "PDF Ligero",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                AppLog.Write("Mostrando dialogo de certificado para " + pdfFiles.Count + " PDF(s).");
                using (var certificateDialog = new CertificateDialog())
                {
                    if (certificateDialog.ShowDialog() != DialogResult.OK)
                    {
                        AppLog.Write("Dialogo de certificado cancelado.");
                        return;
                    }

                    AppLog.Write("Certificado seleccionado: " + certificateDialog.SelectedCertificateLabel);
                    using (var flow = new SigningFlowController(pdfFiles, certificateDialog.SelectedCertificate))
                    {
                        flow.Run();
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Write("Excepcion durante la firma: " + ex);
                MessageBox.Show(
                    ex.Message,
                    "Error en la firma",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }

    internal static class SingleInstanceCoordinator
    {
        private const int InitialAggregationWindowMs = 1600;
        private const int SubsequentAggregationWindowMs = 700;

        public static List<string> CollectInvocationFiles(IEnumerable<string> args, string instanceScope)
        {
            var initialFiles = CliArgumentParser.Parse(args);
            AppLog.Write("Parse inicial: " + initialFiles.Count + " archivo(s).");
            var safeScope = string.IsNullOrWhiteSpace(instanceScope) ? "Default" : instanceScope.Trim();
            var mutexName = "FirmaAutomatica" + safeScope + "Mutex";
            var pipeName = "FirmaAutomatica" + safeScope + "Pipe";
            var createdNew = false;
            using (var mutex = new Mutex(true, mutexName, out createdNew))
            {
                if (!createdNew)
                {
                    AppLog.Write("Mutex existente. Enviando a instancia primaria.");
                    if (SendToPrimaryInstance(initialFiles, pipeName))
                    {
                        return null;
                    }

                    AppLog.Write("La instancia primaria ya no aceptaba archivos. Continuando en esta instancia.");
                    return initialFiles;
                }

                AppLog.Write("Instancia primaria creada.");
                return CollectAsPrimary(initialFiles, pipeName);
            }
        }

        private static List<string> CollectAsPrimary(List<string> initialFiles, string pipeName)
        {
            var collected = new List<string>(initialFiles);
            var silenceDeadline = DateTime.UtcNow.AddMilliseconds(InitialAggregationWindowMs);

            while (DateTime.UtcNow < silenceDeadline)
            {
                using (var server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
                {
                    var asyncResult = server.BeginWaitForConnection(null, null);
                    var remaining = silenceDeadline - DateTime.UtcNow;
                    if (remaining.TotalMilliseconds <= 0)
                    {
                        break;
                    }

                    if (!asyncResult.AsyncWaitHandle.WaitOne(remaining))
                    {
                        AppLog.Write("Ventana de agregacion cerrada. Total=" + collected.Count);
                        break;
                    }

                    server.EndWaitForConnection(asyncResult);
                    using (var reader = new StreamReader(server))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            collected.Add(line);
                            AppLog.Write("Archivo agregado desde instancia secundaria: " + line);
                        }
                    }

                    silenceDeadline = DateTime.UtcNow.AddMilliseconds(SubsequentAggregationWindowMs);
                }
            }

            return collected
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool SendToPrimaryInstance(IEnumerable<string> files, string pipeName)
        {
            try
            {
                AppLog.Write("Conectando a la tuberia de instancia primaria.");
                using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out))
                {
                    client.Connect(1500);
                    using (var writer = new StreamWriter(client))
                    {
                        foreach (var file in files)
                        {
                            writer.WriteLine(file);
                            AppLog.Write("Archivo enviado a primaria: " + file);
                        }

                        writer.Flush();
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                AppLog.Write("Fallo enviando a primaria: " + ex.Message);
                return false;
            }
        }
    }

    internal static class CliArgumentParser
    {
        public static List<string> Parse(IEnumerable<string> args)
        {
            return args
                .Where(arg => !string.IsNullOrWhiteSpace(arg))
                .Select(arg => arg.Trim().Trim('"'))
                .Where(File.Exists)
                .Where(path => string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    internal static class AppLog
    {
        private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "FirmaAutomatica.log");
        private const string LogMutexName = "FirmaAutomaticaLogMutex";

        public static void Write(string message)
        {
            Mutex mutex = null;
            var mutexTaken = false;
            try
            {
                mutex = new Mutex(false, LogMutexName);
                try
                {
                    mutexTaken = mutex.WaitOne(250);
                }
                catch (AbandonedMutexException)
                {
                    mutexTaken = true;
                }

                var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + Environment.NewLine;
                for (var attempt = 0; attempt < 4; attempt++)
                {
                    try
                    {
                        using (var stream = new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                        using (var writer = new StreamWriter(stream))
                        {
                            writer.Write(line);
                        }

                        return;
                    }
                    catch (IOException)
                    {
                        Thread.Sleep(15 * (attempt + 1));
                    }
                }
            }
            catch
            {
            }
            finally
            {
                if (mutexTaken && mutex != null)
                {
                    try
                    {
                        mutex.ReleaseMutex();
                    }
                    catch
                    {
                    }
                }

                if (mutex != null)
                {
                    mutex.Dispose();
                }
            }
        }
    }
}

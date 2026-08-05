using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using iTextSharp.text.pdf;

namespace FirmaAutomatica
{
    internal static class InsertUiHarness
    {
        private static readonly BindingFlags InstancePrivate =
            BindingFlags.Instance | BindingFlags.NonPublic;

        private static readonly List<string> Report = new List<string>();

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length != 3)
            {
                Console.Error.WriteLine(
                    "Uso: InsertUiHarness <directorio-salida> <base-2-paginas> <insert-80-paginas>");
                return 2;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string root = Path.GetFullPath(args[0]);
            string baseFixture = Path.GetFullPath(args[1]);
            string insertFixture = Path.GetFullPath(args[2]);
            Directory.CreateDirectory(root);

            var failures = new List<string>();
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
            {
                failures.Add("Excepción de UI: " + e.Exception);
            };

            using (var bootstrap = new Form())
            {
                bootstrap.ShowInTaskbar = false;
                bootstrap.StartPosition = FormStartPosition.Manual;
                bootstrap.Location = new Point(-20000, -20000);
                bootstrap.Opacity = 0d;
                bootstrap.Shown += delegate
                {
                    bootstrap.BeginInvoke(new Action(delegate
                    {
                        try
                        {
                            RunNormalAndDoubleDropScenario(
                                Path.Combine(root, "normal-double-drop"),
                                baseFixture,
                                insertFixture);
                            RunSourceTabClosedScenario(
                                Path.Combine(root, "source-tab-closed"),
                                baseFixture,
                                insertFixture);
                            RunFormCloseGuardScenario(
                                Path.Combine(root, "form-close-guard"),
                                baseFixture,
                                insertFixture);
                            RunInvalidPdfScenario(
                                Path.Combine(root, "invalid-pdf"),
                                baseFixture);
                        }
                        catch (Exception ex)
                        {
                            failures.Add(ex.ToString());
                        }
                        finally
                        {
                            Report.Add("UI thread exceptions: " + failures.Count);
                            Report.AddRange(failures);
                            File.WriteAllLines(
                                Path.Combine(root, "report.txt"),
                                Report.ToArray());
                            bootstrap.Close();
                        }
                    }));
                };
                Application.Run(bootstrap);
            }

            foreach (string line in Report)
            {
                Console.WriteLine(line);
            }

            return failures.Count == 0 ? 0 : 1;
        }

        private static void RunNormalAndDoubleDropScenario(
            string scenarioDirectory,
            string baseFixture,
            string insertFixture)
        {
            Directory.CreateDirectory(scenarioDirectory);
            string basePath = Path.Combine(scenarioDirectory, "base.pdf");
            string insertedPath = Path.Combine(scenarioDirectory, "insert.pdf");
            File.Copy(baseFixture, basePath, true);
            File.Copy(insertFixture, insertedPath, true);

            using (var form = CreateHiddenViewer())
            {
                form.OpenPdfTabs(new[] { basePath });
                object sourceWorkspace = WaitForLoadedWorkspace(form, 10000);
                var thumbnails = (PdfThumbnailList)GetWorkspaceField(
                    sourceWorkspace,
                    "Thumbnails");

                int insertionEvents = 0;
                int genericDropEvents = 0;
                thumbnails.PdfFilesInsertRequested +=
                    delegate { insertionEvents++; };
                thumbnails.DragDrop += delegate { genericDropEvents++; };

                int uiTimerTicks = 0;
                bool postedUiCallbackRan = false;
                using (var timer = new System.Windows.Forms.Timer())
                {
                    timer.Interval = 1;
                    timer.Tick += delegate { uiTimerTicks++; };
                    timer.Start();

                    DragEventArgs dragEnter = CreateDragArgs(
                        thumbnails,
                        insertedPath,
                        1);
                    InvokeDragMethod(thumbnails, "OnDragEnter", dragEnter);
                    Assert(
                        dragEnter.Effect == DragDropEffects.Copy,
                        "DragEnter no aceptó el PDF como copia.");

                    DragEventArgs dragOver = CreateDragArgs(
                        thumbnails,
                        insertedPath,
                        1);
                    InvokeDragMethod(thumbnails, "OnDragOver", dragOver);
                    int indicatedIndex = (int)GetField(
                        thumbnails,
                        "dragInsertionPageIndex");
                    Assert(
                        indicatedIndex == 1,
                        "La línea de inserción quedó en " + indicatedIndex +
                        " en vez de entre las páginas 1 y 2.");

                    var firstDropWatch = Stopwatch.StartNew();
                    InvokeDragMethod(
                        thumbnails,
                        "OnDragDrop",
                        CreateDragArgs(thumbnails, insertedPath, 1));
                    firstDropWatch.Stop();
                    Assert(
                        firstDropWatch.ElapsedMilliseconds < 500,
                        "El drop bloqueó la UI durante " +
                        firstDropWatch.ElapsedMilliseconds + " ms.");
                    Assert(
                        (bool)GetField(form, "pageInsertInProgress"),
                        "La operación no quedó marcada como asíncrona tras el drop.");
                    Assert(
                        insertionEvents == 1,
                        "El primer drop emitió " + insertionEvents +
                        " eventos específicos.");
                    Assert(
                        genericDropEvents == 0,
                        "El drop también se propagó al manejador genérico.");

                    form.BeginInvoke(new Action(delegate
                    {
                        postedUiCallbackRan = true;
                    }));

                    // A second immediate drop must be rejected while the worker is busy.
                    InvokeDragMethod(
                        thumbnails,
                        "OnDragDrop",
                        CreateDragArgs(thumbnails, insertedPath, 1));
                    Assert(
                        insertionEvents == 2,
                        "El control no notificó el segundo gesto para que el formulario lo rechazase.");
                    Assert(
                        genericDropEvents == 0,
                        "El segundo drop se propagó al manejador genérico.");

                    PumpUntil(
                        delegate
                        {
                            return !(bool)GetField(form, "pageInsertInProgress") &&
                                GetWorkspaces(form).Count == 2;
                        },
                        30000,
                        "La inserción asíncrona no terminó o no abrió una pestaña nueva.");
                    timer.Stop();

                    Assert(postedUiCallbackRan, "La cola de UI no respondió durante la inserción.");
                    Assert(uiTimerTicks > 0, "El temporizador de UI no pudo procesar ningún tick.");
                    VerifyResultWorkspace(
                        form,
                        basePath,
                        1,
                        82,
                        expectedWorkspaceCount: 2);

                    string[] outputs = Directory.GetFiles(
                        scenarioDirectory,
                        "base_editado*.pdf",
                        SearchOption.TopDirectoryOnly);
                    Assert(
                        outputs.Length == 1,
                        "El doble drop creó " + outputs.Length +
                        " copias en vez de una.");

                    Report.Add(
                        "PASS normal/doble drop: retorno=" +
                        firstDropWatch.ElapsedMilliseconds +
                        " ms, ticks UI=" + uiTimerTicks +
                        ", salida=" + Path.GetFileName(outputs[0]));
                }

                form.Close();
                Application.DoEvents();
            }
        }

        private static void RunSourceTabClosedScenario(
            string scenarioDirectory,
            string baseFixture,
            string insertFixture)
        {
            Directory.CreateDirectory(scenarioDirectory);
            string basePath = Path.Combine(scenarioDirectory, "close-base.pdf");
            string insertedPath = Path.Combine(scenarioDirectory, "close-insert.pdf");
            File.Copy(baseFixture, basePath, true);
            File.Copy(insertFixture, insertedPath, true);

            using (var form = CreateHiddenViewer())
            {
                form.OpenPdfTabs(new[] { basePath });
                object sourceWorkspace = WaitForLoadedWorkspace(form, 10000);
                var thumbnails = (PdfThumbnailList)GetWorkspaceField(
                    sourceWorkspace,
                    "Thumbnails");

                InvokeDragMethod(
                    thumbnails,
                    "OnDragDrop",
                    CreateDragArgs(thumbnails, insertedPath, 1));
                Assert(
                    (bool)GetField(form, "pageInsertInProgress"),
                    "La inserción no estaba activa antes de cerrar la pestaña origen.");

                var tabs = (ClosablePdfTabControl)GetField(
                    form,
                    "documentTabs");
                Assert(
                    tabs.CloseActiveTab(),
                    "El control de pestañas rechazó cerrar la pestaña origen.");
                Assert(
                    GetWorkspaces(form).Count == 0,
                    "La pestaña origen no se cerró durante la operación.");

                PumpUntil(
                    delegate
                    {
                        return !(bool)GetField(form, "pageInsertInProgress") &&
                            GetWorkspaces(form).Count == 1;
                    },
                    30000,
                    "Cerrar la pestaña origen impidió finalizar/abrir el resultado.");

                VerifyResultWorkspace(
                    form,
                    basePath,
                    1,
                    82,
                    expectedWorkspaceCount: 1);
                Report.Add(
                    "PASS pestaña origen cerrada: la copia terminó y se abrió correctamente.");

                form.Close();
                Application.DoEvents();
            }
        }

        private static void RunFormCloseGuardScenario(
            string scenarioDirectory,
            string baseFixture,
            string insertFixture)
        {
            Directory.CreateDirectory(scenarioDirectory);
            string basePath = Path.Combine(scenarioDirectory, "guard-base.pdf");
            string insertedPath = Path.Combine(scenarioDirectory, "guard-insert.pdf");
            File.Copy(baseFixture, basePath, true);
            File.Copy(insertFixture, insertedPath, true);

            using (var form = CreateHiddenViewer())
            {
                form.OpenPdfTabs(new[] { basePath });
                object sourceWorkspace = WaitForLoadedWorkspace(form, 10000);
                var thumbnails = (PdfThumbnailList)GetWorkspaceField(
                    sourceWorkspace,
                    "Thumbnails");

                InvokeDragMethod(
                    thumbnails,
                    "OnDragDrop",
                    CreateDragArgs(thumbnails, insertedPath, 1));
                Assert(
                    (bool)GetField(form, "pageInsertInProgress"),
                    "La inserción no estaba activa al probar el cierre del formulario.");

                using (var messageCloser = new System.Threading.Timer(
                    delegate { CloseOwnedMessageBoxes(); },
                    null,
                    25,
                    25))
                {
                    form.Close();
                }

                Assert(
                    !form.IsDisposed && form.Visible,
                    "El formulario se cerró mientras seguía creando la copia.");

                PumpUntil(
                    delegate
                    {
                        return !(bool)GetField(form, "pageInsertInProgress") &&
                            GetWorkspaces(form).Count == 2;
                    },
                    30000,
                    "La inserción no terminó después de cancelar el cierre.");
                VerifyResultWorkspace(
                    form,
                    basePath,
                    1,
                    82,
                    expectedWorkspaceCount: 2);
                Report.Add(
                    "PASS cierre del formulario: se bloqueó durante el trabajo y terminó la copia.");

                form.Close();
                Application.DoEvents();
            }
        }

        private static void RunInvalidPdfScenario(
            string scenarioDirectory,
            string baseFixture)
        {
            Directory.CreateDirectory(scenarioDirectory);
            string basePath = Path.Combine(scenarioDirectory, "invalid-base.pdf");
            string insertedPath = Path.Combine(scenarioDirectory, "corrupt.pdf");
            File.Copy(baseFixture, basePath, true);
            File.WriteAllText(insertedPath, "Esto no es un PDF.");

            using (var form = CreateHiddenViewer())
            {
                form.OpenPdfTabs(new[] { basePath });
                object sourceWorkspace = WaitForLoadedWorkspace(form, 10000);
                var thumbnails = (PdfThumbnailList)GetWorkspaceField(
                    sourceWorkspace,
                    "Thumbnails");

                using (var messageCloser = new System.Threading.Timer(
                    delegate { CloseOwnedMessageBoxes(); },
                    null,
                    25,
                    25))
                {
                    InvokeDragMethod(
                        thumbnails,
                        "OnDragDrop",
                        CreateDragArgs(thumbnails, insertedPath, 1));
                    PumpUntil(
                        delegate
                        {
                            return !(bool)GetField(form, "pageInsertInProgress");
                        },
                        10000,
                        "La excepción del PDF corrupto dejó la operación bloqueada.");
                }

                Assert(
                    GetWorkspaces(form).Count == 1,
                    "El fallo al analizar el PDF corrupto abrió otra pestaña.");
                Assert(
                    Directory.GetFiles(
                        scenarioDirectory,
                        "invalid-base_editado*.pdf",
                        SearchOption.TopDirectoryOnly).Length == 0,
                    "El fallo dejó una copia *_editado.pdf.");
                Assert(
                    Directory.GetFiles(
                        scenarioDirectory,
                        "*.tmp",
                        SearchOption.TopDirectoryOnly).Length == 0,
                    "El fallo dejó archivos temporales.");
                Report.Add(
                    "PASS excepción: PDF corrupto recuperó la UI sin salida parcial.");

                form.Close();
                Application.DoEvents();
            }
        }

        private static PdfViewerForm CreateHiddenViewer()
        {
            var form = new PdfViewerForm(new string[0]);
            form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-20000, -20000);
            form.Opacity = 0d;
            form.Show();
            Application.DoEvents();
            return form;
        }

        private static object WaitForLoadedWorkspace(
            PdfViewerForm form,
            int timeoutMilliseconds)
        {
            object workspace = null;
            PumpUntil(
                delegate
                {
                    workspace = GetField(form, "activeWorkspace");
                    return workspace != null &&
                        (bool)GetWorkspaceField(workspace, "IsLoaded");
                },
                timeoutMilliseconds,
                "El PDF base no llegó a cargarse en la pestaña.");
            return workspace;
        }

        private static DragEventArgs CreateDragArgs(
            PdfThumbnailList thumbnails,
            string pdfPath,
            int insertionIndex)
        {
            int itemHeight = (int)GetField(thumbnails, "itemHeight");
            int clientY = 8 + insertionIndex * itemHeight;
            Point screenPoint = thumbnails.PointToScreen(
                new Point(
                    Math.Max(1, thumbnails.ClientSize.Width / 2),
                    clientY));
            var data = new DataObject();
            data.SetData(DataFormats.FileDrop, new[] { pdfPath });
            return new DragEventArgs(
                data,
                0,
                screenPoint.X,
                screenPoint.Y,
                DragDropEffects.Copy,
                DragDropEffects.None);
        }

        private static void InvokeDragMethod(
            PdfThumbnailList thumbnails,
            string methodName,
            DragEventArgs args)
        {
            MethodInfo method = typeof(PdfThumbnailList).GetMethod(
                methodName,
                InstancePrivate);
            if (method == null)
            {
                throw new MissingMethodException(
                    typeof(PdfThumbnailList).FullName,
                    methodName);
            }

            try
            {
                method.Invoke(thumbnails, new object[] { args });
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException ?? ex;
            }
        }

        private static void VerifyResultWorkspace(
            PdfViewerForm form,
            string basePath,
            int expectedInsertionIndex,
            int expectedPageCount,
            int expectedWorkspaceCount)
        {
            IList workspaces = GetWorkspaces(form);
            Assert(
                workspaces.Count == expectedWorkspaceCount,
                "Hay " + workspaces.Count + " pestañas en vez de " +
                expectedWorkspaceCount + ".");

            object resultWorkspace = GetField(form, "activeWorkspace");
            Assert(resultWorkspace != null, "No quedó una pestaña activa.");
            string resultPath = (string)GetWorkspaceField(resultWorkspace, "Path");
            Assert(
                !string.Equals(
                    resultPath,
                    basePath,
                    StringComparison.OrdinalIgnoreCase),
                "La pestaña activa siguió siendo el PDF original.");
            Assert(
                Path.GetFileName(resultPath).StartsWith(
                    Path.GetFileNameWithoutExtension(basePath) + "_editado",
                    StringComparison.OrdinalIgnoreCase),
                "La salida no usa el nombre *_editado.pdf: " + resultPath);
            Assert(File.Exists(resultPath), "La salida abierta no existe.");

            object document = GetWorkspaceField(resultWorkspace, "Document");
            int loadedPageCount = (int)document.GetType().GetProperty("PageCount").GetValue(
                document,
                null);
            Assert(
                loadedPageCount == expectedPageCount,
                "La pestaña resultado tiene " + loadedPageCount +
                " páginas en vez de " + expectedPageCount + ".");

            object viewer = GetWorkspaceField(resultWorkspace, "Viewer");
            object renderer = viewer.GetType().GetProperty("Renderer").GetValue(
                viewer,
                null);
            int visiblePage = (int)renderer.GetType().GetProperty("Page").GetValue(
                renderer,
                null);
            Assert(
                visiblePage == expectedInsertionIndex,
                "El visor saltó a la página índice " + visiblePage +
                " en vez de " + expectedInsertionIndex + ".");

            var resultThumbnails = (PdfThumbnailList)GetWorkspaceField(
                resultWorkspace,
                "Thumbnails");
            Assert(
                resultThumbnails.SelectedPage == expectedInsertionIndex,
                "La miniatura activa no corresponde a la primera página insertada.");

            var currentPageTextBox = (TextBox)GetField(
                form,
                "currentPageTextBox");
            Assert(
                currentPageTextBox.Text ==
                    (expectedInsertionIndex + 1).ToString(),
                "El selector de página muestra " + currentPageTextBox.Text +
                " en vez de " + (expectedInsertionIndex + 1) + ".");

            using (var reader = new PdfReader(resultPath))
            {
                Assert(
                    reader.NumberOfPages == expectedPageCount,
                    "El PDF guardado tiene " + reader.NumberOfPages +
                    " páginas en vez de " + expectedPageCount + ".");
            }
        }

        private static IList GetWorkspaces(PdfViewerForm form)
        {
            return (IList)GetField(form, "workspaces");
        }

        private static object GetWorkspaceField(object workspace, string name)
        {
            FieldInfo field = workspace.GetType().GetField(
                name,
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);
            if (field == null)
            {
                throw new MissingFieldException(workspace.GetType().FullName, name);
            }

            return field.GetValue(workspace);
        }

        private static object GetField(object instance, string name)
        {
            FieldInfo field = instance.GetType().GetField(name, InstancePrivate);
            if (field == null)
            {
                throw new MissingFieldException(instance.GetType().FullName, name);
            }

            return field.GetValue(instance);
        }

        private static void PumpUntil(
            Func<bool> condition,
            int timeoutMilliseconds,
            string failureMessage)
        {
            var stopwatch = Stopwatch.StartNew();
            while (!condition())
            {
                if (stopwatch.ElapsedMilliseconds >= timeoutMilliseconds)
                {
                    throw new TimeoutException(failureMessage);
                }

                Application.DoEvents();
                Thread.Sleep(2);
            }

            Application.DoEvents();
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static void CloseOwnedMessageBoxes()
        {
            int processId = Process.GetCurrentProcess().Id;
            EnumWindows(
                delegate(IntPtr window, IntPtr state)
                {
                    int windowProcessId;
                    GetWindowThreadProcessId(window, out windowProcessId);
                    if (windowProcessId != processId)
                    {
                        return true;
                    }

                    var className = new StringBuilder(64);
                    GetClassName(window, className, className.Capacity);
                    if (string.Equals(
                            className.ToString(),
                            "#32770",
                            StringComparison.Ordinal))
                    {
                        PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero);
                    }

                    return true;
                },
                IntPtr.Zero);
        }

        private delegate bool EnumWindowsCallback(IntPtr window, IntPtr state);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(
            EnumWindowsCallback callback,
            IntPtr state);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(
            IntPtr window,
            StringBuilder className,
            int maximumCount);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(
            IntPtr window,
            out int processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(
            IntPtr window,
            uint message,
            IntPtr wParam,
            IntPtr lParam);
    }
}

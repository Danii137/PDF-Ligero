using System;
using System.Collections;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
using IOPath = System.IO.Path;

namespace FirmaAutomatica
{
    internal static class Phase1UiHarness
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length != 4)
            {
                Console.Error.WriteLine(
                    "Uso: Phase1UiHarness <base.pdf> <insertar.pdf> " +
                    "<recovery-root> <captura.png>");
                return 2;
            }

            var basePath = IOPath.GetFullPath(args[0]);
            var insertedPath = IOPath.GetFullPath(args[1]);
            var recoveryRoot = IOPath.GetFullPath(args[2]);
            var screenshotPath = IOPath.GetFullPath(args[3]);
            Environment.SetEnvironmentVariable(
                PdfEditSession.RecoveryRootOverrideEnvironmentVariable,
                recoveryRoot);
            Directory.CreateDirectory(recoveryRoot);

            var originalHash = ComputeSha256(basePath);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string sessionDirectory;
            using (var form = new PdfViewerForm(new[] { basePath }))
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-30000, -30000);
                form.ShowInTaskbar = false;
                form.Show();
                Pump(800);
                AssertBackgroundOperationWaitsForWorker(form);

                var workspaces = GetField<IList>(form, "workspaces");
                Require(workspaces.Count == 1, "No se abrió el PDF base.");
                var workspace = workspaces[0];
                var thumbnails =
                    (Control)GetWorkspaceField(workspace, "Thumbnails");
                var itemHeight =
                    GetPrivateField<int>(thumbnails, "itemHeight");
                var gapClientPoint = new Point(
                    Math.Max(5, thumbnails.ClientSize.Width / 2),
                    8 + itemHeight);
                var gapScreenPoint = thumbnails.PointToScreen(gapClientPoint);
                var data = new DataObject();
                data.SetData(
                    DataFormats.FileDrop,
                    new[] { insertedPath });
                var drag = new DragEventArgs(
                    data,
                    0,
                    gapScreenPoint.X,
                    gapScreenPoint.Y,
                    DragDropEffects.Copy,
                    DragDropEffects.Copy);

                InvokeProtected(thumbnails, "OnDragEnter", drag);
                InvokeProtected(thumbnails, "OnDragOver", drag);
                Require(
                    GetPrivateField<int>(
                        thumbnails,
                        "dragInsertionPageIndex") == 1,
                    "El indicador de inserción no quedó entre las páginas.");
                InvokeProtected(thumbnails, "OnDragDrop", drag);

                PumpUntil(
                    delegate
                    {
                        return !GetField<bool>(
                            form,
                            "pageInsertInProgress");
                    },
                    15000,
                    "La inserción no terminó.");
                Pump(400);

                var tabs = GetField<TabControl>(form, "documentTabs");
                Require(
                    tabs.TabPages.Count == 1,
                    "La edición debe permanecer en la misma pestaña.");

                var session =
                    (PdfEditSession)GetWorkspaceField(
                        workspace,
                        "EditSession");
                sessionDirectory = session.SessionDirectory;
                var contentPath =
                    (string)GetWorkspaceField(
                        workspace,
                        "ContentPath");
                Require(
                    File.Exists(contentPath) &&
                    !string.Equals(
                        contentPath,
                        basePath,
                        StringComparison.OrdinalIgnoreCase),
                    "No se creó la revisión recuperable.");
                Require(
                    session.HasUnsavedChanges &&
                    session.CanUndo &&
                    !session.CanRedo,
                    "El estado de Undo tras insertar no es correcto.");
                Require(
                    tabs.TabPages[0].Text.Contains("•"),
                    "La pestaña no indica cambios sin guardar.");
                Require(
                    string.Equals(
                        originalHash,
                        ComputeSha256(basePath),
                        StringComparison.Ordinal),
                    "El PDF original fue modificado.");
                AssertPdfOrder(contentPath, 3, true);

                Directory.CreateDirectory(
                    IOPath.GetDirectoryName(screenshotPath));
                using (var bitmap = new Bitmap(
                    form.ClientSize.Width,
                    form.ClientSize.Height))
                {
                    form.DrawToBitmap(
                        bitmap,
                        new Rectangle(Point.Empty, form.ClientSize));
                    bitmap.Save(screenshotPath);
                }

                InvokePrivate(form, "UndoActiveDocument");
                Pump(300);
                Require(
                    !session.HasUnsavedChanges &&
                    !session.CanUndo &&
                    session.CanRedo,
                    "Undo no volvió al punto guardado.");
                AssertPdfOrder(session.CurrentPath, 2, false);
                Require(
                    !tabs.TabPages[0].Text.Contains("•"),
                    "Undo hasta el original debe limpiar el indicador.");

                InvokePrivate(form, "RedoActiveDocument");
                Pump(300);
                Require(
                    session.HasUnsavedChanges &&
                    session.CanUndo &&
                    !session.CanRedo,
                    "Redo no restauró la edición.");
                AssertPdfOrder(session.CurrentPath, 3, true);

                for (var cycle = 0; cycle < 5; cycle++)
                {
                    InvokePrivate(form, "UndoActiveDocument");
                    Pump(80);
                    InvokePrivate(form, "RedoActiveDocument");
                    Pump(80);
                }

                Require(
                    string.Equals(
                        originalHash,
                        ComputeSha256(basePath),
                        StringComparison.Ordinal),
                    "Undo/Redo modificó el original.");

                var savedTarget = IOPath.Combine(
                    IOPath.GetDirectoryName(screenshotPath),
                    "saved-copy.pdf");
                var savedFullHash =
                    PdfAtomicFileService.SaveCopyWithContentHash(
                        session.CurrentPath,
                        savedTarget);
                session.MarkCurrentRevisionSaved(savedTarget);
                var savedInfo = new FileInfo(savedTarget);
                SetWorkspaceField(
                    workspace,
                    "LastSavedPath",
                    savedTarget);
                SetWorkspaceField(
                    workspace,
                    "LastSavedLength",
                    savedInfo.Length);
                SetWorkspaceField(
                    workspace,
                    "LastSavedWriteUtcTicks",
                    savedInfo.LastWriteTimeUtc.Ticks);
                SetWorkspaceField(
                    workspace,
                    "LastSavedFingerprint",
                    PdfAtomicFileService.ComputeContentFingerprint(
                        savedTarget));
                SetWorkspaceField(
                    workspace,
                    "LastSavedFullHash",
                    savedFullHash);
                Require(
                    !session.HasUnsavedChanges &&
                    session.CanUndo,
                    "El punto guardado no conservó el historial.");
                Require(
                    PdfEditSession.FindRecoverableSessions().Count == 1,
                    "La copia de seguridad guardada debe sobrevivir hasta " +
                    "un cierre comprobado.");

                Require(
                    (bool)InvokePrivate(
                        form,
                        "ConfirmCloseWorkspace",
                        workspace),
                    "El cierre limpio no quedó confirmado.");
                Require(
                    GetWorkspaceField(
                        workspace,
                        "LastSavedVerificationLease") != null,
                    "La comprobación final no conservó el lease.");
                var writeWasBlocked = false;
                try
                {
                    using (new FileStream(
                        savedTarget,
                        FileMode.Open,
                        FileAccess.Write,
                        FileShare.ReadWrite))
                    {
                    }
                }
                catch (IOException)
                {
                    writeWasBlocked = true;
                }

                Require(
                    writeWasBlocked,
                    "El destino pudo cambiar antes de borrar Recovery.");

                form.Close();
                Pump(150);
                using (new FileStream(
                    savedTarget,
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.None))
                {
                }
            }

            Require(
                !Directory.Exists(sessionDirectory),
                "El cierre limpio no eliminó la sesión temporal.");
            Require(
                string.Equals(
                    originalHash,
                    ComputeSha256(basePath),
                    StringComparison.Ordinal),
                "El cierre cambió el original.");

            Console.WriteLine("PASS");
            Console.WriteLine("screenshot=" + screenshotPath);
            return 0;
        }

        private static void AssertPdfOrder(
            string path,
            int expectedPages,
            bool expectInsertedMiddle)
        {
            using (var reader = new PdfReader(path))
            {
                Require(
                    reader.NumberOfPages == expectedPages,
                    "Número de páginas inesperado.");
                var first = PdfTextExtractor.GetTextFromPage(reader, 1);
                var last = PdfTextExtractor.GetTextFromPage(
                    reader,
                    reader.NumberOfPages);
                Require(
                    first.Contains("DOCUMENTO A") &&
                    last.Contains("DOCUMENTO A"),
                    "Las páginas exteriores no son del documento A.");

                if (expectInsertedMiddle)
                {
                    var middle =
                        PdfTextExtractor.GetTextFromPage(reader, 2);
                    Require(
                        middle.Contains("DOCUMENTO B"),
                        "La página insertada no está en el centro.");
                }
            }
        }

        private static string ComputeSha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                return BitConverter.ToString(sha.ComputeHash(stream));
            }
        }

        private static T GetField<T>(object target, string name)
        {
            var field = target.GetType().GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            return (T)field.GetValue(target);
        }

        private static T GetPrivateField<T>(
            object target,
            string name)
        {
            var field = target.GetType().GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            return (T)field.GetValue(target);
        }

        private static object GetWorkspaceField(
            object workspace,
            string name)
        {
            var field = workspace.GetType().GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public);
            return field.GetValue(workspace);
        }

        private static void SetWorkspaceField(
            object workspace,
            string name,
            object value)
        {
            var field = workspace.GetType().GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public);
            field.SetValue(workspace, value);
        }

        private static object InvokePrivate(
            object target,
            string name,
            params object[] arguments)
        {
            var method = target.GetType().GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            return method.Invoke(target, arguments);
        }

        private static void AssertBackgroundOperationWaitsForWorker(
            IWin32Window owner)
        {
            using (var releaseWorker = new ManualResetEvent(false))
            using (var progress = new PdfBackgroundOperationForm(
                "Prueba",
                "Comprobando sincronización…",
                delegate
                {
                    releaseWorker.WaitOne();
                    Thread.Sleep(100);
                }))
            using (var closeTimer = new System.Windows.Forms.Timer())
            using (var releaseTimer = new System.Windows.Forms.Timer())
            {
                closeTimer.Interval = 40;
                closeTimer.Tick += delegate
                {
                    closeTimer.Stop();
                    progress.Close();
                };
                releaseTimer.Interval = 180;
                releaseTimer.Tick += delegate
                {
                    releaseTimer.Stop();
                    releaseWorker.Set();
                };

                var stopwatch = Stopwatch.StartNew();
                closeTimer.Start();
                releaseTimer.Start();
                progress.Run(owner);
                stopwatch.Stop();
                Require(
                    stopwatch.ElapsedMilliseconds >= 240,
                    "El diálogo volvió antes de terminar el worker.");
            }
        }

        private static object InvokeProtected(
            object target,
            string name,
            params object[] arguments)
        {
            var method = target.GetType().GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            return method.Invoke(target, arguments);
        }

        private static void PumpUntil(
            Func<bool> predicate,
            int timeoutMilliseconds,
            string error)
        {
            var deadline = Environment.TickCount + timeoutMilliseconds;
            while (!predicate() &&
                unchecked(deadline - Environment.TickCount) > 0)
            {
                Pump(20);
            }

            Require(predicate(), error);
        }

        private static void Pump(int milliseconds)
        {
            var deadline = Environment.TickCount + milliseconds;
            while (unchecked(deadline - Environment.TickCount) > 0)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}

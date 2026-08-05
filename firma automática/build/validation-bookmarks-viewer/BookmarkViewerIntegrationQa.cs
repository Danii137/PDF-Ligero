using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using PdfiumViewer;

namespace FirmaAutomatica
{
    internal static class BookmarkViewerIntegrationQa
    {
        private const string OriginalPlanTitle =
            "Plan general / Fit";
        private const string EditedPlanTitle =
            "Plan E2E revisado";

        private static string captureDirectory;
        private static bool editorAutomated;
        private static Exception automationError;

        [STAThread]
        private static int Main(string[] args)
        {
            if (args == null || args.Length < 2)
            {
                Console.Error.WriteLine(
                    "Uso: BookmarkViewerIntegrationQa <run> <fixture.pdf>");
                return 2;
            }

            var validationRoot = Path.GetFullPath(args[0]);
            var fixturePath = Path.GetFullPath(args[1]);
            var sessionDirectory = Path.Combine(
                validationRoot,
                "session-" +
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            captureDirectory = Path.Combine(
                sessionDirectory,
                "captures");
            var recoveryDirectory = Path.Combine(
                sessionDirectory,
                "recovery");
            Directory.CreateDirectory(captureDirectory);
            Directory.CreateDirectory(recoveryDirectory);
            Environment.SetEnvironmentVariable(
                PdfEditSession.RecoveryRootOverrideEnvironmentVariable,
                recoveryDirectory);

            var sourcePath = Path.Combine(
                sessionDirectory,
                "marcadores E2E - Málaga.pdf");
            File.Copy(fixturePath, sourcePath, false);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            PdfViewerForm viewer = null;
            var report = new StringBuilder();
            try
            {
                viewer = new PdfViewerForm(new[] { sourcePath });
                viewer.Width = 1260;
                viewer.Height = 820;
                viewer.StartPosition =
                    FormStartPosition.CenterScreen;
                viewer.Show();
                viewer.Activate();

                PumpUntil(
                    delegate
                    {
                        var candidate = GetField(
                            viewer,
                            "activeWorkspace");
                        return candidate != null &&
                            (bool)GetField(candidate, "IsLoaded");
                    },
                    20000,
                    "El visor no terminó de abrir el fixture.");

                var workspace = GetField(
                    viewer,
                    "activeWorkspace");
                Invoke(
                    viewer,
                    "ShowNavigationMode",
                    workspace,
                    true);
                Application.DoEvents();

                var bookmarkTree =
                    (TreeView)GetField(
                        workspace,
                        "BookmarksTree");
                AssertBookmarkTree(
                    bookmarkTree,
                    OriginalPlanTitle);
                AssertAdvancedActionsDoNotNavigate(
                    workspace,
                    bookmarkTree);
                ValidateViewportModes(
                    workspace,
                    bookmarkTree);
                CaptureForm(
                    viewer,
                    Path.Combine(
                        captureDirectory,
                        "01-panel-marcadores-origen.png"));

                using (var automationTimer =
                    new System.Windows.Forms.Timer())
                {
                    automationTimer.Interval = 80;
                    automationTimer.Tick +=
                        AutomationTimer_Tick;
                    automationTimer.Start();
                    Invoke(viewer, "EditActiveBookmarks");
                    automationTimer.Stop();
                }

                if (automationError != null)
                {
                    throw new InvalidOperationException(
                        "Falló la automatización del editor.",
                        automationError);
                }
                Assert(
                    editorAutomated,
                    "El editor real no llegó a mostrarse.");

                workspace = GetField(
                    viewer,
                    "activeWorkspace");
                PumpUntil(
                    delegate
                    {
                        return !(bool)GetField(
                            viewer,
                            "bookmarkEditInProgress");
                    },
                    20000,
                    "No terminó la aplicación de marcadores.");
                var revisionPath =
                    (string)GetField(workspace, "ContentPath");
                Assert(
                    !string.Equals(
                        revisionPath,
                        sourcePath,
                        StringComparison.OrdinalIgnoreCase),
                    "La revisión no se activó en el visor.");
                Assert(
                    File.Exists(revisionPath),
                    "No existe la revisión activa.");

                bookmarkTree =
                    (TreeView)GetField(
                        workspace,
                        "BookmarksTree");
                AssertBookmarkTree(
                    bookmarkTree,
                    EditedPlanTitle);
                var editedPlan = FindTreeNode(
                    bookmarkTree.Nodes,
                    EditedPlanTitle);
                var editedDestination =
                    editedPlan.Tag as PdfBookmarkDestination;
                Assert(
                    editedDestination != null &&
                    editedDestination.PageNumber == 3 &&
                    editedDestination.TopPositionPercent.HasValue &&
                    NearlyEqual(
                        editedDestination.TopPositionPercent.Value,
                        42D),
                    "El árbol no refleja el destino editado.");

                var session =
                    (PdfEditSession)GetField(
                        workspace,
                        "EditSession");
                Assert(
                    session.HasUnsavedChanges &&
                    session.CanUndo,
                    "La edición no entró en el historial.");

                SendViewerShortcut(
                    viewer,
                    Keys.Control | Keys.Z);
                workspace = GetField(
                    viewer,
                    "activeWorkspace");
                PumpUntil(
                    delegate
                    {
                        return string.Equals(
                            (string)GetField(
                                workspace,
                                "ContentPath"),
                            sourcePath,
                            StringComparison.OrdinalIgnoreCase);
                    },
                    15000,
                    "Ctrl+Z no volvió al original.");
                bookmarkTree =
                    (TreeView)GetField(
                        workspace,
                        "BookmarksTree");
                AssertBookmarkTree(
                    bookmarkTree,
                    OriginalPlanTitle);

                SendViewerShortcut(
                    viewer,
                    Keys.Control | Keys.Y);
                workspace = GetField(
                    viewer,
                    "activeWorkspace");
                PumpUntil(
                    delegate
                    {
                        return string.Equals(
                            (string)GetField(
                                workspace,
                                "ContentPath"),
                            revisionPath,
                            StringComparison.OrdinalIgnoreCase);
                    },
                    15000,
                    "Ctrl+Y no restauró la revisión.");
                bookmarkTree =
                    (TreeView)GetField(
                        workspace,
                        "BookmarksTree");
                AssertBookmarkTree(
                    bookmarkTree,
                    EditedPlanTitle);

                NavigateThroughTree(
                    workspace,
                    bookmarkTree,
                    EditedPlanTitle,
                    2);
                AssertAdvancedActionsDoNotNavigate(
                    workspace,
                    bookmarkTree);
                CaptureForm(
                    viewer,
                    Path.Combine(
                        captureDirectory,
                        "04-revision-redo-y-navegacion.png"));

                var reloaded =
                    PdfBookmarkService.Load(revisionPath);
                Assert(
                    FindBookmark(
                        reloaded.Bookmarks,
                        EditedPlanTitle) != null,
                    "El PDF final no contiene el título editado.");
                Assert(
                    FindBookmark(
                        reloaded.Bookmarks,
                        "Alternar capa / SetOCGState") != null &&
                    FindBookmark(
                        reloaded.Bookmarks,
                        "Acción JavaScript QA") != null,
                    "El PDF final perdió acciones avanzadas.");

                report.AppendLine(
                    "PASS: integración real del visor y marcadores.");
                report.AppendLine(
                    "Abrir → panel → editor → mutar → aplicar: PASS");
                report.AppendLine(
                    "Árbol actualizado y destino página 3 / 42 %: PASS");
                report.AppendLine(
                    "Ctrl+Z / Ctrl+Y por el handler real: PASS");
                report.AppendLine(
                    "Navegación editable y acciones avanzadas seguras: PASS");
                report.AppendLine(
                    "Fit/FitH/FitV/XYZ-null y ejes de desplazamiento: PASS");
                report.AppendLine("Original: " + sourcePath);
                report.AppendLine("Revisión: " + revisionPath);
                report.AppendLine(
                    "Capturas: " + captureDirectory);
                File.WriteAllText(
                    Path.Combine(
                        sessionDirectory,
                        "qa-report.txt"),
                    report.ToString(),
                    Encoding.UTF8);
                Console.Write(report.ToString());
                Console.WriteLine(
                    "report=" +
                    Path.Combine(
                        sessionDirectory,
                        "qa-report.txt"));
                return 0;
            }
            catch (Exception ex)
            {
                report.AppendLine("FAIL: " + ex);
                File.WriteAllText(
                    Path.Combine(
                        sessionDirectory,
                        "qa-report.txt"),
                    report.ToString(),
                    Encoding.UTF8);
                Console.Error.Write(report.ToString());
                Console.Error.WriteLine(
                    "report=" +
                    Path.Combine(
                        sessionDirectory,
                        "qa-report.txt"));
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
            if (editorAutomated)
            {
                return;
            }

            var editor = Application.OpenForms
                .Cast<Form>()
                .OfType<PdfBookmarkEditorForm>()
                .FirstOrDefault();
            if (editor == null ||
                !editor.Visible ||
                !editor.IsHandleCreated)
            {
                return;
            }

            editorAutomated = true;
            try
            {
                var tree = editor.BookmarkTreeForTesting;
                AssertBookmarkTree(tree, OriginalPlanTitle);
                CaptureForm(
                    editor,
                    Path.Combine(
                        captureDirectory,
                        "02-editor-antes.png"));

                var plan = FindTreeNode(
                    tree.Nodes,
                    OriginalPlanTitle);
                Assert(plan != null, "No aparece el plan en el editor.");
                tree.SelectedNode = plan;
                tree.Focus();
                Application.DoEvents();

                var editArguments =
                    new NodeLabelEditEventArgs(
                        plan,
                        EditedPlanTitle);
                Invoke(
                    editor,
                    "BookmarkTree_AfterLabelEdit",
                    tree,
                    editArguments);
                Assert(
                    string.Equals(
                        plan.Text,
                        EditedPlanTitle,
                        StringComparison.Ordinal),
                    "El editor no aplicó el nuevo título.");

                editor.PageNumberInputForTesting.Value = 3;
                editor.ExactPositionCheckBoxForTesting.Checked =
                    true;
                editor.PositionInputForTesting.Value = 42;
                Application.DoEvents();
                Assert(
                    editor.HasChanges,
                    "El editor no marcó cambios.");
                CaptureForm(
                    editor,
                    Path.Combine(
                        captureDirectory,
                        "03-editor-mutado.png"));

                Assert(
                    editor.ApplyButtonForTesting.Enabled,
                    "Aplicar quedó deshabilitado.");
                editor.ApplyButtonForTesting.PerformClick();
            }
            catch (Exception ex)
            {
                automationError = ex;
                editor.DialogResult = DialogResult.Cancel;
                editor.Close();
            }
        }

        private static void AssertBookmarkTree(
            TreeView tree,
            string expectedPlanTitle)
        {
            Assert(tree != null, "No existe el árbol de marcadores.");
            Assert(
                tree.Nodes.Count == 3,
                "El árbol raíz no contiene tres marcadores. " +
                    "Actual=" +
                    tree.Nodes.Count +
                    "; títulos=" +
                    string.Join(
                        " | ",
                        tree.Nodes
                            .Cast<TreeNode>()
                            .Select(node => node.Text)
                            .ToArray()));
            Assert(
                CountTreeNodes(tree.Nodes) == 8,
                "No se conserva el árbol anidado de ocho marcadores.");
            Assert(
                FindTreeNode(
                    tree.Nodes,
                    expectedPlanTitle) != null,
                "No aparece " + expectedPlanTitle + ".");
            var layer = FindTreeNode(
                tree.Nodes,
                "Alternar capa / SetOCGState");
            var script = FindTreeNode(
                tree.Nodes,
                "Acción JavaScript QA");
            Assert(
                layer != null &&
                script != null,
                "Las acciones avanzadas no aparecen en el árbol.");
        }

        private static void NavigateThroughTree(
            object workspace,
            TreeView tree,
            string title,
            int expectedPageIndex)
        {
            var node = FindTreeNode(tree.Nodes, title);
            Assert(node != null, "No existe el nodo para navegar.");
            var handler =
                (TreeNodeMouseClickEventHandler)GetField(
                    workspace,
                    "BookmarkSelectionHandler");
            Assert(handler != null, "No existe el handler de navegación.");
            handler(
                tree,
                new TreeNodeMouseClickEventArgs(
                    node,
                    MouseButtons.Left,
                    1,
                    node.Bounds.Left + 2,
                    node.Bounds.Top + 2));
            var pdfViewer =
                (PdfiumViewer.PdfViewer)GetField(
                    workspace,
                    "Viewer");
            PumpUntil(
                delegate
                {
                    return pdfViewer.Renderer.Page ==
                        expectedPageIndex;
                },
                5000,
                "El marcador no llevó a la página esperada.");
        }

        private static void AssertAdvancedActionsDoNotNavigate(
            object workspace,
            TreeView tree)
        {
            var layer = FindTreeNode(
                tree.Nodes,
                "Alternar capa / SetOCGState");
            Assert(layer != null, "No aparece SetOCGState.");
            var pdfViewer =
                (PdfiumViewer.PdfViewer)GetField(
                    workspace,
                    "Viewer");
            pdfViewer.Renderer.Page = 2;
            var handler =
                (TreeNodeMouseClickEventHandler)GetField(
                    workspace,
                    "BookmarkSelectionHandler");
            handler(
                tree,
                new TreeNodeMouseClickEventArgs(
                    layer,
                    MouseButtons.Left,
                    1,
                    1,
                    1));
            Application.DoEvents();
            Assert(
                pdfViewer.Renderer.Page == 2,
                "Una acción avanzada saltó erróneamente de página.");
        }

        private static void ValidateViewportModes(
            object workspace,
            TreeView tree)
        {
            var pdfViewer =
                (PdfiumViewer.PdfViewer)GetField(
                    workspace,
                    "Viewer");
            var renderer = pdfViewer.Renderer;
            var bookmarkDocument =
                (PdfBookmarkDocument)GetField(
                    workspace,
                    "BookmarkDocument");
            Assert(
                bookmarkDocument != null,
                "No existe el modelo de destinos para probar la vista.");

            var fit = GetTreeDestination(
                tree,
                OriginalPlanTitle);
            Assert(
                fit.Mode == PdfBookmarkDestinationMode.Fit,
                "El fixture no expone /Fit.");
            renderer.Page = fit.PageNumber - 1;
            InvokeStatic(
                typeof(PdfViewerForm),
                "ApplyBookmarkViewport",
                workspace,
                fit,
                fit.PageNumber - 1);
            Application.DoEvents();
            Assert(
                renderer.ZoomMode == PdfViewerZoomMode.FitBest,
                "/Fit no activa Ajustar página.");

            var fitHorizontal = GetTreeDestination(
                tree,
                "Destino nominal / FitH");
            var horizontalPoint =
                PdfBookmarkService.GetPdfPoint(
                    bookmarkDocument,
                    fitHorizontal);
            Assert(
                fitHorizontal.Mode ==
                    PdfBookmarkDestinationMode.FitHorizontal &&
                !horizontalPoint.HasX &&
                horizontalPoint.HasY,
                "/FitH no conserva su semántica de eje Y.");
            renderer.Page =
                fitHorizontal.PageNumber - 1;
            renderer.ZoomMode = PdfViewerZoomMode.FitWidth;
            renderer.PerformLayout();
            Application.DoEvents();
            var horizontalStart =
                renderer.DisplayRectangle.Location;
            renderer.SetDisplayRectLocation(
                new Point(
                    horizontalStart.X,
                    horizontalStart.Y - 320),
                false);
            Application.DoEvents();
            var horizontalBefore =
                renderer.DisplayRectangle.Location;
            InvokeStatic(
                typeof(PdfViewerForm),
                "ApplyBookmarkViewport",
                workspace,
                fitHorizontal,
                fitHorizontal.PageNumber - 1);
            Application.DoEvents();
            var horizontalAfter =
                renderer.DisplayRectangle.Location;
            Assert(
                renderer.ZoomMode == PdfViewerZoomMode.FitWidth &&
                horizontalAfter.X == horizontalBefore.X &&
                horizontalAfter.Y != horizontalBefore.Y,
                "/FitH no conservó X o no posicionó Y. Antes=" +
                    horizontalBefore +
                    "; después=" +
                    horizontalAfter +
                    ".");

            var fixtureFitVertical = GetTreeDestination(
                tree,
                "Destino Name homónimo / FitV");
            Assert(
                fixtureFitVertical.Mode ==
                    PdfBookmarkDestinationMode.FitVertical,
                "El fixture no expone /FitV.");
            // Use a point well inside the panoramic sheet. The fixture's
            // homonymous named destination is intentionally only 36 pt from
            // the left edge and can already coincide with the clamped
            // leftmost viewport.
            var fitVertical = new PdfBookmarkDestination(
                fixtureFitVertical.PageNumber,
                PdfBookmarkDestinationMode.FitVertical,
                null,
                80D,
                null,
                null,
                null);
            var verticalPoint =
                PdfBookmarkService.GetPdfPoint(
                    bookmarkDocument,
                    fitVertical);
            Assert(
                verticalPoint.HasX &&
                !verticalPoint.HasY,
                "/FitV no conserva su semántica de eje X.");
            var hostForm = pdfViewer.FindForm();
            var originalHostSize = hostForm == null
                ? Size.Empty
                : hostForm.Size;
            var originalMinimumSize = hostForm == null
                ? Size.Empty
                : hostForm.MinimumSize;
            if (hostForm != null)
            {
                hostForm.MinimumSize = new Size(620, 500);
                hostForm.Width = 720;
                Application.DoEvents();
            }
            renderer.Page =
                fitVertical.PageNumber - 1;
            renderer.ZoomMode = PdfViewerZoomMode.FitHeight;
            renderer.PerformLayout();
            Application.DoEvents();
            var horizontalScrollAvailable =
                (bool)GetProperty(renderer, "HScroll");
            var verticalStart =
                renderer.DisplayRectangle.Location;
            renderer.SetDisplayRectLocation(
                new Point(
                    verticalStart.X - 480,
                    verticalStart.Y),
                false);
            Application.DoEvents();
            var verticalBefore =
                renderer.DisplayRectangle.Location;
            InvokeStatic(
                typeof(PdfViewerForm),
                "ApplyBookmarkViewport",
                workspace,
                fitVertical,
                fitVertical.PageNumber - 1);
            Application.DoEvents();
            var verticalAfter =
                renderer.DisplayRectangle.Location;
            if (hostForm != null)
            {
                hostForm.MinimumSize = originalMinimumSize;
                hostForm.Size = originalHostSize;
                Application.DoEvents();
            }
            Assert(
                renderer.ZoomMode == PdfViewerZoomMode.FitHeight &&
                verticalAfter.Y == verticalBefore.Y &&
                (!horizontalScrollAvailable ||
                 verticalAfter.X != verticalBefore.X),
                "/FitV no conservó Y o no posicionó X. Antes=" +
                    verticalBefore +
                    "; después=" +
                    verticalAfter +
                    "; HScroll=" +
                    horizontalScrollAvailable +
                    ".");

            var xyzNull = new PdfBookmarkDestination(
                2,
                PdfBookmarkDestinationMode.Xyz,
                null,
                null,
                null,
                null,
                null);
            var xyzNullPoint =
                PdfBookmarkService.GetPdfPoint(
                    bookmarkDocument,
                    xyzNull);
            Assert(
                !xyzNullPoint.HasX &&
                !xyzNullPoint.HasY,
                "/XYZ null se convirtió en coordenadas de CropBox.");
            renderer.Page = 1;
            Application.DoEvents();
            var xyzBefore =
                renderer.DisplayRectangle.Location;
            var xyzZoomMode = renderer.ZoomMode;
            InvokeStatic(
                typeof(PdfViewerForm),
                "ApplyBookmarkViewport",
                workspace,
                xyzNull,
                1);
            Application.DoEvents();
            var xyzAfter =
                renderer.DisplayRectangle.Location;
            Assert(
                xyzAfter == xyzBefore &&
                renderer.ZoomMode == xyzZoomMode,
                "/XYZ null forzó desplazamiento o zoom.");
        }

        private static PdfBookmarkDestination GetTreeDestination(
            TreeView tree,
            string title)
        {
            var node = FindTreeNode(tree.Nodes, title);
            var destination = node == null
                ? null
                : node.Tag as PdfBookmarkDestination;
            Assert(
                destination != null,
                "No existe destino navegable para " + title + ".");
            return destination;
        }

        private static void SendViewerShortcut(
            PdfViewerForm viewer,
            Keys keys)
        {
            var arguments = new KeyEventArgs(keys);
            Invoke(
                viewer,
                "PdfViewerForm_KeyDown",
                viewer,
                arguments);
            Assert(
                arguments.Handled &&
                arguments.SuppressKeyPress,
                "El visor no consumió el atajo " +
                    keys.ToString() +
                    ".");
            Application.DoEvents();
        }

        private static PdfBookmarkNode FindBookmark(
            System.Collections.Generic.IList<PdfBookmarkNode> nodes,
            string title)
        {
            if (nodes == null)
            {
                return null;
            }
            foreach (var node in nodes)
            {
                if (string.Equals(
                    node.Title,
                    title,
                    StringComparison.Ordinal))
                {
                    return node;
                }
                var child = FindBookmark(
                    node.Children,
                    title);
                if (child != null)
                {
                    return child;
                }
            }
            return null;
        }

        private static TreeNode FindTreeNode(
            TreeNodeCollection nodes,
            string title)
        {
            foreach (TreeNode node in nodes)
            {
                if (string.Equals(
                    node.Text,
                    title,
                    StringComparison.Ordinal))
                {
                    return node;
                }
                var child = FindTreeNode(
                    node.Nodes,
                    title);
                if (child != null)
                {
                    return child;
                }
            }
            return null;
        }

        private static int CountTreeNodes(
            TreeNodeCollection nodes)
        {
            var count = 0;
            foreach (TreeNode node in nodes)
            {
                count++;
                count += CountTreeNodes(node.Nodes);
            }
            return count;
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
                if (stopwatch.ElapsedMilliseconds >
                    timeoutMilliseconds)
                {
                    throw new TimeoutException(failureMessage);
                }
            }
            Application.DoEvents();
        }

        private static void CaptureForm(
            Form form,
            string path)
        {
            if (form == null ||
                form.IsDisposed ||
                form.Width < 1 ||
                form.Height < 1)
            {
                return;
            }
            using (var bitmap =
                new Bitmap(form.Width, form.Height))
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

        private static object GetField(
            object instance,
            string name)
        {
            Assert(
                instance != null,
                "No existe el objeto para leer " + name + ".");
            var field = instance.GetType().GetField(
                name,
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);
            Assert(field != null, "No existe el campo " + name + ".");
            return field.GetValue(instance);
        }

        private static object GetProperty(
            object instance,
            string name)
        {
            Assert(
                instance != null,
                "No existe el objeto para leer " + name + ".");
            var property = instance.GetType().GetProperty(
                name,
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);
            Assert(
                property != null,
                "No existe la propiedad " + name + ".");
            return property.GetValue(instance, null);
        }

        private static object Invoke(
            object instance,
            string name,
            params object[] arguments)
        {
            Assert(instance != null, "No existe el objeto para " + name + ".");
            var method = instance.GetType().GetMethod(
                name,
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);
            Assert(method != null, "No existe el método " + name + ".");
            try
            {
                return method.Invoke(instance, arguments);
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException ?? ex;
            }
        }

        private static object InvokeStatic(
            Type type,
            string name,
            params object[] arguments)
        {
            var method = type.GetMethod(
                name,
                BindingFlags.Static |
                BindingFlags.Public |
                BindingFlags.NonPublic);
            Assert(method != null, "No existe el método " + name + ".");
            try
            {
                return method.Invoke(null, arguments);
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException ?? ex;
            }
        }

        private static bool NearlyEqual(
            double first,
            double second)
        {
            return Math.Abs(first - second) < 0.05D;
        }

        private static void Assert(
            bool condition,
            string message)
        {
            if (!condition)
            {
                throw new InvalidDataException(message);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using iTextSharp.text;
using iTextSharp.text.pdf;

namespace FirmaAutomatica
{
    internal static class BookmarkUiQa
    {
        private static readonly List<string> Report =
            new List<string>();

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.Error.WriteLine(
                    "Uso: BookmarkUiQa <carpeta-salida>");
                return 2;
            }

            var outputDirectory = Path.GetFullPath(args[0]);
            Directory.CreateDirectory(outputDirectory);
            var fixturePath = Path.Combine(
                outputDirectory,
                "marcadores-ui-fixture.pdf");

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                CreateFixture(fixturePath);

                var original = PdfBookmarkService.Load(fixturePath);
                var originalSnapshot = Snapshot(original.Bookmarks);
                Require(original.PageCount == 4, "Fixture de 4 páginas.");
                Require(
                    Count(original.Bookmarks) == 4,
                    "Fixture de 4 marcadores.");

                AssertCancellationIsTransactional(
                    original,
                    originalSnapshot);
                AssertApplyWithoutChanges(original);
                AssertPageEditPreservesRawDestination(original);
                AssertEditingFlow(
                    original,
                    originalSnapshot,
                    outputDirectory);

                Report.Add("RESULTADO: PASS");
                File.WriteAllLines(
                    Path.Combine(outputDirectory, "qa-report.txt"),
                    Report.ToArray(),
                    Encoding.UTF8);
                Console.WriteLine(string.Join(
                    Environment.NewLine,
                    Report.ToArray()));
                return 0;
            }
            catch (Exception ex)
            {
                Report.Add("RESULTADO: FAIL");
                Report.Add(ex.ToString());
                File.WriteAllLines(
                    Path.Combine(outputDirectory, "qa-report.txt"),
                    Report.ToArray(),
                    Encoding.UTF8);
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        private static void AssertCancellationIsTransactional(
            PdfBookmarkDocument original,
            string originalSnapshot)
        {
            using (var form = new PdfBookmarkEditorForm(original))
            {
                form.Show();
                Pump(120);
                SelectByTitle(form.BookmarkTreeForTesting, "Capítulo B");
                Invoke(form, "MoveSelectedBookmark", -1);
                Require(form.HasChanges, "Cancelar: mutación detectada.");
                form.Close();
            }

            Require(
                string.Equals(
                    originalSnapshot,
                    Snapshot(original.Bookmarks),
                    StringComparison.Ordinal),
                "Cancelar conserva intacto el modelo original.");
            Report.Add("PASS · edición transaccional al cancelar");
        }

        private static void AssertApplyWithoutChanges(
            PdfBookmarkDocument original)
        {
            using (var form = new PdfBookmarkEditorForm(original))
            {
                form.Show();
                Pump(80);
                form.ApplyButtonForTesting.PerformClick();
                Pump(40);
                Require(
                    form.DialogResult == DialogResult.OK,
                    "Aplicar sin cambios devuelve OK.");
                Require(
                    !form.HasChanges,
                    "Aplicar sin cambios no crea una revisión.");
                Require(
                    form.EditedDocument != null,
                    "Aplicar expone el documento clonado.");
            }
            Report.Add("PASS · HasChanges=false sin edición");
        }

        private static void AssertPageEditPreservesRawDestination(
            PdfBookmarkDocument original)
        {
            var document = PdfBookmarkService.CloneDocument(original);
            var sourceNode = FindModelByTitle(
                document.Bookmarks,
                "Capítulo B");
            Require(
                sourceNode != null,
                "Existe modelo para comprobar coordenadas PDF raw.");
            var rawDestination = PdfBookmarkDestination.FromPdf(
                3,
                PdfBookmarkDestinationMode.FitRectangle,
                -25D,
                -10D,
                125D,
                140D,
                null);
            PdfBookmarkService.SetDestination(
                document,
                sourceNode.Id,
                rawDestination);

            using (var form = new PdfBookmarkEditorForm(document))
            {
                form.Show();
                Pump(80);
                SelectByTitle(
                    form.BookmarkTreeForTesting,
                    "Capítulo B");
                form.PageNumberInputForTesting.Value = 4;
                Pump(30);
                form.ApplyButtonForTesting.PerformClick();
                Pump(30);

                var editedNode = FindModelByTitle(
                    form.EditedDocument.Bookmarks,
                    "Capítulo B");
                var edited = editedNode == null
                    ? null
                    : editedNode.Destination;
                Require(
                    edited != null &&
                    edited.PageNumber == 4,
                    "Cambiar página actualiza únicamente la página.");
                Require(
                    edited.Mode ==
                        PdfBookmarkDestinationMode.FitRectangle &&
                    edited.TopPositionPercent == -25D &&
                    edited.LeftPositionPercent == -10D &&
                    edited.BottomPositionPercent == 125D &&
                    edited.RightPositionPercent == 140D,
                    "Cambiar página conserva coordenadas FitR fuera del CropBox.");
            }

            Report.Add(
                "PASS · cambio de página conserva destino PDF raw");
        }

        private static void AssertEditingFlow(
            PdfBookmarkDocument original,
            string originalSnapshot,
            string outputDirectory)
        {
            var capturedDestination =
                new PdfBookmarkDestination(4, 37.5);
            using (var form = new PdfBookmarkEditorForm(
                original,
                capturedDestination))
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(30, 30);
                form.Show();
                Pump(180);

                Require(
                    form.ClientSize.Width >= 760 &&
                    form.ClientSize.Height >= 540,
                    "Tamaño inicial utilizable.");
                Require(
                    !string.IsNullOrWhiteSpace(
                        form.BookmarkTreeForTesting.AccessibleName),
                    "Árbol con nombre accesible.");

                Capture(
                    form,
                    Path.Combine(
                        outputDirectory,
                        "01-editor-marcadores.png"));

                SelectByTitle(
                    form.BookmarkTreeForTesting,
                    "Capítulo A");
                Invoke(form, "UseVisibleViewButton_Click", null, EventArgs.Empty);
                Require(
                    form.PageNumberInputForTesting.Value == 4,
                    "Usar vista actual toma la página.");
                Require(
                    form.ExactPositionCheckBoxForTesting.Checked &&
                    form.PositionInputForTesting.Value == 37.5M,
                    "Usar vista actual toma la posición.");

                Invoke(form, "CreateBookmark");
                Pump(60);
                Invoke(form, "FinishPendingRename");
                Pump(40);
                var createdTreeNode =
                    form.BookmarkTreeForTesting.SelectedNode;
                Require(
                    createdTreeNode != null,
                    "Crear selecciona el nuevo marcador.");
                Invoke(
                    form,
                    "BookmarkTree_AfterLabelEdit",
                    form.BookmarkTreeForTesting,
                    new NodeLabelEditEventArgs(
                        createdTreeNode,
                        "Detalle 01"));
                Require(
                    createdTreeNode.Text == "Detalle 01",
                    "F2/doble clic comparte renombrado validado.");

                SelectByTitle(
                    form.BookmarkTreeForTesting,
                    "Capítulo B");
                Invoke(form, "IndentSelectedBookmark");
                var chapterB =
                    FindByTitle(
                        form.BookmarkTreeForTesting.Nodes,
                        "Capítulo B");
                Require(
                    chapterB != null && chapterB.Parent != null,
                    "Aumentar nivel crea jerarquía.");
                Invoke(form, "OutdentSelectedBookmark");
                chapterB = FindByTitle(
                    form.BookmarkTreeForTesting.Nodes,
                    "Capítulo B");
                Require(
                    chapterB != null && chapterB.Parent == null,
                    "Reducir nivel recupera la raíz.");

                var beforeIndex = chapterB.Index;
                Invoke(form, "MoveSelectedBookmark", -1);
                chapterB = FindByTitle(
                    form.BookmarkTreeForTesting.Nodes,
                    "Capítulo B");
                Require(
                    chapterB.Index == Math.Max(0, beforeIndex - 1),
                    "Subir cambia el orden entre hermanos.");
                Invoke(form, "MoveSelectedBookmark", 1);

                SelectByTitle(
                    form.BookmarkTreeForTesting,
                    "Web externa");
                Require(
                    !form.PageNumberInputForTesting.Enabled,
                    "Acción externa conserva destino y bloquea edición.");

                SelectByTitle(
                    form.BookmarkTreeForTesting,
                    "Detalle 01");
                Invoke(form, "DeleteSelectedBookmark");
                Require(
                    FindByTitle(
                        form.BookmarkTreeForTesting.Nodes,
                        "Detalle 01") == null,
                    "Supr elimina en la copia editable.");

                form.Size = form.MinimumSize;
                Pump(100);
                RequireImportantControlsInside(form);
                Capture(
                    form,
                    Path.Combine(
                        outputDirectory,
                        "02-editor-marcadores-compacto.png"));

                form.ApplyButtonForTesting.PerformClick();
                Pump(40);
                Require(
                    form.DialogResult == DialogResult.OK,
                    "Aplicar devuelve OK.");
                Require(
                    form.HasChanges,
                    "Aplicar expone HasChanges tras editar.");
                Require(
                    form.EditedDocument != null,
                    "Aplicar expone el documento editado.");
                Require(
                    !string.IsNullOrEmpty(form.SelectedNodeId),
                    "Se expone el ID seleccionado.");
                Require(
                    string.Equals(
                        originalSnapshot,
                        Snapshot(original.Bookmarks),
                        StringComparison.Ordinal),
                    "Aplicar tampoco muta el modelo de entrada.");
            }

            Report.Add(
                "PASS · crear/renombrar/eliminar/ordenar/niveles/destino");
            Report.Add("PASS · destino avanzado preservado y no editable");
            Report.Add("PASS · diseño normal y mínimo sin recortes críticos");
        }

        private static void RequireImportantControlsInside(
            PdfBookmarkEditorForm form)
        {
            var names = new[]
            {
                "bookmarkTree",
                "pageNumberInput",
                "positionInput",
                "useVisibleViewButton",
                "applyButton",
                "cancelButton"
            };
            foreach (var name in names)
            {
                var control = GetField<Control>(form, name);
                Require(
                    control.Width > 0 &&
                    control.Height > 0 &&
                    control.Visible,
                    name + " sigue visible en tamaño mínimo.");
                var screenBounds = control.RectangleToScreen(
                    control.ClientRectangle);
                var formBounds = form.RectangleToScreen(
                    form.ClientRectangle);
                Require(
                    formBounds.IntersectsWith(screenBounds),
                    name + " permanece dentro del formulario.");
            }
        }

        private static void CreateFixture(string path)
        {
            using (var stream = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            {
                using (var document = new Document(PageSize.A4))
                {
                    PdfWriter.GetInstance(document, stream);
                    document.Open();
                    for (var page = 1; page <= 4; page++)
                    {
                        if (page > 1)
                        {
                            document.NewPage();
                        }
                        document.Add(new Paragraph(
                            "Página " +
                            page.ToString(
                                CultureInfo.InvariantCulture)));
                    }
                }
            }

            var reader = new PdfReader(path);
            var temporaryPath = path + ".tmp.pdf";
            try
            {
                using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None))
                {
                    using (var stamper = new PdfStamper(reader, stream))
                    {
                        var chapterA =
                            CreateGoTo("Capítulo A", "1 Fit");
                        chapterA["Kids"] =
                            new List<Dictionary<string, object>>
                            {
                                CreateGoTo("Detalle A.1", "2 Fit")
                            };
                        var outlines =
                            new List<Dictionary<string, object>>
                            {
                                chapterA,
                                CreateGoTo("Capítulo B", "3 Fit"),
                                new Dictionary<string, object>
                                {
                                    { "Title", "Web externa" },
                                    { "Action", "URI" },
                                    {
                                        "URI",
                                        "https://example.invalid/"
                                    }
                                }
                            };
                        stamper.Outlines = outlines;
                    }
                }
            }
            finally
            {
                reader.Close();
            }

            File.Delete(path);
            File.Move(temporaryPath, path);
        }

        private static Dictionary<string, object> CreateGoTo(
            string title,
            string destination)
        {
            return new Dictionary<string, object>
            {
                { "Title", title },
                { "Action", "GoTo" },
                { "Page", destination }
            };
        }

        private static void Capture(Form form, string path)
        {
            using (var bitmap = new Bitmap(
                form.Width,
                form.Height))
            {
                form.DrawToBitmap(
                    bitmap,
                    new System.Drawing.Rectangle(
                        Point.Empty,
                        form.Size));
                bitmap.Save(path);
            }
        }

        private static void SelectByTitle(
            TreeView tree,
            string title)
        {
            var node = FindByTitle(tree.Nodes, title);
            Require(node != null, "Existe el nodo " + title + ".");
            tree.SelectedNode = node;
            node.EnsureVisible();
            Pump(20);
        }

        private static TreeNode FindByTitle(
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
                var child = FindByTitle(node.Nodes, title);
                if (child != null)
                {
                    return child;
                }
            }
            return null;
        }

        private static string Snapshot(
            IList<PdfBookmarkNode> bookmarks)
        {
            var builder = new StringBuilder();
            AppendSnapshot(builder, bookmarks, 0);
            return builder.ToString();
        }

        private static void AppendSnapshot(
            StringBuilder builder,
            IList<PdfBookmarkNode> bookmarks,
            int level)
        {
            foreach (var bookmark in bookmarks)
            {
                builder.Append(level);
                builder.Append('|');
                builder.Append(bookmark.Id);
                builder.Append('|');
                builder.Append(bookmark.Title);
                builder.Append('|');
                if (bookmark.Destination == null)
                {
                    builder.Append("external");
                }
                else
                {
                    builder.Append(bookmark.Destination.PageNumber);
                    builder.Append('@');
                    builder.Append(
                        bookmark.Destination.TopPositionPercent);
                }
                builder.AppendLine();
                AppendSnapshot(
                    builder,
                    bookmark.Children,
                    level + 1);
            }
        }

        private static int Count(
            IList<PdfBookmarkNode> bookmarks)
        {
            var count = 0;
            foreach (var bookmark in bookmarks)
            {
                count++;
                count += Count(bookmark.Children);
            }
            return count;
        }

        private static PdfBookmarkNode FindModelByTitle(
            IList<PdfBookmarkNode> bookmarks,
            string title)
        {
            foreach (var bookmark in bookmarks)
            {
                if (string.Equals(
                    bookmark.Title,
                    title,
                    StringComparison.Ordinal))
                {
                    return bookmark;
                }

                var child = FindModelByTitle(
                    bookmark.Children,
                    title);
                if (child != null)
                {
                    return child;
                }
            }

            return null;
        }

        private static object Invoke(
            object instance,
            string methodName,
            params object[] arguments)
        {
            var method = instance.GetType().GetMethod(
                methodName,
                BindingFlags.Instance |
                BindingFlags.NonPublic);
            Require(method != null, "Existe el método " + methodName + ".");
            return method.Invoke(instance, arguments);
        }

        private static T GetField<T>(
            object instance,
            string fieldName)
            where T : class
        {
            var field = instance.GetType().GetField(
                fieldName,
                BindingFlags.Instance |
                BindingFlags.NonPublic);
            Require(field != null, "Existe el campo " + fieldName + ".");
            var value = field.GetValue(instance) as T;
            Require(value != null, "Campo " + fieldName + " disponible.");
            return value;
        }

        private static void Pump(int milliseconds)
        {
            var until = Environment.TickCount + milliseconds;
            do
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
            while (Environment.TickCount < until);
        }

        private static void Require(
            bool condition,
            string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
            Report.Add("  PASS " + message);
        }
    }
}

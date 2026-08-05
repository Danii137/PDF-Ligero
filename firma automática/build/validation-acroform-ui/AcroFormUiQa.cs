using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using iTextSharp.text;
using iTextSharp.text.pdf;
using PdfiumViewer;
using DrawingRectangle = System.Drawing.Rectangle;
using PdfRectangle = iTextSharp.text.Rectangle;
using PdfiumDocument = PdfiumViewer.PdfDocument;

namespace FirmaAutomatica
{
    internal static class AcroFormUiQa
    {
        private static readonly List<string> Report =
            new List<string>();

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.Error.WriteLine(
                    "Uso: AcroFormUiQa <carpeta-salida>");
                return 2;
            }

            var outputDirectory = Path.GetFullPath(args[0]);
            Directory.CreateDirectory(outputDirectory);
            var reportPath = Path.Combine(
                outputDirectory,
                "qa-report.txt");

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                var fixturePath = Path.Combine(
                    outputDirectory,
                    "fixture-acroform-mixto.pdf");
                CreateMixedFixture(fixturePath);
                var document = PdfAcroFormService.Analyze(fixturePath);
                ValidateAnalysis(document);

                var capture100 = RunScale(
                    document,
                    outputDirectory,
                    1.00f,
                    "persona.notas");
                var capture125 = RunScale(
                    document,
                    outputDirectory,
                    1.25f,
                    "idioma");
                var capture150 = RunScale(
                    document,
                    outputDirectory,
                    1.50f,
                    "disciplinas");
                var acceptedChanges =
                    ValidateDifferentialChanges(document);
                var filledRevisionPath = Path.Combine(
                    outputDirectory,
                    "revision-acroform-rellenada.pdf");
                var save = PdfAcroFormService.Apply(
                    fixturePath,
                    filledRevisionPath,
                    document,
                    acceptedChanges);
                var renderedPages = ValidateAndRenderFilledRevision(
                    fixturePath,
                    filledRevisionPath,
                    outputDirectory,
                    save);

                Report.Add("RESULTADO=PASS");
                Report.Add("Fixture=" + fixturePath);
                Report.Add("Campos=" + document.Fields.Count);
                Report.Add("Editables=" + document.EditableFieldCount);
                Report.Add("Escalas=100,125,150");
                Report.Add("Captura100=" + capture100);
                Report.Add("Captura125=" + capture125);
                Report.Add("Captura150=" + capture150);
                Report.Add("RevisionRellenada=" + filledRevisionPath);
                foreach (var renderedPage in renderedPages)
                {
                    Report.Add("Render=" + renderedPage);
                }
                File.WriteAllLines(
                    reportPath,
                    Report.ToArray(),
                    Encoding.UTF8);
                Console.WriteLine(string.Join(
                    Environment.NewLine,
                    Report.ToArray()));
                Console.WriteLine("RUN=" + outputDirectory);
                return 0;
            }
            catch (Exception ex)
            {
                Report.Add("RESULTADO=FAIL");
                Report.Add(ex.ToString());
                File.WriteAllLines(
                    reportPath,
                    Report.ToArray(),
                    Encoding.UTF8);
                Console.Error.WriteLine(ex);
                Console.Error.WriteLine("RUN=" + outputDirectory);
                return 1;
            }
        }

        private static void ValidateAnalysis(PdfAcroFormDocument document)
        {
            Require(document != null, "fixture analizado");
            Require(document.PageCount == 2, "fixture de dos páginas");
            Require(document.Fields.Count == 11, "once campos detectados");
            Require(document.EditableFieldCount == 8,
                "ocho campos editables");
            Require(document.TotalWidgetCount == 12,
                "doce widgets, incluidos dos radios");

            Require(Field(document, "persona.nombre").Kind ==
                PdfAcroFormFieldKind.Text, "texto detectado");
            Require(Field(document, "persona.notas").IsMultiLine,
                "multilínea detectado");
            Require(Field(document, "clave").IsPassword,
                "contraseña detectada");
            Require(Field(document, "consentimiento").Kind ==
                PdfAcroFormFieldKind.CheckBox, "casilla detectada");
            Require(Field(document, "prioridad").Kind ==
                PdfAcroFormFieldKind.RadioButton, "radio detectado");
            Require(Field(document, "idioma").Kind ==
                PdfAcroFormFieldKind.ComboBox, "desplegable detectado");
            Require(Field(document, "provincia").Kind ==
                PdfAcroFormFieldKind.List &&
                !Field(document, "provincia").AllowsMultipleSelection,
                "lista simple detectada");
            Require(Field(document, "disciplinas").Kind ==
                PdfAcroFormFieldKind.List &&
                Field(document, "disciplinas").AllowsMultipleSelection,
                "lista múltiple detectada");
            Require(!Field(document, "solo.lectura").CanEdit,
                "solo lectura informativo");
            Require(Field(document, "firma.pendiente").Kind ==
                PdfAcroFormFieldKind.Signature &&
                !Field(document, "firma.pendiente").CanEdit,
                "firma informativa");
            Require(Field(document, "accion").Kind ==
                PdfAcroFormFieldKind.PushButton &&
                !Field(document, "accion").CanEdit,
                "botón informativo");

            Require(document.Fields.Take(5).All(field =>
                field.Widgets.Any(widget => widget.PageNumber == 1)),
                "primer bloque ordenado en página 1");
            Require(document.Fields.Skip(5).All(field =>
                field.Widgets.Any(widget => widget.PageNumber == 2)),
                "segundo bloque ordenado en página 2");
            Report.Add("PASS · fixture mixto generado y analizado");
        }

        private static string RunScale(
            PdfAcroFormDocument document,
            string outputDirectory,
            float scale,
            string screenshotField)
        {
            var suffix = ((int)Math.Round(scale * 100f)).ToString();
            using (var form = new PdfAcroFormFillForm(document))
            {
                var baseClientSize = form.ClientSize;
                if (Math.Abs(scale - 1f) > 0.001f)
                {
                    form.Scale(new SizeF(scale, scale));
                    form.ClientSize = new Size(
                        (int)Math.Round(baseClientSize.Width * scale),
                        (int)Math.Round(baseClientSize.Height * scale));
                }

                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-30000, -30000);
                form.ShowInTaskbar = false;
                form.Show();
                Pump(180);

                ValidateEveryEditor(form, document, suffix);
                SelectField(form, screenshotField);
                Pump(35);
                ValidateLayout(form, "escala " + suffix + "%");

                var capturePath = Path.Combine(
                    outputDirectory,
                    "acroform-completo-" + suffix + ".png");
                SaveCompleteWindow(form, capturePath);
                ValidateCapture(capturePath, form.Size);
                form.Close();

                Report.Add(
                    "PASS · editores y layout sin solapes al " +
                    suffix + "%");
                return capturePath;
            }
        }

        private static void ValidateEveryEditor(
            PdfAcroFormFillForm form,
            PdfAcroFormDocument document,
            string suffix)
        {
            var list = GetPrivateField<ListBox>(form, "fieldList");
            if (list.Items.Count == 0)
            {
                InvokePrivate(form, "RebuildFieldList");
            }

            Require(list.Items.Count == document.Fields.Count,
                "lista UI completa al " + suffix + "%");
            ExpectEditor(form, "persona.nombre", typeof(TextBox));
            var nameEditor = GetPrivateField<TextBox>(form, "activeEditor");
            Require(!nameEditor.Multiline &&
                !nameEditor.UseSystemPasswordChar,
                "editor de texto simple");

            ExpectEditor(form, "persona.notas", typeof(TextBox));
            Require(GetPrivateField<TextBox>(form, "activeEditor").Multiline,
                "editor multilínea");

            ExpectEditor(form, "clave", typeof(TextBox));
            Require(GetPrivateField<TextBox>(form, "activeEditor")
                .UseSystemPasswordChar, "editor de contraseña protegido");

            ExpectEditor(form, "consentimiento", typeof(CheckBox));
            ExpectEditor(form, "prioridad", typeof(ComboBox));
            ExpectEditor(form, "idioma", typeof(ComboBox));
            ExpectEditor(form, "provincia", typeof(ListBox));
            ExpectEditor(form, "disciplinas", typeof(CheckedListBox));
            ExpectEditor(form, "solo.lectura", typeof(Label));
            ExpectEditor(form, "firma.pendiente", typeof(Label));
            ExpectEditor(form, "accion", typeof(Label));

            Require(form.BuildChanges().Count == 0,
                "recorrer editores no genera cambios");
        }

        private static void ExpectEditor(
            PdfAcroFormFillForm form,
            string fieldName,
            Type expectedType)
        {
            SelectField(form, fieldName);
            var editor = GetPrivateField<Control>(form, "activeEditor");
            var activeField = GetPrivateField<PdfAcroFormField>(
                form,
                "activeField");
            var host = GetPrivateField<Panel>(form, "editorControlHost");
            Require(activeField != null && activeField.Name == fieldName,
                "campo activo " + fieldName);
            Require(editor != null && editor.GetType() == expectedType,
                "editor " + expectedType.Name + " para " + fieldName);
            Require(host.Controls.Count == 1 &&
                object.ReferenceEquals(host.Controls[0], editor),
                "un único editor materializado para " + fieldName);
            ValidateLayout(form, "campo " + fieldName);
        }

        private static IList<PdfAcroFormFieldChange>
            ValidateDifferentialChanges(
            PdfAcroFormDocument document)
        {
            IList<PdfAcroFormFieldChange> acceptedChanges;
            using (var form = new PdfAcroFormFillForm(document))
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-30000, -30000);
                form.ShowInTaskbar = false;
                form.Show();
                Pump(100);

                SelectField(form, "persona.nombre");
                GetPrivateField<TextBox>(form, "activeEditor").Text =
                    "東京";

                SelectField(form, "persona.notas");
                GetPrivateField<TextBox>(form, "activeEditor").Text =
                    "Revisión técnica\r\nLínea 2: Málaga";

                SelectField(form, "consentimiento");
                GetPrivateField<CheckBox>(form, "activeEditor").Checked = true;

                SelectField(form, "prioridad");
                SelectChoiceByExport(
                    GetPrivateField<ComboBox>(form, "activeEditor"),
                    "urgente");

                SelectField(form, "idioma");
                SelectChoiceByExport(
                    GetPrivateField<ComboBox>(form, "activeEditor"),
                    "fr");

                SelectField(form, "provincia");
                SelectChoiceByExport(
                    GetPrivateField<ListBox>(form, "activeEditor"),
                    "M");

                SelectField(form, "disciplinas");
                var choices = GetPrivateField<CheckedListBox>(
                    form,
                    "activeEditor");
                for (var index = 0; index < choices.Items.Count; index++)
                {
                    choices.SetItemChecked(index, index == 1 || index == 2);
                }

                var changes = form.BuildChanges();
                Require(changes.Count == 7,
                    "solo siete cambios diferenciales");
                Require(Change(changes, "persona.nombre").Value ==
                    "東京", "texto Unicode conservado por UI");
                Require(Change(changes, "persona.notas").Value ==
                    "Revisión técnica\r\nLínea 2: Málaga",
                    "multilínea conservado por UI");
                Require(Change(changes, "consentimiento").Value ==
                    "Aceptado", "estado exportado de casilla");
                Require(Change(changes, "prioridad").Value == "urgente",
                    "estado exportado de radio");
                Require(Change(changes, "idioma").Value == "fr",
                    "estado exportado de desplegable");
                Require(Change(changes, "provincia").Value == "M",
                    "estado exportado de lista simple");
                Require(Change(changes, "disciplinas").Values
                    .SequenceEqual(new[]
                    {
                        "estructuras",
                        "instalaciones"
                    }), "selección múltiple diferencial");
                Require(form.HasChanges, "indicador de cambios activo");

                var search = GetPrivateField<TextBox>(form, "searchTextBox");
                var list = GetPrivateField<ListBox>(form, "fieldList");
                search.Text = "persona.nombre";
                Pump(25);
                Require(list.Items.Count == 1 &&
                    GetItemFieldName(list.Items[0]) == "persona.nombre",
                    "filtro conserva el campo exacto");
                Require(form.BuildChanges().Count == 7,
                    "filtro conserva todos los borradores");
                search.Clear();
                Pump(25);
                Require(list.Items.Count == document.Fields.Count,
                    "limpiar filtro restaura la lista");

                GetPrivateField<Button>(form, "applyButton").PerformClick();
                Require(form.DialogResult == DialogResult.OK,
                    "Aplicar acepta la edición modal");
                Require(form.Changes.Count == 7 &&
                    form.ChangedFieldCount == 7,
                    "el modal devuelve solo cambios aceptados");
                acceptedChanges = new List<PdfAcroFormFieldChange>(
                    form.Changes).AsReadOnly();
            }

            Report.Add("PASS · cambios diferenciales y contrato modal");
            return acceptedChanges;
        }

        private static IList<string> ValidateAndRenderFilledRevision(
            string sourcePath,
            string revisionPath,
            string outputDirectory,
            PdfAcroFormSaveResult save)
        {
            Require(save != null && save.ChangedFieldCount == 7,
                "revisión conserva siete cambios");
            Require(File.Exists(revisionPath) &&
                new FileInfo(revisionPath).Length >
                new FileInfo(sourcePath).Length,
                "revisión incremental conservada en el RUN");

            var filled = PdfAcroFormService.Analyze(revisionPath);
            Require(Field(filled, "persona.nombre").Value ==
                "東京", "Unicode reabierto desde la revisión");
            Require(Field(filled, "persona.notas").Value ==
                "Revisión técnica\r\nLínea 2: Málaga",
                "multilínea reabierto desde la revisión");
            Require(Field(filled, "consentimiento").Value == "Aceptado",
                "casilla reabierta desde la revisión");
            Require(Field(filled, "prioridad").Value == "urgente",
                "radio reabierto desde la revisión");
            Require(Field(filled, "idioma").Value == "fr",
                "desplegable reabierto desde la revisión");
            Require(Field(filled, "provincia").Value == "M",
                "lista simple reabierta desde la revisión");
            Require(Field(filled, "disciplinas").SelectedValues
                .SequenceEqual(new[]
                {
                    "estructuras",
                    "instalaciones"
                }), "lista múltiple reabierta desde la revisión");

            var renderedPages = new List<string>();
            using (var source = PdfiumDocument.Load(sourcePath))
            using (var revision = PdfiumDocument.Load(revisionPath))
            {
                Require(source.PageCount == revision.PageCount &&
                    revision.PageCount == 2,
                    "render mantiene las dos páginas");
                for (var page = 0; page < revision.PageCount; page++)
                {
                    var pageSize = revision.PageSizes[page];
                    const int width = 1200;
                    var height = Math.Max(
                        1,
                        (int)Math.Round(
                            width * pageSize.Height / pageSize.Width));
                    var flags = PdfRenderFlags.Annotations |
                        PdfRenderFlags.LcdText |
                        PdfRenderFlags.LimitImageCacheSize;
                    using (var sourceImage = source.Render(
                        page,
                        width,
                        height,
                        120f,
                        120f,
                        flags))
                    using (var revisionImage = revision.Render(
                        page,
                        width,
                        height,
                        120f,
                        120f,
                        flags))
                    using (var sourceBitmap = new Bitmap(sourceImage))
                    using (var revisionBitmap = new Bitmap(revisionImage))
                    {
                        Require(CountDifferentSamples(
                                sourceBitmap,
                                revisionBitmap) > 80,
                            "apariencias visibles modifican la página " +
                            (page + 1).ToString());
                        if (page == 0)
                        {
                            Require(CountDifferentSamplesInPdfRegion(
                                    sourceBitmap,
                                    revisionBitmap,
                                    pageSize,
                                    74f,
                                    704f,
                                    130f,
                                    726f) > 15,
                                "glifos Unicode visibles en su apariencia");
                        }
                        var renderPath = Path.Combine(
                            outputDirectory,
                            "revision-rellenada-pagina-" +
                            (page + 1).ToString() + ".png");
                        revisionBitmap.Save(renderPath, ImageFormat.Png);
                        Require(File.Exists(renderPath) &&
                            new FileInfo(renderPath).Length > 4096,
                            "render PNG de página " +
                            (page + 1).ToString());
                        renderedPages.Add(renderPath);
                    }
                }
            }

            Report.Add(
                "PASS · revisión rellenada reabierta y apariencias renderizadas");
            return renderedPages.AsReadOnly();
        }

        private static int CountDifferentSamples(
            Bitmap first,
            Bitmap second)
        {
            Require(first.Width == second.Width &&
                first.Height == second.Height,
                "renders comparables");
            var different = 0;
            for (var y = 0; y < first.Height; y += 2)
            {
                for (var x = 0; x < first.Width; x += 2)
                {
                    var left = first.GetPixel(x, y);
                    var right = second.GetPixel(x, y);
                    if (Math.Abs(left.R - right.R) > 8 ||
                        Math.Abs(left.G - right.G) > 8 ||
                        Math.Abs(left.B - right.B) > 8)
                    {
                        different++;
                    }
                }
            }

            return different;
        }

        private static int CountDifferentSamplesInPdfRegion(
            Bitmap first,
            Bitmap second,
            SizeF pageSize,
            float left,
            float bottom,
            float right,
            float top)
        {
            Require(first.Width == second.Width &&
                first.Height == second.Height &&
                pageSize.Width > 0f && pageSize.Height > 0f,
                "región renderizada comparable");
            var pixelLeft = Math.Max(0, Math.Min(
                first.Width - 1,
                (int)Math.Floor(left * first.Width / pageSize.Width)));
            var pixelRight = Math.Max(pixelLeft + 1, Math.Min(
                first.Width,
                (int)Math.Ceiling(right * first.Width / pageSize.Width)));
            var pixelTop = Math.Max(0, Math.Min(
                first.Height - 1,
                (int)Math.Floor(
                    (pageSize.Height - top) *
                    first.Height /
                    pageSize.Height)));
            var pixelBottom = Math.Max(pixelTop + 1, Math.Min(
                first.Height,
                (int)Math.Ceiling(
                    (pageSize.Height - bottom) *
                    first.Height /
                    pageSize.Height)));
            var different = 0;
            for (var y = pixelTop; y < pixelBottom; y++)
            {
                for (var x = pixelLeft; x < pixelRight; x++)
                {
                    var sourcePixel = first.GetPixel(x, y);
                    var resultPixel = second.GetPixel(x, y);
                    if (Math.Abs(sourcePixel.R - resultPixel.R) > 8 ||
                        Math.Abs(sourcePixel.G - resultPixel.G) > 8 ||
                        Math.Abs(sourcePixel.B - resultPixel.B) > 8)
                    {
                        different++;
                    }
                }
            }

            return different;
        }

        private static void SelectChoiceByExport(
            ComboBox control,
            string exportValue)
        {
            SelectChoiceByExportCore(
                control.Items.Cast<object>().ToList(),
                delegate(int index) { control.SelectedIndex = index; },
                exportValue);
        }

        private static void SelectChoiceByExport(
            ListBox control,
            string exportValue)
        {
            SelectChoiceByExportCore(
                control.Items.Cast<object>().ToList(),
                delegate(int index) { control.SelectedIndex = index; },
                exportValue);
        }

        private static void SelectChoiceByExportCore(
            IList<object> items,
            Action<int> select,
            string exportValue)
        {
            for (var index = 0; index < items.Count; index++)
            {
                var property = items[index].GetType().GetProperty(
                    "ExportValue",
                    BindingFlags.Instance | BindingFlags.Public);
                var value = property == null
                    ? string.Empty
                    : property.GetValue(items[index], null) as string;
                if (string.Equals(
                        value,
                        exportValue,
                        StringComparison.Ordinal))
                {
                    select(index);
                    return;
                }
            }

            throw new InvalidOperationException(
                "No se encuentra la opción exportada: " + exportValue);
        }

        private static void ValidateLayout(Form form, string state)
        {
            ValidateControlTree(form, state, "Formulario");

            var footerStatus = GetPrivateField<Control>(
                form,
                "footerStatusLabel");
            var apply = GetPrivateField<Control>(form, "applyButton");
            Require(!HasPositiveIntersection(
                    footerStatus.Bounds,
                    apply.Bounds),
                "estado y Aplicar separados en " + state);

            var fieldList = GetPrivateField<Control>(form, "fieldList");
            var editorHost = GetPrivateField<Control>(
                form,
                "editorControlHost");
            var fieldListOnForm = form.RectangleToClient(
                fieldList.Parent.RectangleToScreen(fieldList.Bounds));
            var editorOnForm = form.RectangleToClient(
                editorHost.Parent.RectangleToScreen(editorHost.Bounds));
            Require(!HasPositiveIntersection(
                    fieldListOnForm,
                    editorOnForm),
                "lista y editor separados en " + state);
        }

        private static void ValidateControlTree(
            Control parent,
            string state,
            string path)
        {
            var visibleChildren = parent.Controls
                .Cast<Control>()
                .Where(child => child.Visible)
                .ToList();
            foreach (var child in visibleChildren)
            {
                var bounds = child.Bounds;
                Require(bounds.Width > 0 && bounds.Height > 0,
                    "tamaño positivo de " + Describe(child) +
                    " en " + state);
                Require(bounds.Left >= -1 && bounds.Top >= -1 &&
                    bounds.Right <= parent.ClientSize.Width + 1 &&
                    bounds.Bottom <= parent.ClientSize.Height + 1,
                    Describe(child) + " dentro de " + path +
                    " en " + state + " (hijo=" + bounds +
                    ", padre=" + parent.ClientSize + ")");
            }

            for (var first = 0; first < visibleChildren.Count; first++)
            {
                for (var second = first + 1;
                    second < visibleChildren.Count;
                    second++)
                {
                    Require(!HasPositiveIntersection(
                            visibleChildren[first].Bounds,
                            visibleChildren[second].Bounds),
                        "sin solape entre " +
                        Describe(visibleChildren[first]) + " y " +
                        Describe(visibleChildren[second]) +
                        " en " + state);
                }
            }

            foreach (var child in visibleChildren)
            {
                if (child.HasChildren)
                {
                    ValidateControlTree(
                        child,
                        state,
                        path + "/" + Describe(child));
                }
            }
        }

        private static bool HasPositiveIntersection(
            DrawingRectangle first,
            DrawingRectangle second)
        {
            var intersection = DrawingRectangle.Intersect(first, second);
            return intersection.Width > 0 && intersection.Height > 0;
        }

        private static string Describe(Control control)
        {
            var text = (control.Text ?? string.Empty)
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();
            if (text.Length > 28)
            {
                text = text.Substring(0, 28) + "…";
            }

            return control.GetType().Name +
                (string.IsNullOrEmpty(text)
                    ? string.Empty
                    : "[" + text + "]");
        }

        private static void SaveCompleteWindow(Form form, string path)
        {
            using (var bitmap = new Bitmap(
                form.Width,
                form.Height,
                PixelFormat.Format32bppArgb))
            {
                form.DrawToBitmap(
                    bitmap,
                    new DrawingRectangle(Point.Empty, form.Size));
                bitmap.Save(path, ImageFormat.Png);
            }
        }

        private static void ValidateCapture(string path, Size expectedSize)
        {
            Require(File.Exists(path) && new FileInfo(path).Length > 4096,
                "captura completa escrita");
            using (var image = System.Drawing.Image.FromFile(path))
            {
                Require(image.Width == expectedSize.Width &&
                    image.Height == expectedSize.Height,
                    "captura conserva el tamaño completo de la ventana");
            }
        }

        private static void SelectField(
            PdfAcroFormFillForm form,
            string fieldName)
        {
            var list = GetPrivateField<ListBox>(form, "fieldList");
            for (var index = 0; index < list.Items.Count; index++)
            {
                if (GetItemFieldName(list.Items[index]) == fieldName)
                {
                    list.SelectedIndex = index;
                    Pump(8);
                    return;
                }
            }

            throw new InvalidOperationException(
                "No se encuentra el campo UI: " + fieldName);
        }

        private static string GetItemFieldName(object item)
        {
            if (item == null)
            {
                return string.Empty;
            }

            var property = item.GetType().GetProperty(
                "Field",
                BindingFlags.Instance | BindingFlags.Public);
            var field = property == null
                ? null
                : property.GetValue(item, null) as PdfAcroFormField;
            return field == null ? string.Empty : field.Name;
        }

        private static PdfAcroFormField Field(
            PdfAcroFormDocument document,
            string name)
        {
            return document.Fields.Single(field => field.Name == name);
        }

        private static PdfAcroFormFieldChange Change(
            IEnumerable<PdfAcroFormFieldChange> changes,
            string name)
        {
            return changes.Single(change => change.FieldName == name);
        }

        private static T GetPrivateField<T>(object target, string name)
            where T : class
        {
            var field = target.GetType().GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new InvalidOperationException(
                    "No existe el miembro privado de QA: " + name);
            }

            var value = field.GetValue(target) as T;
            if (value == null)
            {
                throw new InvalidOperationException(
                    "El miembro privado no tiene el tipo esperado: " + name);
            }

            return value;
        }

        private static object InvokePrivate(
            object target,
            string name,
            params object[] arguments)
        {
            var method = target.GetType().GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null)
            {
                throw new InvalidOperationException(
                    "No existe el método privado de QA: " + name);
            }

            return method.Invoke(target, arguments);
        }

        private static void CreateMixedFixture(string path)
        {
            var document = new Document(PageSize.A4);
            using (var output = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            {
                var writer = PdfWriter.GetInstance(document, output);
                document.AddTitle("Fixture AcroForm mixto para QA visual");
                document.Open();
                document.Add(new Paragraph(
                    "Página 1 - identificación y decisiones"));

                AddTextField(
                    writer,
                    "persona.nombre",
                    new PdfRectangle(72, 700, 330, 730),
                    0,
                    string.Empty);
                AddTextField(
                    writer,
                    "persona.notas",
                    new PdfRectangle(72, 590, 330, 680),
                    TextField.MULTILINE,
                    "Memoria inicial");
                AddTextField(
                    writer,
                    "clave",
                    new PdfRectangle(72, 535, 250, 565),
                    TextField.PASSWORD,
                    "secreto");

                var check = new RadioCheckField(
                    writer,
                    new PdfRectangle(72, 480, 94, 502),
                    "consentimiento",
                    "Aceptado")
                {
                    CheckType = RadioCheckField.TYPE_CHECK,
                    Checked = false,
                    BorderColor = BaseColor.DARK_GRAY,
                    BorderWidth = 1
                };
                writer.AddAnnotation(check.CheckField);

                var radio = PdfFormField.CreateRadioButton(writer, true);
                radio.FieldName = "prioridad";
                radio.ValueAsName = "normal";
                var normal = new RadioCheckField(
                    writer,
                    new PdfRectangle(72, 420, 94, 442),
                    null,
                    "normal")
                {
                    CheckType = RadioCheckField.TYPE_CIRCLE,
                    Checked = true
                };
                var urgente = new RadioCheckField(
                    writer,
                    new PdfRectangle(122, 420, 144, 442),
                    null,
                    "urgente")
                {
                    CheckType = RadioCheckField.TYPE_CIRCLE,
                    Checked = false
                };
                radio.AddKid(normal.RadioField);
                radio.AddKid(urgente.RadioField);
                writer.AddAnnotation(radio);

                document.NewPage();
                document.Add(new Paragraph(
                    "Página 2 - opciones y campos informativos"));

                var combo = new TextField(
                    writer,
                    new PdfRectangle(72, 690, 300, 722),
                    "idioma")
                {
                    Choices = new[] { "Español", "English", "Français" },
                    ChoiceExports = new[] { "es", "en", "fr" },
                    ChoiceSelection = 0,
                    FontSize = 10,
                    BorderColor = BaseColor.GRAY,
                    BorderWidth = 1
                };
                writer.AddAnnotation(combo.GetComboField());

                var singleList = new TextField(
                    writer,
                    new PdfRectangle(72, 575, 300, 660),
                    "provincia")
                {
                    Choices = new[] { "Toledo", "Madrid", "Cuenca" },
                    ChoiceExports = new[] { "TO", "M", "CU" },
                    ChoiceSelection = 0,
                    FontSize = 10,
                    BorderColor = BaseColor.GRAY,
                    BorderWidth = 1
                };
                writer.AddAnnotation(singleList.GetListField());

                var multipleList = new TextField(
                    writer,
                    new PdfRectangle(72, 430, 300, 545),
                    "disciplinas")
                {
                    Choices = new[]
                    {
                        "Arquitectura",
                        "Estructuras",
                        "Instalaciones"
                    },
                    ChoiceExports = new[]
                    {
                        "arquitectura",
                        "estructuras",
                        "instalaciones"
                    },
                    ChoiceSelections = new List<int> { 0 },
                    Options = TextField.MULTISELECT,
                    FontSize = 10,
                    BorderColor = BaseColor.GRAY,
                    BorderWidth = 1
                };
                writer.AddAnnotation(multipleList.GetListField());

                AddTextField(
                    writer,
                    "solo.lectura",
                    new PdfRectangle(72, 370, 300, 402),
                    TextField.READ_ONLY,
                    "Expediente VUMR-2026-041");

                var signature = PdfFormField.CreateSignature(writer);
                signature.FieldName = "firma.pendiente";
                signature.SetWidget(
                    new PdfRectangle(330, 520, 515, 580),
                    PdfAnnotation.HIGHLIGHT_INVERT);
                writer.AddAnnotation(signature);

                var push = PdfFormField.CreatePushButton(writer);
                push.FieldName = "accion";
                push.SetWidget(
                    new PdfRectangle(330, 455, 455, 490),
                    PdfAnnotation.HIGHLIGHT_PUSH);
                writer.AddAnnotation(push);

                document.Close();
            }
        }

        private static void AddTextField(
            PdfWriter writer,
            string name,
            PdfRectangle rectangle,
            int options,
            string value)
        {
            var field = new TextField(writer, rectangle, name)
            {
                Text = value ?? string.Empty,
                Options = options,
                FontSize = 10,
                BorderColor = BaseColor.GRAY,
                BorderWidth = 1
            };
            writer.AddAnnotation(field.GetTextField());
        }

        private static void Pump(int milliseconds)
        {
            var deadline = Environment.TickCount + milliseconds;
            do
            {
                Application.DoEvents();
                Thread.Sleep(5);
            }
            while (Environment.TickCount < deadline);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(
                    "Fallo QA: " + message);
            }
        }
    }
}

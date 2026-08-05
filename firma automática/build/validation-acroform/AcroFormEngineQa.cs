using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using iTextSharp.text;
using iTextSharp.text.pdf;

namespace FirmaAutomatica
{
    internal static class AcroFormEngineQa
    {
        private static string runDirectory;

        [STAThread]
        public static int Main()
        {
            runDirectory = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "run-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(runDirectory);
            var report = new List<string>();
            try
            {
                var source = Path.Combine(runDirectory, "formulario.pdf");
                CreateFixture(source, false);
                var sourceHash =
                    PdfAtomicFileService.ComputeFullContentHash(source);
                var analyzed = PdfAcroFormService.Analyze(source);
                Assert(analyzed.Fields.Count >= 8, "campos detectados");
                Assert(analyzed.EditableFieldCount >= 6, "campos editables");
                Assert(
                    !string.IsNullOrWhiteSpace(analyzed.XmpHash),
                    "fixture con XMP real");
                ValidateXmpSemanticRules(source, analyzed.XmpHash);
                Assert(analyzed.Fields.Any(field =>
                    field.Kind == PdfAcroFormFieldKind.Signature &&
                    !field.CanEdit), "firma informativa");
                Assert(analyzed.Fields.Any(field =>
                    field.Name == "solo.lectura" &&
                    !field.CanEdit), "readonly bloqueado");
                Assert(analyzed.Fields.Select(field => field.Name)
                    .SequenceEqual(new[]
                    {
                        "persona.nombre",
                        "persona.notas",
                        "solo.lectura",
                        "firma.pendiente",
                        "acepta",
                        "tipo",
                        "accion",
                        "idioma",
                        "capitulos"
                    }), "orden visual por página, Y y X");
                ValidateBasicUi(analyzed);

                var session = PdfEditSession.Create(source);
                var revision = session.ReserveRevisionPath(
                    new FileInfo(source).Length);
                var changes = new[]
                {
                    PdfAcroFormFieldChange.ForValue(
                        "persona.nombre",
                        "Álvaro Núñez 東京"),
                    PdfAcroFormFieldChange.ForValue(
                        "persona.notas",
                        "Primera línea\r\nSegunda línea"),
                    PdfAcroFormFieldChange.ForValue(
                        "acepta",
                        "Aceptado"),
                    PdfAcroFormFieldChange.ForValue(
                        "tipo",
                        "B"),
                    PdfAcroFormFieldChange.ForValue(
                        "idioma",
                        "es"),
                    PdfAcroFormFieldChange.ForValues(
                        "capitulos",
                        new[] { "uno", "tres" })
                };
                var save = PdfAcroFormService.Apply(
                    source,
                    revision,
                    analyzed,
                    changes);
                Assert(save.ChangedFieldCount == 6, "seis cambios");
                Assert(File.Exists(revision), "revisión publicada");
                Assert(
                    PdfAtomicFileService.ComputeFullContentHash(source) ==
                    sourceHash,
                    "original intacto");

                var result = PdfAcroFormService.Analyze(revision);
                Assert(
                    result.XmpHash == analyzed.XmpHash,
                    "XMP descriptivo/personalizado conservado");
                ValidateWrittenXmp(source, revision);
                Assert(Value(result, "persona.nombre") ==
                    "Álvaro Núñez 東京", "texto Unicode");
                Assert(Value(result, "acepta") == "Aceptado", "checkbox");
                Assert(Value(result, "tipo") == "B", "radio");
                Assert(Value(result, "idioma") == "es", "combo export");
                Assert(Selected(result, "capitulos")
                    .SequenceEqual(new[] { "uno", "tres" }),
                    "lista múltiple");

                var commit = session.BeginRevisionCommit(
                    revision,
                    "Formulario rellenado");
                commit.Complete();
                Assert(session.CanUndo, "undo disponible");
                Assert(session.Undo() == source, "undo vuelve al original");
                Assert(session.CanRedo, "redo disponible");
                Assert(session.Redo() == revision, "redo vuelve a revisión");
                session.DeleteRecovery();

                var protectedPath = Path.Combine(
                    runDirectory,
                    "protegido.pdf");
                CreateFixture(protectedPath, true);
                AssertThrows(
                    delegate { PdfAcroFormService.Analyze(protectedPath); },
                    "protegido bloqueado");

                var changedSource = Path.Combine(
                    runDirectory,
                    "cambio-externo.pdf");
                File.Copy(source, changedSource);
                var changedAnalysis =
                    PdfAcroFormService.Analyze(changedSource);
                using (var append = new FileStream(
                    changedSource,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.None))
                {
                    append.WriteByte(0x20);
                }
                AssertThrows(
                    delegate
                    {
                        PdfAcroFormService.Apply(
                            changedSource,
                            Path.Combine(runDirectory, "no-debe-existir.pdf"),
                            changedAnalysis,
                            new[]
                            {
                                PdfAcroFormFieldChange.ForValue(
                                    "persona.nombre",
                                    "Cambio")
                            });
                    },
                    "cambio externo bloqueado");

                var readOnlyAnalysis =
                    PdfAcroFormService.Analyze(source);
                AssertThrows(
                    delegate
                    {
                        PdfAcroFormService.Apply(
                            source,
                            Path.Combine(runDirectory, "readonly.pdf"),
                            readOnlyAnalysis,
                            new[]
                            {
                                PdfAcroFormFieldChange.ForValue(
                                    "solo.lectura",
                                    "No")
                            });
                    },
                    "readonly no modificable");

                report.Add("RESULTADO=PASS");
                report.Add("Campos=" + analyzed.Fields.Count);
                report.Add("Editables=" + analyzed.EditableFieldCount);
                report.Add("Cambios=" + save.ChangedFieldCount);
                report.Add("Unicode=PASS");
                report.Add("AP/canonical/prefix=PASS");
                report.Add("XMP semántico/custom=PASS");
                report.Add("UI ligera/cambios diferenciales=PASS");
                report.Add("Recovery Undo/Redo=PASS");
                report.Add("Protegido/source-change/readonly=PASS");
                File.WriteAllLines(
                    Path.Combine(runDirectory, "qa-report.txt"),
                    report);
                Console.WriteLine(string.Join(Environment.NewLine, report));
                Console.WriteLine("RUN=" + runDirectory);
                return 0;
            }
            catch (Exception ex)
            {
                report.Add("RESULTADO=FAIL");
                report.Add(ex.ToString());
                File.WriteAllLines(
                    Path.Combine(runDirectory, "qa-report.txt"),
                    report);
                Console.Error.WriteLine(ex);
                Console.Error.WriteLine("RUN=" + runDirectory);
                return 1;
            }
        }

        private static string Value(
            PdfAcroFormDocument document,
            string name)
        {
            return document.Fields.Single(field => field.Name == name).Value;
        }

        private static IList<string> Selected(
            PdfAcroFormDocument document,
            string name)
        {
            return document.Fields.Single(
                field => field.Name == name).SelectedValues;
        }

        private static void ValidateBasicUi(PdfAcroFormDocument document)
        {
            using (var form = new PdfAcroFormFillForm(document))
            {
                InvokePrivate(form, "RebuildFieldList");

                var list = (ListBox)GetPrivateField(form, "fieldList");
                Assert(list.Items.Count == document.Fields.Count,
                    "UI enumera todos los campos");
                Assert(form.BuildChanges().Count == 0,
                    "UI no crea cambios espurios");

                var nameIndex = -1;
                for (var index = 0; index < list.Items.Count; index++)
                {
                    if ((list.Items[index] == null
                            ? string.Empty
                            : list.Items[index].ToString()).IndexOf(
                            "persona.nombre",
                            StringComparison.Ordinal) >= 0)
                    {
                        nameIndex = index;
                        break;
                    }
                }

                Assert(nameIndex >= 0, "UI localiza el campo de texto");
                list.SelectedIndex = nameIndex;
                var editor = GetPrivateField(form, "activeEditor") as TextBox;
                Assert(editor != null,
                    "UI materializa un único editor de texto");
                editor.Text = "Cambio desde UI";

                var changes = form.BuildChanges();
                Assert(changes.Count == 1,
                    "UI emite solo el campo modificado");
                Assert(changes[0].FieldName == "persona.nombre" &&
                    changes[0].Value == "Cambio desde UI",
                    "UI conserva nombre y valor editado");

                var search = (TextBox)GetPrivateField(
                    form,
                    "searchTextBox");
                search.Text = "persona.nombre";
                Assert(list.Items.Count == 1,
                    "UI filtra campos sin perder el borrador");
                Assert(form.HasChanges,
                    "UI informa cambios pendientes");
            }
        }

        private static object GetPrivateField(object instance, string name)
        {
            var field = instance.GetType().GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new InvalidOperationException(
                    "Fallo QA: no existe el miembro UI " + name);
            }

            return field.GetValue(instance);
        }

        private static void InvokePrivate(object instance, string name)
        {
            var method = instance.GetType().GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null)
            {
                throw new InvalidOperationException(
                    "Fallo QA: no existe el método UI " + name);
            }

            method.Invoke(instance, null);
        }

        private static void ValidateXmpSemanticRules(
            string sourcePath,
            string analyzedHash)
        {
            var sourceBytes = ReadXmpBytes(sourcePath);
            var sourceText = System.Text.Encoding.UTF8.GetString(sourceBytes);
            Assert(SemanticXmpHash(sourceBytes) == analyzedHash,
                "huella XMP semántica del análisis");

            var technicalVariant = sourceText
                .Replace(
                    "Productor QA original",
                    "iTextSharp 5.5.13.3")
                .Replace(
                    "2026-08-04T10:15:00+02:00",
                    "2026-08-04T19:55:00+02:00")
                .Replace(
                    "2026-08-04T10:16:00+02:00",
                    "2026-08-04T19:56:00+02:00");
            Assert(SemanticXmpHash(ToUtf8(technicalVariant)) ==
                analyzedHash,
                "solo Producer/ModifyDate/MetadataDate son tolerables");

            var titleVariant = sourceText.Replace(
                "Fixture AcroForm XMP",
                "Título descriptivo alterado");
            Assert(SemanticXmpHash(ToUtf8(titleVariant)) != analyzedHash,
                "título XMP se compara estrictamente");

            var customVariant = sourceText.Replace(
                "VUMR-XMP-042",
                "VUMR-XMP-999");
            Assert(SemanticXmpHash(ToUtf8(customVariant)) != analyzedHash,
                "propiedad XMP personalizada se compara estrictamente");
        }

        private static void ValidateWrittenXmp(
            string sourcePath,
            string revisionPath)
        {
            var sourceBytes = ReadXmpBytes(sourcePath);
            var revisionBytes = ReadXmpBytes(revisionPath);
            Assert(!sourceBytes.SequenceEqual(revisionBytes),
                "iText actualiza el paquete XMP técnico");
            Assert(SemanticXmpHash(sourceBytes) ==
                SemanticXmpHash(revisionBytes),
                "XMP equivalente tras ignorar solo campos técnicos");

            var revisionText =
                System.Text.Encoding.UTF8.GetString(revisionBytes);
            Assert(revisionText.Contains("VUMR-XMP-042") &&
                revisionText.Contains("Licencias urbanísticas") &&
                revisionText.Contains("Fixture AcroForm XMP"),
                "XMP personalizado y descriptivo permanece en la revisión");
        }

        private static byte[] ReadXmpBytes(string path)
        {
            var reader = new PdfReader(path);
            try
            {
                return reader.Metadata ?? new byte[0];
            }
            finally
            {
                reader.Close();
            }
        }

        private static string SemanticXmpHash(byte[] bytes)
        {
            var method = typeof(PdfAcroFormService).GetMethod(
                "ComputeXmpSemanticHash",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (method == null)
            {
                throw new InvalidOperationException(
                    "Fallo QA: no existe el comparador XMP semántico.");
            }

            return (string)method.Invoke(null, new object[] { bytes });
        }

        private static byte[] ToUtf8(string value)
        {
            return new System.Text.UTF8Encoding(false).GetBytes(
                value ?? string.Empty);
        }

        private static void CreateFixture(string path, bool encrypted)
        {
            var document = new Document(PageSize.A4);
            using (var output = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            {
                var writer = PdfWriter.GetInstance(document, output);
                if (encrypted)
                {
                    writer.SetEncryption(
                        System.Text.Encoding.ASCII.GetBytes("usuario"),
                        System.Text.Encoding.ASCII.GetBytes("propietario"),
                        PdfWriter.ALLOW_FILL_IN |
                        PdfWriter.ALLOW_PRINTING,
                        PdfWriter.ENCRYPTION_AES_128);
                }

                document.AddTitle("Fixture AcroForm");
                writer.XmpMetadata = new System.Text.UTF8Encoding(false)
                    .GetBytes(
                        "<?xpacket begin='\uFEFF' id='W5M0MpCehiHzreSzNTczkc9d'?>" +
                        "<x:xmpmeta xmlns:x='adobe:ns:meta/'>" +
                        "<rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#'>" +
                        "<rdf:Description rdf:about='' " +
                        "xmlns:dc='http://purl.org/dc/elements/1.1/' " +
                        "xmlns:pdf='http://ns.adobe.com/pdf/1.3/' " +
                        "xmlns:xmp='http://ns.adobe.com/xap/1.0/' " +
                        "xmlns:agoin='https://agoin.es/ns/pdf-ligero/1.0/'>" +
                        "<pdf:Producer>Productor QA original</pdf:Producer>" +
                        "<xmp:ModifyDate>2026-08-04T10:15:00+02:00</xmp:ModifyDate>" +
                        "<xmp:MetadataDate>2026-08-04T10:16:00+02:00</xmp:MetadataDate>" +
                        "<dc:title><rdf:Alt><rdf:li xml:lang='x-default'>" +
                        "Fixture AcroForm XMP</rdf:li></rdf:Alt></dc:title>" +
                        "<dc:description><rdf:Alt>" +
                        "<rdf:li xml:lang='x-default'>Licencias urbanísticas</rdf:li>" +
                        "</rdf:Alt></dc:description>" +
                        "<agoin:ProjectCode>VUMR-XMP-042</agoin:ProjectCode>" +
                        "<agoin:Workflow>Formulario editable</agoin:Workflow>" +
                        "</rdf:Description></rdf:RDF></x:xmpmeta>" +
                        "<?xpacket end='w'?>");
                document.Open();
                document.Add(new Paragraph("Formulario de validación"));

                AddText(writer, "persona.nombre", 700, false, false);
                AddText(writer, "persona.notas", 640, true, false);
                AddText(writer, "solo.lectura", 580, false, true);

                var check = new RadioCheckField(
                    writer,
                    new Rectangle(72, 520, 92, 540),
                    "acepta",
                    "Aceptado")
                {
                    CheckType = RadioCheckField.TYPE_CHECK,
                    Checked = false,
                    BorderColor = BaseColor.DARK_GRAY,
                    BorderWidth = 1
                };
                writer.AddAnnotation(check.CheckField);

                var radio = PdfFormField.CreateRadioButton(writer, true);
                radio.FieldName = "tipo";
                radio.ValueAsName = "A";
                var radioA = new RadioCheckField(
                    writer,
                    new Rectangle(72, 470, 92, 490),
                    null,
                    "A")
                {
                    CheckType = RadioCheckField.TYPE_CIRCLE,
                    Checked = true
                };
                var radioB = new RadioCheckField(
                    writer,
                    new Rectangle(112, 470, 132, 490),
                    null,
                    "B")
                {
                    CheckType = RadioCheckField.TYPE_CIRCLE,
                    Checked = false
                };
                radio.AddKid(radioA.RadioField);
                radio.AddKid(radioB.RadioField);
                writer.AddAnnotation(radio);

                var combo = new TextField(
                    writer,
                    new Rectangle(72, 410, 250, 438),
                    "idioma")
                {
                    Choices = new[] { "Español", "English" },
                    ChoiceExports = new[] { "es", "en" },
                    ChoiceSelection = 1,
                    FontSize = 10,
                    BorderColor = BaseColor.GRAY,
                    BorderWidth = 1
                };
                writer.AddAnnotation(combo.GetComboField());

                var list = new TextField(
                    writer,
                    new Rectangle(72, 290, 250, 390),
                    "capitulos")
                {
                    Choices = new[] { "Uno", "Dos", "Tres" },
                    ChoiceExports = new[] { "uno", "dos", "tres" },
                    ChoiceSelections = new List<int> { 1 },
                    Options = TextField.MULTISELECT,
                    FontSize = 10,
                    BorderColor = BaseColor.GRAY,
                    BorderWidth = 1
                };
                writer.AddAnnotation(list.GetListField());

                var signature = PdfFormField.CreateSignature(writer);
                signature.FieldName = "firma.pendiente";
                signature.SetWidget(
                    new Rectangle(300, 500, 480, 550),
                    PdfAnnotation.HIGHLIGHT_INVERT);
                writer.AddAnnotation(signature);

                var push = PdfFormField.CreatePushButton(writer);
                push.FieldName = "accion";
                push.SetWidget(
                    new Rectangle(300, 440, 420, 470),
                    PdfAnnotation.HIGHLIGHT_PUSH);
                writer.AddAnnotation(push);

                document.Close();
            }
        }

        private static void AddText(
            PdfWriter writer,
            string name,
            float top,
            bool multiline,
            bool readOnly)
        {
            var field = new TextField(
                writer,
                new Rectangle(72, top - 38, 310, top),
                name)
            {
                Text = readOnly ? "Original" : string.Empty,
                FontSize = 10,
                BorderColor = BaseColor.GRAY,
                BorderWidth = 1,
                Options =
                    (multiline ? TextField.MULTILINE : 0) |
                    (readOnly ? TextField.READ_ONLY : 0)
            };
            writer.AddAnnotation(field.GetTextField());
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(
                    "Fallo QA: " + message);
            }
        }

        private static void AssertThrows(Action action, string message)
        {
            try
            {
                action();
            }
            catch
            {
                return;
            }

            throw new InvalidOperationException(
                "Fallo QA: " + message);
        }
    }
}

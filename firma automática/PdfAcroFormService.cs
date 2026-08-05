using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using iTextSharp.text.pdf;

namespace FirmaAutomatica
{
    internal enum PdfAcroFormFieldKind
    {
        Unsupported,
        Text,
        CheckBox,
        RadioButton,
        List,
        ComboBox,
        Signature,
        PushButton
    }

    internal sealed class PdfAcroFormOption
    {
        public PdfAcroFormOption(string exportValue, string displayValue)
        {
            ExportValue = exportValue ?? string.Empty;
            DisplayValue = string.IsNullOrWhiteSpace(displayValue)
                ? ExportValue
                : displayValue;
        }

        public string ExportValue { get; private set; }

        public string DisplayValue { get; private set; }
    }

    internal sealed class PdfAcroFormWidget
    {
        public PdfAcroFormWidget(
            int pageNumber,
            float left,
            float bottom,
            float right,
            float top,
            int annotationFlags)
        {
            PageNumber = pageNumber;
            Left = left;
            Bottom = bottom;
            Right = right;
            Top = top;
            AnnotationFlags = annotationFlags;
        }

        public int PageNumber { get; private set; }

        public float Left { get; private set; }

        public float Bottom { get; private set; }

        public float Right { get; private set; }

        public float Top { get; private set; }

        public int AnnotationFlags { get; private set; }
    }

    internal sealed class PdfAcroFormField
    {
        internal PdfAcroFormField(
            string name,
            string alternateName,
            PdfAcroFormFieldKind kind,
            int rawFlags,
            string value,
            string defaultValue,
            IList<string> selectedValues,
            IList<PdfAcroFormOption> options,
            IList<PdfAcroFormWidget> widgets,
            int maximumLength)
        {
            Name = name ?? string.Empty;
            AlternateName = alternateName ?? string.Empty;
            Kind = kind;
            RawFlags = rawFlags;
            Value = value ?? string.Empty;
            DefaultValue = defaultValue ?? string.Empty;
            SelectedValues = new List<string>(
                selectedValues ?? new string[0]).AsReadOnly();
            Options = new List<PdfAcroFormOption>(
                options ?? new PdfAcroFormOption[0]).AsReadOnly();
            Widgets = new List<PdfAcroFormWidget>(
                widgets ?? new PdfAcroFormWidget[0]).AsReadOnly();
            MaximumLength = Math.Max(0, maximumLength);

            IsReadOnly = (rawFlags & PdfFormField.FF_READ_ONLY) != 0 ||
                Widgets.Any(widget =>
                    (widget.AnnotationFlags &
                     (PdfAnnotation.FLAGS_READONLY |
                      PdfAnnotation.FLAGS_LOCKED |
                      PdfAnnotation.FLAGS_LOCKEDCONTENTS)) != 0);
            IsRequired = (rawFlags & PdfFormField.FF_REQUIRED) != 0;
            IsMultiLine = (rawFlags & PdfFormField.FF_MULTILINE) != 0;
            IsPassword = (rawFlags & PdfFormField.FF_PASSWORD) != 0;
            IsRichText = (rawFlags & PdfFormField.FF_RICHTEXT) != 0;
            IsFileSelect = (rawFlags & PdfFormField.FF_FILESELECT) != 0;
            AllowsCustomValue =
                (rawFlags & PdfFormField.FF_EDIT) != 0;
            AllowsMultipleSelection =
                (rawFlags & PdfFormField.FF_MULTISELECT) != 0;
            AllowsToggleOff =
                (rawFlags & PdfFormField.FF_NO_TOGGLE_TO_OFF) == 0;

            EditRestriction = ResolveEditRestriction();
            CanEdit = string.IsNullOrWhiteSpace(EditRestriction);
        }

        public string Name { get; private set; }

        public string AlternateName { get; private set; }

        public string DisplayName
        {
            get
            {
                return string.IsNullOrWhiteSpace(AlternateName)
                    ? Name
                    : AlternateName;
            }
        }

        public PdfAcroFormFieldKind Kind { get; private set; }

        public int RawFlags { get; private set; }

        public string Value { get; private set; }

        public string DefaultValue { get; private set; }

        public IList<string> SelectedValues { get; private set; }

        public IList<PdfAcroFormOption> Options { get; private set; }

        public IList<PdfAcroFormWidget> Widgets { get; private set; }

        public int MaximumLength { get; private set; }

        public bool IsReadOnly { get; private set; }

        public bool IsRequired { get; private set; }

        public bool IsMultiLine { get; private set; }

        public bool IsPassword { get; private set; }

        public bool IsRichText { get; private set; }

        public bool IsFileSelect { get; private set; }

        public bool AllowsCustomValue { get; private set; }

        public bool AllowsMultipleSelection { get; private set; }

        public bool AllowsToggleOff { get; private set; }

        public bool CanEdit { get; private set; }

        public string EditRestriction { get; private set; }

        private string ResolveEditRestriction()
        {
            if (IsReadOnly)
            {
                return "Campo de solo lectura o bloqueado.";
            }

            if (Kind == PdfAcroFormFieldKind.Signature)
            {
                return "Campo de firma. Utiliza la herramienta Firmar.";
            }

            if (Kind == PdfAcroFormFieldKind.PushButton)
            {
                return "Botón de acción; no contiene un valor rellenable.";
            }

            if (Kind == PdfAcroFormFieldKind.Unsupported)
            {
                return "Tipo de campo no compatible.";
            }

            if (IsRichText)
            {
                return "El texto enriquecido se conserva, pero no se edita " +
                    "hasta poder garantizar su apariencia.";
            }

            if (IsFileSelect)
            {
                return "Los campos de selección de archivo no se modifican.";
            }

            if ((Kind == PdfAcroFormFieldKind.CheckBox ||
                 Kind == PdfAcroFormFieldKind.RadioButton ||
                 Kind == PdfAcroFormFieldKind.List) &&
                Options.Count == 0)
            {
                return "El campo no declara opciones válidas.";
            }

            return string.Empty;
        }
    }

    internal sealed class PdfAcroFormDocument
    {
        internal PdfAcroFormDocument(
            string sourcePath,
            long sourceLength,
            long sourceLastWriteUtcTicks,
            string sourceFingerprint,
            int pageCount,
            IList<PdfAcroFormField> fields,
            int totalWidgetCount,
            bool containsJavaScriptOrCalculations,
            IList<string> pageContentTokens,
            IDictionary<string, string> metadata,
            string xmpHash)
        {
            SourcePath = sourcePath;
            SourceLength = sourceLength;
            SourceLastWriteUtcTicks = sourceLastWriteUtcTicks;
            SourceFingerprint = sourceFingerprint;
            PageCount = pageCount;
            Fields = new List<PdfAcroFormField>(fields).AsReadOnly();
            TotalWidgetCount = totalWidgetCount;
            ContainsJavaScriptOrCalculations =
                containsJavaScriptOrCalculations;
            PageContentTokens = new List<string>(
                pageContentTokens).AsReadOnly();
            Metadata = new Dictionary<string, string>(
                metadata,
                StringComparer.Ordinal);
            XmpHash = xmpHash ?? string.Empty;
        }

        public string SourcePath { get; private set; }

        public long SourceLength { get; private set; }

        public long SourceLastWriteUtcTicks { get; private set; }

        public string SourceFingerprint { get; private set; }

        public int PageCount { get; private set; }

        public IList<PdfAcroFormField> Fields { get; private set; }

        public int TotalWidgetCount { get; private set; }

        public bool ContainsJavaScriptOrCalculations { get; private set; }

        public int EditableFieldCount
        {
            get { return Fields.Count(field => field.CanEdit); }
        }

        internal IList<string> PageContentTokens { get; private set; }

        internal IDictionary<string, string> Metadata { get; private set; }

        // Semantic fingerprint of the XMP packet. It excludes only the three
        // technical properties that PdfStamper is expected to refresh.
        internal string XmpHash { get; private set; }
    }

    internal sealed class PdfAcroFormFieldChange
    {
        private PdfAcroFormFieldChange(
            string fieldName,
            string value,
            IEnumerable<string> values)
        {
            if (string.IsNullOrWhiteSpace(fieldName))
            {
                throw new ArgumentException(
                    "Se necesita el nombre exacto del campo.",
                    "fieldName");
            }

            FieldName = fieldName;
            Value = value ?? string.Empty;
            Values = new List<string>(
                values ?? new string[0]).AsReadOnly();
        }

        public string FieldName { get; private set; }

        public string Value { get; private set; }

        public IList<string> Values { get; private set; }

        public static PdfAcroFormFieldChange ForValue(
            string fieldName,
            string value)
        {
            return new PdfAcroFormFieldChange(
                fieldName,
                value,
                null);
        }

        public static PdfAcroFormFieldChange ForValues(
            string fieldName,
            IEnumerable<string> values)
        {
            return new PdfAcroFormFieldChange(
                fieldName,
                null,
                values);
        }
    }

    internal sealed class PdfAcroFormSaveResult
    {
        public PdfAcroFormSaveResult(
            string outputPath,
            int changedFieldCount,
            int fieldCount)
        {
            OutputPath = outputPath;
            ChangedFieldCount = changedFieldCount;
            FieldCount = fieldCount;
        }

        public string OutputPath { get; private set; }

        public int ChangedFieldCount { get; private set; }

        public int FieldCount { get; private set; }
    }

    /// <summary>
    /// Reads and fills classic AcroForm fields. Work is deliberately performed
    /// only when the feature is invoked; the normal PDF opening path does not
    /// touch iText or enumerate fields.
    /// </summary>
    internal static class PdfAcroFormService
    {
        public const string XfaUnsupportedMessage =
            "Los formularios XFA no se pueden rellenar de forma segura en " +
            "esta versión. Guarda antes una copia como PDF normal.";

        public const string EncryptedUnsupportedMessage =
            "Los formularios PDF protegidos con contraseña todavía no se " +
            "pueden guardar con Recovery de forma segura.";

        public const string SignedUnsupportedMessage =
            "Este PDF ya contiene una firma digital. Para no alterar sus " +
            "permisos DocMDP o FieldMDP, el rellenado se ha bloqueado en " +
            "esta primera versión.";

        public const string CertifiedUnsupportedMessage =
            "El PDF está certificado y restringe las modificaciones. El " +
            "rellenado se ha bloqueado para conservar su estado.";

        public const string UsageRightsUnsupportedMessage =
            "El PDF contiene derechos de uso ampliados de Adobe. iText puede " +
            "invalidarlos al modificarlo, por lo que esta versión no lo " +
            "rellena automáticamente.";

        public const string SourceChangedMessage =
            "El PDF cambió mientras se estaba rellenando. Vuelve a abrir " +
            "el editor para trabajar sobre la versión actual.";

        public static PdfAcroFormDocument Analyze(string sourcePdfPath)
        {
            var sourcePath = NormalizeExistingPdfPath(sourcePdfPath);
            using (OpenSourceReadGuard(sourcePath))
            {
                var info = new FileInfo(sourcePath);
                var fingerprint =
                    PdfAtomicFileService.ComputeFullContentHash(sourcePath);
                PdfReader reader = null;
                try
                {
                    reader = OpenPdfReader(sourcePath);
                    EnforceSafeEditingPolicy(reader);
                    return ReadDocument(
                        sourcePath,
                        info.Length,
                        info.LastWriteTimeUtc.Ticks,
                        fingerprint,
                        reader);
                }
                finally
                {
                    if (reader != null)
                    {
                        reader.Close();
                    }
                }
            }
        }

        public static PdfAcroFormSaveResult Apply(
            string sourcePdfPath,
            string outputPath,
            PdfAcroFormDocument document,
            IEnumerable<PdfAcroFormFieldChange> changes)
        {
            if (document == null)
            {
                throw new ArgumentNullException("document");
            }

            var sourcePath = NormalizeExistingPdfPath(sourcePdfPath);
            var normalizedOutputPath =
                ValidateOutputPath(sourcePath, outputPath);
            if (!string.Equals(
                    sourcePath,
                    document.SourcePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(SourceChangedMessage);
            }

            EnsureSourceQuickIdentity(sourcePath, document);
            var normalizedChanges = NormalizeChanges(document, changes);
            if (normalizedChanges.Count == 0)
            {
                throw new InvalidOperationException(
                    "No hay cambios de formulario que aplicar.");
            }

            var directory = Path.GetDirectoryName(normalizedOutputPath);
            var temporaryPath = Path.Combine(
                directory,
                "." + Path.GetFileNameWithoutExtension(normalizedOutputPath) +
                "." + Guid.NewGuid().ToString("N") + ".tmp");

            using (OpenSourceReadGuard(sourcePath))
            {
                EnsureSourceIdentity(sourcePath, document);
                try
                {
                    WriteIncrementalRevision(
                        sourcePath,
                        temporaryPath,
                        normalizedChanges);
                    ValidateWrittenRevision(
                        sourcePath,
                        temporaryPath,
                        document,
                        normalizedChanges);
                    // The source guard stays open with FileShare.Read, so no
                    // writer can change the already-hashed source here.
                    File.Move(temporaryPath, normalizedOutputPath);
                }
                finally
                {
                    TryDeleteFile(temporaryPath);
                }
            }

            return new PdfAcroFormSaveResult(
                normalizedOutputPath,
                normalizedChanges.Count,
                document.Fields.Count);
        }

        private static PdfAcroFormDocument ReadDocument(
            string sourcePath,
            long sourceLength,
            long sourceLastWriteUtcTicks,
            string sourceFingerprint,
            PdfReader reader)
        {
            var fields = reader.AcroFields;
            var result = new List<PdfAcroFormField>();
            if (fields != null)
            {
                foreach (var name in fields.Fields.Keys
                    .OrderBy(value => value, StringComparer.Ordinal))
                {
                    var item = fields.GetFieldItem(name);
                    if (item == null || item.Size < 1)
                    {
                        continue;
                    }

                    result.Add(ReadField(fields, name, item));
                }

                result = result
                    .Select(field => new
                    {
                        Field = field,
                        Widget = GetFirstPositionedWidget(field)
                    })
                    .OrderBy(entry => entry.Widget == null
                        ? int.MaxValue
                        : entry.Widget.PageNumber)
                    .ThenByDescending(entry => entry.Widget == null
                        ? float.MinValue
                        : entry.Widget.Top)
                    .ThenBy(entry => entry.Widget == null
                        ? float.MaxValue
                        : entry.Widget.Left)
                    .ThenBy(entry => entry.Field.Name, StringComparer.Ordinal)
                    .Select(entry => entry.Field)
                    .ToList();
            }

            var pageTokens = new List<string>();
            for (var page = 1; page <= reader.NumberOfPages; page++)
            {
                var pageDictionary = reader.GetPageN(page);
                pageTokens.Add(CanonicalizeObject(
                    pageDictionary == null
                        ? null
                        : pageDictionary.Get(PdfName.CONTENTS),
                    0));
            }

            return new PdfAcroFormDocument(
                sourcePath,
                sourceLength,
                sourceLastWriteUtcTicks,
                sourceFingerprint,
                reader.NumberOfPages,
                result,
                CountWidgets(reader),
                ContainsJavaScriptOrCalculations(reader, fields),
                pageTokens,
                CloneMetadata(reader.Info),
                ComputeXmpSemanticHash(reader.Metadata));
        }

        private static PdfAcroFormWidget GetFirstPositionedWidget(
            PdfAcroFormField field)
        {
            if (field == null || field.Widgets == null)
            {
                return null;
            }

            return field.Widgets
                .Where(widget => widget != null && widget.PageNumber > 0)
                .OrderBy(widget => widget.PageNumber)
                .ThenByDescending(widget => widget.Top)
                .ThenBy(widget => widget.Left)
                .FirstOrDefault();
        }

        private static PdfAcroFormField ReadField(
            AcroFields fields,
            string name,
            AcroFields.Item item)
        {
            var rawFlags = 0;
            var maximumLength = 0;
            var alternateName = string.Empty;
            var defaultValue = string.Empty;
            for (var index = 0; index < item.Size; index++)
            {
                var merged = item.GetMerged(index);
                if (merged == null)
                {
                    continue;
                }

                var flags = merged.GetAsNumber(PdfName.FF);
                if (flags != null)
                {
                    rawFlags |= flags.IntValue;
                }

                if (maximumLength == 0)
                {
                    var maxLength = merged.GetAsNumber(PdfName.MAXLEN);
                    if (maxLength != null)
                    {
                        maximumLength = Math.Max(0, maxLength.IntValue);
                    }
                }

                if (string.IsNullOrWhiteSpace(alternateName))
                {
                    alternateName = GetUnicodeString(
                        merged.GetAsString(PdfName.TU));
                }

                if (string.IsNullOrEmpty(defaultValue))
                {
                    defaultValue = GetUnicodeString(
                        merged.GetAsString(PdfName.DV));
                }
            }

            var kind = MapFieldKind(fields.GetFieldType(name));
            var options = ReadOptions(fields, name, kind);
            var selectedValues = ReadSelectedValues(
                fields,
                name,
                kind,
                options);
            var value = NormalizeSingleValue(
                kind,
                fields.GetField(name));

            return new PdfAcroFormField(
                name,
                alternateName,
                kind,
                rawFlags,
                value,
                defaultValue,
                selectedValues,
                options,
                ReadWidgets(fields, name, item),
                maximumLength);
        }

        private static IList<PdfAcroFormWidget> ReadWidgets(
            AcroFields fields,
            string name,
            AcroFields.Item item)
        {
            var result = new List<PdfAcroFormWidget>();
            var positions = fields.GetFieldPositions(name);
            for (var index = 0; index < item.Size; index++)
            {
                var page = item.GetPage(index);
                var annotationFlags = 0;
                var widget = item.GetWidget(index);
                if (widget != null)
                {
                    var flags = widget.GetAsNumber(PdfName.F);
                    if (flags != null)
                    {
                        annotationFlags = flags.IntValue;
                    }
                }

                iTextSharp.text.Rectangle rectangle = null;
                if (positions != null && index < positions.Count &&
                    positions[index] != null)
                {
                    page = positions[index].page;
                    rectangle = positions[index].position;
                }

                if (rectangle == null && widget != null)
                {
                    rectangle = ReadRectangle(widget.GetAsArray(PdfName.RECT));
                }

                if (rectangle == null)
                {
                    result.Add(new PdfAcroFormWidget(
                        page,
                        0,
                        0,
                        0,
                        0,
                        annotationFlags));
                }
                else
                {
                    result.Add(new PdfAcroFormWidget(
                        page,
                        rectangle.Left,
                        rectangle.Bottom,
                        rectangle.Right,
                        rectangle.Top,
                        annotationFlags));
                }
            }

            return result;
        }

        private static IList<PdfAcroFormOption> ReadOptions(
            AcroFields fields,
            string name,
            PdfAcroFormFieldKind kind)
        {
            var result = new List<PdfAcroFormOption>();
            if (kind == PdfAcroFormFieldKind.ComboBox ||
                kind == PdfAcroFormFieldKind.List)
            {
                var exports = fields.GetListOptionExport(name) ??
                    new string[0];
                var displays = fields.GetListOptionDisplay(name) ??
                    new string[0];
                var count = Math.Max(exports.Length, displays.Length);
                for (var index = 0; index < count; index++)
                {
                    var export = index < exports.Length
                        ? exports[index]
                        : displays[index];
                    var display = index < displays.Length
                        ? displays[index]
                        : export;
                    AddDistinctOption(result, export, display);
                }
            }
            else if (kind == PdfAcroFormFieldKind.CheckBox ||
                     kind == PdfAcroFormFieldKind.RadioButton)
            {
                var states = fields.GetAppearanceStates(name) ??
                    new string[0];
                foreach (var state in states)
                {
                    if (string.IsNullOrWhiteSpace(state) ||
                        string.Equals(
                            state,
                            "Off",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    AddDistinctOption(result, state, state);
                }
            }

            return result;
        }

        private static void AddDistinctOption(
            IList<PdfAcroFormOption> options,
            string exportValue,
            string displayValue)
        {
            var normalizedExport = exportValue ?? string.Empty;
            if (options.Any(option => string.Equals(
                    option.ExportValue,
                    normalizedExport,
                    StringComparison.Ordinal)))
            {
                return;
            }

            options.Add(new PdfAcroFormOption(
                normalizedExport,
                displayValue));
        }

        private static IList<string> ReadSelectedValues(
            AcroFields fields,
            string name,
            PdfAcroFormFieldKind kind,
            IList<PdfAcroFormOption> options)
        {
            if (kind != PdfAcroFormFieldKind.List)
            {
                var single = NormalizeSingleValue(
                    kind,
                    fields.GetField(name));
                return string.IsNullOrEmpty(single)
                    ? new string[0]
                    : new[] { single };
            }

            var selected = fields.GetListSelection(name) ??
                new string[0];
            return OrderChoiceValues(selected, options);
        }

        private static string NormalizeSingleValue(
            PdfAcroFormFieldKind kind,
            string value)
        {
            var normalized = value ?? string.Empty;
            if ((kind == PdfAcroFormFieldKind.CheckBox ||
                 kind == PdfAcroFormFieldKind.RadioButton) &&
                string.Equals(
                    normalized,
                    "Off",
                    StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return normalized;
        }

        private static PdfAcroFormFieldKind MapFieldKind(int fieldType)
        {
            switch (fieldType)
            {
                case AcroFields.FIELD_TYPE_TEXT:
                    return PdfAcroFormFieldKind.Text;
                case AcroFields.FIELD_TYPE_CHECKBOX:
                    return PdfAcroFormFieldKind.CheckBox;
                case AcroFields.FIELD_TYPE_RADIOBUTTON:
                    return PdfAcroFormFieldKind.RadioButton;
                case AcroFields.FIELD_TYPE_LIST:
                    return PdfAcroFormFieldKind.List;
                case AcroFields.FIELD_TYPE_COMBO:
                    return PdfAcroFormFieldKind.ComboBox;
                case AcroFields.FIELD_TYPE_SIGNATURE:
                    return PdfAcroFormFieldKind.Signature;
                case AcroFields.FIELD_TYPE_PUSHBUTTON:
                    return PdfAcroFormFieldKind.PushButton;
                default:
                    return PdfAcroFormFieldKind.Unsupported;
            }
        }

        private static List<NormalizedChange> NormalizeChanges(
            PdfAcroFormDocument document,
            IEnumerable<PdfAcroFormFieldChange> changes)
        {
            var fields = document.Fields.ToDictionary(
                field => field.Name,
                StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<NormalizedChange>();
            foreach (var change in changes ??
                Enumerable.Empty<PdfAcroFormFieldChange>())
            {
                if (change == null ||
                    !seen.Add(change.FieldName))
                {
                    if (change != null)
                    {
                        throw new InvalidOperationException(
                            "El campo \"" + change.FieldName +
                            "\" aparece más de una vez en los cambios.");
                    }

                    continue;
                }

                PdfAcroFormField field;
                if (!fields.TryGetValue(change.FieldName, out field))
                {
                    throw new InvalidOperationException(
                        "El campo \"" + change.FieldName +
                        "\" ya no existe.");
                }

                if (!field.CanEdit)
                {
                    throw new UnauthorizedAccessException(
                        "No se puede modificar \"" + field.DisplayName +
                        "\": " + field.EditRestriction);
                }

                var normalized = NormalizeChange(field, change);
                if (!normalized.IsSameAsSource)
                {
                    result.Add(normalized);
                }
            }

            return result;
        }

        private static NormalizedChange NormalizeChange(
            PdfAcroFormField field,
            PdfAcroFormFieldChange change)
        {
            if (field.Kind == PdfAcroFormFieldKind.List &&
                field.AllowsMultipleSelection)
            {
                var values = OrderChoiceValues(
                    change.Values,
                    field.Options);
                ValidateDeclaredOptions(field, values);
                return new NormalizedChange(
                    field,
                    string.Empty,
                    values,
                    SequenceEqual(
                        values,
                        OrderChoiceValues(
                            field.SelectedValues,
                            field.Options)));
            }

            var value = change.Value ?? string.Empty;
            if (field.Kind == PdfAcroFormFieldKind.CheckBox ||
                field.Kind == PdfAcroFormFieldKind.RadioButton)
            {
                value = NormalizeSingleValue(field.Kind, value);
                if (!string.IsNullOrEmpty(value))
                {
                    ValidateDeclaredOptions(field, new[] { value });
                }

                if (field.Kind == PdfAcroFormFieldKind.RadioButton &&
                    !field.AllowsToggleOff &&
                    !string.IsNullOrEmpty(field.Value) &&
                    string.IsNullOrEmpty(value))
                {
                    throw new InvalidOperationException(
                        "El grupo \"" + field.DisplayName +
                        "\" no permite quitar la selección.");
                }
            }
            else if (field.Kind == PdfAcroFormFieldKind.ComboBox &&
                !field.AllowsCustomValue)
            {
                ValidateDeclaredOptions(field, new[] { value });
            }
            else if (field.Kind == PdfAcroFormFieldKind.List)
            {
                if (!string.IsNullOrEmpty(value))
                {
                    ValidateDeclaredOptions(field, new[] { value });
                }
            }

            if (field.Kind == PdfAcroFormFieldKind.Text &&
                field.MaximumLength > 0 &&
                value.Length > field.MaximumLength)
            {
                throw new InvalidOperationException(
                    "El campo \"" + field.DisplayName +
                    "\" admite como máximo " +
                    field.MaximumLength.ToString(CultureInfo.InvariantCulture) +
                    " caracteres.");
            }

            return new NormalizedChange(
                field,
                value,
                new string[0],
                string.Equals(
                    value,
                    field.Value,
                    StringComparison.Ordinal));
        }

        private static void ValidateDeclaredOptions(
            PdfAcroFormField field,
            IEnumerable<string> values)
        {
            var allowed = new HashSet<string>(
                field.Options.Select(option => option.ExportValue),
                StringComparer.Ordinal);
            foreach (var value in values)
            {
                if (!allowed.Contains(value ?? string.Empty))
                {
                    throw new InvalidOperationException(
                        "El valor seleccionado para \"" +
                        field.DisplayName +
                        "\" no pertenece a sus opciones.");
                }
            }
        }

        private static IList<string> OrderChoiceValues(
            IEnumerable<string> values,
            IList<PdfAcroFormOption> options)
        {
            var requested = new HashSet<string>(
                (values ?? Enumerable.Empty<string>())
                    .Select(value => value ?? string.Empty),
                StringComparer.Ordinal);
            var result = new List<string>();
            foreach (var option in options)
            {
                if (requested.Remove(option.ExportValue))
                {
                    result.Add(option.ExportValue);
                }
            }

            foreach (var remaining in requested
                .OrderBy(value => value, StringComparer.Ordinal))
            {
                result.Add(remaining);
            }

            return result;
        }

        private static void WriteIncrementalRevision(
            string sourcePath,
            string temporaryPath,
            IList<NormalizedChange> changes)
        {
            PdfReader reader = null;
            PdfStamper stamper = null;
            FileStream output = null;
            try
            {
                reader = OpenPdfReader(sourcePath);
                EnforceSafeEditingPolicy(reader);
                var sourceXmpMetadata = reader.Metadata;
                output = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    1024 * 1024,
                    FileOptions.SequentialScan |
                    FileOptions.WriteThrough);
                stamper = new PdfStamper(
                    reader,
                    output,
                    '\0',
                    true);
                stamper.FormFlattening = false;
                stamper.FreeTextFlattening = false;
                stamper.AnnotationFlattening = false;
                stamper.MoreInfo = CloneMetadata(reader.Info);
                if (sourceXmpMetadata != null &&
                    sourceXmpMetadata.Length > 0)
                {
                    // Preserve the source packet and all custom/descriptive
                    // properties. PdfStamper can still refresh Producer,
                    // ModifyDate and MetadataDate; validation compares the
                    // remaining XMP graph strictly and prefix-independently.
                    stamper.XmpMetadata = sourceXmpMetadata;
                }

                var fields = stamper.AcroFields;
                fields.GenerateAppearances = true;
                AddUnicodeSubstitutionFontIfNeeded(fields, changes);
                foreach (var change in changes)
                {
                    bool changed;
                    if (change.Field.Kind ==
                            PdfAcroFormFieldKind.List &&
                        change.Field.AllowsMultipleSelection)
                    {
                        changed = fields.SetListSelection(
                            change.Field.Name,
                            change.Values.ToArray());
                        // iTextSharp 5.5.13.3 writes /I, the array /V and
                        // the normal appearance in SetListSelection itself.
                        // RegenerateField calls GetField, which cannot read
                        // an array /V and would erase a valid multi-selection.
                    }
                    else
                    {
                        var value = change.Value;
                        if ((change.Field.Kind ==
                                PdfAcroFormFieldKind.CheckBox ||
                             change.Field.Kind ==
                                PdfAcroFormFieldKind.RadioButton) &&
                            string.IsNullOrEmpty(value))
                        {
                            value = "Off";
                        }

                        changed = fields.SetField(
                            change.Field.Name,
                            value);
                    }

                    if (!changed)
                    {
                        throw new InvalidDataException(
                            "iText no pudo actualizar el campo \"" +
                            change.Field.DisplayName + "\".");
                    }
                }

                stamper.Close();
                stamper = null;
                output = null;
                reader = null;
            }
            finally
            {
                if (stamper != null)
                {
                    try
                    {
                        stamper.Close();
                    }
                    catch
                    {
                    }
                }
                else
                {
                    if (output != null)
                    {
                        output.Dispose();
                    }

                    if (reader != null)
                    {
                        reader.Close();
                    }
                }
            }
        }

        private static void ValidateWrittenRevision(
            string sourcePath,
            string outputPath,
            PdfAcroFormDocument sourceDocument,
            IList<NormalizedChange> changes)
        {
            if (!File.Exists(outputPath) ||
                new FileInfo(outputPath).Length <= sourceDocument.SourceLength)
            {
                throw new InvalidDataException(
                    "La revisión incremental no contiene una actualización " +
                    "PDF completa.");
            }

            if (!HasExactSourcePrefix(sourcePath, outputPath))
            {
                throw new InvalidDataException(
                    "La revisión no conserva intactos todos los bytes del PDF " +
                    "original.");
            }

            PdfReader reader = null;
            try
            {
                reader = OpenPdfReader(outputPath);
                if (HasXfa(reader))
                {
                    throw new InvalidDataException(
                        "La revisión contiene XFA inesperado.");
                }

                var outputInfo = new FileInfo(outputPath);
                var outputDocument = ReadDocument(
                    outputPath,
                    outputInfo.Length,
                    outputInfo.LastWriteTimeUtc.Ticks,
                    string.Empty,
                    reader);
                ValidateCanonicalStructure(
                    sourceDocument,
                    outputDocument);
                ValidateValues(
                    sourceDocument,
                    outputDocument,
                    changes);
                ValidateAppearances(reader.AcroFields, changes);
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidDataException(
                    "La copia rellenada no superó la comprobación: " +
                    ex.GetBaseException().Message,
                    ex);
            }
            finally
            {
                if (reader != null)
                {
                    reader.Close();
                }
            }
        }

        private static void ValidateCanonicalStructure(
            PdfAcroFormDocument expected,
            PdfAcroFormDocument actual)
        {
            if (expected.PageCount != actual.PageCount ||
                expected.TotalWidgetCount != actual.TotalWidgetCount ||
                expected.Fields.Count != actual.Fields.Count)
            {
                throw new InvalidDataException(
                    "La estructura de páginas o campos cambió al rellenar.");
            }

            if (!SequenceEqual(
                    expected.PageContentTokens,
                    actual.PageContentTokens))
            {
                throw new InvalidDataException(
                    "El contenido interno de alguna página cambió.");
            }

            if (!MetadataEqual(expected.Metadata, actual.Metadata))
            {
                throw new InvalidDataException(
                    "El diccionario de metadatos del documento cambió " +
                    "inesperadamente.");
            }

            if (!string.Equals(
                    expected.XmpHash,
                    actual.XmpHash,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Los metadatos XMP descriptivos o personalizados " +
                    "cambiaron inesperadamente.");
            }

            var actualByName = actual.Fields.ToDictionary(
                field => field.Name,
                StringComparer.Ordinal);
            foreach (var expectedField in expected.Fields)
            {
                PdfAcroFormField actualField;
                if (!actualByName.TryGetValue(
                        expectedField.Name,
                        out actualField) ||
                    expectedField.Kind != actualField.Kind ||
                    expectedField.RawFlags != actualField.RawFlags ||
                    expectedField.MaximumLength !=
                        actualField.MaximumLength ||
                    !string.Equals(
                        expectedField.DefaultValue,
                        actualField.DefaultValue,
                        StringComparison.Ordinal) ||
                    !OptionsEqual(
                        expectedField.Options,
                        actualField.Options) ||
                    !WidgetsEqual(
                        expectedField.Widgets,
                        actualField.Widgets))
                {
                    throw new InvalidDataException(
                        "Cambió la definición del campo \"" +
                        expectedField.Name + "\".");
                }
            }
        }

        private static void ValidateValues(
            PdfAcroFormDocument source,
            PdfAcroFormDocument output,
            IList<NormalizedChange> changes)
        {
            var changeByName = changes.ToDictionary(
                change => change.Field.Name,
                StringComparer.Ordinal);
            var outputByName = output.Fields.ToDictionary(
                field => field.Name,
                StringComparer.Ordinal);
            foreach (var sourceField in source.Fields)
            {
                var outputField = outputByName[sourceField.Name];
                NormalizedChange change;
                if (!changeByName.TryGetValue(sourceField.Name, out change))
                {
                    if (!string.Equals(
                            sourceField.Value,
                            outputField.Value,
                            StringComparison.Ordinal) ||
                        !SequenceEqual(
                            sourceField.SelectedValues,
                            outputField.SelectedValues))
                    {
                        throw new InvalidDataException(
                            "Cambió un campo no editado: \"" +
                            sourceField.Name + "\".");
                    }

                    continue;
                }

                if (sourceField.Kind == PdfAcroFormFieldKind.List &&
                    sourceField.AllowsMultipleSelection)
                {
                    if (!SequenceEqual(
                            change.Values,
                            outputField.SelectedValues))
                    {
                        throw new InvalidDataException(
                            "La lista \"" + sourceField.Name +
                            "\" no conserva la selección solicitada. " +
                            "Esperado=[" + string.Join(",", change.Values) +
                            "] Actual=[" + string.Join(",",
                                outputField.SelectedValues) + "].");
                    }
                }
                else if (!string.Equals(
                    change.Value,
                    outputField.Value,
                    StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "El campo \"" + sourceField.Name +
                        "\" no conserva el valor solicitado.");
                }
            }
        }

        private static void ValidateAppearances(
            AcroFields fields,
            IList<NormalizedChange> changes)
        {
            foreach (var change in changes)
            {
                var item = fields.GetFieldItem(change.Field.Name);
                if (item == null || item.Size != change.Field.Widgets.Count)
                {
                    throw new InvalidDataException(
                        "No se pueden comprobar las apariencias de \"" +
                        change.Field.Name + "\".");
                }

                var selectedButtonAppearanceFound = false;
                for (var index = 0; index < item.Size; index++)
                {
                    var widget = item.GetWidget(index);
                    var appearance = ResolveDictionary(
                        widget == null
                            ? null
                            : widget.Get(PdfName.AP));
                    var normal = appearance == null
                        ? null
                        : PdfReader.GetPdfObject(
                            appearance.Get(PdfName.N));
                    if (normal == null)
                    {
                        throw new InvalidDataException(
                            "El campo \"" + change.Field.Name +
                            "\" quedó sin apariencia normal.");
                    }

                    if (change.Field.Kind !=
                            PdfAcroFormFieldKind.CheckBox &&
                        change.Field.Kind !=
                            PdfAcroFormFieldKind.RadioButton)
                    {
                        if (!(normal is PdfStream))
                        {
                            throw new InvalidDataException(
                                "La apariencia de \"" +
                                change.Field.Name +
                                "\" no es un flujo visible.");
                        }

                        continue;
                    }

                    var normalStates = normal as PdfDictionary;
                    var appearanceState = widget == null
                        ? null
                        : widget.GetAsName(PdfName.AS);
                    if (normalStates == null ||
                        appearanceState == null ||
                        normalStates.Get(appearanceState) == null)
                    {
                        throw new InvalidDataException(
                            "Los estados visuales de \"" +
                            change.Field.Name +
                            "\" no son coherentes.");
                    }

                    var decodedState = PdfName.DecodeName(
                        appearanceState.ToString());
                    if (!string.IsNullOrEmpty(change.Value) &&
                        string.Equals(
                            decodedState,
                            change.Value,
                            StringComparison.Ordinal))
                    {
                        selectedButtonAppearanceFound = true;
                    }
                }

                if ((change.Field.Kind ==
                        PdfAcroFormFieldKind.CheckBox ||
                     change.Field.Kind ==
                        PdfAcroFormFieldKind.RadioButton) &&
                    !string.IsNullOrEmpty(change.Value) &&
                    !selectedButtonAppearanceFound)
                {
                    throw new InvalidDataException(
                        "El estado seleccionado de \"" +
                        change.Field.Name +
                        "\" no tiene una apariencia activa.");
                }
            }
        }

        private static void EnforceSafeEditingPolicy(PdfReader reader)
        {
            if (HasXfa(reader))
            {
                throw new NotSupportedException(XfaUnsupportedMessage);
            }

            if (reader.IsEncrypted() ||
                !reader.IsOpenedWithFullPermissions)
            {
                throw new UnauthorizedAccessException(
                    EncryptedUnsupportedMessage);
            }

            if (reader.GetCertificationLevel() !=
                PdfSignatureAppearance.NOT_CERTIFIED)
            {
                throw new UnauthorizedAccessException(
                    CertifiedUnsupportedMessage);
            }

            var fields = reader.AcroFields;
            var signatures = fields == null
                ? null
                : fields.GetSignatureNames();
            if (signatures != null && signatures.Count > 0)
            {
                throw new UnauthorizedAccessException(
                    SignedUnsupportedMessage);
            }

            if (reader.HasUsageRights())
            {
                throw new UnauthorizedAccessException(
                    UsageRightsUnsupportedMessage);
            }
        }

        private static void AddUnicodeSubstitutionFontIfNeeded(
            AcroFields fields,
            IEnumerable<NormalizedChange> changes)
        {
            var values = (changes ?? Enumerable.Empty<NormalizedChange>())
                .SelectMany(change =>
                    change.Field.Kind == PdfAcroFormFieldKind.List &&
                    change.Field.AllowsMultipleSelection
                        ? change.Values
                        : new[] { change.Value })
                .Where(value => !string.IsNullOrEmpty(value))
                .ToList();
            if (!values.Any(value => value.Any(character => character > 127)))
            {
                return;
            }

            var windowsDirectory = Environment.GetFolderPath(
                Environment.SpecialFolder.Windows);
            var fontDirectory = string.IsNullOrWhiteSpace(windowsDirectory)
                ? string.Empty
                : Path.Combine(windowsDirectory, "Fonts");
            var candidates = new[]
            {
                Path.Combine(fontDirectory, "segoeui.ttf"),
                Path.Combine(fontDirectory, "arial.ttf"),
                Path.Combine(fontDirectory, "calibri.ttf"),
                Path.Combine(fontDirectory, "arialuni.ttf"),
                Path.Combine(fontDirectory, "msgothic.ttc") + ",0",
                Path.Combine(fontDirectory, "YuGothR.ttc") + ",0",
                Path.Combine(fontDirectory, "meiryo.ttc") + ",0"
            };
            Exception lastError = null;
            foreach (var candidate in candidates
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(candidate) ||
                    !UnicodeFontFileExists(candidate))
                {
                    continue;
                }

                try
                {
                    var font = BaseFont.CreateFont(
                        candidate,
                        BaseFont.IDENTITY_H,
                        BaseFont.EMBEDDED);
                    if (!FontCoversAllValues(font, values))
                    {
                        continue;
                    }

                    fields.AddSubstitutionFont(font);
                    return;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }

            // Western European text is still representable by the standard
            // AcroForm fonts. For characters outside that range, failing is
            // safer than creating a valid value whose appearance is blank.
            if (values.Any(value => !CanEncodeWithWindows1252(value)))
            {
                throw new InvalidOperationException(
                    "No se encontró una fuente Unicode incrustable para " +
                    "representar uno de los valores del formulario.",
                    lastError);
            }
        }

        private static bool UnicodeFontFileExists(string candidate)
        {
            var marker = candidate.LastIndexOf(
                ".ttc,",
                StringComparison.OrdinalIgnoreCase);
            var filePath = marker < 0
                ? candidate
                : candidate.Substring(0, marker + 4);
            return File.Exists(filePath);
        }

        private static bool FontCoversAllValues(
            BaseFont font,
            IEnumerable<string> values)
        {
            if (font == null)
            {
                return false;
            }

            foreach (var value in values ?? Enumerable.Empty<string>())
            {
                for (var index = 0; index < value.Length; index++)
                {
                    var character = value[index];
                    if (char.IsControl(character))
                    {
                        continue;
                    }

                    int codePoint;
                    if (char.IsHighSurrogate(character))
                    {
                        if (index + 1 >= value.Length ||
                            !char.IsLowSurrogate(value[index + 1]))
                        {
                            return false;
                        }

                        codePoint = char.ConvertToUtf32(
                            character,
                            value[++index]);
                    }
                    else if (char.IsLowSurrogate(character))
                    {
                        return false;
                    }
                    else
                    {
                        codePoint = character;
                    }

                    if (!font.CharExists(codePoint))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool CanEncodeWithWindows1252(string value)
        {
            try
            {
                var encoding = Encoding.GetEncoding(
                    1252,
                    EncoderFallback.ExceptionFallback,
                    DecoderFallback.ExceptionFallback);
                var bytes = encoding.GetBytes(value ?? string.Empty);
                return string.Equals(
                    encoding.GetString(bytes),
                    value ?? string.Empty,
                    StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        private static bool HasXfa(PdfReader reader)
        {
            var catalog = reader == null ? null : reader.Catalog;
            var acroForm = ResolveDictionary(
                catalog == null
                    ? null
                    : catalog.Get(PdfName.ACROFORM));
            if (acroForm != null && acroForm.Get(PdfName.XFA) != null)
            {
                return true;
            }

            return reader != null &&
                reader.AcroFields != null &&
                reader.AcroFields.Xfa != null &&
                reader.AcroFields.Xfa.XfaPresent;
        }

        private static bool ContainsJavaScriptOrCalculations(
            PdfReader reader,
            AcroFields fields)
        {
            var catalog = reader.Catalog;
            var names = ResolveDictionary(
                catalog == null
                    ? null
                    : catalog.Get(PdfName.NAMES));
            if ((names != null &&
                 names.Get(PdfName.JAVASCRIPT) != null) ||
                (catalog != null &&
                 catalog.Get(PdfName.OPENACTION) != null))
            {
                return true;
            }

            var acroForm = ResolveDictionary(
                catalog == null
                    ? null
                    : catalog.Get(PdfName.ACROFORM));
            if (acroForm != null &&
                acroForm.Get(PdfName.CO) != null)
            {
                return true;
            }

            if (fields != null)
            {
                foreach (var pair in fields.Fields)
                {
                    var item = pair.Value;
                    for (var index = 0; index < item.Size; index++)
                    {
                        var merged = item.GetMerged(index);
                        if (merged != null &&
                            (merged.Get(PdfName.AA) != null ||
                             merged.Get(PdfName.A) != null))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static int CountWidgets(PdfReader reader)
        {
            var count = 0;
            for (var page = 1; page <= reader.NumberOfPages; page++)
            {
                var pageDictionary = reader.GetPageN(page);
                var annotations = pageDictionary == null
                    ? null
                    : pageDictionary.GetAsArray(PdfName.ANNOTS);
                if (annotations == null)
                {
                    continue;
                }

                for (var index = 0; index < annotations.Size; index++)
                {
                    var annotation = ResolveDictionary(
                        annotations.GetPdfObject(index));
                    if (annotation != null &&
                        PdfName.WIDGET.Equals(
                            annotation.GetAsName(PdfName.SUBTYPE)))
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private static string CanonicalizeObject(
            PdfObject value,
            int depth)
        {
            if (value == null)
            {
                return "null";
            }

            var reference = value as PRIndirectReference;
            if (reference != null)
            {
                return "R" +
                    reference.Number.ToString(CultureInfo.InvariantCulture) +
                    ":" +
                    reference.Generation.ToString(
                        CultureInfo.InvariantCulture);
            }

            if (depth > 12)
            {
                return "depth";
            }

            var array = value as PdfArray;
            if (array != null)
            {
                return "[" + string.Join(
                    ",",
                    Enumerable.Range(0, array.Size)
                        .Select(index => CanonicalizeObject(
                            array.GetPdfObject(index),
                            depth + 1))) + "]";
            }

            return value.ToString();
        }

        private static PdfDictionary ResolveDictionary(PdfObject value)
        {
            return PdfReader.GetPdfObject(value) as PdfDictionary;
        }

        private static iTextSharp.text.Rectangle ReadRectangle(
            PdfArray values)
        {
            if (values == null || values.Size < 4)
            {
                return null;
            }

            var left = values.GetAsNumber(0);
            var bottom = values.GetAsNumber(1);
            var right = values.GetAsNumber(2);
            var top = values.GetAsNumber(3);
            if (left == null || bottom == null ||
                right == null || top == null)
            {
                return null;
            }

            return new iTextSharp.text.Rectangle(
                left.FloatValue,
                bottom.FloatValue,
                right.FloatValue,
                top.FloatValue);
        }

        private static bool WidgetsEqual(
            IList<PdfAcroFormWidget> first,
            IList<PdfAcroFormWidget> second)
        {
            if (first.Count != second.Count)
            {
                return false;
            }

            for (var index = 0; index < first.Count; index++)
            {
                if (first[index].PageNumber != second[index].PageNumber ||
                    first[index].AnnotationFlags !=
                        second[index].AnnotationFlags ||
                    !AreClose(first[index].Left, second[index].Left) ||
                    !AreClose(first[index].Bottom, second[index].Bottom) ||
                    !AreClose(first[index].Right, second[index].Right) ||
                    !AreClose(first[index].Top, second[index].Top))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool OptionsEqual(
            IList<PdfAcroFormOption> first,
            IList<PdfAcroFormOption> second)
        {
            if (first.Count != second.Count)
            {
                return false;
            }

            for (var index = 0; index < first.Count; index++)
            {
                if (!string.Equals(
                        first[index].ExportValue,
                        second[index].ExportValue,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        first[index].DisplayValue,
                        second[index].DisplayValue,
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool MetadataEqual(
            IDictionary<string, string> first,
            IDictionary<string, string> second)
        {
            var firstComparable = first.Where(pair =>
                    !IsExpectedIncrementalMetadataKey(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value,
                    StringComparer.Ordinal);
            var secondComparable = second.Where(pair =>
                    !IsExpectedIncrementalMetadataKey(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value,
                    StringComparer.Ordinal);
            if (firstComparable.Count != secondComparable.Count)
            {
                return false;
            }

            foreach (var pair in firstComparable)
            {
                string value;
                if (!secondComparable.TryGetValue(pair.Key, out value) ||
                    !string.Equals(
                        pair.Value,
                        value,
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsExpectedIncrementalMetadataKey(string key)
        {
            // PdfStamper records the incremental writer in Producer and can
            // refresh ModDate. User-authored/descriptive metadata must remain
            // exact, and XMP is checked byte for byte separately.
            return string.Equals(
                    key,
                    "ModDate",
                    StringComparison.Ordinal) ||
                string.Equals(
                    key,
                    "Producer",
                    StringComparison.Ordinal);
        }

        private static bool SequenceEqual<T>(
            IEnumerable<T> first,
            IEnumerable<T> second)
        {
            return Enumerable.SequenceEqual(
                first ?? Enumerable.Empty<T>(),
                second ?? Enumerable.Empty<T>());
        }

        private static bool AreClose(float first, float second)
        {
            return Math.Abs(first - second) <= 0.02f;
        }

        private static bool HasExactSourcePrefix(
            string sourcePath,
            string outputPath)
        {
            var bufferA = new byte[1024 * 1024];
            var bufferB = new byte[bufferA.Length];
            using (var source = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            using (var output = new FileStream(
                outputPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            {
                if (output.Length < source.Length)
                {
                    return false;
                }

                long remaining = source.Length;
                while (remaining > 0)
                {
                    var requested = (int)Math.Min(
                        bufferA.Length,
                        remaining);
                    if (ReadExactly(source, bufferA, requested) != requested ||
                        ReadExactly(output, bufferB, requested) != requested)
                    {
                        return false;
                    }

                    for (var index = 0; index < requested; index++)
                    {
                        if (bufferA[index] != bufferB[index])
                        {
                            return false;
                        }
                    }

                    remaining -= requested;
                }
            }

            return true;
        }

        private static int ReadExactly(
            Stream stream,
            byte[] buffer,
            int count)
        {
            var total = 0;
            while (total < count)
            {
                var read = stream.Read(buffer, total, count - total);
                if (read <= 0)
                {
                    break;
                }

                total += read;
            }

            return total;
        }

        private static string NormalizeExistingPdfPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException(
                    "Se necesita un PDF de origen.",
                    "path");
            }

            var normalized = Path.GetFullPath(path);
            if (!File.Exists(normalized))
            {
                throw new FileNotFoundException(
                    "No se encuentra el PDF de origen.",
                    normalized);
            }

            if (!string.Equals(
                    Path.GetExtension(normalized),
                    ".pdf",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "El archivo de origen debe ser PDF.",
                    "path");
            }

            return normalized;
        }

        private static string ValidateOutputPath(
            string sourcePath,
            string outputPath)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new ArgumentException(
                    "Se necesita una ruta para la revisión.",
                    "outputPath");
            }

            var normalized = Path.GetFullPath(outputPath);
            if (string.Equals(
                    sourcePath,
                    normalized,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "El original no se puede sobrescribir.");
            }

            if (!string.Equals(
                    Path.GetExtension(normalized),
                    ".pdf",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "La revisión debe tener extensión PDF.",
                    "outputPath");
            }

            var directory = Path.GetDirectoryName(normalized);
            if (string.IsNullOrWhiteSpace(directory) ||
                !Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException(
                    "La carpeta de la revisión no existe.");
            }

            if (File.Exists(normalized))
            {
                throw new IOException(
                    "La ruta reservada para la revisión ya existe.");
            }

            return normalized;
        }

        private static PdfReader OpenPdfReader(string path)
        {
            try
            {
                return new PdfReader(path);
            }
            catch (iTextSharp.text.exceptions.BadPasswordException ex)
            {
                throw new UnauthorizedAccessException(
                    EncryptedUnsupportedMessage,
                    ex);
            }
        }

        private static IDisposable OpenSourceReadGuard(string sourcePath)
        {
            return new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.RandomAccess);
        }

        private static void EnsureSourceQuickIdentity(
            string sourcePath,
            PdfAcroFormDocument document)
        {
            var info = new FileInfo(sourcePath);
            if (info.Length != document.SourceLength ||
                info.LastWriteTimeUtc.Ticks !=
                    document.SourceLastWriteUtcTicks)
            {
                throw new InvalidOperationException(SourceChangedMessage);
            }
        }

        private static void EnsureSourceIdentity(
            string sourcePath,
            PdfAcroFormDocument document)
        {
            EnsureSourceQuickIdentity(sourcePath, document);
            if (!string.Equals(
                    PdfAtomicFileService.ComputeFullContentHash(sourcePath),
                    document.SourceFingerprint,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(SourceChangedMessage);
            }
        }

        private static IDictionary<string, string> CloneMetadata(
            IDictionary<string, string> metadata)
        {
            return new Dictionary<string, string>(
                metadata ?? new Dictionary<string, string>(),
                StringComparer.Ordinal);
        }

        private static string ComputeXmpSemanticHash(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return string.Empty;
            }

            try
            {
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    IgnoreComments = true,
                    IgnoreProcessingInstructions = true,
                    IgnoreWhitespace = true
                };
                var document = new XmlDocument
                {
                    PreserveWhitespace = false,
                    XmlResolver = null
                };
                using (var stream = new MemoryStream(bytes, false))
                using (var reader = XmlReader.Create(stream, settings))
                {
                    document.Load(reader);
                }

                var rdfNodes = document.GetElementsByTagName(
                    "RDF",
                    "http://www.w3.org/1999/02/22-rdf-syntax-ns#");
                if (rdfNodes.Count == 0)
                {
                    return "raw:" + ComputeBytesHash(bytes);
                }

                // XMP semantics live in the RDF graph. Wrapper details such
                // as x:xmptk, prefixes, packet padding and formatting are not
                // metadata properties and are intentionally excluded.
                var canonical = CanonicalizeXmpNode(rdfNodes[0]);
                return "xml:" + ComputeBytesHash(
                    new UTF8Encoding(false).GetBytes(canonical));
            }
            catch (XmlException)
            {
                // Malformed XMP cannot be compared semantically. Falling
                // back to exact bytes is deliberately conservative.
                return "raw:" + ComputeBytesHash(bytes);
            }
        }

        private static string CanonicalizeXmpNode(XmlNode node)
        {
            if (node == null || IsIgnoredTechnicalXmpProperty(node))
            {
                return string.Empty;
            }

            if (node.NodeType == XmlNodeType.Text ||
                node.NodeType == XmlNodeType.CDATA ||
                node.NodeType == XmlNodeType.SignificantWhitespace)
            {
                var text = new StringBuilder("T");
                AppendXmpScalar(text, node.Value ?? string.Empty);
                return text.ToString();
            }

            if (node.NodeType != XmlNodeType.Element)
            {
                return string.Empty;
            }

            if (string.Equals(
                    node.NamespaceURI,
                    "http://www.w3.org/1999/02/22-rdf-syntax-ns#",
                    StringComparison.Ordinal) &&
                string.Equals(
                    node.LocalName,
                    "Description",
                    StringComparison.Ordinal))
            {
                return CanonicalizeRdfDescription(node);
            }

            var builder = new StringBuilder("E");
            AppendXmpScalar(builder, node.NamespaceURI ?? string.Empty);
            AppendXmpScalar(builder, node.LocalName ?? string.Empty);

            var attributes = new List<string>();
            if (node.Attributes != null)
            {
                foreach (XmlAttribute attribute in node.Attributes)
                {
                    if (attribute.NamespaceURI ==
                            "http://www.w3.org/2000/xmlns/" ||
                        IsIgnoredTechnicalXmpProperty(attribute))
                    {
                        continue;
                    }

                    var attributeToken = new StringBuilder("A");
                    AppendXmpScalar(
                        attributeToken,
                        attribute.NamespaceURI ?? string.Empty);
                    AppendXmpScalar(
                        attributeToken,
                        attribute.LocalName ?? string.Empty);
                    AppendXmpScalar(
                        attributeToken,
                        attribute.Value ?? string.Empty);
                    attributes.Add(attributeToken.ToString());
                }
            }

            attributes.Sort(StringComparer.Ordinal);
            foreach (var attribute in attributes)
            {
                AppendXmpScalar(builder, attribute);
            }

            var children = new List<string>();
            foreach (XmlNode child in node.ChildNodes)
            {
                var childToken = CanonicalizeXmpNode(child);
                if (!string.IsNullOrEmpty(childToken))
                {
                    children.Add(childToken);
                }
            }

            if (HasUnorderedXmpChildren(node))
            {
                children.Sort(StringComparer.Ordinal);
            }

            foreach (var child in children)
            {
                AppendXmpScalar(builder, child);
            }

            return builder.ToString();
        }

        private static string CanonicalizeRdfDescription(XmlNode node)
        {
            var builder = new StringBuilder("D");
            AppendXmpScalar(builder, node.NamespaceURI ?? string.Empty);
            AppendXmpScalar(builder, node.LocalName ?? string.Empty);

            var structuralAttributes = new List<string>();
            var properties = new List<string>();
            if (node.Attributes != null)
            {
                foreach (XmlAttribute attribute in node.Attributes)
                {
                    if (attribute.NamespaceURI ==
                            "http://www.w3.org/2000/xmlns/" ||
                        IsIgnoredTechnicalXmpProperty(attribute))
                    {
                        continue;
                    }

                    if (IsRdfStructuralAttribute(attribute))
                    {
                        structuralAttributes.Add(
                            CanonicalizeXmpAttribute(attribute));
                    }
                    else
                    {
                        properties.Add(CanonicalizeSimpleXmpProperty(
                            attribute.NamespaceURI,
                            attribute.LocalName,
                            attribute.Value));
                    }
                }
            }

            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element ||
                    IsIgnoredTechnicalXmpProperty(child))
                {
                    continue;
                }

                properties.Add(IsSimpleXmpPropertyElement(child)
                    ? CanonicalizeSimpleXmpProperty(
                        child.NamespaceURI,
                        child.LocalName,
                        child.InnerText)
                    : "P" + CanonicalizeXmpNode(child));
            }

            structuralAttributes.Sort(StringComparer.Ordinal);
            properties.Sort(StringComparer.Ordinal);
            foreach (var attribute in structuralAttributes)
            {
                AppendXmpScalar(builder, attribute);
            }

            foreach (var property in properties)
            {
                AppendXmpScalar(builder, property);
            }

            return builder.ToString();
        }

        private static string CanonicalizeXmpAttribute(
            XmlAttribute attribute)
        {
            var builder = new StringBuilder("A");
            AppendXmpScalar(
                builder,
                attribute.NamespaceURI ?? string.Empty);
            AppendXmpScalar(
                builder,
                attribute.LocalName ?? string.Empty);
            AppendXmpScalar(
                builder,
                attribute.Value ?? string.Empty);
            return builder.ToString();
        }

        private static string CanonicalizeSimpleXmpProperty(
            string namespaceUri,
            string localName,
            string value)
        {
            var builder = new StringBuilder("S");
            AppendXmpScalar(builder, namespaceUri ?? string.Empty);
            AppendXmpScalar(builder, localName ?? string.Empty);
            AppendXmpScalar(builder, value ?? string.Empty);
            return builder.ToString();
        }

        private static bool IsRdfStructuralAttribute(XmlAttribute attribute)
        {
            return attribute != null &&
                (string.Equals(
                        attribute.NamespaceURI,
                        "http://www.w3.org/1999/02/22-rdf-syntax-ns#",
                        StringComparison.Ordinal) ||
                 string.Equals(
                        attribute.NamespaceURI,
                        "http://www.w3.org/XML/1998/namespace",
                        StringComparison.Ordinal));
        }

        private static bool IsSimpleXmpPropertyElement(XmlNode node)
        {
            if (node == null || node.NodeType != XmlNodeType.Element)
            {
                return false;
            }

            if (node.Attributes != null)
            {
                foreach (XmlAttribute attribute in node.Attributes)
                {
                    if (attribute.NamespaceURI !=
                        "http://www.w3.org/2000/xmlns/")
                    {
                        return false;
                    }
                }
            }

            return !node.ChildNodes.Cast<XmlNode>().Any(child =>
                child.NodeType == XmlNodeType.Element);
        }

        private static void AppendXmpScalar(
            StringBuilder builder,
            string value)
        {
            var normalized = value ?? string.Empty;
            builder.Append(normalized.Length.ToString(
                CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(normalized);
        }

        private static bool IsIgnoredTechnicalXmpProperty(XmlNode node)
        {
            if (node == null)
            {
                return false;
            }

            if (string.Equals(
                    node.NamespaceURI,
                    "http://ns.adobe.com/pdf/1.3/",
                    StringComparison.Ordinal) &&
                string.Equals(
                    node.LocalName,
                    "Producer",
                    StringComparison.Ordinal))
            {
                return true;
            }

            return string.Equals(
                    node.NamespaceURI,
                    "http://ns.adobe.com/xap/1.0/",
                    StringComparison.Ordinal) &&
                (string.Equals(
                        node.LocalName,
                        "ModifyDate",
                        StringComparison.Ordinal) ||
                 string.Equals(
                        node.LocalName,
                        "MetadataDate",
                        StringComparison.Ordinal));
        }

        private static bool HasUnorderedXmpChildren(XmlNode node)
        {
            if (node == null || !string.Equals(
                    node.NamespaceURI,
                    "http://www.w3.org/1999/02/22-rdf-syntax-ns#",
                    StringComparison.Ordinal))
            {
                return false;
            }

            return string.Equals(
                    node.LocalName,
                    "RDF",
                    StringComparison.Ordinal) ||
                string.Equals(
                    node.LocalName,
                    "Description",
                    StringComparison.Ordinal) ||
                string.Equals(
                    node.LocalName,
                    "Bag",
                    StringComparison.Ordinal) ||
                string.Equals(
                    node.LocalName,
                    "Alt",
                    StringComparison.Ordinal);
        }

        private static string ComputeBytesHash(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return string.Empty;
            }

            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(bytes))
                    .Replace("-", string.Empty);
            }
        }

        private static string GetUnicodeString(PdfString value)
        {
            return value == null
                ? string.Empty
                : value.ToUnicodeString();
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        private sealed class NormalizedChange
        {
            public NormalizedChange(
                PdfAcroFormField field,
                string value,
                IEnumerable<string> values,
                bool isSameAsSource)
            {
                Field = field;
                Value = value ?? string.Empty;
                Values = new List<string>(
                    values ?? new string[0]).AsReadOnly();
                IsSameAsSource = isSameAsSource;
            }

            public PdfAcroFormField Field { get; private set; }

            public string Value { get; private set; }

            public IList<string> Values { get; private set; }

            public bool IsSameAsSource { get; private set; }
        }
    }
}

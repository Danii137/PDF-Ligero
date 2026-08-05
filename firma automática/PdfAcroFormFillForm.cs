using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace FirmaAutomatica
{
    /// <summary>
    /// Lightweight AcroForm editor. Only one WinForms editor is materialized at
    /// a time, so forms with hundreds or thousands of fields do not create a
    /// large control tree. The PDF is not touched until the caller applies the
    /// returned changes through PdfAcroFormService.
    /// </summary>
    internal sealed class PdfAcroFormFillForm : Form
    {
        private static readonly Color WindowColor =
            Color.FromArgb(247, 246, 243);
        private static readonly Color PanelColor =
            Color.FromArgb(252, 251, 249);
        private static readonly Color TitleColor =
            Color.FromArgb(35, 36, 35);
        private static readonly Color BodyColor =
            Color.FromArgb(82, 83, 80);
        private static readonly Color MutedColor =
            Color.FromArgb(129, 128, 123);
        private static readonly Color DividerColor =
            Color.FromArgb(218, 215, 208);
        private static readonly Color AccentColor =
            Color.FromArgb(238, 91, 61);
        private static readonly Color AccentTintColor =
            Color.FromArgb(255, 239, 234);
        private static readonly Color WarningColor =
            Color.FromArgb(139, 91, 28);
        private static readonly Color WarningBackgroundColor =
            Color.FromArgb(255, 247, 225);

        private readonly PdfAcroFormDocument document;
        private readonly Dictionary<string, DraftValue> drafts =
            new Dictionary<string, DraftValue>(StringComparer.Ordinal);
        private readonly ListBox fieldList;
        private readonly TextBox searchTextBox;
        private readonly Label fieldTitleLabel;
        private readonly Label fieldNameLabel;
        private readonly Label fieldMetaLabel;
        private readonly Label restrictionLabel;
        private readonly Panel editorControlHost;
        private readonly Button resetButton;
        private readonly Button clearButton;
        private readonly Button applyButton;
        private readonly Label footerStatusLabel;
        private PdfAcroFormField activeField;
        private Control activeEditor;
        private bool rebuildingList;
        private IList<PdfAcroFormFieldChange> acceptedChanges =
            new List<PdfAcroFormFieldChange>().AsReadOnly();

        public PdfAcroFormFillForm(PdfAcroFormDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException("document");
            }

            this.document = document;
            Text = "Rellenar formulario · PDF Ligero";
            AppBranding.ApplyWindowIcon(this);
            Width = 820;
            Height = 620;
            MinimumSize = new Size(650, 470);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = WindowColor;
            Font = CreateUiFont(9.25f, FontStyle.Regular);
            KeyPreview = true;

            var header = new Panel
            {
                Dock = DockStyle.Top,
                Width = ClientSize.Width,
                Height = document.ContainsJavaScriptOrCalculations
                    ? 122
                    : 91,
                BackColor = PanelColor,
                Padding = new Padding(20, 15, 20, 10)
            };
            var accentLine = new Panel
            {
                Left = 20,
                Top = 15,
                Width = 42,
                Height = 2,
                BackColor = AccentColor
            };
            var eyebrow = new Label
            {
                Left = 20,
                Top = 23,
                Width = 550,
                Height = 17,
                Text = "FORMULARIO ACROFORM / CAMPOS INTERACTIVOS",
                Font = CreateArchitecturalFont(7.75f, FontStyle.Bold),
                ForeColor = AccentColor
            };
            var title = new Label
            {
                Left = 20,
                Top = 42,
                Width = 570,
                Height = 29,
                Text = "Rellenar sin aplanar el PDF",
                Font = CreateArchitecturalFont(17f, FontStyle.Regular),
                ForeColor = TitleColor
            };
            var summary = new Label
            {
                AutoSize = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Left = 590,
                Top = 28,
                Width = 195,
                Height = 42,
                TextAlign = ContentAlignment.MiddleRight,
                Font = CreateUiFont(8.75f, FontStyle.Regular),
                ForeColor = MutedColor,
                Text = BuildDocumentSummary()
            };
            header.Controls.Add(accentLine);
            header.Controls.Add(eyebrow);
            header.Controls.Add(title);
            header.Controls.Add(summary);

            if (document.ContainsJavaScriptOrCalculations)
            {
                var warning = new Label
                {
                    Left = 20,
                    Top = 81,
                    Width = 765,
                    Height = 28,
                    Anchor = AnchorStyles.Top |
                        AnchorStyles.Left |
                        AnchorStyles.Right,
                    BackColor = WarningBackgroundColor,
                    ForeColor = WarningColor,
                    Padding = new Padding(8, 5, 8, 4),
                    Text = "Este formulario usa acciones o cálculos. PDF Ligero " +
                        "conserva sus scripts, pero no los ejecuta: revisa los " +
                        "campos calculados después de guardar."
                };
                header.Controls.Add(warning);
            }

            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Width = ClientSize.Width,
                Height = 62,
                BackColor = PanelColor,
                Padding = new Padding(20, 13, 20, 12)
            };
            footer.Controls.Add(new Panel
            {
                Dock = DockStyle.Top,
                Height = 1,
                BackColor = DividerColor
            });

            var cancelButton = new Button
            {
                Width = 92,
                Height = 32,
                Text = "Cancelar",
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Left = 594,
                Top = 16,
                DialogResult = DialogResult.Cancel
            };
            StyleButton(cancelButton, false);
            applyButton = new Button
            {
                Width = 108,
                Height = 32,
                Text = "Aplicar",
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Left = 692,
                Top = 16,
                Enabled = document.EditableFieldCount > 0
            };
            StyleButton(applyButton, true);
            applyButton.Click += ApplyButton_Click;

            footerStatusLabel = new Label
            {
                Left = 20,
                Top = 21,
                Width = 545,
                Height = 23,
                Anchor = AnchorStyles.Top |
                    AnchorStyles.Left |
                    AnchorStyles.Right,
                ForeColor = MutedColor,
                AutoEllipsis = true,
                Text = "Los cambios crearán una revisión recuperable; " +
                    "el original no se sobrescribe."
            };
            footer.Controls.Add(footerStatusLabel);
            footer.Controls.Add(cancelButton);
            footer.Controls.Add(applyButton);

            var body = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = WindowColor,
                Padding = new Padding(20, 14, 20, 14)
            };
            var leftPanel = new Panel
            {
                Dock = DockStyle.Left,
                Width = 292,
                BackColor = PanelColor,
                Padding = new Padding(10)
            };
            var searchLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 20,
                Text = "BUSCAR CAMPO",
                Font = CreateArchitecturalFont(7.5f, FontStyle.Bold),
                ForeColor = MutedColor
            };
            searchTextBox = new TextBox
            {
                Dock = DockStyle.Top,
                Height = 26,
                BorderStyle = BorderStyle.FixedSingle,
                Font = CreateUiFont(9.25f, FontStyle.Regular),
                AccessibleName = "Buscar campos del formulario",
                AccessibleDescription =
                    "Filtra la lista por el nombre visible o interno del campo."
            };
            searchTextBox.TextChanged += delegate { RebuildFieldList(); };
            fieldList = new ListBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = PanelColor,
                ForeColor = TitleColor,
                Font = CreateUiFont(9.25f, FontStyle.Regular),
                IntegralHeight = false,
                ItemHeight = 24,
                AccessibleName = "Campos del formulario",
                AccessibleDescription =
                    "Lista de campos interactivos disponibles en el PDF."
            };
            fieldList.SelectedIndexChanged += FieldList_SelectedIndexChanged;
            var searchSpacer = new Panel
            {
                Dock = DockStyle.Top,
                Height = 9,
                BackColor = PanelColor
            };
            leftPanel.Controls.Add(fieldList);
            leftPanel.Controls.Add(searchSpacer);
            leftPanel.Controls.Add(searchTextBox);
            leftPanel.Controls.Add(searchLabel);

            var divider = new Panel
            {
                Dock = DockStyle.Left,
                Width = 1,
                BackColor = DividerColor
            };

            var editorPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = WindowColor,
                Padding = new Padding(22, 4, 4, 4)
            };
            fieldTitleLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 31,
                Font = CreateArchitecturalFont(13.5f, FontStyle.Regular),
                ForeColor = TitleColor,
                AutoEllipsis = true
            };
            fieldNameLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 31,
                Font = CreateUiFont(8.25f, FontStyle.Regular),
                ForeColor = MutedColor,
                AutoEllipsis = true
            };
            fieldMetaLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 25,
                Font = CreateUiFont(8.75f, FontStyle.Regular),
                ForeColor = BodyColor,
                AutoEllipsis = true
            };
            restrictionLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 45,
                Font = CreateUiFont(8.75f, FontStyle.Regular),
                ForeColor = WarningColor,
                BackColor = WarningBackgroundColor,
                Padding = new Padding(8, 6, 8, 4),
                Visible = false
            };

            var fieldActions = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 43,
                BackColor = WindowColor
            };
            resetButton = new Button
            {
                Left = 0,
                Top = 7,
                Width = 118,
                Height = 29,
                Text = "Restablecer"
            };
            StyleButton(resetButton, false);
            resetButton.Click += ResetButton_Click;
            clearButton = new Button
            {
                Left = 126,
                Top = 7,
                Width = 82,
                Height = 29,
                Text = "Vaciar"
            };
            StyleButton(clearButton, false);
            clearButton.Click += ClearButton_Click;
            fieldActions.Controls.Add(resetButton);
            fieldActions.Controls.Add(clearButton);

            editorControlHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = WindowColor,
                Padding = new Padding(0, 14, 0, 6)
            };
            editorPanel.Controls.Add(editorControlHost);
            editorPanel.Controls.Add(fieldActions);
            editorPanel.Controls.Add(restrictionLabel);
            editorPanel.Controls.Add(fieldMetaLabel);
            editorPanel.Controls.Add(fieldNameLabel);
            editorPanel.Controls.Add(fieldTitleLabel);

            body.Controls.Add(editorPanel);
            body.Controls.Add(divider);
            body.Controls.Add(leftPanel);

            Controls.Add(body);
            Controls.Add(footer);
            Controls.Add(header);
            AcceptButton = applyButton;
            CancelButton = cancelButton;
            KeyDown += PdfAcroFormFillForm_KeyDown;
            Shown += delegate
            {
                RebuildFieldList();
                if (fieldList.Items.Count == 0)
                {
                    searchTextBox.Focus();
                }
            };
        }

        public bool HasChanges
        {
            get
            {
                if (acceptedChanges.Count > 0)
                {
                    return true;
                }

                CommitActiveEditor();
                return BuildPendingChanges().Count > 0;
            }
        }

        public int ChangedFieldCount
        {
            get { return acceptedChanges.Count; }
        }

        public IList<PdfAcroFormFieldChange> Changes
        {
            get
            {
                return new List<PdfAcroFormFieldChange>(
                    acceptedChanges).AsReadOnly();
            }
        }

        public IList<PdfAcroFormFieldChange> BuildChanges()
        {
            CommitActiveEditor();
            return BuildPendingChanges().AsReadOnly();
        }

        private void RebuildFieldList()
        {
            if (rebuildingList)
            {
                return;
            }

            CommitActiveEditor();
            rebuildingList = true;
            try
            {
                var selectedName = activeField == null
                    ? string.Empty
                    : activeField.Name;
                var query = (searchTextBox.Text ?? string.Empty).Trim();
                var filtered = document.Fields.Where(field =>
                    string.IsNullOrEmpty(query) ||
                    field.DisplayName.IndexOf(
                        query,
                        StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                    field.Name.IndexOf(
                        query,
                        StringComparison.OrdinalIgnoreCase) >= 0).ToList();

                fieldList.BeginUpdate();
                fieldList.Items.Clear();
                foreach (var field in filtered)
                {
                    fieldList.Items.Add(new FieldListEntry(field));
                }
                fieldList.EndUpdate();

                var selectedIndex = -1;
                for (var index = 0; index < fieldList.Items.Count; index++)
                {
                    var entry = fieldList.Items[index] as FieldListEntry;
                    if (entry != null && string.Equals(
                            entry.Field.Name,
                            selectedName,
                            StringComparison.Ordinal))
                    {
                        selectedIndex = index;
                        break;
                    }
                }

                if (selectedIndex < 0 && fieldList.Items.Count > 0)
                {
                    selectedIndex = 0;
                }

                fieldList.SelectedIndex = selectedIndex;
                if (selectedIndex < 0)
                {
                    ShowNoField(
                        document.Fields.Count == 0
                            ? "Este PDF no contiene campos AcroForm."
                            : "No hay campos que coincidan con la búsqueda.");
                }
                else
                {
                    var selectedEntry =
                        fieldList.Items[selectedIndex] as FieldListEntry;
                    if (selectedEntry != null)
                    {
                        ShowField(selectedEntry.Field);
                    }
                }
            }
            finally
            {
                rebuildingList = false;
            }
        }

        private void FieldList_SelectedIndexChanged(
            object sender,
            EventArgs e)
        {
            if (rebuildingList)
            {
                return;
            }

            CommitActiveEditor();
            var entry = fieldList.SelectedItem as FieldListEntry;
            if (entry == null)
            {
                ShowNoField("Selecciona un campo para ver sus detalles.");
                return;
            }

            ShowField(entry.Field);
        }

        private void ShowField(PdfAcroFormField field)
        {
            activeField = field;
            fieldTitleLabel.Text = field.DisplayName;
            fieldNameLabel.Text = string.Equals(
                    field.DisplayName,
                    field.Name,
                    StringComparison.Ordinal)
                ? field.Name
                : "Nombre PDF: " + field.Name;
            fieldMetaLabel.Text = BuildFieldMetadata(field);
            restrictionLabel.Visible = !field.CanEdit;
            restrictionLabel.Text = field.CanEdit
                ? string.Empty
                : field.EditRestriction;
            resetButton.Enabled = field.CanEdit;
            clearButton.Enabled = CanClear(field);

            DisposeActiveEditor();
            activeEditor = CreateEditor(field);
            if (activeEditor != null)
            {
                editorControlHost.Controls.Add(activeEditor);
                activeEditor.BringToFront();
            }
        }

        private Control CreateEditor(PdfAcroFormField field)
        {
            if (!field.CanEdit)
            {
                return CreateInformationalValue(field);
            }

            var draft = GetDraft(field);
            switch (field.Kind)
            {
                case PdfAcroFormFieldKind.Text:
                    return CreateTextEditor(field, draft);
                case PdfAcroFormFieldKind.CheckBox:
                    return CreateCheckBoxEditor(field, draft);
                case PdfAcroFormFieldKind.RadioButton:
                    return CreateChoiceCombo(field, draft, true);
                case PdfAcroFormFieldKind.ComboBox:
                    return CreateChoiceCombo(field, draft, false);
                case PdfAcroFormFieldKind.List:
                    return field.AllowsMultipleSelection
                        ? CreateMultipleList(field, draft)
                        : CreateSingleList(field, draft);
                default:
                    return CreateInformationalValue(field);
            }
        }

        private Control CreateTextEditor(
            PdfAcroFormField field,
            DraftValue draft)
        {
            var textBox = new TextBox
            {
                Dock = field.IsMultiLine
                    ? DockStyle.Fill
                    : DockStyle.Top,
                Height = field.IsMultiLine ? 150 : 29,
                Multiline = field.IsMultiLine,
                AcceptsReturn = field.IsMultiLine,
                ScrollBars = field.IsMultiLine
                    ? ScrollBars.Vertical
                    : ScrollBars.None,
                BorderStyle = BorderStyle.FixedSingle,
                Font = CreateUiFont(10f, FontStyle.Regular),
                Text = draft.Value,
                UseSystemPasswordChar = field.IsPassword
            };
            if (field.MaximumLength > 0)
            {
                textBox.MaxLength = field.MaximumLength;
            }

            textBox.AccessibleName = field.DisplayName;
            return textBox;
        }

        private Control CreateCheckBoxEditor(
            PdfAcroFormField field,
            DraftValue draft)
        {
            if (field.Options.Count == 1)
            {
                var option = field.Options[0];
                return new CheckBox
                {
                    Dock = DockStyle.Top,
                    Height = 34,
                    Text = option.DisplayValue,
                    Checked = string.Equals(
                        draft.Value,
                        option.ExportValue,
                        StringComparison.Ordinal),
                    Tag = option,
                    Font = CreateUiFont(9.75f, FontStyle.Regular),
                    ForeColor = TitleColor,
                    AccessibleName = field.DisplayName
                };
            }

            return CreateChoiceCombo(field, draft, true);
        }

        private Control CreateChoiceCombo(
            PdfAcroFormField field,
            DraftValue draft,
            bool includeEmpty)
        {
            var combo = new ComboBox
            {
                Dock = DockStyle.Top,
                Height = 29,
                Font = CreateUiFont(9.75f, FontStyle.Regular),
                DropDownStyle = field.Kind ==
                        PdfAcroFormFieldKind.ComboBox &&
                    field.AllowsCustomValue
                        ? ComboBoxStyle.DropDown
                        : ComboBoxStyle.DropDownList,
                AccessibleName = field.DisplayName
            };

            if (includeEmpty ||
                field.Kind == PdfAcroFormFieldKind.ComboBox)
            {
                combo.Items.Add(new ChoiceItem(
                    string.Empty,
                    "— Sin seleccionar —"));
            }

            foreach (var option in field.Options)
            {
                combo.Items.Add(new ChoiceItem(
                    option.ExportValue,
                    option.DisplayValue));
            }

            var matched = false;
            for (var index = 0; index < combo.Items.Count; index++)
            {
                var item = combo.Items[index] as ChoiceItem;
                if (item != null && string.Equals(
                        item.ExportValue,
                        draft.Value,
                        StringComparison.Ordinal))
                {
                    combo.SelectedIndex = index;
                    matched = true;
                    break;
                }
            }

            if (!matched && combo.DropDownStyle == ComboBoxStyle.DropDown)
            {
                combo.Text = draft.Value;
            }
            else if (!matched && combo.Items.Count > 0)
            {
                combo.SelectedIndex = 0;
            }

            return combo;
        }

        private Control CreateSingleList(
            PdfAcroFormField field,
            DraftValue draft)
        {
            var list = new ListBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                Font = CreateUiFont(9.5f, FontStyle.Regular),
                IntegralHeight = false,
                AccessibleName = field.DisplayName
            };
            foreach (var option in field.Options)
            {
                list.Items.Add(new ChoiceItem(
                    option.ExportValue,
                    option.DisplayValue));
            }

            for (var index = 0; index < list.Items.Count; index++)
            {
                var item = list.Items[index] as ChoiceItem;
                if (item != null && string.Equals(
                        item.ExportValue,
                        draft.Value,
                        StringComparison.Ordinal))
                {
                    list.SelectedIndex = index;
                    break;
                }
            }

            return list;
        }

        private Control CreateMultipleList(
            PdfAcroFormField field,
            DraftValue draft)
        {
            var list = new CheckedListBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                Font = CreateUiFont(9.5f, FontStyle.Regular),
                CheckOnClick = true,
                IntegralHeight = false,
                AccessibleName = field.DisplayName
            };
            var selected = new HashSet<string>(
                draft.Values,
                StringComparer.Ordinal);
            foreach (var option in field.Options)
            {
                var index = list.Items.Add(new ChoiceItem(
                    option.ExportValue,
                    option.DisplayValue));
                if (selected.Contains(option.ExportValue))
                {
                    list.SetItemChecked(index, true);
                }
            }

            return list;
        }

        private Control CreateInformationalValue(PdfAcroFormField field)
        {
            var value = field.IsPassword &&
                !string.IsNullOrEmpty(field.Value)
                    ? "••••••••"
                    : field.Value;
            if (field.Kind == PdfAcroFormFieldKind.Signature)
            {
                value = string.IsNullOrWhiteSpace(field.Value)
                    ? "Campo de firma vacío"
                    : "Campo de firma completado";
            }
            else if (field.Kind == PdfAcroFormFieldKind.PushButton)
            {
                value = "Botón de acción";
            }
            else if (string.IsNullOrWhiteSpace(value))
            {
                value = "Sin valor";
            }

            return new Label
            {
                Dock = DockStyle.Top,
                Height = 62,
                Padding = new Padding(10, 10, 10, 8),
                BackColor = PanelColor,
                ForeColor = BodyColor,
                Font = CreateUiFont(9.5f, FontStyle.Regular),
                Text = value,
                AutoEllipsis = true
            };
        }

        private void CommitActiveEditor()
        {
            if (activeField == null ||
                activeEditor == null ||
                !activeField.CanEdit)
            {
                return;
            }

            var draft = GetDraft(activeField);
            var textBox = activeEditor as TextBox;
            if (textBox != null)
            {
                draft.Value = textBox.Text ?? string.Empty;
                return;
            }

            var checkBox = activeEditor as CheckBox;
            if (checkBox != null)
            {
                var option = checkBox.Tag as PdfAcroFormOption;
                draft.Value = checkBox.Checked && option != null
                    ? option.ExportValue
                    : string.Empty;
                return;
            }

            var combo = activeEditor as ComboBox;
            if (combo != null)
            {
                var selected = combo.SelectedItem as ChoiceItem;
                draft.Value = selected == null
                    ? combo.Text ?? string.Empty
                    : selected.ExportValue;
                return;
            }

            var checkedList = activeEditor as CheckedListBox;
            if (checkedList != null)
            {
                draft.Values.Clear();
                foreach (var selectedObject in checkedList.CheckedItems)
                {
                    var selected = selectedObject as ChoiceItem;
                    if (selected != null)
                    {
                        draft.Values.Add(selected.ExportValue);
                    }
                }

                return;
            }

            var list = activeEditor as ListBox;
            if (list != null)
            {
                var selected = list.SelectedItem as ChoiceItem;
                draft.Value = selected == null
                    ? string.Empty
                    : selected.ExportValue;
            }
        }

        private DraftValue GetDraft(PdfAcroFormField field)
        {
            DraftValue value;
            if (!drafts.TryGetValue(field.Name, out value))
            {
                value = new DraftValue(
                    field.Value,
                    field.SelectedValues);
                drafts.Add(field.Name, value);
            }

            return value;
        }

        private void ResetButton_Click(object sender, EventArgs e)
        {
            if (activeField == null || !activeField.CanEdit)
            {
                return;
            }

            drafts[activeField.Name] = new DraftValue(
                activeField.Value,
                activeField.SelectedValues);
            ShowField(activeField);
            UpdateDraftStatus();
        }

        private void ClearButton_Click(object sender, EventArgs e)
        {
            if (activeField == null || !CanClear(activeField))
            {
                return;
            }

            drafts[activeField.Name] = new DraftValue(
                string.Empty,
                new string[0]);
            ShowField(activeField);
            UpdateDraftStatus();
        }

        private static bool CanClear(PdfAcroFormField field)
        {
            return field != null &&
                field.CanEdit &&
                (field.Kind != PdfAcroFormFieldKind.RadioButton ||
                 field.AllowsToggleOff ||
                 string.IsNullOrEmpty(field.Value));
        }

        private void ApplyButton_Click(object sender, EventArgs e)
        {
            CommitActiveEditor();
            var changes = BuildPendingChanges();
            if (changes.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "No has cambiado ningún campo.",
                    "Rellenar formulario",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            var emptyRequired = document.Fields
                .Where(field => field.CanEdit && field.IsRequired)
                .Where(field => IsEffectiveValueEmpty(field))
                .Select(field => field.DisplayName)
                .Take(4)
                .ToList();
            if (emptyRequired.Count > 0)
            {
                var answer = MessageBox.Show(
                    this,
                    "Quedan campos obligatorios vacíos:\r\n\r\n" +
                    string.Join("\r\n", emptyRequired.Select(
                        name => "• " + name)) +
                    (document.Fields.Count(field =>
                        field.CanEdit &&
                        field.IsRequired &&
                        IsEffectiveValueEmpty(field)) >
                        emptyRequired.Count
                            ? "\r\n• …"
                            : string.Empty) +
                    "\r\n\r\n¿Quieres aplicar igualmente esta revisión parcial?",
                    "Campos obligatorios",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);
                if (answer != DialogResult.Yes)
                {
                    return;
                }
            }

            acceptedChanges = changes.AsReadOnly();
            DialogResult = DialogResult.OK;
            Close();
        }

        private List<PdfAcroFormFieldChange> BuildPendingChanges()
        {
            var result = new List<PdfAcroFormFieldChange>();
            foreach (var field in document.Fields.Where(
                candidate => candidate.CanEdit))
            {
                DraftValue draft;
                if (!drafts.TryGetValue(field.Name, out draft))
                {
                    continue;
                }

                if (field.Kind == PdfAcroFormFieldKind.List &&
                    field.AllowsMultipleSelection)
                {
                    if (!Enumerable.SequenceEqual(
                            field.SelectedValues,
                            OrderByOptions(field, draft.Values)))
                    {
                        result.Add(PdfAcroFormFieldChange.ForValues(
                            field.Name,
                            draft.Values));
                    }
                }
                else if (!string.Equals(
                    field.Value,
                    draft.Value,
                    StringComparison.Ordinal))
                {
                    result.Add(PdfAcroFormFieldChange.ForValue(
                        field.Name,
                        draft.Value));
                }
            }

            return result;
        }

        private bool IsEffectiveValueEmpty(PdfAcroFormField field)
        {
            DraftValue draft;
            if (!drafts.TryGetValue(field.Name, out draft))
            {
                return field.Kind == PdfAcroFormFieldKind.List &&
                    field.AllowsMultipleSelection
                        ? field.SelectedValues.Count == 0
                        : string.IsNullOrWhiteSpace(field.Value);
            }

            return field.Kind == PdfAcroFormFieldKind.List &&
                field.AllowsMultipleSelection
                    ? draft.Values.Count == 0
                    : string.IsNullOrWhiteSpace(draft.Value);
        }

        private static IList<string> OrderByOptions(
            PdfAcroFormField field,
            IEnumerable<string> values)
        {
            var selected = new HashSet<string>(
                values ?? new string[0],
                StringComparer.Ordinal);
            var result = new List<string>();
            foreach (var option in field.Options)
            {
                if (selected.Contains(option.ExportValue))
                {
                    result.Add(option.ExportValue);
                }
            }

            return result;
        }

        private void UpdateDraftStatus()
        {
            CommitActiveEditor();
            var count = BuildPendingChanges().Count;
            footerStatusLabel.Text = count == 0
                ? "Los cambios crearán una revisión recuperable; " +
                  "el original no se sobrescribe."
                : count.ToString(CultureInfo.CurrentCulture) +
                  (count == 1
                    ? " campo modificado · pendiente de aplicar"
                    : " campos modificados · pendientes de aplicar");
        }

        private void ShowNoField(string message)
        {
            activeField = null;
            DisposeActiveEditor();
            fieldTitleLabel.Text = "Formulario";
            fieldNameLabel.Text = string.Empty;
            fieldMetaLabel.Text = string.Empty;
            restrictionLabel.Visible = false;
            resetButton.Enabled = false;
            clearButton.Enabled = false;
            activeEditor = new Label
            {
                Dock = DockStyle.Top,
                Height = 70,
                Padding = new Padding(10),
                BackColor = PanelColor,
                ForeColor = BodyColor,
                Text = message
            };
            editorControlHost.Controls.Add(activeEditor);
        }

        private void DisposeActiveEditor()
        {
            if (activeEditor == null)
            {
                return;
            }

            editorControlHost.Controls.Remove(activeEditor);
            activeEditor.Dispose();
            activeEditor = null;
        }

        private string BuildDocumentSummary()
        {
            return document.Fields.Count.ToString(
                    CultureInfo.CurrentCulture) +
                (document.Fields.Count == 1 ? " campo" : " campos") +
                "\r\n" +
                document.EditableFieldCount.ToString(
                    CultureInfo.CurrentCulture) +
                " editables · " +
                document.PageCount.ToString(CultureInfo.CurrentCulture) +
                (document.PageCount == 1 ? " página" : " páginas");
        }

        private static string BuildFieldMetadata(PdfAcroFormField field)
        {
            var parts = new List<string>
            {
                GetKindLabel(field)
            };
            var pages = field.Widgets
                .Select(widget => widget.PageNumber)
                .Where(page => page > 0)
                .Distinct()
                .OrderBy(page => page)
                .ToList();
            if (pages.Count > 0)
            {
                parts.Add(
                    pages.Count == 1
                        ? "Página " + pages[0].ToString(
                            CultureInfo.CurrentCulture)
                        : "Páginas " + string.Join(
                            ", ",
                            pages.Select(page => page.ToString(
                                CultureInfo.CurrentCulture))));
            }

            if (field.IsRequired)
            {
                parts.Add("Obligatorio");
            }
            if (field.IsMultiLine)
            {
                parts.Add("Multilínea");
            }
            if (field.MaximumLength > 0)
            {
                parts.Add("Máx. " + field.MaximumLength.ToString(
                    CultureInfo.CurrentCulture));
            }
            if (field.Widgets.Count > 1)
            {
                parts.Add(field.Widgets.Count.ToString(
                    CultureInfo.CurrentCulture) + " ubicaciones");
            }

            return string.Join("  ·  ", parts);
        }

        private static string GetKindLabel(PdfAcroFormField field)
        {
            switch (field.Kind)
            {
                case PdfAcroFormFieldKind.Text:
                    return field.IsPassword ? "Texto protegido" : "Texto";
                case PdfAcroFormFieldKind.CheckBox:
                    return "Casilla";
                case PdfAcroFormFieldKind.RadioButton:
                    return "Opciones";
                case PdfAcroFormFieldKind.ComboBox:
                    return "Desplegable";
                case PdfAcroFormFieldKind.List:
                    return field.AllowsMultipleSelection
                        ? "Lista múltiple"
                        : "Lista";
                case PdfAcroFormFieldKind.Signature:
                    return "Firma";
                case PdfAcroFormFieldKind.PushButton:
                    return "Botón";
                default:
                    return "Campo no compatible";
            }
        }

        private void PdfAcroFormFillForm_KeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.F)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                searchTextBox.Focus();
                searchTextBox.SelectAll();
                return;
            }

            if (e.Control && e.KeyCode == Keys.Enter &&
                applyButton.Enabled)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                ApplyButton_Click(sender, EventArgs.Empty);
            }
        }

        private static void StyleButton(Button button, bool primary)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = primary
                ? AccentColor
                : DividerColor;
            button.BackColor = primary ? AccentColor : PanelColor;
            button.ForeColor = primary ? Color.White : TitleColor;
            button.Font = CreateUiFont(9f, FontStyle.Regular);
            button.Cursor = Cursors.Hand;
        }

        private static Font CreateUiFont(
            float size,
            FontStyle style)
        {
            return new Font(
                "Segoe UI",
                size,
                style,
                GraphicsUnit.Point);
        }

        private static Font CreateArchitecturalFont(
            float size,
            FontStyle style)
        {
            try
            {
                return new Font(
                    "Bahnschrift Light",
                    size,
                    style,
                    GraphicsUnit.Point);
            }
            catch
            {
                return new Font(
                    "Segoe UI Light",
                    size,
                    style,
                    GraphicsUnit.Point);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeActiveEditor();
            }

            base.Dispose(disposing);
        }

        private sealed class DraftValue
        {
            public DraftValue(
                string value,
                IEnumerable<string> values)
            {
                Value = value ?? string.Empty;
                Values = new List<string>(values ?? new string[0]);
            }

            public string Value;

            public List<string> Values;
        }

        private sealed class ChoiceItem
        {
            public ChoiceItem(string exportValue, string displayValue)
            {
                ExportValue = exportValue ?? string.Empty;
                DisplayValue = displayValue ?? string.Empty;
            }

            public string ExportValue { get; private set; }

            public string DisplayValue { get; private set; }

            public override string ToString()
            {
                return DisplayValue;
            }
        }

        private sealed class FieldListEntry
        {
            public FieldListEntry(PdfAcroFormField field)
            {
                Field = field;
            }

            public PdfAcroFormField Field { get; private set; }

            public override string ToString()
            {
                var prefix = Field.CanEdit
                    ? (Field.IsRequired ? "●  " : "○  ")
                    : "—  ";
                return prefix + Field.DisplayName +
                    "  ·  " + GetKindLabel(Field);
            }
        }
    }
}

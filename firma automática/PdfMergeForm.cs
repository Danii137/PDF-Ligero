using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace FirmaAutomatica
{
    internal sealed class PdfMergeForm : Form
    {
        private const int MaximumFiles = 50;
        private readonly ListView filesListView;
        private readonly Button addButton;
        private readonly Button removeButton;
        private readonly Button moveUpButton;
        private readonly Button moveDownButton;
        private readonly Button combineButton;
        private readonly Button cancelButton;
        private readonly Label totalsLabel;
        private readonly Label statusLabel;
        private readonly ProgressBar progressBar;
        private readonly CheckBox openResultCheckBox;
        private readonly BackgroundWorker mergeWorker;
        private bool mergeInProgress;

        public PdfMergeForm(IEnumerable<string> initialPaths)
        {
            Text = "Combinar PDFs";
            AppBranding.ApplyWindowIcon(this);
            Width = 820;
            Height = 570;
            MinimumSize = new Size(720, 500);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(247, 247, 245);
            AllowDrop = true;

            var titleLabel = new Label
            {
                Left = 20,
                Top = 17,
                Width = 520,
                Height = 25,
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                Text = "Combinar PDFs"
            };

            var helpLabel = new Label
            {
                Left = 21,
                Top = 46,
                Width = 740,
                Height = 37,
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.FromArgb(76, 76, 76),
                Text = "Ordena los documentos como quieres que aparezcan. Tambien puedes arrastrar PDFs a esta ventana o arrastrar una fila para recolocarla."
            };

            filesListView = new ListView
            {
                Left = 20,
                Top = 88,
                Width = 650,
                Height = 300,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = true,
                AllowDrop = true,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9F)
            };
            filesListView.Columns.Add("PDF", 310);
            filesListView.Columns.Add("Paginas", 70, HorizontalAlignment.Right);
            filesListView.Columns.Add("Carpeta", 245);
            filesListView.SelectedIndexChanged += FilesListView_SelectedIndexChanged;
            filesListView.ItemDrag += FilesListView_ItemDrag;
            filesListView.DragEnter += FilesListView_DragEnter;
            filesListView.DragOver += FilesListView_DragOver;
            filesListView.DragDrop += FilesListView_DragDrop;
            filesListView.KeyDown += FilesListView_KeyDown;

            addButton = CreateSideButton("Agregar...", 682, 88);
            addButton.Click += AddButton_Click;

            removeButton = CreateSideButton("Quitar", 682, 126);
            removeButton.Click += RemoveButton_Click;

            moveUpButton = CreateSideButton("Subir", 682, 184);
            moveUpButton.Click += delegate { MoveSelectedItem(-1); };

            moveDownButton = CreateSideButton("Bajar", 682, 222);
            moveDownButton.Click += delegate { MoveSelectedItem(1); };

            totalsLabel = new Label
            {
                Left = 21,
                Top = 398,
                Width = 450,
                Height = 22,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(64, 64, 64)
            };

            var signatureWarningLabel = new Label
            {
                Left = 21,
                Top = 424,
                Width = 750,
                Height = 35,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(102, 82, 42),
                Text = "Los originales no se modifican. El combinado es un PDF nuevo: una firma digital previa puede seguir viendose, pero deja de ser una firma valida del archivo resultante."
            };

            progressBar = new ProgressBar
            {
                Left = 20,
                Top = 461,
                Width = 484,
                Height = 8,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Visible = false,
                Style = ProgressBarStyle.Continuous
            };

            statusLabel = new Label
            {
                Left = 21,
                Top = 474,
                Width = 483,
                Height = 30,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(80, 80, 80),
                AutoEllipsis = true
            };

            openResultCheckBox = new CheckBox
            {
                Left = 520,
                Top = 460,
                Width = 255,
                Height = 22,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Font = new Font("Segoe UI", 9F),
                Text = "Abrir el resultado al terminar",
                Checked = true
            };

            cancelButton = new Button
            {
                Left = 576,
                Top = 489,
                Width = 96,
                Height = 32,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Text = "Cancelar",
                DialogResult = DialogResult.Cancel,
                Font = new Font("Segoe UI", 9F)
            };

            combineButton = new Button
            {
                Left = 680,
                Top = 489,
                Width = 112,
                Height = 32,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Text = "Combinar",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                BackColor = Color.FromArgb(179, 33, 42),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            combineButton.FlatAppearance.BorderSize = 0;
            combineButton.Click += CombineButton_Click;

            Controls.Add(titleLabel);
            Controls.Add(helpLabel);
            Controls.Add(filesListView);
            Controls.Add(addButton);
            Controls.Add(removeButton);
            Controls.Add(moveUpButton);
            Controls.Add(moveDownButton);
            Controls.Add(totalsLabel);
            Controls.Add(signatureWarningLabel);
            Controls.Add(progressBar);
            Controls.Add(statusLabel);
            Controls.Add(openResultCheckBox);
            Controls.Add(cancelButton);
            Controls.Add(combineButton);

            AcceptButton = combineButton;
            CancelButton = cancelButton;

            DragEnter += Form_DragEnter;
            DragDrop += Form_DragDrop;
            FormClosing += PdfMergeForm_FormClosing;
            Resize += PdfMergeForm_Resize;

            mergeWorker = new BackgroundWorker
            {
                WorkerReportsProgress = true
            };
            mergeWorker.DoWork += MergeWorker_DoWork;
            mergeWorker.ProgressChanged += MergeWorker_ProgressChanged;
            mergeWorker.RunWorkerCompleted += MergeWorker_RunWorkerCompleted;

            AddPaths(initialPaths, true);
            UpdateListState();
        }

        private Button CreateSideButton(string text, int left, int top)
        {
            var button = new Button
            {
                Left = left,
                Top = top,
                Width = 110,
                Height = 30,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Text = text,
                Font = new Font("Segoe UI", 9F)
            };
            Controls.Add(button);
            return button;
        }

        private void AddButton_Click(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "Agregar PDFs";
                dialog.Filter = "Archivos PDF (*.pdf)|*.pdf";
                dialog.Multiselect = true;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    AddPaths(dialog.FileNames, true);
                }
            }
        }

        private void RemoveButton_Click(object sender, EventArgs e)
        {
            RemoveSelectedItems();
        }

        private void CombineButton_Click(object sender, EventArgs e)
        {
            if (filesListView.Items.Count < 2)
            {
                MessageBox.Show(
                    this,
                    "Añade al menos dos PDFs para combinarlos.",
                    "Combinar PDFs",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            var sources = GetOrderedEntries();
            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = "Guardar PDF combinado";
                dialog.Filter = "Archivo PDF (*.pdf)|*.pdf";
                dialog.DefaultExt = "pdf";
                dialog.AddExtension = true;
                dialog.OverwritePrompt = true;
                dialog.FileName = BuildDefaultOutputName(sources[0].Path);
                dialog.InitialDirectory = Path.GetDirectoryName(sources[0].Path);

                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                var outputPath = Path.GetFullPath(dialog.FileName);
                if (sources.Any(
                    entry => string.Equals(
                        Path.GetFullPath(entry.Path),
                        outputPath,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show(
                        this,
                        "El resultado no puede sobrescribir uno de los PDFs de origen. Elige otro nombre.",
                        "Combinar PDFs",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                StartMerge(sources, outputPath);
            }
        }

        private void StartMerge(IList<PdfMergeEntry> entries, string outputPath)
        {
            mergeInProgress = true;
            SetEditingEnabled(false);
            progressBar.Minimum = 0;
            progressBar.Maximum = 100;
            progressBar.Value = 0;
            progressBar.Visible = true;
            statusLabel.Text = "Preparando la combinacion...";

            var request = new PdfMergeRequest(
                entries.Select(entry => entry.Path).ToList(),
                outputPath,
                entries.Sum(entry => entry.PageCount));
            mergeWorker.RunWorkerAsync(request);
        }

        private void MergeWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            var request = (PdfMergeRequest)e.Argument;
            e.Result = PdfMergeService.Merge(
                request.SourcePaths,
                request.OutputPath,
                request.ExpectedPageCount,
                progress =>
                {
                    var percent = progress.TotalPages <= 0
                        ? 0
                        : Math.Min(100, (int)Math.Round(progress.CompletedPages * 100D / progress.TotalPages));
                    mergeWorker.ReportProgress(percent, progress);
                });
        }

        private void MergeWorker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            progressBar.Value = Math.Max(progressBar.Minimum, Math.Min(progressBar.Maximum, e.ProgressPercentage));
            var progress = e.UserState as PdfMergeProgress;
            if (progress != null)
            {
                statusLabel.Text = string.Format(
                    "Pagina {0} de {1} - {2}",
                    progress.CompletedPages,
                    progress.TotalPages,
                    Path.GetFileName(progress.SourcePath));
            }
        }

        private void MergeWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            mergeInProgress = false;
            SetEditingEnabled(true);

            if (e.Error != null)
            {
                progressBar.Visible = false;
                statusLabel.Text = "No se ha creado ningun archivo.";
                AppLog.Write("Fallo combinando PDFs: " + e.Error);
                MessageBox.Show(
                    this,
                    e.Error.Message,
                    "No se pudieron combinar los PDFs",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            var result = (PdfMergeResult)e.Result;
            progressBar.Value = 100;
            statusLabel.Text = string.Format(
                "Creado: {0} ({1} paginas)",
                Path.GetFileName(result.OutputPath),
                result.PageCount);
            AppLog.Write("PDF combinado creado: " + result.OutputPath);

            if (openResultCheckBox.Checked)
            {
                try
                {
                    Process.Start(
                        Application.ExecutablePath,
                        "--open \"" + result.OutputPath + "\"");
                    Close();
                    return;
                }
                catch (Exception ex)
                {
                    AppLog.Write("No se pudo abrir el combinado automaticamente: " + ex.Message);
                }
            }

            MessageBox.Show(
                this,
                "PDF combinado creado correctamente en:" + Environment.NewLine + result.OutputPath,
                "Combinar PDFs",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void AddPaths(IEnumerable<string> paths, bool showErrors)
        {
            if (paths == null)
            {
                return;
            }

            var errors = new List<string>();
            var existingPaths = new HashSet<string>(
                GetOrderedEntries().Select(entry => Path.GetFullPath(entry.Path)),
                StringComparer.OrdinalIgnoreCase);

            foreach (var rawPath in paths)
            {
                if (string.IsNullOrWhiteSpace(rawPath) ||
                    !string.Equals(Path.GetExtension(rawPath), ".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (filesListView.Items.Count >= MaximumFiles)
                {
                    errors.Add("Se ha alcanzado el maximo de " + MaximumFiles + " PDFs.");
                    break;
                }

                try
                {
                    var path = Path.GetFullPath(rawPath);
                    if (!File.Exists(path) || !existingPaths.Add(path))
                    {
                        continue;
                    }

                    var pageCount = PdfMergeService.ReadPageCount(path);
                    var entry = new PdfMergeEntry(path, pageCount);
                    var item = new ListViewItem(Path.GetFileName(path));
                    item.SubItems.Add(pageCount.ToString());
                    item.SubItems.Add(Path.GetDirectoryName(path));
                    item.Tag = entry;
                    item.ToolTipText = path;
                    filesListView.Items.Add(item);
                }
                catch (Exception ex)
                {
                    errors.Add(Path.GetFileName(rawPath) + ": " + ex.Message);
                }
            }

            AutoSizeColumns();
            UpdateListState();

            if (showErrors && errors.Count > 0)
            {
                MessageBox.Show(
                    this,
                    "Algunos archivos no se pudieron agregar:" + Environment.NewLine +
                    string.Join(Environment.NewLine, errors.Take(6).ToArray()),
                    "Combinar PDFs",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private IList<PdfMergeEntry> GetOrderedEntries()
        {
            return filesListView.Items
                .Cast<ListViewItem>()
                .Select(item => (PdfMergeEntry)item.Tag)
                .ToList();
        }

        private void RemoveSelectedItems()
        {
            var selectedItems = filesListView.SelectedItems.Cast<ListViewItem>().ToList();
            foreach (var item in selectedItems)
            {
                filesListView.Items.Remove(item);
            }

            UpdateListState();
        }

        private void MoveSelectedItem(int offset)
        {
            if (filesListView.SelectedItems.Count != 1)
            {
                return;
            }

            var item = filesListView.SelectedItems[0];
            var targetIndex = item.Index + offset;
            if (targetIndex < 0 || targetIndex >= filesListView.Items.Count)
            {
                return;
            }

            filesListView.Items.Remove(item);
            filesListView.Items.Insert(targetIndex, item);
            item.Selected = true;
            item.Focused = true;
            item.EnsureVisible();
            UpdateListState();
        }

        private void FilesListView_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateListState();
        }

        private void FilesListView_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
            {
                RemoveSelectedItems();
                e.Handled = true;
                return;
            }

            if (e.Alt && e.KeyCode == Keys.Up)
            {
                MoveSelectedItem(-1);
                e.Handled = true;
                return;
            }

            if (e.Alt && e.KeyCode == Keys.Down)
            {
                MoveSelectedItem(1);
                e.Handled = true;
            }
        }

        private void FilesListView_ItemDrag(object sender, ItemDragEventArgs e)
        {
            if (!mergeInProgress && filesListView.SelectedItems.Count == 1)
            {
                filesListView.DoDragDrop(e.Item, DragDropEffects.Move);
            }
        }

        private void FilesListView_DragEnter(object sender, DragEventArgs e)
        {
            if (mergeInProgress)
            {
                e.Effect = DragDropEffects.None;
                return;
            }

            if (e.Data.GetDataPresent(typeof(ListViewItem)))
            {
                e.Effect = DragDropEffects.Move;
                return;
            }

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
            }
        }

        private void FilesListView_DragOver(object sender, DragEventArgs e)
        {
            FilesListView_DragEnter(sender, e);
        }

        private void FilesListView_DragDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                AddPaths((string[])e.Data.GetData(DataFormats.FileDrop), true);
                return;
            }

            var draggedItem = e.Data.GetData(typeof(ListViewItem)) as ListViewItem;
            if (draggedItem == null || draggedItem.ListView != filesListView)
            {
                return;
            }

            var clientPoint = filesListView.PointToClient(new Point(e.X, e.Y));
            var targetItem = filesListView.GetItemAt(clientPoint.X, clientPoint.Y);
            var originalIndex = draggedItem.Index;
            var targetIndex = targetItem == null ? filesListView.Items.Count : targetItem.Index;
            if (originalIndex < targetIndex)
            {
                targetIndex--;
            }

            filesListView.Items.Remove(draggedItem);
            targetIndex = Math.Max(0, Math.Min(filesListView.Items.Count, targetIndex));
            filesListView.Items.Insert(targetIndex, draggedItem);
            draggedItem.Selected = true;
            draggedItem.Focused = true;
            draggedItem.EnsureVisible();
            UpdateListState();
        }

        private void Form_DragEnter(object sender, DragEventArgs e)
        {
            if (!mergeInProgress && e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
            }
        }

        private void Form_DragDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                AddPaths((string[])e.Data.GetData(DataFormats.FileDrop), true);
            }
        }

        private void PdfMergeForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!mergeInProgress)
            {
                return;
            }

            e.Cancel = true;
            System.Media.SystemSounds.Beep.Play();
            statusLabel.Text = "Espera a que termine la combinacion.";
        }

        private void PdfMergeForm_Resize(object sender, EventArgs e)
        {
            AutoSizeColumns();
        }

        private void AutoSizeColumns()
        {
            if (filesListView.Columns.Count < 3 || filesListView.ClientSize.Width < 250)
            {
                return;
            }

            filesListView.Columns[1].Width = 70;
            filesListView.Columns[0].Width = Math.Max(180, (int)(filesListView.ClientSize.Width * 0.47));
            filesListView.Columns[2].Width = Math.Max(
                120,
                filesListView.ClientSize.Width - filesListView.Columns[0].Width -
                filesListView.Columns[1].Width - 5);
        }

        private void UpdateListState()
        {
            var entries = GetOrderedEntries();
            var totalPages = entries.Sum(entry => entry.PageCount);
            totalsLabel.Text = string.Format(
                "{0} PDF{1} - {2} pagina{3}",
                entries.Count,
                entries.Count == 1 ? string.Empty : "s",
                totalPages,
                totalPages == 1 ? string.Empty : "s");

            combineButton.Enabled = !mergeInProgress && entries.Count >= 2;
            removeButton.Enabled = !mergeInProgress && filesListView.SelectedItems.Count > 0;
            moveUpButton.Enabled =
                !mergeInProgress &&
                filesListView.SelectedItems.Count == 1 &&
                filesListView.SelectedItems[0].Index > 0;
            moveDownButton.Enabled =
                !mergeInProgress &&
                filesListView.SelectedItems.Count == 1 &&
                filesListView.SelectedItems[0].Index < filesListView.Items.Count - 1;
        }

        private void SetEditingEnabled(bool enabled)
        {
            filesListView.Enabled = enabled;
            addButton.Enabled = enabled;
            cancelButton.Enabled = enabled;
            openResultCheckBox.Enabled = enabled;
            UpdateListState();
        }

        private static string BuildDefaultOutputName(string firstSourcePath)
        {
            return Path.GetFileNameWithoutExtension(firstSourcePath) + "_combinado.pdf";
        }

        private sealed class PdfMergeEntry
        {
            public PdfMergeEntry(string path, int pageCount)
            {
                Path = path;
                PageCount = pageCount;
            }

            public string Path { get; private set; }

            public int PageCount { get; private set; }
        }

        private sealed class PdfMergeRequest
        {
            public PdfMergeRequest(IList<string> sourcePaths, string outputPath, int expectedPageCount)
            {
                SourcePaths = sourcePaths;
                OutputPath = outputPath;
                ExpectedPageCount = expectedPageCount;
            }

            public IList<string> SourcePaths { get; private set; }

            public string OutputPath { get; private set; }

            public int ExpectedPageCount { get; private set; }
        }
    }
}

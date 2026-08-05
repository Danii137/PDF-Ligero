using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace FirmaAutomatica
{
    internal sealed class SigningProgressForm : Form
    {
        private readonly Label titleLabel;
        private readonly Label detailLabel;
        private readonly ProgressBar progressBar;

        public SigningProgressForm(int totalFiles)
        {
            Text = "Firmando PDFs";
            AppBranding.ApplyWindowIcon(this);
            Width = 540;
            Height = 170;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ControlBox = false;
            ShowInTaskbar = false;

            titleLabel = new Label
            {
                Left = 16,
                Top = 18,
                Width = 490,
                Height = 24,
                Font = new Font(Font.FontFamily, 10, FontStyle.Bold),
                Text = "Preparando firma..."
            };

            detailLabel = new Label
            {
                Left = 16,
                Top = 48,
                Width = 490,
                Height = 34,
                Text = totalFiles <= 1
                    ? "Se esta firmando 1 PDF."
                    : string.Format("Se estan firmando {0} PDFs.", totalFiles)
            };

            progressBar = new ProgressBar
            {
                Left = 16,
                Top = 92,
                Width = 490,
                Height = 22,
                Minimum = 0,
                Maximum = Math.Max(1, totalFiles),
                Value = 0,
                Style = ProgressBarStyle.Continuous
            };

            Controls.Add(titleLabel);
            Controls.Add(detailLabel);
            Controls.Add(progressBar);
        }

        public void UpdateProgress(int currentFile, int totalFiles, string filePath)
        {
            titleLabel.Text = string.Format("Firmando PDF {0} de {1}", currentFile, totalFiles);
            detailLabel.Text = Path.GetFileName(filePath);
            progressBar.Maximum = Math.Max(1, totalFiles);
            progressBar.Value = Math.Max(progressBar.Minimum, Math.Min(currentFile, progressBar.Maximum));
            Refresh();
        }
    }
}

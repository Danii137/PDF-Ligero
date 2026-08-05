using System;
using System.Drawing;
using System.Windows.Forms;

namespace FirmaAutomatica
{
    /// <summary>
    /// Small modeless progress surface used by the OCR worker. Keeping it
    /// modeless leaves page navigation, search and tab switching available
    /// while recognition runs.
    /// </summary>
    internal sealed class PdfOcrProgressForm : Form
    {
        private static readonly Color SurfaceColor =
            Color.FromArgb(250, 249, 247);
        private static readonly Color TitleColor =
            Color.FromArgb(31, 31, 29);
        private static readonly Color BodyColor =
            Color.FromArgb(96, 94, 90);
        private static readonly Color DividerColor =
            Color.FromArgb(211, 209, 204);
        private static readonly Color AccentColor =
            Color.FromArgb(238, 91, 61);
        private static readonly Color AccentTintColor =
            Color.FromArgb(251, 236, 231);

        private readonly Label stageLabel;
        private readonly Label detailLabel;
        private readonly ProgressBar progressBar;
        private readonly Button cancelButton;
        private bool operationActive = true;
        private bool cancellationRequested;

        public PdfOcrProgressForm(string title, string initialStage)
        {
            Text = string.IsNullOrWhiteSpace(title)
                ? "OCR y enderezado"
                : title;
            AppBranding.ApplyWindowIcon(this);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(486, 148);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = SurfaceColor;
            Font = CreateUiFont(9.25F, FontStyle.Regular);

            var accentLine = new Panel
            {
                Left = 20,
                Top = 18,
                Width = 38,
                Height = 2,
                BackColor = AccentColor
            };

            var eyebrowLabel = new Label
            {
                Left = 20,
                Top = 27,
                Width = 270,
                Height = 16,
                Text = "OCR LOCAL / ESPAÑOL + INGLÉS",
                Font = CreateArchitecturalFont(7.5F),
                ForeColor = AccentColor
            };

            stageLabel = new Label
            {
                Left = 20,
                Top = 48,
                Width = 432,
                Height = 24,
                Text = initialStage ?? "Preparando…",
                Font = CreateArchitecturalFont(11.25F),
                ForeColor = TitleColor,
                AutoEllipsis = true
            };

            detailLabel = new Label
            {
                Left = 20,
                Top = 75,
                Width = 330,
                Height = 18,
                Text = "El documento original permanece intacto.",
                ForeColor = BodyColor,
                AutoEllipsis = true
            };

            progressBar = new ProgressBar
            {
                Left = 20,
                Top = 105,
                Width = 330,
                Height = 10,
                Minimum = 0,
                Maximum = 100,
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 22
            };
            progressBar.AccessibleName = "Progreso del OCR";
            progressBar.AccessibleDescription =
                "Porcentaje de páginas analizadas o reconocidas.";

            cancelButton = new Button
            {
                Left = 364,
                Top = 92,
                Width = 88,
                Height = 32,
                Text = "Cancelar",
                AccessibleName = "Cancelar OCR"
            };
            StyleSecondaryButton(cancelButton);
            cancelButton.Click += CancelButton_Click;

            Controls.Add(accentLine);
            Controls.Add(eyebrowLabel);
            Controls.Add(stageLabel);
            Controls.Add(detailLabel);
            Controls.Add(progressBar);
            Controls.Add(cancelButton);
            AcceptButton = null;
            CancelButton = cancelButton;
        }

        public event EventHandler CancelRequested;

        public bool IsCancellationRequested
        {
            get { return cancellationRequested; }
        }

        public void UpdateProgress(PdfOcrProgress progress)
        {
            if (progress == null || IsDisposed)
            {
                return;
            }

            stageLabel.Text = string.IsNullOrWhiteSpace(progress.Stage)
                ? "Procesando…"
                : progress.Stage;

            if (progress.TotalPages > 0)
            {
                detailLabel.Text = string.Format(
                    "Página {0} de {1} · el original permanece intacto",
                    Math.Min(
                        progress.TotalPages,
                        Math.Max(0, progress.ProcessedPages)),
                    progress.TotalPages);
            }
            else
            {
                detailLabel.Text =
                    "El documento original permanece intacto.";
            }

            if (progress.TotalSteps > 0)
            {
                progressBar.Style = ProgressBarStyle.Continuous;
                progressBar.Value = Math.Max(
                    progressBar.Minimum,
                    Math.Min(progressBar.Maximum, progress.Percentage));
            }
        }

        public void MarkCancelling()
        {
            if (IsDisposed)
            {
                return;
            }

            cancellationRequested = true;
            cancelButton.Enabled = false;
            cancelButton.Text = "Cancelando…";
            stageLabel.Text = "Cancelando con seguridad…";
            detailLabel.Text =
                "Se terminará la página actual y se borrarán los temporales.";
            progressBar.Style = ProgressBarStyle.Marquee;
            progressBar.MarqueeAnimationSpeed = 18;
        }

        public void CompleteAndClose()
        {
            operationActive = false;
            if (!IsDisposed)
            {
                Close();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (operationActive &&
                e.CloseReason == CloseReason.UserClosing)
            {
                RequestCancellation();
                e.Cancel = true;
                return;
            }

            base.OnFormClosing(e);
        }

        private void CancelButton_Click(object sender, EventArgs e)
        {
            RequestCancellation();
        }

        private void RequestCancellation()
        {
            if (cancellationRequested)
            {
                return;
            }

            MarkCancelling();
            var handler = CancelRequested;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private static void StyleSecondaryButton(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = DividerColor;
            button.BackColor = SurfaceColor;
            button.ForeColor = TitleColor;
            button.Cursor = Cursors.Hand;
            button.FlatAppearance.MouseOverBackColor = AccentTintColor;
            button.FlatAppearance.MouseDownBackColor = DividerColor;
            button.Font = CreateArchitecturalFont(9F);
        }

        private static Font CreateUiFont(float size, FontStyle style)
        {
            try
            {
                return new Font(
                    "Segoe UI Variable Text",
                    size,
                    style,
                    GraphicsUnit.Point);
            }
            catch
            {
                return new Font(
                    SystemFonts.MessageBoxFont.FontFamily,
                    size,
                    style,
                    GraphicsUnit.Point);
            }
        }

        private static Font CreateArchitecturalFont(float size)
        {
            var candidates = new[]
            {
                "Bahnschrift Light SemiCondensed",
                "Bahnschrift Light",
                "Segoe UI Semilight",
                "Segoe UI"
            };
            foreach (var candidate in candidates)
            {
                try
                {
                    var font = new Font(
                        candidate,
                        size,
                        FontStyle.Regular,
                        GraphicsUnit.Point);
                    if (string.Equals(
                            font.Name,
                            candidate,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return font;
                    }

                    font.Dispose();
                }
                catch
                {
                }
            }

            return CreateUiFont(size, FontStyle.Regular);
        }
    }
}

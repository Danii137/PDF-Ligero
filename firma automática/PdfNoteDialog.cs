using System;
using System.Drawing;
using System.Windows.Forms;

namespace FirmaAutomatica
{
    /// <summary>
    /// Cuadro breve para escribir el texto de una nota.
    ///
    /// Deliberadamente pequeño: una nota se escribe de una sentada, sin salir
    /// del documento que se esta revisando.
    /// </summary>
    internal sealed class PdfNoteDialog : Form
    {
        private static readonly Color SurfaceColor =
            Color.FromArgb(250, 249, 247);
        private static readonly Color TitleColor =
            Color.FromArgb(31, 31, 29);
        private static readonly Color BodyColor =
            Color.FromArgb(96, 94, 90);
        private static readonly Color AccentColor =
            Color.FromArgb(238, 91, 61);
        private static readonly Color AccentTextColor =
            Color.FromArgb(185, 68, 45);

        private readonly TextBox noteInput;

        public PdfNoteDialog(string initialText)
        {
            Text = "Nota";
            AppBranding.ApplyWindowIcon(this);
            ClientSize = new Size(420, 250);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = SurfaceColor;
            ForeColor = TitleColor;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);

            var eyebrow = new Label
            {
                Left = 18,
                Top = 14,
                Width = 300,
                Height = 15,
                Text = "ANOTAR / NOTA",
                ForeColor = AccentTextColor,
                Font = new Font("Bahnschrift", 7.5f, FontStyle.Bold)
            };

            var title = new Label
            {
                Left = 18,
                Top = 30,
                Width = 384,
                Height = 26,
                Text = "Escribe el comentario",
                ForeColor = TitleColor,
                Font = new Font("Bahnschrift", 13.5f, FontStyle.Regular)
            };

            var accent = new Panel
            {
                Left = 18,
                Top = 60,
                Width = 34,
                Height = 2,
                BackColor = AccentColor
            };

            noteInput = new TextBox
            {
                Left = 18,
                Top = 76,
                Width = 384,
                Height = 108,
                Multiline = true,
                AcceptsReturn = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White,
                ForeColor = TitleColor,
                MaxLength = 4000,
                Text = initialText ?? string.Empty,
                AccessibleName = "Texto de la nota"
            };
            noteInput.TextChanged += delegate { RefreshEnabled(); };

            var hint = new Label
            {
                Left = 18,
                Top = 190,
                Width = 384,
                Height = 16,
                Text = "Quien reciba el PDF podrá leerla y borrarla.",
                ForeColor = BodyColor,
                Font = new Font("Segoe UI", 8f, FontStyle.Regular)
            };

            var cancelButton = new Button
            {
                Left = 202,
                Top = 208,
                Width = 96,
                Height = 30,
                Text = "Cancelar",
                DialogResult = DialogResult.Cancel,
                FlatStyle = FlatStyle.Flat,
                BackColor = SurfaceColor,
                ForeColor = TitleColor
            };
            cancelButton.FlatAppearance.BorderColor =
                Color.FromArgb(211, 209, 204);

            AcceptButtonControl = new Button
            {
                Left = 306,
                Top = 208,
                Width = 96,
                Height = 30,
                Text = "Añadir",
                DialogResult = DialogResult.OK,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(31, 31, 29),
                ForeColor = Color.White
            };
            AcceptButtonControl.FlatAppearance.BorderSize = 0;
            AcceptButtonControl.Click += delegate
            {
                Note = noteInput.Text;
            };

            Controls.Add(eyebrow);
            Controls.Add(title);
            Controls.Add(accent);
            Controls.Add(noteInput);
            Controls.Add(hint);
            Controls.Add(cancelButton);
            Controls.Add(AcceptButtonControl);

            AcceptButton = AcceptButtonControl;
            CancelButton = cancelButton;
            Note = string.Empty;
            RefreshEnabled();
        }

        private Button AcceptButtonControl { get; set; }

        public string Note { get; private set; }

        private void RefreshEnabled()
        {
            AcceptButtonControl.Enabled =
                !string.IsNullOrWhiteSpace(noteInput.Text);
        }
    }
}

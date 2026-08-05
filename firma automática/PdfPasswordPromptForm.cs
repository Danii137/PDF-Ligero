using System;
using System.Drawing;
using System.Windows.Forms;

namespace FirmaAutomatica
{
    /// <summary>
    /// Pide la contrasena de apertura de un PDF con la identidad visual de la
    /// aplicacion, en lugar del formulario en ingles que trae PdfiumViewer.
    ///
    /// La contrasena solo vive en el TextBox y en la propiedad Password mientras
    /// el dialogo existe. Nunca se registra en AppLog, no se guarda en
    /// preferencias y se borra en Dispose.
    /// </summary>
    internal sealed class PdfPasswordPromptForm : Form
    {
        private static readonly Color PaperColor =
            Color.FromArgb(250, 249, 247);
        private static readonly Color WorkspaceColor =
            Color.FromArgb(239, 238, 235);
        private static readonly Color DividerColor =
            Color.FromArgb(211, 209, 204);
        private static readonly Color TitleColor =
            Color.FromArgb(31, 31, 29);
        private static readonly Color BodyColor =
            Color.FromArgb(96, 94, 90);
        private static readonly Color MutedColor =
            Color.FromArgb(139, 136, 130);
        private static readonly Color AccentColor =
            Color.FromArgb(238, 91, 61);
        private static readonly Color AccentTextColor =
            Color.FromArgb(185, 68, 45);
        private static readonly Color AccentTintColor =
            Color.FromArgb(251, 236, 231);

        private readonly TextBox passwordTextBox;
        private readonly Button openButton;
        private readonly Button cancelButton;

        public PdfPasswordPromptForm(string displayName, bool retryAfterFailure)
        {
            var safeName = string.IsNullOrWhiteSpace(displayName)
                ? "Documento PDF"
                : displayName.Trim();

            Password = string.Empty;

            Text = "PDF protegido - PDF Ligero";
            AppBranding.ApplyWindowIcon(this);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(468, 286);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = WorkspaceColor;
            Font = CreateUiFont(9.25f, FontStyle.Regular);

            var headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 76,
                BackColor = PaperColor
            };
            headerPanel.Controls.Add(new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = DividerColor
            });

            var eyebrowLabel = new Label
            {
                Left = 20,
                Top = 8,
                Width = 350,
                Height = 15,
                Text = "PDF PROTEGIDO / CONTRASEÑA",
                ForeColor = AccentTextColor,
                Font = CreateArchitecturalFont(7.5f, true),
                TextAlign = ContentAlignment.MiddleLeft
            };
            var titleLabel = new Label
            {
                Left = 20,
                Top = 24,
                Width = 386,
                Height = 28,
                Text = safeName,
                ForeColor = TitleColor,
                Font = CreateArchitecturalFont(13f, false),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
            var accentLine = new Panel
            {
                Left = 20,
                Top = 61,
                Width = 42,
                Height = 2,
                BackColor = AccentColor
            };
            var lockLabel = new Label
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Left = 411,
                Top = 16,
                Width = 40,
                Height = 36,
                Text = "\uE72E",
                ForeColor = DividerColor,
                Font = CreateGlyphFont(21f),
                TextAlign = ContentAlignment.MiddleRight
            };

            headerPanel.Controls.Add(eyebrowLabel);
            headerPanel.Controls.Add(titleLabel);
            headerPanel.Controls.Add(accentLine);
            headerPanel.Controls.Add(lockLabel);

            var footerPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 64,
                BackColor = PaperColor
            };
            footerPanel.Controls.Add(new Panel
            {
                Dock = DockStyle.Top,
                Height = 1,
                BackColor = DividerColor
            });

            cancelButton = CreateActionButton("Cancelar", false);
            cancelButton.Left = 254;
            cancelButton.Top = 15;
            cancelButton.DialogResult = DialogResult.Cancel;
            cancelButton.AccessibleName = "Cancelar la apertura del PDF protegido";

            openButton = CreateActionButton("Abrir", true);
            openButton.Left = 360;
            openButton.Top = 15;
            openButton.Enabled = false;
            openButton.AccessibleName = "Abrir el PDF con esta contraseña";
            openButton.AccessibleDescription =
                "El documento se abrirá en modo protegido: se puede ver, buscar, " +
                "imprimir y guardar una copia, pero no editar.";
            openButton.Click += OpenButton_Click;

            footerPanel.Controls.Add(cancelButton);
            footerPanel.Controls.Add(openButton);

            var bodyPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(20, 16, 20, 12),
                BackColor = WorkspaceColor
            };

            var explanationLabel = new Label
            {
                Left = 20,
                Top = 16,
                Width = 428,
                Height = 34,
                Text = "Este documento pide una contraseña para abrirse. " +
                    "PDF Ligero no la guarda ni la envía a ningún sitio.",
                ForeColor = BodyColor,
                Font = CreateUiFont(9.25f, FontStyle.Regular),
                TextAlign = ContentAlignment.TopLeft
            };

            var fieldCaptionLabel = new Label
            {
                Left = 20,
                Top = 58,
                Width = 428,
                Height = 16,
                Text = "CONTRASEÑA DE APERTURA",
                ForeColor = AccentTextColor,
                Font = CreateArchitecturalFont(7.25f, true),
                TextAlign = ContentAlignment.MiddleLeft
            };

            passwordTextBox = new TextBox
            {
                Left = 20,
                Top = 77,
                Width = 428,
                Height = 26,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = PaperColor,
                ForeColor = TitleColor,
                Font = CreateUiFont(10f, FontStyle.Regular),
                UseSystemPasswordChar = true,
                MaxLength = 512
            };
            passwordTextBox.AccessibleName = "Contraseña de apertura del PDF";
            passwordTextBox.TextChanged += PasswordTextBox_TextChanged;

            var retryLabel = new Label
            {
                Left = 20,
                Top = 108,
                Width = 428,
                Height = 18,
                Text = "La contraseña no es correcta. Inténtalo de nuevo.",
                ForeColor = AccentTextColor,
                Font = CreateUiFont(9f, FontStyle.Regular),
                TextAlign = ContentAlignment.MiddleLeft,
                Visible = retryAfterFailure
            };

            var modeLabel = new Label
            {
                Left = 20,
                Top = 130,
                Width = 428,
                Height = 34,
                Text = "Se abrirá en modo protegido: podrás verlo, buscar, imprimir " +
                    "y guardar una copia, pero no editarlo.",
                ForeColor = MutedColor,
                Font = CreateUiFont(9f, FontStyle.Regular),
                TextAlign = ContentAlignment.TopLeft
            };

            bodyPanel.Controls.Add(explanationLabel);
            bodyPanel.Controls.Add(fieldCaptionLabel);
            bodyPanel.Controls.Add(passwordTextBox);
            bodyPanel.Controls.Add(retryLabel);
            bodyPanel.Controls.Add(modeLabel);

            Controls.Add(bodyPanel);
            Controls.Add(footerPanel);
            Controls.Add(headerPanel);

            AcceptButton = openButton;
            CancelButton = cancelButton;
            ActiveControl = passwordTextBox;
        }

        /// <summary>
        /// Contrasena escrita. Solo es valida mientras el dialogo no se ha
        /// dispuesto.
        /// </summary>
        public string Password { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (passwordTextBox != null && !passwordTextBox.IsDisposed)
                {
                    passwordTextBox.TextChanged -= PasswordTextBox_TextChanged;
                    passwordTextBox.Clear();
                }

                if (openButton != null && !openButton.IsDisposed)
                {
                    openButton.Click -= OpenButton_Click;
                }

                Password = null;
            }

            base.Dispose(disposing);
        }

        private void PasswordTextBox_TextChanged(object sender, EventArgs e)
        {
            openButton.Enabled = passwordTextBox.Text.Length > 0;
        }

        private void OpenButton_Click(object sender, EventArgs e)
        {
            if (passwordTextBox.Text.Length == 0)
            {
                return;
            }

            Password = passwordTextBox.Text;
            DialogResult = DialogResult.OK;
            Close();
        }

        private static Button CreateActionButton(string text, bool primary)
        {
            var button = new Button
            {
                Width = 96,
                Height = 34,
                Text = text,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Font = CreateArchitecturalFont(9f, true),
                BackColor = primary ? TitleColor : PaperColor,
                ForeColor = primary ? Color.White : TitleColor
            };
            button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.BorderColor = DividerColor;
            button.FlatAppearance.MouseOverBackColor = primary
                ? Color.FromArgb(57, 58, 54)
                : AccentTintColor;
            button.FlatAppearance.MouseDownBackColor = primary
                ? Color.FromArgb(57, 58, 54)
                : DividerColor;
            return button;
        }

        private static Font CreateGlyphFont(float size)
        {
            try
            {
                return new Font(
                    "Segoe MDL2 Assets",
                    size,
                    FontStyle.Regular,
                    GraphicsUnit.Point);
            }
            catch
            {
                return CreateArchitecturalFont(size, false);
            }
        }

        private static Font CreateArchitecturalFont(float size, bool semibold)
        {
            var style = semibold
                ? FontStyle.Bold
                : FontStyle.Regular;
            try
            {
                return new Font(
                    semibold
                        ? "Bahnschrift SemiCondensed"
                        : "Bahnschrift Light SemiCondensed",
                    size,
                    style,
                    GraphicsUnit.Point);
            }
            catch
            {
                return CreateUiFont(size, style);
            }
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
                    "Segoe UI",
                    size,
                    style,
                    GraphicsUnit.Point);
            }
        }
    }
}

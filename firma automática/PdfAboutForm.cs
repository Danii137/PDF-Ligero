using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace FirmaAutomatica
{
    /// <summary>
    /// Acerca de: version, licencia y avisos de terceros.
    ///
    /// No es un adorno. La AGPL v3 exige que un programa con interfaz de
    /// usuario enseñe el aviso legal y diga como conseguir el codigo fuente.
    /// El ejecutable ya lo llevaba en sus metadatos, pero desde la ventana no
    /// se veia.
    /// </summary>
    internal sealed class PdfAboutForm : Form
    {
        private const string SourceUrl =
            "https://github.com/Danii137/PDF-Ligero";

        private static readonly Color PaperColor =
            Color.FromArgb(250, 249, 247);
        private static readonly Color WorkspaceColor =
            Color.FromArgb(239, 238, 235);
        private static readonly Color DividerColor =
            Color.FromArgb(211, 209, 204);
        private static readonly Color TitleColor =
            Color.FromArgb(31, 31, 29);
        private static readonly Color MutedColor =
            Color.FromArgb(139, 136, 130);
        private static readonly Color AccentColor =
            Color.FromArgb(238, 91, 61);
        private static readonly Color AccentTextColor =
            Color.FromArgb(185, 68, 45);
        private static readonly Color AccentTintColor =
            Color.FromArgb(251, 236, 231);

        public PdfAboutForm()
        {
            Text = "Acerca de PDF Ligero";
            AppBranding.ApplyWindowIcon(this);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(560, 486);
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
                Height = 92,
                BackColor = PaperColor
            };
            headerPanel.Controls.Add(new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = DividerColor
            });
            headerPanel.Controls.Add(new Label
            {
                Left = 20,
                Top = 10,
                Width = 420,
                Height = 15,
                Text = "ACERCA DE",
                ForeColor = AccentTextColor,
                Font = CreateArchitecturalFont(7.5f, true),
                TextAlign = ContentAlignment.MiddleLeft
            });
            headerPanel.Controls.Add(new Label
            {
                Left = 20,
                Top = 26,
                Width = 500,
                Height = 30,
                Text = "PDF Ligero",
                ForeColor = TitleColor,
                Font = CreateArchitecturalFont(15f, false),
                TextAlign = ContentAlignment.MiddleLeft
            });
            headerPanel.Controls.Add(new Label
            {
                Left = 20,
                Top = 56,
                Width = 500,
                Height = 20,
                Text = DescribeVersion(),
                ForeColor = MutedColor,
                Font = CreateArchitecturalFont(8f, false),
                TextAlign = ContentAlignment.MiddleLeft
            });
            headerPanel.Controls.Add(new Panel
            {
                Left = 20,
                Top = 80,
                Width = 42,
                Height = 2,
                BackColor = AccentColor
            });

            var footerPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 60,
                BackColor = PaperColor
            };
            footerPanel.Controls.Add(new Panel
            {
                Dock = DockStyle.Top,
                Height = 1,
                BackColor = DividerColor
            });

            var licenseButton = CreateActionButton("Ver la licencia", false);
            licenseButton.Width = 128;
            licenseButton.Left = 20;
            licenseButton.Top = 13;
            licenseButton.Click += delegate { OpenLicenseFile(); };

            var sourceButton = CreateActionButton("Código fuente", false);
            sourceButton.Width = 124;
            sourceButton.Left = 156;
            sourceButton.Top = 13;
            sourceButton.Click += delegate { OpenSource(); };

            var closeButton = CreateActionButton("Cerrar", true);
            closeButton.Left = 444;
            closeButton.Top = 13;
            closeButton.DialogResult = DialogResult.OK;

            footerPanel.Controls.Add(licenseButton);
            footerPanel.Controls.Add(sourceButton);
            footerPanel.Controls.Add(closeButton);

            var bodyPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(20, 14, 20, 14),
                BackColor = WorkspaceColor
            };

            var texto = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = PaperColor,
                ForeColor = TitleColor,
                Font = CreateUiFont(9f, FontStyle.Regular),
                Text = BuildNotice(),
                AccessibleName = "Aviso de licencia"
            };
            texto.Select(0, 0);
            bodyPanel.Controls.Add(texto);

            Controls.Add(bodyPanel);
            Controls.Add(footerPanel);
            Controls.Add(headerPanel);

            AcceptButton = closeButton;
            CancelButton = closeButton;
        }

        private static string DescribeVersion()
        {
            try
            {
                var ensamblado = Assembly.GetExecutingAssembly();
                var version = FileVersionInfo.GetVersionInfo(
                    ensamblado.Location);
                return "Versión " + version.FileVersion +
                    "   ·   AGOIN   ·   " +
                    Path.GetFileName(ensamblado.Location);
            }
            catch (Exception)
            {
                return "AGOIN";
            }
        }

        private static string BuildNotice()
        {
            return
                "PDF Ligero y Word2PDF" + Environment.NewLine +
                "Copyright (C) 2026 AGOIN" + Environment.NewLine +
                Environment.NewLine +
                "Este programa es software libre: puede redistribuirlo y " +
                "modificarlo bajo los términos de la Licencia Pública " +
                "General Affero de GNU, versión 3, publicada por la Free " +
                "Software Foundation." + Environment.NewLine +
                Environment.NewLine +
                "Se distribuye con la esperanza de que sea útil, pero SIN " +
                "NINGUNA GARANTÍA; ni siquiera la garantía implícita de " +
                "COMERCIABILIDAD o APTITUD PARA UN PROPÓSITO DETERMINADO. " +
                "Véase la Licencia Pública General Affero de GNU para más " +
                "detalles." + Environment.NewLine +
                Environment.NewLine +
                "Debería haber recibido una copia de la licencia junto con " +
                "este programa. Si no es así, véase " +
                "https://www.gnu.org/licenses/" + Environment.NewLine +
                Environment.NewLine +
                "CÓDIGO FUENTE" + Environment.NewLine +
                "La sección 13 de la AGPL obliga a ofrecer el código fuente " +
                "de la versión que se está usando. Está en:" +
                Environment.NewLine +
                SourceUrl + Environment.NewLine +
                Environment.NewLine +
                "COMPONENTES DE TERCEROS" + Environment.NewLine +
                Environment.NewLine +
                "· iTextSharp 5.5.13 — iText Group NV — AGPL v3." +
                Environment.NewLine +
                "  Es el motivo de que este programa sea AGPL." +
                Environment.NewLine +
                Environment.NewLine +
                "· PDFium — The Chromium Authors — licencia BSD de 3 " +
                "cláusulas." + Environment.NewLine +
                Environment.NewLine +
                "· PdfiumViewer — Pieter van Ginkel — Apache 2.0." +
                Environment.NewLine +
                Environment.NewLine +
                "· Bouncy Castle — The Legion of the Bouncy Castle — " +
                "licencia MIT." + Environment.NewLine +
                Environment.NewLine +
                "· Tesseract OCR — Apache 2.0. Se distribuye el motor y los " +
                "modelos de español, inglés y detección de orientación." +
                Environment.NewLine +
                Environment.NewLine +
                "El detalle completo está en LICENCIAS.md y " +
                "THIRD-PARTY-NOTICES.md, junto al programa.";
        }

        private void OpenLicenseFile()
        {
            // El LICENSE vive junto al codigo, no junto al ejecutable: se
            // buscan las dos rutas antes de rendirse.
            var candidatos = new[]
            {
                Path.Combine(
                    Path.GetDirectoryName(Application.ExecutablePath),
                    "LICENSE"),
                Path.Combine(
                    Path.GetDirectoryName(Application.ExecutablePath),
                    "..\\..\\..\\LICENSE")
            };

            foreach (var candidato in candidatos)
            {
                try
                {
                    if (File.Exists(candidato))
                    {
                        Process.Start(new ProcessStartInfo(
                            Path.GetFullPath(candidato))
                        {
                            UseShellExecute = true
                        });
                        return;
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Write("No se pudo abrir la licencia: " + ex);
                }
            }

            OpenUrl("https://www.gnu.org/licenses/agpl-3.0.html");
        }

        private void OpenSource()
        {
            OpenUrl(SourceUrl);
        }

        private void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                AppLog.Write("No se pudo abrir " + url + ": " + ex);
                MessageBox.Show(
                    this,
                    "No se pudo abrir el navegador.\r\n\r\n" + url,
                    "Acerca de PDF Ligero",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
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

        private static Font CreateArchitecturalFont(float size, bool semibold)
        {
            var style = semibold ? FontStyle.Bold : FontStyle.Regular;
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
                return new Font("Segoe UI", size, style, GraphicsUnit.Point);
            }
            catch
            {
                return new Font(
                    FontFamily.GenericSansSerif,
                    size,
                    style,
                    GraphicsUnit.Point);
            }
        }
    }
}

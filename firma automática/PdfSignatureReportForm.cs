using System;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace FirmaAutomatica
{
    /// <summary>
    /// Enseña quien ha firmado el documento y si la firma sigue valiendo.
    /// </summary>
    internal sealed class PdfSignatureReportForm : Form
    {
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
        private static readonly Color ValidColor =
            Color.FromArgb(34, 105, 63);
        private static readonly Color WarningColor =
            Color.FromArgb(160, 106, 15);
        private static readonly Color InvalidColor =
            Color.FromArgb(173, 42, 33);

        public PdfSignatureReportForm(
            PdfSignatureReport report,
            string documentName)
        {
            if (report == null)
            {
                throw new ArgumentNullException("report");
            }

            Text = "Firmas del documento - PDF Ligero";
            AppBranding.ApplyWindowIcon(this);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(560, 520);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(480, 380);
            MaximizeBox = true;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = WorkspaceColor;
            Font = CreateUiFont(9.25f, FontStyle.Regular);

            var headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 88,
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
                Top = 8,
                Width = 420,
                Height = 15,
                Text = "DOCUMENTO / FIRMAS",
                ForeColor = AccentTextColor,
                Font = CreateArchitecturalFont(7.5f, true),
                TextAlign = ContentAlignment.MiddleLeft
            });
            headerPanel.Controls.Add(new Label
            {
                Left = 20,
                Top = 24,
                Width = 500,
                Height = 26,
                Text = report.HasSignatures
                    ? DescribeCount(report.Signatures.Count)
                    : "Este documento no está firmado",
                ForeColor = TitleColor,
                Font = CreateArchitecturalFont(13f, false),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            });
            headerPanel.Controls.Add(new Label
            {
                Left = 20,
                Top = 52,
                Width = 500,
                Height = 20,
                Text = documentName ?? string.Empty,
                ForeColor = MutedColor,
                Font = CreateArchitecturalFont(8f, false),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            });
            headerPanel.Controls.Add(new Panel
            {
                Left = 20,
                Top = 76,
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

            var closeButton = CreateActionButton("Cerrar", true);
            closeButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            closeButton.Left = ClientSize.Width - 116;
            closeButton.Top = 13;
            closeButton.DialogResult = DialogResult.OK;
            footerPanel.Controls.Add(closeButton);

            var bodyPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(20, 14, 20, 14),
                BackColor = WorkspaceColor,
                AutoScroll = true
            };

            if (report.HasSignatures)
            {
                // De abajo arriba: con Dock.Top el ultimo que se añade queda
                // el primero, asi que se recorre al reves.
                for (var i = report.Signatures.Count - 1; i >= 0; i--)
                {
                    bodyPanel.Controls.Add(
                        CreateSignatureCard(report.Signatures[i], i + 1));
                }
            }
            else
            {
                bodyPanel.Controls.Add(new Label
                {
                    Dock = DockStyle.Top,
                    Height = 60,
                    Text =
                        "No lleva ninguna firma digital. Puedes firmarlo tú " +
                        "con Ctrl+Mayús+S.",
                    ForeColor = MutedColor,
                    Font = CreateUiFont(9.25f, FontStyle.Regular),
                    TextAlign = ContentAlignment.TopLeft
                });
            }

            Controls.Add(bodyPanel);
            Controls.Add(footerPanel);
            Controls.Add(headerPanel);

            AcceptButton = closeButton;
            CancelButton = closeButton;
        }

        private static string DescribeCount(int count)
        {
            return count == 1
                ? "Una firma digital"
                : count.ToString(CultureInfo.CurrentCulture) +
                    " firmas digitales";
        }

        private Panel CreateSignatureCard(PdfSignatureInfo info, int numero)
        {
            var alto = 156 + (info.Warnings.Count * 32);
            var card = new Panel
            {
                Dock = DockStyle.Top,
                Height = alto,
                Padding = new Padding(0, 0, 0, 12),
                BackColor = WorkspaceColor
            };

            var inner = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = PaperColor
            };
            inner.Controls.Add(new Panel
            {
                Dock = DockStyle.Left,
                Width = 3,
                BackColor = StatusColor(info.Status)
            });

            inner.Controls.Add(new Label
            {
                Left = 16,
                Top = 12,
                Width = 480,
                Height = 22,
                Text = info.StatusText,
                ForeColor = StatusColor(info.Status),
                Font = CreateArchitecturalFont(10.5f, true),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            });

            inner.Controls.Add(new Label
            {
                Left = 16,
                Top = 36,
                Width = 480,
                Height = 22,
                Text = info.SignerName,
                ForeColor = TitleColor,
                Font = CreateUiFont(10f, FontStyle.Regular),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            });

            inner.Controls.Add(new Label
            {
                Left = 16,
                Top = 60,
                Width = 480,
                Height = 20,
                Text = BuildWhenLine(info),
                ForeColor = MutedColor,
                Font = CreateArchitecturalFont(8f, false),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            });

            inner.Controls.Add(new Label
            {
                Left = 16,
                Top = 80,
                Width = 480,
                Height = 20,
                Text = BuildIssuerLine(info),
                ForeColor = MutedColor,
                Font = CreateArchitecturalFont(8f, false),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            });

            inner.Controls.Add(new Label
            {
                Left = 16,
                Top = 100,
                Width = 480,
                Height = 20,
                Text = BuildCoverageLine(info),
                ForeColor = MutedColor,
                Font = CreateArchitecturalFont(8f, false),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            });

            var top = 126;
            foreach (var aviso in info.Warnings)
            {
                inner.Controls.Add(new Label
                {
                    Left = 16,
                    Top = top,
                    Width = 500,
                    Height = 30,
                    Text = "·  " + aviso,
                    ForeColor = WarningColor,
                    Font = CreateUiFont(8.5f, FontStyle.Regular),
                    TextAlign = ContentAlignment.TopLeft
                });
                top += 32;
            }

            card.Controls.Add(inner);
            return card;
        }

        private static string BuildWhenLine(PdfSignatureInfo info)
        {
            var texto = new StringBuilder();
            if (info.SignedAt.HasValue)
            {
                texto.Append("Firmado el ");
                texto.Append(
                    info.SignedAt.Value.ToString(
                        "d 'de' MMMM 'de' yyyy',' HH:mm",
                        CultureInfo.CurrentCulture));
            }
            else
            {
                texto.Append("Sin fecha de firma");
            }

            if (info.TimeStampedAt.HasValue)
            {
                texto.Append("   ·   con sello de tiempo");
            }

            if (!string.IsNullOrWhiteSpace(info.Reason))
            {
                texto.Append("   ·   ");
                texto.Append(info.Reason);
            }

            return texto.ToString();
        }

        private static string BuildIssuerLine(PdfSignatureInfo info)
        {
            return string.IsNullOrWhiteSpace(info.IssuerName)
                ? "Emisor del certificado desconocido"
                : "Certificado emitido por " + info.IssuerName;
        }

        private static string BuildCoverageLine(PdfSignatureInfo info)
        {
            if (info.CoversWholeDocument)
            {
                return "Cubre el documento entero.";
            }

            return "Cubre la revisión " +
                info.Revision.ToString(CultureInfo.CurrentCulture) +
                " de " +
                info.TotalRevisions.ToString(CultureInfo.CurrentCulture) + ".";
        }

        private static Color StatusColor(PdfSignatureStatus status)
        {
            switch (status)
            {
                case PdfSignatureStatus.Valida:
                    return ValidColor;

                case PdfSignatureStatus.ConAvisos:
                    return WarningColor;

                case PdfSignatureStatus.Invalida:
                    return InvalidColor;

                default:
                    return MutedColor;
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

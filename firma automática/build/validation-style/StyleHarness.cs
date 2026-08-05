using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using PdfiumViewer;

namespace FirmaAutomatica
{
    internal static class StyleHarness
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string pdfPath = args.Length > 0 ? args[0] : null;
            string screenshotPath = args.Length > 1
                ? args[1]
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "architectural-style.png");

            using (var form = new Form())
            using (var tabs = new ClosablePdfTabControl())
            {
                form.Text = "PDF Ligero · validación visual";
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(20, 20);
                form.ClientSize = new Size(980, 680);
                form.BackColor = Color.FromArgb(232, 231, 227);

                tabs.Dock = DockStyle.Fill;
                tabs.TabPages.Add(CreateWorkspacePage("LICENCIA URBANÍSTICA.pdf", pdfPath));
                tabs.TabPages.Add(new TabPage("Memoria de proyecto.pdf")
                {
                    BackColor = Color.FromArgb(252, 251, 248)
                });
                tabs.TabPages.Add(new TabPage("Planos de ejecución.pdf")
                {
                    BackColor = Color.FromArgb(252, 251, 248)
                });
                tabs.SelectedIndex = 0;
                form.Controls.Add(tabs);

                form.Show();
                DateTime deadline = DateTime.UtcNow.AddSeconds(2.5);
                while (DateTime.UtcNow < deadline)
                {
                    Application.DoEvents();
                    Thread.Sleep(25);
                }

                using (var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
                {
                    form.DrawToBitmap(bitmap, form.ClientRectangle);
                    bitmap.Save(screenshotPath);
                }

                form.Close();
            }
        }

        private static TabPage CreateWorkspacePage(string title, string pdfPath)
        {
            var page = new TabPage(title)
            {
                BackColor = Color.FromArgb(252, 251, 248),
                Padding = new Padding(0)
            };

            var thumbnailHost = new Panel
            {
                Dock = DockStyle.Left,
                Width = 218,
                BackColor = Color.FromArgb(241, 240, 236),
                Padding = new Padding(6, 8, 4, 8)
            };
            var thumbnails = new PdfThumbnailList
            {
                Dock = DockStyle.Fill
            };
            thumbnailHost.Controls.Add(thumbnails);

            var canvas = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(229, 228, 224),
                Padding = new Padding(50, 28, 50, 28)
            };
            var sheet = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Padding = new Padding(42)
            };
            var titleLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 72,
                Text = "PROYECTO BÁSICO · DOCUMENTACIÓN",
                Font = new Font("Bahnschrift", 16f, FontStyle.Regular),
                ForeColor = Color.FromArgb(38, 40, 42),
                TextAlign = ContentAlignment.BottomLeft
            };
            var rule = new Panel
            {
                Dock = DockStyle.Top,
                Height = 2,
                BackColor = Color.FromArgb(239, 101, 78)
            };
            var subtitle = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 48,
                Text = "VISTA PREVIA DE LA LÁMINA ACTIVA",
                Font = new Font("Bahnschrift", 8.5f, FontStyle.Regular),
                ForeColor = Color.FromArgb(108, 108, 103),
                TextAlign = ContentAlignment.MiddleLeft
            };
            sheet.Controls.Add(subtitle);
            sheet.Controls.Add(rule);
            sheet.Controls.Add(titleLabel);
            canvas.Controls.Add(sheet);

            page.Controls.Add(canvas);
            page.Controls.Add(thumbnailHost);

            if (!string.IsNullOrEmpty(pdfPath) && File.Exists(pdfPath))
            {
                thumbnails.LoadDocument(PdfDocument.Load(pdfPath), true);
                thumbnails.SetActivePage(1, true);
            }

            return page;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Windows.Forms;
using PdfiumViewer;

namespace FirmaAutomatica
{
    internal static class TextEditUiQa
    {
        private static readonly List<string> Report =
            new List<string>();

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length != 2)
            {
                Console.Error.WriteLine(
                    "Uso: TextEditUiQa <fixture-pdf> <carpeta-salida>");
                return 2;
            }

            var fixture = Path.GetFullPath(args[0]);
            var output = Path.GetFullPath(args[1]);
            Directory.CreateDirectory(output);

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                TestSelectionController(fixture, output);
                TestDialog(output);
                TestScaledDialog(output, 1.25f);
                TestScaledDialog(output, 1.50f);
                Report.Add("RESULTADO: PASS");
                File.WriteAllLines(
                    Path.Combine(output, "qa-report.txt"),
                    Report.ToArray(),
                    Encoding.UTF8);
                Console.WriteLine(string.Join(
                    Environment.NewLine,
                    Report.ToArray()));
                return 0;
            }
            catch (Exception ex)
            {
                Report.Add("RESULTADO: FAIL");
                Report.Add(ex.ToString());
                File.WriteAllLines(
                    Path.Combine(output, "qa-report.txt"),
                    Report.ToArray(),
                    Encoding.UTF8);
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        private static void TestSelectionController(
            string fixture,
            string output)
        {
            using (var document = PdfDocument.Load(fixture))
            using (var form = new Form())
            using (var renderer = new PdfRenderer())
            {
                form.ClientSize = new Size(820, 620);
                renderer.Dock = DockStyle.Fill;
                renderer.Load(document);
                renderer.ZoomMode = PdfViewerZoomMode.FitWidth;
                form.Controls.Add(renderer);
                form.Show();
                Pump(180);

                var unrelatedMarker = new PdfMarker(
                    0,
                    new RectangleF(12, 12, 18, 12),
                    Color.FromArgb(30, Color.Gold),
                    Color.Transparent,
                    0f);
                renderer.Markers.Add(unrelatedMarker);

                using (var controller =
                    new PdfTextEditSelectionController(
                        renderer,
                        delegate { return true; },
                        Color.FromArgb(238, 91, 61),
                        Color.FromArgb(185, 68, 45),
                        Color.FromArgb(250, 249, 247)))
                {
                    Require(controller.Activate(), "Selector activado.");
                    var pageSize = document.PageSizes[0];
                    var pageBounds = renderer.BoundsFromPdf(
                        new PdfRectangle(
                            0,
                            new RectangleF(
                                0,
                                0,
                                pageSize.Width,
                                pageSize.Height)));
                    var start = new Point(
                        pageBounds.Left + pageBounds.Width / 5,
                        pageBounds.Top + pageBounds.Height / 5);
                    var finish = new Point(
                        pageBounds.Left + (pageBounds.Width * 3 / 5),
                        pageBounds.Top + (pageBounds.Height * 2 / 5));

                    var previousCursorPosition = Cursor.Position;
                    Cursor.Position = renderer.PointToScreen(start);
                    controller.Deactivate();
                    Require(
                        controller.Activate() &&
                        controller.IsPrecisionCursorApplied,
                        "Cruceta precisa aplicada sobre el lienzo.");
                    Cursor.Position = previousCursorPosition;

                    Require(
                        controller.BeginSelection(start),
                        "Selección iniciada dentro de la página.");
                    Require(
                        controller.UpdateSelection(finish),
                        "Selección actualizada.");
                    Require(
                        controller.CompleteSelection(finish),
                        "Selección completada.");
                    Require(
                        controller.HasSelection &&
                        controller.Selection.Value.Page == 0,
                        "Selección limitada a una página.");
                    Require(
                        renderer.Markers.Count == 2,
                        "Marcador propio convive con otro marcador.");
                    Require(
                        controller.AcceptButton.Visible,
                        "Confirmación central visible.");
                    Pump(40);
                    SaveWindow(
                        form,
                        Path.Combine(output, "selection-100.png"));

                    var accepted = 0;
                    controller.SelectionAccepted += delegate(
                        object sender,
                        PdfTextEditSelectionEventArgs e)
                    {
                        if (e.PageIndex == 0 && e.Bounds.Width > 0)
                        {
                            accepted++;
                        }
                    };
                    controller.AcceptButton.PerformClick();
                    Require(
                        accepted == 1,
                        "La confirmación publica la región exacta.");

                    controller.Cancel();
                    Require(
                        !controller.IsActive &&
                        !controller.HasSelection,
                        "Cancelar desactiva y limpia la selección.");
                    Require(
                        renderer.Markers.Count == 1 &&
                        renderer.Markers.Contains(unrelatedMarker),
                        "Cancelar conserva marcadores ajenos.");

                    Require(
                        controller.Activate(),
                        "Selector reactivado para probar Escape.");
                    var escapeMessage = Message.Create(
                        renderer.Handle,
                        0x0100,
                        new IntPtr((int)Keys.Escape),
                        IntPtr.Zero);
                    Require(
                        controller.PreFilterMessage(ref escapeMessage) &&
                        !controller.IsActive,
                        "Escape cancela y consume el modo de edición.");
                }

                renderer.Markers.Remove(unrelatedMarker);
            }

            Report.Add("PASS · selector perezoso, una página y marcador propio");
        }

        private static void TestDialog(string output)
        {
            var state = new PdfTextEditDialogState
            {
                Text =
                    "Texto detectado en el plano\r\n" +
                    "Segunda línea con acentos: Málaga y medición.",
                BaseFontName = PdfTextEditDialogState.HelveticaFontName,
                FontSizePoints = 11M,
                AutoFit = true,
                Alignment = HorizontalAlignment.Left,
                CoverBackground = true,
                TextColor = Color.FromArgb(32, 61, 92),
                CoverColor = Color.FromArgb(255, 252, 235)
            };

            using (var form = new PdfTextEditDialog(
                state,
                "Página 1 · 82 × 24 mm"))
            {
                form.Show();
                Pump(100);
                RequireChildrenInside(form);
                Require(
                    form.ApplyButtonForTesting.Enabled,
                    "Aplicar habilitado con un reemplazo válido.");
                SaveWindow(form, Path.Combine(output, "dialog-100.png"));

                form.FontSelectorForTesting.SelectedItem =
                    PdfTextEditDialogState.TimesFontName;
                form.FontSizeInputForTesting.Value = 13.5M;
                form.AutoFitCheckBoxForTesting.Checked = false;
                form.AlignmentSelectorForTesting.SelectedIndex = 1;
                form.CoverBackgroundCheckBoxForTesting.Checked = false;
                form.TextColorButtonForTesting.BackColor =
                    Color.FromArgb(128, 32, 48);
                form.CoverColorButtonForTesting.BackColor =
                    Color.FromArgb(235, 244, 250);
                form.TextInputForTesting.Text = "Reemplazo centrado";
                Pump(40);
                form.ApplyButtonForTesting.PerformClick();

                Require(
                    form.DialogResult == DialogResult.OK &&
                    form.Result != null,
                    "El diálogo devuelve un resultado al aplicar.");
                Require(
                    form.Result.BaseFontName ==
                        PdfTextEditDialogState.TimesFontName &&
                    form.Result.FontSizePoints == 13.5M &&
                    !form.Result.AutoFit &&
                    form.Result.Alignment == HorizontalAlignment.Center &&
                    !form.Result.CoverBackground &&
                    form.Result.TextColor.ToArgb() ==
                        Color.FromArgb(128, 32, 48).ToArgb() &&
                    form.Result.CoverColor.ToArgb() ==
                        Color.FromArgb(235, 244, 250).ToArgb(),
                    "Fuente, tamaño, autofit, alineación y colores conservados.");
            }

            using (var invalid = new PdfTextEditDialog(
                new PdfTextEditDialogState
                {
                    Text = string.Empty,
                    CoverBackground = false
                }))
            {
                invalid.Show();
                Pump(50);
                Require(
                    !invalid.ApplyButtonForTesting.Enabled,
                    "No se aplica una operación vacía.");
            }

            Report.Add("PASS · diálogo compacto y modelo transaccional");
        }

        private static void TestScaledDialog(
            string output,
            float scale)
        {
            using (var form = new PdfTextEditDialog(
                new PdfTextEditDialogState
                {
                    Text =
                        "Texto largo para comprobar controles, vista previa " +
                        "y botones en una pantalla con escala ampliada.",
                    FontSizePoints = 12M,
                    AutoFit = true,
                    CoverBackground = true
                },
                "Página 2 · escala " +
                    ((int)Math.Round(scale * 100f)).ToString() + "%"))
            {
                form.Scale(new SizeF(scale, scale));
                form.ClientSize = new Size(
                    (int)Math.Round(640f * scale),
                    (int)Math.Round(530f * scale));
                form.Show();
                Pump(80);
                RequireChildrenInside(form);

                var cancel = form.CancelButton as Control;
                var accept = form.AcceptButton as Control;
                Require(
                    cancel != null &&
                    accept != null &&
                    form.ClientRectangle.Contains(cancel.Bounds) &&
                    form.ClientRectangle.Contains(accept.Bounds),
                    "Botones completos al " +
                        ((int)Math.Round(scale * 100f)).ToString() + "%.");

                SaveWindow(
                    form,
                    Path.Combine(
                        output,
                        "dialog-" +
                        ((int)Math.Round(scale * 100f)).ToString() +
                        ".png"));
            }
        }

        private static void RequireChildrenInside(Form form)
        {
            foreach (Control control in form.Controls)
            {
                if (!form.ClientRectangle.Contains(control.Bounds))
                {
                    throw new InvalidOperationException(
                        "Control fuera del diálogo: " +
                        control.GetType().Name + " " + control.Bounds);
                }
            }
        }

        private static void SaveWindow(Form form, string path)
        {
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(
                    bitmap,
                    new Rectangle(Point.Empty, form.Size));
                bitmap.Save(path, ImageFormat.Png);
            }
        }

        private static void Pump(int milliseconds)
        {
            var until = Environment.TickCount + milliseconds;
            do
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(5);
            }
            while (Environment.TickCount < until);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }

            Report.Add("PASS · " + message);
        }
    }
}

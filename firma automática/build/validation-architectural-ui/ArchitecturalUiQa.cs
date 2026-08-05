using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace FirmaAutomatica
{
    internal static class ArchitecturalUiQa
    {
        private static readonly string OutputDirectory = Path.GetFullPath(
            Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "captures"));

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Se esperaban tres PDF de prueba.");
                return 2;
            }

            Directory.CreateDirectory(OutputDirectory);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var failures = new List<string>();
            CaptureEmptyStates(failures);
            CaptureStandardStates(args, failures);
            CaptureScaledState(args, 1.25f, failures);
            CaptureScaledState(args, 1.50f, failures);

            var reportPath = Path.Combine(OutputDirectory, "qa-report.txt");
            File.WriteAllLines(
                reportPath,
                failures.Count == 0
                    ? new[] { "PASS: no se detectaron solapes técnicos." }
                    : failures.ToArray());

            Console.WriteLine("captures=" + OutputDirectory);
            Console.WriteLine("failures=" + failures.Count);
            foreach (var failure in failures)
            {
                Console.WriteLine(failure);
            }

            return failures.Count == 0 ? 0 : 1;
        }

        private static void CaptureEmptyStates(IList<string> failures)
        {
            using (var form = CreateForm(new string[0]))
            {
                form.ClientSize = new Size(1220, 840);
                Pump(250);
                ValidateEmptyState(form, "empty-1220x840", failures);
                SaveClient(form, "00-empty-1220x840.png");

                form.ClientSize = new Size(900, 620);
                Pump(200);
                ValidateEmptyState(form, "empty-900x620", failures);
                SaveClient(form, "00-empty-900x620.png");
            }
        }

        private static void CaptureStandardStates(
            string[] args,
            IList<string> failures)
        {
            using (var form = CreateForm(args))
            {
                form.ClientSize = new Size(1220, 840);
                Pump(350);
                NavigateToSecondPage(form);
                Pump(250);
                ValidateLayout(form, "1220x840", failures);
                SaveClient(form, "01-default-1220x840.png");

                HoverToolButton(form, "searchToolButton");
                Pump(100);
                SaveClient(form, "02-hover-search-1220x840.png");
                LeaveToolButton(form, "searchToolButton");

                HoverSecondTab(form);
                Pump(100);
                SaveClient(form, "02-hover-inactive-tab-1220x840.png");
                LeaveTabs(form);

                form.ClientSize = new Size(900, 620);
                Pump(250);
                ValidateLayout(form, "900x620", failures);
                SaveClient(form, "03-compact-900x620.png");

                InvokePrivate(form, "ShowSearchPanel");
                Pump(150);
                var searchTextBox = GetField<TextBox>(form, "searchTextBox");
                searchTextBox.Text = "pagina";
                Pump(100);
                SaveClient(form, "04-search-before-enter-900x620.png");

                InvokePrivate(
                    form,
                    "SearchTextBox_KeyDown",
                    searchTextBox,
                    new KeyEventArgs(Keys.Enter));
                Pump(300);
                ValidateLayout(form, "search-900x620", failures);
                SaveClient(form, "05-search-results-900x620.png");

                var workspace = GetField<object>(form, "activeWorkspace");
                var collapseButton = (Button)GetWorkspaceField(
                    workspace,
                    "CollapseNavigationButton");
                collapseButton.PerformClick();
                Pump(180);
                ValidateLayout(form, "collapsed-900x620", failures);
                SaveClient(form, "06-navigation-collapsed-900x620.png");
            }
        }

        private static void CaptureScaledState(
            string[] args,
            float scale,
            IList<string> failures)
        {
            using (var form = CreateForm(args))
            {
                form.Scale(new SizeF(scale, scale));
                form.ClientSize = new Size(
                    (int)Math.Round(900 * scale),
                    (int)Math.Round(620 * scale));
                Pump(350);

                var tabs = GetField<Control>(form, "documentTabs");
                var dpiScaleField = tabs.GetType().GetField(
                    "dpiScale",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var applyDpiMetrics = tabs.GetType().GetMethod(
                    "ApplyDpiMetrics",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (dpiScaleField != null && applyDpiMetrics != null)
                {
                    dpiScaleField.SetValue(tabs, scale);
                    applyDpiMetrics.Invoke(tabs, null);
                }

                var suffix = ((int)Math.Round(scale * 100)).ToString();
                ValidateLayout(form, "scaled-" + suffix, failures);
                SaveClient(
                    form,
                    "07-scaled-" + suffix + "-compact.png");

                var workspace = GetField<object>(form, "activeWorkspace");
                var collapseButton = (Button)GetWorkspaceField(
                    workspace,
                    "CollapseNavigationButton");
                collapseButton.PerformClick();
                Pump(180);

                var navigationPanel = (Panel)GetWorkspaceField(
                    workspace,
                    "NavigationPanel");
                var minimumExpectedCollapsedWidth =
                    (int)Math.Round(30f * scale);
                if (navigationPanel.Width < minimumExpectedCollapsedWidth)
                {
                    failures.Add(
                        "FAIL scaled-" + suffix +
                        ": panel plegado=" + navigationPanel.Width +
                        " px; esperado al menos " +
                        minimumExpectedCollapsedWidth + " px.");
                }

                ValidateLayout(
                    form,
                    "scaled-" + suffix + "-collapsed",
                    failures);
                SaveClient(
                    form,
                    "08-scaled-" + suffix + "-collapsed.png");
            }
        }

        private static PdfViewerForm CreateForm(string[] args)
        {
            var form = new PdfViewerForm(args);
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-30000, -30000);
            form.ShowInTaskbar = false;
            form.Show();
            Pump(1250);
            return form;
        }

        private static void NavigateToSecondPage(PdfViewerForm form)
        {
            var nextPageButton = GetField<Button>(
                form,
                "nextPageButton");
            if (nextPageButton.Enabled)
            {
                nextPageButton.PerformClick();
            }
        }

        private static void HoverToolButton(
            PdfViewerForm form,
            string fieldName)
        {
            var button = GetField<Button>(form, fieldName);
            var onMouseEnter = typeof(ButtonBase).GetMethod(
                "OnMouseEnter",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (onMouseEnter != null)
            {
                onMouseEnter.Invoke(button, new object[] { EventArgs.Empty });
            }
        }

        private static void LeaveToolButton(
            PdfViewerForm form,
            string fieldName)
        {
            var button = GetField<Button>(form, fieldName);
            InvokeMouseLifecycle(button, "OnMouseLeave", EventArgs.Empty);
        }

        private static void HoverSecondTab(PdfViewerForm form)
        {
            var tabs = GetField<TabControl>(form, "documentTabs");
            if (tabs.TabCount < 2)
            {
                return;
            }

            var bounds = tabs.GetTabRect(1);
            InvokeMouseLifecycle(
                tabs,
                "OnMouseMove",
                new MouseEventArgs(
                    MouseButtons.None,
                    0,
                    bounds.Left + Math.Max(2, bounds.Width / 2),
                    bounds.Top + Math.Max(2, bounds.Height / 2),
                    0));
        }

        private static void LeaveTabs(PdfViewerForm form)
        {
            var tabs = GetField<TabControl>(form, "documentTabs");
            InvokeMouseLifecycle(tabs, "OnMouseLeave", EventArgs.Empty);
        }

        private static void InvokeMouseLifecycle(
            Control control,
            string methodName,
            EventArgs eventArgs)
        {
            var method = control.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (method != null)
            {
                method.Invoke(control, new object[] { eventArgs });
            }
        }

        private static void ValidateLayout(
            PdfViewerForm form,
            string state,
            IList<string> failures)
        {
            var header = GetField<Control>(form, "headerPanel");
            var content = GetField<Control>(form, "contentPanel");
            var tabs = GetField<Control>(form, "documentTabs");
            var rail = GetField<Control>(form, "toolRail");
            var currentPage = GetField<Control>(form, "currentPageTextBox");
            var previous = GetField<Control>(form, "previousPageButton");
            var next = GetField<Control>(form, "nextPageButton");

            RequireInside(form, header, state, "headerPanel", failures);
            RequireInside(form, content, state, "contentPanel", failures);
            RequireInside(form, tabs, state, "documentTabs", failures);
            RequireInside(form, rail, state, "toolRail", failures);

            if (currentPage.Bounds.IntersectsWith(previous.Bounds) ||
                currentPage.Bounds.IntersectsWith(next.Bounds))
            {
                failures.Add(
                    "FAIL " + state +
                    ": el editor de página se solapa con navegación.");
            }

            var searchPanel = GetField<Control>(form, "searchPanel");
            if (searchPanel.Visible)
            {
                var searchText = GetField<Control>(form, "searchTextBox");
                var status = GetField<Control>(form, "searchStatusLabel");
                var searchPrevious = GetField<Control>(
                    form,
                    "searchPreviousButton");
                var searchNext = GetField<Control>(
                    form,
                    "searchNextButton");
                var searchClose = GetField<Control>(
                    form,
                    "searchCloseButton");
                var controls = new[]
                {
                    searchText,
                    status,
                    searchPrevious,
                    searchNext,
                    searchClose
                };

                for (var index = 0; index < controls.Length; index++)
                {
                    RequireInside(
                        searchPanel,
                        controls[index],
                        state,
                        controls[index].Name,
                        failures);
                }

                for (var left = 0; left < controls.Length; left++)
                {
                    for (var right = left + 1;
                        right < controls.Length;
                        right++)
                    {
                        if (controls[left].Bounds.IntersectsWith(
                            controls[right].Bounds))
                        {
                            failures.Add(
                                "FAIL " + state +
                                ": controles de búsqueda solapados.");
                        }
                    }
                }
            }

            var workspace = GetField<object>(form, "activeWorkspace");
            if (workspace == null)
            {
                failures.Add(
                    "FAIL " + state + ": no hay pestaña activa.");
                return;
            }

            var navigationPanel = (Control)GetWorkspaceField(
                workspace,
                "NavigationPanel");
            var viewer = (Control)GetWorkspaceField(workspace, "Viewer");
            RequireInside(
                tabs,
                navigationPanel,
                state,
                "NavigationPanel",
                failures);
            RequireInside(tabs, viewer, state, "Viewer", failures);

            if (viewer.ClientSize.Width < 320 ||
                viewer.ClientSize.Height < 300)
            {
                failures.Add(
                    "FAIL " + state +
                    ": visor demasiado pequeño (" +
                    viewer.ClientSize.Width + "x" +
                    viewer.ClientSize.Height + ").");
            }
        }

        private static void ValidateEmptyState(
            PdfViewerForm form,
            string state,
            IList<string> failures)
        {
            var emptyPanel = GetField<Control>(form, "emptyPanel");
            var title = GetField<Control>(form, "emptyTitleLabel");
            var body = GetField<Control>(form, "emptyBodyLabel");
            var open = GetField<Button>(form, "emptyOpenButton");
            var search = GetField<Button>(form, "searchToolButton");
            var sign = GetField<Button>(form, "signToolButton");

            if (!emptyPanel.Visible)
            {
                failures.Add(
                    "FAIL " + state +
                    ": el estado vacío no está visible.");
            }

            RequireInside(emptyPanel, title, state, "emptyTitleLabel", failures);
            RequireInside(emptyPanel, body, state, "emptyBodyLabel", failures);
            RequireInside(emptyPanel, open, state, "emptyOpenButton", failures);

            if (!open.Enabled)
            {
                failures.Add(
                    "FAIL " + state +
                    ": Abrir PDF debería estar habilitado.");
            }

            if (search.Enabled || sign.Enabled)
            {
                failures.Add(
                    "FAIL " + state +
                    ": hay herramientas de documento habilitadas sin PDF.");
            }
        }

        private static void RequireInside(
            Control parent,
            Control child,
            string state,
            string name,
            IList<string> failures)
        {
            var parentBounds = parent.ClientRectangle;
            var childBounds = parent == child.Parent
                ? child.Bounds
                : parent.RectangleToClient(
                    child.Parent.RectangleToScreen(child.Bounds));
            if (!parentBounds.IntersectsWith(childBounds))
            {
                failures.Add(
                    "FAIL " + state + ": " + name +
                    " queda fuera del contenedor.");
            }
        }

        private static void SaveClient(
            PdfViewerForm form,
            string fileName)
        {
            using (var bitmap = new Bitmap(
                form.ClientSize.Width,
                form.ClientSize.Height,
                PixelFormat.Format32bppArgb))
            {
                form.DrawToBitmap(
                    bitmap,
                    new Rectangle(Point.Empty, form.ClientSize));
                bitmap.Save(
                    Path.Combine(OutputDirectory, fileName),
                    ImageFormat.Png);
            }
        }

        private static T GetField<T>(object target, string name)
        {
            var field = target.GetType().GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            return (T)field.GetValue(target);
        }

        private static object GetWorkspaceField(
            object workspace,
            string name)
        {
            var field = workspace.GetType().GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public);
            return field.GetValue(workspace);
        }

        private static object InvokePrivate(
            object target,
            string name,
            params object[] arguments)
        {
            var method = target.GetType().GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            return method.Invoke(target, arguments);
        }

        private static void Pump(int milliseconds)
        {
            var deadline = Environment.TickCount + milliseconds;
            while (Environment.TickCount < deadline)
            {
                Application.DoEvents();
                Thread.Sleep(15);
            }
        }
    }
}

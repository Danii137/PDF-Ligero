using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;
using iTextSharp.text;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
using PdfiumViewer;
using DrawingRectangle = System.Drawing.Rectangle;
using IOPath = System.IO.Path;
using PdfiumDocument = PdfiumViewer.PdfDocument;

namespace FirmaAutomatica
{
    internal static class OrganizerUiQa
    {
        private static readonly string ValidationDirectory =
            IOPath.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
        private static readonly string CapturesDirectory =
            IOPath.Combine(ValidationDirectory, "captures");
        private static readonly List<string> Results = new List<string>();
        private static readonly List<string> Failures = new List<string>();

        [STAThread]
        private static int Main()
        {
            Directory.CreateDirectory(CapturesDirectory);
            var fixturePath = IOPath.Combine(
                ValidationDirectory,
                "organizer-five-pages.pdf");
            var recoveryRoot = IOPath.Combine(
                ValidationDirectory,
                "recovery");

            TryDeleteDirectory(recoveryRoot);
            Directory.CreateDirectory(recoveryRoot);
            Environment.SetEnvironmentVariable(
                PdfEditSession.RecoveryRootOverrideEnvironmentVariable,
                recoveryRoot);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                CreateFixture(fixturePath);
                RunSection(
                    "COMPONENTE MINIATURAS",
                    delegate
                    {
                        ValidateThumbnailComponent(fixturePath);
                    });
                RunSection(
                    "ESTRUCTURAS PDF AVANZADAS",
                    ValidateAdvancedPdfStructures);
                RunSection(
                    "CAMBIO DE ORIGEN MISMO TAMAÑO",
                    ValidateSameSizeSourceMutation);
                RunSection(
                    "INTEGRACION VISOR + HISTORIAL",
                    delegate
                    {
                        ValidateViewerIntegration(
                            fixturePath,
                            recoveryRoot);
                    });
            }
            catch (Exception ex)
            {
                Failures.Add(
                    "FAIL GLOBAL: " + FormatException(ex));
            }

            var report = new List<string>();
            report.Add(
                Failures.Count == 0
                    ? "PASS: organizador de páginas validado."
                    : "FAIL: se detectaron incidencias en el organizador.");
            report.Add(
                "Fecha UTC: " +
                DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") +
                "Z");
            report.Add(
                "Fixture: " + fixturePath);
            report.Add(string.Empty);
            report.AddRange(Results);
            if (Failures.Count > 0)
            {
                report.Add(string.Empty);
                report.Add("FALLOS CONCRETOS");
                report.AddRange(Failures);
            }

            var reportPath = IOPath.Combine(
                ValidationDirectory,
                "qa-report.txt");
            File.WriteAllLines(reportPath, report.ToArray());

            Console.WriteLine(
                Failures.Count == 0
                    ? "PASS"
                    : "FAIL");
            Console.WriteLine("report=" + reportPath);
            Console.WriteLine(
                "captures=" + CapturesDirectory);
            Console.WriteLine(
                "failures=" + Failures.Count);
            foreach (var failure in Failures)
            {
                Console.WriteLine(failure);
            }

            return Failures.Count == 0 ? 0 : 1;
        }

        private static void RunSection(
            string name,
            Action action)
        {
            try
            {
                action();
                Results.Add("PASS " + name);
            }
            catch (Exception ex)
            {
                Failures.Add(
                    "FAIL " + name + ": " +
                    FormatException(ex));
            }
        }

        private static void CaptureFailure(
            IList<string> failures,
            string caseName,
            Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failures.Add(
                    caseName +
                    ": " +
                    ex.GetBaseException().Message);
            }
        }

        private static void ValidateThumbnailComponent(
            string fixturePath)
        {
            using (var document =
                PdfiumDocument.Load(fixturePath))
            using (var host = new Form())
            {
                host.StartPosition =
                    FormStartPosition.Manual;
                host.Location =
                    new Point(-30000, -30000);
                host.ShowInTaskbar = false;
                host.ClientSize = new Size(290, 910);

                var thumbnails = new PdfThumbnailList
                {
                    Dock = DockStyle.Fill
                };
                host.Controls.Add(thumbnails);
                host.Show();
                thumbnails.LoadDocument(document);
                Pump(250);

                Require(
                    thumbnails.PageCount == 5,
                    "La lista no cargó las cinco páginas.");
                RequireSelection(
                    thumbnails,
                    new[] { 0 },
                    "selección inicial");

                // Estas dos funciones privadas son exactamente las ramas que
                // OnMouseDown utiliza al detectar Ctrl y Shift.
                InvokePrivate(
                    thumbnails,
                    "ToggleSelectedPage",
                    2,
                    true);
                RequireSelection(
                    thumbnails,
                    new[] { 0, 2 },
                    "Ctrl+clic para añadir");
                Require(
                    thumbnails.SelectedPage == 2,
                    "Ctrl+clic no dejó activa la última página.");

                InvokePrivate(
                    thumbnails,
                    "ToggleSelectedPage",
                    0,
                    true);
                RequireSelection(
                    thumbnails,
                    new[] { 2 },
                    "Ctrl+clic para quitar");
                InvokePrivate(
                    thumbnails,
                    "ToggleSelectedPage",
                    2,
                    true);
                RequireSelection(
                    thumbnails,
                    new[] { 2 },
                    "Ctrl+clic no debe dejar una selección vacía");

                thumbnails.SelectedPage = 1;
                InvokePrivate(
                    thumbnails,
                    "SelectRange",
                    4,
                    false,
                    true);
                RequireSelection(
                    thumbnails,
                    new[] { 1, 2, 3, 4 },
                    "Shift+clic");
                Require(
                    thumbnails.SelectedPage == 4,
                    "Shift+clic no dejó activo el extremo del rango.");

                thumbnails.SetSelectedPages(
                    new[] { 0, 4 },
                    4,
                    false);
                SetPrivateField(
                    thumbnails,
                    "selectionAnchorPage",
                    0);
                InvokePrivate(
                    thumbnails,
                    "SelectRange",
                    2,
                    true,
                    true);
                RequireSelection(
                    thumbnails,
                    new[] { 0, 1, 2, 4 },
                    "Ctrl+Shift+clic");

                var message = new Message();
                object[] commandArguments =
                {
                    message,
                    Keys.Control | Keys.A
                };
                var handled = (bool)InvokeMethod(
                    thumbnails,
                    "ProcessCmdKey",
                    commandArguments);
                Require(
                    handled,
                    "Ctrl+A no fue consumido por la lista.");
                RequireSelection(
                    thumbnails,
                    new[] { 0, 1, 2, 3, 4 },
                    "Ctrl+A");

                var selectionBeforeActivePage =
                    thumbnails.SelectedPages.ToArray();
                var focusedSelectionPage =
                    thumbnails.SelectedPage;
                thumbnails.SetActivePage(3, false);
                RequireSelection(
                    thumbnails,
                    selectionBeforeActivePage,
                    "SetActivePage");
                Require(
                    thumbnails.SelectedPage ==
                        focusedSelectionPage,
                    "SetActivePage cambió la página focal de " +
                    "la multiselección.");
                Require(
                    GetPrivateField<int>(
                        thumbnails,
                        "activePage") == 3,
                    "SetActivePage no actualizó la página vista.");

                ValidatePageOperationEvents(
                    thumbnails);
                ValidateDragCoexistence(
                    thumbnails,
                    fixturePath);

                thumbnails.SetSelectedPages(
                    new[] { 0, 2, 4 },
                    2,
                    true);
                Pump(250);
                SaveControl(
                    thumbnails,
                    IOPath.Combine(
                        CapturesDirectory,
                        "01-miniaturas-multiseleccion.png"));

                thumbnails.ClearDocument();
            }
        }

        private static void ValidatePageOperationEvents(
            PdfThumbnailList thumbnails)
        {
            var events =
                new List<PdfThumbnailPageOperationRequestedEventArgs>();
            thumbnails.PageOperationRequested +=
                delegate(
                    object sender,
                    PdfThumbnailPageOperationRequestedEventArgs e)
                {
                    events.Add(e);
                };
            thumbnails.SetSelectedPages(
                new[] { 1, 3 },
                3,
                false);

            InvokePrivate(
                thumbnails,
                "RaisePageOperationRequested",
                PdfThumbnailPageOperation.RotateLeft);
            InvokePrivate(
                thumbnails,
                "RaisePageOperationRequested",
                PdfThumbnailPageOperation.RotateRight);
            InvokePrivate(
                thumbnails,
                "RaisePageOperationRequested",
                PdfThumbnailPageOperation.Delete);

            Require(
                events.Count == 3,
                "No se emitieron los tres eventos de operación.");
            Require(
                events[0].Operation ==
                    PdfThumbnailPageOperation.RotateLeft &&
                events[1].Operation ==
                    PdfThumbnailPageOperation.RotateRight &&
                events[2].Operation ==
                    PdfThumbnailPageOperation.Delete,
                "El orden/tipo de eventos de giro y borrado no coincide.");
            foreach (var operationEvent in events)
            {
                Require(
                    operationEvent.PageIndexes.SequenceEqual(
                        new[] { 1, 3 }) &&
                    operationEvent.ActivePageIndex == 3,
                    "Un evento no conservó selección y página activa.");
            }

            thumbnails.PageOperationsEnabled = false;
            InvokePrivate(
                thumbnails,
                "RaisePageOperationRequested",
                PdfThumbnailPageOperation.Delete);
            Require(
                events.Count == 3,
                "Una operación deshabilitada emitió un evento.");
            thumbnails.PageOperationsEnabled = true;
            Results.Add(
                "  PASS eventos: girar izquierda/derecha, " +
                "eliminar y bloqueo durante worker.");
        }

        private static void ValidateDragCoexistence(
            PdfThumbnailList thumbnails,
            string fixturePath)
        {
            var reorderEvents =
                new List<PdfThumbnailPagesReorderRequestedEventArgs>();
            var insertEvents =
                new List<PdfFilesInsertRequestedEventArgs>();
            var genericDropEvents = 0;
            thumbnails.PagesReorderRequested +=
                delegate(
                    object sender,
                    PdfThumbnailPagesReorderRequestedEventArgs e)
                {
                    reorderEvents.Add(e);
                };
            thumbnails.PdfFilesInsertRequested +=
                delegate(
                    object sender,
                    PdfFilesInsertRequestedEventArgs e)
                {
                    insertEvents.Add(e);
                };
            thumbnails.DragDrop +=
                delegate
                {
                    genericDropEvents++;
                };

            thumbnails.SetSelectedPages(
                new[] { 1, 3 },
                3,
                false);
            var generation = GetPrivateField<int>(
                thumbnails,
                "documentGeneration");
            var mixedData = new DataObject();
            mixedData.SetData(
                "PDFLigero.InternalPageSelection",
                false,
                new PdfThumbnailInternalDragData(
                    thumbnails,
                    generation,
                    thumbnails.SelectedPages));
            mixedData.SetData(
                DataFormats.FileDrop,
                new[] { fixturePath });
            var topPoint = thumbnails.PointToScreen(
                new Point(
                    Math.Max(
                        5,
                        thumbnails.ClientSize.Width / 2),
                    2));
            var mixedDrop = new DragEventArgs(
                mixedData,
                0,
                topPoint.X,
                topPoint.Y,
                DragDropEffects.Copy |
                    DragDropEffects.Move,
                DragDropEffects.None);

            InvokeProtected(
                thumbnails,
                "OnDragEnter",
                mixedDrop);
            Require(
                mixedDrop.Effect ==
                    DragDropEffects.Move,
                "El drag interno no tuvo prioridad sobre FileDrop.");
            InvokeProtected(
                thumbnails,
                "OnDragDrop",
                mixedDrop);
            Require(
                reorderEvents.Count == 1 &&
                reorderEvents[0].PageIndexes.SequenceEqual(
                    new[] { 1, 3 }) &&
                reorderEvents[0].InsertionPageIndex == 0,
                "El drag interno no emitió el reordenado esperado.");
            Require(
                insertEvents.Count == 0 &&
                genericDropEvents == 0,
                "El drag interno también se trató como PDF externo.");

            var externalData = new DataObject();
            externalData.SetData(
                DataFormats.FileDrop,
                new[] { fixturePath });
            var itemHeight = GetPrivateField<int>(
                thumbnails,
                "itemHeight");
            var externalClientPoint = new Point(
                Math.Max(
                    5,
                    thumbnails.ClientSize.Width / 2),
                8 + itemHeight);
            var expectedInsertion =
                (int)InvokePrivate(
                    thumbnails,
                    "GetInsertionPageIndex",
                    externalClientPoint);
            var externalScreenPoint =
                thumbnails.PointToScreen(
                    externalClientPoint);
            var externalDrop = new DragEventArgs(
                externalData,
                0,
                externalScreenPoint.X,
                externalScreenPoint.Y,
                DragDropEffects.Copy,
                DragDropEffects.None);

            InvokeProtected(
                thumbnails,
                "OnDragEnter",
                externalDrop);
            Require(
                externalDrop.Effect ==
                    DragDropEffects.Copy,
                "FileDrop PDF no se anunció como copia.");
            InvokeProtected(
                thumbnails,
                "OnDragDrop",
                externalDrop);
            Require(
                insertEvents.Count == 1 &&
                insertEvents[0].PdfFilePaths.Count == 1 &&
                string.Equals(
                    insertEvents[0].PdfFilePaths[0],
                    IOPath.GetFullPath(fixturePath),
                    StringComparison.OrdinalIgnoreCase) &&
                insertEvents[0].InsertionPageIndex ==
                    expectedInsertion,
                "FileDrop PDF no emitió la inserción esperada.");
            Require(
                reorderEvents.Count == 1 &&
                genericDropEvents == 0,
                "FileDrop PDF se propagó como reordenado o apertura genérica.");

            Results.Add(
                "  PASS drag: interno=Move, PDF externo=Copy, " +
                "sin doble apertura.");
        }

        private static void ValidateViewerIntegration(
            string fixturePath,
            string recoveryRoot)
        {
            var originalHash = ComputeSha256(fixturePath);
            PdfViewerForm form = null;
            object workspace = null;
            try
            {
                form = new PdfViewerForm(
                    new[] { fixturePath });
                form.StartPosition =
                    FormStartPosition.Manual;
                form.Location =
                    new Point(-30000, -30000);
                form.ShowInTaskbar = false;
                form.ClientSize =
                    new Size(1120, 780);
                form.Show();

                PumpUntil(
                    delegate
                    {
                        var workspaces =
                            GetField<IList>(
                                form,
                                "workspaces");
                        if (workspaces.Count != 1)
                        {
                            return false;
                        }

                        var candidate = workspaces[0];
                        return GetWorkspaceField<bool>(
                            candidate,
                            "IsLoaded");
                    },
                    15000,
                    "El visor no terminó de abrir el fixture.");

                workspace =
                    GetField<IList>(
                        form,
                        "workspaces")[0];
                var thumbnails =
                    (PdfThumbnailList)GetWorkspaceField(
                        workspace,
                        "Thumbnails");
                var session =
                    (PdfEditSession)GetWorkspaceField(
                        workspace,
                        "EditSession");
                Require(
                    session != null,
                    "El visor no creó sesión de edición.");
                AssertWorkspacePdf(
                    workspace,
                    new[] { "QA-A", "QA-B", "QA-C", "QA-D", "QA-E" },
                    new[] { 0, 0, 0, 0, 0 },
                    "estado inicial");

                var rotateWatch =
                    Stopwatch.StartNew();
                thumbnails.SetSelectedPages(
                    new[] { 1, 3 },
                    3,
                    false);
                InvokePrivate(
                    thumbnails,
                    "RaisePageOperationRequested",
                    PdfThumbnailPageOperation.RotateRight);
                WaitForPageOrganization(
                    form,
                    "El giro no terminó.");
                rotateWatch.Stop();
                AssertWorkspacePdf(
                    workspace,
                    new[] { "QA-A", "QA-B", "QA-C", "QA-D", "QA-E" },
                    new[] { 0, 90, 0, 90, 0 },
                    "giro múltiple");
                RequireSelection(
                    thumbnails,
                    new[] { 1, 3 },
                    "selección tras giro");
                Require(
                    session.CanUndo &&
                    !session.CanRedo &&
                    session.HasUnsavedChanges,
                    "El giro no dejó historial recuperable.");
                Results.Add(
                    "  PERF giro de 2 páginas: " +
                    rotateWatch.ElapsedMilliseconds +
                    " ms.");

                InvokePrivate(
                    form,
                    "UndoActiveDocument");
                Pump(300);
                AssertWorkspacePdf(
                    workspace,
                    new[] { "QA-A", "QA-B", "QA-C", "QA-D", "QA-E" },
                    new[] { 0, 0, 0, 0, 0 },
                    "Undo del giro");
                Require(
                    !session.CanUndo &&
                    session.CanRedo &&
                    !session.HasUnsavedChanges,
                    "Undo del giro no restauró el punto inicial.");

                InvokePrivate(
                    form,
                    "RedoActiveDocument");
                Pump(300);
                AssertWorkspacePdf(
                    workspace,
                    new[] { "QA-A", "QA-B", "QA-C", "QA-D", "QA-E" },
                    new[] { 0, 90, 0, 90, 0 },
                    "Redo del giro");

                var reorderWatch =
                    Stopwatch.StartNew();
                thumbnails.SetSelectedPages(
                    new[] { 0 },
                    0,
                    false);
                InvokePrivate(
                    form,
                    "BeginPdfPageReorder",
                    workspace,
                    new PdfThumbnailPagesReorderRequestedEventArgs(
                        new[] { 0 },
                        5));
                WaitForPageOrganization(
                    form,
                    "El reordenado no terminó.");
                reorderWatch.Stop();
                AssertWorkspacePdf(
                    workspace,
                    new[] { "QA-B", "QA-C", "QA-D", "QA-E", "QA-A" },
                    new[] { 90, 0, 90, 0, 0 },
                    "reordenado al final");
                RequireSelection(
                    thumbnails,
                    new[] { 4 },
                    "selección tras reordenar");
                Results.Add(
                    "  PERF reordenado: " +
                    reorderWatch.ElapsedMilliseconds +
                    " ms.");

                InvokePrivate(
                    form,
                    "UndoActiveDocument");
                Pump(300);
                AssertWorkspacePdf(
                    workspace,
                    new[] { "QA-A", "QA-B", "QA-C", "QA-D", "QA-E" },
                    new[] { 0, 90, 0, 90, 0 },
                    "Undo del reordenado");
                InvokePrivate(
                    form,
                    "RedoActiveDocument");
                Pump(300);
                AssertWorkspacePdf(
                    workspace,
                    new[] { "QA-B", "QA-C", "QA-D", "QA-E", "QA-A" },
                    new[] { 90, 0, 90, 0, 0 },
                    "Redo del reordenado");

                var deleteWatch =
                    Stopwatch.StartNew();
                var deletePlan =
                    new List<PdfPageOrganizerPage>
                    {
                        new PdfPageOrganizerPage(1, 0),
                        new PdfPageOrganizerPage(2, 0),
                        new PdfPageOrganizerPage(4, 0),
                        new PdfPageOrganizerPage(5, 0)
                    };
                InvokePrivate(
                    form,
                    "BeginPdfPageOrganization",
                    workspace,
                    deletePlan,
                    2,
                    new[] { 2 },
                    "Página eliminada / QA",
                    "Eliminando página / QA…",
                    false);
                WaitForPageOrganization(
                    form,
                    "La eliminación no terminó.");
                deleteWatch.Stop();
                AssertWorkspacePdf(
                    workspace,
                    new[] { "QA-B", "QA-C", "QA-E", "QA-A" },
                    new[] { 90, 0, 0, 0 },
                    "eliminación");
                RequireSelection(
                    thumbnails,
                    new[] { 2 },
                    "selección tras eliminar");
                Results.Add(
                    "  PERF eliminación: " +
                    deleteWatch.ElapsedMilliseconds +
                    " ms.");

                SaveControl(
                    form,
                    IOPath.Combine(
                        CapturesDirectory,
                        "02-visor-tras-organizar.png"));

                InvokePrivate(
                    form,
                    "UndoActiveDocument");
                Pump(300);
                AssertWorkspacePdf(
                    workspace,
                    new[] { "QA-B", "QA-C", "QA-D", "QA-E", "QA-A" },
                    new[] { 90, 0, 90, 0, 0 },
                    "Undo de eliminación");
                InvokePrivate(
                    form,
                    "RedoActiveDocument");
                Pump(300);
                AssertWorkspacePdf(
                    workspace,
                    new[] { "QA-B", "QA-C", "QA-E", "QA-A" },
                    new[] { 90, 0, 0, 0 },
                    "Redo de eliminación");

                Require(
                    string.Equals(
                        originalHash,
                        ComputeSha256(fixturePath),
                        StringComparison.Ordinal),
                    "Las operaciones modificaron el PDF original.");
                Require(
                    session.HasUnsavedChanges &&
                    session.CanUndo &&
                    !session.CanRedo,
                    "El historial final no refleja la eliminación rehecha.");
                Require(
                    Directory.Exists(
                        session.SessionDirectory),
                    "No existe la recuperación de la edición.");
                Results.Add(
                    "  PASS original intacto y revisión recuperable.");
            }
            finally
            {
                if (form != null)
                {
                    try
                    {
                        if (workspace != null &&
                            !GetWorkspaceField<bool>(
                                workspace,
                                "IsDisposed"))
                        {
                            SetWorkspaceField(
                                workspace,
                                "DeleteRecoveryOnClose",
                                true);
                            InvokePrivate(
                                form,
                                "CloseWorkspace",
                                workspace);
                        }
                    }
                    catch
                    {
                    }

                    form.Dispose();
                }

                Pump(100);
                TryDeleteDirectory(recoveryRoot);
            }
        }

        private static void ValidateAdvancedPdfStructures()
        {
            var sourcePath = IOPath.Combine(
                ValidationDirectory,
                "advanced-structures-source.pdf");
            var deleteSourcePath = IOPath.Combine(
                ValidationDirectory,
                "advanced-structures-delete-source.pdf");
            var reorderPath = IOPath.Combine(
                ValidationDirectory,
                "advanced-structures-reorder.pdf");
            var deletePath = IOPath.Combine(
                ValidationDirectory,
                "advanced-structures-delete.pdf");
            var blockedDeletePath = IOPath.Combine(
                ValidationDirectory,
                "advanced-outline-delete-blocked.pdf");
            var nextBlockSourcePath = IOPath.Combine(
                ValidationDirectory,
                "next-primary-goto-source.pdf");
            var nextBlockedOutputPath = IOPath.Combine(
                ValidationDirectory,
                "next-primary-goto-blocked.pdf");
            TryDeleteFile(sourcePath);
            TryDeleteFile(deleteSourcePath);
            TryDeleteFile(reorderPath);
            TryDeleteFile(deletePath);
            TryDeleteFile(blockedDeletePath);
            TryDeleteFile(nextBlockSourcePath);
            TryDeleteFile(nextBlockedOutputPath);
            CreateAdvancedFixture(
                sourcePath,
                true);
            CreateAdvancedFixture(
                deleteSourcePath,
                false);
            CreateAdvancedFixture(
                nextBlockSourcePath,
                false);
            AddPrimaryGoToWithNext(
                nextBlockSourcePath);
            var sourceHash = ComputeSha256(sourcePath);
            var deleteSourceHash =
                ComputeSha256(deleteSourcePath);
            var nextBlockSourceHash =
                ComputeSha256(nextBlockSourcePath);
            var advancedFailures =
                new List<string>();

            var reorderWatch = Stopwatch.StartNew();
            PdfPageOrganizerService.Organize(
                sourcePath,
                new List<PdfPageOrganizerPage>
                {
                    new PdfPageOrganizerPage(3, 90),
                    new PdfPageOrganizerPage(1, 0),
                    new PdfPageOrganizerPage(2, 0)
                },
                reorderPath,
                null);
            reorderWatch.Stop();
            Require(
                File.Exists(reorderPath),
                "No se creó la copia avanzada reordenada.");
            CaptureFailure(
                advancedFailures,
                "outline SetOCGState reorder/rotate",
                delegate
                {
                    ValidateSetOcgStateOutline(
                        reorderPath,
                        "reordenado/giro");
                });
            CaptureFailure(
                advancedFailures,
                "GoToR remoto homónimo reorder/rotate",
                delegate
                {
                    ValidateRemoteGoToR(
                        reorderPath,
                        "reordenado/giro");
                });
            CaptureFailure(
                advancedFailures,
                "destino /Catalog/Dests legacy",
                delegate
                {
                    ValidateNamedDestination(
                        reorderPath,
                        "LegacyTarget",
                        3,
                        true,
                        "reordenado/giro");
                });
            CaptureFailure(
                advancedFailures,
                "destino /Names/Dests homónimo",
                delegate
                {
                    ValidateNamedDestination(
                        reorderPath,
                        "Shared",
                        3,
                        true,
                        "reordenado/giro");
                });
            CaptureFailure(
                advancedFailures,
                "AcroForm /CO reorder/rotate",
                delegate
                {
                    ValidateCalculationOrder(
                        reorderPath,
                        new[] { "keep", "delete" },
                        "reordenado/giro");
                });
            CaptureFailure(
                advancedFailures,
                "PageLabels reorder/rotate",
                delegate
                {
                    ValidatePageLabelsAndRules(
                        reorderPath,
                        new[] { "i", "A-1", "5" },
                        new[]
                        {
                            new ExpectedLabelRule(
                                0,
                                new PdfName("r"),
                                1,
                                null),
                            new ExpectedLabelRule(
                                1,
                                PdfName.D,
                                1,
                                "A-"),
                            new ExpectedLabelRule(
                                2,
                                PdfName.D,
                                5,
                                null)
                        },
                        "reordenado/giro");
                });
            CaptureFailure(
                advancedFailures,
                "OpenAction y Catalog/AA reorder/rotate",
                delegate
                {
                    ValidateCatalogActions(
                        reorderPath,
                        1,
                        3,
                        "reordenado/giro");
                });
            SavePdfFirstPage(
                reorderPath,
                IOPath.Combine(
                    CapturesDirectory,
                    "03-estructuras-reordenadas.png"));

            var deleteWatch = Stopwatch.StartNew();
            PdfPageOrganizerService.Organize(
                deleteSourcePath,
                new List<PdfPageOrganizerPage>
                {
                    new PdfPageOrganizerPage(1, 0),
                    new PdfPageOrganizerPage(3, 0)
                },
                deletePath,
                null);
            deleteWatch.Stop();
            Require(
                File.Exists(deletePath),
                "No se creó la copia avanzada con borrado.");
            CaptureFailure(
                advancedFailures,
                "GoToR remoto homónimo delete",
                delegate
                {
                    ValidateRemoteGoToR(
                        deletePath,
                        "borrado");
                });
            CaptureFailure(
                advancedFailures,
                "destinos locales borrados",
                delegate
                {
                    ValidateNamedDestination(
                        deletePath,
                        "LegacyTarget",
                        -1,
                        false,
                        "borrado");
                    ValidateNamedDestination(
                        deletePath,
                        "Shared",
                        -1,
                        false,
                        "borrado");
                });
            CaptureFailure(
                advancedFailures,
                "AcroForm /CO delete",
                delegate
                {
                    ValidateCalculationOrder(
                        deletePath,
                        new[] { "keep" },
                        "borrado");
                });
            CaptureFailure(
                advancedFailures,
                "PageLabels delete",
                delegate
                {
                    ValidatePageLabelsAndRules(
                        deletePath,
                        new[] { "A-1", "i" },
                        new[]
                        {
                            new ExpectedLabelRule(
                                0,
                                PdfName.D,
                                1,
                                "A-"),
                            new ExpectedLabelRule(
                                1,
                                new PdfName("r"),
                                1,
                                null)
                        },
                        "borrado");
                });
            CaptureFailure(
                advancedFailures,
                "OpenAction y Catalog/AA delete",
                delegate
                {
                    ValidateCatalogActions(
                        deletePath,
                        2,
                        -1,
                        "borrado");
                });
            SavePdfFirstPage(
                deletePath,
                IOPath.Combine(
                    CapturesDirectory,
                    "04-estructuras-tras-borrado.png"));

            Exception blockedDeleteError = null;
            try
            {
                PdfPageOrganizerService.Organize(
                    sourcePath,
                    new List<PdfPageOrganizerPage>
                    {
                        new PdfPageOrganizerPage(1, 0),
                        new PdfPageOrganizerPage(3, 0)
                    },
                    blockedDeletePath,
                    null);
            }
            catch (Exception ex)
            {
                blockedDeleteError =
                    ex.GetBaseException();
            }

            CaptureFailure(
                advancedFailures,
                "bloqueo delete con outline avanzado",
                delegate
                {
                    Require(
                        blockedDeleteError != null,
                        "El borrado aceptó un outline con acción avanzada " +
                        "sin poder demostrar su seguridad.");
                    Require(
                        !File.Exists(blockedDeletePath),
                        "El borrado bloqueado publicó una salida.");
                });
            Exception nextBlockedDeleteError = null;
            try
            {
                PdfPageOrganizerService.Organize(
                    nextBlockSourcePath,
                    new List<PdfPageOrganizerPage>
                    {
                        new PdfPageOrganizerPage(1, 0),
                        new PdfPageOrganizerPage(3, 0)
                    },
                    nextBlockedOutputPath,
                    null);
            }
            catch (Exception ex)
            {
                nextBlockedDeleteError =
                    ex.GetBaseException();
            }

            CaptureFailure(
                advancedFailures,
                "bloqueo GoTo principal borrado con /Next",
                delegate
                {
                    Require(
                        nextBlockedDeleteError != null,
                        "El borrado aceptó un GoTo principal a página " +
                        "borrada aunque tenía una cadena /Next.");
                    Require(
                        !File.Exists(nextBlockedOutputPath),
                        "El bloqueo de GoTo+/Next publicó una salida.");
                });
            Require(
                string.Equals(
                    sourceHash,
                    ComputeSha256(sourcePath),
                    StringComparison.Ordinal),
                "La validación avanzada modificó el origen.");
            Require(
                string.Equals(
                    deleteSourceHash,
                    ComputeSha256(deleteSourcePath),
                    StringComparison.Ordinal),
                "La validación de borrado modificó su origen.");
            Require(
                string.Equals(
                    nextBlockSourceHash,
                    ComputeSha256(nextBlockSourcePath),
                    StringComparison.Ordinal),
                "La validación de /Next modificó su origen.");
            if (advancedFailures.Count > 0)
            {
                throw new InvalidOperationException(
                    string.Join(
                        Environment.NewLine,
                        advancedFailures.ToArray()));
            }
            Results.Add(
                "  PASS GoToR remoto homónimo, destino PdfName " +
                "legacy, SetOCGState y /CO.");
            Results.Add(
                "  PASS reglas PageLabels sin /P en reorder/rotate " +
                "y delete.");
            Results.Add(
                "  PASS outline avanzado preservado al reordenar/girar " +
                "y borrado inseguro bloqueado.");
            Results.Add(
                "  PASS /Next array poda solo GoTo y conserva URI; " +
                "GoTo principal inseguro bloqueado.");
            Results.Add(
                "  PERF estructuras avanzadas: reorder/rotate=" +
                reorderWatch.ElapsedMilliseconds +
                " ms; delete=" +
                deleteWatch.ElapsedMilliseconds +
                " ms.");
        }

        private static void CreateAdvancedFixture(
            string path,
            bool includeAdvancedOutline)
        {
            using (var stream = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            using (var document =
                new iTextSharp.text.Document(
                    PageSize.A4))
            {
                var writer = PdfWriter.GetInstance(
                    document,
                    stream);
                document.Open();
                document.Add(
                    new Paragraph(
                        "ADV-A / REMOTE LINK + KEEP FIELD",
                        FontFactory.GetFont(
                            FontFactory.HELVETICA_BOLD,
                            22f)));
                var keepField = new TextField(
                    writer,
                    new iTextSharp.text.Rectangle(
                        72f,
                        650f,
                        280f,
                        682f),
                    "keep");
                keepField.Text = "KEEP";
                writer.AddAnnotation(
                    keepField.GetTextField());

                document.NewPage();
                document.Add(
                    new Paragraph(
                        "ADV-B / LOCAL DESTINATION + DELETE FIELD",
                        FontFactory.GetFont(
                            FontFactory.HELVETICA_BOLD,
                            22f)));
                var deleteField = new TextField(
                    writer,
                    new iTextSharp.text.Rectangle(
                        72f,
                        650f,
                        280f,
                        682f),
                    "delete");
                deleteField.Text = "DELETE";
                writer.AddAnnotation(
                    deleteField.GetTextField());

                document.NewPage();
                document.Add(
                    new Paragraph(
                        "ADV-C / LAYER ACTION + ROMAN LABEL",
                        FontFactory.GetFont(
                            FontFactory.HELVETICA_BOLD,
                            22f)));
            }

            AddAdvancedLowLevelStructures(
                path,
                includeAdvancedOutline);
        }

        private static void AddAdvancedLowLevelStructures(
            string path,
            bool includeAdvancedOutline)
        {
            var temporaryPath = path + ".structures.tmp";
            TryDeleteFile(temporaryPath);
            PdfReader reader = null;
            try
            {
                reader = new PdfReader(path);
                using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None))
                using (var stamper =
                    new PdfStamper(reader, stream))
                {
                    var writer = stamper.Writer;
                    var catalog = reader.Catalog;

                    var legacyDestinations =
                        new PdfDictionary();
                    legacyDestinations.Put(
                        new PdfName("LegacyTarget"),
                        CreateExplicitDestination(
                            reader.GetPageOrigRef(2)));
                    catalog.Put(
                        PdfName.DESTS,
                        legacyDestinations);
                    var destinationNameTreeEntries =
                        new Dictionary<string, PdfObject>();
                    destinationNameTreeEntries.Add(
                        "Shared",
                        CreateExplicitDestination(
                            reader.GetPageOrigRef(2)));
                    var names =
                        new PdfDictionary();
                    names.Put(
                        PdfName.DESTS,
                        PdfNameTree.WriteTree(
                            destinationNameTreeEntries,
                            writer));
                    catalog.Put(
                        PdfName.NAMES,
                        names);

                    var remoteAction =
                        new PdfDictionary();
                    remoteAction.Put(
                        PdfName.S,
                        PdfName.GOTOR);
                    remoteAction.Put(
                        PdfName.F,
                        new PdfString(
                            "external-target.pdf"));
                    remoteAction.Put(
                        PdfName.D,
                        new PdfString("Shared"));
                    remoteAction.Put(
                        PdfName.NEWWINDOW,
                        PdfBoolean.PDFTRUE);
                    var remoteLink =
                        new PdfDictionary();
                    remoteLink.Put(
                        PdfName.TYPE,
                        PdfName.ANNOT);
                    remoteLink.Put(
                        PdfName.SUBTYPE,
                        PdfName.LINK);
                    remoteLink.Put(
                        PdfName.RECT,
                        CreateNumberArray(
                            72f,
                            590f,
                            330f,
                            625f));
                    remoteLink.Put(
                        PdfName.BORDER,
                        CreateNumberArray(
                            0f,
                            0f,
                            0f));
                    remoteLink.Put(
                        PdfName.A,
                        remoteAction);
                    var remoteLinkReference =
                        writer.AddToBody(
                            remoteLink)
                            .IndirectReference;
                    var firstPage =
                        reader.GetPageN(1);
                    var annotations =
                        firstPage.GetAsArray(
                            PdfName.ANNOTS);
                    if (annotations == null)
                    {
                        annotations =
                            new PdfArray();
                        firstPage.Put(
                            PdfName.ANNOTS,
                            annotations);
                    }

                    annotations.Add(
                        remoteLinkReference);
                    stamper.MarkUsed(firstPage);
                    stamper.MarkUsed(annotations);

                    var optionalContentGroup =
                        new PdfDictionary(
                            PdfName.OCG);
                    optionalContentGroup.Put(
                        PdfName.NAME,
                        new PdfString(
                            "QA LAYER"));
                    var ocgReference =
                        writer.AddToBody(
                            optionalContentGroup)
                            .IndirectReference;
                    var ocgs = new PdfArray();
                    ocgs.Add(ocgReference);
                    var defaultOcg =
                        new PdfDictionary();
                    var order = new PdfArray();
                    order.Add(ocgReference);
                    defaultOcg.Put(
                        PdfName.ORDER,
                        order);
                    var ocProperties =
                        new PdfDictionary();
                    ocProperties.Put(
                        PdfName.OCGS,
                        ocgs);
                    ocProperties.Put(
                        PdfName.D,
                        defaultOcg);
                    catalog.Put(
                        PdfName.OCPROPERTIES,
                        ocProperties);

                    if (includeAdvancedOutline)
                    {
                        var outlineRootReference =
                            writer.PdfIndirectReference;
                        var outlineItemReference =
                            writer.PdfIndirectReference;
                        var state = new PdfArray();
                        state.Add(PdfName.TOGGLE);
                        state.Add(ocgReference);
                        var layerAction =
                            new PdfDictionary();
                        layerAction.Put(
                            PdfName.S,
                            PdfName.SETOCGSTATE);
                        layerAction.Put(
                            PdfName.STATE,
                            state);
                        var outlineItem =
                            new PdfDictionary();
                        outlineItem.Put(
                            PdfName.TITLE,
                            new PdfString(
                                "Alternar capa QA"));
                        outlineItem.Put(
                            PdfName.PARENT,
                            outlineRootReference);
                        outlineItem.Put(
                            PdfName.A,
                            layerAction);
                        var outlineRoot =
                            new PdfDictionary();
                        outlineRoot.Put(
                            PdfName.TYPE,
                            PdfName.OUTLINES);
                        outlineRoot.Put(
                            PdfName.FIRST,
                            outlineItemReference);
                        outlineRoot.Put(
                            PdfName.LAST,
                            outlineItemReference);
                        outlineRoot.Put(
                            PdfName.COUNT,
                            new PdfNumber(1));
                        writer.AddToBody(
                            outlineRoot,
                            outlineRootReference);
                        writer.AddToBody(
                            outlineItem,
                            outlineItemReference);
                        catalog.Put(
                            PdfName.OUTLINES,
                            outlineRootReference);
                    }

                    var labelNumbers =
                        new PdfArray();
                    labelNumbers.Add(
                        new PdfNumber(0));
                    var decimalRule =
                        new PdfDictionary();
                    decimalRule.Put(
                        PdfName.S,
                        PdfName.D);
                    decimalRule.Put(
                        PdfName.ST,
                        new PdfNumber(1));
                    decimalRule.Put(
                        PdfName.P,
                        new PdfString("A-"));
                    labelNumbers.Add(
                        decimalRule);
                    labelNumbers.Add(
                        new PdfNumber(1));
                    var decimalWithoutPrefixRule =
                        new PdfDictionary();
                    decimalWithoutPrefixRule.Put(
                        PdfName.S,
                        PdfName.D);
                    decimalWithoutPrefixRule.Put(
                        PdfName.ST,
                        new PdfNumber(5));
                    labelNumbers.Add(
                        decimalWithoutPrefixRule);
                    labelNumbers.Add(
                        new PdfNumber(2));
                    var romanRule =
                        new PdfDictionary();
                    romanRule.Put(
                        PdfName.S,
                        new PdfName("r"));
                    labelNumbers.Add(
                        romanRule);
                    var pageLabels =
                        new PdfDictionary();
                    pageLabels.Put(
                        PdfName.NUMS,
                        labelNumbers);
                    catalog.Put(
                        PdfName.PAGELABELS,
                        pageLabels);

                    var openAction =
                        new PdfDictionary();
                    openAction.Put(
                        PdfName.S,
                        PdfName.GOTO);
                    openAction.Put(
                        PdfName.D,
                        CreateExplicitDestination(
                            reader.GetPageOrigRef(3)));
                    catalog.Put(
                        PdfName.OPENACTION,
                        openAction);
                    var willCloseAction =
                        new PdfDictionary();
                    willCloseAction.Put(
                        PdfName.S,
                        PdfName.GOTO);
                    willCloseAction.Put(
                        PdfName.D,
                        CreateExplicitDestination(
                            reader.GetPageOrigRef(2)));
                    var additionalActions =
                        new PdfDictionary();
                    additionalActions.Put(
                        PdfName.WC,
                        willCloseAction);
                    var chainedLocalAction =
                        new PdfDictionary();
                    chainedLocalAction.Put(
                        PdfName.S,
                        PdfName.GOTO);
                    chainedLocalAction.Put(
                        PdfName.D,
                        CreateExplicitDestination(
                            reader.GetPageOrigRef(2)));
                    var chainedUriAction =
                        new PdfDictionary();
                    chainedUriAction.Put(
                        PdfName.S,
                        PdfName.URI);
                    chainedUriAction.Put(
                        PdfName.URI,
                        new PdfString(
                            "https://qa.invalid/survivor"));
                    var nextActions =
                        new PdfArray();
                    nextActions.Add(
                        chainedLocalAction);
                    nextActions.Add(
                        chainedUriAction);
                    var windowShownAction =
                        new PdfDictionary();
                    windowShownAction.Put(
                        PdfName.S,
                        PdfName.URI);
                    windowShownAction.Put(
                        PdfName.URI,
                        new PdfString(
                            "https://qa.invalid/primary"));
                    windowShownAction.Put(
                        PdfName.NEXT,
                        nextActions);
                    additionalActions.Put(
                        PdfName.WS,
                        windowShownAction);
                    catalog.Put(
                        PdfName.AA,
                        additionalActions);

                    var fields =
                        reader.AcroFields;
                    var keepReference =
                        fields.GetFieldItem(
                            "keep")
                            .GetWidgetRef(0);
                    var deleteReference =
                        fields.GetFieldItem(
                            "delete")
                            .GetWidgetRef(0);
                    var calculationOrder =
                        new PdfArray();
                    calculationOrder.Add(
                        keepReference);
                    calculationOrder.Add(
                        deleteReference);
                    var acroForm =
                        catalog.GetAsDict(
                            PdfName.ACROFORM);
                    Require(
                        acroForm != null,
                        "El fixture avanzado no creó AcroForm.");
                    acroForm.Put(
                        PdfName.CO,
                        calculationOrder);
                    stamper.MarkUsed(acroForm);
                    stamper.MarkUsed(catalog);
                }
            }
            finally
            {
                if (reader != null)
                {
                    reader.Close();
                }
            }

            File.Delete(path);
            File.Move(
                temporaryPath,
                path);
        }

        private static PdfArray CreateExplicitDestination(
            PdfIndirectReference pageReference)
        {
            var destination = new PdfArray();
            destination.Add(pageReference);
            destination.Add(PdfName.FIT);
            return destination;
        }

        private static void AddPrimaryGoToWithNext(
            string path)
        {
            var temporaryPath =
                path + ".next.tmp";
            TryDeleteFile(temporaryPath);
            PdfReader reader = null;
            try
            {
                reader = new PdfReader(path);
                using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None))
                using (var stamper =
                    new PdfStamper(
                        reader,
                        stream))
                {
                    var catalog =
                        reader.Catalog;
                    var additionalActions =
                        catalog.GetAsDict(
                            PdfName.AA);
                    if (additionalActions == null)
                    {
                        additionalActions =
                            new PdfDictionary();
                        catalog.Put(
                            PdfName.AA,
                            additionalActions);
                    }

                    var trailingUri =
                        new PdfDictionary();
                    trailingUri.Put(
                        PdfName.S,
                        PdfName.URI);
                    trailingUri.Put(
                        PdfName.URI,
                        new PdfString(
                            "https://qa.invalid/after-blocked-goto"));
                    var primaryGoTo =
                        new PdfDictionary();
                    primaryGoTo.Put(
                        PdfName.S,
                        PdfName.GOTO);
                    primaryGoTo.Put(
                        PdfName.D,
                        CreateExplicitDestination(
                            reader.GetPageOrigRef(2)));
                    primaryGoTo.Put(
                        PdfName.NEXT,
                        trailingUri);
                    additionalActions.Put(
                        PdfName.DP,
                        primaryGoTo);
                    stamper.MarkUsed(
                        additionalActions);
                    stamper.MarkUsed(
                        catalog);
                }
            }
            finally
            {
                if (reader != null)
                {
                    reader.Close();
                }
            }

            File.Delete(path);
            File.Move(
                temporaryPath,
                path);
        }

        private static PdfArray CreateNumberArray(
            params float[] values)
        {
            var array = new PdfArray();
            foreach (var value in values)
            {
                array.Add(
                    new PdfNumber(value));
            }

            return array;
        }

        private static void ValidateRemoteGoToR(
            string path,
            string stage)
        {
            var reader = new PdfReader(
                ReadAllBytesShared(path));
            try
            {
                PdfDictionary action = null;
                for (var pageNumber = 1;
                    pageNumber <= reader.NumberOfPages;
                    pageNumber++)
                {
                    var annotations =
                        reader.GetPageN(pageNumber)
                            .GetAsArray(
                                PdfName.ANNOTS);
                    if (annotations == null)
                    {
                        continue;
                    }

                    for (var index = 0;
                        index < annotations.Size;
                        index++)
                    {
                        var annotation =
                            PdfReader.GetPdfObject(
                                annotations[index])
                                as PdfDictionary;
                        var candidate =
                            annotation == null
                                ? null
                                : annotation.GetAsDict(
                                    PdfName.A);
                        if (candidate != null &&
                            PdfName.GOTOR.Equals(
                                candidate.GetAsName(
                                    PdfName.S)))
                        {
                            action = candidate;
                            break;
                        }
                    }

                    if (action != null)
                    {
                        break;
                    }
                }

                Require(
                    action != null,
                    stage +
                    ": desapareció la acción /GoToR externa.");
                Require(
                    string.Equals(
                        ReadFileSpecification(
                            action.Get(
                                PdfName.F)),
                        "external-target.pdf",
                        StringComparison.Ordinal),
                    stage +
                    ": /GoToR cambió su archivo externo.");
                Require(
                    string.Equals(
                        ReadDestinationName(
                            action.Get(
                                PdfName.D)),
                        "Shared",
                        StringComparison.Ordinal),
                    stage +
                    ": /GoToR cambió el destino remoto homónimo.");
            }
            finally
            {
                reader.Close();
            }
        }

        private static string ReadFileSpecification(
            PdfObject value)
        {
            var resolved = PdfReader.GetPdfObject(
                value);
            var text = resolved as PdfString;
            if (text != null)
            {
                return text.ToUnicodeString();
            }

            var dictionary =
                resolved as PdfDictionary;
            if (dictionary != null)
            {
                var file =
                    dictionary.GetAsString(
                        PdfName.F);
                return file == null
                    ? null
                    : file.ToUnicodeString();
            }

            return null;
        }

        private static string ReadDestinationName(
            PdfObject value)
        {
            var resolved = PdfReader.GetPdfObject(
                value);
            var text = resolved as PdfString;
            if (text != null)
            {
                return text.ToUnicodeString();
            }

            var name = resolved as PdfName;
            if (name != null)
            {
                return PdfName.DecodeName(
                    name.ToString());
            }

            return null;
        }

        private static void ValidateSetOcgStateOutline(
            string path,
            string stage)
        {
            var reader = new PdfReader(
                ReadAllBytesShared(path));
            try
            {
                var outlines =
                    reader.Catalog.GetAsDict(
                        PdfName.OUTLINES);
                var first =
                    outlines == null
                        ? null
                        : outlines.GetAsDict(
                            PdfName.FIRST);
                var action =
                    first == null
                        ? null
                        : first.GetAsDict(
                            PdfName.A);
                Require(
                    action != null &&
                    PdfName.SETOCGSTATE.Equals(
                        action.GetAsName(
                            PdfName.S)),
                    stage +
                    ": el outline perdió /SetOCGState.");
                var state =
                    action.GetAsArray(
                        PdfName.STATE);
                Require(
                    state != null &&
                    state.Size >= 2 &&
                    PdfName.TOGGLE.Equals(
                        state.GetAsName(0)),
                    stage +
                    ": /SetOCGState perdió su array /State.");
                var ocg =
                    PdfReader.GetPdfObject(
                        state[1])
                        as PdfDictionary;
                Require(
                    ocg != null &&
                    PdfName.OCG.Equals(
                        ocg.GetAsName(
                            PdfName.TYPE)),
                    stage +
                    ": /SetOCGState conserva una referencia OCG inválida.");
                Require(
                    reader.Catalog.GetAsDict(
                        PdfName.OCPROPERTIES) != null,
                    stage +
                    ": desapareció /OCProperties.");
            }
            finally
            {
                reader.Close();
            }
        }

        private static void ValidateNamedDestination(
            string path,
            string destinationName,
            int expectedPage,
            bool shouldExist,
            string stage)
        {
            var reader = new PdfReader(
                ReadAllBytesShared(path));
            try
            {
                var stringDestinations =
                    SimpleNamedDestination
                        .GetNamedDestination(
                            reader,
                            false);
                var nameDestinations =
                    SimpleNamedDestination
                        .GetNamedDestination(
                            reader,
                            true);
                var catalogDestinations =
                    reader.Catalog.GetAsDict(
                        PdfName.DESTS);
                var catalogValue =
                    catalogDestinations == null
                        ? null
                        : catalogDestinations.Get(
                            new PdfName(
                                destinationName));
                var names =
                    reader.Catalog.GetAsDict(
                        PdfName.NAMES);
                var destinationTree =
                    names == null
                        ? null
                        : names.GetAsDict(
                            PdfName.DESTS);
                var treeValues =
                    destinationTree == null
                        ? new Dictionary<string, PdfObject>()
                        : PdfNameTree.ReadTree(
                            destinationTree);
                PdfObject treeValue;
                treeValues.TryGetValue(
                    destinationName,
                    out treeValue);
                string destination = null;
                if (stringDestinations != null)
                {
                    stringDestinations.TryGetValue(
                        destinationName,
                        out destination);
                }

                if (destination == null &&
                    nameDestinations != null)
                {
                    nameDestinations.TryGetValue(
                        destinationName,
                        out destination);
                }

                if (!shouldExist)
                {
                    Require(
                        destination == null &&
                        catalogValue == null &&
                        treeValue == null,
                        stage +
                        ": sobrevivió el destino local borrado " +
                        destinationName +
                        ".");
                    return;
                }

                Require(
                    destination != null,
                    stage +
                    ": desapareció el destino legacy /" +
                    destinationName +
                    ".");
                var firstToken =
                    destination.Split(
                        new[] { ' ' },
                        StringSplitOptions
                            .RemoveEmptyEntries)
                        .FirstOrDefault();
                int actualPage;
                Require(
                    int.TryParse(
                        firstToken,
                        out actualPage) &&
                    actualPage == expectedPage,
                    stage +
                    ": el destino " +
                    destinationName +
                    " apunta a " +
                    destination +
                    " en vez de la página " +
                    expectedPage +
                    ".");
                if (string.Equals(
                        destinationName,
                        "LegacyTarget",
                        StringComparison.Ordinal))
                {
                    Require(
                        catalogValue != null,
                        stage +
                        ": LegacyTarget fue migrado fuera de " +
                        "/Catalog/Dests; debe conservar clave PdfName.");
                    Require(
                        treeValue == null,
                        stage +
                        ": LegacyTarget apareció duplicado en " +
                        "/Names/Dests.");
                    Require(
                        ResolveDestinationTargetPage(
                            reader,
                            NormalizeNamedDestinationObject(
                                catalogValue)) ==
                            expectedPage,
                        stage +
                        ": el valor físico /Catalog/Dests/LegacyTarget " +
                        "no apunta a la página esperada.");
                }
                else if (string.Equals(
                        destinationName,
                        "Shared",
                        StringComparison.Ordinal))
                {
                    Require(
                        treeValue != null,
                        stage +
                        ": Shared fue migrado fuera de " +
                        "/Names/Dests; debe conservar clave string.");
                    Require(
                        catalogValue == null,
                        stage +
                        ": Shared apareció duplicado en /Catalog/Dests.");
                    Require(
                        ResolveDestinationTargetPage(
                            reader,
                            NormalizeNamedDestinationObject(
                                treeValue)) ==
                            expectedPage,
                        stage +
                        ": el valor físico /Names/Dests/Shared " +
                        "no apunta a la página esperada.");
                }
            }
            finally
            {
                reader.Close();
            }
        }

        private static PdfObject NormalizeNamedDestinationObject(
            PdfObject value)
        {
            var resolved =
                PdfReader.GetPdfObject(
                    value);
            var dictionary =
                resolved as PdfDictionary;
            return dictionary == null
                ? resolved
                : dictionary.Get(
                    PdfName.D);
        }

        private static void ValidateCatalogActions(
            string path,
            int expectedOpenActionPage,
            int expectedAdditionalActionPage,
            string stage)
        {
            var reader = new PdfReader(
                ReadAllBytesShared(path));
            try
            {
                var openAction =
                    reader.Catalog.GetAsDict(
                        PdfName.OPENACTION);
                Require(
                    openAction != null &&
                    PdfName.GOTO.Equals(
                        openAction.GetAsName(
                            PdfName.S)),
                    stage +
                    ": /OpenAction local desapareció o cambió de tipo.");
                Require(
                    ResolveDestinationTargetPage(
                        reader,
                        openAction.Get(
                            PdfName.D)) ==
                        expectedOpenActionPage,
                    stage +
                    ": /OpenAction no se remapeó a la página " +
                    expectedOpenActionPage +
                    ".");

                var additionalActions =
                    reader.Catalog.GetAsDict(
                        PdfName.AA);
                var willClose =
                    additionalActions == null
                        ? null
                        : additionalActions.GetAsDict(
                            PdfName.WC);
                if (expectedAdditionalActionPage < 0)
                {
                    Require(
                        willClose == null,
                        stage +
                        ": /Catalog/AA/WC siguió apuntando a " +
                        "una página eliminada.");
                }
                else
                {
                    Require(
                        willClose != null &&
                        PdfName.GOTO.Equals(
                            willClose.GetAsName(
                                PdfName.S)) &&
                        ResolveDestinationTargetPage(
                            reader,
                            willClose.Get(
                                PdfName.D)) ==
                            expectedAdditionalActionPage,
                        stage +
                        ": /Catalog/AA/WC no se remapeó a la página " +
                        expectedAdditionalActionPage +
                        ".");
                }

                var windowShown =
                    additionalActions == null
                        ? null
                        : additionalActions.GetAsDict(
                            PdfName.WS);
                ValidateNextActionArray(
                    reader,
                    windowShown,
                    expectedAdditionalActionPage,
                    stage);
            }
            finally
            {
                reader.Close();
            }
        }

        private static void ValidateNextActionArray(
            PdfReader reader,
            PdfDictionary primaryAction,
            int expectedLocalPage,
            string stage)
        {
            Require(
                primaryAction != null &&
                PdfName.URI.Equals(
                    primaryAction.GetAsName(
                        PdfName.S)) &&
                string.Equals(
                    ReadUri(
                        primaryAction),
                    "https://qa.invalid/primary",
                    StringComparison.Ordinal),
                stage +
                ": AA/WS perdió su acción URI principal.");
            var nextObject =
                PdfReader.GetPdfObject(
                    primaryAction.Get(
                        PdfName.NEXT));
            var actions =
                new List<PdfDictionary>();
            var nextArray =
                nextObject as PdfArray;
            if (nextArray != null)
            {
                for (var index = 0;
                    index < nextArray.Size;
                    index++)
                {
                    var action =
                        PdfReader.GetPdfObject(
                            nextArray[index])
                            as PdfDictionary;
                    if (action != null)
                    {
                        actions.Add(action);
                    }
                }
            }
            else
            {
                var single =
                    nextObject as PdfDictionary;
                if (single != null)
                {
                    actions.Add(single);
                }
            }

            var localTargets =
                new List<int>();
            var survivorUriCount = 0;
            foreach (var action in actions)
            {
                var actionType =
                    action.GetAsName(
                        PdfName.S);
                if (PdfName.GOTO.Equals(
                        actionType))
                {
                    localTargets.Add(
                        ResolveDestinationTargetPage(
                            reader,
                            action.Get(
                                PdfName.D)));
                }
                else if (PdfName.URI.Equals(
                        actionType) &&
                    string.Equals(
                        ReadUri(action),
                        "https://qa.invalid/survivor",
                        StringComparison.Ordinal))
                {
                    survivorUriCount++;
                }
            }

            Require(
                survivorUriCount == 1,
                stage +
                ": la poda de /Next no conservó exactamente el URI posterior.");
            if (expectedLocalPage < 0)
            {
                Require(
                    localTargets.Count == 0,
                    stage +
                    ": /Next conservó un GoTo a página borrada.");
            }
            else
            {
                Require(
                    localTargets.Count == 1 &&
                    localTargets[0] ==
                        expectedLocalPage,
                    stage +
                    ": /Next no remapeó su GoTo local a página " +
                    expectedLocalPage +
                    ".");
            }
        }

        private static string ReadUri(
            PdfDictionary action)
        {
            var uri =
                action == null
                    ? null
                    : action.GetAsString(
                        PdfName.URI);
            return uri == null
                ? null
                : uri.ToUnicodeString();
        }

        private static int ResolveDestinationTargetPage(
            PdfReader reader,
            PdfObject destinationObject)
        {
            var destination =
                PdfReader.GetPdfObject(
                    destinationObject)
                    as PdfArray;
            if (destination == null ||
                destination.Size == 0)
            {
                return -1;
            }

            var pageReference =
                destination[0]
                    as PdfIndirectReference;
            if (pageReference == null)
            {
                return -1;
            }

            for (var pageNumber = 1;
                pageNumber <= reader.NumberOfPages;
                pageNumber++)
            {
                var candidate =
                    reader.GetPageOrigRef(
                        pageNumber);
                if (candidate != null &&
                    candidate.Number ==
                        pageReference.Number &&
                    candidate.Generation ==
                        pageReference.Generation)
                {
                    return pageNumber;
                }
            }

            return -1;
        }

        private static void ValidateCalculationOrder(
            string path,
            string[] expectedNames,
            string stage)
        {
            var reader = new PdfReader(
                ReadAllBytesShared(path));
            try
            {
                var fields =
                    reader.AcroFields.Fields;
                foreach (var expectedName in expectedNames)
                {
                    Require(
                        fields.ContainsKey(
                            expectedName),
                        stage +
                        ": falta el campo " +
                        expectedName +
                        ".");
                }

                Require(
                    !fields.ContainsKey("delete") ||
                    expectedNames.Contains(
                        "delete"),
                    stage +
                    ": el widget borrado sigue en AcroForm.");

                var acroForm =
                    reader.Catalog.GetAsDict(
                        PdfName.ACROFORM);
                var order =
                    acroForm == null
                        ? null
                        : acroForm.GetAsArray(
                            PdfName.CO);
                Require(
                    order != null &&
                    order.Size ==
                        expectedNames.Length,
                    stage +
                    ": /CO no tiene el tamaño esperado.");
                var actualNames =
                    new List<string>();
                for (var index = 0;
                    index < order.Size;
                    index++)
                {
                    var field =
                        PdfReader.GetPdfObject(
                            order[index])
                            as PdfDictionary;
                    Require(
                        field != null,
                        stage +
                        ": /CO contiene una referencia colgante.");
                    var name =
                        ReadEffectiveFieldName(
                            field);
                    Require(
                        !string.IsNullOrWhiteSpace(
                            name),
                        stage +
                        ": una entrada de /CO no identifica campo.");
                    actualNames.Add(name);
                }

                Require(
                    actualNames.SequenceEqual(
                        expectedNames),
                    stage +
                    ": /CO=[" +
                    string.Join(
                        ",",
                        actualNames.ToArray()) +
                    "], esperado=[" +
                    string.Join(
                        ",",
                        expectedNames) +
                    "].");
            }
            finally
            {
                reader.Close();
            }
        }

        private static string ReadEffectiveFieldName(
            PdfDictionary field)
        {
            var current = field;
            for (var depth = 0;
                current != null && depth < 16;
                depth++)
            {
                var name =
                    current.GetAsString(
                        PdfName.T);
                if (name != null)
                {
                    return name.ToUnicodeString();
                }

                current =
                    current.GetAsDict(
                        PdfName.PARENT);
            }

            return null;
        }

        private static void ValidatePageLabelsAndRules(
            string path,
            string[] expectedLabels,
            ExpectedLabelRule[] expectedRules,
            string stage)
        {
            var reader = new PdfReader(
                ReadAllBytesShared(path));
            try
            {
                var pageLabels =
                    reader.Catalog.GetAsDict(
                        PdfName.PAGELABELS);
                Require(
                    pageLabels != null,
                    stage +
                    ": falta /PageLabels.");
                var rules =
                    PdfNumberTree.ReadTree(
                        pageLabels);
                Require(
                    rules != null,
                    stage +
                    ": no se pudo leer el number tree de etiquetas.");
                var actualLabels =
                    RenderPageLabelsFromRawRules(
                        reader,
                        rules);
                Require(
                    actualLabels.SequenceEqual(
                        expectedLabels),
                    stage +
                    ": etiquetas crudas=[" +
                    string.Join(
                        ",",
                        actualLabels) +
                    "], esperadas=[" +
                    string.Join(
                        ",",
                        expectedLabels) +
                    "].");
                foreach (var expectedRule in expectedRules)
                {
                    PdfObject ruleObject;
                    Require(
                        rules.TryGetValue(
                            expectedRule.PageIndex,
                            out ruleObject),
                        stage +
                        ": falta regla PageLabels en índice " +
                        expectedRule.PageIndex +
                        ".");
                    var rule =
                        PdfReader.GetPdfObject(
                            ruleObject)
                            as PdfDictionary;
                    Require(
                        rule != null,
                        stage +
                        ": regla PageLabels inválida.");
                    var prefix =
                        rule.GetAsString(
                            PdfName.P);
                    if (expectedRule.Prefix == null)
                    {
                        Require(
                            prefix == null,
                            stage +
                            ": la regla " +
                            expectedRule.PageIndex +
                            " añadió /P aunque el origen no tenía prefijo.");
                    }
                    else
                    {
                        Require(
                            prefix != null &&
                            string.Equals(
                                prefix.ToUnicodeString(),
                                expectedRule.Prefix,
                                StringComparison.Ordinal),
                            stage +
                            ": la regla " +
                            expectedRule.PageIndex +
                            " no conserva el prefijo " +
                            expectedRule.Prefix +
                            ".");
                    }
                    Require(
                        expectedRule.Style.Equals(
                            rule.GetAsName(
                                PdfName.S)),
                        stage +
                        ": estilo de etiqueta incorrecto en índice " +
                        expectedRule.PageIndex +
                        ".");
                    var start =
                        rule.GetAsNumber(
                            PdfName.ST);
                    var actualStart =
                        start == null
                            ? 1
                            : start.IntValue;
                    Require(
                        actualStart ==
                            expectedRule.Start,
                        stage +
                        ": /St=" +
                        actualStart +
                        " en índice " +
                        expectedRule.PageIndex +
                        ", esperado=" +
                        expectedRule.Start +
                        ".");
                }
            }
            finally
            {
                reader.Close();
            }
        }

        private static string[] RenderPageLabelsFromRawRules(
            PdfReader reader,
            IDictionary<int, PdfObject> rules)
        {
            var orderedRules =
                rules.Keys
                    .Where(index =>
                        index >= 0 &&
                        index < reader.NumberOfPages)
                    .OrderBy(index => index)
                    .ToArray();
            var labels =
                new string[reader.NumberOfPages];
            for (var pageIndex = 0;
                pageIndex < reader.NumberOfPages;
                pageIndex++)
            {
                var ruleIndex = orderedRules
                    .Where(index =>
                        index <= pageIndex)
                    .DefaultIfEmpty(-1)
                    .Last();
                if (ruleIndex < 0)
                {
                    labels[pageIndex] =
                        (pageIndex + 1).ToString();
                    continue;
                }

                var rule =
                    PdfReader.GetPdfObject(
                        rules[ruleIndex])
                        as PdfDictionary;
                Require(
                    rule != null,
                    "El number tree contiene una regla PageLabels inválida.");
                var prefixObject =
                    rule.GetAsString(
                        PdfName.P);
                var prefix =
                    prefixObject == null
                        ? string.Empty
                        : prefixObject.ToUnicodeString();
                var style =
                    rule.GetAsName(
                        PdfName.S);
                var startObject =
                    rule.GetAsNumber(
                        PdfName.ST);
                var start =
                    startObject == null
                        ? 1
                        : startObject.IntValue;
                var number =
                    start +
                    pageIndex -
                    ruleIndex;
                labels[pageIndex] =
                    prefix +
                    FormatPageLabelNumber(
                        style,
                        number);
            }

            return labels;
        }

        private static string FormatPageLabelNumber(
            PdfName style,
            int number)
        {
            if (style == null)
            {
                return string.Empty;
            }

            if (PdfName.D.Equals(style))
            {
                return number.ToString();
            }

            if (PdfName.R.Equals(style))
            {
                return ToRoman(number);
            }

            if (new PdfName("r").Equals(style))
            {
                return ToRoman(number)
                    .ToLowerInvariant();
            }

            if (PdfName.A.Equals(style))
            {
                return ToAlphabetic(number);
            }

            if (new PdfName("a").Equals(style))
            {
                return ToAlphabetic(number)
                    .ToLowerInvariant();
            }

            throw new InvalidDataException(
                "Estilo PageLabels no reconocido: " +
                style);
        }

        private static string ToRoman(
            int number)
        {
            Require(
                number > 0,
                "Un número romano de PageLabels debe ser positivo.");
            var values =
                new[]
                {
                    1000, 900, 500, 400,
                    100, 90, 50, 40,
                    10, 9, 5, 4, 1
                };
            var symbols =
                new[]
                {
                    "M", "CM", "D", "CD",
                    "C", "XC", "L", "XL",
                    "X", "IX", "V", "IV", "I"
                };
            var result =
                new System.Text.StringBuilder();
            for (var index = 0;
                index < values.Length;
                index++)
            {
                while (number >= values[index])
                {
                    result.Append(
                        symbols[index]);
                    number -=
                        values[index];
                }
            }

            return result.ToString();
        }

        private static string ToAlphabetic(
            int number)
        {
            Require(
                number > 0,
                "Una letra de PageLabels debe ser positiva.");
            var letterIndex =
                (number - 1) % 26;
            var repeatCount =
                (number - 1) / 26 + 1;
            return new string(
                (char)('A' + letterIndex),
                repeatCount);
        }

        private static void ValidateSameSizeSourceMutation()
        {
            var sourcePath = IOPath.Combine(
                ValidationDirectory,
                "same-size-mutation-source.pdf");
            var outputPath = IOPath.Combine(
                ValidationDirectory,
                "same-size-mutation-output.pdf");
            TryDeleteFile(sourcePath);
            TryDeleteFile(outputPath);
            long mutationOffset;
            CreateLargeMutationFixture(
                sourcePath,
                out mutationOffset);
            var originalLength =
                new FileInfo(sourcePath).Length;
            var originalHash =
                ComputeSha256(sourcePath);
            var originalWriteTime =
                File.GetLastWriteTimeUtc(
                    sourcePath);
            var mutationAttempted = false;
            var mutationApplied = false;
            Exception rejectedBy = null;
            try
            {
                PdfPageOrganizerService.Organize(
                    sourcePath,
                    new List<PdfPageOrganizerPage>
                    {
                        new PdfPageOrganizerPage(2, 0),
                        new PdfPageOrganizerPage(1, 0),
                        new PdfPageOrganizerPage(3, 0)
                    },
                    outputPath,
                    delegate(
                        PdfPageOrganizerProgress progress)
                    {
                        if (mutationAttempted)
                        {
                            return;
                        }

                        mutationAttempted = true;
                        using (var stream =
                            new FileStream(
                                sourcePath,
                                FileMode.Open,
                                FileAccess.ReadWrite,
                                FileShare.Read))
                        {
                            stream.Position =
                                mutationOffset;
                            var previous =
                                stream.ReadByte();
                            Require(
                                previous == (byte)'A',
                                "El byte de mutación no era el esperado.");
                            stream.Position =
                                mutationOffset;
                            stream.WriteByte(
                                (byte)'B');
                            stream.Flush(true);
                            mutationApplied = true;
                        }

                        File.SetLastWriteTimeUtc(
                            sourcePath,
                            originalWriteTime);
                    });
            }
            catch (Exception ex)
            {
                rejectedBy =
                    ex.GetBaseException();
            }

            Require(
                mutationAttempted,
                "El callback no llegó a intentar el cambio de origen.");
            Require(
                rejectedBy != null,
                "El organizador aceptó un origen modificado " +
                "con el mismo tamaño.");
            Require(
                !File.Exists(outputPath),
                "Quedó publicada una salida tras cambiar el origen.");
            Require(
                new FileInfo(sourcePath).Length ==
                    originalLength,
                "La mutación no conservó el tamaño del origen.");
            if (mutationApplied)
            {
                Require(
                    !string.Equals(
                        originalHash,
                        ComputeSha256(sourcePath),
                        StringComparison.Ordinal),
                    "La mutación aplicada no cambió el contenido.");
            }

            var temporaryPrefix =
                "." +
                IOPath.GetFileNameWithoutExtension(
                    outputPath) +
                ".";
            Require(
                Directory.GetFiles(
                    ValidationDirectory,
                    temporaryPrefix + "*.tmp")
                    .Length == 0,
                "Quedó un temporal tras rechazar el origen cambiado.");
            Results.Add(
                "  PASS cambio same-size rechazado; mutación=" +
                (mutationApplied
                    ? "aplicada y detectada"
                    : "bloqueada por lease") +
                "; error=" +
                rejectedBy.GetType().Name +
                ".");
        }

        private static void CreateLargeMutationFixture(
            string path,
            out long mutationOffset)
        {
            using (var stream = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            using (var document =
                new iTextSharp.text.Document(
                    PageSize.A4))
            {
                var writer =
                    PdfWriter.GetInstance(
                        document,
                        stream);
                document.Open();
                document.Add(
                    new Paragraph(
                        "MUTATION QA PAGE 1"));
                document.NewPage();
                document.Add(
                    new Paragraph(
                        "MUTATION QA PAGE 2"));
                document.NewPage();
                document.Add(
                    new Paragraph(
                        "MUTATION QA PAGE 3"));
                var padding =
                    Enumerable.Repeat(
                        (byte)'A',
                        1024 * 1024)
                        .ToArray();
                writer.AddToBody(
                    new PdfStream(
                        padding));
            }

            var bytes =
                File.ReadAllBytes(path);
            mutationOffset =
                FindUnsampledPaddingOffset(
                    bytes);
            Require(
                mutationOffset >= 0,
                "No se encontró un hueco no muestreado para mutar.");
        }

        private static long FindUnsampledPaddingOffset(
            byte[] bytes)
        {
            const int sampleLength =
                32 * 1024;
            var length =
                (long)bytes.Length;
            var maximumStart =
                Math.Max(
                    0L,
                    length - sampleLength);
            var sampleStarts =
                new[]
                {
                    0L,
                    Math.Max(
                        0L,
                        length / 4L -
                        sampleLength / 2L),
                    Math.Max(
                        0L,
                        length / 2L -
                        sampleLength / 2L),
                    Math.Max(
                        0L,
                        length * 3L / 4L -
                        sampleLength / 2L),
                    maximumStart
                };
            for (var offset = sampleLength;
                offset < bytes.Length -
                    sampleLength;
                offset++)
            {
                if (bytes[offset] !=
                        (byte)'A' ||
                    bytes[offset - 1] !=
                        (byte)'A' ||
                    bytes[offset + 1] !=
                        (byte)'A')
                {
                    continue;
                }

                var sampled = false;
                foreach (var sampleStart in
                    sampleStarts)
                {
                    if (offset >= sampleStart &&
                        offset <
                            sampleStart +
                            sampleLength)
                    {
                        sampled = true;
                        break;
                    }
                }

                if (!sampled)
                {
                    return offset;
                }
            }

            return -1;
        }

        private static void SavePdfFirstPage(
            string pdfPath,
            string imagePath)
        {
            using (var document =
                PdfiumDocument.Load(
                    pdfPath))
            using (var image =
                document.Render(
                    0,
                    794,
                    1123,
                    96,
                    96,
                    PdfRenderFlags.Annotations |
                    PdfRenderFlags.LcdText))
            {
                image.Save(
                    imagePath,
                    ImageFormat.Png);
            }
        }

        private static void WaitForPageOrganization(
            PdfViewerForm form,
            string timeoutMessage)
        {
            PumpUntil(
                delegate
                {
                    var inProgress =
                        GetField<bool>(
                            form,
                            "pageOrganizationInProgress");
                    var worker =
                        GetField<BackgroundWorker>(
                            form,
                            "pageOrganizerWorker");
                    return !inProgress &&
                        !worker.IsBusy;
                },
                30000,
                timeoutMessage);
            Pump(250);
        }

        private static void AssertWorkspacePdf(
            object workspace,
            string[] expectedLabels,
            int[] expectedRotations,
            string stage)
        {
            var document =
                (PdfiumDocument)GetWorkspaceField(
                    workspace,
                    "Document");
            var thumbnails =
                (PdfThumbnailList)GetWorkspaceField(
                    workspace,
                    "Thumbnails");
            var contentPath =
                (string)GetWorkspaceField(
                    workspace,
                    "ContentPath");

            Require(
                document != null &&
                document.PageCount ==
                    expectedLabels.Length,
                stage +
                ": Pdfium no tiene el número de páginas esperado.");
            Require(
                thumbnails.PageCount ==
                    expectedLabels.Length,
                stage +
                ": las miniaturas no tienen el número de páginas esperado.");
            AssertPdf(
                contentPath,
                expectedLabels,
                expectedRotations,
                stage);
        }

        private static void AssertPdf(
            string path,
            string[] expectedLabels,
            int[] expectedRotations,
            string stage)
        {
            Require(
                File.Exists(path),
                stage + ": no existe la revisión PDF.");
            var bytes = ReadAllBytesShared(path);
            var reader = new PdfReader(bytes);
            try
            {
                Require(
                    reader.NumberOfPages ==
                        expectedLabels.Length,
                    stage +
                    ": páginas=" +
                    reader.NumberOfPages +
                    ", esperadas=" +
                    expectedLabels.Length +
                    ".");
                for (var index = 0;
                    index < expectedLabels.Length;
                    index++)
                {
                    var pageNumber = index + 1;
                    var text =
                        PdfTextExtractor.GetTextFromPage(
                            reader,
                            pageNumber);
                    Require(
                        text != null &&
                        text.Contains(
                            expectedLabels[index]),
                        stage +
                        ": la página " +
                        pageNumber +
                        " no contiene " +
                        expectedLabels[index] +
                        ".");
                    var actualRotation =
                        NormalizeRotation(
                            reader.GetPageRotation(
                                pageNumber));
                    Require(
                        actualRotation ==
                            NormalizeRotation(
                                expectedRotations[index]),
                        stage +
                        ": rotación de página " +
                        pageNumber +
                        "=" +
                        actualRotation +
                        ", esperada=" +
                        expectedRotations[index] +
                        ".");
                }
            }
            finally
            {
                reader.Close();
            }
        }

        private static void CreateFixture(
            string path)
        {
            var labels =
                new[] { "QA-A", "QA-B", "QA-C", "QA-D", "QA-E" };
            using (var stream = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            using (var document =
                new iTextSharp.text.Document(
                    PageSize.A4))
            {
                PdfWriter.GetInstance(
                    document,
                    stream);
                document.Open();
                for (var index = 0;
                    index < labels.Length;
                    index++)
                {
                    if (index > 0)
                    {
                        document.NewPage();
                    }

                    document.Add(
                        new Paragraph(
                            labels[index],
                            FontFactory.GetFont(
                                FontFactory.HELVETICA_BOLD,
                                32f)));
                    document.Add(
                        new Paragraph(
                            "ORGANIZER QA / SOURCE PAGE " +
                            (index + 1)));
                    document.Add(
                        new Paragraph(
                            "Esta página permite verificar orden, " +
                            "giro, borrado y recuperación."));
                }
            }
        }

        private static void RequireSelection(
            PdfThumbnailList thumbnails,
            IEnumerable<int> expected,
            string stage)
        {
            var expectedArray =
                expected.OrderBy(index => index).ToArray();
            var actualArray =
                thumbnails.SelectedPages.ToArray();
            Require(
                actualArray.SequenceEqual(
                    expectedArray),
                stage +
                ": selección=[" +
                string.Join(
                    ",",
                    actualArray.Select(
                        index => index.ToString()).ToArray()) +
                "], esperada=[" +
                string.Join(
                    ",",
                    expectedArray.Select(
                        index => index.ToString()).ToArray()) +
                "].");
        }

        private static int NormalizeRotation(
            int rotation)
        {
            var normalized = rotation % 360;
            if (normalized < 0)
            {
                normalized += 360;
            }

            return normalized;
        }

        private static byte[] ReadAllBytesShared(
            string path)
        {
            using (var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite |
                    FileShare.Delete))
            using (var memory =
                new MemoryStream())
            {
                stream.CopyTo(memory);
                return memory.ToArray();
            }
        }

        private static string ComputeSha256(
            string path)
        {
            using (var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite |
                    FileShare.Delete))
            using (var algorithm =
                SHA256.Create())
            {
                return BitConverter
                    .ToString(
                        algorithm.ComputeHash(
                            stream))
                    .Replace("-", string.Empty);
            }
        }

        private static void SaveControl(
            Control control,
            string path)
        {
            Directory.CreateDirectory(
                IOPath.GetDirectoryName(path));
            var width = Math.Max(
                1,
                control.ClientSize.Width);
            var height = Math.Max(
                1,
                control.ClientSize.Height);
            using (var bitmap =
                new Bitmap(width, height))
            {
                control.DrawToBitmap(
                    bitmap,
                    new DrawingRectangle(
                        Point.Empty,
                        new Size(width, height)));
                bitmap.Save(
                    path,
                    ImageFormat.Png);
            }
        }

        private static void Pump(
            int milliseconds)
        {
            var watch = Stopwatch.StartNew();
            do
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
            while (watch.ElapsedMilliseconds <
                milliseconds);
        }

        private static void PumpUntil(
            Func<bool> predicate,
            int timeoutMilliseconds,
            string timeoutMessage)
        {
            var watch = Stopwatch.StartNew();
            while (!predicate())
            {
                if (watch.ElapsedMilliseconds >=
                    timeoutMilliseconds)
                {
                    throw new TimeoutException(
                        timeoutMessage);
                }

                Application.DoEvents();
                Thread.Sleep(15);
            }
        }

        private static T GetField<T>(
            object target,
            string name)
        {
            var value = GetFieldValue(
                target,
                name);
            return (T)value;
        }

        private static object GetFieldValue(
            object target,
            string name)
        {
            var field = FindField(
                target.GetType(),
                name);
            return field.GetValue(target);
        }

        private static T GetWorkspaceField<T>(
            object workspace,
            string name)
        {
            return (T)GetWorkspaceField(
                workspace,
                name);
        }

        private static object GetWorkspaceField(
            object workspace,
            string name)
        {
            return FindField(
                workspace.GetType(),
                name).GetValue(workspace);
        }

        private static void SetWorkspaceField(
            object workspace,
            string name,
            object value)
        {
            FindField(
                workspace.GetType(),
                name).SetValue(
                    workspace,
                    value);
        }

        private static T GetPrivateField<T>(
            object target,
            string name)
        {
            return (T)FindField(
                target.GetType(),
                name).GetValue(target);
        }

        private static void SetPrivateField(
            object target,
            string name,
            object value)
        {
            FindField(
                target.GetType(),
                name).SetValue(
                    target,
                    value);
        }

        private static FieldInfo FindField(
            Type type,
            string name)
        {
            for (var current = type;
                current != null;
                current = current.BaseType)
            {
                var field = current.GetField(
                    name,
                    BindingFlags.Instance |
                    BindingFlags.Static |
                    BindingFlags.Public |
                    BindingFlags.NonPublic);
                if (field != null)
                {
                    return field;
                }
            }

            throw new MissingFieldException(
                type.FullName,
                name);
        }

        private static object InvokePrivate(
            object target,
            string name,
            params object[] arguments)
        {
            return InvokeMethod(
                target,
                name,
                arguments);
        }

        private static object InvokeProtected(
            object target,
            string name,
            params object[] arguments)
        {
            return InvokeMethod(
                target,
                name,
                arguments);
        }

        private static object InvokeMethod(
            object target,
            string name,
            object[] arguments)
        {
            MethodInfo selected = null;
            for (var current = target.GetType();
                current != null;
                current = current.BaseType)
            {
                foreach (var method in current.GetMethods(
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly))
                {
                    if (method.Name == name &&
                        method.GetParameters().Length ==
                            arguments.Length)
                    {
                        selected = method;
                        break;
                    }
                }

                if (selected != null)
                {
                    break;
                }
            }

            if (selected == null)
            {
                throw new MissingMethodException(
                    target.GetType().FullName,
                    name);
            }

            try
            {
                return selected.Invoke(
                    target,
                    arguments);
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException ?? ex;
            }
        }

        private static void Require(
            bool condition,
            string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(
                    message);
            }
        }

        private static string FormatException(
            Exception exception)
        {
            var baseException =
                exception.GetBaseException();
            return baseException.GetType().Name +
                ": " +
                baseException.Message +
                Environment.NewLine +
                baseException.StackTrace;
        }

        private static void TryDeleteDirectory(
            string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
            }
        }

        private static void TryDeleteFile(
            string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        private sealed class ExpectedLabelRule
        {
            public ExpectedLabelRule(
                int pageIndex,
                PdfName style,
                int start,
                string prefix)
            {
                PageIndex = pageIndex;
                Style = style;
                Start = start;
                Prefix = prefix;
            }

            public int PageIndex { get; private set; }

            public PdfName Style { get; private set; }

            public int Start { get; private set; }

            public string Prefix { get; private set; }
        }
    }
}

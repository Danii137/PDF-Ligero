using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using iTextSharp.text.pdf;
using PdfiumViewer;
using PdfiumDocument = PdfiumViewer.PdfDocument;

namespace FirmaAutomatica
{
    internal static class BookmarkServiceIntegrationQa
    {
        private static readonly List<string> Results = new List<string>();
        private static readonly List<string> Failures = new List<string>();

        private static string runDirectory;
        private static string sourcePath;
        private static string signedSourcePath;

        [STAThread]
        private static int Main(string[] args)
        {
            if (args == null || args.Length < 2)
            {
                Console.Error.WriteLine(
                    "Uso: BookmarkServiceIntegrationQa <run> <fixtures>");
                return 2;
            }

            runDirectory = Path.GetFullPath(args[0]);
            var fixturesDirectory = Path.GetFullPath(args[1]);
            Directory.CreateDirectory(runDirectory);
            sourcePath = Path.Combine(
                fixturesDirectory,
                "bookmark-advanced-fixture.pdf");
            signedSourcePath = Path.Combine(
                fixturesDirectory,
                "bookmark-advanced-signed-fixture.pdf");

            RunSection("MUTACION Y PRESERVACION RAW", TestSaveAndRawPreservation);
            RunSection("FIRMA DIGITAL REAL", TestSignedIncrementalSave);
            RunSection("CANCELACION Y LIMPIEZA", TestCancellation);
            RunSection("HISTORIAL UNDO/REDO", TestEditSessionHistory);
            RunSection("CAMBIO EXTERNO DEL ORIGEN", TestSourceMutationRejection);
            RunSection("COMBINACION", TestMergePreservation);

            var report = new List<string>
            {
                Failures.Count == 0
                    ? "PASS: integración de marcadores validada."
                    : "FAIL: regresiones en la integración de marcadores.",
                "Fecha UTC: " +
                    DateTime.UtcNow.ToString(
                        "yyyy-MM-dd HH:mm:ss",
                        CultureInfo.InvariantCulture) +
                    "Z",
                "Origen: " + sourcePath,
                "Origen firmado: " + signedSourcePath,
                string.Empty
            };
            report.AddRange(Results);
            if (Failures.Count > 0)
            {
                report.Add(string.Empty);
                report.Add("FALLOS");
                report.AddRange(Failures);
            }

            var reportPath = Path.Combine(
                runDirectory,
                "qa-report.txt");
            File.WriteAllLines(reportPath, report.ToArray());
            Console.WriteLine(Failures.Count == 0 ? "PASS" : "FAIL");
            Console.WriteLine("report=" + reportPath);
            Console.WriteLine("failures=" + Failures.Count);
            foreach (var failure in Failures)
            {
                Console.WriteLine(failure);
            }

            return Failures.Count == 0 ? 0 : 1;
        }

        private static void TestSaveAndRawPreservation()
        {
            Require(File.Exists(sourcePath), "Falta el fixture avanzado.");
            var sourceHash = ComputeSha256(sourcePath);
            var outputPath = Path.Combine(
                runDirectory,
                "bookmark-edited-output.pdf");
            TryDeleteFile(outputPath);

            var sourceRaw = CaptureRawSnapshot(sourcePath);
            var document = PdfBookmarkService.Load(sourcePath);
            Require(document.PageCount == 4, "Load: número de páginas.");
            var plan = FindNode(document.Bookmarks, "Plan general / Fit");
            var xyz = FindNode(document.Bookmarks, "Detalle / XYZ");
            var named = FindNode(
                document.Bookmarks,
                "Destino nominal / FitH");
            var namedName = FindNode(
                document.Bookmarks,
                "Destino Name homónimo / FitV");
            var nestedClosed = FindNode(
                document.Bookmarks,
                "Subsección cerrada");
            var layer = FindNode(
                document.Bookmarks,
                "Alternar capa / SetOCGState");
            var script = FindNode(
                document.Bookmarks,
                "Acción JavaScript QA");
            Require(
                plan != null && xyz != null && named != null &&
                namedName != null && nestedClosed != null &&
                layer != null && script != null,
                "Load: árbol completo.");
            Require(
                plan.IsDestinationEditable &&
                xyz.IsDestinationEditable &&
                named.IsDestinationEditable &&
                namedName.IsDestinationEditable,
                "Load: destinos locales editables.");
            Require(
                !layer.IsDestinationEditable &&
                !script.IsDestinationEditable,
                "Load: acciones avanzadas bloqueadas.");
            Require(
                xyz.Destination != null &&
                xyz.Destination.PageNumber == 2 &&
                NearlyEqual(xyz.Destination.Zoom, 1.25D, 0.001D),
                "Load: destino XYZ exacto.");
            Require(
                named.Destination != null &&
                named.Destination.PageNumber == 3 &&
                namedName.Destination != null &&
                namedName.Destination.PageNumber == 4,
                "Load: no distingue PdfString y PdfName homónimos.");
            Require(
                !nestedClosed.IsOpen &&
                nestedClosed.Children.Count == 1 &&
                nestedClosed.Children[0].Title == "Hoja profunda",
                "Load: cierre anidado.");

            var edited = PdfBookmarkService.CloneDocument(document);
            plan = FindNode(edited.Bookmarks, "Plan general / Fit");
            xyz = FindNode(edited.Bookmarks, "Detalle / XYZ");
            named = FindNode(edited.Bookmarks, "Destino nominal / FitH");
            layer = FindNode(
                edited.Bookmarks,
                "Alternar capa / SetOCGState");
            script = FindNode(
                edited.Bookmarks,
                "Acción JavaScript QA");

            PdfBookmarkService.Rename(
                edited,
                plan.Id,
                "Plan general revisado");
            PdfBookmarkService.Rename(
                edited,
                layer.Id,
                "Capa avanzada preservada");
            PdfBookmarkService.SetDestination(
                edited,
                xyz.Id,
                new PdfBookmarkDestination(
                    3,
                    25D,
                    10D,
                    1.5D));
            PdfBookmarkService.Move(
                edited,
                named.Id,
                null,
                1);
            PdfBookmarkService.Move(
                edited,
                named.Id,
                plan.Id,
                0);
            var created = PdfBookmarkService.Create(
                edited,
                plan.Id,
                1,
                "Nuevo detalle",
                new PdfBookmarkDestination(
                    4,
                    33.3D,
                    20D,
                    null));
            PdfBookmarkService.SetOpen(
                edited,
                plan.Id,
                false);
            PdfBookmarkService.Move(
                edited,
                script.Id,
                null,
                1);

            var progress = new List<int>();
            var result = PdfBookmarkService.Save(
                sourcePath,
                edited,
                outputPath,
                delegate(PdfBookmarkProgress item)
                {
                    progress.Add(item.Percentage);
                },
                CancellationToken.None);
            Require(
                result != null &&
                string.Equals(
                    Path.GetFullPath(result.OutputPath),
                    Path.GetFullPath(outputPath),
                    StringComparison.OrdinalIgnoreCase) &&
                result.BookmarkCount == 9,
                "Save: resultado y recuento.");
            Require(
                progress.Count > 0 &&
                progress[progress.Count - 1] == 100,
                "Save: progreso completo.");
            Require(
                string.Equals(
                    sourceHash,
                    ComputeSha256(sourcePath),
                    StringComparison.Ordinal),
                "Save modificó el original.");

            var reloaded = PdfBookmarkService.Load(outputPath);
            ValidateEditedModel(reloaded);
            var outputRaw = CaptureRawSnapshot(outputPath);
            ValidateRawPreservation(
                sourceRaw,
                outputRaw,
                false);
            ValidateEditedRawDestinations(outputPath);
            AssertRenderedPagesEqual(
                sourcePath,
                outputPath,
                Path.Combine(
                    runDirectory,
                    "edited-page-1.png"));
            AssertNoTemporaryFiles(outputPath);
            Results.Add(
                "  PASS crear/renombrar/mover/nivel/destino/open; " +
                "GoTo+/Next, /Count anidado, Name/String homónimos, " +
                "raw avanzado, formulario, enlace, metadata y render.");
        }

        private static void ValidateEditedModel(
            PdfBookmarkDocument document)
        {
            Require(document.Bookmarks.Count == 3, "Raíces editadas.");
            Require(
                document.Bookmarks[0].Title ==
                    "Plan general revisado" &&
                document.Bookmarks[1].Title ==
                    "Acción JavaScript QA" &&
                document.Bookmarks[2].Title ==
                    "Capa avanzada preservada",
                "Orden raíz editado.");
            var plan = document.Bookmarks[0];
            Require(!plan.IsOpen, "Estado cerrado.");
            Require(plan.Children.Count == 4, "Jerarquía del plan.");
            var xyz = FindNode(plan.Children, "Detalle / XYZ");
            var created = FindNode(plan.Children, "Nuevo detalle");
            Require(
                xyz != null &&
                xyz.Destination.PageNumber == 3 &&
                NearlyEqual(
                    xyz.Destination.TopPositionPercent,
                    25D,
                    0.02D) &&
                NearlyEqual(
                    xyz.Destination.LeftPositionPercent,
                    10D,
                    0.02D) &&
                NearlyEqual(
                    xyz.Destination.Zoom,
                    1.5D,
                    0.001D),
                "Destino XYZ cambiado.");
            Require(
                created != null &&
                created.Destination.PageNumber == 4 &&
                NearlyEqual(
                    created.Destination.TopPositionPercent,
                    33.3D,
                    0.02D),
                "Marcador nuevo.");
            Require(
                !document.Bookmarks[1].IsDestinationEditable &&
                !document.Bookmarks[2].IsDestinationEditable,
                "Acciones avanzadas siguen bloqueadas.");
            var named = FindNode(
                plan.Children,
                "Destino nominal / FitH");
            var namedName = FindNode(
                plan.Children,
                "Destino Name homónimo / FitV");
            var nestedClosed = FindNode(
                plan.Children,
                "Subsección cerrada");
            Require(
                named != null &&
                named.Destination.PageNumber == 3 &&
                namedName != null &&
                namedName.Destination.PageNumber == 4,
                "Colisión Name/String tras Save.");
            Require(
                nestedClosed != null &&
                !nestedClosed.IsOpen &&
                nestedClosed.Children.Count == 1,
                "Cierre anidado tras Save.");
        }

        private static void TestSignedIncrementalSave()
        {
            Require(
                File.Exists(signedSourcePath),
                "Falta el fixture firmado.");
            var sourceHash = ComputeSha256(signedSourcePath);
            var outputPath = Path.Combine(
                runDirectory,
                "bookmark-signed-edited-output.pdf");
            TryDeleteFile(outputPath);
            var sourceRaw = CaptureRawSnapshot(signedSourcePath);
            var document =
                PdfBookmarkService.Load(signedSourcePath);
            Require(
                document.ContainsDigitalSignatures,
                "Load no detectó firma.");
            var plan = FindNode(
                document.Bookmarks,
                "Plan general / Fit");
            PdfBookmarkService.Rename(
                document,
                plan.Id,
                "Plan firmado revisado");
            var result = PdfBookmarkService.Save(
                signedSourcePath,
                document,
                outputPath,
                null,
                CancellationToken.None);
            Require(
                result.DigitalSignaturesInvalidated &&
                !string.IsNullOrWhiteSpace(
                    result.DigitalSignatureWarning),
                "Aviso de firma.");
            Require(
                string.Equals(
                    sourceHash,
                    ComputeSha256(signedSourcePath),
                    StringComparison.Ordinal),
                "Save firmado modificó el original.");

            var outputRaw = CaptureRawSnapshot(outputPath);
            ValidateRawPreservation(
                sourceRaw,
                outputRaw,
                true);
            ValidateSignatureAfterAppend(
                signedSourcePath,
                outputPath);
            AssertRenderedPagesEqual(
                signedSourcePath,
                outputPath,
                Path.Combine(
                    runDirectory,
                    "signed-edited-page-1.png"));
            AssertNoTemporaryFiles(outputPath);
            Results.Add(
                "  PASS firma real permanece criptográficamente verificable " +
                "y se informa modificación posterior.");
        }

        private static void TestCancellation()
        {
            var outputPath = Path.Combine(
                runDirectory,
                "bookmark-cancelled-output.pdf");
            TryDeleteFile(outputPath);
            var sourceHash = ComputeSha256(sourcePath);
            var document = PdfBookmarkService.Load(sourcePath);
            var plan = FindNode(
                document.Bookmarks,
                "Plan general / Fit");
            PdfBookmarkService.Rename(
                document,
                plan.Id,
                "No debe publicarse");
            using (var cancellation =
                new CancellationTokenSource())
            {
                var cancelled = false;
                try
                {
                    PdfBookmarkService.Save(
                        sourcePath,
                        document,
                        outputPath,
                        delegate(PdfBookmarkProgress progress)
                        {
                            if (progress.CompletedSteps >= 2)
                            {
                                cancellation.Cancel();
                            }
                        },
                        cancellation.Token);
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                }

                Require(cancelled, "Save no notificó cancelación.");
            }

            Require(
                !File.Exists(outputPath),
                "Cancelación publicó una salida.");
            AssertNoTemporaryFiles(outputPath);
            Require(
                string.Equals(
                    sourceHash,
                    ComputeSha256(sourcePath),
                    StringComparison.Ordinal),
                "Cancelación modificó el original.");
            Results.Add(
                "  PASS cancelación en escritura: sin salida ni temporales.");
        }

        private static void TestEditSessionHistory()
        {
            var historyDirectory = Path.Combine(
                runDirectory,
                "history");
            RecreateDirectory(historyDirectory);
            var recoveryRoot = Path.Combine(
                historyDirectory,
                "recovery");
            Directory.CreateDirectory(recoveryRoot);
            Environment.SetEnvironmentVariable(
                PdfEditSession.RecoveryRootOverrideEnvironmentVariable,
                recoveryRoot);
            var historySource = Path.Combine(
                historyDirectory,
                "history-source.pdf");
            File.Copy(sourcePath, historySource, true);
            var session = PdfEditSession.Create(historySource);
            try
            {
                var document =
                    PdfBookmarkService.Load(historySource);
                var plan = FindNode(
                    document.Bookmarks,
                    "Plan general / Fit");
                PdfBookmarkService.Rename(
                    document,
                    plan.Id,
                    "Historial aplicado");
                var revisionPath = session.ReserveRevisionPath(
                    new FileInfo(historySource).Length +
                    (1024 * 1024));
                PdfBookmarkService.Save(
                    historySource,
                    document,
                    revisionPath,
                    null,
                    CancellationToken.None);
                var commit = session.BeginRevisionCommit(
                    revisionPath,
                    "Editar marcadores");
                commit.Complete();

                Require(
                    session.CanUndo && !session.CanRedo,
                    "Commit no habilitó Undo.");
                var undoPath = session.Undo();
                Require(
                    Path.GetFullPath(undoPath) ==
                        Path.GetFullPath(historySource) &&
                    FindNode(
                        PdfBookmarkService.Load(undoPath).Bookmarks,
                        "Plan general / Fit") != null,
                    "Undo no restauró árbol original.");
                var redoPath = session.Redo();
                Require(
                    Path.GetFullPath(redoPath) ==
                        Path.GetFullPath(revisionPath) &&
                    FindNode(
                        PdfBookmarkService.Load(redoPath).Bookmarks,
                        "Historial aplicado") != null,
                    "Redo no restauró edición.");
                Results.Add(
                    "  PASS revisión transaccional, Undo y Redo.");
            }
            finally
            {
                session.DeleteRecovery();
                Environment.SetEnvironmentVariable(
                    PdfEditSession.RecoveryRootOverrideEnvironmentVariable,
                    null);
            }
        }

        private static void TestSourceMutationRejection()
        {
            var mutationDirectory = Path.Combine(
                runDirectory,
                "source-mutation");
            RecreateDirectory(mutationDirectory);
            var mutatedSource = Path.Combine(
                mutationDirectory,
                "source.pdf");
            var outputPath = Path.Combine(
                mutationDirectory,
                "should-not-exist.pdf");
            File.Copy(sourcePath, mutatedSource, true);
            long paddingStart;
            using (var stream = new FileStream(
                mutatedSource,
                FileMode.Append,
                FileAccess.Write,
                FileShare.None))
            {
                var prefix =
                    Encoding.ASCII.GetBytes("\r\n%");
                stream.Write(prefix, 0, prefix.Length);
                paddingStart = stream.Position;
                var padding = Enumerable.Repeat(
                    (byte)'A',
                    1024 * 1024).ToArray();
                stream.Write(padding, 0, padding.Length);
                stream.WriteByte((byte)'\r');
                stream.WriteByte((byte)'\n');
            }
            var originalTimestamp =
                File.GetLastWriteTimeUtc(mutatedSource);
            var document =
                PdfBookmarkService.Load(mutatedSource);
            var bytes = File.ReadAllBytes(mutatedSource);
            var originalLength = bytes.Length;
            var offset = checked(
                (int)paddingStart +
                (256 * 1024) +
                137);
            Require(
                offset > 64 * 1024 &&
                offset < bytes.Length / 2 - 64 * 1024,
                "La mutación no cayó en el hueco del muestreo antiguo.");
            bytes[offset] = (byte)'B';
            File.WriteAllBytes(mutatedSource, bytes);
            File.SetLastWriteTimeUtc(
                mutatedSource,
                originalTimestamp);
            Require(
                new FileInfo(mutatedSource).Length == originalLength &&
                File.GetLastWriteTimeUtc(mutatedSource) ==
                    originalTimestamp,
                "La mutación cambió tamaño o fecha.");

            Exception rejected = null;
            try
            {
                PdfBookmarkService.Save(
                    mutatedSource,
                    document,
                    outputPath,
                    null,
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                rejected = ex.GetBaseException();
            }

            Require(
                rejected != null &&
                rejected.Message.IndexOf(
                    "cambio",
                    StringComparison.OrdinalIgnoreCase) >= 0,
                "No se rechazó origen cambiado con mismo tamaño/fecha.");
            Require(
                !File.Exists(outputPath),
                "Mutación externa publicó salida.");
            AssertNoTemporaryFiles(outputPath);
            Results.Add(
                "  PASS SHA-256 completo detecta cambio en hueco del " +
                "muestreo antiguo con mismo tamaño y fecha.");
        }

        private static void TestMergePreservation()
        {
            var firstPath = Path.Combine(
                runDirectory,
                "bookmark-edited-output.pdf");
            Require(
                File.Exists(firstPath),
                "Falta salida editada para combinar.");
            var secondPath = Path.Combine(
                runDirectory,
                "bookmark-edited-second.pdf");
            var mergedPath = Path.Combine(
                runDirectory,
                "bookmark-merged-output.pdf");
            File.Copy(firstPath, secondPath, true);
            TryDeleteFile(mergedPath);
            var result = PdfMergeService.Merge(
                new[] { firstPath, secondPath },
                mergedPath,
                null);
            Require(
                result.PageCount == 8,
                "Combinación: páginas.");
            var reader = new PdfReader(mergedPath);
            try
            {
                var advancedActions =
                    ReadAdvancedOutlineActionTypes(reader);
                Require(
                    advancedActions.Count(item =>
                        PdfName.SETOCGSTATE.ToString() == item) == 2,
                    "Combinar perdió SetOCGState.");
                Require(
                    advancedActions.Count(item =>
                        PdfName.JAVASCRIPT.ToString() == item) == 2,
                    "Combinar perdió JavaScript.");
                var bookmarks =
                    SimpleBookmark.GetBookmark(reader);
                Require(
                    CountSimpleBookmarks(bookmarks) == 18,
                    "Combinar: recuento de marcadores.");
                Require(
                    CountChainedOutlineActions(
                        reader,
                        PdfName.JAVASCRIPT,
                        "keep-goto-next-raw") == 2,
                    "Combinar perdió /A/Next JavaScript o su payload.");
                ValidateMergedNamedDestinationCollisions(reader);
            }
            finally
            {
                reader.Close();
            }

            Results.Add(
                "  PASS combinación conserva árbol, GoTo+/Next, acciones " +
                "avanzadas y destinos Name/String con offsets exactos.");
        }

        private static RawSnapshot CaptureRawSnapshot(string path)
        {
            var reader = new PdfReader(path);
            try
            {
                return new RawSnapshot(
                    reader.NumberOfPages,
                    CapturePageSizes(reader),
                    CaptureMetadata(reader),
                    CaptureNamedDestinations(reader),
                    CaptureFormState(reader),
                    CaptureLinkActions(reader),
                    CaptureFirstOutlineAction(
                        reader,
                        "Alternar capa / SetOCGState",
                        "Capa avanzada preservada"),
                    CaptureOutlineAction(
                        reader,
                        "Acción JavaScript QA"),
                    CaptureGoToActionTail(
                        reader,
                        "Detalle / XYZ"),
                    CaptureFirstOutlineDestination(
                        reader,
                        "Plan general / Fit",
                        "Plan general revisado",
                        "Plan firmado revisado"),
                    CaptureOutlineDestination(
                        reader,
                        "Destino nominal / FitH"));
            }
            finally
            {
                reader.Close();
            }
        }

        private static void ValidateRawPreservation(
            RawSnapshot source,
            RawSnapshot output,
            bool signed)
        {
            Require(
                source.PageCount == output.PageCount,
                "Cambió número de páginas.");
            Require(
                SequenceEqual(source.PageSizes, output.PageSizes),
                "Cambiaron tamaños/rotaciones.");
            Require(
                string.Equals(
                    source.Metadata,
                    output.Metadata,
                    StringComparison.Ordinal),
                "Cambiaron metadatos.\nFUENTE:\n" +
                    source.Metadata +
                    "\nSALIDA:\n" +
                    output.Metadata);
            Require(
                string.Equals(
                    source.NamedDestinations,
                    output.NamedDestinations,
                    StringComparison.Ordinal),
                "Cambió /Names/Dests.");
            Require(
                string.Equals(
                    source.FormState,
                    output.FormState,
                    StringComparison.Ordinal),
                "Cambió AcroForm.");
            Require(
                string.Equals(
                    source.LinkActions,
                    output.LinkActions,
                    StringComparison.Ordinal),
                "Cambiaron enlaces.");
            Require(
                string.Equals(
                    source.SetOcgAction,
                    output.SetOcgAction,
                    StringComparison.Ordinal),
                "Cambió acción SetOCGState.");
            Require(
                string.Equals(
                    source.JavaScriptAction,
                    output.JavaScriptAction,
                    StringComparison.Ordinal),
                "Cambió acción JavaScript.");
            Require(
                string.Equals(
                    source.GoToActionTail,
                    output.GoToActionTail,
                    StringComparison.Ordinal),
                "Cambió /A /GoTo fuera de /D o se perdió /Next.");
            Require(
                string.Equals(
                    source.PlanDestination,
                    output.PlanDestination,
                    StringComparison.Ordinal),
                "Renombrar cambió /Fit.");
            Require(
                string.Equals(
                    source.NamedOutlineDestination,
                    output.NamedOutlineDestination,
                    StringComparison.Ordinal),
                "Mover cambió destino nominal.");
        }

        private static void ValidateEditedRawDestinations(string path)
        {
            var reader = new PdfReader(path);
            try
            {
                var xyz = FindRawOutline(
                    reader,
                    "Detalle / XYZ");
                var xyzAction = ResolveDictionary(
                    xyz.Get(PdfName.A));
                var destination = ResolveArray(
                    xyzAction == null
                        ? null
                        : xyzAction.Get(PdfName.D));
                Require(
                    xyz.Get(PdfName.DEST) == null &&
                    xyzAction != null &&
                    PdfName.GOTO.Equals(
                        xyzAction.GetAsName(PdfName.S)) &&
                    destination != null &&
                    PdfName.XYZ.Equals(
                        destination.GetAsName(1)) &&
                    ResolveDestinationPage(reader, destination) == 3 &&
                    NearlyEqual(
                        destination.GetAsNumber(4).DoubleValue,
                        1.5D,
                        0.001D),
                    "Destino raw XYZ editado dentro de /A/D.");
                var chainedAction = ResolveDictionary(
                    xyzAction.Get(PdfName.NEXT));
                Require(
                    string.Equals(
                        xyzAction.GetAsString(
                            new PdfName("QAActionFlag"))
                            .ToUnicodeString(),
                        "keep-goto-action-raw",
                        StringComparison.Ordinal) &&
                    chainedAction != null &&
                    PdfName.JAVASCRIPT.Equals(
                        chainedAction.GetAsName(PdfName.S)) &&
                    chainedAction.GetAsString(PdfName.JS)
                        .ToUnicodeString()
                        .Contains("never execute") &&
                    string.Equals(
                        chainedAction.GetAsString(
                            new PdfName("QAFlag"))
                            .ToUnicodeString(),
                        "keep-goto-next-raw",
                        StringComparison.Ordinal),
                    "SetDestination perdió claves de /A o /A/Next.");
                var created = FindRawOutline(
                    reader,
                    "Nuevo detalle");
                var createdDestination = ResolveArray(
                    created.Get(PdfName.DEST));
                Require(
                    createdDestination != null &&
                    PdfName.XYZ.Equals(
                        createdDestination.GetAsName(1)) &&
                    ResolveDestinationPage(
                        reader,
                        createdDestination) == 4,
                    "Destino raw nuevo.");
                var plan = FindRawOutline(
                    reader,
                    "Plan general revisado");
                Require(
                    plan.GetAsNumber(PdfName.COUNT).IntValue == -5,
                    "Raw /Count cerrado no respeta descendientes visibles.");
                var root = ResolveDictionary(
                    reader.Catalog.Get(PdfName.OUTLINES));
                Require(
                    root.GetAsNumber(PdfName.COUNT).IntValue == 3,
                    "Raw /Outlines/Count no respeta la rama cerrada.");
                var named = FindRawOutline(
                    reader,
                    "Destino nominal / FitH");
                var nestedClosed = FindRawOutline(
                    reader,
                    "Subsección cerrada");
                Require(
                    named.GetAsNumber(PdfName.COUNT).IntValue == 1 &&
                    nestedClosed.GetAsNumber(
                        PdfName.COUNT).IntValue == -1,
                    "Raw /Count anidado cerrado incorrecto.");
                var namedName = FindRawOutline(
                    reader,
                    "Destino Name homónimo / FitV");
                Require(
                    named.Get(PdfName.DEST) is PdfString &&
                    namedName.Get(PdfName.DEST) is PdfName,
                    "Save colapsó PdfString y PdfName homónimos.");
            }
            finally
            {
                reader.Close();
            }
        }

        private static void ValidateSignatureAfterAppend(
            string source,
            string output)
        {
            string sourceDictionary;
            using (var reader = new PdfReader(source))
            {
                var fields = reader.AcroFields;
                Require(
                    fields.VerifySignature(
                        "signature.pending").Verify(),
                    "Firma fuente no válida.");
                Require(
                    fields.SignatureCoversWholeDocument(
                        "signature.pending"),
                    "Firma fuente no cubre el documento.");
                sourceDictionary = Canonicalize(
                    fields.GetSignatureDictionary(
                        "signature.pending"));
            }

            using (var reader = new PdfReader(output))
            {
                var fields = reader.AcroFields;
                Require(
                    fields.GetSignatureNames()
                        .Contains("signature.pending"),
                    "Desapareció firma.");
                Require(
                    fields.VerifySignature(
                        "signature.pending").Verify(),
                    "Firma dejó de ser criptográficamente verificable.");
                Require(
                    !fields.SignatureCoversWholeDocument(
                        "signature.pending"),
                    "No se detecta la revisión posterior a la firma.");
                var outputDictionary = Canonicalize(
                    fields.GetSignatureDictionary(
                        "signature.pending"));
                Require(
                    string.Equals(
                        sourceDictionary,
                        outputDictionary,
                        StringComparison.Ordinal),
                    "Cambió el diccionario de firma.");
            }
        }

        private static void AssertRenderedPagesEqual(
            string firstPath,
            string secondPath,
            string capturePath)
        {
            using (var first = PdfiumDocument.Load(firstPath))
            using (var second = PdfiumDocument.Load(secondPath))
            {
                Require(
                    first.PageCount == second.PageCount,
                    "Render: páginas.");
                for (var page = 0; page < first.PageCount; page++)
                {
                    var width = 650;
                    var ratio =
                        first.PageSizes[page].Height /
                        first.PageSizes[page].Width;
                    var height = Math.Max(
                        1,
                        (int)Math.Round(width * ratio));
                    using (var firstImage = first.Render(
                        page,
                        width,
                        height,
                        96f,
                        96f,
                        PdfRenderFlags.Annotations))
                    using (var secondImage = second.Render(
                        page,
                        width,
                        height,
                        96f,
                        96f,
                        PdfRenderFlags.Annotations))
                    {
                        Require(
                            string.Equals(
                                ComputePixelHash(firstImage),
                                ComputePixelHash(secondImage),
                                StringComparison.Ordinal),
                            "Render cambió en página " +
                            (page + 1).ToString(
                                CultureInfo.InvariantCulture) +
                            ".");
                        if (page == 0)
                        {
                            secondImage.Save(
                                capturePath,
                                ImageFormat.Png);
                        }
                    }
                }
            }
        }

        private static string ComputePixelHash(Image image)
        {
            using (var bitmap = new Bitmap(image))
            {
                var rectangle = new System.Drawing.Rectangle(
                    0,
                    0,
                    bitmap.Width,
                    bitmap.Height);
                var data = bitmap.LockBits(
                    rectangle,
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format32bppArgb);
                try
                {
                    var stride = Math.Abs(data.Stride);
                    var bytes = new byte[stride * bitmap.Height];
                    Marshal.Copy(
                        data.Scan0,
                        bytes,
                        0,
                        bytes.Length);
                    using (var sha = SHA256.Create())
                    {
                        return ToHex(sha.ComputeHash(bytes));
                    }
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }
            }
        }

        private static List<string> CapturePageSizes(PdfReader reader)
        {
            var result = new List<string>();
            for (var page = 1; page <= reader.NumberOfPages; page++)
            {
                var box = reader.GetCropBox(page);
                result.Add(
                    box.Left.ToString("R", CultureInfo.InvariantCulture) +
                    "," +
                    box.Bottom.ToString("R", CultureInfo.InvariantCulture) +
                    "," +
                    box.Right.ToString("R", CultureInfo.InvariantCulture) +
                    "," +
                    box.Top.ToString("R", CultureInfo.InvariantCulture) +
                    "," +
                    reader.GetPageRotation(page).ToString(
                        CultureInfo.InvariantCulture));
            }

            return result;
        }

        private static string CaptureMetadata(PdfReader reader)
        {
            return string.Join(
                "\n",
                reader.Info
                    .Where(item => !string.Equals(
                        item.Key,
                        "ModDate",
                        StringComparison.Ordinal))
                    .OrderBy(item => item.Key, StringComparer.Ordinal)
                    .Select(item => item.Key + "=" + item.Value)
                    .ToArray()) +
                "\nXMP=" +
                ComputeBytesHash(reader.Metadata);
        }

        private static string CaptureNamedDestinations(
            PdfReader reader)
        {
            var values = new List<string>();
            foreach (var item in
                reader.GetNamedDestination(true)
                    .Select(item => new
                    {
                        Key = GetTypedNamedDestinationKey(item.Key),
                        Value = item.Value
                    })
                    .OrderBy(
                        item => item.Key,
                        StringComparer.Ordinal))
            {
                values.Add(
                    item.Key +
                    "=" +
                    Canonicalize(item.Value));
            }

            return string.Join("\n", values.ToArray());
        }

        private static string GetTypedNamedDestinationKey(
            object value)
        {
            var name = value as PdfName;
            if (name != null)
            {
                return "N:" +
                    PdfName.DecodeName(name.ToString());
            }
            var text = value as PdfString;
            if (text != null)
            {
                return "S:" + text.ToUnicodeString();
            }
            return "S:" +
                (value == null
                    ? string.Empty
                    : value.ToString());
        }

        private static string CaptureFormState(PdfReader reader)
        {
            var values = new List<string>();
            foreach (var item in reader.AcroFields.Fields
                .OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                var field = item.Value;
                values.Add(
                    item.Key +
                    "=" +
                    reader.AcroFields.GetField(item.Key) +
                    "|widgets=" +
                    field.Size.ToString(
                        CultureInfo.InvariantCulture));
                for (var index = 0; index < field.Size; index++)
                {
                    var widget = field.GetWidget(index);
                    var appearance = widget == null
                        ? null
                        : widget.Get(PdfName.AP);
                    values.Add(
                        "  ap=" +
                        (appearance == null
                            ? "none"
                            : "present"));
                }
            }

            return string.Join("\n", values.ToArray());
        }

        private static string CaptureLinkActions(PdfReader reader)
        {
            var actions = new List<string>();
            for (var page = 1; page <= reader.NumberOfPages; page++)
            {
                var annotations = reader.GetPageN(page)
                    .GetAsArray(PdfName.ANNOTS);
                if (annotations == null)
                {
                    continue;
                }

                for (var index = 0; index < annotations.Size; index++)
                {
                    var annotation =
                        ResolveDictionary(annotations[index]);
                    if (annotation != null &&
                        PdfName.LINK.Equals(
                            annotation.GetAsName(
                                PdfName.SUBTYPE)))
                    {
                        actions.Add(
                            page.ToString(
                                CultureInfo.InvariantCulture) +
                            ":" +
                            Canonicalize(
                                annotation.Get(PdfName.A)));
                    }
                }
            }

            return string.Join("\n", actions.ToArray());
        }

        private static string CaptureOutlineAction(
            PdfReader reader,
            string title)
        {
            var outline = FindRawOutline(reader, title);
            return outline == null
                ? string.Empty
                : Canonicalize(outline.Get(PdfName.A));
        }

        private static string CaptureGoToActionTail(
            PdfReader reader,
            string title)
        {
            var outline = FindRawOutline(reader, title);
            var action = outline == null
                ? null
                : ResolveDictionary(outline.Get(PdfName.A));
            if (action == null)
            {
                return string.Empty;
            }

            return "S=" +
                Canonicalize(action.Get(PdfName.S)) +
                "|Next=" +
                Canonicalize(action.Get(PdfName.NEXT)) +
                "|QAActionFlag=" +
                Canonicalize(
                    action.Get(new PdfName("QAActionFlag")));
        }

        private static string CaptureFirstOutlineAction(
            PdfReader reader,
            params string[] titles)
        {
            foreach (var title in titles)
            {
                var outline = FindRawOutline(reader, title);
                if (outline != null)
                {
                    return Canonicalize(outline.Get(PdfName.A));
                }
            }
            return string.Empty;
        }

        private static string CaptureOutlineDestination(
            PdfReader reader,
            string title)
        {
            var outline = FindRawOutline(reader, title);
            return outline == null
                ? string.Empty
                : Canonicalize(outline.Get(PdfName.DEST));
        }

        private static string CaptureFirstOutlineDestination(
            PdfReader reader,
            params string[] titles)
        {
            foreach (var title in titles)
            {
                var outline = FindRawOutline(reader, title);
                if (outline != null)
                {
                    return Canonicalize(outline.Get(PdfName.DEST));
                }
            }
            return string.Empty;
        }

        private static PdfDictionary FindRawOutline(
            PdfReader reader,
            string title)
        {
            var root = ResolveDictionary(
                reader.Catalog.Get(PdfName.OUTLINES));
            return root == null
                ? null
                : FindRawOutline(
                    root.Get(PdfName.FIRST),
                    title,
                    new HashSet<string>(
                        StringComparer.Ordinal),
                    0);
        }

        private static PdfDictionary FindRawOutline(
            PdfObject first,
            string title,
            ISet<string> visited,
            int depth)
        {
            if (depth > 256)
            {
                return null;
            }

            var current = first;
            while (current != null)
            {
                var reference =
                    current as PdfIndirectReference;
                if (reference != null &&
                    !visited.Add(
                        reference.Number.ToString(
                            CultureInfo.InvariantCulture) +
                        ":" +
                        reference.Generation.ToString(
                            CultureInfo.InvariantCulture)))
                {
                    return null;
                }
                var dictionary = ResolveDictionary(current);
                if (dictionary == null)
                {
                    return null;
                }
                var value = dictionary.GetAsString(PdfName.TITLE);
                if (value != null &&
                    string.Equals(
                        value.ToUnicodeString(),
                        title,
                        StringComparison.Ordinal))
                {
                    return dictionary;
                }
                var nested = FindRawOutline(
                    dictionary.Get(PdfName.FIRST),
                    title,
                    visited,
                    depth + 1);
                if (nested != null)
                {
                    return nested;
                }
                current = dictionary.Get(PdfName.NEXT);
            }

            return null;
        }

        private static List<string>
            ReadAdvancedOutlineActionTypes(PdfReader reader)
        {
            var result = new List<string>();
            var root = ResolveDictionary(
                reader.Catalog.Get(PdfName.OUTLINES));
            if (root != null)
            {
                ReadAdvancedOutlineActionTypes(
                    root.Get(PdfName.FIRST),
                    result,
                    new HashSet<string>(
                        StringComparer.Ordinal),
                    0);
            }

            return result;
        }

        private static void ReadAdvancedOutlineActionTypes(
            PdfObject first,
            IList<string> result,
            ISet<string> visited,
            int depth)
        {
            if (depth > 256)
            {
                return;
            }
            var current = first;
            while (current != null)
            {
                var reference =
                    current as PdfIndirectReference;
                if (reference != null &&
                    !visited.Add(
                        reference.Number.ToString(
                            CultureInfo.InvariantCulture) +
                        ":" +
                        reference.Generation.ToString(
                            CultureInfo.InvariantCulture)))
                {
                    return;
                }
                var dictionary = ResolveDictionary(current);
                if (dictionary == null)
                {
                    return;
                }
                var action = ResolveDictionary(
                    dictionary.Get(PdfName.A));
                var actionType = action == null
                    ? null
                    : action.GetAsName(PdfName.S);
                if (actionType != null)
                {
                    result.Add(actionType.ToString());
                }
                ReadAdvancedOutlineActionTypes(
                    dictionary.Get(PdfName.FIRST),
                    result,
                    visited,
                    depth + 1);
                current = dictionary.Get(PdfName.NEXT);
            }
        }

        private static int CountChainedOutlineActions(
            PdfReader reader,
            PdfName expectedType,
            string expectedPayload)
        {
            var root = ResolveDictionary(
                reader.Catalog.Get(PdfName.OUTLINES));
            return root == null
                ? 0
                : CountChainedOutlineActions(
                    root.Get(PdfName.FIRST),
                    expectedType,
                    expectedPayload,
                    new HashSet<string>(
                        StringComparer.Ordinal),
                    0);
        }

        private static int CountChainedOutlineActions(
            PdfObject first,
            PdfName expectedType,
            string expectedPayload,
            ISet<string> visited,
            int depth)
        {
            if (depth > 256)
            {
                return 0;
            }
            var count = 0;
            var current = first;
            while (current != null)
            {
                var reference =
                    current as PdfIndirectReference;
                if (reference != null &&
                    !visited.Add(
                        reference.Number.ToString(
                            CultureInfo.InvariantCulture) +
                        ":" +
                        reference.Generation.ToString(
                            CultureInfo.InvariantCulture)))
                {
                    return count;
                }
                var dictionary = ResolveDictionary(current);
                if (dictionary == null)
                {
                    return count;
                }
                var action = ResolveDictionary(
                    dictionary.Get(PdfName.A));
                var chained = action == null
                    ? null
                    : ResolveDictionary(
                        action.Get(PdfName.NEXT));
                if (chained != null &&
                    expectedType.Equals(
                        chained.GetAsName(PdfName.S)))
                {
                    var payload = chained.GetAsString(
                        new PdfName("QAFlag"));
                    if (payload != null &&
                        string.Equals(
                            payload.ToUnicodeString(),
                            expectedPayload,
                            StringComparison.Ordinal))
                    {
                        count++;
                    }
                }
                count += CountChainedOutlineActions(
                    dictionary.Get(PdfName.FIRST),
                    expectedType,
                    expectedPayload,
                    visited,
                    depth + 1);
                current = dictionary.Get(PdfName.NEXT);
            }
            return count;
        }

        private static void ValidateMergedNamedDestinationCollisions(
            PdfReader reader)
        {
            var strings = FindRawOutlines(
                reader,
                "Destino nominal / FitH");
            var names = FindRawOutlines(
                reader,
                "Destino Name homónimo / FitV");
            Require(
                strings.Count == 2 &&
                names.Count == 2,
                "Combinar perdió destinos homónimos.");
            for (var index = 0; index < 2; index++)
            {
                var stringRaw =
                    strings[index].Get(PdfName.DEST);
                var nameRaw =
                    names[index].Get(PdfName.DEST);
                var stringDestination =
                    ResolveNamedOutlineDestination(
                        reader,
                        stringRaw);
                var nameDestination =
                    ResolveNamedOutlineDestination(
                        reader,
                        nameRaw);
                Require(
                    stringDestination != null &&
                    PdfName.FITH.Equals(
                        stringDestination.GetAsName(1)) &&
                    ResolveDestinationPage(
                        reader,
                        stringDestination) == 3 + (index * 4) &&
                    NearlyEqual(
                        stringDestination.GetAsNumber(2)
                            .DoubleValue,
                        700D,
                        0.001D),
                    "Destino String combinado incorrecto.");
                Require(
                    nameDestination != null &&
                    PdfName.FITV.Equals(
                        nameDestination.GetAsName(1)) &&
                    ResolveDestinationPage(
                        reader,
                        nameDestination) == 4 + (index * 4) &&
                    NearlyEqual(
                        nameDestination.GetAsNumber(2)
                            .DoubleValue,
                        36D,
                        0.001D),
                    "Destino Name combinado incorrecto.");
            }
        }

        private static PdfArray ResolveNamedOutlineDestination(
            PdfReader reader,
            PdfObject rawDestination)
        {
            var direct = ResolveArray(rawDestination);
            if (direct != null)
            {
                return direct;
            }
            var resolved = PdfReader.GetPdfObject(rawDestination);
            var destinationName = resolved as PdfName;
            var destinationText = resolved as PdfString;
            foreach (var item in reader.GetNamedDestination(true))
            {
                var keyName = item.Key as PdfName;
                var keyText = item.Key as PdfString;
                var keyString = item.Key as string;
                if (destinationName != null &&
                    keyName != null &&
                    string.Equals(
                        PdfName.DecodeName(
                            destinationName.ToString()),
                        PdfName.DecodeName(
                            keyName.ToString()),
                        StringComparison.Ordinal))
                {
                    return ResolveArray(item.Value);
                }
                if (destinationText != null &&
                    ((keyText != null &&
                      string.Equals(
                          destinationText.ToUnicodeString(),
                          keyText.ToUnicodeString(),
                          StringComparison.Ordinal)) ||
                     (keyString != null &&
                      string.Equals(
                          destinationText.ToUnicodeString(),
                          keyString,
                          StringComparison.Ordinal))))
                {
                    return ResolveArray(item.Value);
                }
            }
            return null;
        }

        private static string DescribePdfObject(PdfObject value)
        {
            var resolved = value == null
                ? null
                : PdfReader.GetPdfObject(value);
            return resolved == null
                ? "null"
                : resolved.GetType().Name +
                    ":" +
                    Canonicalize(value);
        }

        private static List<PdfDictionary> FindRawOutlines(
            PdfReader reader,
            string title)
        {
            var result = new List<PdfDictionary>();
            var root = ResolveDictionary(
                reader.Catalog.Get(PdfName.OUTLINES));
            if (root != null)
            {
                FindRawOutlines(
                    root.Get(PdfName.FIRST),
                    title,
                    result,
                    new HashSet<string>(
                        StringComparer.Ordinal),
                    0);
            }
            return result;
        }

        private static void FindRawOutlines(
            PdfObject first,
            string title,
            IList<PdfDictionary> result,
            ISet<string> visited,
            int depth)
        {
            if (depth > 256)
            {
                return;
            }
            var current = first;
            while (current != null)
            {
                var reference =
                    current as PdfIndirectReference;
                if (reference != null &&
                    !visited.Add(
                        reference.Number.ToString(
                            CultureInfo.InvariantCulture) +
                        ":" +
                        reference.Generation.ToString(
                            CultureInfo.InvariantCulture)))
                {
                    return;
                }
                var dictionary = ResolveDictionary(current);
                if (dictionary == null)
                {
                    return;
                }
                var rawTitle =
                    dictionary.GetAsString(PdfName.TITLE);
                if (rawTitle != null &&
                    string.Equals(
                        rawTitle.ToUnicodeString(),
                        title,
                        StringComparison.Ordinal))
                {
                    result.Add(dictionary);
                }
                FindRawOutlines(
                    dictionary.Get(PdfName.FIRST),
                    title,
                    result,
                    visited,
                    depth + 1);
                current = dictionary.Get(PdfName.NEXT);
            }
        }

        private static int CountSimpleBookmarks(
            IList<Dictionary<string, object>> bookmarks)
        {
            if (bookmarks == null)
            {
                return 0;
            }
            var count = 0;
            foreach (var bookmark in bookmarks)
            {
                count++;
                object kidsValue;
                count += CountSimpleBookmarks(
                    bookmark.TryGetValue(
                        "Kids",
                        out kidsValue)
                        ? kidsValue as
                            IList<Dictionary<string, object>>
                        : null);
            }
            return count;
        }

        private static PdfBookmarkNode FindNode(
            IList<PdfBookmarkNode> nodes,
            string title)
        {
            if (nodes == null)
            {
                return null;
            }
            foreach (var node in nodes)
            {
                if (string.Equals(
                    node.Title,
                    title,
                    StringComparison.Ordinal))
                {
                    return node;
                }
                var child = FindNode(node.Children, title);
                if (child != null)
                {
                    return child;
                }
            }
            return null;
        }

        private static PdfBookmarkNode FindNodeById(
            IList<PdfBookmarkNode> nodes,
            string id)
        {
            if (nodes == null)
            {
                return null;
            }
            foreach (var node in nodes)
            {
                if (string.Equals(
                    node.Id,
                    id,
                    StringComparison.Ordinal))
                {
                    return node;
                }
                var child = FindNodeById(node.Children, id);
                if (child != null)
                {
                    return child;
                }
            }
            return null;
        }

        private static string Canonicalize(PdfObject value)
        {
            return Canonicalize(
                value,
                new HashSet<string>(
                    StringComparer.Ordinal),
                0);
        }

        private static string Canonicalize(
            PdfObject value,
            ISet<string> visited,
            int depth)
        {
            if (value == null)
            {
                return "null";
            }
            if (depth > 64)
            {
                return "<depth>";
            }
            var reference = value as PdfIndirectReference;
            if (reference != null)
            {
                return "ref:" +
                    reference.Number.ToString(
                        CultureInfo.InvariantCulture) +
                    ":" +
                    reference.Generation.ToString(
                        CultureInfo.InvariantCulture);
            }
            var resolved = PdfReader.GetPdfObject(value);
            var dictionary = resolved as PdfDictionary;
            if (dictionary != null)
            {
                var entries = new List<string>();
                foreach (var key in dictionary.Keys
                    .OrderBy(item => item.ToString(),
                        StringComparer.Ordinal))
                {
                    entries.Add(
                        key.ToString() +
                        "=" +
                        Canonicalize(
                            dictionary.Get(key),
                            visited,
                            depth + 1));
                }
                return "{" + string.Join(",", entries.ToArray()) + "}";
            }
            var array = resolved as PdfArray;
            if (array != null)
            {
                var entries = new List<string>();
                for (var index = 0; index < array.Size; index++)
                {
                    entries.Add(
                        Canonicalize(
                            array[index],
                            visited,
                            depth + 1));
                }
                return "[" + string.Join(",", entries.ToArray()) + "]";
            }
            var text = resolved as PdfString;
            if (text != null)
            {
                return "str:" + text.ToUnicodeString();
            }
            return resolved.ToString();
        }

        private static PdfDictionary ResolveDictionary(
            PdfObject value)
        {
            return value == null
                ? null
                : PdfReader.GetPdfObject(value)
                    as PdfDictionary;
        }

        private static PdfArray ResolveArray(PdfObject value)
        {
            return value == null
                ? null
                : PdfReader.GetPdfObject(value)
                    as PdfArray;
        }

        private static int ResolveDestinationPage(
            PdfReader reader,
            PdfArray destination)
        {
            if (destination == null ||
                destination.Size < 1)
            {
                return -1;
            }
            var reference =
                destination[0] as PdfIndirectReference;
            if (reference == null)
            {
                return -1;
            }
            for (var page = 1; page <= reader.NumberOfPages; page++)
            {
                var candidate =
                    reader.GetPageOrigRef(page);
                if (candidate.Number == reference.Number &&
                    candidate.Generation ==
                        reference.Generation)
                {
                    return page;
                }
            }
            return -1;
        }

        private static int FindBytes(byte[] source, byte[] pattern)
        {
            for (var index = 0;
                index <= source.Length - pattern.Length;
                index++)
            {
                var matches = true;
                for (var offset = 0;
                    offset < pattern.Length;
                    offset++)
                {
                    if (source[index + offset] != pattern[offset])
                    {
                        matches = false;
                        break;
                    }
                }
                if (matches)
                {
                    return index;
                }
            }
            return -1;
        }

        private static void AssertNoTemporaryFiles(
            string outputPath)
        {
            var directory = Path.GetDirectoryName(outputPath);
            var pattern = "." +
                Path.GetFileNameWithoutExtension(outputPath) +
                ".*.tmp";
            Require(
                Directory.GetFiles(directory, pattern).Length == 0,
                "Quedó un temporal: " + pattern);
        }

        private static bool SequenceEqual(
            IList<string> first,
            IList<string> second)
        {
            return first.Count == second.Count &&
                first.SequenceEqual(second);
        }

        private static bool NearlyEqual(
            double? actual,
            double expected,
            double tolerance)
        {
            return actual.HasValue &&
                Math.Abs(actual.Value - expected) <= tolerance;
        }

        private static bool NearlyEqual(
            double actual,
            double expected,
            double tolerance)
        {
            return Math.Abs(actual - expected) <= tolerance;
        }

        private static string ComputeSha256(string path)
        {
            using (var input = File.OpenRead(path))
            using (var sha = SHA256.Create())
            {
                return ToHex(sha.ComputeHash(input));
            }
        }

        private static string ComputeBytesHash(byte[] bytes)
        {
            if (bytes == null)
            {
                return string.Empty;
            }
            using (var sha = SHA256.Create())
            {
                return ToHex(sha.ComputeHash(bytes));
            }
        }

        private static string ToHex(byte[] bytes)
        {
            return BitConverter.ToString(bytes)
                .Replace("-", string.Empty);
        }

        private static void RunSection(
            string name,
            Action action)
        {
            Results.Add(name);
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Failures.Add(
                    "FAIL " +
                    name +
                    ": " +
                    ex.GetType().Name +
                    ": " +
                    ex.GetBaseException().Message);
            }
        }

        private static void Require(
            bool condition,
            string description)
        {
            if (!condition)
            {
                throw new InvalidDataException(description);
            }
        }

        private static void RecreateDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
            Directory.CreateDirectory(path);
        }

        private static void TryDeleteFile(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private sealed class RawSnapshot
        {
            public RawSnapshot(
                int pageCount,
                IList<string> pageSizes,
                string metadata,
                string namedDestinations,
                string formState,
                string linkActions,
                string setOcgAction,
                string javaScriptAction,
                string goToActionTail,
                string planDestination,
                string namedOutlineDestination)
            {
                PageCount = pageCount;
                PageSizes = pageSizes;
                Metadata = metadata;
                NamedDestinations = namedDestinations;
                FormState = formState;
                LinkActions = linkActions;
                SetOcgAction = setOcgAction;
                JavaScriptAction = javaScriptAction;
                GoToActionTail = goToActionTail;
                PlanDestination = planDestination;
                NamedOutlineDestination =
                    namedOutlineDestination;
            }

            public int PageCount;
            public IList<string> PageSizes;
            public string Metadata;
            public string NamedDestinations;
            public string FormState;
            public string LinkActions;
            public string SetOcgAction;
            public string JavaScriptAction;
            public string GoToActionTail;
            public string PlanDestination;
            public string NamedOutlineDestination;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
using IOPath = System.IO.Path;

namespace FirmaAutomatica
{
    /// <summary>
    /// Headless regression harness for the on-disk edit/recovery history.
    ///
    /// Compile this file together with the production sources and select this
    /// entry point with:
    ///   /main:FirmaAutomatica.Phase1EditSessionHarness
    ///
    /// Usage:
    ///   Phase1EditSessionHarness.exe <repository-root> [validation-output-root]
    /// </summary>
    internal static class Phase1EditSessionHarness
    {
        private static readonly List<string> Report = new List<string>();

        private static string runDirectory;
        private static string recoveryRoot;
        private static string basePath;
        private static string insertBPath;
        private static string insertCPath;
        private static FileSnapshot baseSnapshot;
        private static FileSnapshot insertBSnapshot;
        private static FileSnapshot insertCSnapshot;

        private static int Main(string[] args)
        {
            var previousRecoveryRoot = Environment.GetEnvironmentVariable(
                PdfEditSession.RecoveryRootOverrideEnvironmentVariable);
            var exitCode = 1;

            try
            {
                if (args.Length < 1 || args.Length > 2)
                {
                    throw new InvalidOperationException(
                        "Uso: Phase1EditSessionHarness " +
                        "<raiz-repositorio> [directorio-validacion]");
                }

                PrepareRun(args);
                Environment.SetEnvironmentVariable(
                    PdfEditSession.RecoveryRootOverrideEnvironmentVariable,
                    recoveryRoot);

                TestCommitUndoRedoBranchAndSavedState();
                TestTransactionalCommitRollbackPreservesRedo();
                TestRecoveryRestoreAndDiscard();
                TestHistoryLimits();
                TestPruningProtectsCurrentAndImmediatePredecessor();
                TestReservedRevisionCancellationAndCleanup();

                AssertOriginalsIntact("al finalizar todas las pruebas");
                AssertNoTemporaryFiles(recoveryRoot);
                AssertEqual(
                    0,
                    PdfEditSession.FindRecoverableSessions().Count,
                    "sesiones recuperables finales");
                AssertEqual(
                    0,
                    Directory.GetDirectories(
                        recoveryRoot,
                        "*",
                        SearchOption.TopDirectoryOnly).Length,
                    "directorios de recuperación finales");
                AssertFilesUnlocked(
                    new[] { basePath, insertBPath, insertCPath });

                Report.Add("PASS final: originales intactos, sin locks ni recuperación residual.");
                Report.Add("OK: todas las pruebas de PdfEditSession han pasado.");
                exitCode = 0;
            }
            catch (Exception ex)
            {
                Report.Add("FAIL: " + ex);
                Console.Error.WriteLine(ex);
            }
            finally
            {
                Environment.SetEnvironmentVariable(
                    PdfEditSession.RecoveryRootOverrideEnvironmentVariable,
                    previousRecoveryRoot);
                WriteReport();
            }

            foreach (var line in Report)
            {
                Console.WriteLine(line);
            }

            if (!string.IsNullOrWhiteSpace(runDirectory))
            {
                Console.WriteLine("Directorio de validación: " + runDirectory);
            }

            return exitCode;
        }

        private static void PrepareRun(string[] args)
        {
            var repositoryRoot = IOPath.GetFullPath(args[0]);
            var fixtureDirectory = IOPath.Combine(
                repositoryRoot,
                "tmp",
                "pdfs",
                "qa");
            var fixtureA = IOPath.Combine(
                fixtureDirectory,
                "A dos paginas.pdf");
            var fixtureB = IOPath.Combine(
                fixtureDirectory,
                "B una pagina.pdf");
            var fixtureC = IOPath.Combine(
                fixtureDirectory,
                "C tres paginas.pdf");

            AssertFileExists(fixtureA, "fixture A");
            AssertFileExists(fixtureB, "fixture B");
            AssertFileExists(fixtureC, "fixture C");

            var outputRoot = args.Length == 2
                ? IOPath.GetFullPath(args[1])
                : IOPath.Combine(
                    repositoryRoot,
                    "firma automática",
                    "build",
                    "validation-phase1");
            runDirectory = IOPath.Combine(
                outputRoot,
                "run-" +
                DateTime.UtcNow.ToString(
                    "yyyyMMdd-HHmmss",
                    CultureInfo.InvariantCulture) +
                "-" +
                Guid.NewGuid().ToString("N").Substring(0, 8));
            recoveryRoot = IOPath.Combine(runDirectory, "recovery");
            var inputsDirectory = IOPath.Combine(runDirectory, "inputs");

            Directory.CreateDirectory(inputsDirectory);
            Directory.CreateDirectory(recoveryRoot);

            basePath = IOPath.Combine(inputsDirectory, "base-A.pdf");
            insertBPath = IOPath.Combine(inputsDirectory, "insert-B.pdf");
            insertCPath = IOPath.Combine(inputsDirectory, "insert-C.pdf");
            File.Copy(fixtureA, basePath, false);
            File.Copy(fixtureB, insertBPath, false);
            File.Copy(fixtureC, insertCPath, false);

            baseSnapshot = FileSnapshot.Capture(basePath);
            insertBSnapshot = FileSnapshot.Capture(insertBPath);
            insertCSnapshot = FileSnapshot.Capture(insertCPath);

            Report.Add("Run: " + runDirectory);
            Report.Add("Recovery root inyectada: " + recoveryRoot);
        }

        private static void TestCommitUndoRedoBranchAndSavedState()
        {
            var session = PdfEditSession.Create(basePath);
            AssertEqual(
                IOPath.GetFullPath(basePath),
                IOPath.GetFullPath(session.CurrentPath),
                "ruta inicial");
            AssertFalse(session.HasUnsavedChanges, "la sesión empezó dirty");
            AssertFalse(session.CanUndo, "había Undo al abrir");
            AssertFalse(session.CanRedo, "había Redo al abrir");

            var firstRevisionPath = session.ReserveRevisionPath(
                GetLength(basePath) + GetLength(insertBPath));
            var firstResult = PdfPageInsertService.Insert(
                session.CurrentPath,
                new[] { insertBPath },
                1,
                firstRevisionPath,
                null);
            AssertEqual(3, firstResult.PageCount, "páginas de primera revisión");
            session.CommitRevision(
                firstRevisionPath,
                "Insertar B entre las páginas de A");

            AssertTrue(session.HasUnsavedChanges, "Commit no marcó dirty");
            AssertTrue(session.CanUndo, "Commit no habilitó Undo");
            AssertFalse(session.CanRedo, "Commit habilitó Redo indebidamente");
            AssertEqual(
                IOPath.GetFullPath(firstRevisionPath),
                IOPath.GetFullPath(session.CurrentPath),
                "ruta tras Commit");
            AssertPageTextOrder(
                session.CurrentPath,
                new[]
                {
                    "DOCUMENTO A - pagina 1",
                    "DOCUMENTO B - pagina 1",
                    "DOCUMENTO A - pagina 2"
                });
            AssertOriginalsIntact("tras Commit A+B");
            AssertNoTemporaryFiles(session.SessionDirectory);

            var initialCandidates =
                PdfEditSession.FindRecoverableSessions();
            AssertEqual(
                1,
                initialCandidates.Count,
                "candidatos tras el primer Commit");
            var initialRestored = PdfEditSession.Restore(
                initialCandidates[0]);
            AssertEqual(
                IOPath.GetFullPath(firstRevisionPath),
                IOPath.GetFullPath(initialRestored.CurrentPath),
                "ruta restaurada tras el primer Commit");
            AssertTrue(
                initialRestored.HasUnsavedChanges,
                "la restauración perdió el estado dirty");
            AssertTrue(
                initialRestored.CanUndo,
                "la restauración perdió Undo");
            AssertPageCount(initialRestored.CurrentPath, 3);

            var undoPath = session.Undo();
            AssertEqual(
                IOPath.GetFullPath(basePath),
                IOPath.GetFullPath(undoPath),
                "ruta de Undo");
            AssertFalse(
                session.HasUnsavedChanges,
                "Undo al documento abierto no quedó clean");
            AssertFalse(session.CanUndo, "Undo seguía habilitado al inicio");
            AssertTrue(session.CanRedo, "Undo no habilitó Redo");
            AssertPageCount(session.CurrentPath, 2);

            var redoPath = session.Redo();
            AssertEqual(
                IOPath.GetFullPath(firstRevisionPath),
                IOPath.GetFullPath(redoPath),
                "ruta de Redo");
            AssertTrue(session.HasUnsavedChanges, "Redo no quedó dirty");
            AssertTrue(session.CanUndo, "Redo no habilitó Undo");
            AssertFalse(session.CanRedo, "Redo seguía habilitado al final");
            AssertPageCount(session.CurrentPath, 3);

            session.Undo();
            AssertTrue(session.CanRedo, "no había rama Redo antes del branch");
            AssertTrue(
                File.Exists(firstRevisionPath),
                "la revisión Redo desapareció antes de crear un branch");

            var rejectedBranchPath = session.ReserveRevisionPath();
            AssertThrows<InvalidOperationException>(
                delegate
                {
                    session.CommitRevision(
                        rejectedBranchPath,
                        "Branch incompleto");
                },
                "se aceptó una revisión reservada que no existía");
            AssertTrue(
                session.CanRedo,
                "un Commit fallido destruyó la rama Redo");
            AssertTrue(
                File.Exists(firstRevisionPath),
                "un Commit fallido borró el PDF de la rama Redo");
            session.CancelReservedRevision(rejectedBranchPath);

            var corruptBranchPath = session.ReserveRevisionPath();
            File.WriteAllText(
                corruptBranchPath,
                "Este archivo no es un PDF.");
            AssertThrows<InvalidDataException>(
                delegate
                {
                    session.CommitRevision(
                        corruptBranchPath,
                        "Branch PDF corrupto");
                },
                "se aceptó como revisión un archivo que no era PDF");
            AssertTrue(
                session.CanRedo,
                "un PDF corrupto destruyó la rama Redo");
            AssertTrue(
                File.Exists(firstRevisionPath),
                "un PDF corrupto borró el PDF de la rama Redo");
            session.CancelReservedRevision(corruptBranchPath);
            AssertFalse(
                File.Exists(corruptBranchPath),
                "el PDF corrupto reservado no se pudo cancelar");

            var branchPath = session.ReserveRevisionPath(
                GetLength(basePath) + GetLength(insertCPath));
            var branchResult = PdfPageInsertService.Insert(
                session.CurrentPath,
                new[] { insertCPath },
                0,
                branchPath,
                null);
            AssertEqual(5, branchResult.PageCount, "páginas del branch");
            session.CommitRevision(
                branchPath,
                "Insertar C antes de A");

            AssertFalse(
                File.Exists(firstRevisionPath),
                "el Commit del branch no limpió la antigua rama Redo");
            AssertFalse(
                session.CanRedo,
                "el Commit del branch conservó Redo obsoleto");
            AssertTrue(session.CanUndo, "el branch perdió Undo");
            AssertTrue(session.HasUnsavedChanges, "el branch no quedó dirty");
            AssertPageTextOrder(
                session.CurrentPath,
                new[]
                {
                    "DOCUMENTO C - pagina 1",
                    "DOCUMENTO C - pagina 2",
                    "DOCUMENTO C - pagina 3",
                    "DOCUMENTO A - pagina 1",
                    "DOCUMENTO A - pagina 2"
                });

            session.MarkCurrentRevisionSaved();
            AssertFalse(
                session.HasUnsavedChanges,
                "MarkCurrentRevisionSaved no dejó la sesión clean");
            AssertTrue(
                session.CanUndo,
                "MarkCurrentRevisionSaved eliminó Undo durante la sesión");
            AssertEqual(
                1,
                PdfEditSession.FindRecoverableSessions().Count,
                "la revisión guardada no conservó respaldo hasta el cierre");
            AssertTrue(
                File.Exists(branchPath),
                "guardar eliminó la revisión activa");

            var undoAfterSave = session.Undo();
            AssertEqual(
                IOPath.GetFullPath(basePath),
                IOPath.GetFullPath(undoAfterSave),
                "Undo después de guardar");
            AssertTrue(
                session.HasUnsavedChanges,
                "Undo después de guardar no volvió a dirty");
            AssertTrue(
                session.CanRedo,
                "Undo después de guardar no habilitó Redo");

            var savedUndoCandidates =
                PdfEditSession.FindRecoverableSessions();
            AssertEqual(
                1,
                savedUndoCandidates.Count,
                "candidatos tras Undo de una revisión guardada");
            var restoredAfterSavedUndo = PdfEditSession.Restore(
                savedUndoCandidates[0]);
            AssertTrue(
                restoredAfterSavedUndo.HasUnsavedChanges,
                "restore tras Undo guardado perdió dirty");
            AssertTrue(
                restoredAfterSavedUndo.CanRedo,
                "restore tras Undo guardado perdió Redo");
            AssertEqual(
                IOPath.GetFullPath(branchPath),
                IOPath.GetFullPath(savedUndoCandidates[0].SavedPath),
                "SavedPath del candidato");
            AssertEqual(
                IOPath.GetFullPath(branchPath),
                IOPath.GetFullPath(restoredAfterSavedUndo.Redo()),
                "Redo restaurado hacia la revisión guardada");
            AssertFalse(
                restoredAfterSavedUndo.HasUnsavedChanges,
                "Redo a la revisión guardada no quedó clean");
            AssertEqual(
                1,
                PdfEditSession.FindRecoverableSessions().Count,
                "Redo a saved no conservó respaldo hasta el cierre");

            var sessionDirectory = session.SessionDirectory;
            session.DeleteRecovery();
            AssertFalse(
                Directory.Exists(sessionDirectory),
                "DeleteRecovery no limpió la sesión principal");
            AssertOriginalsIntact("tras branch, guardado y Undo/Redo");

            Report.Add(
                "PASS commit/undo/redo/branch/saved: orden, dirty y rama verificados.");
        }

        private static void TestRecoveryRestoreAndDiscard()
        {
            var session = PdfEditSession.Create(basePath);
            var revisionPath = session.ReserveRevisionPath(
                GetLength(basePath) + GetLength(insertBPath));
            PdfPageInsertService.Insert(
                session.CurrentPath,
                new[] { insertBPath },
                1,
                revisionPath,
                null);
            session.CommitRevision(
                revisionPath,
                "Revisión recuperable A+B");

            var candidates = PdfEditSession.FindRecoverableSessions();
            AssertEqual(
                1,
                candidates.Count,
                "candidatos del escenario Restore");
            var candidate = candidates[0];
            AssertEqual(
                IOPath.GetFullPath(basePath),
                IOPath.GetFullPath(candidate.SourcePath),
                "SourcePath del candidato");
            AssertEqual(
                IOPath.GetFullPath(revisionPath),
                IOPath.GetFullPath(candidate.CurrentPath),
                "CurrentPath del candidato");
            AssertTrue(
                candidate.Revisions.Count >= 2,
                "el manifiesto no conservó el historial");

            var restored = PdfEditSession.Restore(candidate);
            AssertTrue(restored.HasUnsavedChanges, "Restore no quedó dirty");
            AssertTrue(restored.CanUndo, "Restore no conservó Undo");
            AssertFalse(restored.CanRedo, "Restore creó un Redo inexistente");
            AssertPageTextOrder(
                restored.CurrentPath,
                new[]
                {
                    "DOCUMENTO A - pagina 1",
                    "DOCUMENTO B - pagina 1",
                    "DOCUMENTO A - pagina 2"
                });

            AssertEqual(
                IOPath.GetFullPath(basePath),
                IOPath.GetFullPath(restored.Undo()),
                "Undo de la sesión restaurada");
            AssertFalse(
                restored.HasUnsavedChanges,
                "Undo restaurado al origen no quedó clean");
            AssertEqual(
                IOPath.GetFullPath(revisionPath),
                IOPath.GetFullPath(restored.Redo()),
                "Redo de la sesión restaurada");
            AssertTrue(
                restored.HasUnsavedChanges,
                "Redo restaurado no volvió a dirty");

            PdfEditSession.Discard(candidate);
            AssertFalse(
                Directory.Exists(candidate.SessionDirectory),
                "Discard conservó el directorio de sesión");
            AssertFalse(
                File.Exists(revisionPath),
                "Discard conservó la revisión temporal");
            AssertEqual(
                0,
                PdfEditSession.FindRecoverableSessions().Count,
                "Discard dejó un candidato");
            AssertOriginalsIntact("tras Restore y Discard");

            Report.Add(
                "PASS recovery: scan, restore, Undo/Redo y discard verificados.");
        }

        private static void TestTransactionalCommitRollbackPreservesRedo()
        {
            var session = PdfEditSession.Create(basePath);
            var existingRedoPath = session.ReserveRevisionPath(
                GetLength(insertBPath));
            File.Copy(insertBPath, existingRedoPath, false);
            session.CommitRevision(
                existingRedoPath,
                "Revisión que debe sobrevivir como Redo");
            session.MarkCurrentRevisionSaved();
            session.Undo();

            AssertTrue(
                session.CanRedo,
                "no había rama Redo antes de la transacción");
            AssertTrue(
                session.HasUnsavedChanges,
                "Undo desde el punto guardado no quedó dirty");
            AssertEqual(
                IOPath.GetFullPath(existingRedoPath),
                IOPath.GetFullPath(session.GetRedoPath()),
                "Redo previo a la transacción");

            var candidatePath = session.ReserveRevisionPath(
                GetLength(insertCPath));
            File.Copy(insertCPath, candidatePath, false);
            var transaction = session.BeginRevisionCommit(
                candidatePath,
                "Revisión cuya activación se simula fallida");

            AssertFalse(
                session.CanRedo,
                "la rama nueva no sustituyó lógicamente al Redo");
            AssertTrue(
                File.Exists(existingRedoPath),
                "BeginRevisionCommit borró físicamente el Redo antes de confirmar");
            AssertEqual(
                IOPath.GetFullPath(candidatePath),
                IOPath.GetFullPath(session.CurrentPath),
                "ruta lógica durante la transacción");

            transaction.Rollback();

            AssertEqual(
                IOPath.GetFullPath(basePath),
                IOPath.GetFullPath(session.CurrentPath),
                "Rollback no restauró la revisión anterior");
            AssertTrue(
                session.CanRedo,
                "Rollback perdió la rama Redo anterior");
            AssertTrue(
                session.HasUnsavedChanges,
                "Rollback no restauró el índice guardado anterior");
            AssertEqual(
                IOPath.GetFullPath(existingRedoPath),
                IOPath.GetFullPath(session.GetRedoPath()),
                "Rollback restauró un Redo distinto");
            AssertTrue(
                File.Exists(existingRedoPath),
                "Rollback borró el archivo del Redo anterior");
            AssertFalse(
                File.Exists(candidatePath),
                "Rollback dejó la revisión candidata huérfana");
            AssertEqual(
                IOPath.GetFullPath(existingRedoPath),
                IOPath.GetFullPath(session.Redo()),
                "Redo no funcionó después del Rollback");
            AssertFalse(
                session.HasUnsavedChanges,
                "Redo tras Rollback no volvió al punto guardado");

            var sessionDirectory = session.SessionDirectory;
            session.DeleteRecovery();
            AssertFalse(
                Directory.Exists(sessionDirectory),
                "la prueba transaccional dejó recuperación residual");
            Report.Add(
                "PASS commit transaccional: Rollback conservó íntegra la rama Redo.");
        }

        private static void TestHistoryLimits()
        {
            var maximumRevisionField = typeof(PdfEditSession).GetField(
                "MaximumOwnedRevisions",
                BindingFlags.Static | BindingFlags.NonPublic);
            var maximumBytesField = typeof(PdfEditSession).GetField(
                "MaximumOwnedRevisionBytes",
                BindingFlags.Static | BindingFlags.NonPublic);
            AssertNotNull(
                maximumRevisionField,
                "no se encontró MaximumOwnedRevisions");
            AssertNotNull(
                maximumBytesField,
                "no se encontró MaximumOwnedRevisionBytes");

            var maximumOwnedRevisions =
                (int)maximumRevisionField.GetRawConstantValue();
            var maximumOwnedRevisionBytes =
                (long)maximumBytesField.GetRawConstantValue();
            AssertTrue(
                maximumOwnedRevisions >= 2 &&
                maximumOwnedRevisions <= 32,
                "límite de pasos no razonable: " +
                maximumOwnedRevisions);
            AssertTrue(
                maximumOwnedRevisionBytes >= 64L * 1024L * 1024L &&
                maximumOwnedRevisionBytes <= 2L * 1024L * 1024L * 1024L,
                "límite de bytes no razonable: " +
                maximumOwnedRevisionBytes);

            var session = PdfEditSession.Create(basePath);
            var commits = maximumOwnedRevisions + 4;
            for (var index = 0; index < commits; index++)
            {
                var revisionPath = session.ReserveRevisionPath(
                    GetLength(session.CurrentPath));
                File.Copy(session.CurrentPath, revisionPath, false);
                session.CommitRevision(
                    revisionPath,
                    "Revisión límite " +
                    (index + 1).ToString(CultureInfo.InvariantCulture));

                var ownedFiles = Directory.GetFiles(
                    session.SessionDirectory,
                    "revision-*.pdf",
                    SearchOption.TopDirectoryOnly);
                AssertTrue(
                    ownedFiles.Length <= maximumOwnedRevisions,
                    "el historial superó su límite: " +
                    ownedFiles.Length + " > " +
                    maximumOwnedRevisions);
                AssertNoTemporaryFiles(session.SessionDirectory);
            }

            var retainedFiles = Directory.GetFiles(
                session.SessionDirectory,
                "revision-*.pdf",
                SearchOption.TopDirectoryOnly).Length;
            AssertEqual(
                maximumOwnedRevisions,
                retainedFiles,
                "revisiones retenidas al alcanzar el límite");

            var undoCount = 0;
            while (session.Undo() != null)
            {
                undoCount++;
            }

            AssertEqual(
                retainedFiles,
                undoCount,
                "pasos Undo retenidos");
            AssertEqual(
                IOPath.GetFullPath(basePath),
                IOPath.GetFullPath(session.CurrentPath),
                "ruta tras agotar Undo");
            AssertFalse(
                session.HasUnsavedChanges,
                "agotar Undo al origen no quedó clean");

            var redoCount = 0;
            while (session.Redo() != null)
            {
                redoCount++;
            }

            AssertEqual(
                retainedFiles,
                redoCount,
                "pasos Redo retenidos");
            AssertTrue(
                session.HasUnsavedChanges,
                "agotar Redo no volvió a dirty");

            var sessionDirectory = session.SessionDirectory;
            session.DeleteRecovery();
            AssertFalse(
                Directory.Exists(sessionDirectory),
                "la prueba de límites dejó su sesión");
            AssertOriginalsIntact("tras probar límites de historial");

            Report.Add(
                "PASS límites: máximo " +
                maximumOwnedRevisions +
                " revisiones / " +
                maximumOwnedRevisionBytes +
                " bytes; poda y navegación verificadas.");
        }

        private static void TestPruningProtectsCurrentAndImmediatePredecessor()
        {
            // Isolate the pruning-selection primitive. The count-limit loop is
            // exercised by TestHistoryLimits; invoking this private method lets us
            // force the exact edge case of the byte budget without creating
            // artificial 768 MiB files.
            var session = PdfEditSession.Create(basePath);
            var revisionPaths = CommitClonedRevisions(
                session,
                3,
                "Poda dirigida");

            AssertEqual(
                IOPath.GetFullPath(revisionPaths[1]),
                IOPath.GetFullPath(session.Undo()),
                "current preparado para poda dirigida");
            var protectedCurrentPath = session.CurrentPath;
            var protectedPredecessorPath = revisionPaths[0];
            var removableRedoPath = revisionPaths[2];
            var obsoletePaths = new List<string>();

            var removed = InvokeRemoveOldestOwnedRevision(
                session,
                obsoletePaths);
            AssertTrue(
                removed,
                "la poda no encontró una revisión no protegida");
            AssertEqual(
                IOPath.GetFullPath(protectedCurrentPath),
                IOPath.GetFullPath(session.CurrentPath),
                "la poda cambió o eliminó current");

            var retainedRevisions = GetSessionRevisions(session);
            AssertTrue(
                ContainsRevisionPath(
                    retainedRevisions,
                    protectedCurrentPath),
                "la poda eliminó current");
            AssertTrue(
                ContainsRevisionPath(
                    retainedRevisions,
                    protectedPredecessorPath),
                "la poda eliminó el predecessor inmediato");
            AssertFalse(
                ContainsRevisionPath(
                    retainedRevisions,
                    removableRedoPath),
                "la poda conservó la única revisión no protegida");
            AssertEqual(
                1,
                obsoletePaths.Count,
                "rutas obsoletas de la poda dirigida");
            AssertEqual(
                IOPath.GetFullPath(removableRedoPath),
                IOPath.GetFullPath(obsoletePaths[0]),
                "ruta elegida por la poda");
            AssertTrue(
                File.Exists(protectedCurrentPath),
                "el PDF current desapareció durante la selección de poda");
            AssertTrue(
                File.Exists(protectedPredecessorPath),
                "el PDF predecessor desapareció durante la selección de poda");

            var firstSessionDirectory = session.SessionDirectory;
            session.DeleteRecovery();
            AssertFalse(
                Directory.Exists(firstSessionDirectory),
                "la poda dirigida dejó su primera sesión");

            // If current and its immediate predecessor are the only owned
            // revisions, pruning must stop even while a caller still considers the
            // budget exceeded. Correctness of Undo takes priority over the budget.
            var protectedOnlySession = PdfEditSession.Create(basePath);
            var protectedOnlyPaths = CommitClonedRevisions(
                protectedOnlySession,
                2,
                "Poda sin candidato");
            var protectedOnlyObsoletePaths = new List<string>();
            var removedProtectedOnly = InvokeRemoveOldestOwnedRevision(
                protectedOnlySession,
                protectedOnlyObsoletePaths);

            AssertFalse(
                removedProtectedOnly,
                "la poda eliminó current o predecessor al no haber alternativa");
            AssertEqual(
                0,
                protectedOnlyObsoletePaths.Count,
                "la poda marcó una revisión protegida como obsoleta");
            var protectedOnlyRevisions =
                GetSessionRevisions(protectedOnlySession);
            AssertTrue(
                ContainsRevisionPath(
                    protectedOnlyRevisions,
                    protectedOnlyPaths[0]),
                "la poda sin candidato eliminó predecessor");
            AssertTrue(
                ContainsRevisionPath(
                    protectedOnlyRevisions,
                    protectedOnlyPaths[1]),
                "la poda sin candidato eliminó current");
            AssertEqual(
                IOPath.GetFullPath(protectedOnlyPaths[1]),
                IOPath.GetFullPath(protectedOnlySession.CurrentPath),
                "current cambió en la poda sin candidato");

            var secondSessionDirectory =
                protectedOnlySession.SessionDirectory;
            protectedOnlySession.DeleteRecovery();
            AssertFalse(
                Directory.Exists(secondSessionDirectory),
                "la poda dirigida dejó su segunda sesión");
            AssertOriginalsIntact(
                "tras la prueba dirigida de poda protegida");

            Report.Add(
                "PASS poda protegida: current y predecessor inmediato nunca se eliminan.");
        }

        private static void TestReservedRevisionCancellationAndCleanup()
        {
            var session = PdfEditSession.Create(basePath);
            var reservedPath = session.ReserveRevisionPath(
                GetLength(basePath));
            File.Copy(basePath, reservedPath, false);
            AssertTrue(
                File.Exists(reservedPath),
                "no se creó la revisión que se iba a cancelar");
            session.CancelReservedRevision(reservedPath);
            AssertFalse(
                File.Exists(reservedPath),
                "CancelReservedRevision dejó el archivo");

            var externalSentinel = IOPath.Combine(
                runDirectory,
                "NO_BORRAR_fuera_de_recovery.txt");
            File.WriteAllText(externalSentinel, "sentinel");
            session.CancelReservedRevision(externalSentinel);
            AssertTrue(
                File.Exists(externalSentinel),
                "CancelReservedRevision borró una ruta externa");

            var committedPath = session.ReserveRevisionPath(
                GetLength(basePath));
            File.Copy(basePath, committedPath, false);
            session.CommitRevision(
                committedPath,
                "Revisión para probar limpieza");
            AssertTrue(
                Directory.Exists(session.SessionDirectory),
                "la sesión no creó su directorio");
            AssertTrue(
                File.Exists(committedPath),
                "la revisión confirmada no existe");

            var sessionDirectory = session.SessionDirectory;
            session.DeleteRecovery();
            AssertFalse(
                Directory.Exists(sessionDirectory),
                "DeleteRecovery dejó temporales");
            AssertTrue(
                File.Exists(externalSentinel),
                "DeleteRecovery borró el sentinel externo");
            AssertOriginalsIntact("tras cancelar y limpiar revisiones");

            Report.Add(
                "PASS limpieza: reserva cancelada, ruta externa protegida y sesión eliminada.");
        }

        private static IList<string> CommitClonedRevisions(
            PdfEditSession session,
            int count,
            string descriptionPrefix)
        {
            var paths = new List<string>();
            for (var index = 0; index < count; index++)
            {
                var revisionPath = session.ReserveRevisionPath(
                    GetLength(session.CurrentPath));
                File.Copy(session.CurrentPath, revisionPath, false);
                session.CommitRevision(
                    revisionPath,
                    descriptionPrefix + " " +
                    (index + 1).ToString(
                        CultureInfo.InvariantCulture));
                paths.Add(revisionPath);
            }

            return paths;
        }

        private static bool InvokeRemoveOldestOwnedRevision(
            PdfEditSession session,
            ICollection<string> obsoletePaths)
        {
            var method = typeof(PdfEditSession).GetMethod(
                "RemoveOldestOwnedRevision",
                BindingFlags.Instance | BindingFlags.NonPublic);
            AssertNotNull(
                method,
                "no se encontró RemoveOldestOwnedRevision");

            try
            {
                return (bool)method.Invoke(
                    session,
                    new object[] { obsoletePaths });
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException ?? ex;
            }
        }

        private static IList<PdfEditRevision> GetSessionRevisions(
            PdfEditSession session)
        {
            var field = typeof(PdfEditSession).GetField(
                "revisions",
                BindingFlags.Instance | BindingFlags.NonPublic);
            AssertNotNull(field, "no se encontró el historial interno");
            return (IList<PdfEditRevision>)field.GetValue(session);
        }

        private static bool ContainsRevisionPath(
            IEnumerable<PdfEditRevision> revisions,
            string path)
        {
            return revisions.Any(
                revision => string.Equals(
                    revision.Path,
                    path,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static void AssertPageCount(string path, int expected)
        {
            using (var reader = new PdfReader(path))
            {
                AssertEqual(
                    expected,
                    reader.NumberOfPages,
                    "páginas de " + IOPath.GetFileName(path));
            }
        }

        private static void AssertPageTextOrder(
            string path,
            IList<string> expectedPageText)
        {
            using (var reader = new PdfReader(path))
            {
                AssertEqual(
                    expectedPageText.Count,
                    reader.NumberOfPages,
                    "número de páginas en " + IOPath.GetFileName(path));

                for (var page = 1; page <= reader.NumberOfPages; page++)
                {
                    var actualText = PdfTextExtractor.GetTextFromPage(
                        reader,
                        page);
                    AssertTrue(
                        actualText.IndexOf(
                            expectedPageText[page - 1],
                            StringComparison.OrdinalIgnoreCase) >= 0,
                        "la página " +
                        page.ToString(CultureInfo.InvariantCulture) +
                        " no contiene \"" +
                        expectedPageText[page - 1] +
                        "\". Texto: " +
                        actualText);
                }
            }
        }

        private static void AssertOriginalsIntact(string moment)
        {
            baseSnapshot.AssertUnchanged(moment);
            insertBSnapshot.AssertUnchanged(moment);
            insertCSnapshot.AssertUnchanged(moment);
        }

        private static void AssertNoTemporaryFiles(string directory)
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            var temporaryFiles = Directory.GetFiles(
                directory,
                "*.tmp",
                SearchOption.AllDirectories);
            AssertEqual(
                0,
                temporaryFiles.Length,
                "temporales .tmp en " + directory);
        }

        private static void AssertFilesUnlocked(IEnumerable<string> paths)
        {
            foreach (var path in paths)
            {
                using (File.Open(
                    path,
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.None))
                {
                }
            }
        }

        private static long GetLength(string path)
        {
            return new FileInfo(path).Length;
        }

        private static void AssertFileExists(string path, string label)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    "No existe " + label + ": " + path,
                    path);
            }
        }

        private static void AssertNotNull(object value, string message)
        {
            if (value == null)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static void AssertTrue(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static void AssertFalse(bool condition, string message)
        {
            AssertTrue(!condition, message);
        }

        private static void AssertEqual<T>(
            T expected,
            T actual,
            string description)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    description + ": esperado=" + expected +
                    ", actual=" + actual + ".");
            }
        }

        private static void AssertThrows<TException>(
            Action action,
            string failureMessage)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }

            throw new InvalidOperationException(failureMessage);
        }

        private static void WriteReport()
        {
            if (string.IsNullOrWhiteSpace(runDirectory))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(runDirectory);
                File.WriteAllLines(
                    IOPath.Combine(runDirectory, "report.txt"),
                    Report.ToArray());
            }
            catch
            {
            }
        }

        private sealed class FileSnapshot
        {
            private FileSnapshot(
                string path,
                long length,
                DateTime lastWriteTimeUtc,
                string sha256)
            {
                Path = path;
                Length = length;
                LastWriteTimeUtc = lastWriteTimeUtc;
                Sha256 = sha256;
            }

            public string Path { get; private set; }

            public long Length { get; private set; }

            public DateTime LastWriteTimeUtc { get; private set; }

            public string Sha256 { get; private set; }

            public static FileSnapshot Capture(string path)
            {
                var info = new FileInfo(path);
                return new FileSnapshot(
                    IOPath.GetFullPath(path),
                    info.Length,
                    info.LastWriteTimeUtc,
                    ComputeSha256(path));
            }

            public void AssertUnchanged(string moment)
            {
                AssertTrue(
                    File.Exists(Path),
                    "el original desapareció " + moment + ": " + Path);
                var info = new FileInfo(Path);
                AssertEqual(
                    Length,
                    info.Length,
                    "tamaño del original " + moment);
                AssertEqual(
                    LastWriteTimeUtc,
                    info.LastWriteTimeUtc,
                    "fecha del original " + moment);
                AssertEqual(
                    Sha256,
                    ComputeSha256(Path),
                    "hash del original " + moment);
            }

            private static string ComputeSha256(string path)
            {
                using (var sha = SHA256.Create())
                using (var stream = File.OpenRead(path))
                {
                    return BitConverter.ToString(
                        sha.ComputeHash(stream));
                }
            }
        }
    }
}

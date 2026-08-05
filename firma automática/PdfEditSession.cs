using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using iTextSharp.text.pdf;

namespace FirmaAutomatica
{
    /// <summary>
    /// Immutable identity of the exact revision loaded by Pdfium. The viewer
    /// stores this object together with the rendered document so later screen
    /// coordinates cannot be interpreted against another path or revision.
    /// </summary>
    internal sealed class PdfEditViewIdentity
    {
        public PdfEditViewIdentity(
            string contentPath,
            long expectedLength,
            long expectedLastWriteUtcTicks)
        {
            if (string.IsNullOrWhiteSpace(contentPath))
            {
                throw new ArgumentException(
                    "Se necesita la ruta de la revision visible.",
                    "contentPath");
            }

            ContentPath = Path.GetFullPath(contentPath);
            ExpectedLength = expectedLength;
            ExpectedLastWriteUtcTicks = expectedLastWriteUtcTicks;
        }

        public string ContentPath { get; private set; }

        public long ExpectedLength { get; private set; }

        public long ExpectedLastWriteUtcTicks { get; private set; }
    }

    /// <summary>
    /// Keeps PDF edit history as immutable files on disk. This deliberately avoids
    /// retaining complete documents in memory: only the active revision is loaded by
    /// PdfiumViewer, while undo/redo revisions live in the recovery directory.
    /// </summary>
    internal sealed class PdfEditSession
    {
        internal const string RecoveryRootOverrideEnvironmentVariable =
            "PDFLIGERO_RECOVERY_ROOT";

        private const string ManifestFileName = "recovery.txt";
        private const string ManifestHeader = "PDFLIGERO_RECOVERY_V1";
        private const int MaximumOwnedRevisions = 8;
        private const long MaximumOwnedRevisionBytes = 768L * 1024L * 1024L;
        private const long MaximumGlobalRecoveryBytes =
            2L * 1024L * 1024L * 1024L;
        private static readonly object RecoveryRootSync = new object();
        private static string cachedDefaultRecoveryRoot;

        private readonly List<PdfEditRevision> revisions;
        private readonly List<string> pendingDeletionPaths =
            new List<string>();
        private readonly string sessionDirectory;
        private readonly long sourceLength;
        private readonly long sourceLastWriteUtcTicks;
        private int currentIndex;
        private int savedIndex;
        private string lastSavedTargetPath;
        private RevisionCommit activeRevisionCommit;

        private PdfEditSession(
            string sourcePath,
            string sessionDirectory,
            IList<PdfEditRevision> revisions,
            int currentIndex,
            int savedIndex,
            long sourceLength,
            long sourceLastWriteUtcTicks,
            string lastSavedTargetPath)
        {
            SourcePath = Path.GetFullPath(sourcePath);
            this.sessionDirectory = Path.GetFullPath(sessionDirectory);
            this.revisions = new List<PdfEditRevision>(revisions);
            this.currentIndex = currentIndex;
            this.savedIndex = savedIndex;
            this.sourceLength = sourceLength;
            this.sourceLastWriteUtcTicks = sourceLastWriteUtcTicks;
            this.lastSavedTargetPath = lastSavedTargetPath ?? string.Empty;
        }

        public string SourcePath { get; private set; }

        public string CurrentPath
        {
            get { return revisions[currentIndex].Path; }
        }

        public string CurrentDescription
        {
            get { return revisions[currentIndex].Description; }
        }

        public PdfEditViewIdentity CurrentViewIdentity
        {
            get { return revisions[currentIndex].ViewIdentity; }
        }

        /// <summary>
        /// File identity captured for the revision that Pdfium is expected to
        /// be displaying. Selection-based editors pass these values back to
        /// their preparation service so an external replacement is rejected
        /// before old viewport coordinates are interpreted against new bytes.
        /// </summary>
        public long CurrentExpectedLength
        {
            get { return CurrentViewIdentity.ExpectedLength; }
        }

        public long CurrentExpectedLastWriteUtcTicks
        {
            get
            {
                return CurrentViewIdentity.ExpectedLastWriteUtcTicks;
            }
        }

        public bool HasUnsavedChanges
        {
            get { return currentIndex != savedIndex; }
        }

        public bool CanUndo
        {
            get { return FindExistingRevisionIndex(currentIndex - 1, -1) >= 0; }
        }

        public bool CanRedo
        {
            get
            {
                return FindExistingRevisionIndex(currentIndex + 1, 1) <
                    revisions.Count;
            }
        }

        public string LastSavedTargetPath
        {
            get { return lastSavedTargetPath; }
        }

        internal string SessionDirectory
        {
            get { return sessionDirectory; }
        }

        public static PdfEditSession Create(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                throw new ArgumentException(
                    "Se necesita el PDF de origen.",
                    "sourcePath");
            }

            var normalizedSourcePath = Path.GetFullPath(sourcePath);
            var sourceInfo = new FileInfo(normalizedSourcePath);
            var sessionDirectory = Path.Combine(
                GetRecoveryRoot(),
                Guid.NewGuid().ToString("N"));
            var initialRevision = new PdfEditRevision(
                normalizedSourcePath,
                "Documento abierto",
                false);

            return new PdfEditSession(
                normalizedSourcePath,
                sessionDirectory,
                new[] { initialRevision },
                0,
                0,
                sourceInfo.Exists ? sourceInfo.Length : -1,
                sourceInfo.Exists ? sourceInfo.LastWriteTimeUtc.Ticks : -1,
                string.Empty);
        }

        public string ReserveRevisionPath()
        {
            return ReserveRevisionPath(0);
        }

        public string ReserveRevisionPath(long estimatedOutputBytes)
        {
            EnsureFreeSpace(estimatedOutputBytes);
            Directory.CreateDirectory(sessionDirectory);
            return Path.Combine(
                sessionDirectory,
                "revision-" +
                DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture) +
                "-" +
                Guid.NewGuid().ToString("N") +
                ".pdf");
        }

        public void CancelReservedRevision(string path)
        {
            if (!IsValidOwnedRevisionPath(sessionDirectory, path))
            {
                return;
            }

            TryDeleteFile(path);
        }

        public void CommitRevision(string path, string description)
        {
            var transaction = BeginRevisionCommit(path, description);
            transaction.Complete();
        }

        /// <summary>
        /// Adds a revision to the logical history without deleting the previous
        /// redo branch yet. The caller must either Complete after the revision is
        /// active in the viewer, or Rollback if activation fails.
        /// </summary>
        public RevisionCommit BeginRevisionCommit(
            string path,
            string description)
        {
            if (activeRevisionCommit != null)
            {
                throw new InvalidOperationException(
                    "Ya hay una revisión pendiente de confirmar.");
            }

            var normalizedPath = Path.GetFullPath(path);
            if (!IsValidOwnedRevisionPath(
                    sessionDirectory,
                    normalizedPath) ||
                !File.Exists(normalizedPath))
            {
                throw new InvalidOperationException(
                    "La revision temporal no existe o no pertenece a esta sesion.");
            }

            ValidateRevisionPdf(normalizedPath);
            var previousRevisions =
                new List<PdfEditRevision>(revisions);
            var previousCurrentIndex = currentIndex;
            var previousSavedIndex = savedIndex;
            var previousCurrentPath = CurrentPath;
            var obsoletePaths = new List<string>();

            try
            {
                TruncateRedoHistory(obsoletePaths);
                revisions.Add(
                    new PdfEditRevision(
                        normalizedPath,
                        string.IsNullOrWhiteSpace(description)
                            ? "Documento editado"
                            : description.Trim(),
                        true));
                currentIndex = revisions.Count - 1;
                TrimHistory(obsoletePaths);
                PersistRecoveryState();
            }
            catch
            {
                revisions.Clear();
                revisions.AddRange(previousRevisions);
                currentIndex = previousCurrentIndex;
                savedIndex = previousSavedIndex;
                throw;
            }

            activeRevisionCommit = new RevisionCommit(
                this,
                previousRevisions,
                previousCurrentIndex,
                previousSavedIndex,
                previousCurrentPath,
                normalizedPath,
                obsoletePaths);
            return activeRevisionCommit;
        }

        private void CompleteRevisionCommit(RevisionCommit transaction)
        {
            EnsureActiveRevisionCommit(transaction);
            activeRevisionCommit = null;
            transaction.MarkCompleted();

            foreach (var obsoletePath in transaction.ObsoletePaths
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.Equals(
                        obsoletePath,
                        transaction.PreviousCurrentPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (!pendingDeletionPaths.Contains(
                            obsoletePath,
                            StringComparer.OrdinalIgnoreCase))
                    {
                        pendingDeletionPaths.Add(obsoletePath);
                    }
                    continue;
                }

                if (IsValidOwnedRevisionPath(
                        sessionDirectory,
                        obsoletePath))
                {
                    TryDeleteFile(obsoletePath);
                }
            }
        }

        private void RollbackRevisionCommit(RevisionCommit transaction)
        {
            EnsureActiveRevisionCommit(transaction);
            var committedRevisions =
                new List<PdfEditRevision>(revisions);
            var committedCurrentIndex = currentIndex;
            var committedSavedIndex = savedIndex;

            revisions.Clear();
            revisions.AddRange(transaction.PreviousRevisions);
            currentIndex = transaction.PreviousCurrentIndex;
            savedIndex = transaction.PreviousSavedIndex;

            try
            {
                PersistRecoveryState();
            }
            catch
            {
                // Keep the in-memory state aligned with the last manifest that
                // was durably written. The caller will fault the edit history
                // and preserve this recoverable committed revision.
                revisions.Clear();
                revisions.AddRange(committedRevisions);
                currentIndex = committedCurrentIndex;
                savedIndex = committedSavedIndex;
                activeRevisionCommit = null;
                transaction.MarkFaulted();
                throw;
            }

            activeRevisionCommit = null;
            transaction.MarkRolledBack();
            if (!transaction.NewRevisionWasPreviouslyTracked)
            {
                TryDeleteFile(transaction.NewRevisionPath);
            }
        }

        private void PreserveRevisionCommitForRecovery(
            RevisionCommit transaction)
        {
            EnsureActiveRevisionCommit(transaction);
            activeRevisionCommit = null;
            transaction.MarkFaulted();
            PersistRecoveryState();
        }

        private void EnsureActiveRevisionCommit(
            RevisionCommit transaction)
        {
            if (transaction == null ||
                !ReferenceEquals(activeRevisionCommit, transaction) ||
                !ReferenceEquals(transaction.Owner, this) ||
                transaction.IsFinished)
            {
                throw new InvalidOperationException(
                    "La transacción de revisión ya no está activa.");
            }
        }

        public string Undo()
        {
            var targetIndex = FindExistingRevisionIndex(currentIndex - 1, -1);
            if (targetIndex < 0)
            {
                return null;
            }

            var previousIndex = currentIndex;
            currentIndex = targetIndex;
            try
            {
                PersistRecoveryState();
            }
            catch
            {
                currentIndex = previousIndex;
                throw;
            }

            return CurrentPath;
        }

        public string GetUndoPath()
        {
            var targetIndex = FindExistingRevisionIndex(currentIndex - 1, -1);
            return targetIndex < 0
                ? null
                : revisions[targetIndex].Path;
        }

        public string Redo()
        {
            var targetIndex = FindExistingRevisionIndex(currentIndex + 1, 1);
            if (targetIndex >= revisions.Count)
            {
                return null;
            }

            var previousIndex = currentIndex;
            currentIndex = targetIndex;
            try
            {
                PersistRecoveryState();
            }
            catch
            {
                currentIndex = previousIndex;
                throw;
            }

            return CurrentPath;
        }

        public string GetRedoPath()
        {
            var targetIndex = FindExistingRevisionIndex(currentIndex + 1, 1);
            return targetIndex >= revisions.Count
                ? null
                : revisions[targetIndex].Path;
        }

        public void MarkCurrentRevisionSaved()
        {
            MarkCurrentRevisionSaved(lastSavedTargetPath);
        }

        public void MarkCurrentRevisionSaved(string targetPath)
        {
            var previousSavedIndex = savedIndex;
            var previousTargetPath = lastSavedTargetPath;
            savedIndex = currentIndex;
            if (!string.IsNullOrWhiteSpace(targetPath))
            {
                lastSavedTargetPath = Path.GetFullPath(targetPath);
            }

            try
            {
                PersistRecoveryState();
            }
            catch
            {
                savedIndex = previousSavedIndex;
                lastSavedTargetPath = previousTargetPath;
                throw;
            }
        }

        public void CleanupObsoleteRevisions(string activePath)
        {
            for (var index = pendingDeletionPaths.Count - 1;
                index >= 0;
                index--)
            {
                var path = pendingDeletionPaths[index];
                if (string.Equals(
                        path,
                        activePath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (IsValidOwnedRevisionPath(sessionDirectory, path))
                {
                    TryDeleteFile(path);
                }

                if (!File.Exists(path))
                {
                    pendingDeletionPaths.RemoveAt(index);
                }
            }
        }

        public void PreserveRecovery()
        {
            PersistRecoveryState();
        }

        public void DeleteRecovery()
        {
            if (!IsSafeSessionDirectory(sessionDirectory) ||
                !Directory.Exists(sessionDirectory))
            {
                return;
            }

            try
            {
                DeleteSessionDirectorySafely(sessionDirectory);
            }
            catch
            {
                // Recovery cleanup must never prevent the viewer from closing.
            }
        }

        public static IList<PdfRecoveryCandidate> FindRecoverableSessions()
        {
            var result = new List<PdfRecoveryCandidate>();
            foreach (var root in GetRecoveryRootsForScan())
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                IEnumerable<string> directories;
                try
                {
                    directories = Directory
                        .EnumerateDirectories(root)
                        .OrderByDescending(directory =>
                        {
                            try
                            {
                                return Directory.GetLastWriteTimeUtc(directory);
                            }
                            catch
                            {
                                return DateTime.MinValue;
                            }
                        })
                        .Take(100)
                        .ToList();
                }
                catch
                {
                    continue;
                }

                var staleCandidates = new List<string>();
                foreach (var directory in directories)
                {
                    PdfRecoveryCandidate candidate;
                    if (TryReadCandidate(directory, out candidate))
                    {
                        result.Add(candidate);
                    }
                    else
                    {
                        staleCandidates.Add(directory);
                    }
                }

                foreach (var staleDirectory in staleCandidates
                    .OrderBy(directory =>
                    {
                        try
                        {
                            return Directory.GetLastWriteTimeUtc(directory);
                        }
                        catch
                        {
                            return DateTime.MaxValue;
                        }
                    })
                    .Take(8))
                {
                    CleanupStaleSessionArtifacts(staleDirectory);
                }
            }

            return result
                .OrderByDescending(candidate => candidate.UpdatedUtc)
                .ToList();
        }

        private static void CleanupStaleSessionArtifacts(string directory)
        {
            if (!IsSafeSessionDirectory(directory) ||
                !Directory.Exists(directory))
            {
                return;
            }

            try
            {
                foreach (var temporaryPath in Directory.EnumerateFiles(
                    directory,
                    "*.tmp",
                    SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        if (File.GetLastWriteTimeUtc(temporaryPath) <
                            DateTime.UtcNow.AddHours(-1))
                        {
                            File.Delete(temporaryPath);
                        }
                    }
                    catch
                    {
                    }
                }

                var manifestPath = Path.Combine(
                    directory,
                    ManifestFileName);
                if (!File.Exists(manifestPath) &&
                    Directory.GetLastWriteTimeUtc(directory) <
                        DateTime.UtcNow.AddDays(-1))
                {
                    foreach (var revisionPath in Directory.EnumerateFiles(
                        directory,
                        "revision-*.pdf",
                        SearchOption.TopDirectoryOnly))
                    {
                        if (IsValidOwnedRevisionPath(
                                directory,
                                revisionPath))
                        {
                            TryDeleteFile(revisionPath);
                        }
                    }

                    if (!Directory.EnumerateFileSystemEntries(directory).Any())
                    {
                        Directory.Delete(directory, false);
                    }
                }
            }
            catch
            {
            }
        }

        public static PdfEditSession Restore(PdfRecoveryCandidate candidate)
        {
            if (candidate == null)
            {
                throw new ArgumentNullException("candidate");
            }

            var validRevisions = candidate.Revisions
                .Where(revision => File.Exists(revision.Path))
                .ToList();
            if (validRevisions.Count == 0)
            {
                throw new InvalidDataException(
                    "La copia de recuperacion ya no existe.");
            }

            var currentIndex = validRevisions.FindIndex(
                revision => string.Equals(
                    revision.Path,
                    candidate.CurrentPath,
                    StringComparison.OrdinalIgnoreCase));
            if (currentIndex < 0)
            {
                throw new InvalidDataException(
                    "No se encuentra la revision activa que se iba a recuperar.");
            }

            var savedPath = candidate.SavedPath;
            var savedIndex = validRevisions.FindIndex(
                revision => string.Equals(
                    revision.Path,
                    savedPath,
                    StringComparison.OrdinalIgnoreCase));
            if (savedIndex < 0)
            {
                savedIndex = -1;
            }

            return new PdfEditSession(
                candidate.SourcePath,
                candidate.SessionDirectory,
                validRevisions,
                currentIndex,
                savedIndex,
                candidate.SourceLength,
                candidate.SourceLastWriteUtcTicks,
                candidate.LastSavedTargetPath);
        }

        public static void Discard(PdfRecoveryCandidate candidate)
        {
            if (candidate == null ||
                !IsSafeSessionDirectory(candidate.SessionDirectory) ||
                !Directory.Exists(candidate.SessionDirectory))
            {
                return;
            }

            try
            {
                DeleteSessionDirectorySafely(candidate.SessionDirectory);
            }
            catch
            {
            }
        }

        private void TruncateRedoHistory(ICollection<string> obsoletePaths)
        {
            while (revisions.Count > currentIndex + 1)
            {
                var revision = revisions[revisions.Count - 1];
                revisions.RemoveAt(revisions.Count - 1);
                if (revision.Owned &&
                    IsValidOwnedRevisionPath(
                        sessionDirectory,
                        revision.Path))
                {
                    obsoletePaths.Add(revision.Path);
                }
            }

            if (savedIndex > currentIndex)
            {
                savedIndex = -1;
            }
        }

        private void TrimHistory(ICollection<string> obsoletePaths)
        {
            while (CountOwnedRevisions() > MaximumOwnedRevisions)
            {
                if (!RemoveOldestOwnedRevision(obsoletePaths))
                {
                    break;
                }
            }

            while (GetOwnedRevisionBytes() > MaximumOwnedRevisionBytes &&
                   CountOwnedRevisions() > 1)
            {
                if (!RemoveOldestOwnedRevision(obsoletePaths))
                {
                    break;
                }
            }
        }

        private int CountOwnedRevisions()
        {
            return revisions.Count(revision => revision.Owned);
        }

        private long GetOwnedRevisionBytes()
        {
            long total = 0;
            foreach (var revision in revisions)
            {
                if (!revision.Owned)
                {
                    continue;
                }

                try
                {
                    total += new FileInfo(revision.Path).Length;
                }
                catch
                {
                }
            }

            return total;
        }

        private bool RemoveOldestOwnedRevision(
            ICollection<string> obsoletePaths)
        {
            for (var index = 1; index < revisions.Count; index++)
            {
                if (!revisions[index].Owned ||
                    index == currentIndex ||
                    index == currentIndex - 1)
                {
                    continue;
                }

                var revision = revisions[index];
                revisions.RemoveAt(index);
                if (IsValidOwnedRevisionPath(
                        sessionDirectory,
                        revision.Path))
                {
                    obsoletePaths.Add(revision.Path);
                }

                if (currentIndex > index)
                {
                    currentIndex--;
                }

                if (savedIndex == index)
                {
                    savedIndex = -1;
                }
                else if (savedIndex > index)
                {
                    savedIndex--;
                }

                return true;
            }

            return false;
        }

        private int FindExistingRevisionIndex(int startIndex, int direction)
        {
            var index = startIndex;
            while (index >= 0 && index < revisions.Count)
            {
                if (File.Exists(revisions[index].Path))
                {
                    return index;
                }

                index += direction;
            }

            return direction < 0 ? -1 : revisions.Count;
        }

        private void PersistRecoveryState()
        {
            var manifestPath = Path.Combine(sessionDirectory, ManifestFileName);
            var currentRevisionIsOwned =
                currentIndex >= 0 &&
                currentIndex < revisions.Count &&
                revisions[currentIndex].Owned;
            if (!HasUnsavedChanges && !currentRevisionIsOwned)
            {
                TryDeleteFile(manifestPath);
                return;
            }

            Directory.CreateDirectory(sessionDirectory);
            var temporaryManifestPath = Path.Combine(
                sessionDirectory,
                "." + ManifestFileName + "." +
                Guid.NewGuid().ToString("N") + ".tmp");
            var lines = new List<string>
            {
                ManifestHeader,
                "source=" + Encode(SourcePath),
                "current=" + Encode(CurrentPath),
                "saved=" + Encode(
                    savedIndex >= 0 && savedIndex < revisions.Count
                        ? revisions[savedIndex].Path
                        : string.Empty),
                "savedTarget=" + Encode(lastSavedTargetPath),
                "sourceLength=" +
                    sourceLength.ToString(CultureInfo.InvariantCulture),
                "sourceLastWriteUtcTicks=" +
                    sourceLastWriteUtcTicks.ToString(
                        CultureInfo.InvariantCulture),
                "updatedUtcTicks=" +
                    DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture),
                "revisionCount=" +
                    revisions.Count.ToString(CultureInfo.InvariantCulture)
            };

            for (var index = 0; index < revisions.Count; index++)
            {
                var revision = revisions[index];
                lines.Add(
                    "revision." + index.ToString(CultureInfo.InvariantCulture) +
                    ".path=" + Encode(revision.Path));
                lines.Add(
                    "revision." + index.ToString(CultureInfo.InvariantCulture) +
                    ".description=" + Encode(revision.Description));
                lines.Add(
                    "revision." + index.ToString(CultureInfo.InvariantCulture) +
                    ".owned=" + (revision.Owned ? "1" : "0"));
            }

            try
            {
                using (var stream = new FileStream(
                    temporaryManifestPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.WriteThrough))
                using (var writer = new StreamWriter(
                    stream,
                    new UTF8Encoding(false)))
                {
                    foreach (var line in lines)
                    {
                        writer.WriteLine(line);
                    }

                    writer.Flush();
                    stream.Flush(true);
                }

                if (File.Exists(manifestPath))
                {
                    File.Replace(temporaryManifestPath, manifestPath, null);
                }
                else
                {
                    File.Move(temporaryManifestPath, manifestPath);
                }
            }
            finally
            {
                TryDeleteFile(temporaryManifestPath);
            }
        }

        private static bool TryReadCandidate(
            string sessionDirectory,
            out PdfRecoveryCandidate candidate)
        {
            candidate = null;
            if (!IsSafeSessionDirectory(sessionDirectory))
            {
                return false;
            }

            var manifestPath = Path.Combine(sessionDirectory, ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                return false;
            }

            try
            {
                var lines = File.ReadAllLines(manifestPath, Encoding.UTF8);
                if (lines.Length < 2 ||
                    !string.Equals(lines[0], ManifestHeader, StringComparison.Ordinal))
                {
                    return false;
                }

                var values = new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);
                for (var index = 1; index < lines.Length; index++)
                {
                    var separator = lines[index].IndexOf('=');
                    if (separator <= 0)
                    {
                        continue;
                    }

                    values[lines[index].Substring(0, separator)] =
                        lines[index].Substring(separator + 1);
                }

                var sourcePath = Decode(GetValue(values, "source"));
                var currentPath = Decode(GetValue(values, "current"));
                var savedPath = Decode(GetValue(values, "saved"));
                var savedTargetPath = Decode(
                    GetValue(values, "savedTarget"));
                int revisionCount;
                long updatedUtcTicks;
                long sourceLength;
                long sourceLastWriteUtcTicks;
                if (string.IsNullOrWhiteSpace(sourcePath) ||
                    string.IsNullOrWhiteSpace(currentPath) ||
                    !File.Exists(currentPath) ||
                    !int.TryParse(
                        GetValue(values, "revisionCount"),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out revisionCount) ||
                    revisionCount < 1 ||
                    revisionCount > 100 ||
                    !long.TryParse(
                        GetValue(values, "updatedUtcTicks"),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out updatedUtcTicks))
                {
                    return false;
                }

                if (!long.TryParse(
                        GetValue(values, "sourceLength"),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out sourceLength))
                {
                    sourceLength = -1;
                }

                if (!long.TryParse(
                        GetValue(values, "sourceLastWriteUtcTicks"),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out sourceLastWriteUtcTicks))
                {
                    sourceLastWriteUtcTicks = -1;
                }

                var revisions = new List<PdfEditRevision>();
                for (var index = 0; index < revisionCount; index++)
                {
                    var prefix =
                        "revision." +
                        index.ToString(CultureInfo.InvariantCulture);
                    var revisionPath = Decode(
                        GetValue(values, prefix + ".path"));
                    if (string.IsNullOrWhiteSpace(revisionPath))
                    {
                        continue;
                    }

                    var normalizedRevisionPath =
                        Path.GetFullPath(revisionPath);
                    var owned = string.Equals(
                        GetValue(values, prefix + ".owned"),
                        "1",
                        StringComparison.Ordinal);
                    if (owned &&
                        !IsValidOwnedRevisionPath(
                            sessionDirectory,
                            normalizedRevisionPath))
                    {
                        return false;
                    }

                    revisions.Add(
                        new PdfEditRevision(
                            normalizedRevisionPath,
                            Decode(GetValue(values, prefix + ".description")),
                            owned));
                }

                if (revisions.Count == 0)
                {
                    return false;
                }

                candidate = new PdfRecoveryCandidate(
                    Path.GetFullPath(sessionDirectory),
                    Path.GetFullPath(sourcePath),
                    Path.GetFullPath(currentPath),
                    savedPath,
                    new DateTime(
                        Math.Max(
                            DateTime.MinValue.Ticks,
                            Math.Min(DateTime.MaxValue.Ticks, updatedUtcTicks)),
                        DateTimeKind.Utc),
                    sourceLength,
                    sourceLastWriteUtcTicks,
                    savedTargetPath,
                    revisions);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsValidOwnedRevisionPath(
            string sessionDirectory,
            string path)
        {
            if (string.IsNullOrWhiteSpace(sessionDirectory) ||
                string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                var normalizedSession = Path.GetFullPath(
                    sessionDirectory).TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar);
                var normalizedPath = Path.GetFullPath(path);
                if (!string.Equals(
                        Path.GetDirectoryName(normalizedPath),
                        normalizedSession,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(
                        Path.GetExtension(normalizedPath),
                        ".pdf",
                        StringComparison.OrdinalIgnoreCase) ||
                    !Path.GetFileName(normalizedPath).StartsWith(
                        "revision-",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (File.Exists(normalizedPath))
                {
                    var attributes = File.GetAttributes(normalizedPath);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        return false;
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsSafeSessionDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return false;
            }

            try
            {
                var normalizedDirectory = Path.GetFullPath(directory).TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
                if (Directory.Exists(normalizedDirectory) &&
                    (File.GetAttributes(normalizedDirectory) &
                     FileAttributes.ReparsePoint) != 0)
                {
                    return false;
                }

                Guid sessionId;
                if (!Guid.TryParseExact(
                        Path.GetFileName(normalizedDirectory),
                        "N",
                        out sessionId))
                {
                    return false;
                }

                foreach (var root in GetRecoveryRootsForScan())
                {
                    var normalizedRoot = Path.GetFullPath(root).TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar);
                    if (string.Equals(
                            Path.GetDirectoryName(normalizedDirectory),
                            normalizedRoot,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static string GetRecoveryRoot()
        {
            var overridePath = Environment.GetEnvironmentVariable(
                RecoveryRootOverrideEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                return Path.GetFullPath(overridePath);
            }

            lock (RecoveryRootSync)
            {
                if (!string.IsNullOrWhiteSpace(cachedDefaultRecoveryRoot))
                {
                    return cachedDefaultRecoveryRoot;
                }
            }

            var localRoot = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "PDFLigero",
                "Recovery");
            lock (RecoveryRootSync)
            {
                cachedDefaultRecoveryRoot = localRoot;
            }

            return localRoot;
        }

        private static IEnumerable<string> GetRecoveryRootsForScan()
        {
            var overridePath = Environment.GetEnvironmentVariable(
                RecoveryRootOverrideEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                return new[] { Path.GetFullPath(overridePath) };
            }

            return new[] { GetRecoveryRoot() };
        }

        private void EnsureFreeSpace(long estimatedOutputBytes)
        {
            if (estimatedOutputBytes <= 0)
            {
                return;
            }

            try
            {
                var currentRecoveryBytes = GetGlobalRecoveryBytes();
                if (SaturatingAdd(
                        currentRecoveryBytes,
                        estimatedOutputBytes) >
                    MaximumGlobalRecoveryBytes)
                {
                    throw new IOException(
                        "El historial temporal de PDF Ligero alcanzaría el límite " +
                        "seguro de 2 GB. Guarda y cierra alguna edición antes de " +
                        "continuar.");
                }

                var root = Path.GetPathRoot(sessionDirectory);
                if (string.IsNullOrWhiteSpace(root))
                {
                    return;
                }

                var drive = new DriveInfo(root);
                if (!drive.IsReady)
                {
                    return;
                }

                var estimatedWithMargin = SaturatingAdd(
                    estimatedOutputBytes,
                    Math.Max(
                        64L * 1024L * 1024L,
                        estimatedOutputBytes / 4L));
                const long safetyReserve = 512L * 1024L * 1024L;
                if (drive.AvailableFreeSpace <
                    SaturatingAdd(
                        estimatedWithMargin,
                        safetyReserve))
                {
                    throw new IOException(
                        "No hay espacio libre suficiente para crear una revision " +
                        "segura. Libera espacio o guarda y cierra alguna edicion.");
                }
            }
            catch (IOException)
            {
                throw;
            }
            catch
            {
                // If Windows cannot report capacity (for example, a network volume),
                // the PDF writer will still fail safely before replacing any source.
            }
        }

        private static long GetGlobalRecoveryBytes()
        {
            var root = GetRecoveryRoot();
            if (!Directory.Exists(root))
            {
                return 0;
            }

            long total = 0;
            IEnumerable<string> sessionDirectories;
            try
            {
                sessionDirectories = Directory
                    .EnumerateDirectories(root)
                    .ToList();
            }
            catch
            {
                return 0;
            }

            foreach (var directory in sessionDirectories)
            {
                if (!IsSafeSessionDirectory(directory))
                {
                    continue;
                }

                IEnumerable<string> files;
                try
                {
                    files = Directory
                        .EnumerateFiles(
                            directory,
                            "revision-*.pdf",
                            SearchOption.TopDirectoryOnly)
                        .ToList();
                }
                catch
                {
                    continue;
                }

                foreach (var file in files)
                {
                    if (!IsValidOwnedRevisionPath(directory, file))
                    {
                        continue;
                    }

                    try
                    {
                        total = SaturatingAdd(
                            total,
                            new FileInfo(file).Length);
                    }
                    catch
                    {
                    }
                }
            }

            return total;
        }

        private static long SaturatingAdd(long left, long right)
        {
            if (left < 0 || right < 0 || left > long.MaxValue - right)
            {
                return long.MaxValue;
            }

            return left + right;
        }

        private static string Encode(string value)
        {
            return Convert.ToBase64String(
                Encoding.UTF8.GetBytes(value ?? string.Empty));
        }

        private static string Decode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return Encoding.UTF8.GetString(Convert.FromBase64String(value));
        }

        private static string GetValue(
            IDictionary<string, string> values,
            string key)
        {
            string value;
            return values.TryGetValue(key, out value)
                ? value
                : string.Empty;
        }

        private static void TryDeleteFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

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

        private static void DeleteSessionDirectorySafely(string directory)
        {
            if (!IsSafeSessionDirectory(directory) ||
                !Directory.Exists(directory))
            {
                return;
            }

            var directoryInfo = new DirectoryInfo(directory);
            if ((directoryInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(
                directory,
                "*",
                SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(file);
                var knownManifest =
                    string.Equals(
                        name,
                        ManifestFileName,
                        StringComparison.OrdinalIgnoreCase) ||
                    (name.StartsWith(
                        "." + ManifestFileName + ".",
                        StringComparison.OrdinalIgnoreCase) &&
                     name.EndsWith(
                        ".tmp",
                        StringComparison.OrdinalIgnoreCase));
                if (knownManifest ||
                    IsValidOwnedRevisionPath(directory, file))
                {
                    TryDeleteFile(file);
                }
            }

            if (!Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory, false);
            }
        }

        private static void ValidateRevisionPdf(string path)
        {
            PdfReader reader = null;
            try
            {
                reader = new PdfReader(path);
                if (reader.NumberOfPages < 1)
                {
                    throw new InvalidDataException(
                        "La revision no contiene paginas.");
                }
            }
            catch (Exception ex)
            {
                throw new InvalidDataException(
                    "La revision temporal no es un PDF valido: " +
                    ex.GetBaseException().Message,
                    ex);
            }
            finally
            {
                if (reader != null)
                {
                    reader.Close();
                }
            }
        }

        internal sealed class RevisionCommit
        {
            private bool isFinished;

            internal RevisionCommit(
                PdfEditSession owner,
                IList<PdfEditRevision> previousRevisions,
                int previousCurrentIndex,
                int previousSavedIndex,
                string previousCurrentPath,
                string newRevisionPath,
                IEnumerable<string> obsoletePaths)
            {
                Owner = owner;
                PreviousRevisions =
                    new List<PdfEditRevision>(previousRevisions);
                PreviousCurrentIndex = previousCurrentIndex;
                PreviousSavedIndex = previousSavedIndex;
                PreviousCurrentPath = previousCurrentPath;
                NewRevisionPath = newRevisionPath;
                ObsoletePaths = new List<string>(
                    obsoletePaths ?? Enumerable.Empty<string>());
                NewRevisionWasPreviouslyTracked =
                    PreviousRevisions.Any(revision =>
                        string.Equals(
                            revision.Path,
                            newRevisionPath,
                            StringComparison.OrdinalIgnoreCase));
            }

            internal PdfEditSession Owner { get; private set; }

            internal IList<PdfEditRevision> PreviousRevisions
            {
                get;
                private set;
            }

            internal int PreviousCurrentIndex { get; private set; }

            internal int PreviousSavedIndex { get; private set; }

            internal string PreviousCurrentPath { get; private set; }

            internal string NewRevisionPath { get; private set; }

            internal IList<string> ObsoletePaths { get; private set; }

            internal bool NewRevisionWasPreviouslyTracked
            {
                get;
                private set;
            }

            internal bool IsFinished
            {
                get { return isFinished; }
            }

            public void Complete()
            {
                Owner.CompleteRevisionCommit(this);
            }

            public void Rollback()
            {
                Owner.RollbackRevisionCommit(this);
            }

            public void PreserveForRecovery()
            {
                Owner.PreserveRevisionCommitForRecovery(this);
            }

            internal void MarkCompleted()
            {
                isFinished = true;
            }

            internal void MarkRolledBack()
            {
                isFinished = true;
            }

            internal void MarkFaulted()
            {
                isFinished = true;
            }
        }
    }

    internal sealed class PdfRecoveryCandidate
    {
        public PdfRecoveryCandidate(
            string sessionDirectory,
            string sourcePath,
            string currentPath,
            string savedPath,
            DateTime updatedUtc,
            long sourceLength,
            long sourceLastWriteUtcTicks,
            string lastSavedTargetPath,
            IList<PdfEditRevision> revisions)
        {
            SessionDirectory = sessionDirectory;
            SourcePath = sourcePath;
            CurrentPath = currentPath;
            SavedPath = savedPath;
            UpdatedUtc = updatedUtc;
            SourceLength = sourceLength;
            SourceLastWriteUtcTicks = sourceLastWriteUtcTicks;
            LastSavedTargetPath = lastSavedTargetPath ?? string.Empty;
            Revisions = new List<PdfEditRevision>(revisions);
        }

        public string SessionDirectory { get; private set; }

        public string SourcePath { get; private set; }

        public string CurrentPath { get; private set; }

        public string SavedPath { get; private set; }

        public DateTime UpdatedUtc { get; private set; }

        public long SourceLength { get; private set; }

        public long SourceLastWriteUtcTicks { get; private set; }

        public string LastSavedTargetPath { get; private set; }

        public IList<PdfEditRevision> Revisions { get; private set; }

        public bool SourceChangedSinceEditing
        {
            get
            {
                try
                {
                    var info = new FileInfo(SourcePath);
                    return info.Exists &&
                        SourceLength >= 0 &&
                        SourceLastWriteUtcTicks >= 0 &&
                        (info.Length != SourceLength ||
                         info.LastWriteTimeUtc.Ticks !=
                            SourceLastWriteUtcTicks);
                }
                catch
                {
                    return false;
                }
            }
        }

        public string DisplayName
        {
            get
            {
                var path = !string.IsNullOrWhiteSpace(SourcePath)
                    ? SourcePath
                    : CurrentPath;
                return Path.GetFileName(path);
            }
        }
    }

    internal sealed class PdfEditRevision
    {
        public PdfEditRevision(
            string path,
            string description,
            bool owned)
        {
            Path = System.IO.Path.GetFullPath(path);
            Description = string.IsNullOrWhiteSpace(description)
                ? "Documento editado"
                : description;
            Owned = owned;

            var info = new FileInfo(Path);
            ExpectedLength = info.Exists ? info.Length : -1;
            ExpectedLastWriteUtcTicks = info.Exists
                ? info.LastWriteTimeUtc.Ticks
                : -1;
            ViewIdentity = new PdfEditViewIdentity(
                Path,
                ExpectedLength,
                ExpectedLastWriteUtcTicks);
        }

        public string Path { get; private set; }

        public string Description { get; private set; }

        public bool Owned { get; private set; }

        public long ExpectedLength { get; private set; }

        public long ExpectedLastWriteUtcTicks { get; private set; }

        public PdfEditViewIdentity ViewIdentity { get; private set; }
    }
}

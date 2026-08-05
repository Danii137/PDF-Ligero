using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace FirmaAutomatica
{
    internal static class PdfAtomicFileService
    {
        private const int FingerprintSampleBytes = 32 * 1024;

        public static void SaveCopy(string sourcePath, string targetPath)
        {
            SaveCopyWithContentHash(sourcePath, targetPath);
        }

        public static string SaveCopyWithContentHash(
            string sourcePath,
            string targetPath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) ||
                !File.Exists(sourcePath))
            {
                throw new FileNotFoundException(
                    "No se encuentra la revision activa del PDF.",
                    sourcePath);
            }

            var normalizedSource = Path.GetFullPath(sourcePath);
            var normalizedTarget = Path.GetFullPath(targetPath);
            if (string.Equals(
                    normalizedSource,
                    normalizedTarget,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "La copia debe guardarse con otro nombre.");
            }

            var directory = Path.GetDirectoryName(normalizedTarget);
            if (string.IsNullOrWhiteSpace(directory) ||
                !Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException(
                    "La carpeta de destino ya no existe.");
            }

            EnsureFreeSpace(normalizedSource, directory);
            var temporaryPath = Path.Combine(
                directory,
                "." + Path.GetFileNameWithoutExtension(normalizedTarget) +
                "." + Guid.NewGuid().ToString("N") + ".tmp");

            try
            {
                var contentHash =
                    CopyDurably(normalizedSource, temporaryPath);
                ValidateCopy(normalizedSource, temporaryPath);

                if (File.Exists(normalizedTarget))
                {
                    File.Replace(temporaryPath, normalizedTarget, null);
                }
                else
                {
                    File.Move(temporaryPath, normalizedTarget);
                }

                return contentHash;
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }
                catch
                {
                }
            }
        }

        public static string ComputeFullContentHash(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                throw new FileNotFoundException(
                    "No se encuentra el PDF que se iba a comprobar.",
                    path);
            }

            using (var input = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                1024 * 1024,
                FileOptions.SequentialScan))
            {
                return ComputeFullContentHash(input);
            }
        }

        public static string ComputeFullContentHash(Stream input)
        {
            if (input == null || !input.CanRead)
            {
                throw new ArgumentException(
                    "Se necesita un flujo legible para calcular la huella.",
                    "input");
            }

            if (input.CanSeek)
            {
                input.Position = 0;
            }

            using (var sha256 = SHA256.Create())
            {
                return ToHex(sha256.ComputeHash(input));
            }
        }

        /// <summary>
        /// Creates a fast content fingerprint from five small ranges distributed
        /// across the file. It detects normal replacement/sync edits even when a
        /// provider preserves size and timestamps, while reading at most 160 KiB.
        /// </summary>
        public static string ComputeContentFingerprint(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                throw new FileNotFoundException(
                    "No se encuentra el PDF que se iba a comprobar.",
                    path);
            }

            using (var input = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                FingerprintSampleBytes,
                FileOptions.RandomAccess))
            using (var sampledContent = new MemoryStream())
            using (var writer = new BinaryWriter(sampledContent))
            using (var sha256 = SHA256.Create())
            {
                var length = input.Length;
                writer.Write(length);

                var maximumStart = Math.Max(
                    0L,
                    length - FingerprintSampleBytes);
                var requestedOffsets = new[]
                {
                    0L,
                    Math.Max(0L, (length / 4L) - (FingerprintSampleBytes / 2L)),
                    Math.Max(0L, (length / 2L) - (FingerprintSampleBytes / 2L)),
                    Math.Max(
                        0L,
                        ((length * 3L) / 4L) -
                        (FingerprintSampleBytes / 2L)),
                    maximumStart
                };
                var offsets = new List<long>();
                foreach (var requestedOffset in requestedOffsets)
                {
                    var offset = Math.Min(maximumStart, requestedOffset);
                    if (!offsets.Contains(offset))
                    {
                        offsets.Add(offset);
                    }
                }

                offsets.Sort();
                var buffer = new byte[FingerprintSampleBytes];
                foreach (var offset in offsets)
                {
                    input.Position = offset;
                    var expected = (int)Math.Min(
                        FingerprintSampleBytes,
                        length - offset);
                    var total = 0;
                    while (total < expected)
                    {
                        var read = input.Read(
                            buffer,
                            total,
                            expected - total);
                        if (read <= 0)
                        {
                            break;
                        }

                        total += read;
                    }

                    if (total != expected)
                    {
                        throw new EndOfStreamException(
                            "El PDF cambió mientras se comprobaba.");
                    }

                    writer.Write(offset);
                    writer.Write(total);
                    writer.Write(buffer, 0, total);
                }

                writer.Flush();
                sampledContent.Position = 0;
                return BitConverter.ToString(
                        sha256.ComputeHash(sampledContent))
                    .Replace("-", string.Empty);
            }
        }

        private static void ValidateCopy(
            string sourcePath,
            string copiedPath)
        {
            var sourceLength = new FileInfo(sourcePath).Length;
            var copiedLength = new FileInfo(copiedPath).Length;
            if (sourceLength <= 0 || copiedLength != sourceLength)
            {
                throw new InvalidDataException(
                    "La copia temporal no coincide con el PDF de origen.");
            }

            var sampleLength = (int)Math.Min(4096L, sourceLength);
            if (!CompareRange(
                    sourcePath,
                    copiedPath,
                    0,
                    sampleLength) ||
                !CompareRange(
                    sourcePath,
                    copiedPath,
                    sourceLength - sampleLength,
                    sampleLength))
            {
                throw new InvalidDataException(
                    "La verificacion de la copia temporal no coincide con el origen.");
            }
        }

        private static string CopyDurably(
            string sourcePath,
            string targetPath)
        {
            var buffer = new byte[1024 * 1024];
            using (var source = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            using (var target = new FileStream(
                targetPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                buffer.Length,
                FileOptions.SequentialScan | FileOptions.WriteThrough))
            using (var sha256 = SHA256.Create())
            {
                int read;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    sha256.TransformBlock(
                        buffer,
                        0,
                        read,
                        buffer,
                        0);
                    target.Write(buffer, 0, read);
                }

                sha256.TransformFinalBlock(new byte[0], 0, 0);
                target.Flush(true);
                return ToHex(sha256.Hash);
            }
        }

        private static bool CompareRange(
            string firstPath,
            string secondPath,
            long offset,
            int count)
        {
            var firstBuffer = new byte[count];
            var secondBuffer = new byte[count];
            using (var first = new FileStream(
                firstPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            using (var second = new FileStream(
                secondPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            {
                first.Position = offset;
                second.Position = offset;
                if (ReadExactly(first, firstBuffer) != count ||
                    ReadExactly(second, secondBuffer) != count)
                {
                    return false;
                }
            }

            for (var index = 0; index < count; index++)
            {
                if (firstBuffer[index] != secondBuffer[index])
                {
                    return false;
                }
            }

            return true;
        }

        private static int ReadExactly(
            Stream stream,
            byte[] buffer)
        {
            var total = 0;
            while (total < buffer.Length)
            {
                var read = stream.Read(
                    buffer,
                    total,
                    buffer.Length - total);
                if (read <= 0)
                {
                    break;
                }

                total += read;
            }

            return total;
        }

        private static void EnsureFreeSpace(
            string sourcePath,
            string targetDirectory)
        {
            try
            {
                var sourceBytes = new FileInfo(sourcePath).Length;
                var root = Path.GetPathRoot(targetDirectory);
                if (string.IsNullOrWhiteSpace(root))
                {
                    return;
                }

                var drive = new DriveInfo(root);
                const long safetyReserve = 256L * 1024L * 1024L;
                if (drive.IsReady &&
                    drive.AvailableFreeSpace < sourceBytes + safetyReserve)
                {
                    throw new IOException(
                        "No hay espacio libre suficiente para guardar una copia " +
                        "de forma segura.");
                }
            }
            catch (IOException)
            {
                throw;
            }
            catch
            {
                // Some network providers do not expose capacity. The atomic copy still
                // leaves the existing destination untouched if writing fails.
            }
        }

        private static string ToHex(byte[] bytes)
        {
            return BitConverter.ToString(bytes)
                .Replace("-", string.Empty);
        }
    }
}

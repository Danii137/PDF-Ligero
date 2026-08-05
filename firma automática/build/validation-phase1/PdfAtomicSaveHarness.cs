using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace FirmaAutomatica
{
    internal static class PdfAtomicSaveHarness
    {
        private static int Main(string[] args)
        {
            if (args.Length != 2)
            {
                Console.Error.WriteLine(
                    "Uso: PdfAtomicSaveHarness <source.pdf> <output-dir>");
                return 2;
            }

            var source = Path.GetFullPath(args[0]);
            var outputDirectory = Path.GetFullPath(args[1]);
            Directory.CreateDirectory(outputDirectory);
            var target = Path.Combine(outputDirectory, "saved-copy.pdf");

            var copiedHash =
                PdfAtomicFileService.SaveCopyWithContentHash(
                    source,
                    target);
            Require(Hash(source) == Hash(target), "La copia nueva no es idéntica.");
            Require(
                copiedHash ==
                    PdfAtomicFileService.ComputeFullContentHash(source) &&
                copiedHash ==
                    PdfAtomicFileService.ComputeFullContentHash(target),
                "La huella completa de la copia no coincide.");

            File.WriteAllBytes(target, new byte[] { 1, 2, 3, 4, 5, 6 });
            PdfAtomicFileService.SaveCopy(source, target);
            Require(Hash(source) == Hash(target), "El reemplazo no es idéntico.");

            var prefixedSource = Path.Combine(
                outputDirectory,
                "prefixed-source.pdf");
            var prefixedTarget = Path.Combine(
                outputDirectory,
                "prefixed-copy.pdf");
            using (var output = File.Create(prefixedSource))
            {
                output.WriteByte(0xEF);
                output.WriteByte(0xBB);
                output.WriteByte(0xBF);
                using (var input = File.OpenRead(source))
                {
                    input.CopyTo(output);
                }
            }

            PdfAtomicFileService.SaveCopy(prefixedSource, prefixedTarget);
            Require(
                Hash(prefixedSource) == Hash(prefixedTarget),
                "La copia con prefijo no es idéntica.");

            var fingerprintPath = Path.Combine(
                outputDirectory,
                "fingerprint-same-metadata.pdf");
            File.Copy(source, fingerprintPath, true);
            var originalTimestamp =
                File.GetLastWriteTimeUtc(fingerprintPath);
            var originalFingerprint =
                PdfAtomicFileService.ComputeContentFingerprint(
                    fingerprintPath);
            using (var stream = new FileStream(
                fingerprintPath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None))
            {
                stream.Position = Math.Max(0L, stream.Length / 2L);
                var originalByte = stream.ReadByte();
                Require(
                    originalByte >= 0,
                    "No se pudo preparar la prueba de huella.");
                stream.Position--;
                stream.WriteByte((byte)(originalByte ^ 0x01));
                stream.Flush(true);
            }

            File.SetLastWriteTimeUtc(
                fingerprintPath,
                originalTimestamp);
            Require(
                new FileInfo(fingerprintPath).Length ==
                    new FileInfo(source).Length,
                "La prueba de huella cambió el tamaño.");
            Require(
                File.GetLastWriteTimeUtc(fingerprintPath) ==
                    originalTimestamp,
                "La prueba de huella no conservó la fecha.");
            Require(
                !string.Equals(
                    originalFingerprint,
                    PdfAtomicFileService.ComputeContentFingerprint(
                        fingerprintPath),
                    StringComparison.Ordinal),
                "La huella no detectó un cambio con igual tamaño y fecha.");
            Require(
                !string.Equals(
                    copiedHash,
                    PdfAtomicFileService.ComputeFullContentHash(
                        fingerprintPath),
                    StringComparison.Ordinal),
                "SHA-256 completo no detectó el cambio de contenido.");
            Require(
                !Directory.EnumerateFiles(
                    outputDirectory,
                    "*.tmp",
                    SearchOption.TopDirectoryOnly).Any(),
                "Quedaron temporales.");

            Console.WriteLine("PASS");
            return 0;
        }

        private static string Hash(string path)
        {
            using (var algorithm = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                return Convert.ToBase64String(
                    algorithm.ComputeHash(stream));
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}

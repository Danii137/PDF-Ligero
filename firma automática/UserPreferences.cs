using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace FirmaAutomatica
{
    internal sealed class CertificateSelectionPreferences
    {
        public string Source { get; set; }

        public string StoreThumbprint { get; set; }

        public string FilePath { get; set; }
    }

    internal static class UserPreferences
    {
        private static readonly string PreferencesDirectory =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FirmaAutomatica");

        private static readonly string PreferencesPath = Path.Combine(PreferencesDirectory, "preferences.ini");

        private static readonly string SignatureGraphicsDirectory =
            Path.Combine(PreferencesDirectory, "signatures");

        public static CertificateSelectionPreferences LoadCertificateSelection()
        {
            var values = LoadKeyValuePairs();
            return new CertificateSelectionPreferences
            {
                Source = GetValue(values, "certificate.source"),
                StoreThumbprint = GetValue(values, "certificate.storeThumbprint"),
                FilePath = GetValue(values, "certificate.filePath")
            };
        }

        public static void SaveCertificateSelection(string source, string storeThumbprint, string filePath)
        {
            var values = LoadKeyValuePairs();
            values["certificate.source"] = source ?? string.Empty;
            values["certificate.storeThumbprint"] = storeThumbprint ?? string.Empty;
            values["certificate.filePath"] = filePath ?? string.Empty;

            Directory.CreateDirectory(PreferencesDirectory);
            using (var writer = new StreamWriter(PreferencesPath, false))
            {
                foreach (var pair in values)
                {
                    writer.WriteLine(pair.Key + "=" + pair.Value);
                }
            }
        }

        public static string GetCertificateSignatureGraphicPath(string thumbprint)
        {
            var normalizedThumbprint = NormalizeThumbprint(thumbprint);
            if (string.IsNullOrWhiteSpace(normalizedThumbprint))
            {
                return null;
            }

            var path = Path.Combine(SignatureGraphicsDirectory, normalizedThumbprint + ".png");
            return File.Exists(path) ? path : null;
        }

        public static string SaveCertificateSignatureGraphic(string thumbprint, string sourcePath)
        {
            var normalizedThumbprint = NormalizeThumbprint(thumbprint);
            if (string.IsNullOrWhiteSpace(normalizedThumbprint))
            {
                throw new InvalidOperationException("El certificado no tiene una huella valida.");
            }

            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                throw new FileNotFoundException("No se encuentra la imagen de firma seleccionada.", sourcePath);
            }

            Directory.CreateDirectory(SignatureGraphicsDirectory);
            var destinationPath = Path.Combine(SignatureGraphicsDirectory, normalizedThumbprint + ".png");
            var temporaryPath = Path.Combine(SignatureGraphicsDirectory, normalizedThumbprint + "." + Guid.NewGuid().ToString("N") + ".tmp");

            try
            {
                using (var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var sourceImage = Image.FromStream(sourceStream, false, true))
                {
                    if (sourceImage.Width <= 0 || sourceImage.Height <= 0)
                    {
                        throw new InvalidDataException("La imagen de firma no tiene un tamano valido.");
                    }

                    using (var normalizedImage = new Bitmap(sourceImage.Width, sourceImage.Height, PixelFormat.Format32bppArgb))
                    using (var graphics = Graphics.FromImage(normalizedImage))
                    {
                        graphics.Clear(Color.Transparent);
                        graphics.DrawImage(sourceImage, new Rectangle(0, 0, normalizedImage.Width, normalizedImage.Height));
                        normalizedImage.Save(temporaryPath, ImageFormat.Png);
                    }
                }

                File.Copy(temporaryPath, destinationPath, true);
                return destinationPath;
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

        public static void ResetCertificateSignatureGraphic(string thumbprint)
        {
            var normalizedThumbprint = NormalizeThumbprint(thumbprint);
            if (string.IsNullOrWhiteSpace(normalizedThumbprint))
            {
                return;
            }

            var path = Path.Combine(SignatureGraphicsDirectory, normalizedThumbprint + ".png");
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static Dictionary<string, string> LoadKeyValuePairs()
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(PreferencesPath))
            {
                return values;
            }

            foreach (var rawLine in File.ReadAllLines(PreferencesPath))
            {
                if (string.IsNullOrWhiteSpace(rawLine))
                {
                    continue;
                }

                var separatorIndex = rawLine.IndexOf('=');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                var key = rawLine.Substring(0, separatorIndex).Trim();
                var value = rawLine.Substring(separatorIndex + 1);
                values[key] = value;
            }

            return values;
        }

        private static string GetValue(Dictionary<string, string> values, string key)
        {
            string value;
            return values.TryGetValue(key, out value) ? value : string.Empty;
        }

        private static string NormalizeThumbprint(string thumbprint)
        {
            if (string.IsNullOrWhiteSpace(thumbprint))
            {
                return string.Empty;
            }

            return new string(
                thumbprint
                    .Where(Uri.IsHexDigit)
                    .Select(character => char.ToUpperInvariant(character))
                    .ToArray());
        }
    }
}

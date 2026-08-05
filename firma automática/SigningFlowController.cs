using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Forms;
using iTextSharp.text;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.security;
using Org.BouncyCastle.Security;

namespace FirmaAutomatica
{
    internal sealed class SigningFlowController : IDisposable
    {
        private readonly IReadOnlyList<string> pdfFiles;
        private readonly X509Certificate2 signingCertificate;
        private readonly SignatureAppearanceProfile signatureProfile;

        public SigningFlowController(IReadOnlyList<string> pdfFiles, X509Certificate2 signingCertificate)
        {
            this.pdfFiles = pdfFiles;
            this.signingCertificate = signingCertificate;
            signatureProfile = BuildSignatureProfile(signingCertificate);
        }

        public void Run()
        {
            var fileResolution = ResolveFilesToSign(pdfFiles);
            if (fileResolution == null)
            {
                AppLog.Write("Proceso cancelado por conflicto de sobrescritura.");
                return;
            }

            if (fileResolution.FilesToSign.Count == 0)
            {
                AppLog.Write("Todos los PDFs se han omitido por archivos _f existentes.");
                MessageBox.Show(
                    "No se ha firmado ningun PDF porque ya existian las copias con sufijo _f y has elegido conservarlas.",
                    "Firma automatica",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            var placements = new List<SignaturePlacement>();
            AppLog.Write("Inicio de flujo de colocacion.");
            for (var i = 0; i < fileResolution.FilesToSign.Count; i++)
            {
                var pdfFile = fileResolution.FilesToSign[i];
                AppLog.Write("Abriendo visor para: " + pdfFile);
                using (var placementForm = new PdfPlacementForm(pdfFile, i + 1, fileResolution.FilesToSign.Count, signatureProfile))
                {
                    if (placementForm.ShowDialog() != DialogResult.OK)
                    {
                        AppLog.Write("Colocacion cancelada por el usuario.");
                        return;
                    }

                    placements.Add(placementForm.GetPlacement());
                }
            }

            AppLog.Write("Iniciando firma de " + placements.Count + " PDF(s).");
            List<string> outputPaths;
            using (var progressForm = new SigningProgressForm(placements.Count))
            {
                progressForm.Show();
                progressForm.BringToFront();
                Application.DoEvents();
                outputPaths = SignAll(
                    placements,
                    (currentIndex, totalCount, filePath) =>
                    {
                        progressForm.UpdateProgress(currentIndex, totalCount, filePath);
                        Application.DoEvents();
                    });
            }

            AppLog.Write("Firma completada correctamente.");
            var successMessage = outputPaths.Count == 1
                ? "PDF firmado guardado en:" + Environment.NewLine + outputPaths[0]
                : string.Format(
                    "Se han guardado {0} PDFs firmados en la carpeta:{1}{2}",
                    outputPaths.Count,
                    Environment.NewLine,
                    Path.GetDirectoryName(outputPaths[0]));

            if (fileResolution.SkippedExistingCount > 0)
            {
                successMessage += string.Format(
                    "{0}{0}Se han omitido {1} PDF(s) porque ya existia su copia _f y has elegido conservarla.",
                    Environment.NewLine,
                    fileResolution.SkippedExistingCount);
            }

            MessageBox.Show(successMessage, "Firma automatica", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public void Dispose()
        {
            if (signingCertificate != null)
            {
                signingCertificate.Dispose();
            }
        }

        private List<string> SignAll(IEnumerable<SignaturePlacement> placements, Action<int, int, string> progressCallback)
        {
            if (signingCertificate == null)
            {
                throw new InvalidOperationException("No hay certificado de firma cargado.");
            }

            if (!signingCertificate.HasPrivateKey)
            {
                throw new InvalidOperationException("El certificado seleccionado no tiene clave privada.");
            }

            var chain = BuildChain(signingCertificate);
            var outputPaths = new List<string>();
            var placementList = placements.ToList();
            for (var i = 0; i < placementList.Count; i++)
            {
                var placement = placementList[i];
                if (progressCallback != null)
                {
                    progressCallback(i + 1, placementList.Count, placement.SourcePath);
                }

                outputPaths.Add(SignSinglePdf(placement, chain, signingCertificate));
            }

            return outputPaths;
        }

        private OverwriteResolution ResolveFilesToSign(IEnumerable<string> files)
        {
            var fileList = files.ToList();
            var conflictingFiles = fileList
                .Where(file => File.Exists(BuildOutputPath(file)))
                .ToList();

            if (conflictingFiles.Count == 0)
            {
                return new OverwriteResolution(fileList, 0);
            }

            var listedFiles = conflictingFiles
                .Take(8)
                .Select(file => " - " + Path.GetFileName(BuildOutputPath(file)))
                .ToList();

            var moreFilesLine = conflictingFiles.Count > listedFiles.Count
                ? Environment.NewLine + string.Format("Y {0} archivo(s) mas...", conflictingFiles.Count - listedFiles.Count)
                : string.Empty;

            var message = string.Format(
                "Ya existen {0} archivo(s) firmados con sufijo _f.{1}{1}{2}{3}{1}{1}Si: sustituir esos archivos existentes.{1}No: conservarlos y omitir esos PDFs.{1}Cancelar: detener el proceso.",
                conflictingFiles.Count,
                Environment.NewLine,
                string.Join(Environment.NewLine, listedFiles),
                moreFilesLine);

            var result = MessageBox.Show(
                message,
                "Firma automatica",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);

            if (result == DialogResult.Cancel)
            {
                return null;
            }

            if (result == DialogResult.Yes)
            {
                AppLog.Write("Se sustituiran " + conflictingFiles.Count + " archivo(s) _f existentes.");
                return new OverwriteResolution(fileList, 0);
            }

            var conflictingSet = new HashSet<string>(
                conflictingFiles.Select(BuildOutputPath),
                StringComparer.OrdinalIgnoreCase);

            var filesToSign = fileList
                .Where(file => !conflictingSet.Contains(BuildOutputPath(file)))
                .ToList();

            AppLog.Write("Se omitiran " + conflictingFiles.Count + " archivo(s) por conflicto con _f existente.");
            return new OverwriteResolution(filesToSign, conflictingFiles.Count);
        }

        private static IList<Org.BouncyCastle.X509.X509Certificate> BuildChain(X509Certificate2 certificate)
        {
            var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.Build(certificate);

            var certificates = chain.ChainElements
                .Cast<X509ChainElement>()
                .Select(element => DotNetUtilities.FromX509Certificate(element.Certificate))
                .ToList();

            if (certificates.Count == 0)
            {
                certificates.Add(DotNetUtilities.FromX509Certificate(certificate));
            }

            return certificates;
        }

        private string SignSinglePdf(SignaturePlacement placement, IList<Org.BouncyCastle.X509.X509Certificate> chain, X509Certificate2 certificate)
        {
            var outputPath = BuildOutputPath(placement.SourcePath);
            AppLog.Write("Firmando PDF de salida: " + outputPath);
            using (var reader = new PdfReader(placement.SourcePath))
            using (var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            {
                var stamper = PdfStamper.CreateSignature(reader, output, '\0', null, true);
                var appearance = stamper.SignatureAppearance;
                if (!string.IsNullOrWhiteSpace(placement.ExistingFieldName))
                {
                    AppLog.Write("Usando campo de firma existente: " + placement.ExistingFieldName);
                    appearance.SetVisibleSignature(placement.ExistingFieldName);
                }
                else
                {
                    appearance.SetVisibleSignature(
                        new Rectangle(placement.Left, placement.Bottom, placement.Right, placement.Top),
                        placement.PageNumber,
                        null);
                }

                appearance.SignDate = placement.SignedAt;
                appearance.Reason = "Firma electronica";
                appearance.Location = string.Empty;
                appearance.Acro6Layers = true;

                var appearanceData = new SignatureAppearanceData(
                    ResolveCertificateName(certificate),
                    certificate.Subject,
                    appearance.Reason,
                    placement.SignedAt,
                    signatureProfile == null ? null : signatureProfile.GraphicBytes);

                appearance.SignatureRenderingMode = PdfSignatureAppearance.RenderingMode.GRAPHIC;
                appearance.SignatureGraphic = SignatureAppearanceRenderer.CreatePdfGraphic(
                    placement.Right - placement.Left,
                    placement.Top - placement.Bottom,
                    appearanceData);

                var signature = CreateSignature(certificate);
                MakeSignature.SignDetached(
                    appearance,
                    signature,
                    chain,
                    null,
                    null,
                    null,
                    0,
                    CryptoStandard.CMS);
            }

            return outputPath;
        }

        private static IExternalSignature CreateSignature(X509Certificate2 certificate)
        {
            var rsa = certificate.PrivateKey as RSACryptoServiceProvider;
            if (rsa != null)
            {
                return new AsymmetricAlgorithmSignature(CreateSha256CompatibleRsa(rsa), DigestAlgorithms.SHA256);
            }

            var dsa = certificate.PrivateKey as DSACryptoServiceProvider;
            if (dsa != null)
            {
                return new AsymmetricAlgorithmSignature(dsa);
            }

            return new X509Certificate2Signature(certificate, DigestAlgorithms.SHA256);
        }

        private static SignatureAppearanceProfile BuildSignatureProfile(X509Certificate2 certificate)
        {
            var certificateGraphicPath = certificate == null
                ? null
                : UserPreferences.GetCertificateSignatureGraphicPath(certificate.Thumbprint);

            return new SignatureAppearanceProfile(
                ResolveCertificateName(certificate),
                certificate.Subject,
                "Firma electronica",
                AdobeSignatureGraphicLoader.TryLoadGraphicBytes(certificateGraphicPath));
        }

        private static RSACryptoServiceProvider CreateSha256CompatibleRsa(RSACryptoServiceProvider rsa)
        {
            var providerName = rsa.CspKeyContainerInfo.ProviderName;
            if (!string.Equals(providerName, "Microsoft Strong Cryptographic Provider", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(providerName, "Microsoft Enhanced Cryptographic Provider v1.0", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(providerName, "Microsoft Base Cryptographic Provider v1.0", StringComparison.OrdinalIgnoreCase))
            {
                return rsa;
            }

            var flags = CspProviderFlags.UseExistingKey;
            if (rsa.CspKeyContainerInfo.MachineKeyStore)
            {
                flags |= CspProviderFlags.UseMachineKeyStore;
            }

            if (rsa.CspKeyContainerInfo.Protected)
            {
                flags |= CspProviderFlags.UseUserProtectedKey;
            }

            var parameters = new CspParameters(24, "Microsoft Enhanced RSA and AES Cryptographic Provider", rsa.CspKeyContainerInfo.KeyContainerName);
            parameters.KeyNumber = (int)rsa.CspKeyContainerInfo.KeyNumber;
            parameters.Flags = flags;
            return new RSACryptoServiceProvider(parameters);
        }

        private static string ResolveCertificateName(X509Certificate2 certificate)
        {
            var name = certificate.GetNameInfo(X509NameType.SimpleName, false);
            return string.IsNullOrWhiteSpace(name) ? certificate.Subject : name;
        }

        private static string BuildOutputPath(string sourcePath)
        {
            var directory = Path.GetDirectoryName(sourcePath);
            var fileName = Path.GetFileNameWithoutExtension(sourcePath);
            var extension = Path.GetExtension(sourcePath);
            return Path.Combine(directory ?? string.Empty, fileName + "_f" + extension);
        }

        private sealed class OverwriteResolution
        {
            public OverwriteResolution(List<string> filesToSign, int skippedExistingCount)
            {
                FilesToSign = filesToSign;
                SkippedExistingCount = skippedExistingCount;
            }

            public List<string> FilesToSign { get; private set; }

            public int SkippedExistingCount { get; private set; }
        }
    }
}

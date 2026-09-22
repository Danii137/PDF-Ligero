using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.security;
using Org.BouncyCastle.Security;
using BcCertificate = Org.BouncyCastle.X509.X509Certificate;

namespace FirmaAutomatica
{
    /// <summary>En que estado esta una firma.</summary>
    internal enum PdfSignatureStatus
    {
        /// <summary>Firma valida y el documento no se ha tocado desde ella.</summary>
        Valida,

        /// <summary>La firma cuadra, pero hay avisos que conviene leer.</summary>
        ConAvisos,

        /// <summary>El documento ha cambiado despues de firmarse.</summary>
        Invalida,

        /// <summary>No se ha podido comprobar.</summary>
        Desconocida
    }

    /// <summary>Lo que se sabe de una firma del documento.</summary>
    internal sealed class PdfSignatureInfo
    {
        private readonly List<string> warnings = new List<string>();

        public PdfSignatureInfo(string fieldName)
        {
            FieldName = fieldName ?? string.Empty;
            SignerName = string.Empty;
            IssuerName = string.Empty;
            Reason = string.Empty;
            Location = string.Empty;
            Status = PdfSignatureStatus.Desconocida;
        }

        /// <summary>Nombre del campo de firma dentro del PDF.</summary>
        public string FieldName { get; private set; }

        /// <summary>Quien firma, sacado del certificado.</summary>
        public string SignerName { get; internal set; }

        /// <summary>Quien emitio el certificado.</summary>
        public string IssuerName { get; internal set; }

        public DateTime? SignedAt { get; internal set; }

        public DateTime? TimeStampedAt { get; internal set; }

        public string Reason { get; internal set; }

        public string Location { get; internal set; }

        /// <summary>La firma cubre el documento entero, no una revision.</summary>
        public bool CoversWholeDocument { get; internal set; }

        /// <summary>Revision del documento a la que corresponde.</summary>
        public int Revision { get; internal set; }

        public int TotalRevisions { get; internal set; }

        public PdfSignatureStatus Status { get; internal set; }

        /// <summary>Avisos en castellano, para leerlos tal cual.</summary>
        public IList<string> Warnings
        {
            get { return warnings; }
        }

        public string StatusText
        {
            get
            {
                switch (Status)
                {
                    case PdfSignatureStatus.Valida:
                        return "Firma válida";

                    case PdfSignatureStatus.ConAvisos:
                        return "Firma válida, con avisos";

                    case PdfSignatureStatus.Invalida:
                        return "Firma no válida";

                    default:
                        return "No se ha podido comprobar";
                }
            }
        }
    }

    /// <summary>Resultado de mirar las firmas de un documento.</summary>
    internal sealed class PdfSignatureReport
    {
        private readonly List<PdfSignatureInfo> signatures =
            new List<PdfSignatureInfo>();

        public IList<PdfSignatureInfo> Signatures
        {
            get { return signatures; }
        }

        public bool HasSignatures
        {
            get { return signatures.Count > 0; }
        }

        /// <summary>Estado del documento en conjunto: manda el peor.</summary>
        public PdfSignatureStatus OverallStatus
        {
            get
            {
                var peor = PdfSignatureStatus.Valida;
                foreach (var firma in signatures)
                {
                    if (firma.Status == PdfSignatureStatus.Invalida)
                    {
                        return PdfSignatureStatus.Invalida;
                    }

                    if (firma.Status == PdfSignatureStatus.Desconocida)
                    {
                        peor = PdfSignatureStatus.Desconocida;
                    }
                    else if (firma.Status == PdfSignatureStatus.ConAvisos &&
                        peor == PdfSignatureStatus.Valida)
                    {
                        peor = PdfSignatureStatus.ConAvisos;
                    }
                }

                return peor;
            }
        }

        /// <summary>Una linea para la barra de estado.</summary>
        public string Summary
        {
            get
            {
                if (!HasSignatures)
                {
                    return "Este PDF no está firmado.";
                }

                var cuantas = signatures.Count == 1
                    ? "Firmado por "
                    : signatures.Count.ToString(CultureInfo.CurrentCulture) +
                        " firmas · ";
                var quien = signatures.Count == 1
                    ? signatures[0].SignerName
                    : string.Empty;

                switch (OverallStatus)
                {
                    case PdfSignatureStatus.Valida:
                        return cuantas + quien +
                            (signatures.Count == 1 ? " · válida" : "válidas");

                    case PdfSignatureStatus.ConAvisos:
                        return cuantas + quien + " · con avisos";

                    case PdfSignatureStatus.Invalida:
                        return "Atención: el documento ha cambiado después " +
                            "de firmarse.";

                    default:
                        return cuantas + quien +
                            " · no se ha podido comprobar";
                }
            }
        }
    }

    /// <summary>
    /// Mira las firmas que ya trae un PDF y dice si valen.
    ///
    /// El programa sabia firmar, pero al abrir un PDF firmado por otro no
    /// decia nada: ni de quien era la firma ni si seguia valiendo. Aqui se
    /// comprueban las tres cosas que importan: que el documento no haya
    /// cambiado desde la firma, que la firma cubra el documento entero y que
    /// el certificado sea de una autoridad reconocida por el equipo.
    ///
    /// No es una validacion legal completa: no se consultan listas de
    /// revocacion ni OCSP, y se dice claramente en los avisos.
    /// </summary>
    internal static class PdfSignatureInspectionService
    {
        public static PdfSignatureReport Inspect(string pdfPath)
        {
            var report = new PdfSignatureReport();
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
            {
                return report;
            }

            PdfReader reader = null;
            try
            {
                reader = new PdfReader(pdfPath, (byte[])null, true);
                var campos = reader.AcroFields;
                if (campos == null)
                {
                    return report;
                }

                var nombres = campos.GetSignatureNames();
                if (nombres == null || nombres.Count == 0)
                {
                    return report;
                }

                foreach (var nombre in nombres)
                {
                    report.Signatures.Add(InspectOne(campos, nombre));
                }
            }
            catch (Exception ex)
            {
                AppLog.Write("No se pudieron leer las firmas: " + ex);
            }
            finally
            {
                if (reader != null)
                {
                    try
                    {
                        reader.Close();
                    }
                    catch (Exception)
                    {
                    }
                }
            }

            return report;
        }

        private static PdfSignatureInfo InspectOne(
            AcroFields campos,
            string nombre)
        {
            var info = new PdfSignatureInfo(nombre);
            try
            {
                info.CoversWholeDocument =
                    campos.SignatureCoversWholeDocument(nombre);
                info.Revision = campos.GetRevision(nombre);
                info.TotalRevisions = campos.TotalRevisions;
            }
            catch (Exception)
            {
            }

            PdfPKCS7 firma = null;
            try
            {
                firma = campos.VerifySignature(nombre);
            }
            catch (Exception ex)
            {
                AppLog.Write(
                    "No se pudo verificar la firma " + nombre + ": " + ex);
            }

            if (firma == null)
            {
                info.Status = PdfSignatureStatus.Desconocida;
                info.Warnings.Add(
                    "El formato de esta firma no se ha podido interpretar.");
                return info;
            }

            var certificado = firma.SigningCertificate;
            info.SignerName = DescribeSubject(certificado, firma);
            info.IssuerName = DescribeIssuer(certificado);
            info.Reason = firma.Reason ?? string.Empty;
            info.Location = firma.Location ?? string.Empty;

            try
            {
                info.SignedAt = firma.SignDate;
            }
            catch (Exception)
            {
            }

            try
            {
                if (firma.TimeStampDate != DateTime.MaxValue)
                {
                    info.TimeStampedAt = firma.TimeStampDate;
                }
            }
            catch (Exception)
            {
            }

            var integra = false;
            try
            {
                integra = firma.Verify();
            }
            catch (Exception ex)
            {
                AppLog.Write(
                    "No se pudo comprobar la integridad de " + nombre +
                    ": " + ex);
                info.Status = PdfSignatureStatus.Desconocida;
                info.Warnings.Add(
                    "No se ha podido comprobar si el documento ha cambiado.");
                return info;
            }

            if (!integra)
            {
                info.Status = PdfSignatureStatus.Invalida;
                info.Warnings.Add(
                    "El contenido firmado no coincide: el documento se ha " +
                    "modificado después de firmarse.");
                return info;
            }

            var avisos = false;
            if (!info.CoversWholeDocument)
            {
                avisos = true;
                info.Warnings.Add(
                    "La firma cubre solo una revisión del documento; " +
                    "después se le añadieron cambios.");
            }

            avisos |= CheckCertificateDates(info, certificado);
            avisos |= CheckTrust(info, firma);

            info.Warnings.Add(
                "No se han consultado listas de revocación: no se puede " +
                "saber aquí si el certificado fue anulado.");

            info.Status = avisos
                ? PdfSignatureStatus.ConAvisos
                : PdfSignatureStatus.Valida;
            return info;
        }

        /// <summary>
        /// Un certificado caducado no invalida una firma anterior a su
        /// caducidad, pero hay que decirlo.
        /// </summary>
        private static bool CheckCertificateDates(
            PdfSignatureInfo info,
            BcCertificate certificado)
        {
            if (certificado == null)
            {
                info.Warnings.Add(
                    "La firma no trae el certificado de quien firma.");
                return true;
            }

            try
            {
                var desde = certificado.NotBefore;
                var hasta = certificado.NotAfter;
                var ahora = DateTime.Now;
                if (ahora > hasta)
                {
                    var cuando = info.SignedAt;
                    if (cuando.HasValue &&
                        cuando.Value <= hasta &&
                        cuando.Value >= desde)
                    {
                        info.Warnings.Add(
                            "El certificado caducó el " +
                            hasta.ToString(
                                "d 'de' MMMM 'de' yyyy",
                                CultureInfo.CurrentCulture) +
                            ", pero ya estaba caducado después de firmar, " +
                            "no al firmar.");
                    }
                    else
                    {
                        info.Warnings.Add(
                            "El certificado estaba caducado: venció el " +
                            hasta.ToString(
                                "d 'de' MMMM 'de' yyyy",
                                CultureInfo.CurrentCulture) + ".");
                    }

                    return true;
                }
            }
            catch (Exception)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Comprueba la cadena contra los certificados raiz en los que confia
        /// este equipo. Sin esto, cualquiera puede firmar con un certificado
        /// que se haya hecho el mismo.
        ///
        /// Se usa X509Chain, la cadena de Windows, y no el
        /// CertificateVerification de iTextSharp: ese recorre las extensiones
        /// criticas del certificado sin comprobar si la lista existe, y con
        /// un certificado que no tenga ninguna lanza una
        /// NullReferenceException. Se descubrio ejecutando el programa, y
        /// mientras duro, la confianza no se llegaba a mirar nunca: todas las
        /// firmas salian con un "no se ha podido comprobar".
        ///
        /// Ademas X509Chain es la que usa el sistema, asi que lo que aqui sale
        /// de confianza es lo mismo que considera de confianza el equipo.
        /// </summary>
        private static bool CheckTrust(
            PdfSignatureInfo info,
            PdfPKCS7 firma)
        {
            try
            {
                var cadena = firma.SignCertificateChain;
                if (cadena == null || cadena.Length == 0)
                {
                    info.Warnings.Add(
                        "La firma no trae la cadena de certificados.");
                    return true;
                }

                using (var chain = new X509Chain())
                {
                    // Las revocaciones no se consultan: se avisa aparte de
                    // que no se han mirado, y pedirlas aqui bloquearia la
                    // apertura del documento contra internet.
                    chain.ChainPolicy.RevocationMode =
                        X509RevocationMode.NoCheck;
                    chain.ChainPolicy.VerificationTime =
                        info.SignedAt.HasValue
                            ? info.SignedAt.Value
                            : DateTime.Now;

                    // Los intermedios que viaja dentro del PDF se le dan a la
                    // cadena: sin ellos, un certificado perfectamente valido
                    // saldria como cadena incompleta.
                    for (var i = 1; i < cadena.Length; i++)
                    {
                        chain.ChainPolicy.ExtraStore.Add(
                            ToDotNet(cadena[i]));
                    }

                    var valida = chain.Build(ToDotNet(cadena[0]));
                    if (valida)
                    {
                        return false;
                    }

                    var motivo = DescribeChainFailure(chain);
                    info.Warnings.Add(motivo);
                    return true;
                }
            }
            catch (Exception ex)
            {
                AppLog.Write("No se pudo validar la cadena: " + ex);
                info.Warnings.Add(
                    "No se ha podido comprobar quién emitió el certificado.");
                return true;
            }
        }

        /// <summary>
        /// Por que no vale la cadena, en castellano. La caducidad se informa
        /// aparte, asi que aqui se resume lo que queda.
        /// </summary>
        private static string DescribeChainFailure(X509Chain chain)
        {
            var raizDesconocida = false;
            var cadenaIncompleta = false;
            var firmaMala = false;

            foreach (var elemento in chain.ChainStatus)
            {
                switch (elemento.Status)
                {
                    case X509ChainStatusFlags.UntrustedRoot:
                        raizDesconocida = true;
                        break;

                    case X509ChainStatusFlags.PartialChain:
                        cadenaIncompleta = true;
                        break;

                    case X509ChainStatusFlags.NotSignatureValid:
                        firmaMala = true;
                        break;
                }
            }

            if (firmaMala)
            {
                return "La cadena del certificado no cuadra: alguno de sus " +
                    "eslabones no está bien firmado.";
            }

            if (raizDesconocida)
            {
                return "El certificado no lo emite ninguna autoridad " +
                    "reconocida por este equipo.";
            }

            if (cadenaIncompleta)
            {
                return "Falta parte de la cadena del certificado, así que no " +
                    "se puede llegar hasta una autoridad reconocida.";
            }

            return "El certificado no supera la comprobación de este equipo.";
        }

        private static X509Certificate2 ToDotNet(BcCertificate certificado)
        {
            return new X509Certificate2(certificado.GetEncoded());
        }

        private static string DescribeSubject(
            BcCertificate certificado,
            PdfPKCS7 firma)
        {
            var nombre = ExtractCommonName(
                certificado == null
                    ? null
                    : certificado.SubjectDN.ToString());
            if (!string.IsNullOrWhiteSpace(nombre))
            {
                return nombre;
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(firma.SignName))
                {
                    return firma.SignName;
                }
            }
            catch (Exception)
            {
            }

            return "Firmante desconocido";
        }

        private static string DescribeIssuer(BcCertificate certificado)
        {
            var nombre = ExtractCommonName(
                certificado == null
                    ? null
                    : certificado.IssuerDN.ToString());
            return string.IsNullOrWhiteSpace(nombre)
                ? string.Empty
                : nombre;
        }

        /// <summary>
        /// Saca el CN de un nombre distinguido. El resto del DN —el pais, la
        /// organizacion, el numero de serie— no aporta nada al leerlo.
        /// </summary>
        private static string ExtractCommonName(string distinguishedName)
        {
            if (string.IsNullOrWhiteSpace(distinguishedName))
            {
                return string.Empty;
            }

            foreach (var trozo in distinguishedName.Split(','))
            {
                var limpio = trozo.Trim();
                if (limpio.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
                {
                    return limpio.Substring(3).Trim();
                }
            }

            return distinguishedName.Trim();
        }
    }
}

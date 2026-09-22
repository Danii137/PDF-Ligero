using System;
using System.Security.Cryptography;
using iTextSharp.text.pdf.security;

namespace FirmaAutomatica
{
    /// <summary>
    /// Firma con una clave RSA de las que devuelve Windows hoy, este en un
    /// proveedor clasico (CSP) o en CNG.
    ///
    /// Hace falta porque el AsymmetricAlgorithmSignature de iTextSharp 5 solo
    /// admite RSACryptoServiceProvider, que es justo la clase que NO se puede
    /// obtener de un certificado cuya clave esta en CNG: los que instalan hoy
    /// la FNMT, el DNIe y casi cualquier token hardware.
    /// </summary>
    internal sealed class PdfModernRsaSignature : IExternalSignature
    {
        private readonly RSA rsa;

        public PdfModernRsaSignature(RSA rsa)
        {
            if (rsa == null)
            {
                throw new ArgumentNullException("rsa");
            }

            this.rsa = rsa;
        }

        public string GetHashAlgorithm()
        {
            return DigestAlgorithms.SHA256;
        }

        public string GetEncryptionAlgorithm()
        {
            return "RSA";
        }

        public byte[] Sign(byte[] message)
        {
            // iText entrega el mensaje SIN resumir: el resumen se hace aqui y
            // se firma, igual que hace su propio PrivateKeySignature. Hacer
            // el hash dos veces produciria una firma que no valida.
            return rsa.SignData(
                message,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
        }
    }
}

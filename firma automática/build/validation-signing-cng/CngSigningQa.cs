using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FirmaAutomatica;
using iTextSharp.text;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.security;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using BcCertificate = Org.BouncyCastle.X509.X509Certificate;
using IoPath = System.IO.Path;

namespace CngSigningQa
{
    /// <summary>
    /// Firmar con la clave privada tal como la entrega Windows hoy.
    ///
    /// El camino de siempre usa certificate.PrivateKey, que con una clave en
    /// CNG —FNMT actual, DNIe, tokens— no devuelve null: lanza "Se ha
    /// especificado un tipo de proveedor no valido". Esta prueba comprueba
    /// que el camino nuevo produce una firma que valida de verdad, no solo
    /// que no lance.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                var directorio = args.Length > 0
                    ? IoPath.GetFullPath(args[0])
                    : IoPath.Combine(
                        IoPath.GetTempPath(),
                        "qa-firma-cng");
                if (Directory.Exists(directorio))
                {
                    Directory.Delete(directorio, true);
                }

                Directory.CreateDirectory(directorio);

                var fallos = 0;
                fallos += ComprobarFirmaConClaveDeWindows(directorio);
                fallos += ComprobarSinDobleResumen(directorio);

                if (fallos == 0)
                {
                    Console.WriteLine(
                        "PASS: la firma con clave moderna es válida.");
                    return 0;
                }

                Console.Error.WriteLine(
                    "FAIL: " +
                    fallos.ToString(CultureInfo.InvariantCulture) +
                    " comprobaciones.");
                return 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: " + ex);
                return 3;
            }
        }

        private static int ComprobarFirmaConClaveDeWindows(string directorio)
        {
            var pfx = IoPath.Combine(directorio, "prueba.pfx");
            var cadena = CrearPfx(pfx, "Estudio CNG de prueba", "1234");

            // Se carga como lo hace el programa con un certificado del
            // almacen: es Windows quien decide el proveedor de la clave.
            using (var certificado = new X509Certificate2(
                pfx,
                "1234",
                X509KeyStorageFlags.Exportable))
            {
                if (!certificado.HasPrivateKey)
                {
                    Console.Error.WriteLine(
                        "ERROR: el certificado de prueba no trae clave.");
                    return 1;
                }

                var rsa = certificado.GetRSAPrivateKey();
                if (rsa == null)
                {
                    Console.Error.WriteLine(
                        "ERROR: Windows no devuelve la clave privada.");
                    return 1;
                }

                Console.WriteLine(
                    "Clave privada de Windows: " + rsa.GetType().Name);

                // Se deja constancia de que hace el camino de siempre con
                // este certificado. Con una clave EXPORTABLE como la de esta
                // prueba, .NET sabe fabricar un RSACryptoServiceProvider a
                // partir de la clave CNG y no falla. El fallo de produccion
                // —"Se ha especificado un tipo de proveedor no valido"—
                // aparece con claves NO exportables: certificados de la FNMT
                // instalados con proteccion fuerte, DNIe y tokens hardware,
                // que no se pueden fabricar aqui sin ese hardware.
                //
                // Esta prueba no reproduce ese lanzamiento, y no pretende
                // hacerlo: comprueba que el camino de reserva produce una
                // firma valida. Lo que evita la regresion es que el camino
                // clasico sigue siendo el primero y sin tocar.
                try
                {
                    var antiguo = certificado.PrivateKey
                        as RSACryptoServiceProvider;
                    Console.WriteLine(
                        "Camino clasico (certificate.PrivateKey): " +
                        (antiguo == null
                            ? "devuelve algo que no sirve"
                            : "funciona con esta clave exportable"));
                }
                catch (CryptographicException ex)
                {
                    Console.WriteLine(
                        "Camino clasico (certificate.PrivateKey): LANZA \"" +
                        ex.Message.Trim() + "\"");
                }

                var origen = IoPath.Combine(directorio, "sin firmar.pdf");
                CrearPdf(origen);
                var firmado = IoPath.Combine(directorio, "firmado cng.pdf");
                Firmar(
                    origen,
                    firmado,
                    cadena,
                    new PdfModernRsaSignature(rsa));

                var informe =
                    PdfSignatureInspectionService.Inspect(firmado);
                if (informe.Signatures.Count != 1)
                {
                    Console.Error.WriteLine(
                        "FAIL: no se lee la firma.");
                    return 1;
                }

                var firma = informe.Signatures[0];
                Console.WriteLine(
                    "Firma con clave moderna: " + firma.StatusText +
                    " · " + firma.SignerName);

                // Lo que importa: el contenido firmado cuadra. Si el resumen
                // se hiciera mal, esto saldria como "Firma no valida".
                if (firma.Status == PdfSignatureStatus.Invalida)
                {
                    Console.Error.WriteLine(
                        "FAIL: la firma no valida: el resumen esta mal.");
                    return 1;
                }

                if (firma.Status == PdfSignatureStatus.Desconocida)
                {
                    Console.Error.WriteLine(
                        "FAIL: la firma no se ha podido comprobar.");
                    return 1;
                }

                if (!firma.CoversWholeDocument)
                {
                    Console.Error.WriteLine(
                        "FAIL: la firma no cubre el documento entero.");
                    return 1;
                }
            }

            return 0;
        }

        private static int ComprobarSinDobleResumen(string directorio)
        {
            // El fallo tipico al escribir un IExternalSignature es resumir el
            // mensaje antes de firmarlo, cuando iText ya lo entrega en claro.
            // Aqui se comprueba contra la firma de referencia de la propia
            // libreria: las dos tienen que verificar con la misma clave.
            AsymmetricKeyParameter privadaBc;
            var cadena = CrearCertificado(
                "Comparacion de firmas",
                out privadaBc);

            var mensaje = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
            var referencia = new PrivateKeySignature(
                privadaBc,
                DigestAlgorithms.SHA256).Sign(mensaje);

            using (var rsa = ToRsa(privadaBc))
            {
                var propia = new PdfModernRsaSignature(rsa).Sign(mensaje);
                if (referencia.Length != propia.Length)
                {
                    Console.Error.WriteLine(
                        "FAIL: la firma propia mide " +
                        propia.Length.ToString(CultureInfo.InvariantCulture) +
                        " y la de iText " +
                        referencia.Length.ToString(
                            CultureInfo.InvariantCulture) + ".");
                    return 1;
                }

                for (var i = 0; i < referencia.Length; i++)
                {
                    if (referencia[i] != propia[i])
                    {
                        Console.Error.WriteLine(
                            "FAIL: la firma propia no coincide con la de " +
                            "iText: probablemente se resume dos veces.");
                        return 1;
                    }
                }
            }

            Console.WriteLine(
                "Byte a byte igual que la firma de referencia de iText.");
            return 0;
        }

        private static RSA ToRsa(AsymmetricKeyParameter clavePrivada)
        {
            var parametros = DotNetUtilities.ToRSAParameters(
                (RsaPrivateCrtKeyParameters)clavePrivada);
            var rsa = RSA.Create();
            rsa.ImportParameters(parametros);
            return rsa;
        }

        private static BcCertificate[] CrearCertificado(
            string nombre,
            out AsymmetricKeyParameter clavePrivada)
        {
            var azar = new SecureRandom();
            var generador = new RsaKeyPairGenerator();
            generador.Init(new KeyGenerationParameters(azar, 2048));
            var par = generador.GenerateKeyPair();
            clavePrivada = par.Private;

            var certificado = new X509V3CertificateGenerator();
            var sujeto = new X509Name("CN=" + nombre + ", C=ES");
            certificado.SetSerialNumber(BigInteger.ProbablePrime(120, azar));
            certificado.SetIssuerDN(sujeto);
            certificado.SetSubjectDN(sujeto);
            certificado.SetNotBefore(DateTime.UtcNow.AddDays(-1));
            certificado.SetNotAfter(DateTime.UtcNow.AddYears(2));
            certificado.SetPublicKey(par.Public);

            var firmante = new Asn1SignatureFactory(
                "SHA256WITHRSA",
                par.Private,
                azar);
            return new[] { certificado.Generate(firmante) };
        }

        private static BcCertificate[] CrearPfx(
            string path,
            string nombre,
            string contrasena)
        {
            AsymmetricKeyParameter privada;
            var cadena = CrearCertificado(nombre, out privada);

            var almacen = new Pkcs12StoreBuilder().Build();
            var entrada = new X509CertificateEntry(cadena[0]);
            almacen.SetCertificateEntry(nombre, entrada);
            almacen.SetKeyEntry(
                nombre,
                new AsymmetricKeyEntry(privada),
                new[] { entrada });

            using (var stream = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write))
            {
                almacen.Save(
                    stream,
                    contrasena.ToCharArray(),
                    new SecureRandom());
            }

            return cadena;
        }

        private static void CrearPdf(string path)
        {
            using (var document = new Document(PageSize.A4, 56F, 56F, 56F, 56F))
            {
                using (var stream = new FileStream(
                    path,
                    FileMode.Create,
                    FileAccess.Write))
                {
                    var writer = PdfWriter.GetInstance(document, stream);
                    document.Open();
                    document.Add(
                        new Paragraph(
                            "Certificado de final de obra",
                            FontFactory.GetFont(FontFactory.HELVETICA, 16F)));
                    document.Close();
                    writer.Close();
                }
            }
        }

        private static void Firmar(
            string origen,
            string destino,
            BcCertificate[] cadena,
            IExternalSignature firma)
        {
            using (var reader = new PdfReader(origen))
            {
                using (var salida = new FileStream(
                    destino,
                    FileMode.Create,
                    FileAccess.Write))
                {
                    var stamper = PdfStamper.CreateSignature(
                        reader,
                        salida,
                        '\0');
                    var apariencia = stamper.SignatureAppearance;
                    apariencia.Reason = "Conformidad";
                    apariencia.Location = "Toledo";
                    apariencia.SetVisibleSignature(
                        new Rectangle(60F, 700F, 260F, 760F),
                        1,
                        "FirmaCng");
                    MakeSignature.SignDetached(
                        apariencia,
                        firma,
                        cadena,
                        null,
                        null,
                        null,
                        0,
                        CryptoStandard.CMS);
                }
            }
        }
    }
}

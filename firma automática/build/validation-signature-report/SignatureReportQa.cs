using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
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
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using IoPath = System.IO.Path;

namespace SignatureReportQa
{
    /// <summary>
    /// Ver y validar las firmas que ya trae un PDF.
    ///
    /// Lo importante no es que lea el nombre del firmante, sino que cante
    /// cuando el documento ha cambiado despues de firmarse. Por eso se firma
    /// de verdad —con un certificado hecho aqui mismo— y luego se manipula el
    /// archivo.
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
                        "qa-firmas-pdf");
                if (Directory.Exists(directorio))
                {
                    Directory.Delete(directorio, true);
                }

                Directory.CreateDirectory(directorio);

                var sinFirmar = IoPath.Combine(directorio, "sin firmar.pdf");
                CrearFixture(sinFirmar);

                AsymmetricKeyParameter clavePrivada;
                var cadena = CrearCertificado(
                    "Estudio de prueba AGOIN",
                    out clavePrivada);

                var firmado = IoPath.Combine(directorio, "firmado.pdf");
                Firmar(sinFirmar, firmado, cadena, clavePrivada);

                var fallos = 0;
                fallos += ComprobarSinFirmar(sinFirmar);
                fallos += ComprobarFirmado(firmado);
                fallos += ComprobarManipulado(firmado, directorio);
                fallos += ComprobarAñadidoPosterior(firmado, directorio);

                if (fallos == 0)
                {
                    Console.WriteLine(
                        "PASS: las firmas se leen y se validan.");
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

        private static int ComprobarSinFirmar(string path)
        {
            var informe = PdfSignatureInspectionService.Inspect(path);
            if (informe.HasSignatures)
            {
                Console.Error.WriteLine(
                    "FAIL: se inventa firmas en un PDF sin firmar.");
                return 1;
            }

            Console.WriteLine("Sin firmar: " + informe.Summary);
            return 0;
        }

        private static int ComprobarFirmado(string path)
        {
            var informe = PdfSignatureInspectionService.Inspect(path);
            var fallos = 0;
            if (informe.Signatures.Count != 1)
            {
                Console.Error.WriteLine(
                    "FAIL: se esperaba una firma, hay " +
                    informe.Signatures.Count.ToString(
                        CultureInfo.InvariantCulture) + ".");
                return 1;
            }

            var firma = informe.Signatures[0];
            Console.WriteLine(
                "Firmado: \"" + firma.SignerName + "\" · " +
                firma.StatusText + " · " +
                firma.Warnings.Count.ToString(CultureInfo.InvariantCulture) +
                " avisos");

            if (firma.SignerName.IndexOf(
                    "Estudio de prueba AGOIN",
                    StringComparison.Ordinal) < 0)
            {
                Console.Error.WriteLine(
                    "FAIL: no ha leido quien firma.");
                fallos++;
            }

            if (!firma.CoversWholeDocument)
            {
                Console.Error.WriteLine(
                    "FAIL: deberia cubrir el documento entero.");
                fallos++;
            }

            // El certificado se lo ha hecho la propia prueba, asi que no lo
            // avala ninguna autoridad: tiene que salir con avisos, no como
            // valida sin mas. Que un PDF firmado con un certificado casero
            // saliera "firma valida" a secas seria el fallo peligroso.
            if (firma.Status != PdfSignatureStatus.ConAvisos)
            {
                Console.Error.WriteLine(
                    "FAIL: un certificado sin autoridad reconocida deberia " +
                    "salir con avisos, no como " + firma.StatusText + ".");
                fallos++;
            }

            if (!firma.SignedAt.HasValue)
            {
                Console.Error.WriteLine("FAIL: no ha leido la fecha.");
                fallos++;
            }

            // El aviso tiene que ser el de "no lo emite ninguna autoridad
            // reconocida", no el de "no se ha podido comprobar". Si la
            // comprobacion de la cadena revienta, el resultado final es el
            // mismo —con avisos— pero la confianza no se ha mirado siquiera.
            // Distinguirlos es lo unico que detecta ese fallo.
            var avisaDeAutoridad = false;
            var avisaDeQueNoPudo = false;
            foreach (var aviso in firma.Warnings)
            {
                Console.WriteLine("   aviso: " + aviso);
                if (aviso.IndexOf(
                        "autoridad reconocida",
                        StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    avisaDeAutoridad = true;
                }

                if (aviso.IndexOf(
                        "no se ha podido comprobar quién",
                        StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    avisaDeQueNoPudo = true;
                }
            }

            if (avisaDeQueNoPudo)
            {
                Console.Error.WriteLine(
                    "FAIL: la comprobación de la cadena ha reventado; la " +
                    "confianza no se está mirando.");
                fallos++;
            }
            else if (!avisaDeAutoridad)
            {
                Console.Error.WriteLine(
                    "FAIL: un certificado sin autoridad reconocida debería " +
                    "avisar de ello.");
                fallos++;
            }

            return fallos;
        }

        private static int ComprobarManipulado(
            string firmado,
            string directorio)
        {
            // Se cambia una letra del texto, dentro de lo que cubre la firma.
            var manipulado = IoPath.Combine(directorio, "manipulado.pdf");
            var bytes = File.ReadAllBytes(firmado);
            var marca = Encoding.ASCII.GetBytes("MEMORIA DE PRUEBA");
            var posicion = IndiceDe(bytes, marca);
            if (posicion < 0)
            {
                Console.Error.WriteLine(
                    "ERROR: el fixture no trae el texto sin comprimir; " +
                    "no se puede manipular.");
                return 1;
            }

            bytes[posicion] = (byte)'N';
            File.WriteAllBytes(manipulado, bytes);

            var informe = PdfSignatureInspectionService.Inspect(manipulado);
            if (informe.Signatures.Count != 1)
            {
                Console.Error.WriteLine(
                    "FAIL: no se lee la firma del documento manipulado.");
                return 1;
            }

            var firma = informe.Signatures[0];
            Console.WriteLine(
                "Manipulado: " + firma.StatusText);
            if (firma.Status != PdfSignatureStatus.Invalida)
            {
                Console.Error.WriteLine(
                    "FAIL: no detecta que el documento ha cambiado.");
                return 1;
            }

            return 0;
        }

        private static int ComprobarAñadidoPosterior(
            string firmado,
            string directorio)
        {
            // Añadir algo en una revision incremental no rompe la firma, pero
            // deja de cubrir el documento entero y hay que decirlo.
            var ampliado = IoPath.Combine(directorio, "con añadido.pdf");
            using (var reader = new PdfReader(firmado))
            {
                using (var salida = new FileStream(
                    ampliado,
                    FileMode.Create,
                    FileAccess.Write))
                {
                    var stamper = new PdfStamper(reader, salida, '\0', true);
                    var content = stamper.GetOverContent(1);
                    var fuente = BaseFont.CreateFont(
                        BaseFont.HELVETICA,
                        BaseFont.CP1252,
                        BaseFont.NOT_EMBEDDED);
                    content.BeginText();
                    content.SetFontAndSize(fuente, 10F);
                    content.ShowTextAligned(
                        PdfContentByte.ALIGN_LEFT,
                        "añadido despues de firmar",
                        60F,
                        60F,
                        0F);
                    content.EndText();
                    stamper.Close();
                }
            }

            var informe = PdfSignatureInspectionService.Inspect(ampliado);
            if (informe.Signatures.Count != 1)
            {
                Console.Error.WriteLine(
                    "FAIL: no se lee la firma del documento ampliado.");
                return 1;
            }

            var firma = informe.Signatures[0];
            Console.WriteLine(
                "Con añadido posterior: " + firma.StatusText +
                ", cubre todo = " +
                (firma.CoversWholeDocument ? "si" : "no"));

            if (firma.CoversWholeDocument)
            {
                Console.Error.WriteLine(
                    "FAIL: dice cubrir el documento entero tras el añadido.");
                return 1;
            }

            var avisa = false;
            foreach (var aviso in firma.Warnings)
            {
                if (aviso.IndexOf(
                        "revisión",
                        StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    avisa = true;
                }
            }

            if (!avisa)
            {
                Console.Error.WriteLine(
                    "FAIL: no avisa de que la firma cubre solo una revision.");
                return 1;
            }

            return 0;
        }

        private static int IndiceDe(byte[] donde, byte[] que)
        {
            for (var i = 0; i <= donde.Length - que.Length; i++)
            {
                var coincide = true;
                for (var j = 0; j < que.Length; j++)
                {
                    if (donde[i + j] != que[j])
                    {
                        coincide = false;
                        break;
                    }
                }

                if (coincide)
                {
                    return i;
                }
            }

            return -1;
        }

        private static void CrearFixture(string path)
        {
            using (var document = new Document(PageSize.A4, 56F, 56F, 56F, 56F))
            {
                using (var stream = new FileStream(
                    path,
                    FileMode.Create,
                    FileAccess.Write))
                {
                    var writer = PdfWriter.GetInstance(document, stream);
                    // Sin comprimir, para poder cambiar una letra del texto
                    // con el archivo en la mano.
                    writer.CompressionLevel = 0;
                    writer.SetFullCompression();
                    document.Open();
                    document.Add(
                        new Paragraph(
                            "MEMORIA DE PRUEBA",
                            FontFactory.GetFont(FontFactory.HELVETICA, 16F)));
                    document.Close();
                    writer.Close();
                }
            }
        }

        private static X509Certificate[] CrearCertificado(
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
            certificado.SetSerialNumber(
                BigInteger.ProbablePrime(120, azar));
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

        private static void Firmar(
            string origen,
            string destino,
            X509Certificate[] cadena,
            AsymmetricKeyParameter clavePrivada)
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
                    apariencia.SignatureCreator = "PDF Ligero QA";
                    apariencia.SetVisibleSignature(
                        new Rectangle(60F, 700F, 260F, 760F),
                        1,
                        "FirmaPrueba");

                    IExternalSignature firma = new PrivateKeySignature(
                        clavePrivada,
                        DigestAlgorithms.SHA256);
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

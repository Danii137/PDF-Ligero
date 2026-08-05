using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using iTextSharp.text;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.security;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

namespace FirmaAutomatica
{
    /// <summary>
    /// Independent Phase 5 fixture. It deliberately does not reference the
    /// bookmark editor implementation, so it can be compiled before that
    /// implementation exists and reused as the immutable regression input.
    /// </summary>
    internal static class BookmarkFixtureAudit
    {
        private static readonly List<string> Results = new List<string>();
        private static readonly List<string> Failures = new List<string>();

        private static int Main(string[] args)
        {
            var runDirectory = args != null && args.Length > 0
                ? Path.GetFullPath(args[0])
                : Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "run");
            Directory.CreateDirectory(runDirectory);

            var fixturePath = Path.Combine(
                runDirectory,
                "bookmark-advanced-fixture.pdf");
            var signedFixturePath = Path.Combine(
                runDirectory,
                "bookmark-advanced-signed-fixture.pdf");
            var reportPath = Path.Combine(
                runDirectory,
                "qa-report.txt");

            TryDeleteFile(fixturePath);
            TryDeleteFile(signedFixturePath);
            try
            {
                CreateBasePdf(fixturePath);
                AddAdvancedStructures(fixturePath);
                ValidateFixture(fixturePath);
                CreateSignedFixture(
                    fixturePath,
                    signedFixturePath);
                ValidateSignedFixture(signedFixturePath);
            }
            catch (Exception ex)
            {
                Failures.Add("FAIL GLOBAL: " + FormatException(ex));
            }

            var report = new List<string>
            {
                Failures.Count == 0
                    ? "PASS: fixture independiente de marcadores validado."
                    : "FAIL: fixture de marcadores incompleto.",
                "Fecha UTC: " +
                    DateTime.UtcNow.ToString(
                        "yyyy-MM-dd HH:mm:ss",
                        CultureInfo.InvariantCulture) +
                "Z",
                "Fixture: " + fixturePath,
                "Fixture firmada: " + signedFixturePath
            };
            if (File.Exists(fixturePath))
            {
                report.Add("SHA-256: " + ComputeSha256(fixturePath));
            }
            if (File.Exists(signedFixturePath))
            {
                report.Add(
                    "SHA-256 firmada: " +
                    ComputeSha256(signedFixturePath));
            }

            report.Add(string.Empty);
            report.AddRange(Results);
            if (Failures.Count > 0)
            {
                report.Add(string.Empty);
                report.Add("FALLOS");
                report.AddRange(Failures);
            }

            File.WriteAllLines(reportPath, report.ToArray());
            Console.WriteLine(Failures.Count == 0 ? "PASS" : "FAIL");
            Console.WriteLine("fixture=" + fixturePath);
            Console.WriteLine("report=" + reportPath);
            Console.WriteLine("failures=" + Failures.Count);
            return Failures.Count == 0 ? 0 : 1;
        }

        private static void CreateSignedFixture(
            string sourcePath,
            string outputPath)
        {
            var random = new SecureRandom();
            var keyGenerator = new RsaKeyPairGenerator();
            keyGenerator.Init(
                new KeyGenerationParameters(random, 2048));
            var keyPair = keyGenerator.GenerateKeyPair();

            var certificateGenerator =
                new X509V3CertificateGenerator();
            var subject =
                new X509Name("CN=PDF Ligero Bookmark QA");
            certificateGenerator.SetSerialNumber(
                BigInteger.ProbablePrime(120, random));
            certificateGenerator.SetIssuerDN(subject);
            certificateGenerator.SetSubjectDN(subject);
            certificateGenerator.SetNotBefore(
                DateTime.UtcNow.AddDays(-1));
            certificateGenerator.SetNotAfter(
                DateTime.UtcNow.AddYears(2));
            certificateGenerator.SetPublicKey(keyPair.Public);
            var signatureFactory = new Asn1SignatureFactory(
                "SHA256WITHRSA",
                keyPair.Private,
                random);
            var certificate =
                certificateGenerator.Generate(signatureFactory);

            PdfReader reader = null;
            try
            {
                reader = new PdfReader(sourcePath);
                using (var output = new FileStream(
                    outputPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None))
                {
                    var stamper = PdfStamper.CreateSignature(
                        reader,
                        output,
                        '\0',
                        null,
                        true);
                    var appearance = stamper.SignatureAppearance;
                    appearance.Reason =
                        "Fixture de regresión de marcadores";
                    appearance.Location = "QA local";
                    appearance.SetVisibleSignature(
                        "signature.pending");
                    var signature = new PrivateKeySignature(
                        keyPair.Private,
                        DigestAlgorithms.SHA256);
                    MakeSignature.SignDetached(
                        appearance,
                        signature,
                        new[] { certificate },
                        null,
                        null,
                        null,
                        0,
                        CryptoStandard.CMS);
                    reader = null;
                }
            }
            finally
            {
                if (reader != null)
                {
                    reader.Close();
                }
            }
        }

        private static void CreateBasePdf(string path)
        {
            using (var stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            using (var document = new Document(PageSize.A4))
            {
                var writer = PdfWriter.GetInstance(document, stream);
                document.AddTitle("Fixture de marcadores avanzados");
                document.AddCreator("PDF Ligero QA");
                document.Open();

                for (var page = 1; page <= 4; page++)
                {
                    document.Add(
                        new Paragraph(
                            "FASE 5 / PAGINA " +
                            page.ToString(CultureInfo.InvariantCulture),
                            FontFactory.GetFont(
                                FontFactory.HELVETICA_BOLD,
                                21f)));
                    document.Add(
                        new Paragraph(
                            "Contenido vectorial estable para comprobar " +
                            "marcadores, enlaces y formularios."));

                    if (page == 1)
                    {
                        var text = new TextField(
                            writer,
                            new iTextSharp.text.Rectangle(
                                72f,
                                650f,
                                310f,
                                684f),
                            "project.name")
                        {
                            Text = "AGOIN",
                            FontSize = 11f
                        };
                        writer.AddAnnotation(text.GetTextField());
                    }
                    else if (page == 2)
                    {
                        var check = new RadioCheckField(
                            writer,
                            new iTextSharp.text.Rectangle(
                                72f,
                                650f,
                                94f,
                                672f),
                            "project.approved",
                            "Yes")
                        {
                            CheckType = RadioCheckField.TYPE_CHECK,
                            Checked = true
                        };
                        writer.AddAnnotation(check.CheckField);
                    }
                    else if (page == 4)
                    {
                        var signature =
                            PdfFormField.CreateSignature(writer);
                        signature.FieldName = "signature.pending";
                        signature.SetWidget(
                            new iTextSharp.text.Rectangle(
                                72f,
                                220f,
                                310f,
                                270f),
                            PdfAnnotation.HIGHLIGHT_INVERT);
                        signature.Flags = PdfAnnotation.FLAGS_PRINT;
                        writer.AddAnnotation(signature);
                    }

                    if (page < 4)
                    {
                        if (page == 3)
                        {
                            // A deliberately panoramic final sheet makes
                            // /FitV exercise horizontal-only navigation in
                            // the real viewer instead of being clamped by a
                            // narrow portrait page.
                            document.SetPageSize(
                                new iTextSharp.text.Rectangle(
                                    1200f,
                                    400f));
                        }
                        document.NewPage();
                    }
                }
            }
        }

        private static void AddAdvancedStructures(string path)
        {
            var temporaryPath = path + ".structures.tmp";
            TryDeleteFile(temporaryPath);
            PdfReader reader = null;
            try
            {
                reader = new PdfReader(path);
                using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None))
                using (var stamper = new PdfStamper(reader, stream))
                {
                    var writer = stamper.Writer;
                    var catalog = reader.Catalog;

                    var ocg = new PdfDictionary(PdfName.OCG);
                    ocg.Put(
                        PdfName.NAME,
                        new PdfString(
                            "Capa QA",
                            PdfObject.TEXT_UNICODE));
                    var ocgReference =
                        writer.AddToBody(ocg).IndirectReference;
                    AddOptionalContentProperties(
                        catalog,
                        ocgReference);

                    AddNamedDestination(
                        reader,
                        writer,
                        catalog);
                    AddOutlineTree(
                        reader,
                        writer,
                        catalog,
                        ocgReference);
                    AddPageLinks(reader, writer, stamper);

                    stamper.MarkUsed(catalog);
                }
            }
            finally
            {
                if (reader != null)
                {
                    reader.Close();
                }
            }

            File.Delete(path);
            File.Move(temporaryPath, path);
        }

        private static void AddOptionalContentProperties(
            PdfDictionary catalog,
            PdfIndirectReference ocgReference)
        {
            var ocgs = new PdfArray();
            ocgs.Add(ocgReference);
            var order = new PdfArray();
            order.Add(ocgReference);
            var defaultConfiguration = new PdfDictionary();
            defaultConfiguration.Put(PdfName.ORDER, order);
            defaultConfiguration.Put(PdfName.NAME, new PdfString("QA"));
            var properties = new PdfDictionary();
            properties.Put(PdfName.OCGS, ocgs);
            properties.Put(PdfName.D, defaultConfiguration);
            catalog.Put(PdfName.OCPROPERTIES, properties);
        }

        private static void AddNamedDestination(
            PdfReader reader,
            PdfWriter writer,
            PdfDictionary catalog)
        {
            var entries =
                new Dictionary<string, PdfObject>(
                    StringComparer.Ordinal)
                {
                    {
                        "NamedFitH",
                        CreateDestination(
                            reader.GetPageOrigRef(3),
                            PdfName.FITH,
                            new PdfNumber(700f))
                    }
                };
            var names = catalog.GetAsDict(PdfName.NAMES);
            if (names == null)
            {
                names = new PdfDictionary();
                catalog.Put(PdfName.NAMES, names);
            }

            names.Put(
                PdfName.DESTS,
                PdfNameTree.WriteTree(entries, writer));

            // PDF distinguishes a name object (/NamedFitH) from a text
            // string ((NamedFitH)). Keep two homonymous destinations with
            // different targets so a reader that normalizes both to plain
            // text is caught by the fixture.
            var legacyDestinations =
                catalog.GetAsDict(PdfName.DESTS);
            if (legacyDestinations == null)
            {
                legacyDestinations = new PdfDictionary();
                catalog.Put(PdfName.DESTS, legacyDestinations);
            }
            legacyDestinations.Put(
                new PdfName("NamedFitH"),
                CreateDestination(
                    reader.GetPageOrigRef(4),
                    PdfName.FITV,
                    new PdfNumber(36f)));
        }

        private static void AddOutlineTree(
            PdfReader reader,
            PdfWriter writer,
            PdfDictionary catalog,
            PdfIndirectReference ocgReference)
        {
            var rootReference = writer.PdfIndirectReference;
            var planReference = writer.PdfIndirectReference;
            var xyzReference = writer.PdfIndirectReference;
            var namedReference = writer.PdfIndirectReference;
            var namedNameReference = writer.PdfIndirectReference;
            var nestedClosedReference = writer.PdfIndirectReference;
            var deepLeafReference = writer.PdfIndirectReference;
            var layerReference = writer.PdfIndirectReference;
            var scriptReference = writer.PdfIndirectReference;

            var root = new PdfDictionary(PdfName.OUTLINES);
            root.Put(PdfName.FIRST, planReference);
            root.Put(PdfName.LAST, scriptReference);
            root.Put(PdfName.COUNT, new PdfNumber(7));

            var plan = CreateOutlineItem(
                "Plan general / Fit",
                rootReference);
            plan.Put(
                PdfName.DEST,
                CreateDestination(
                    reader.GetPageOrigRef(1),
                    PdfName.FIT));
            plan.Put(PdfName.FIRST, xyzReference);
            plan.Put(PdfName.LAST, namedNameReference);
            plan.Put(PdfName.COUNT, new PdfNumber(4));
            plan.Put(PdfName.NEXT, layerReference);
            var color = new PdfArray();
            color.Add(new PdfNumber(0.90f));
            color.Add(new PdfNumber(0.20f));
            color.Add(new PdfNumber(0.12f));
            plan.Put(PdfName.C, color);
            plan.Put(PdfName.F, new PdfNumber(2));

            var xyz = CreateOutlineItem(
                "Detalle / XYZ",
                planReference);
            var xyzAction = CreateGoToAction(
                CreateDestination(
                    reader.GetPageOrigRef(2),
                    PdfName.XYZ,
                    new PdfNumber(72f),
                    new PdfNumber(760f),
                    new PdfNumber(1.25f)));
            xyzAction.Put(
                new PdfName("QAActionFlag"),
                new PdfString("keep-goto-action-raw"));
            var chainedAction = new PdfDictionary();
            chainedAction.Put(PdfName.S, PdfName.JAVASCRIPT);
            chainedAction.Put(
                PdfName.JS,
                new PdfString(
                    "/* chained QA action: preserve, never execute */",
                    PdfObject.TEXT_UNICODE));
            chainedAction.Put(
                new PdfName("QAFlag"),
                new PdfString("keep-goto-next-raw"));
            xyzAction.Put(PdfName.NEXT, chainedAction);
            xyz.Put(PdfName.A, xyzAction);
            xyz.Put(PdfName.NEXT, namedReference);

            var named = CreateOutlineItem(
                "Destino nominal / FitH",
                planReference);
            named.Put(PdfName.PREV, xyzReference);
            named.Put(
                PdfName.DEST,
                new PdfString("NamedFitH"));
            named.Put(PdfName.NEXT, namedNameReference);
            named.Put(PdfName.FIRST, nestedClosedReference);
            named.Put(PdfName.LAST, nestedClosedReference);
            named.Put(PdfName.COUNT, new PdfNumber(1));

            var nestedClosed = CreateOutlineItem(
                "Subsección cerrada",
                namedReference);
            nestedClosed.Put(PdfName.FIRST, deepLeafReference);
            nestedClosed.Put(PdfName.LAST, deepLeafReference);
            nestedClosed.Put(PdfName.COUNT, new PdfNumber(-1));
            nestedClosed.Put(
                PdfName.DEST,
                CreateDestination(
                    reader.GetPageOrigRef(2),
                    PdfName.FIT));

            var deepLeaf = CreateOutlineItem(
                "Hoja profunda",
                nestedClosedReference);
            deepLeaf.Put(
                PdfName.DEST,
                CreateDestination(
                    reader.GetPageOrigRef(1),
                    PdfName.FITH,
                    new PdfNumber(680f)));

            var namedName = CreateOutlineItem(
                "Destino Name homónimo / FitV",
                planReference);
            namedName.Put(PdfName.PREV, namedReference);
            namedName.Put(
                PdfName.DEST,
                new PdfName("NamedFitH"));

            var layer = CreateOutlineItem(
                "Alternar capa / SetOCGState",
                rootReference);
            layer.Put(PdfName.PREV, planReference);
            layer.Put(PdfName.NEXT, scriptReference);
            var state = new PdfArray();
            state.Add(PdfName.TOGGLE);
            state.Add(ocgReference);
            var layerAction = new PdfDictionary();
            layerAction.Put(PdfName.S, PdfName.SETOCGSTATE);
            layerAction.Put(PdfName.STATE, state);
            layerAction.Put(PdfName.PRESERVERB, PdfBoolean.PDFTRUE);
            layerAction.Put(
                new PdfName("QAFlag"),
                new PdfString("keep-setocg-raw"));
            layer.Put(PdfName.A, layerAction);

            var script = CreateOutlineItem(
                "Acción JavaScript QA",
                rootReference);
            script.Put(PdfName.PREV, layerReference);
            var scriptAction = new PdfDictionary();
            scriptAction.Put(PdfName.S, PdfName.JAVASCRIPT);
            scriptAction.Put(
                PdfName.JS,
                new PdfString(
                    "/* PDF Ligero QA: preserve, never execute */",
                    PdfObject.TEXT_UNICODE));
            scriptAction.Put(
                new PdfName("QAFlag"),
                new PdfString("keep-javascript-raw"));
            script.Put(PdfName.A, scriptAction);

            writer.AddToBody(root, rootReference);
            writer.AddToBody(plan, planReference);
            writer.AddToBody(xyz, xyzReference);
            writer.AddToBody(named, namedReference);
            writer.AddToBody(namedName, namedNameReference);
            writer.AddToBody(
                nestedClosed,
                nestedClosedReference);
            writer.AddToBody(deepLeaf, deepLeafReference);
            writer.AddToBody(layer, layerReference);
            writer.AddToBody(script, scriptReference);
            catalog.Put(PdfName.OUTLINES, rootReference);
        }

        private static PdfDictionary CreateOutlineItem(
            string title,
            PdfIndirectReference parent)
        {
            var item = new PdfDictionary();
            item.Put(
                PdfName.TITLE,
                new PdfString(title, PdfObject.TEXT_UNICODE));
            item.Put(PdfName.PARENT, parent);
            return item;
        }

        private static PdfDictionary CreateGoToAction(
            PdfObject destination)
        {
            var action = new PdfDictionary();
            action.Put(PdfName.S, PdfName.GOTO);
            action.Put(PdfName.D, destination);
            return action;
        }

        private static PdfArray CreateDestination(
            PdfIndirectReference pageReference,
            PdfName mode,
            params PdfObject[] arguments)
        {
            var result = new PdfArray();
            result.Add(pageReference);
            result.Add(mode);
            foreach (var argument in arguments ??
                new PdfObject[0])
            {
                result.Add(argument);
            }

            return result;
        }

        private static void AddPageLinks(
            PdfReader reader,
            PdfWriter writer,
            PdfStamper stamper)
        {
            var localLink = CreateLinkAnnotation(
                CreateGoToAction(
                    CreateDestination(
                        reader.GetPageOrigRef(4),
                        PdfName.FIT)),
                72f,
                560f,
                330f,
                596f);
            AddAnnotationToPage(
                reader,
                writer,
                stamper,
                1,
                localLink);

            var uriAction = new PdfDictionary();
            uriAction.Put(PdfName.S, PdfName.URI);
            uriAction.Put(
                PdfName.URI,
                new PdfString("https://example.invalid/pdf-ligero-qa"));
            var uriLink = CreateLinkAnnotation(
                uriAction,
                72f,
                560f,
                330f,
                596f);
            AddAnnotationToPage(
                reader,
                writer,
                stamper,
                2,
                uriLink);
        }

        private static PdfDictionary CreateLinkAnnotation(
            PdfDictionary action,
            float left,
            float bottom,
            float right,
            float top)
        {
            var annotation = new PdfDictionary();
            annotation.Put(PdfName.TYPE, PdfName.ANNOT);
            annotation.Put(PdfName.SUBTYPE, PdfName.LINK);
            annotation.Put(
                PdfName.RECT,
                CreateNumberArray(left, bottom, right, top));
            annotation.Put(
                PdfName.BORDER,
                CreateNumberArray(0f, 0f, 0f));
            annotation.Put(PdfName.A, action);
            return annotation;
        }

        private static void AddAnnotationToPage(
            PdfReader reader,
            PdfWriter writer,
            PdfStamper stamper,
            int pageNumber,
            PdfDictionary annotation)
        {
            var page = reader.GetPageN(pageNumber);
            var annotations = page.GetAsArray(PdfName.ANNOTS);
            if (annotations == null)
            {
                annotations = new PdfArray();
                page.Put(PdfName.ANNOTS, annotations);
            }

            annotations.Add(
                writer.AddToBody(annotation).IndirectReference);
            stamper.MarkUsed(page);
            stamper.MarkUsed(annotations);
        }

        private static PdfArray CreateNumberArray(
            params float[] values)
        {
            var result = new PdfArray();
            foreach (var value in values)
            {
                result.Add(new PdfNumber(value));
            }

            return result;
        }

        private static void ValidateFixture(string path)
        {
            var reader = new PdfReader(path);
            try
            {
                Require(reader.NumberOfPages == 4, "Número de páginas.");
                ValidateOutlines(reader);
                ValidateNamedDestination(reader);
                ValidateForms(reader);
                ValidateLinks(reader);
                RecordSimpleBookmarkLossEvidence(reader);
            }
            finally
            {
                reader.Close();
            }

            Results.Add(
                "PASS original inmutable preparado para futuros Apply, " +
                "Undo/Redo y cancelación.");
        }

        private static void ValidateSignedFixture(string path)
        {
            var reader = new PdfReader(path);
            try
            {
                var names =
                    reader.AcroFields.GetSignatureNames();
                Require(
                    names.Contains("signature.pending"),
                    "Firma criptográfica de regresión.");
                var signature =
                    reader.AcroFields.VerifySignature(
                        "signature.pending");
                Require(
                    signature != null && signature.Verify(),
                    "Firma de regresión criptográficamente válida.");
                Require(
                    reader.AcroFields.SignatureCoversWholeDocument(
                        "signature.pending"),
                    "Firma inicial cubre todo el documento.");

                // Signing in append mode must not consume or rewrite any of
                // the structures that Phase 5 is expected to preserve.
                ValidateOutlines(reader);
                ValidateNamedDestination(reader);
                ValidateLinks(reader);
            }
            finally
            {
                reader.Close();
            }

            Results.Add(
                "PASS fixture firmada real: firma válida y estructuras " +
                "avanzadas intactas.");
        }

        private static void ValidateOutlines(PdfReader reader)
        {
            var root = ResolveDictionary(
                reader.Catalog.Get(PdfName.OUTLINES));
            Require(root != null, "Raíz /Outlines.");
            Require(root.GetAsNumber(PdfName.COUNT).IntValue == 7,
                "Cuenta visible del árbol.");

            var plan = ResolveDictionary(root.Get(PdfName.FIRST));
            RequireTitle(plan, "Plan general / Fit");
            Require(
                plan.GetAsNumber(PdfName.COUNT).IntValue == 4,
                "Cuenta visible de Plan.");
            var planDestination = ResolveArray(plan.Get(PdfName.DEST));
            RequireDestination(
                reader,
                planDestination,
                1,
                PdfName.FIT,
                2);

            var xyz = ResolveDictionary(plan.Get(PdfName.FIRST));
            RequireTitle(xyz, "Detalle / XYZ");
            var xyzAction = ResolveDictionary(xyz.Get(PdfName.A));
            Require(
                PdfName.GOTO.Equals(xyzAction.GetAsName(PdfName.S)),
                "Acción GoTo XYZ.");
            var xyzDestination = ResolveArray(
                xyzAction.Get(PdfName.D));
            RequireDestination(
                reader,
                xyzDestination,
                2,
                PdfName.XYZ,
                5);
            Require(
                NearlyEqual(
                    xyzDestination.GetAsNumber(2).FloatValue,
                    72f) &&
                NearlyEqual(
                    xyzDestination.GetAsNumber(3).FloatValue,
                    760f) &&
                NearlyEqual(
                    xyzDestination.GetAsNumber(4).FloatValue,
                    1.25f),
                "Parámetros XYZ exactos.");
            Require(
                string.Equals(
                    xyzAction.GetAsString(
                        new PdfName("QAActionFlag"))
                        .ToUnicodeString(),
                    "keep-goto-action-raw",
                    StringComparison.Ordinal),
                "Payload opaco de la acción GoTo.");
            var chainedAction = ResolveDictionary(
                xyzAction.Get(PdfName.NEXT));
            Require(
                chainedAction != null &&
                PdfName.JAVASCRIPT.Equals(
                    chainedAction.GetAsName(PdfName.S)) &&
                chainedAction.GetAsString(PdfName.JS)
                    .ToUnicodeString()
                    .Contains("never execute") &&
                string.Equals(
                    chainedAction.GetAsString(
                        new PdfName("QAFlag"))
                        .ToUnicodeString(),
                    "keep-goto-next-raw",
                    StringComparison.Ordinal),
                "Cadena /A/Next de la acción GoTo.");

            var named = ResolveDictionary(xyz.Get(PdfName.NEXT));
            RequireTitle(named, "Destino nominal / FitH");
            Require(
                string.Equals(
                    named.GetAsString(PdfName.DEST).ToUnicodeString(),
                    "NamedFitH",
                    StringComparison.Ordinal),
                "Marcador con destino nominal.");
            Require(
                named.GetAsNumber(PdfName.COUNT).IntValue == 1,
                "Cuenta abierta del destino String.");
            var nestedClosed = ResolveDictionary(
                named.Get(PdfName.FIRST));
            RequireTitle(nestedClosed, "Subsección cerrada");
            Require(
                nestedClosed.GetAsNumber(PdfName.COUNT).IntValue == -1,
                "Cuenta cerrada del nieto.");
            var deepLeaf = ResolveDictionary(
                nestedClosed.Get(PdfName.FIRST));
            RequireTitle(deepLeaf, "Hoja profunda");

            var namedName = ResolveDictionary(
                named.Get(PdfName.NEXT));
            RequireTitle(
                namedName,
                "Destino Name homónimo / FitV");
            Require(
                PdfName.DecodeName(
                    namedName.GetAsName(PdfName.DEST)
                        .ToString()) == "NamedFitH",
                "Marcador con destino PdfName homónimo.");

            var layer = ResolveDictionary(plan.Get(PdfName.NEXT));
            RequireTitle(layer, "Alternar capa / SetOCGState");
            var layerAction = ResolveDictionary(layer.Get(PdfName.A));
            Require(
                PdfName.SETOCGSTATE.Equals(
                    layerAction.GetAsName(PdfName.S)),
                "Acción SetOCGState.");
            Require(
                layerAction.GetAsArray(PdfName.STATE).Size == 2 &&
                string.Equals(
                    layerAction.GetAsString(
                        new PdfName("QAFlag")).ToUnicodeString(),
                    "keep-setocg-raw",
                    StringComparison.Ordinal),
                "Payload raw de SetOCGState.");

            var script = ResolveDictionary(layer.Get(PdfName.NEXT));
            RequireTitle(script, "Acción JavaScript QA");
            var scriptAction = ResolveDictionary(script.Get(PdfName.A));
            Require(
                PdfName.JAVASCRIPT.Equals(
                    scriptAction.GetAsName(PdfName.S)),
                "Acción JavaScript.");
            Require(
                scriptAction.GetAsString(PdfName.JS)
                    .ToUnicodeString()
                    .Contains("never execute") &&
                string.Equals(
                    scriptAction.GetAsString(
                        new PdfName("QAFlag")).ToUnicodeString(),
                    "keep-javascript-raw",
                    StringComparison.Ordinal),
                "Payload raw de JavaScript.");

            Results.Add(
                "PASS outlines anidados: Fit, GoTo+/Next, named " +
                "String/Name homónimos, cierres, SetOCGState y JavaScript.");
        }

        private static void ValidateNamedDestination(PdfReader reader)
        {
            var destinations = reader.GetNamedDestination(true);
            PdfObject stringDestination = null;
            PdfObject nameDestination = null;
            foreach (var item in destinations)
            {
                var keyName = item.Key as PdfName;
                var keyString = item.Key as PdfString;
                if (keyName != null &&
                    string.Equals(
                        PdfName.DecodeName(keyName.ToString()),
                        "NamedFitH",
                        StringComparison.Ordinal))
                {
                    nameDestination = item.Value;
                }
                else if (
                    (keyString != null &&
                     string.Equals(
                         keyString.ToUnicodeString(),
                         "NamedFitH",
                         StringComparison.Ordinal)) ||
                    (item.Key is string &&
                     string.Equals(
                         (string)item.Key,
                         "NamedFitH",
                         StringComparison.Ordinal)))
                {
                    stringDestination = item.Value;
                }
            }

            RequireDestination(
                reader,
                ResolveArray(stringDestination),
                3,
                PdfName.FITH,
                3);
            RequireDestination(
                reader,
                ResolveArray(nameDestination),
                4,
                PdfName.FITV,
                3);
            Require(
                NearlyEqual(
                    ResolveArray(stringDestination)
                        .GetAsNumber(2).FloatValue,
                    700f) &&
                NearlyEqual(
                    ResolveArray(nameDestination)
                        .GetAsNumber(2).FloatValue,
                    36f),
                "Coordenadas de destinos homónimos.");
            Results.Add(
                "PASS destinos homónimos tipados: " +
                "(NamedFitH) != /NamedFitH.");
        }

        private static void ValidateForms(PdfReader reader)
        {
            var fields = reader.AcroFields;
            Require(
                fields.Fields.ContainsKey("project.name") &&
                string.Equals(
                    fields.GetField("project.name"),
                    "AGOIN",
                    StringComparison.Ordinal),
                "Campo de texto AcroForm.");
            Require(
                fields.Fields.ContainsKey("project.approved") &&
                string.Equals(
                    fields.GetField("project.approved"),
                    "Yes",
                    StringComparison.Ordinal),
                "Checkbox AcroForm.");
            Require(
                fields.GetBlankSignatureNames()
                    .Contains("signature.pending"),
                "Campo de firma vacío.");
            Results.Add(
                "PASS AcroForm: texto, checkbox y firma vacía.");
        }

        private static void ValidateLinks(PdfReader reader)
        {
            var localFound = false;
            var uriFound = false;
            for (var pageNumber = 1;
                pageNumber <= reader.NumberOfPages;
                pageNumber++)
            {
                var annotations = reader.GetPageN(pageNumber)
                    .GetAsArray(PdfName.ANNOTS);
                if (annotations == null)
                {
                    continue;
                }

                for (var index = 0;
                    index < annotations.Size;
                    index++)
                {
                    var annotation = ResolveDictionary(
                        annotations[index]);
                    if (annotation == null ||
                        !PdfName.LINK.Equals(
                            annotation.GetAsName(
                                PdfName.SUBTYPE)))
                    {
                        continue;
                    }

                    var action = ResolveDictionary(
                        annotation.Get(PdfName.A));
                    if (action == null)
                    {
                        continue;
                    }

                    var actionType =
                        action.GetAsName(PdfName.S);
                    if (PdfName.GOTO.Equals(actionType))
                    {
                        localFound =
                            ResolveDestinationTargetPage(
                                reader,
                                action.Get(PdfName.D)) == 4;
                    }
                    else if (PdfName.URI.Equals(actionType))
                    {
                        var uri = action.GetAsString(PdfName.URI);
                        uriFound = uri != null &&
                            string.Equals(
                                uri.ToUnicodeString(),
                                "https://example.invalid/pdf-ligero-qa",
                                StringComparison.Ordinal);
                    }
                }
            }

            Require(localFound, "Enlace local GoTo a página 4.");
            Require(uriFound, "Enlace URI.");
            Results.Add("PASS enlaces de página GoTo y URI.");
        }

        private static void RecordSimpleBookmarkLossEvidence(
            PdfReader reader)
        {
            var bookmarks = SimpleBookmark.GetBookmark(reader);
            var layer = FindSimpleBookmark(
                bookmarks,
                "Alternar capa / SetOCGState");
            Require(layer != null, "Lectura SimpleBookmark de SetOCGState.");
            var keys = string.Join(
                ",",
                layer.Keys.OrderBy(key => key).ToArray());
            Results.Add(
                "EVIDENCIA: SimpleBookmark expone SetOCGState como claves [" +
                keys +
                "]; el editor no debe reserializar este árbol a ciegas.");
        }

        private static Dictionary<string, object> FindSimpleBookmark(
            IList<Dictionary<string, object>> bookmarks,
            string title)
        {
            if (bookmarks == null)
            {
                return null;
            }

            foreach (var bookmark in bookmarks)
            {
                object value;
                if (bookmark.TryGetValue("Title", out value) &&
                    string.Equals(
                        Convert.ToString(
                            value,
                            CultureInfo.InvariantCulture),
                        title,
                        StringComparison.Ordinal))
                {
                    return bookmark;
                }

                object kidsValue;
                var kids = bookmark.TryGetValue(
                    "Kids",
                    out kidsValue)
                        ? kidsValue as
                            IList<Dictionary<string, object>>
                        : null;
                var nested = FindSimpleBookmark(kids, title);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private static void RequireDestination(
            PdfReader reader,
            PdfArray destination,
            int expectedPage,
            PdfName expectedMode,
            int expectedSize)
        {
            Require(destination != null, "Array de destino.");
            Require(
                destination.Size == expectedSize,
                "Longitud de destino " + expectedMode + ".");
            Require(
                ResolveDestinationTargetPage(reader, destination) ==
                    expectedPage,
                "Página de destino " + expectedMode + ".");
            Require(
                expectedMode.Equals(destination.GetAsName(1)),
                "Tipo de destino " + expectedMode + ".");
        }

        private static int ResolveDestinationTargetPage(
            PdfReader reader,
            PdfObject destinationObject)
        {
            var destination = ResolveArray(destinationObject);
            if (destination == null || destination.Size == 0)
            {
                return -1;
            }

            var pageReference =
                destination[0] as PdfIndirectReference;
            if (pageReference == null)
            {
                return -1;
            }

            for (var pageNumber = 1;
                pageNumber <= reader.NumberOfPages;
                pageNumber++)
            {
                var candidate =
                    reader.GetPageOrigRef(pageNumber);
                if (candidate != null &&
                    candidate.Number == pageReference.Number &&
                    candidate.Generation ==
                        pageReference.Generation)
                {
                    return pageNumber;
                }
            }

            return -1;
        }

        private static void RequireTitle(
            PdfDictionary outline,
            string expected)
        {
            Require(outline != null, "Nodo " + expected + ".");
            var title = outline.GetAsString(PdfName.TITLE);
            Require(
                title != null &&
                string.Equals(
                    title.ToUnicodeString(),
                    expected,
                    StringComparison.Ordinal),
                "Título " + expected + ".");
        }

        private static PdfDictionary ResolveDictionary(
            PdfObject value)
        {
            return PdfReader.GetPdfObject(value)
                as PdfDictionary;
        }

        private static PdfArray ResolveArray(PdfObject value)
        {
            return PdfReader.GetPdfObject(value)
                as PdfArray;
        }

        private static bool NearlyEqual(float left, float right)
        {
            return Math.Abs(left - right) <= 0.001f;
        }

        private static void Require(bool condition, string description)
        {
            if (!condition)
            {
                throw new InvalidDataException(
                    "No se validó: " + description);
            }
        }

        private static string ComputeSha256(string path)
        {
            using (var input = File.OpenRead(path))
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(
                        sha.ComputeHash(input))
                    .Replace("-", string.Empty);
            }
        }

        private static string FormatException(Exception error)
        {
            return error.GetType().Name +
                ": " +
                error.GetBaseException().Message;
        }

        private static void TryDeleteFile(string path)
        {
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
    }
}

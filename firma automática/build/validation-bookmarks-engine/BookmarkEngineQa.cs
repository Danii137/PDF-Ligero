using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using iTextSharp.text.pdf;

namespace FirmaAutomatica
{
    internal static class BookmarkEngineQa
    {
        private static readonly List<string> Results =
            new List<string>();

        private static int Main(string[] args)
        {
            try
            {
                var run = Path.GetFullPath(args[0]);
                var fixture = Path.GetFullPath(args[1]);
                var signedFixture = Path.GetFullPath(args[2]);
                Directory.CreateDirectory(run);
                TestUnsigned(run, fixture);
                TestSigned(run, signedFixture);
                TestCancellation(run, fixture);
                TestSourceChange(run, fixture);
                var report = Path.Combine(run, "qa-report.txt");
                File.WriteAllLines(
                    report,
                    Results.ToArray());
                Console.WriteLine("PASS");
                Console.WriteLine("report=" + report);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }

        private static void TestUnsigned(
            string run,
            string fixture)
        {
            var sourceHash = Hash(fixture);
            var loaded = PdfBookmarkService.Load(fixture);
            Require(loaded.PageCount == 4, "Carga 4 páginas.");
            Require(Count(loaded.Bookmarks) == 8, "Carga árbol completo.");
            Require(loaded.PageGeometries.Count == 4, "Carga geometría.");
            var xyz = Find(loaded.Bookmarks, "Detalle / XYZ");
            Require(
                xyz != null &&
                xyz.Destination != null &&
                xyz.Destination.Mode ==
                    PdfBookmarkDestinationMode.Xyz &&
                xyz.Destination.PageNumber == 2 &&
                Near(xyz.Destination.Zoom, 1.25),
                "Lee destino XYZ.");
            var fit = Find(
                loaded.Bookmarks,
                "Plan general / Fit");
            Require(
                fit != null &&
                fit.Destination != null &&
                fit.Destination.Mode ==
                    PdfBookmarkDestinationMode.Fit,
                "Conserva modo Fit.");
            var fitPoint = PdfBookmarkService.GetPdfPoint(
                loaded,
                fit.Destination);
            Require(
                !fitPoint.HasX && !fitPoint.HasY,
                "Fit no inventa coordenadas.");
            var named = Find(
                loaded.Bookmarks,
                "Destino nominal / FitH");
            Require(
                named != null &&
                named.Destination != null &&
                named.Destination.Mode ==
                    PdfBookmarkDestinationMode.FitHorizontal &&
                named.Destination.PageNumber == 3,
                "Resuelve destino nominal.");
            var advanced = Find(
                loaded.Bookmarks,
                "Alternar capa / SetOCGState");
            Require(
                advanced != null &&
                advanced.Destination == null &&
                !advanced.IsDestinationEditable,
                "Expone acción avanzada como opaca.");

            var edited = PdfBookmarkService.CloneDocument(loaded);
            var plan = Find(edited.Bookmarks, "Plan general / Fit");
            var layer = Find(
                edited.Bookmarks,
                "Alternar capa / SetOCGState");
            var script = Find(
                edited.Bookmarks,
                "Acción JavaScript QA");
            var detail = Find(edited.Bookmarks, "Detalle / XYZ");
            PdfBookmarkService.Rename(
                edited,
                layer.Id,
                "CAPAS / SetOCGState");
            PdfBookmarkService.Move(
                edited,
                script.Id,
                plan.Id,
                0);
            PdfBookmarkService.Move(
                edited,
                detail.Id,
                null,
                1);
            PdfBookmarkService.SetDestination(
                edited,
                plan.Id,
                new PdfBookmarkDestination(
                    1,
                    12.5,
                    20.0,
                    1.1));
            var created = PdfBookmarkService.Create(
                edited,
                plan.Id,
                plan.Children.Count,
                "NUEVO / PÁGINA 4",
                new PdfBookmarkDestination(
                    4,
                    37.5,
                    null,
                    null));
            PdfBookmarkService.Rename(
                edited,
                plan.Id,
                "PLAN GENERAL RENOMBRADO");
            var disposable = PdfBookmarkService.Create(
                edited,
                null,
                edited.Bookmarks.Count,
                "BORRABLE",
                new PdfBookmarkDestination(2, null));
            PdfBookmarkService.Delete(edited, disposable.Id);
            Require(
                Find(loaded.Bookmarks, "CAPAS / SetOCGState") == null,
                "Clon no muta modelo cargado.");

            var output = Path.Combine(
                run,
                "bookmark-engine-result.pdf");
            TryDelete(output);
            var progress = new List<int>();
            var result = PdfBookmarkService.Save(
                fixture,
                edited,
                output,
                delegate(PdfBookmarkProgress value)
                {
                    progress.Add(value.Percentage);
                },
                CancellationToken.None);
            Require(File.Exists(output), "Crea salida.");
            Require(
                result.BookmarkCount == 9,
                "Resultado declara nueve marcadores.");
            Require(
                progress.Count >= 4 &&
                progress[progress.Count - 1] == 100,
                "Informa progreso.");
            Require(
                string.Equals(
                    sourceHash,
                    Hash(fixture),
                    StringComparison.Ordinal),
                "Original intacto.");

            var reopened = PdfBookmarkService.Load(output);
            Require(
                Count(reopened.Bookmarks) == 9,
                "Reabre árbol editado.");
            Require(
                string.Equals(
                    reopened.Bookmarks[0].Title,
                    "PLAN GENERAL RENOMBRADO",
                    StringComparison.Ordinal),
                "Conserva orden raíz.");
            Require(
                string.Equals(
                    reopened.Bookmarks[1].Title,
                    "Detalle / XYZ",
                    StringComparison.Ordinal),
                "Reordena entre niveles.");
            Require(
                Find(reopened.Bookmarks, created.Title) != null,
                "Crea marcador.");
            ValidateRawAdvanced(output);
            ValidateForms(output, false);
            Results.Add(
                "PASS motor: leer/crear/renombrar/borrar/reordenar/" +
                "reparentar/destino.");
            Results.Add(
                "PASS preservación: SetOCGState, JavaScript, named, " +
                "formularios, metadatos y contenido vectorial.");

            var emptyDocument =
                PdfBookmarkService.CloneDocument(loaded);
            while (emptyDocument.Bookmarks.Count > 0)
            {
                PdfBookmarkService.Delete(
                    emptyDocument,
                    emptyDocument.Bookmarks[
                        emptyDocument.Bookmarks.Count - 1].Id);
            }

            var emptyOutput = Path.Combine(
                run,
                "bookmark-engine-empty-result.pdf");
            TryDelete(emptyOutput);
            PdfBookmarkService.Save(
                fixture,
                emptyOutput,
                emptyDocument);
            var emptyReloaded =
                PdfBookmarkService.Load(emptyOutput);
            Require(
                emptyReloaded.Bookmarks.Count == 0,
                "Elimina todos los marcadores.");
            using (var emptyReader = new PdfReader(emptyOutput))
            {
                Require(
                    emptyReader.Catalog.Get(PdfName.OUTLINES) == null,
                    "Retira /Outlines vacío del catálogo.");
            }

            Results.Add(
                "PASS borrado completo sin raíz /Outlines residual.");
        }

        private static void TestSigned(
            string run,
            string fixture)
        {
            var sourceHash = Hash(fixture);
            var loaded = PdfBookmarkService.Load(fixture);
            Require(
                loaded.ContainsDigitalSignatures,
                "Detecta firma digital.");
            var edited = PdfBookmarkService.CloneDocument(loaded);
            PdfBookmarkService.Rename(
                edited,
                edited.Bookmarks[0].Id,
                "PLAN / EDICIÓN POSTERIOR");
            var output = Path.Combine(
                run,
                "bookmark-engine-signed-result.pdf");
            TryDelete(output);
            var result = PdfBookmarkService.Save(
                fixture,
                output,
                edited);
            Require(
                result.DigitalSignaturesInvalidated,
                "Avisa de firma.");
            Require(
                string.Equals(
                    sourceHash,
                    Hash(fixture),
                    StringComparison.Ordinal),
                "Original firmado intacto.");
            ValidateForms(output, true);
            ValidateRawAdvanced(output);
            Results.Add(
                "PASS firma: revisión incremental, firma previa " +
                "criptográficamente verificable y aviso activo.");
        }

        private static void TestCancellation(
            string run,
            string fixture)
        {
            var output = Path.Combine(run, "cancelled.pdf");
            TryDelete(output);
            var document = PdfBookmarkService.Load(fixture);
            var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var cancelled = false;
            try
            {
                PdfBookmarkService.Save(
                    fixture,
                    document,
                    output,
                    null,
                    cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }

            Require(cancelled, "Respeta cancelación.");
            Require(!File.Exists(output), "Cancelación sin salida.");
            Results.Add("PASS cancelación sin salida parcial.");
        }

        private static void TestSourceChange(
            string run,
            string fixture)
        {
            var changed = Path.Combine(run, "changed-source.pdf");
            File.Copy(fixture, changed, true);
            var document = PdfBookmarkService.Load(changed);
            File.SetLastWriteTimeUtc(
                changed,
                File.GetLastWriteTimeUtc(changed).AddSeconds(2));
            var output = Path.Combine(run, "changed-result.pdf");
            TryDelete(output);
            var rejected = false;
            try
            {
                PdfBookmarkService.Save(changed, output, document);
            }
            catch (InvalidOperationException ex)
            {
                rejected = ex.Message.Contains("cambio");
            }

            Require(rejected, "Rechaza fuente cambiada.");
            Require(!File.Exists(output), "Fuente cambiada sin salida.");
            Results.Add("PASS control de concurrencia por identidad.");
        }

        private static void ValidateRawAdvanced(string path)
        {
            var reader = new PdfReader(path);
            try
            {
                var root = Dict(reader.Catalog.Get(PdfName.OUTLINES));
                Require(root != null, "Salida conserva /Outlines.");
                var all = Flatten(root.Get(PdfName.FIRST));
                var layer = FindRaw(all, "CAPAS / SetOCGState") ??
                    FindRaw(all, "Alternar capa / SetOCGState");
                var action = Dict(layer.Get(PdfName.A));
                Require(
                    PdfName.SETOCGSTATE.Equals(
                        action.GetAsName(PdfName.S)),
                    "Conserva SetOCGState.");
                Require(
                    string.Equals(
                        action.GetAsString(new PdfName("QAFlag"))
                            .ToUnicodeString(),
                        "keep-setocg-raw",
                        StringComparison.Ordinal),
                    "Conserva payload SetOCGState.");
                var script = FindRaw(all, "Acción JavaScript QA");
                var scriptAction = Dict(script.Get(PdfName.A));
                Require(
                    PdfName.JAVASCRIPT.Equals(
                        scriptAction.GetAsName(PdfName.S)) &&
                    scriptAction.GetAsString(PdfName.JS)
                        .ToUnicodeString()
                        .Contains("never execute"),
                    "Conserva JavaScript raw.");
                var named = FindRaw(
                    all,
                    "Destino nominal / FitH");
                Require(
                    named.GetAsString(PdfName.DEST) != null &&
                    string.Equals(
                        named.GetAsString(PdfName.DEST)
                            .ToUnicodeString(),
                        "NamedFitH",
                        StringComparison.Ordinal),
                    "Conserva destino nominal.");
                var destinations =
                    SimpleNamedDestination.GetNamedDestination(
                        reader,
                        false);
                Require(
                    destinations.ContainsKey("NamedFitH"),
                    "Conserva Names/Dests.");
            }
            finally
            {
                reader.Close();
            }
        }

        private static void ValidateForms(
            string path,
            bool signed)
        {
            var reader = new PdfReader(path);
            try
            {
                Require(
                    reader.NumberOfPages == 4,
                    "Conserva páginas.");
                Require(
                    string.Equals(
                        reader.Info["Title"],
                        "Fixture de marcadores avanzados",
                        StringComparison.Ordinal),
                    "Conserva título metadata.");
                Require(
                    string.Equals(
                        reader.AcroFields.GetField("project.name"),
                        "AGOIN",
                        StringComparison.Ordinal),
                    "Conserva campo texto.");
                Require(
                    string.Equals(
                        reader.AcroFields.GetField("project.approved"),
                        "Yes",
                        StringComparison.Ordinal),
                    "Conserva checkbox.");
                if (signed)
                {
                    var signatures =
                        reader.AcroFields.GetSignatureNames();
                    Require(
                        signatures.Contains("signature.pending"),
                        "Conserva firma.");
                    var pkcs7 = reader.AcroFields.VerifySignature(
                        "signature.pending");
                    Require(
                        pkcs7 != null && pkcs7.Verify(),
                        "Firma previa sigue criptográficamente válida.");
                    Require(
                        !reader.AcroFields.SignatureCoversWholeDocument(
                            "signature.pending"),
                        "Edición queda después de firma.");
                }
            }
            finally
            {
                reader.Close();
            }
        }

        private static List<PdfDictionary> Flatten(PdfObject first)
        {
            var result = new List<PdfDictionary>();
            var current = first;
            while (current != null)
            {
                var item = Dict(current);
                result.Add(item);
                result.AddRange(Flatten(item.Get(PdfName.FIRST)));
                current = item.Get(PdfName.NEXT);
            }

            return result;
        }

        private static PdfDictionary FindRaw(
            IList<PdfDictionary> nodes,
            string title)
        {
            foreach (var node in nodes)
            {
                var value = node.GetAsString(PdfName.TITLE);
                if (value != null &&
                    string.Equals(
                        value.ToUnicodeString(),
                        title,
                        StringComparison.Ordinal))
                {
                    return node;
                }
            }

            return null;
        }

        private static PdfDictionary Dict(PdfObject value)
        {
            return PdfReader.GetPdfObject(value) as PdfDictionary;
        }

        private static PdfBookmarkNode Find(
            IList<PdfBookmarkNode> nodes,
            string title)
        {
            foreach (var node in nodes)
            {
                if (string.Equals(
                        node.Title,
                        title,
                        StringComparison.Ordinal))
                {
                    return node;
                }

                var nested = Find(node.Children, title);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private static int Count(IList<PdfBookmarkNode> nodes)
        {
            var result = nodes.Count;
            foreach (var node in nodes)
            {
                result += Count(node.Children);
            }

            return result;
        }

        private static bool Near(double? value, double expected)
        {
            return value.HasValue &&
                Math.Abs(value.Value - expected) < 0.001;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidDataException(message);
            }
        }

        private static string Hash(string path)
        {
            using (var input = File.OpenRead(path))
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(
                        sha.ComputeHash(input))
                    .Replace("-", string.Empty);
            }
        }

        private static void TryDelete(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}

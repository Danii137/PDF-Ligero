using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using iTextSharp.text;
using iTextSharp.text.pdf;
using PdfiumViewer;
using DrawingRectangle = System.Drawing.Rectangle;
using PdfDocument = PdfiumViewer.PdfDocument;
using PdfRectangle = iTextSharp.text.Rectangle;
using PdfTextExtractor =
    iTextSharp.text.pdf.parser.PdfTextExtractor;
using SimpleTextExtractionStrategy =
    iTextSharp.text.pdf.parser.SimpleTextExtractionStrategy;

namespace FirmaAutomatica
{
    internal static class PlanComparisonFixtureQa
    {
        private const long MemoryCeilingBytes = 384L * 1024L * 1024L;
        private const long PixelCeiling = 4000000L;
        private static readonly List<string> Report = new List<string>();

        private sealed class PageDefinition
        {
            public int PageNumber;
            public float WidthA;
            public float HeightA;
            public float WidthB;
            public float HeightB;
            public float TranslationX;
            public float TranslationY;
            public ChangeRegion[] ExpectedChanges;
        }

        private sealed class ChangeRegion
        {
            public string Name;
            public float Left;
            public float Bottom;
            public float Right;
            public float Top;

            public ChangeRegion(
                string name,
                float left,
                float bottom,
                float right,
                float top)
            {
                Name = name;
                Left = left;
                Bottom = bottom;
                Right = right;
                Top = top;
            }
        }

        private static readonly PageDefinition[] Pages =
        {
            new PageDefinition
            {
                PageNumber = 1,
                WidthA = 1190F,
                HeightA = 842F,
                WidthB = 1204F,
                HeightB = 856F,
                TranslationX = 15F,
                TranslationY = -15F,
                ExpectedChanges = new[]
                {
                    new ChangeRegion(
                        "muro desplazado",
                        680F,
                        175F,
                        752F,
                        430F),
                    new ChangeRegion(
                        "pilar añadido",
                        548F,
                        492F,
                        606F,
                        550F),
                    new ChangeRegion(
                        "nota revisada",
                        520F,
                        650F,
                        870F,
                        700F),
                    new ChangeRegion(
                        "índice de revisión",
                        1060F,
                        62F,
                        1170F,
                        118F)
                }
            },
            new PageDefinition
            {
                PageNumber = 2,
                WidthA = 595F,
                HeightA = 842F,
                WidthB = 603F,
                HeightB = 849F,
                TranslationX = 10F,
                TranslationY = -10F,
                ExpectedChanges = new[]
                {
                    new ChangeRegion(
                        "cubierta modificada",
                        250F,
                        540F,
                        455F,
                        670F),
                    new ChangeRegion(
                        "conducto añadido",
                        405F,
                        235F,
                        480F,
                        535F),
                    new ChangeRegion(
                        "nota de sección",
                        70F,
                        690F,
                        510F,
                        750F),
                    new ChangeRegion(
                        "índice de revisión",
                        480F,
                        62F,
                        570F,
                        118F)
                }
            }
        };

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.Error.WriteLine(
                    "Uso: PlanComparisonFixtureQa <carpeta-run>");
                return 2;
            }

            var run = Path.GetFullPath(args[0]);
            Directory.CreateDirectory(run);
            var sourceA = Path.Combine(run, "revision-A.pdf");
            var sourceB = Path.Combine(run, "revision-B.pdf");

            try
            {
                Report.Add("QA FIXTURE / COMPARACIÓN DE PLANOS");
                Report.Add(
                    "Inicio UTC: " +
                    DateTime.UtcNow.ToString(
                        "O",
                        CultureInfo.InvariantCulture));

                CreateFixture(sourceA, false);
                CreateFixture(sourceB, true);
                WriteManifest(run);

                var hashA = HashFile(sourceA);
                var hashB = HashFile(sourceB);
                var infoA = new FileInfo(sourceA);
                var infoB = new FileInfo(sourceB);
                var writeA = infoA.LastWriteTimeUtc;
                var writeB = infoB.LastWriteTimeUtc;
                Report.Add("SHA-256 A: " + hashA);
                Report.Add("SHA-256 B: " + hashB);

                ValidatePdf(sourceA, false);
                ValidatePdf(sourceB, true);
                RenderAndCompare(run, sourceA, sourceB);
                ValidateReferenceCancellation(run);

                Require(
                    string.Equals(
                        hashA,
                        HashFile(sourceA),
                        StringComparison.Ordinal),
                    "Cambió el SHA-256 de revision-A.pdf.");
                Require(
                    string.Equals(
                        hashB,
                        HashFile(sourceB),
                        StringComparison.Ordinal),
                    "Cambió el SHA-256 de revision-B.pdf.");
                Require(
                    new FileInfo(sourceA).LastWriteTimeUtc == writeA,
                    "Cambió la fecha de revision-A.pdf.");
                Require(
                    new FileInfo(sourceB).LastWriteTimeUtc == writeB,
                    "Cambió la fecha de revision-B.pdf.");

                var peak = Process.GetCurrentProcess().PeakWorkingSet64;
                Report.Add(
                    "Pico de memoria del harness: " +
                    FormatMiB(peak));
                Require(
                    peak <= MemoryCeilingBytes,
                    "El harness superó el techo de memoria: " +
                    FormatMiB(peak) +
                    " > " +
                    FormatMiB(MemoryCeilingBytes) +
                    ".");
                Report.Add(
                    "PASS consumo acotado: <= " +
                    FormatMiB(MemoryCeilingBytes) +
                    " y <= " +
                    PixelCeiling.ToString(
                        CultureInfo.InvariantCulture) +
                    " píxeles/render.");
                Report.Add(
                    "PASS originales: SHA-256 y fecha intactos.");
                Report.Add("RESULTADO GLOBAL: PASS");
                WriteReport(run);
                Console.WriteLine("PASS");
                Console.WriteLine(
                    "RUN_DIRECTORY=" + run);
                return 0;
            }
            catch (Exception ex)
            {
                Report.Add("RESULTADO GLOBAL: FAIL");
                Report.Add(ex.ToString());
                WriteReport(run);
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }

        private static void CreateFixture(
            string path,
            bool revisionB)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            using (var output = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            {
                var document = new Document(
                    new PdfRectangle(
                        revisionB
                            ? Pages[0].WidthB
                            : Pages[0].WidthA,
                        revisionB
                            ? Pages[0].HeightB
                            : Pages[0].HeightA),
                    0F,
                    0F,
                    0F,
                    0F);
                var writer = PdfWriter.GetInstance(document, output);
                writer.SetFullCompression();
                document.AddTitle(
                    revisionB
                        ? "Edificio QA - revisión B"
                        : "Edificio QA - revisión A");
                document.AddAuthor("PDF Ligero QA");
                document.AddSubject(
                    "Fixture vectorial de comparación de planos");
                document.Open();

                for (var index = 0; index < Pages.Length; index++)
                {
                    var definition = Pages[index];
                    var width = revisionB
                        ? definition.WidthB
                        : definition.WidthA;
                    var height = revisionB
                        ? definition.HeightB
                        : definition.HeightA;
                    document.SetPageSize(
                        new PdfRectangle(width, height));
                    if (index > 0)
                    {
                        document.NewPage();
                    }

                    var canvas = writer.DirectContent;
                    canvas.SaveState();
                    if (revisionB)
                    {
                        canvas.ConcatCTM(
                            1F,
                            0F,
                            0F,
                            1F,
                            definition.TranslationX,
                            definition.TranslationY);
                    }

                    if (definition.PageNumber == 1)
                    {
                        DrawPlanPage(canvas, revisionB);
                    }
                    else
                    {
                        DrawSectionPage(canvas, revisionB);
                    }

                    canvas.RestoreState();
                }

                document.Close();
            }
        }

        private static void DrawPlanPage(
            PdfContentByte canvas,
            bool revisionB)
        {
            DrawSheetFrame(
                canvas,
                1190F,
                842F,
                "PLANTA GENERAL",
                "A1.01",
                revisionB ? "B" : "A");
            DrawText(
                canvas,
                60F,
                786F,
                19F,
                "CENTRO CÍVICO / PLANTA BAJA",
                true);
            DrawText(
                canvas,
                60F,
                766F,
                8F,
                "COMPARACIÓN VECTORIAL - ESCALA GRÁFICA 1:100",
                false);

            canvas.SetLineWidth(0.35F);
            canvas.SetColorStroke(new BaseColor(170, 170, 170));
            for (var x = 180F; x <= 900F; x += 120F)
            {
                canvas.MoveTo(x, 150F);
                canvas.LineTo(x, 680F);
            }

            for (var y = 190F; y <= 630F; y += 110F)
            {
                canvas.MoveTo(140F, y);
                canvas.LineTo(940F, y);
            }

            canvas.Stroke();
            canvas.SetColorStroke(BaseColor.BLACK);
            canvas.SetLineWidth(4F);
            canvas.Rectangle(180F, 190F, 720F, 440F);
            canvas.Stroke();

            canvas.SetLineWidth(2.2F);
            canvas.MoveTo(420F, 190F);
            canvas.LineTo(420F, 630F);
            canvas.MoveTo(180F, 410F);
            canvas.LineTo(900F, 410F);
            var wallX = revisionB ? 730F : 700F;
            canvas.MoveTo(wallX, 190F);
            canvas.LineTo(wallX, 410F);
            canvas.Stroke();

            DrawDoor(canvas, 420F, 410F, 56F, true);
            DrawDoor(canvas, wallX, 190F, 62F, false);
            DrawWindow(canvas, 270F, 630F, 115F);
            DrawWindow(canvas, 760F, 630F, 90F);
            DrawColumn(canvas, 300F, 300F);
            DrawColumn(canvas, 585F, 520F);
            if (revisionB)
            {
                DrawColumn(canvas, 575F, 520F);
                canvas.SetLineWidth(1.1F);
                canvas.Circle(575F, 520F, 24F);
                canvas.Stroke();
            }

            DrawRoomLabel(canvas, 260F, 515F, "SALA MULTIUSOS", "62,4 m2");
            DrawRoomLabel(canvas, 520F, 515F, "VESTÍBULO", "28,8 m2");
            DrawRoomLabel(canvas, 735F, 515F, "ADMINISTRACIÓN", "31,6 m2");
            DrawRoomLabel(canvas, 265F, 300F, "AULA 01", "48,2 m2");
            DrawRoomLabel(canvas, 520F, 300F, "AULA 02", "45,7 m2");
            DrawRoomLabel(canvas, 760F, 300F, "INSTALACIONES", "22,1 m2");

            canvas.SetColorStroke(new BaseColor(75, 75, 75));
            canvas.SetLineWidth(0.6F);
            DrawDimension(
                canvas,
                180F,
                675F,
                900F,
                675F,
                "25,40");
            DrawDimension(canvas, 135F, 190F, 135F, 630F, "15,50");
            canvas.SetColorStroke(BaseColor.BLACK);

            DrawText(
                canvas,
                525F,
                676F,
                10F,
                revisionB
                    ? "NOTA B: NUEVO PILAR Y AJUSTE DE DISTRIBUCIÓN"
                    : "NOTA A: DISTRIBUCIÓN PARA LICENCIA",
                true);
            DrawNorthArrow(canvas, 1020F, 560F);
            DrawScale(canvas, 965F, 470F);
        }

        private static void DrawSectionPage(
            PdfContentByte canvas,
            bool revisionB)
        {
            DrawSheetFrame(
                canvas,
                595F,
                842F,
                "SECCIÓN CONSTRUCTIVA",
                "A3.02",
                revisionB ? "B" : "A");
            DrawText(
                canvas,
                48F,
                786F,
                17F,
                "SECCIÓN TRANSVERSAL S-02",
                true);
            DrawText(
                canvas,
                48F,
                765F,
                8F,
                "COTA ±0,00 / ESCALA 1:50",
                false);

            canvas.SetLineWidth(3.5F);
            canvas.Rectangle(100F, 185F, 370F, 360F);
            canvas.Stroke();
            canvas.SetLineWidth(2F);
            canvas.MoveTo(100F, 335F);
            canvas.LineTo(470F, 335F);
            canvas.MoveTo(100F, 465F);
            canvas.LineTo(470F, 465F);
            canvas.Stroke();

            canvas.SetLineWidth(4F);
            canvas.MoveTo(82F, 545F);
            canvas.LineTo(270F, 655F);
            if (revisionB)
            {
                canvas.LineTo(455F, 585F);
            }
            else
            {
                canvas.LineTo(470F, 545F);
            }

            canvas.Stroke();
            canvas.SetLineWidth(0.5F);
            for (var x = 115F; x < 460F; x += 22F)
            {
                canvas.MoveTo(x, 190F);
                canvas.LineTo(x + 35F, 225F);
            }

            canvas.Stroke();
            DrawPerson(canvas, 185F, 195F);
            DrawPerson(canvas, 320F, 335F);
            DrawLevel(canvas, 62F, 185F, "±0,00");
            DrawLevel(canvas, 62F, 335F, "+3,20");
            DrawLevel(canvas, 62F, 465F, "+6,05");
            DrawLevel(canvas, 62F, 545F, "+7,80");

            if (revisionB)
            {
                canvas.SetLineWidth(2.5F);
                canvas.Rectangle(424F, 250F, 28F, 270F);
                canvas.Stroke();
                canvas.Circle(438F, 270F, 8F);
                canvas.Stroke();
                DrawText(
                    canvas,
                    390F,
                    225F,
                    7.5F,
                    "NUEVO CONDUCTO",
                    true);
            }

            DrawText(
                canvas,
                72F,
                715F,
                10F,
                revisionB
                    ? "REV. B / PENDIENTE DE CUBIERTA Y CONDUCTO ACTUALIZADOS"
                    : "REV. A / SECCIÓN BASE PARA COORDINACIÓN",
                true);
            DrawText(canvas, 130F, 580F, 7F, "CUBIERTA LIGERA", false);
            DrawText(canvas, 120F, 430F, 7F, "FORJADO PLANTA 1", false);
            DrawText(canvas, 120F, 300F, 7F, "FORJADO PLANTA BAJA", false);
        }

        private static void DrawSheetFrame(
            PdfContentByte canvas,
            float width,
            float height,
            string sheetName,
            string sheetNumber,
            string revision)
        {
            canvas.SetColorStroke(new BaseColor(55, 55, 55));
            canvas.SetLineWidth(0.8F);
            canvas.Rectangle(28F, 28F, width - 56F, height - 56F);
            canvas.Stroke();

            var blockLeft = width - 285F;
            canvas.SetLineWidth(0.6F);
            canvas.Rectangle(blockLeft, 48F, 237F, 82F);
            canvas.MoveTo(blockLeft, 83F);
            canvas.LineTo(width - 48F, 83F);
            canvas.MoveTo(width - 108F, 48F);
            canvas.LineTo(width - 108F, 130F);
            canvas.Stroke();
            DrawText(
                canvas,
                blockLeft + 10F,
                104F,
                9F,
                "PDF LIGERO / QA",
                true);
            DrawText(
                canvas,
                blockLeft + 10F,
                89F,
                7F,
                sheetName,
                false);
            DrawText(
                canvas,
                blockLeft + 10F,
                66F,
                7F,
                "PROYECTO: CENTRO CÍVICO",
                false);
            DrawText(
                canvas,
                width - 98F,
                92F,
                15F,
                sheetNumber,
                true);
            DrawText(
                canvas,
                width - 98F,
                65F,
                9F,
                "REV. " + revision,
                true);
        }

        private static void DrawText(
            PdfContentByte canvas,
            float x,
            float y,
            float size,
            string text,
            bool bold)
        {
            var font = BaseFont.CreateFont(
                bold
                    ? BaseFont.HELVETICA_BOLD
                    : BaseFont.HELVETICA,
                BaseFont.CP1252,
                BaseFont.NOT_EMBEDDED);
            canvas.BeginText();
            canvas.SetFontAndSize(font, size);
            canvas.SetTextMatrix(x, y);
            canvas.ShowText(text);
            canvas.EndText();
        }

        private static void DrawRoomLabel(
            PdfContentByte canvas,
            float x,
            float y,
            string name,
            string area)
        {
            DrawText(canvas, x, y, 8F, name, true);
            DrawText(canvas, x, y - 13F, 7F, area, false);
        }

        private static void DrawColumn(
            PdfContentByte canvas,
            float x,
            float y)
        {
            canvas.SetLineWidth(1.4F);
            canvas.Rectangle(x - 8F, y - 8F, 16F, 16F);
            canvas.Stroke();
        }

        private static void DrawDoor(
            PdfContentByte canvas,
            float x,
            float y,
            float radius,
            bool horizontal)
        {
            canvas.SetLineWidth(0.8F);
            if (horizontal)
            {
                canvas.MoveTo(x, y);
                canvas.LineTo(x + radius, y);
                canvas.Arc(
                    x - radius,
                    y - radius,
                    x + radius,
                    y + radius,
                    0F,
                    90F);
            }
            else
            {
                canvas.MoveTo(x, y);
                canvas.LineTo(x, y + radius);
                canvas.Arc(
                    x - radius,
                    y - radius,
                    x + radius,
                    y + radius,
                    90F,
                    90F);
            }

            canvas.Stroke();
        }

        private static void DrawWindow(
            PdfContentByte canvas,
            float x,
            float y,
            float width)
        {
            canvas.SetLineWidth(1F);
            canvas.MoveTo(x, y - 5F);
            canvas.LineTo(x + width, y - 5F);
            canvas.MoveTo(x, y + 5F);
            canvas.LineTo(x + width, y + 5F);
            canvas.Stroke();
        }

        private static void DrawDimension(
            PdfContentByte canvas,
            float x1,
            float y1,
            float x2,
            float y2,
            string label)
        {
            canvas.MoveTo(x1, y1);
            canvas.LineTo(x2, y2);
            canvas.Stroke();
            var vertical = Math.Abs(x1 - x2) < 0.1F;
            if (vertical)
            {
                canvas.MoveTo(x1 - 5F, y1);
                canvas.LineTo(x1 + 5F, y1);
                canvas.MoveTo(x2 - 5F, y2);
                canvas.LineTo(x2 + 5F, y2);
                canvas.Stroke();
                DrawText(
                    canvas,
                    x1 - 28F,
                    (y1 + y2) / 2F,
                    7F,
                    label,
                    false);
            }
            else
            {
                canvas.MoveTo(x1, y1 - 5F);
                canvas.LineTo(x1, y1 + 5F);
                canvas.MoveTo(x2, y2 - 5F);
                canvas.LineTo(x2, y2 + 5F);
                canvas.Stroke();
                DrawText(
                    canvas,
                    (x1 + x2) / 2F - 12F,
                    y1 + 7F,
                    7F,
                    label,
                    false);
            }
        }

        private static void DrawNorthArrow(
            PdfContentByte canvas,
            float x,
            float y)
        {
            canvas.SetLineWidth(1.2F);
            canvas.MoveTo(x, y);
            canvas.LineTo(x, y + 88F);
            canvas.LineTo(x - 9F, y + 70F);
            canvas.MoveTo(x, y + 88F);
            canvas.LineTo(x + 9F, y + 70F);
            canvas.Stroke();
            DrawText(canvas, x - 5F, y + 98F, 10F, "N", true);
        }

        private static void DrawScale(
            PdfContentByte canvas,
            float x,
            float y)
        {
            canvas.SetLineWidth(0.6F);
            for (var index = 0; index < 5; index++)
            {
                if (index % 2 == 0)
                {
                    canvas.SetColorFill(BaseColor.BLACK);
                    canvas.Rectangle(
                        x + index * 18F,
                        y,
                        18F,
                        7F);
                    canvas.Fill();
                }
                else
                {
                    canvas.SetColorStroke(BaseColor.BLACK);
                    canvas.Rectangle(
                        x + index * 18F,
                        y,
                        18F,
                        7F);
                    canvas.Stroke();
                }
            }

            DrawText(canvas, x, y - 12F, 6F, "0  1  2  3  4  5 m", false);
        }

        private static void DrawPerson(
            PdfContentByte canvas,
            float x,
            float y)
        {
            canvas.SetLineWidth(1F);
            canvas.Circle(x, y + 67F, 7F);
            canvas.MoveTo(x, y + 60F);
            canvas.LineTo(x, y + 26F);
            canvas.MoveTo(x, y + 48F);
            canvas.LineTo(x - 13F, y + 35F);
            canvas.MoveTo(x, y + 48F);
            canvas.LineTo(x + 13F, y + 35F);
            canvas.MoveTo(x, y + 26F);
            canvas.LineTo(x - 10F, y);
            canvas.MoveTo(x, y + 26F);
            canvas.LineTo(x + 10F, y);
            canvas.Stroke();
        }

        private static void DrawLevel(
            PdfContentByte canvas,
            float x,
            float y,
            string label)
        {
            canvas.SetLineWidth(0.6F);
            canvas.MoveTo(x, y);
            canvas.LineTo(x + 30F, y);
            canvas.MoveTo(x + 15F, y);
            canvas.LineTo(x + 15F, y + 12F);
            canvas.Stroke();
            DrawText(canvas, x, y + 16F, 7F, label, false);
        }

        private static void ValidatePdf(
            string path,
            bool revisionB)
        {
            using (var reader = new PdfReader(path))
            {
                Require(
                    reader.NumberOfPages == Pages.Length,
                    "El fixture no tiene dos páginas.");
                for (var index = 0; index < Pages.Length; index++)
                {
                    var definition = Pages[index];
                    var page = reader.GetPageN(index + 1);
                    var size = reader.GetPageSize(index + 1);
                    var expectedWidth = revisionB
                        ? definition.WidthB
                        : definition.WidthA;
                    var expectedHeight = revisionB
                        ? definition.HeightB
                        : definition.HeightA;
                    Require(
                        Math.Abs(size.Width - expectedWidth) < 0.1F &&
                        Math.Abs(size.Height - expectedHeight) < 0.1F,
                        "MediaBox inesperado en página " +
                        (index + 1).ToString(
                            CultureInfo.InvariantCulture) +
                        ".");
                    var resources =
                        page.GetAsDict(PdfName.RESOURCES);
                    var xObjects = resources == null
                        ? null
                        : resources.GetAsDict(PdfName.XOBJECT);
                    if (xObjects != null)
                    {
                        foreach (var key in xObjects.Keys)
                        {
                            var stream = PdfReader.GetPdfObject(
                                xObjects.Get(key)) as PRStream;
                            Require(
                                stream == null ||
                                !PdfName.IMAGE.Equals(
                                    stream.GetAsName(
                                        PdfName.SUBTYPE)),
                                "El fixture contiene una imagen raster.");
                        }
                    }

                    var text = PdfTextExtractor.GetTextFromPage(
                        reader,
                        index + 1,
                        new SimpleTextExtractionStrategy());
                    Require(
                        !string.IsNullOrWhiteSpace(text) &&
                        text.IndexOf(
                            revisionB ? "REV. B" : "REV. A",
                            StringComparison.Ordinal) >= 0,
                        "No se extrajo texto vectorial de revisión.");
                }
            }

            Report.Add(
                "PASS fixture " +
                (revisionB ? "B" : "A") +
                ": 2 páginas vectoriales, texto y MediaBox.");
        }

        private static void RenderAndCompare(
            string run,
            string sourceA,
            string sourceB)
        {
            using (var documentA = PdfDocument.Load(sourceA))
            using (var documentB = PdfDocument.Load(sourceB))
            {
                for (var index = 0; index < Pages.Length; index++)
                {
                    var definition = Pages[index];
                    var widthA = (int)Math.Round(
                        definition.WidthA);
                    var heightA = (int)Math.Round(
                        definition.HeightA);
                    var widthB = (int)Math.Round(
                        definition.WidthB);
                    var heightB = (int)Math.Round(
                        definition.HeightB);
                    Require(
                        (long)widthA * heightA <= PixelCeiling &&
                        (long)widthB * heightB <= PixelCeiling,
                        "El render solicitado supera el límite de píxeles.");

                    using (var renderA = RenderPage(
                        documentA,
                        index,
                        widthA,
                        heightA))
                    using (var renderB = RenderPage(
                        documentB,
                        index,
                        widthB,
                        heightB))
                    {
                        var pageLabel =
                            (index + 1).ToString(
                                CultureInfo.InvariantCulture);
                        renderA.Save(
                            Path.Combine(
                                run,
                                "revision-A-page-" +
                                pageLabel +
                                ".png"),
                            ImageFormat.Png);
                        renderB.Save(
                            Path.Combine(
                                run,
                                "revision-B-page-" +
                                pageLabel +
                                ".png"),
                            ImageFormat.Png);

                        var theoreticalOffsetX = (int)Math.Round(
                            definition.TranslationX);
                        var theoreticalOffsetY = (int)Math.Round(
                            definition.HeightB -
                            definition.HeightA -
                            definition.TranslationY);
                        int offsetX;
                        int offsetY;
                        FindBestRasterOffset(
                            renderA,
                            renderB,
                            definition,
                            theoreticalOffsetX,
                            theoreticalOffsetY,
                            out offsetX,
                            out offsetY);
                        long changedPixels;
                        Dictionary<string, int> expectedHits;
                        using (var overlay = CreateReferenceOverlay(
                            renderA,
                            renderB,
                            offsetX,
                            offsetY,
                            definition,
                            out changedPixels,
                            out expectedHits))
                        {
                            overlay.Save(
                                Path.Combine(
                                    run,
                                    "page-" +
                                    pageLabel +
                                    "-reference-overlay.png"),
                                ImageFormat.Png);
                            Require(
                                changedPixels > 250,
                                "El fixture no produce diferencias útiles.");
                            Require(
                                changedPixels <
                                (long)widthA * heightA / 12L,
                                "Las diferencias no están localizadas.");
                            foreach (var region in
                                definition.ExpectedChanges)
                            {
                                Require(
                                    expectedHits.ContainsKey(
                                        region.Name) &&
                                    expectedHits[region.Name] >= 8,
                                    "No hay cambio visible en la región " +
                                    region.Name +
                                    ".");
                            }

                            Report.Add(
                                "PASS render página " +
                                pageLabel +
                                ": " +
                                changedPixels.ToString(
                                    CultureInfo.InvariantCulture) +
                                " píxeles distintos tras alineación conocida; " +
                                "offset imagen B=(" +
                                offsetX.ToString(
                                    CultureInfo.InvariantCulture) +
                                ", " +
                                offsetY.ToString(
                                    CultureInfo.InvariantCulture) +
                                "), estimación inicial=(" +
                                theoreticalOffsetX.ToString(
                                    CultureInfo.InvariantCulture) +
                                ", " +
                                theoreticalOffsetY.ToString(
                                    CultureInfo.InvariantCulture) +
                                ").");
                        }
                    }
                }
            }
        }

        private static Bitmap RenderPage(
            PdfDocument document,
            int pageIndex,
            int width,
            int height)
        {
            using (var rendered = document.Render(
                pageIndex,
                width,
                height,
                72F,
                72F,
                PdfRenderFlags.Annotations |
                PdfRenderFlags.LcdText |
                PdfRenderFlags.LimitImageCacheSize))
            {
                var result = new Bitmap(
                    width,
                    height,
                    PixelFormat.Format24bppRgb);
                using (var graphics = Graphics.FromImage(result))
                {
                    graphics.Clear(Color.White);
                    graphics.CompositingMode =
                        CompositingMode.SourceCopy;
                    graphics.DrawImageUnscaled(rendered, 0, 0);
                }

                return result;
            }
        }

        private static Bitmap CreateReferenceOverlay(
            Bitmap imageA,
            Bitmap imageB,
            int offsetX,
            int offsetY,
            PageDefinition definition,
            out long changedPixels,
            out Dictionary<string, int> expectedHits)
        {
            var result = new Bitmap(
                imageA.Width,
                imageA.Height,
                PixelFormat.Format24bppRgb);
            changedPixels = 0L;
            expectedHits = new Dictionary<string, int>(
                StringComparer.Ordinal);
            foreach (var region in definition.ExpectedChanges)
            {
                expectedHits.Add(region.Name, 0);
            }

            for (var y = 0; y < imageA.Height; y++)
            {
                for (var x = 0; x < imageA.Width; x++)
                {
                    var colorA = imageA.GetPixel(x, y);
                    var bx = x + offsetX;
                    var by = y + offsetY;
                    var colorB = bx >= 0 &&
                        by >= 0 &&
                        bx < imageB.Width &&
                        by < imageB.Height
                        ? imageB.GetPixel(bx, by)
                        : Color.White;
                    var grayA = Gray(colorA);
                    var grayB = Gray(colorB);
                    var difference = Math.Abs(grayA - grayB);
                    Color output;
                    if (difference >= 26)
                    {
                        changedPixels++;
                        if (grayA < grayB)
                        {
                            output = Color.FromArgb(
                                40,
                                105,
                                205);
                        }
                        else
                        {
                            output = Color.FromArgb(
                                238,
                                79,
                                46);
                        }

                        foreach (var region in
                            definition.ExpectedChanges)
                        {
                            if (ContainsPdfPoint(
                                region,
                                x,
                                y,
                                imageA.Height))
                            {
                                expectedHits[region.Name]++;
                            }
                        }
                    }
                    else
                    {
                        var value = Math.Min(
                            247,
                            Math.Max(70, (grayA + grayB) / 2));
                        output = Color.FromArgb(
                            value,
                            value,
                            value);
                    }

                    result.SetPixel(x, y, output);
                }
            }

            using (var graphics = Graphics.FromImage(result))
            using (var pen = new Pen(
                Color.FromArgb(165, 255, 126, 0),
                1F))
            {
                pen.DashStyle = DashStyle.Dash;
                foreach (var region in definition.ExpectedChanges)
                {
                    var left = (int)Math.Floor(region.Left);
                    var top = (int)Math.Floor(
                        imageA.Height - region.Top);
                    var width = (int)Math.Ceiling(
                        region.Right - region.Left);
                    var height = (int)Math.Ceiling(
                        region.Top - region.Bottom);
                    graphics.DrawRectangle(
                        pen,
                        left,
                        top,
                        width,
                        height);
                }
            }

            return result;
        }

        private static void FindBestRasterOffset(
            Bitmap imageA,
            Bitmap imageB,
            PageDefinition definition,
            int initialX,
            int initialY,
            out int bestX,
            out int bestY)
        {
            bestX = initialX;
            bestY = initialY;
            long bestScore = long.MaxValue;
            for (var candidateY = initialY - 10;
                candidateY <= initialY + 10;
                candidateY++)
            {
                for (var candidateX = initialX - 10;
                    candidateX <= initialX + 10;
                    candidateX++)
                {
                    long score = 0L;
                    long samples = 0L;
                    for (var y = 4;
                        y < imageA.Height - 4;
                        y += 4)
                    {
                        for (var x = 4;
                            x < imageA.Width - 4;
                            x += 4)
                        {
                            if (IsInsideExpectedChange(
                                definition,
                                x,
                                y,
                                imageA.Height))
                            {
                                continue;
                            }

                            var bx = x + candidateX;
                            var by = y + candidateY;
                            if (bx < 0 ||
                                by < 0 ||
                                bx >= imageB.Width ||
                                by >= imageB.Height)
                            {
                                continue;
                            }

                            var difference = Math.Abs(
                                Gray(imageA.GetPixel(x, y)) -
                                Gray(imageB.GetPixel(bx, by)));
                            score += Math.Min(80, difference);
                            samples++;
                        }
                    }

                    if (samples == 0L)
                    {
                        continue;
                    }

                    var normalized =
                        score * 1000000L / samples;
                    if (normalized < bestScore)
                    {
                        bestScore = normalized;
                        bestX = candidateX;
                        bestY = candidateY;
                    }
                }
            }
        }

        private static bool IsInsideExpectedChange(
            PageDefinition definition,
            int imageX,
            int imageY,
            int imageHeight)
        {
            foreach (var region in definition.ExpectedChanges)
            {
                if (ContainsPdfPoint(
                    region,
                    imageX,
                    imageY,
                    imageHeight))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsPdfPoint(
            ChangeRegion region,
            int imageX,
            int imageY,
            int imageHeight)
        {
            var pdfY = imageHeight - imageY;
            const float margin = 16F;
            return imageX >= region.Left - margin &&
                imageX <= region.Right + margin &&
                pdfY >= region.Bottom - margin &&
                pdfY <= region.Top + margin;
        }

        private static int Gray(Color color)
        {
            return (color.R * 30 +
                color.G * 59 +
                color.B * 11) / 100;
        }

        private static void ValidateReferenceCancellation(
            string run)
        {
            var forbidden = Path.Combine(
                run,
                "NO-DEBE-EXISTIR-cancelado.png");
            if (File.Exists(forbidden))
            {
                File.Delete(forbidden);
            }

            var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var cancelled = false;
            try
            {
                cancellation.Token.ThrowIfCancellationRequested();
                using (var bitmap = new Bitmap(16, 16))
                {
                    bitmap.Save(forbidden, ImageFormat.Png);
                }
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            finally
            {
                cancellation.Dispose();
            }

            Require(cancelled, "El oráculo no respetó cancelación.");
            Require(
                !File.Exists(forbidden),
                "La cancelación publicó un artefacto parcial.");
            Report.Add(
                "PASS cancelación del oráculo sin salida parcial.");
        }

        private static void WriteManifest(string run)
        {
            var lines = new List<string>
            {
                "PDF LIGERO / FIXTURE COMPARACIÓN DE PLANOS",
                "Unidades: puntos PDF; origen inferior izquierdo.",
                "La transformación no debe pasarse al motor.",
                string.Empty
            };
            foreach (var page in Pages)
            {
                lines.Add(
                    "Página " +
                    page.PageNumber.ToString(
                        CultureInfo.InvariantCulture));
                lines.Add(
                    "A=" +
                    page.WidthA.ToString(
                        "0.###",
                        CultureInfo.InvariantCulture) +
                    "x" +
                    page.HeightA.ToString(
                        "0.###",
                        CultureInfo.InvariantCulture));
                lines.Add(
                    "B=" +
                    page.WidthB.ToString(
                        "0.###",
                        CultureInfo.InvariantCulture) +
                    "x" +
                    page.HeightB.ToString(
                        "0.###",
                        CultureInfo.InvariantCulture));
                lines.Add(
                    "Transformación contenido B: dx=" +
                    page.TranslationX.ToString(
                        "0.###",
                        CultureInfo.InvariantCulture) +
                    "; dy=" +
                    page.TranslationY.ToString(
                        "0.###",
                        CultureInfo.InvariantCulture) +
                    "; escala=1");
                foreach (var region in page.ExpectedChanges)
                {
                    lines.Add(
                        "Cambio esperado: " +
                        region.Name +
                        " [" +
                        region.Left.ToString(
                            "0.###",
                            CultureInfo.InvariantCulture) +
                        "," +
                        region.Bottom.ToString(
                            "0.###",
                            CultureInfo.InvariantCulture) +
                        "," +
                        region.Right.ToString(
                            "0.###",
                            CultureInfo.InvariantCulture) +
                        "," +
                        region.Top.ToString(
                            "0.###",
                            CultureInfo.InvariantCulture) +
                        "]");
                }

                lines.Add(string.Empty);
            }

            File.WriteAllLines(
                Path.Combine(run, "fixture-manifest.txt"),
                lines.ToArray(),
                new UTF8Encoding(true));
        }

        private static string HashFile(string path)
        {
            using (var input = File.OpenRead(path))
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(
                        sha.ComputeHash(input))
                    .Replace("-", string.Empty);
            }
        }

        private static string FormatMiB(long bytes)
        {
            return (bytes / 1048576D).ToString(
                "0.0",
                CultureInfo.InvariantCulture) +
                " MiB";
        }

        private static void Require(
            bool condition,
            string message)
        {
            if (!condition)
            {
                throw new InvalidDataException(message);
            }
        }

        private static void WriteReport(string run)
        {
            try
            {
                File.WriteAllLines(
                    Path.Combine(run, "qa-report.txt"),
                    Report.ToArray(),
                    new UTF8Encoding(true));
            }
            catch
            {
            }
        }
    }
}

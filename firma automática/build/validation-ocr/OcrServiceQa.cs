using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using FirmaAutomatica;
using iTextSharp.text;
using iTextSharp.text.pdf;
using PdfiumViewer;
using DrawingImage = System.Drawing.Image;
using PdfDocument = PdfiumViewer.PdfDocument;
using PdfImage = iTextSharp.text.Image;
using PdfRectangle = iTextSharp.text.Rectangle;
using PdfTextExtractor = iTextSharp.text.pdf.parser.PdfTextExtractor;

internal static class OcrServiceQa
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 1)
            {
                throw new ArgumentException(
                    "Se necesita la carpeta de salida.");
            }

            var outputDirectory = Path.Combine(
                Path.GetFullPath(args[0]),
                "caso OCR rápido con espacios y acentos");
            Directory.CreateDirectory(outputDirectory);
            var sourcePath = Path.Combine(
                outputDirectory,
                "planos escaneados - Málaga.pdf");
            var resultPath = Path.Combine(
                outputDirectory,
                "planos escaneados - Málaga OCR.pdf");
            TryDelete(sourcePath);
            TryDelete(resultPath);
            CreateFixture(sourcePath);
            var sourceHash = Hash(sourcePath);

            var availability = PdfOcrService.GetAvailability();
            Require(
                availability.IsAvailable,
                "No se encontro el runtime OCR.");
            Require(
                availability.ExecutablePath.StartsWith(
                    Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory,
                        "ocr"),
                    StringComparison.OrdinalIgnoreCase),
                "La prueba no esta usando el runtime OCR distribuido.");
            Require(
                Contains(availability.AvailableLanguages, "spa") &&
                Contains(availability.AvailableLanguages, "eng") &&
                Contains(availability.AvailableLanguages, "osd"),
                "Faltan los modelos spa, eng u osd.");

            var settings = new PdfOcrSettings();
            settings.AutoOrient = true;
            settings.AutoDeskew = true;
            settings.OcrDpi = 220;
            settings.AnalysisDpi = 110;
            settings.SelectedPages = new[] { 1, 2 };
            var analysis = PdfOcrService.Analyze(
                sourcePath,
                settings,
                null,
                CancellationToken.None);
            Require(analysis.PageCount == 3, "Conteo de paginas incorrecto.");
            Require(analysis.OcrPageCount == 2, "La seleccion OCR no se respeto.");
            Require(
                analysis.Pages[2].HasSearchableText &&
                !analysis.Pages[2].NeedsOcr,
                "La pagina vectorial deberia conservarse sin OCR.");
            Require(
                analysis.Pages[1]
                    .SuggestedClockwiseRotationDegrees == 270,
                "La orientacion automatica no corrigio la pagina girada.");

            var instructions =
                PdfOcrService.CreateDefaultInstructions(analysis);
            instructions[0].Process = true;
            instructions[0].ClockwiseRotationDegrees = 0;
            instructions[0].ApplyDeskew = true;
            instructions[0].DeskewDegrees = -2F;
            instructions[1].Process = true;
            instructions[1].ClockwiseRotationDegrees = 270;
            instructions[1].ApplyDeskew = false;
            instructions[2].Process = false;

            var preview = PdfOcrService.RenderPreviewPng(
                sourcePath,
                instructions[0],
                100,
                CancellationToken.None);
            Require(preview.Length > 10000, "Vista previa OCR vacia.");
            File.WriteAllBytes(
                Path.Combine(outputDirectory, "preview-pagina-1.png"),
                preview);

            var started = DateTime.UtcNow;
            var result = PdfOcrService.Process(
                sourcePath,
                resultPath,
                analysis,
                instructions,
                settings,
                null,
                CancellationToken.None);
            var elapsed = DateTime.UtcNow - started;
            Require(File.Exists(resultPath), "No se creo la salida OCR.");
            Require(result.ProcessedPageCount == 2, "Procesadas incorrectas.");
            Require(result.RecognizedWordCount > 30, "Muy pocas palabras OCR.");
            Require(Hash(sourcePath) == sourceHash, "El original fue modificado.");

            using (var reader = new PdfReader(resultPath))
            {
                Require(reader.NumberOfPages == 3, "Se perdieron paginas.");
                var first = PdfTextExtractor.GetTextFromPage(reader, 1);
                var second = PdfTextExtractor.GetTextFromPage(reader, 2);
                var third = PdfTextExtractor.GetTextFromPage(reader, 3);
                Require(
                    ContainsText(first, "ARQUITECTURA") &&
                    ContainsText(first, "LICENCIA"),
                    "La primera capa OCR no contiene el texto esperado.");
                Require(
                    ContainsText(second, "URBANISTICA") &&
                    ContainsText(second, "TOLEDO"),
                    "La segunda capa OCR no contiene el texto esperado.");
                Require(
                    ContainsText(third, "PAGINA VECTORIAL"),
                    "No se conservo el texto vectorial.");
            }

            using (var document = PdfDocument.Load(resultPath))
            {
                for (var page = 0; page < document.PageCount; page++)
                {
                    using (var image = document.Render(
                        page,
                        992,
                        1403,
                        120,
                        120,
                        PdfRenderFlags.Annotations |
                        PdfRenderFlags.LcdText |
                        PdfRenderFlags.LimitImageCacheSize))
                    {
                        image.Save(
                            Path.Combine(
                                outputDirectory,
                                "resultado-pagina-" +
                                (page + 1).ToString("D2") +
                                ".png"),
                            ImageFormat.Png);
                    }
                }
            }

            var report = new StringBuilder();
            report.AppendLine("PASS: motor OCR local.");
            report.AppendLine("Runtime: " + availability.ExecutablePath);
            report.AppendLine("Idiomas: " + string.Join(", ", availability.AvailableLanguages));
            report.AppendLine("Paginas OCR: " + result.ProcessedPageCount);
            report.AppendLine("Palabras: " + result.RecognizedWordCount);
            report.AppendLine(
                "Deskew propuesto p1: " +
                analysis.Pages[0].SuggestedDeskewDegrees.ToString("0.00") +
                " grados (" +
                analysis.Pages[0].DeskewConfidence.ToString("0.00") +
                "%)");
            report.AppendLine(
                "Orientacion propuesta p2: " +
                analysis.Pages[1]
                    .SuggestedClockwiseRotationDegrees +
                " grados (confianza " +
                analysis.Pages[1].OrientationConfidence.ToString("0.00") +
                ")");
            report.AppendLine("Duracion: " + elapsed.TotalSeconds.ToString("0.00") + " s");
            report.AppendLine("Original SHA-256 intacto: " + sourceHash);
            report.AppendLine("Ruta con espacios y acentos: PASS");
            File.WriteAllText(
                Path.Combine(outputDirectory, "qa-report.txt"),
                report.ToString(),
                Encoding.UTF8);
            Console.WriteLine(report.ToString());
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void CreateFixture(string path)
    {
        using (var first = CreateTextPage(
            "ARQUITECTURA Y LICENCIA URBANISTICA",
            2F))
        using (var secondBase = CreateTextPage(
            "LICENCIA URBANISTICA AYUNTAMIENTO DE TOLEDO",
            0F))
        {
            secondBase.RotateFlip(RotateFlipType.Rotate90FlipNone);
            using (var stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            {
                var document = new Document(
                    PageSize.A4,
                    0F,
                    0F,
                    0F,
                    0F);
                var writer = PdfWriter.GetInstance(document, stream);
                document.Open();
                AddScannedPage(document, writer, first);
                document.NewPage();
                AddScannedPage(document, writer, secondBase);
                document.NewPage();
                var font = BaseFont.CreateFont(
                    BaseFont.HELVETICA,
                    BaseFont.CP1252,
                    BaseFont.EMBEDDED);
                var canvas = writer.DirectContent;
                canvas.BeginText();
                canvas.SetFontAndSize(font, 16F);
                canvas.SetTextMatrix(70F, 730F);
                canvas.ShowText(
                    "PAGINA VECTORIAL - NO DEBE REPROCESARSE");
                canvas.EndText();
                document.Close();
            }
        }
    }

    private static Bitmap CreateTextPage(string heading, float rotation)
    {
        var baseImage = new Bitmap(
            1240,
            1754,
            PixelFormat.Format24bppRgb);
        baseImage.SetResolution(150F, 150F);
        using (var graphics = Graphics.FromImage(baseImage))
        {
            graphics.Clear(Color.White);
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.TextRenderingHint =
                System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using (var headingFont = new System.Drawing.Font(
                "Arial",
                32F,
                FontStyle.Bold,
                GraphicsUnit.Pixel))
            using (var bodyFont = new System.Drawing.Font(
                "Arial",
                23F,
                FontStyle.Regular,
                GraphicsUnit.Pixel))
            using (var pen = new Pen(Color.Black, 2F))
            {
                graphics.DrawString(
                    heading,
                    headingFont,
                    Brushes.Black,
                    new PointF(90F, 120F));
                graphics.DrawLine(pen, 90F, 175F, 1130F, 175F);
                for (var line = 0; line < 28; line++)
                {
                    graphics.DrawString(
                        "Proyecto de arquitectura, memoria técnica y licencia " +
                        "de obras número " + (line + 1) +
                        ". Documento municipal para comprobación.",
                        bodyFont,
                        Brushes.Black,
                        new PointF(90F, 220F + line * 47F));
                }
            }
        }

        if (Math.Abs(rotation) < 0.01F)
        {
            return baseImage;
        }

        var rotated = new Bitmap(
            baseImage.Width,
            baseImage.Height,
            PixelFormat.Format24bppRgb);
        rotated.SetResolution(150F, 150F);
        using (var graphics = Graphics.FromImage(rotated))
        {
            graphics.Clear(Color.White);
            graphics.InterpolationMode =
                InterpolationMode.HighQualityBicubic;
            graphics.TranslateTransform(
                baseImage.Width / 2F,
                baseImage.Height / 2F);
            graphics.RotateTransform(rotation);
            graphics.TranslateTransform(
                -baseImage.Width / 2F,
                -baseImage.Height / 2F);
            graphics.DrawImageUnscaled(baseImage, 0, 0);
        }

        baseImage.Dispose();
        return rotated;
    }

    private static void AddScannedPage(
        Document document,
        PdfWriter writer,
        Bitmap bitmap)
    {
        using (var imageStream = new MemoryStream())
        {
            bitmap.Save(imageStream, ImageFormat.Jpeg);
            var image = PdfImage.GetInstance(imageStream.ToArray());
            image.SetAbsolutePosition(0F, 0F);
            image.ScaleAbsolute(PageSize.A4.Width, PageSize.A4.Height);
            writer.DirectContent.AddImage(image);
        }
    }

    private static string Hash(string path)
    {
        using (var input = File.OpenRead(path))
        using (var sha = SHA256.Create())
        {
            return BitConverter.ToString(sha.ComputeHash(input))
                .Replace("-", string.Empty);
        }
    }

    private static bool Contains(IList<string> values, string value)
    {
        foreach (var item in values)
        {
            if (string.Equals(
                item,
                value,
                StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsText(string text, string value)
    {
        return (text ?? string.Empty).IndexOf(
            value,
            StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
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

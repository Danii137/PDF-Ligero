using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FirmaAutomatica;
using iTextSharp.text;
using iTextSharp.text.pdf;
using PdfiumViewer;
using DrawingImage = System.Drawing.Image;
using PdfDocument = PdfiumViewer.PdfDocument;
using PdfImage = iTextSharp.text.Image;
using PdfRectangle = iTextSharp.text.Rectangle;
using PdfTextExtractor =
    iTextSharp.text.pdf.parser.PdfTextExtractor;

internal static class OcrStressQa
{
    private const int MaximumPixels = 16000000;
    private static readonly StringBuilder Report = new StringBuilder();
    private static readonly List<string> Failures = new List<string>();
    private static string ExpectedTesseractPath;
    private static string SourcePath;
    private static string SourceHash;
    private static string RunDirectory;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 1)
            {
                throw new ArgumentException(
                    "Se necesita una carpeta de ejecucion.");
            }

            RunDirectory = Path.GetFullPath(args[0]);
            Directory.CreateDirectory(RunDirectory);
            ExpectedTesseractPath = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "ocr",
                "tesseract.exe"));

            Report.AppendLine("QA OCR STRESS / CANCELACION / A0");
            Report.AppendLine("Inicio UTC: " +
                DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            Report.AppendLine("Motor esperado: " + ExpectedTesseractPath);
            Report.AppendLine("Limite configurado: " +
                MaximumPixels.ToString(CultureInfo.InvariantCulture) +
                " px");
            Report.AppendLine();

            var availability = PdfOcrService.GetAvailability();
            Require(
                availability.IsAvailable,
                "El runtime OCR no esta disponible.");
            Require(
                PathsEqual(
                    availability.ExecutablePath,
                    ExpectedTesseractPath),
                "La prueba no usa el runtime OCR embebido.");
            Require(
                GetAllTesseractProcessIds().Count == 0,
                "Ya habia procesos tesseract antes de la prueba.");

            SourcePath = Path.Combine(
                RunDirectory,
                "plano-A0-escaneado-original.pdf");
            CreateA0ScannedFixture(SourcePath);
            SourceHash = HashFile(SourcePath);
            Report.AppendLine("Fixture: " + SourcePath);
            Report.AppendLine("SHA-256 original: " + SourceHash);
            Report.AppendLine("Imagenes originales: " +
                string.Join(" | ", GetImageFingerprints(SourcePath)));
            Report.AppendLine();

            RunCase(
                "Cancelacion durante Analyze",
                TestAnalyzeCancellation);
            RunCase(
                "Cancelacion durante Process",
                TestProcessCancellation);
            RunCase(
                "A0 completo con limite 16 Mpx",
                TestA0Completion);
            RunCase(
                "Cero procesos tesseract al finalizar",
                TestNoTesseractProcesses);

            RequireSourceUnchanged();
            Report.AppendLine();
            Report.AppendLine("PeakWorkingSet harness: " +
                FormatMiB(
                    Process.GetCurrentProcess().PeakWorkingSet64));
            Report.AppendLine("Fin UTC: " +
                DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            Report.AppendLine(
                Failures.Count == 0
                    ? "RESULTADO GLOBAL: PASS"
                    : "RESULTADO GLOBAL: FAIL (" +
                      Failures.Count.ToString(
                          CultureInfo.InvariantCulture) +
                      ")");
        }
        catch (Exception ex)
        {
            Failures.Add("FATAL: " + ex);
            Report.AppendLine("FATAL: " + ex);
            BestEffortWaitForNoTesseract(5000);
        }
        finally
        {
            try
            {
                File.WriteAllText(
                    Path.Combine(RunDirectory, "qa-report.txt"),
                    Report.ToString(),
                    new UTF8Encoding(true));
            }
            catch
            {
            }

            Console.WriteLine(Report.ToString());
        }

        return Failures.Count == 0 ? 0 : 1;
    }

    private static void RunCase(string name, Action test)
    {
        Report.AppendLine("CASO: " + name);
        var started = Stopwatch.StartNew();
        try
        {
            test();
            Report.AppendLine("Estado: PASS");
        }
        catch (Exception ex)
        {
            var message = ex.GetType().Name + ": " + ex.Message;
            Failures.Add(name + " -> " + message);
            Report.AppendLine("Estado: FAIL");
            Report.AppendLine("Fallo: " + message);
        }
        finally
        {
            started.Stop();
            Report.AppendLine("Duracion caso: " +
                started.Elapsed.TotalSeconds.ToString(
                    "0.00",
                    CultureInfo.InvariantCulture) +
                " s");
            Report.AppendLine();
        }
    }

    private static void TestAnalyzeCancellation()
    {
        RequireSourceUnchanged();
        var settings = CreateSettings();
        settings.AutoOrient = true;
        settings.AutoDeskew = false;
        settings.AnalysisDpi = 180;
        var baseline = GetEmbeddedTesseractProcessIds();
        var osdBefore = SnapshotFiles(
            Path.GetTempPath(),
            "pdf-ligero-osd-*.png");
        var cts = new CancellationTokenSource();
        Task<PdfOcrAnalysis> task = null;
        var observation = new TesseractObservation();
        try
        {
            task = Task.Factory.StartNew(
                delegate
                {
                    return PdfOcrService.Analyze(
                        SourcePath,
                        settings,
                        null,
                        cts.Token);
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);

            observation = WaitForEmbeddedTesseract(
                baseline,
                60000);
            Require(
                observation.Pid > 0,
                "Analyze no llego a iniciar Tesseract.");
            cts.Cancel();
            RequireTaskCanceled(
                task,
                30000,
                "Analyze");
            Require(
                WaitForProcessExit(observation.Pid, 5000),
                "Tesseract siguio vivo tras cancelar Analyze.");

            var osdAfter = SnapshotFiles(
                Path.GetTempPath(),
                "pdf-ligero-osd-*.png");
            var newOsd = Except(osdAfter, osdBefore);
            var residue = new List<string>();
            foreach (var path in newOsd)
            {
                if (File.Exists(path))
                {
                    residue.Add(path);
                }
            }

            Report.AppendLine("PID cancelado: " +
                observation.Pid.ToString(
                    CultureInfo.InvariantCulture));
            Report.AppendLine("PNG temporal observado: " +
                EmptyAsDash(observation.InputPngPath));
            Report.AppendLine("Temporales OSD residuales: " +
                residue.Count.ToString(
                    CultureInfo.InvariantCulture));
            Require(
                residue.Count == 0,
                "P0 privacidad: Analyze dejo PNG OSD temporal: " +
                string.Join(", ", residue));
            RequireSourceUnchanged();
        }
        finally
        {
            cts.Cancel();
            cts.Dispose();
            BestEffortWaitTask(task, 5000);
            BestEffortWaitForNoEmbeddedTesseract(5000);
            CleanupSafeOsdResidue(osdBefore);
        }
    }

    private static void TestProcessCancellation()
    {
        RequireSourceUnchanged();
        var settings = CreateSettings();
        settings.AutoOrient = false;
        settings.AutoDeskew = false;
        var analysis = PdfOcrService.Analyze(
            SourcePath,
            settings,
            null,
            CancellationToken.None);
        var instructions =
            PdfOcrService.CreateDefaultInstructions(analysis);
        var outputPath = Path.Combine(
            RunDirectory,
            "NO-DEBE-EXISTIR-cancelado.pdf");
        TryDelete(outputPath);
        var outputTempsBefore = SnapshotFiles(
            RunDirectory,
            ".*.ocr.tmp");
        var workingBefore = SnapshotDirectories(GetOcrTempRoot());
        var baseline = GetEmbeddedTesseractProcessIds();
        var cts = new CancellationTokenSource();
        Task<PdfOcrResult> task = null;
        var observation = new TesseractObservation();
        try
        {
            task = Task.Factory.StartNew(
                delegate
                {
                    return PdfOcrService.Process(
                        SourcePath,
                        outputPath,
                        analysis,
                        instructions,
                        settings,
                        null,
                        cts.Token);
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);

            observation = WaitForEmbeddedTesseract(
                baseline,
                60000);
            Require(
                observation.Pid > 0,
                "Process no llego a iniciar Tesseract.");
            cts.Cancel();
            RequireTaskCanceled(
                task,
                30000,
                "Process");
            Require(
                WaitForProcessExit(observation.Pid, 5000),
                "Tesseract siguio vivo tras cancelar Process.");

            var newWorking = Except(
                SnapshotDirectories(GetOcrTempRoot()),
                workingBefore);
            var newOutputTemps = Except(
                SnapshotFiles(
                    RunDirectory,
                    ".*.ocr.tmp"),
                outputTempsBefore);
            var residualFiles =
                EnumerateFilesSafely(newWorking);
            Report.AppendLine("PID cancelado: " +
                observation.Pid.ToString(
                    CultureInfo.InvariantCulture));
            Report.AppendLine("PNG temporal observado: " +
                EmptyAsDash(observation.InputPngPath));
            Report.AppendLine("Dimensiones PNG observado: " +
                FormatDimensions(observation));
            Report.AppendLine("Salida final existe: " +
                File.Exists(outputPath).ToString());
            Report.AppendLine("Directorios OCR residuales: " +
                newWorking.Count.ToString(
                    CultureInfo.InvariantCulture));
            Report.AppendLine("Archivos OCR residuales: " +
                (residualFiles.Count == 0
                    ? "-"
                    : string.Join(", ", residualFiles)));
            Report.AppendLine("Temporales de salida residuales: " +
                newOutputTemps.Count.ToString(
                    CultureInfo.InvariantCulture));

            Require(
                !File.Exists(outputPath),
                "Process publico una salida pese a cancelarse.");
            Require(
                newOutputTemps.Count == 0,
                "P0: Process dejo un temporal junto a la salida: " +
                string.Join(", ", newOutputTemps));
            Require(
                newWorking.Count == 0,
                "P0 privacidad: Process dejo datos OCR temporales: " +
                string.Join(", ", newWorking));
            RequireSourceUnchanged();
        }
        finally
        {
            cts.Cancel();
            cts.Dispose();
            BestEffortWaitTask(task, 5000);
            BestEffortWaitForNoEmbeddedTesseract(5000);
            CleanupSafeWorkingResidue(workingBefore);
            TryDelete(outputPath);
        }
    }

    private static void TestA0Completion()
    {
        RequireSourceUnchanged();
        var settings = CreateSettings();
        settings.AutoOrient = false;
        settings.AutoDeskew = false;
        settings.OcrDpi = 300;
        settings.MaximumPixelsPerPage = MaximumPixels;
        var analysis = PdfOcrService.Analyze(
            SourcePath,
            settings,
            null,
            CancellationToken.None);
        var instructions =
            PdfOcrService.CreateDefaultInstructions(analysis);
        var outputPath = Path.Combine(
            RunDirectory,
            "plano-A0-escaneado-OCR.pdf");
        TryDelete(outputPath);
        var sourceImages = GetImageFingerprints(SourcePath);
        var sourcePageSize = GetPageSize(SourcePath);
        var workingBefore = SnapshotDirectories(GetOcrTempRoot());
        var outputTempsBefore = SnapshotFiles(
            RunDirectory,
            ".*.ocr.tmp");
        var baseline = GetEmbeddedTesseractProcessIds();
        var observation = new TesseractObservation();
        var started = Stopwatch.StartNew();
        var task = Task.Factory.StartNew(
            delegate
            {
                return PdfOcrService.Process(
                    SourcePath,
                    outputPath,
                    analysis,
                    instructions,
                    settings,
                    null,
                    CancellationToken.None);
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        while (!task.IsCompleted)
        {
            ObserveEmbeddedTesseract(
                baseline,
                observation);
            Thread.Sleep(10);
        }

        ObserveEmbeddedTesseract(
            baseline,
            observation);
        PdfOcrResult result;
        try
        {
            result = task.Result;
        }
        catch (AggregateException ex)
        {
            throw ex.Flatten().InnerException ?? ex;
        }
        finally
        {
            started.Stop();
        }

        Require(
            WaitForNoEmbeddedTesseract(5000),
            "Quedo Tesseract vivo tras el A0 completo.");
        Require(
            File.Exists(outputPath),
            "No se creo la salida OCR A0.");
        Require(
            result.ProcessedPageCount == 1,
            "El A0 no proceso exactamente una pagina.");
        Require(
            result.RecognizedWordCount >= 20,
            "El A0 reconocio menos de 20 palabras.");
        Require(
            observation.Width > 0 &&
            observation.Height > 0,
            "No se pudieron observar las dimensiones del render OCR.");
        var renderedPixels =
            (long)observation.Width * observation.Height;
        Require(
            renderedPixels <= MaximumPixels,
            "El render A0 excedio 16 Mpx: " +
            renderedPixels.ToString(CultureInfo.InvariantCulture));
        Require(
            renderedPixels >= 14000000,
            "El render A0 no ejercito de forma efectiva el limite: " +
            renderedPixels.ToString(CultureInfo.InvariantCulture));

        var outputImages = GetImageFingerprints(outputPath);
        RequireStringListsEqual(
            sourceImages,
            outputImages,
            "El XObject raster original fue sustituido o recomprimido.");
        var outputPageSize = GetPageSize(outputPath);
        Require(
            Math.Abs(sourcePageSize.Width - outputPageSize.Width) <
                0.01F &&
            Math.Abs(sourcePageSize.Height - outputPageSize.Height) <
                0.01F,
            "La salida no conserva el MediaBox A0.");
        Require(
            Math.Abs(outputPageSize.Width - 2383.94F) < 1F &&
            Math.Abs(outputPageSize.Height - 3370.39F) < 1F,
            "El fixture o la salida no tienen formato A0.");

        var sourcePixelHash = RenderPixelHash(
            SourcePath,
            1200,
            1697,
            Path.Combine(
                RunDirectory,
                "A0-original-preview.png"));
        var outputPixelHash = RenderPixelHash(
            outputPath,
            1200,
            1697,
            Path.Combine(
                RunDirectory,
                "A0-OCR-preview.png"));
        Require(
            string.Equals(
                sourcePixelHash,
                outputPixelHash,
                StringComparison.Ordinal),
            "La apariencia visible del A0 cambio al anadir OCR.");
        Require(
            OutputContainsExpectedText(outputPath),
            "La salida A0 no contiene texto OCR esperado.");
        RequireSourceUnchanged();

        var newWorking = Except(
            SnapshotDirectories(GetOcrTempRoot()),
            workingBefore);
        var newOutputTemps = Except(
            SnapshotFiles(
                RunDirectory,
                ".*.ocr.tmp"),
            outputTempsBefore);
        Require(
            newWorking.Count == 0,
            "El A0 completo dejo directorios temporales: " +
            string.Join(", ", newWorking));
        Require(
            newOutputTemps.Count == 0,
            "El A0 completo dejo temporales de salida: " +
            string.Join(", ", newOutputTemps));

        Report.AppendLine("Render OCR observado: " +
            observation.Width.ToString(
                CultureInfo.InvariantCulture) +
            " x " +
            observation.Height.ToString(
                CultureInfo.InvariantCulture) +
            " = " +
            renderedPixels.ToString(
                CultureInfo.InvariantCulture) +
            " px (" +
            (renderedPixels / 1000000D).ToString(
                "0.000",
                CultureInfo.InvariantCulture) +
            " Mpx)");
        Report.AppendLine("Duracion Process A0: " +
            started.Elapsed.TotalSeconds.ToString(
                "0.00",
                CultureInfo.InvariantCulture) +
            " s");
        Report.AppendLine("PeakWorkingSet harness: " +
            FormatMiB(
                Process.GetCurrentProcess().PeakWorkingSet64));
        Report.AppendLine("PeakWorkingSet Tesseract: " +
            FormatMiB(observation.PeakWorkingSet));
        Report.AppendLine("Peak combinado observado: " +
            FormatMiB(observation.PeakCombinedWorkingSet));
        Report.AppendLine("Palabras reconocidas: " +
            result.RecognizedWordCount.ToString(
                CultureInfo.InvariantCulture));
        Report.AppendLine("MediaBox: " +
            outputPageSize.Width.ToString(
                "0.00",
                CultureInfo.InvariantCulture) +
            " x " +
            outputPageSize.Height.ToString(
                "0.00",
                CultureInfo.InvariantCulture) +
            " pt");
        Report.AppendLine("XObject original conservado: PASS");
        Report.AppendLine("Render visible identico: " +
            sourcePixelHash);
        Report.AppendLine("SHA-256 salida: " +
            HashFile(outputPath));
    }

    private static void TestNoTesseractProcesses()
    {
        Require(
            WaitForNoTesseract(5000),
            "Hay procesos tesseract activos al finalizar.");
        Report.AppendLine("Procesos tesseract finales: 0");
    }

    private static PdfOcrSettings CreateSettings()
    {
        var settings = new PdfOcrSettings();
        settings.Language = "spa+eng";
        settings.OcrDpi = 300;
        settings.AnalysisDpi = 150;
        settings.AutoOrient = false;
        settings.AutoDeskew = false;
        settings.ReprocessPagesWithText = false;
        settings.MaximumPixelsPerPage = MaximumPixels;
        settings.SelectedPages = new[] { 1 };
        return settings;
    }

    private static void CreateA0ScannedFixture(string path)
    {
        TryDelete(path);
        const int width = 3000;
        const int height = 4243;
        using (var bitmap = new Bitmap(
            width,
            height,
            PixelFormat.Format24bppRgb))
        {
            bitmap.SetResolution(91F, 91F);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.White);
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.TextRenderingHint =
                    System.Drawing.Text.TextRenderingHint
                        .AntiAliasGridFit;
                using (var titleFont = new System.Drawing.Font(
                    "Arial",
                    76F,
                    FontStyle.Bold,
                    GraphicsUnit.Pixel))
                using (var subtitleFont = new System.Drawing.Font(
                    "Arial",
                    43F,
                    FontStyle.Bold,
                    GraphicsUnit.Pixel))
                using (var bodyFont = new System.Drawing.Font(
                    "Arial",
                    31F,
                    FontStyle.Regular,
                    GraphicsUnit.Pixel))
                using (var heavyPen = new Pen(Color.Black, 7F))
                using (var finePen = new Pen(
                    Color.FromArgb(90, 90, 90),
                    3F))
                {
                    graphics.DrawString(
                        "PLANO GENERAL A0",
                        titleFont,
                        Brushes.Black,
                        145F,
                        115F);
                    graphics.DrawString(
                        "ARQUITECTURA Y LICENCIA URBANISTICA",
                        subtitleFont,
                        Brushes.Black,
                        150F,
                        230F);
                    graphics.DrawLine(
                        heavyPen,
                        145F,
                        305F,
                        2855F,
                        305F);

                    for (var row = 0; row < 44; row++)
                    {
                        var y = 390F + row * 78F;
                        graphics.DrawString(
                            "DETALLE " +
                            (row + 1).ToString("D2") +
                            "  PROYECTO MUNICIPAL DE TOLEDO  " +
                            "COTAS Y COMPROBACION TECNICA",
                            bodyFont,
                            Brushes.Black,
                            170F,
                            y);
                        graphics.DrawLine(
                            finePen,
                            145F,
                            y + 50F,
                            2855F,
                            y + 50F);
                    }

                    graphics.DrawRectangle(
                        heavyPen,
                        120F,
                        85F,
                        2760F,
                        4050F);
                    graphics.DrawRectangle(
                        heavyPen,
                        2040F,
                        3730F,
                        760F,
                        330F);
                    graphics.DrawString(
                        "ESCALA 1:100",
                        subtitleFont,
                        Brushes.Black,
                        2090F,
                        3790F);
                    graphics.DrawString(
                        "HOJA A0.01",
                        subtitleFont,
                        Brushes.Black,
                        2090F,
                        3890F);
                }
            }

            byte[] jpeg;
            using (var memory = new MemoryStream())
            {
                var encoder = FindEncoder(ImageFormat.Jpeg);
                using (var parameters = new EncoderParameters(1))
                {
                    parameters.Param[0] = new EncoderParameter(
                        System.Drawing.Imaging.Encoder.Quality,
                        90L);
                    bitmap.Save(memory, encoder, parameters);
                }

                jpeg = memory.ToArray();
            }

            using (var output = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            {
                var a0 = new PdfRectangle(
                    2383.94F,
                    3370.39F);
                var document = new Document(
                    a0,
                    0F,
                    0F,
                    0F,
                    0F);
                var writer = PdfWriter.GetInstance(
                    document,
                    output);
                writer.SetPdfVersion(PdfWriter.PDF_VERSION_1_7);
                writer.CompressionLevel = PdfStream.BEST_COMPRESSION;
                document.AddTitle(
                    "Plano A0 escaneado - fixture OCR stress");
                document.Open();
                var image = PdfImage.GetInstance(jpeg);
                image.SetAbsolutePosition(0F, 0F);
                image.ScaleAbsolute(a0.Width, a0.Height);
                writer.DirectContent.AddImage(image);
                document.Close();
            }
        }
    }

    private static ImageCodecInfo FindEncoder(ImageFormat format)
    {
        foreach (var encoder in
            ImageCodecInfo.GetImageEncoders())
        {
            if (encoder.FormatID == format.Guid)
            {
                return encoder;
            }
        }

        throw new InvalidOperationException(
            "No se encontro el codificador JPEG.");
    }

    private static void RequireTaskCanceled<T>(
        Task<T> task,
        int timeoutMilliseconds,
        string operation)
    {
        if (task == null)
        {
            throw new InvalidOperationException(
                operation + " no creo su tarea.");
        }

        try
        {
            if (!task.Wait(timeoutMilliseconds))
            {
                throw new TimeoutException(
                    operation +
                    " no termino tras la cancelacion.");
            }
        }
        catch (AggregateException ex)
        {
            var flattened = ex.Flatten();
            foreach (var inner in flattened.InnerExceptions)
            {
                if (inner is OperationCanceledException)
                {
                    return;
                }
            }

            throw flattened.InnerException ?? flattened;
        }

        if (task.IsCanceled)
        {
            return;
        }

        throw new InvalidOperationException(
            operation +
            " termino normalmente pese a solicitar cancelacion.");
    }

    private static TesseractObservation
        WaitForEmbeddedTesseract(
            HashSet<int> baseline,
            int timeoutMilliseconds)
    {
        var observation = new TesseractObservation();
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds <
            timeoutMilliseconds)
        {
            ObserveEmbeddedTesseract(
                baseline,
                observation);
            if (observation.Pid > 0)
            {
                return observation;
            }

            Thread.Sleep(2);
        }

        return observation;
    }

    private static void ObserveEmbeddedTesseract(
        HashSet<int> baseline,
        TesseractObservation observation)
    {
        var parentWorkingSet =
            Process.GetCurrentProcess().WorkingSet64;
        long childWorkingSetTotal = 0;
        foreach (var process in
            Process.GetProcessesByName("tesseract"))
        {
            using (process)
            {
                try
                {
                    if (baseline.Contains(process.Id) ||
                        !PathsEqual(
                            GetProcessExecutable(process),
                            ExpectedTesseractPath))
                    {
                        continue;
                    }

                    observation.Pid = process.Id;
                    var childWorkingSet = process.WorkingSet64;
                    childWorkingSetTotal += childWorkingSet;
                    observation.PeakWorkingSet = Math.Max(
                        observation.PeakWorkingSet,
                        childWorkingSet);
                    if (string.IsNullOrEmpty(
                        observation.InputPngPath))
                    {
                        observation.InputPngPath =
                            GetPngArgument(process.Id);
                    }

                    TryReadImageDimensions(observation);
                }
                catch
                {
                }
            }
        }

        observation.PeakCombinedWorkingSet = Math.Max(
            observation.PeakCombinedWorkingSet,
            parentWorkingSet + childWorkingSetTotal);
    }

    private static string GetProcessExecutable(Process process)
    {
        try
        {
            return process.MainModule.FileName;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetPngArgument(int processId)
    {
        try
        {
            var query =
                "SELECT CommandLine FROM Win32_Process WHERE ProcessId=" +
                processId.ToString(CultureInfo.InvariantCulture);
            using (var searcher =
                new ManagementObjectSearcher(query))
            using (var results = searcher.Get())
            {
                foreach (ManagementObject item in results)
                {
                    using (item)
                    {
                        var commandLine =
                            item["CommandLine"] as string;
                        if (string.IsNullOrEmpty(commandLine))
                        {
                            continue;
                        }

                        var match = Regex.Match(
                            commandLine,
                            "\"([^\"]+\\.png)\"",
                            RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            return match.Groups[1].Value;
                        }
                    }
                }
            }
        }
        catch
        {
        }

        return string.Empty;
    }

    private static void TryReadImageDimensions(
        TesseractObservation observation)
    {
        if (observation.Width > 0 ||
            string.IsNullOrEmpty(observation.InputPngPath) ||
            !File.Exists(observation.InputPngPath))
        {
            return;
        }

        try
        {
            using (var stream = new FileStream(
                observation.InputPngPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            using (var image = DrawingImage.FromStream(
                stream,
                false,
                false))
            {
                observation.Width = image.Width;
                observation.Height = image.Height;
            }
        }
        catch
        {
        }
    }

    private static bool WaitForProcessExit(
        int processId,
        int timeoutMilliseconds)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds <
            timeoutMilliseconds)
        {
            try
            {
                using (var process =
                    Process.GetProcessById(processId))
                {
                    if (process.HasExited)
                    {
                        return true;
                    }
                }
            }
            catch (ArgumentException)
            {
                return true;
            }

            Thread.Sleep(20);
        }

        return false;
    }

    private static HashSet<int>
        GetEmbeddedTesseractProcessIds()
    {
        var result = new HashSet<int>();
        foreach (var process in
            Process.GetProcessesByName("tesseract"))
        {
            using (process)
            {
                if (PathsEqual(
                    GetProcessExecutable(process),
                    ExpectedTesseractPath))
                {
                    result.Add(process.Id);
                }
            }
        }

        return result;
    }

    private static HashSet<int>
        GetAllTesseractProcessIds()
    {
        var result = new HashSet<int>();
        foreach (var process in
            Process.GetProcessesByName("tesseract"))
        {
            using (process)
            {
                result.Add(process.Id);
            }
        }

        return result;
    }

    private static bool WaitForNoEmbeddedTesseract(
        int timeoutMilliseconds)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds <
            timeoutMilliseconds)
        {
            if (GetEmbeddedTesseractProcessIds().Count == 0)
            {
                return true;
            }

            Thread.Sleep(20);
        }

        return false;
    }

    private static bool WaitForNoTesseract(
        int timeoutMilliseconds)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds <
            timeoutMilliseconds)
        {
            if (GetAllTesseractProcessIds().Count == 0)
            {
                return true;
            }

            Thread.Sleep(20);
        }

        return false;
    }

    private static void BestEffortWaitForNoEmbeddedTesseract(
        int timeoutMilliseconds)
    {
        WaitForNoEmbeddedTesseract(timeoutMilliseconds);
    }

    private static void BestEffortWaitForNoTesseract(
        int timeoutMilliseconds)
    {
        WaitForNoTesseract(timeoutMilliseconds);
    }

    private static void BestEffortWaitTask<T>(
        Task<T> task,
        int timeoutMilliseconds)
    {
        if (task == null)
        {
            return;
        }

        try
        {
            task.Wait(timeoutMilliseconds);
        }
        catch
        {
        }
    }

    private static IList<string>
        GetImageFingerprints(string pdfPath)
    {
        var result = new List<string>();
        using (var reader = new PdfReader(pdfPath))
        {
            for (var pageNumber = 1;
                pageNumber <= reader.NumberOfPages;
                pageNumber++)
            {
                var page = reader.GetPageN(pageNumber);
                var resources =
                    page.GetAsDict(PdfName.RESOURCES);
                var xObjects = resources == null
                    ? null
                    : resources.GetAsDict(PdfName.XOBJECT);
                if (xObjects == null)
                {
                    continue;
                }

                foreach (var key in xObjects.Keys)
                {
                    var value = PdfReader.GetPdfObject(
                        xObjects.Get(key));
                    var stream = value as PRStream;
                    if (stream == null ||
                        !PdfName.IMAGE.Equals(
                            stream.GetAsName(PdfName.SUBTYPE)))
                    {
                        continue;
                    }

                    var raw = PdfReader.GetStreamBytesRaw(stream);
                    result.Add(
                        pageNumber.ToString(
                            CultureInfo.InvariantCulture) +
                        ":" +
                        stream.GetAsNumber(PdfName.WIDTH)
                            .IntValue.ToString(
                                CultureInfo.InvariantCulture) +
                        "x" +
                        stream.GetAsNumber(PdfName.HEIGHT)
                            .IntValue.ToString(
                                CultureInfo.InvariantCulture) +
                        ":bpc=" +
                        stream.GetAsNumber(PdfName.BITSPERCOMPONENT)
                            .IntValue.ToString(
                                CultureInfo.InvariantCulture) +
                        ":sha256=" +
                        HashBytes(raw));
                }
            }
        }

        result.Sort(StringComparer.Ordinal);
        return result;
    }

    private static SizeF GetPageSize(string pdfPath)
    {
        using (var reader = new PdfReader(pdfPath))
        {
            var size = reader.GetPageSize(1);
            return new SizeF(size.Width, size.Height);
        }
    }

    private static string RenderPixelHash(
        string pdfPath,
        int width,
        int height,
        string previewPath)
    {
        using (var document = PdfDocument.Load(pdfPath))
        using (var rendered = document.Render(
            0,
            width,
            height,
            72F,
            72F,
            PdfRenderFlags.Annotations |
            PdfRenderFlags.LcdText |
            PdfRenderFlags.LimitImageCacheSize))
        using (var normalized = new Bitmap(
            width,
            height,
            PixelFormat.Format24bppRgb))
        {
            using (var graphics = Graphics.FromImage(normalized))
            {
                graphics.Clear(Color.White);
                graphics.CompositingMode =
                    CompositingMode.SourceCopy;
                graphics.DrawImageUnscaled(rendered, 0, 0);
            }

            normalized.Save(previewPath, ImageFormat.Png);
            var bounds = new System.Drawing.Rectangle(
                0,
                0,
                width,
                height);
            var data = normalized.LockBits(
                bounds,
                ImageLockMode.ReadOnly,
                PixelFormat.Format24bppRgb);
            try
            {
                var length = Math.Abs(data.Stride) * height;
                var bytes = new byte[length];
                System.Runtime.InteropServices.Marshal.Copy(
                    data.Scan0,
                    bytes,
                    0,
                    length);
                return HashBytes(bytes);
            }
            finally
            {
                normalized.UnlockBits(data);
            }
        }
    }

    private static bool OutputContainsExpectedText(
        string path)
    {
        using (var reader = new PdfReader(path))
        {
            var text =
                PdfTextExtractor.GetTextFromPage(reader, 1) ??
                string.Empty;
            return text.IndexOf(
                    "ARQUITECTURA",
                    StringComparison.OrdinalIgnoreCase) >= 0 &&
                text.IndexOf(
                    "TOLEDO",
                    StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    private static HashSet<string> SnapshotFiles(
        string directory,
        string pattern)
    {
        var result = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(directory))
        {
            return result;
        }

        foreach (var path in Directory.GetFiles(
            directory,
            pattern,
            SearchOption.TopDirectoryOnly))
        {
            result.Add(Path.GetFullPath(path));
        }

        return result;
    }

    private static HashSet<string> SnapshotDirectories(
        string directory)
    {
        var result = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(directory))
        {
            return result;
        }

        foreach (var path in
            Directory.GetDirectories(directory))
        {
            result.Add(Path.GetFullPath(path));
        }

        return result;
    }

    private static List<string> Except(
        HashSet<string> after,
        HashSet<string> before)
    {
        var result = new List<string>();
        foreach (var item in after)
        {
            if (!before.Contains(item))
            {
                result.Add(item);
            }
        }

        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    private static List<string> EnumerateFilesSafely(
        IList<string> directories)
    {
        var result = new List<string>();
        foreach (var directory in directories)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    result.AddRange(Directory.GetFiles(
                        directory,
                        "*",
                        SearchOption.AllDirectories));
                }
            }
            catch
            {
            }
        }

        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    private static string GetOcrTempRoot()
    {
        return Path.Combine(
            Path.GetTempPath(),
            "PDFLigero",
            "ocr");
    }

    private static void CleanupSafeOsdResidue(
        HashSet<string> baseline)
    {
        var current = SnapshotFiles(
            Path.GetTempPath(),
            "pdf-ligero-osd-*.png");
        foreach (var path in Except(current, baseline))
        {
            try
            {
                var normalized = Path.GetFullPath(path);
                var temp = EnsureTrailingSeparator(
                    Path.GetFullPath(Path.GetTempPath()));
                if (normalized.StartsWith(
                        temp,
                        StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(normalized).StartsWith(
                        "pdf-ligero-osd-",
                        StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(normalized);
                }
            }
            catch
            {
            }
        }
    }

    private static void CleanupSafeWorkingResidue(
        HashSet<string> baseline)
    {
        var root = Path.GetFullPath(GetOcrTempRoot());
        var current = SnapshotDirectories(root);
        foreach (var path in Except(current, baseline))
        {
            try
            {
                var normalized = Path.GetFullPath(path);
                if (normalized.StartsWith(
                        EnsureTrailingSeparator(root),
                        StringComparison.OrdinalIgnoreCase) &&
                    Directory.Exists(normalized))
                {
                    Directory.Delete(normalized, true);
                }
            }
            catch
            {
            }
        }
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(
            Path.DirectorySeparatorChar.ToString(),
            StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static void RequireSourceUnchanged()
    {
        Require(
            File.Exists(SourcePath),
            "El original desaparecio.");
        Require(
            string.Equals(
                HashFile(SourcePath),
                SourceHash,
                StringComparison.Ordinal),
            "El SHA-256 del original cambio.");
    }

    private static void RequireStringListsEqual(
        IList<string> expected,
        IList<string> actual,
        string message)
    {
        if (expected.Count != actual.Count)
        {
            throw new InvalidDataException(
                message +
                " Conteos: " +
                expected.Count.ToString(
                    CultureInfo.InvariantCulture) +
                " frente a " +
                actual.Count.ToString(
                    CultureInfo.InvariantCulture) +
                ".");
        }

        for (var index = 0;
            index < expected.Count;
            index++)
        {
            if (!string.Equals(
                    expected[index],
                    actual[index],
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    message +
                    " Esperado: " +
                    expected[index] +
                    "; real: " +
                    actual[index] +
                    ".");
            }
        }
    }

    private static string HashFile(string path)
    {
        using (var input = File.OpenRead(path))
        using (var sha = SHA256.Create())
        {
            return BytesToHex(sha.ComputeHash(input));
        }
    }

    private static string HashBytes(byte[] bytes)
    {
        using (var sha = SHA256.Create())
        {
            return BytesToHex(sha.ComputeHash(bytes));
        }
    }

    private static string BytesToHex(byte[] bytes)
    {
        return BitConverter.ToString(bytes)
            .Replace("-", string.Empty);
    }

    private static bool PathsEqual(
        string left,
        string right)
    {
        if (string.IsNullOrEmpty(left) ||
            string.IsNullOrEmpty(right))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(left)
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right)
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
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

    private static void TryDelete(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

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

    private static string FormatMiB(long bytes)
    {
        return (bytes / 1048576D).ToString(
            "0.0",
            CultureInfo.InvariantCulture) +
            " MiB";
    }

    private static string FormatDimensions(
        TesseractObservation observation)
    {
        return observation.Width > 0
            ? observation.Width.ToString(
                CultureInfo.InvariantCulture) +
              " x " +
              observation.Height.ToString(
                CultureInfo.InvariantCulture)
            : "-";
    }

    private static string EmptyAsDash(string value)
    {
        return string.IsNullOrEmpty(value) ? "-" : value;
    }

    private sealed class TesseractObservation
    {
        public int Pid;
        public string InputPngPath;
        public int Width;
        public int Height;
        public long PeakWorkingSet;
        public long PeakCombinedWorkingSet;
    }
}

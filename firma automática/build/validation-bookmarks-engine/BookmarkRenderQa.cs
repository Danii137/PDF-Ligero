using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using PdfiumViewer;

namespace FirmaAutomatica
{
    internal static class BookmarkRenderQa
    {
        private static int Main(string[] args)
        {
            var sourcePath = Path.GetFullPath(args[0]);
            var resultPath = Path.GetFullPath(args[1]);
            var outputDirectory = Path.GetFullPath(args[2]);
            Directory.CreateDirectory(outputDirectory);
            using (var source = PdfDocument.Load(sourcePath))
            using (var result = PdfDocument.Load(resultPath))
            {
                if (source.PageCount != result.PageCount)
                {
                    return 2;
                }

                using (var sourceImage = source.Render(
                    0,
                    900,
                    1273,
                    110,
                    110,
                    PdfRenderFlags.Annotations |
                    PdfRenderFlags.LcdText |
                    PdfRenderFlags.LimitImageCacheSize))
                using (var resultImage = result.Render(
                    0,
                    900,
                    1273,
                    110,
                    110,
                    PdfRenderFlags.Annotations |
                    PdfRenderFlags.LcdText |
                    PdfRenderFlags.LimitImageCacheSize))
                using (var sourceBitmap = new Bitmap(sourceImage))
                using (var resultBitmap = new Bitmap(resultImage))
                {
                    sourceBitmap.Save(
                        Path.Combine(
                            outputDirectory,
                            "source-page-1.png"),
                        ImageFormat.Png);
                    resultBitmap.Save(
                        Path.Combine(
                            outputDirectory,
                            "result-page-1.png"),
                        ImageFormat.Png);
                    if (!SamePixels(sourceBitmap, resultBitmap))
                    {
                        return 3;
                    }
                }
            }

            Console.WriteLine("PASS render idéntico");
            return 0;
        }

        private static bool SamePixels(
            Bitmap left,
            Bitmap right)
        {
            if (left.Width != right.Width ||
                left.Height != right.Height)
            {
                return false;
            }

            for (var y = 0; y < left.Height; y++)
            {
                for (var x = 0; x < left.Width; x++)
                {
                    if (left.GetPixel(x, y).ToArgb() !=
                        right.GetPixel(x, y).ToArgb())
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}

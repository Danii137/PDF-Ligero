using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using iTextSharp.text.pdf;
using PdfRectangle = iTextSharp.text.Rectangle;

namespace FirmaAutomatica
{
    /// <summary>
    /// Converts between the normalized, rotated page seen by the user and raw
    /// PDF user space. PdfStamper.RotateContents is deliberately disabled and
    /// this transform is applied explicitly, because iText's default rotation
    /// matrices assume a zero-origin MediaBox and can drift on offset CropBoxes.
    /// </summary>
    internal sealed class PdfTextPageTransform
    {
        private PdfTextPageTransform(
            float cropLeft,
            float cropBottom,
            float cropRight,
            float cropTop,
            int rotation)
        {
            CropLeft = cropLeft;
            CropBottom = cropBottom;
            CropRight = cropRight;
            CropTop = cropTop;
            Rotation = NormalizeRotation(rotation);
            VisualWidth = Rotation == 90 || Rotation == 270
                ? CropTop - CropBottom
                : CropRight - CropLeft;
            VisualHeight = Rotation == 90 || Rotation == 270
                ? CropRight - CropLeft
                : CropTop - CropBottom;

            if (VisualWidth <= 0F || VisualHeight <= 0F)
            {
                throw new InvalidDataException(
                    "La pagina no tiene un CropBox valido.");
            }
        }

        public float CropLeft { get; private set; }

        public float CropBottom { get; private set; }

        public float CropRight { get; private set; }

        public float CropTop { get; private set; }

        public int Rotation { get; private set; }

        public float VisualWidth { get; private set; }

        public float VisualHeight { get; private set; }

        public static PdfTextPageTransform Create(
            PdfReader reader,
            int pageNumber)
        {
            if (reader == null)
            {
                throw new ArgumentNullException("reader");
            }
            if (pageNumber < 1 || pageNumber > reader.NumberOfPages)
            {
                throw new ArgumentOutOfRangeException("pageNumber");
            }

            var crop = reader.GetCropBox(pageNumber) ??
                reader.GetPageSize(pageNumber);
            if (crop == null)
            {
                throw new InvalidDataException(
                    "La pagina no tiene un tamano valido.");
            }

            var left = Math.Min(crop.Left, crop.Right);
            var right = Math.Max(crop.Left, crop.Right);
            var bottom = Math.Min(crop.Bottom, crop.Top);
            var top = Math.Max(crop.Bottom, crop.Top);
            return new PdfTextPageTransform(
                left,
                bottom,
                right,
                top,
                reader.GetPageRotation(pageNumber));
        }

        public PdfRectangle GetVisualRectangle(PdfTextEditRegion region)
        {
            EnsureRegionPage(region);
            var left = (float)(region.LeftRatio * VisualWidth);
            var right = (float)(region.RightRatio * VisualWidth);
            var bottom = (float)((1D - region.BottomRatio) * VisualHeight);
            var top = (float)((1D - region.TopRatio) * VisualHeight);
            return new PdfRectangle(left, bottom, right, top);
        }

        public PdfRectangle GetRawRectangle(PdfTextEditRegion region)
        {
            var visual = GetVisualRectangle(region);
            var points = new[]
            {
                VisualToRaw(visual.Left, visual.Bottom),
                VisualToRaw(visual.Left, visual.Top),
                VisualToRaw(visual.Right, visual.Bottom),
                VisualToRaw(visual.Right, visual.Top)
            };
            return new PdfRectangle(
                points.Min(point => point.X),
                points.Min(point => point.Y),
                points.Max(point => point.X),
                points.Max(point => point.Y));
        }

        public PdfTextEditRegion GetRegionFromRawRectangle(
            int pageNumber,
            PdfRectangle rawRectangle)
        {
            if (rawRectangle == null)
            {
                throw new ArgumentNullException("rawRectangle");
            }

            var left = Math.Min(rawRectangle.Left, rawRectangle.Right);
            var right = Math.Max(rawRectangle.Left, rawRectangle.Right);
            var bottom = Math.Min(rawRectangle.Bottom, rawRectangle.Top);
            var top = Math.Max(rawRectangle.Bottom, rawRectangle.Top);
            var points = new[]
            {
                RawToVisual(left, bottom),
                RawToVisual(left, top),
                RawToVisual(right, bottom),
                RawToVisual(right, top)
            };
            var visualLeft = points.Min(point => point.X);
            var visualRight = points.Max(point => point.X);
            var visualBottom = points.Min(point => point.Y);
            var visualTop = points.Max(point => point.Y);
            return new PdfTextEditRegion(
                pageNumber,
                visualLeft / VisualWidth,
                1D - visualTop / VisualHeight,
                visualRight / VisualWidth,
                1D - visualBottom / VisualHeight);
        }

        public PointF VisualToRaw(float x, float y)
        {
            switch (Rotation)
            {
                case 90:
                    return new PointF(
                        CropRight - y,
                        CropBottom + x);

                case 180:
                    return new PointF(
                        CropRight - x,
                        CropTop - y);

                case 270:
                    return new PointF(
                        CropLeft + y,
                        CropTop - x);

                default:
                    return new PointF(
                        CropLeft + x,
                        CropBottom + y);
            }
        }

        public PointF RawToVisual(float x, float y)
        {
            switch (Rotation)
            {
                case 90:
                    return new PointF(
                        y - CropBottom,
                        CropRight - x);

                case 180:
                    return new PointF(
                        CropRight - x,
                        CropTop - y);

                case 270:
                    return new PointF(
                        CropTop - y,
                        x - CropLeft);

                default:
                    return new PointF(
                        x - CropLeft,
                        y - CropBottom);
            }
        }

        public void ConcatVisualToRaw(PdfContentByte canvas)
        {
            if (canvas == null)
            {
                throw new ArgumentNullException("canvas");
            }

            switch (Rotation)
            {
                case 90:
                    canvas.ConcatCTM(
                        0F,
                        1F,
                        -1F,
                        0F,
                        CropRight,
                        CropBottom);
                    break;

                case 180:
                    canvas.ConcatCTM(
                        -1F,
                        0F,
                        0F,
                        -1F,
                        CropRight,
                        CropTop);
                    break;

                case 270:
                    canvas.ConcatCTM(
                        0F,
                        -1F,
                        1F,
                        0F,
                        CropLeft,
                        CropTop);
                    break;

                default:
                    canvas.ConcatCTM(
                        1F,
                        0F,
                        0F,
                        1F,
                        CropLeft,
                        CropBottom);
                    break;
            }
        }

        private void EnsureRegionPage(PdfTextEditRegion region)
        {
            if (region == null)
            {
                throw new ArgumentNullException("region");
            }
        }

        private static int NormalizeRotation(int rotation)
        {
            rotation %= 360;
            if (rotation < 0)
            {
                rotation += 360;
            }

            if (rotation != 0 && rotation != 90 &&
                rotation != 180 && rotation != 270)
            {
                throw new InvalidDataException(
                    "La pagina tiene una rotacion no compatible: " +
                    rotation.ToString(CultureInfo.InvariantCulture) +
                    " grados.");
            }

            return rotation;
        }
    }
}

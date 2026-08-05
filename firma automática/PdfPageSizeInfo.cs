using System;
using System.Drawing;
using System.Globalization;

namespace FirmaAutomatica
{
    internal sealed class PdfPageSizeInfo
    {
        public PdfPageSizeInfo(
            bool isValid,
            double widthPoints,
            double heightPoints,
            double widthMillimetres,
            double heightMillimetres,
            string standardName,
            string orientationName)
        {
            IsValid = isValid;
            WidthPoints = widthPoints;
            HeightPoints = heightPoints;
            WidthMillimetres = widthMillimetres;
            HeightMillimetres = heightMillimetres;
            StandardName = standardName ?? string.Empty;
            OrientationName = orientationName ?? string.Empty;
        }

        public bool IsValid { get; private set; }

        public double WidthPoints { get; private set; }

        public double HeightPoints { get; private set; }

        public double WidthMillimetres { get; private set; }

        public double HeightMillimetres { get; private set; }

        public string StandardName { get; private set; }

        public string OrientationName { get; private set; }

        public string MillimetreText
        {
            get
            {
                return IsValid
                    ? PdfPageSizeFormatter.FormatMillimetres(
                        WidthMillimetres,
                        HeightMillimetres)
                    : "—";
            }
        }

        public string CentimetreText
        {
            get
            {
                return IsValid
                    ? PdfPageSizeFormatter.FormatCentimetres(
                        WidthMillimetres,
                        HeightMillimetres)
                    : "—";
            }
        }

        public string CompactText
        {
            get
            {
                if (!IsValid)
                {
                    return "—";
                }

                return (string.IsNullOrWhiteSpace(StandardName)
                        ? "PERSONALIZADO"
                        : StandardName) +
                    " · " + MillimetreText;
            }
        }
    }

    internal static class PdfPageSizeFormatter
    {
        private const double PointsPerInch = 72.0;
        private const double MillimetresPerInch = 25.4;

        private static readonly StandardPaper[] StandardPapers =
        {
            new StandardPaper("A0", 841.0, 1189.0),
            new StandardPaper("A1", 594.0, 841.0),
            new StandardPaper("A2", 420.0, 594.0),
            new StandardPaper("A3", 297.0, 420.0),
            new StandardPaper("A4", 210.0, 297.0),
            new StandardPaper("A5", 148.0, 210.0),
            new StandardPaper("A6", 105.0, 148.0),
            new StandardPaper("A7", 74.0, 105.0),
            new StandardPaper("A8", 52.0, 74.0),
            new StandardPaper("B0", 1000.0, 1414.0),
            new StandardPaper("B1", 707.0, 1000.0),
            new StandardPaper("B2", 500.0, 707.0),
            new StandardPaper("B3", 353.0, 500.0),
            new StandardPaper("B4", 250.0, 353.0),
            new StandardPaper("B5", 176.0, 250.0),
            new StandardPaper("CARTA", 215.9, 279.4),
            new StandardPaper("LEGAL", 215.9, 355.6),
            new StandardPaper("TABLOIDE", 279.4, 431.8),
            new StandardPaper("ARCH A", 228.6, 304.8),
            new StandardPaper("ARCH B", 304.8, 457.2),
            new StandardPaper("ARCH C", 457.2, 609.6),
            new StandardPaper("ARCH D", 609.6, 914.4),
            new StandardPaper("ARCH E", 914.4, 1219.2)
        };

        public static PdfPageSizeInfo FromPoints(SizeF pageSize)
        {
            return FromPoints(pageSize, false);
        }

        public static PdfPageSizeInfo FromPoints(
            SizeF pageSize,
            bool swapWidthAndHeight)
        {
            var widthPoints = swapWidthAndHeight
                ? pageSize.Height
                : pageSize.Width;
            var heightPoints = swapWidthAndHeight
                ? pageSize.Width
                : pageSize.Height;

            if (double.IsNaN(widthPoints) ||
                double.IsNaN(heightPoints) ||
                double.IsInfinity(widthPoints) ||
                double.IsInfinity(heightPoints) ||
                widthPoints <= 0 ||
                heightPoints <= 0)
            {
                return Invalid();
            }

            var widthMillimetres =
                widthPoints * MillimetresPerInch / PointsPerInch;
            var heightMillimetres =
                heightPoints * MillimetresPerInch / PointsPerInch;
            var standardPaper = FindStandardPaper(
                widthMillimetres,
                heightMillimetres);
            if (standardPaper != null)
            {
                var isLandscape =
                    widthMillimetres > heightMillimetres;
                widthMillimetres = isLandscape
                    ? standardPaper.LongSideMillimetres
                    : standardPaper.ShortSideMillimetres;
                heightMillimetres = isLandscape
                    ? standardPaper.ShortSideMillimetres
                    : standardPaper.LongSideMillimetres;
            }

            var orientation = Math.Abs(
                    widthMillimetres - heightMillimetres) < 0.5
                ? "CUADRADO"
                : widthMillimetres > heightMillimetres
                    ? "HORIZONTAL"
                    : "VERTICAL";

            return new PdfPageSizeInfo(
                true,
                widthPoints,
                heightPoints,
                widthMillimetres,
                heightMillimetres,
                standardPaper == null
                    ? string.Empty
                    : standardPaper.Name,
                orientation);
        }

        public static PdfPageSizeInfo Invalid()
        {
            return new PdfPageSizeInfo(
                false,
                0,
                0,
                0,
                0,
                string.Empty,
                string.Empty);
        }

        public static string FormatMillimetres(
            double widthMillimetres,
            double heightMillimetres)
        {
            return FormatMeasurement(widthMillimetres) +
                " × " +
                FormatMeasurement(heightMillimetres) +
                " mm";
        }

        public static string FormatCentimetres(
            double widthMillimetres,
            double heightMillimetres)
        {
            return FormatMeasurement(widthMillimetres / 10.0) +
                " × " +
                FormatMeasurement(heightMillimetres / 10.0) +
                " cm";
        }

        public static string FormatSingleMillimetres(double millimetres)
        {
            return FormatMeasurement(millimetres) + " mm";
        }

        private static StandardPaper FindStandardPaper(
            double widthMillimetres,
            double heightMillimetres)
        {
            var shortSide = Math.Min(
                widthMillimetres,
                heightMillimetres);
            var longSide = Math.Max(
                widthMillimetres,
                heightMillimetres);

            foreach (var paper in StandardPapers)
            {
                var tolerance = Math.Max(
                    1.2,
                    paper.LongSideMillimetres * 0.0025);
                if (Math.Abs(shortSide - paper.ShortSideMillimetres) <=
                        tolerance &&
                    Math.Abs(longSide - paper.LongSideMillimetres) <=
                        tolerance)
                {
                    return paper;
                }
            }

            return null;
        }

        private static string FormatMeasurement(double value)
        {
            var whole = Math.Round(
                value,
                0,
                MidpointRounding.AwayFromZero);
            if (Math.Abs(value - whole) <= 0.15)
            {
                return whole.ToString(
                    "0",
                    CultureInfo.CurrentCulture);
            }

            return Math.Round(
                    value,
                    1,
                    MidpointRounding.AwayFromZero)
                .ToString("0.#", CultureInfo.CurrentCulture);
        }

        private sealed class StandardPaper
        {
            public StandardPaper(
                string name,
                double widthMillimetres,
                double heightMillimetres)
            {
                Name = name;
                ShortSideMillimetres = Math.Min(
                    widthMillimetres,
                    heightMillimetres);
                LongSideMillimetres = Math.Max(
                    widthMillimetres,
                    heightMillimetres);
            }

            public string Name { get; private set; }

            public double ShortSideMillimetres { get; private set; }

            public double LongSideMillimetres { get; private set; }
        }
    }
}

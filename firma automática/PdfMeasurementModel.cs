using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace FirmaAutomatica
{
    internal enum PdfMeasurementKind
    {
        Distance,
        Perimeter,
        Area
    }

    internal enum PdfMeasurementUnit
    {
        Millimeter,
        Centimeter,
        Meter
    }

    /// <summary>
    /// Relates one PDF point (1/72 inch on the drawing) to the represented
    /// real-world length. Instances are immutable and safe to share.
    /// </summary>
    internal sealed class PdfMeasurementCalibration : ICloneable
    {
        private const double MillimetersPerInch = 25.4D;
        private const double PdfPointsPerInch = 72D;
        private const double MinimumMillimetersPerPdfPoint = 0.000000001D;
        private const double MaximumMillimetersPerPdfPoint = 1000000000D;

        private readonly double realMillimetersPerPdfPoint;
        private readonly string description;

        private PdfMeasurementCalibration(
            double realMillimetersPerPdfPoint,
            string description)
        {
            ValidatePositiveFinite(
                realMillimetersPerPdfPoint,
                "realMillimetersPerPdfPoint");

            if (realMillimetersPerPdfPoint <
                    MinimumMillimetersPerPdfPoint ||
                realMillimetersPerPdfPoint >
                    MaximumMillimetersPerPdfPoint)
            {
                throw new ArgumentOutOfRangeException(
                    "realMillimetersPerPdfPoint",
                    "La calibración está fuera del intervalo admitido.");
            }

            this.realMillimetersPerPdfPoint =
                realMillimetersPerPdfPoint;
            this.description = description ?? string.Empty;
        }

        public double RealMillimetersPerPdfPoint
        {
            get { return realMillimetersPerPdfPoint; }
        }

        public string Description
        {
            get { return description; }
        }

        /// <summary>
        /// Creates a drawing calibration such as 1:100. The denominator
        /// represents real millimetres per printed millimetre.
        /// </summary>
        public static PdfMeasurementCalibration FromScale(
            double denominator)
        {
            ValidatePositiveFinite(denominator, "denominator");

            var millimetersPerPoint =
                denominator * MillimetersPerInch / PdfPointsPerInch;
            if (!IsFinite(millimetersPerPoint))
            {
                throw new ArgumentOutOfRangeException(
                    "denominator",
                    "La escala produce una calibración demasiado grande.");
            }

            var scaleText = PdfMeasurementFormatter.FormatScaleDenominator(
                denominator);
            return new PdfMeasurementCalibration(
                millimetersPerPoint,
                "Escala 1:" + scaleText);
        }

        /// <summary>
        /// Creates a calibration from a known real distance measured between
        /// two points in the PDF.
        /// </summary>
        public static PdfMeasurementCalibration FromKnownDistance(
            double pdfPoints,
            double realValue,
            PdfMeasurementUnit unit)
        {
            ValidatePositiveFinite(pdfPoints, "pdfPoints");
            ValidatePositiveFinite(realValue, "realValue");
            PdfMeasurementFormatter.ValidateUnit(unit);

            var unitMillimeters =
                PdfMeasurementFormatter.GetMillimetersPerUnit(unit);
            var realMillimeters = CheckedMultiply(
                realValue,
                unitMillimeters,
                "realValue");
            var millimetersPerPoint = realMillimeters / pdfPoints;
            if (!IsFinite(millimetersPerPoint))
            {
                throw new ArgumentOutOfRangeException(
                    "realValue",
                    "La distancia conocida produce una calibración no válida.");
            }

            var description =
                "Calibración conocida · " +
                PdfMeasurementFormatter.FormatLength(realValue, unit) +
                " = " +
                PdfMeasurementFormatter.FormatPdfPoints(pdfPoints);
            return new PdfMeasurementCalibration(
                millimetersPerPoint,
                description);
        }

        public double ConvertLength(
            double pdfPoints,
            PdfMeasurementUnit unit)
        {
            ValidateNonNegativeFinite(pdfPoints, "pdfPoints");
            PdfMeasurementFormatter.ValidateUnit(unit);

            var millimeters = CheckedMultiply(
                pdfPoints,
                realMillimetersPerPdfPoint,
                "pdfPoints");
            return millimeters /
                PdfMeasurementFormatter.GetMillimetersPerUnit(unit);
        }

        public double ConvertArea(
            double pdfSquarePoints,
            PdfMeasurementUnit unit)
        {
            ValidateNonNegativeFinite(
                pdfSquarePoints,
                "pdfSquarePoints");
            PdfMeasurementFormatter.ValidateUnit(unit);

            var unitMillimeters =
                PdfMeasurementFormatter.GetMillimetersPerUnit(unit);
            var ratioInTargetUnits =
                realMillimetersPerPdfPoint / unitMillimeters;
            var squareRatio = CheckedMultiply(
                ratioInTargetUnits,
                ratioInTargetUnits,
                "realMillimetersPerPdfPoint");
            return CheckedMultiply(
                pdfSquarePoints,
                squareRatio,
                "pdfSquarePoints");
        }

        public PdfMeasurementCalibration Clone()
        {
            return new PdfMeasurementCalibration(
                realMillimetersPerPdfPoint,
                description);
        }

        object ICloneable.Clone()
        {
            return Clone();
        }

        private static double CheckedMultiply(
            double left,
            double right,
            string parameterName)
        {
            if (left != 0D &&
                right != 0D &&
                Math.Abs(left) > double.MaxValue / Math.Abs(right))
            {
                throw new OverflowException(
                    "El cálculo de medición supera el intervalo numérico.");
            }

            var result = left * right;
            if (!IsFinite(result))
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "El valor produce un resultado no finito.");
            }

            return result;
        }

        private static void ValidatePositiveFinite(
            double value,
            string parameterName)
        {
            if (!IsFinite(value) || value <= 0D)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "El valor debe ser finito y mayor que cero.");
            }
        }

        private static void ValidateNonNegativeFinite(
            double value,
            string parameterName)
        {
            if (!IsFinite(value) || value < 0D)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "El valor debe ser finito y no negativo.");
            }
        }

        internal static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }

    internal struct PdfMeasurementPoint
    {
        private const double MaximumCoordinateMagnitude = 1000000000000D;

        private readonly double x;
        private readonly double y;

        public PdfMeasurementPoint(double x, double y)
        {
            if (!PdfMeasurementCalibration.IsFinite(x) ||
                Math.Abs(x) > MaximumCoordinateMagnitude)
            {
                throw new ArgumentOutOfRangeException(
                    "x",
                    "La coordenada X no es válida.");
            }

            if (!PdfMeasurementCalibration.IsFinite(y) ||
                Math.Abs(y) > MaximumCoordinateMagnitude)
            {
                throw new ArgumentOutOfRangeException(
                    "y",
                    "La coordenada Y no es válida.");
            }

            this.x = x;
            this.y = y;
        }

        public double X
        {
            get { return x; }
        }

        public double Y
        {
            get { return y; }
        }
    }

    /// <summary>
    /// Immutable measurement geometry stored in native PDF point
    /// coordinates for one zero-based page.
    /// </summary>
    internal sealed class PdfPageMeasurement : ICloneable
    {
        private readonly int pageIndex;
        private readonly PdfMeasurementKind kind;
        private readonly ReadOnlyCollection<PdfMeasurementPoint> points;
        private readonly double lengthPdfPoints;
        private readonly double perimeterPdfPoints;
        private readonly double areaPdfSquarePoints;

        public PdfPageMeasurement(
            int pageIndex,
            PdfMeasurementKind kind,
            IEnumerable<PdfMeasurementPoint> points)
        {
            if (pageIndex < 0)
            {
                throw new ArgumentOutOfRangeException(
                    "pageIndex",
                    "El índice de página no puede ser negativo.");
            }

            ValidateKind(kind);
            if (points == null)
            {
                throw new ArgumentNullException("points");
            }

            var copiedPoints = new List<PdfMeasurementPoint>();
            foreach (var point in points)
            {
                ValidatePoint(point);
                copiedPoints.Add(point);
            }

            this.pageIndex = pageIndex;
            this.kind = kind;
            this.points = copiedPoints.AsReadOnly();
            lengthPdfPoints = CalculateOpenLength(copiedPoints);
            perimeterPdfPoints = CalculatePerimeter(copiedPoints);
            areaPdfSquarePoints = CalculateArea(copiedPoints);
        }

        public int PageIndex
        {
            get { return pageIndex; }
        }

        public PdfMeasurementKind Kind
        {
            get { return kind; }
        }

        public ReadOnlyCollection<PdfMeasurementPoint> Points
        {
            get { return points; }
        }

        /// <summary>
        /// Sum of consecutive segments. This is the raw value used for
        /// Distance measurements.
        /// </summary>
        public double LengthPdfPoints
        {
            get { return lengthPdfPoints; }
        }

        /// <summary>
        /// Closed polygon perimeter. Fewer than three points return zero.
        /// </summary>
        public double PerimeterPdfPoints
        {
            get { return perimeterPdfPoints; }
        }

        /// <summary>
        /// Absolute shoelace area. Fewer than three or collinear points
        /// return zero.
        /// </summary>
        public double AreaPdfSquarePoints
        {
            get { return areaPdfSquarePoints; }
        }

        public double Calculate(
            PdfMeasurementCalibration calibration,
            PdfMeasurementUnit unit)
        {
            if (calibration == null)
            {
                throw new ArgumentNullException("calibration");
            }

            PdfMeasurementFormatter.ValidateUnit(unit);
            switch (kind)
            {
                case PdfMeasurementKind.Distance:
                    return calibration.ConvertLength(
                        lengthPdfPoints,
                        unit);

                case PdfMeasurementKind.Perimeter:
                    return calibration.ConvertLength(
                        perimeterPdfPoints,
                        unit);

                case PdfMeasurementKind.Area:
                    return calibration.ConvertArea(
                        areaPdfSquarePoints,
                        unit);

                default:
                    throw new InvalidOperationException(
                        "Tipo de medición no admitido.");
            }
        }

        public PdfMeasurementValue Measure(
            PdfMeasurementCalibration calibration,
            PdfMeasurementUnit unit)
        {
            return new PdfMeasurementValue(
                kind,
                unit,
                Calculate(calibration, unit));
        }

        public string Format(
            PdfMeasurementCalibration calibration,
            PdfMeasurementUnit unit)
        {
            return Measure(calibration, unit).FormattedText;
        }

        public PdfPageMeasurement Clone()
        {
            return new PdfPageMeasurement(pageIndex, kind, points);
        }

        object ICloneable.Clone()
        {
            return Clone();
        }

        private static double CalculateOpenLength(
            IList<PdfMeasurementPoint> source)
        {
            if (source.Count < 2)
            {
                return 0D;
            }

            var total = 0D;
            for (var index = 1; index < source.Count; index++)
            {
                total = CheckedAdd(
                    total,
                    Distance(source[index - 1], source[index]));
            }

            return total;
        }

        private static double CalculatePerimeter(
            IList<PdfMeasurementPoint> source)
        {
            if (source.Count < 3)
            {
                return 0D;
            }

            return CheckedAdd(
                CalculateOpenLength(source),
                Distance(source[source.Count - 1], source[0]));
        }

        private static double CalculateArea(
            IList<PdfMeasurementPoint> source)
        {
            if (source.Count < 3)
            {
                return 0D;
            }

            // Translating every vertex to the first one reduces cancellation
            // when a small plan is stored at a large PDF coordinate offset.
            var originX = source[0].X;
            var originY = source[0].Y;
            var twiceArea = 0D;
            var compensation = 0D;

            for (var index = 0; index < source.Count; index++)
            {
                var next = (index + 1) % source.Count;
                var x1 = source[index].X - originX;
                var y1 = source[index].Y - originY;
                var x2 = source[next].X - originX;
                var y2 = source[next].Y - originY;
                var cross = CheckedSubtractProducts(x1, y2, y1, x2);

                // Kahan summation keeps long polygons stable without any
                // allocation or external geometry dependency.
                var adjusted = cross - compensation;
                var newTotal = twiceArea + adjusted;
                compensation = (newTotal - twiceArea) - adjusted;
                twiceArea = newTotal;

                if (!PdfMeasurementCalibration.IsFinite(twiceArea))
                {
                    throw new OverflowException(
                        "El área de la geometría supera el intervalo numérico.");
                }
            }

            return Math.Abs(twiceArea) / 2D;
        }

        private static double Distance(
            PdfMeasurementPoint left,
            PdfMeasurementPoint right)
        {
            var deltaX = right.X - left.X;
            var deltaY = right.Y - left.Y;
            var scale = Math.Max(Math.Abs(deltaX), Math.Abs(deltaY));
            if (scale == 0D)
            {
                return 0D;
            }

            var normalizedX = deltaX / scale;
            var normalizedY = deltaY / scale;
            var distance =
                scale * Math.Sqrt(
                    normalizedX * normalizedX +
                    normalizedY * normalizedY);
            if (!PdfMeasurementCalibration.IsFinite(distance))
            {
                throw new OverflowException(
                    "La longitud de la geometría supera el intervalo numérico.");
            }

            return distance;
        }

        private static double CheckedSubtractProducts(
            double firstLeft,
            double firstRight,
            double secondLeft,
            double secondRight)
        {
            var first = firstLeft * firstRight;
            var second = secondLeft * secondRight;
            var result = first - second;
            if (!PdfMeasurementCalibration.IsFinite(first) ||
                !PdfMeasurementCalibration.IsFinite(second) ||
                !PdfMeasurementCalibration.IsFinite(result))
            {
                throw new OverflowException(
                    "El área de la geometría supera el intervalo numérico.");
            }

            return result;
        }

        private static double CheckedAdd(double left, double right)
        {
            var result = left + right;
            if (!PdfMeasurementCalibration.IsFinite(result))
            {
                throw new OverflowException(
                    "La longitud de la geometría supera el intervalo numérico.");
            }

            return result;
        }

        private static void ValidatePoint(PdfMeasurementPoint point)
        {
            // A default struct is valid. Reconstructing applies the same
            // finite/range checks to values received from other callers.
            new PdfMeasurementPoint(point.X, point.Y);
        }

        private static void ValidateKind(PdfMeasurementKind value)
        {
            if (value != PdfMeasurementKind.Distance &&
                value != PdfMeasurementKind.Perimeter &&
                value != PdfMeasurementKind.Area)
            {
                throw new ArgumentOutOfRangeException(
                    "kind",
                    "Tipo de medición no admitido.");
            }
        }
    }

    internal sealed class PdfMeasurementValue
    {
        private readonly PdfMeasurementKind kind;
        private readonly PdfMeasurementUnit unit;
        private readonly double value;
        private readonly string formattedText;

        public PdfMeasurementValue(
            PdfMeasurementKind kind,
            PdfMeasurementUnit unit,
            double value)
        {
            PdfMeasurementFormatter.ValidateKind(kind);
            PdfMeasurementFormatter.ValidateUnit(unit);
            if (!PdfMeasurementCalibration.IsFinite(value) || value < 0D)
            {
                throw new ArgumentOutOfRangeException(
                    "value",
                    "El resultado debe ser finito y no negativo.");
            }

            this.kind = kind;
            this.unit = unit;
            this.value = value;
            formattedText = PdfMeasurementFormatter.FormatValue(
                value,
                unit,
                kind == PdfMeasurementKind.Area);
        }

        public PdfMeasurementKind Kind
        {
            get { return kind; }
        }

        public PdfMeasurementUnit Unit
        {
            get { return unit; }
        }

        public double Value
        {
            get { return value; }
        }

        public string FormattedText
        {
            get { return formattedText; }
        }
    }

    internal static class PdfMeasurementFormatter
    {
        private static readonly NumberFormatInfo SpanishNumbers =
            CreateSpanishNumberFormat();

        public static string FormatLength(
            double value,
            PdfMeasurementUnit unit)
        {
            return FormatValue(value, unit, false);
        }

        public static string FormatArea(
            double value,
            PdfMeasurementUnit unit)
        {
            return FormatValue(value, unit, true);
        }

        public static string FormatValue(
            double value,
            PdfMeasurementUnit unit,
            bool square)
        {
            ValidateUnit(unit);
            if (!PdfMeasurementCalibration.IsFinite(value) || value < 0D)
            {
                throw new ArgumentOutOfRangeException(
                    "value",
                    "El valor que se va a formatear no es válido.");
            }

            return value.ToString("N2", SpanishNumbers) +
                " " +
                GetUnitSuffix(unit, square);
        }

        public static string GetUnitSuffix(
            PdfMeasurementUnit unit,
            bool square)
        {
            ValidateUnit(unit);
            string suffix;
            switch (unit)
            {
                case PdfMeasurementUnit.Millimeter:
                    suffix = "mm";
                    break;

                case PdfMeasurementUnit.Centimeter:
                    suffix = "cm";
                    break;

                case PdfMeasurementUnit.Meter:
                    suffix = "m";
                    break;

                default:
                    throw new ArgumentOutOfRangeException("unit");
            }

            return square ? suffix + "\u00B2" : suffix;
        }

        internal static double GetMillimetersPerUnit(
            PdfMeasurementUnit unit)
        {
            ValidateUnit(unit);
            switch (unit)
            {
                case PdfMeasurementUnit.Millimeter:
                    return 1D;

                case PdfMeasurementUnit.Centimeter:
                    return 10D;

                case PdfMeasurementUnit.Meter:
                    return 1000D;

                default:
                    throw new ArgumentOutOfRangeException("unit");
            }
        }

        internal static string FormatPdfPoints(double value)
        {
            return value.ToString("N2", SpanishNumbers) + " pt";
        }

        internal static string FormatScaleDenominator(double value)
        {
            return value.ToString("0.######", SpanishNumbers);
        }

        internal static void ValidateKind(PdfMeasurementKind value)
        {
            if (value != PdfMeasurementKind.Distance &&
                value != PdfMeasurementKind.Perimeter &&
                value != PdfMeasurementKind.Area)
            {
                throw new ArgumentOutOfRangeException(
                    "kind",
                    "Tipo de medición no admitido.");
            }
        }

        internal static void ValidateUnit(PdfMeasurementUnit value)
        {
            if (value != PdfMeasurementUnit.Millimeter &&
                value != PdfMeasurementUnit.Centimeter &&
                value != PdfMeasurementUnit.Meter)
            {
                throw new ArgumentOutOfRangeException(
                    "unit",
                    "Unidad de medición no admitida.");
            }
        }

        private static NumberFormatInfo CreateSpanishNumberFormat()
        {
            var numberFormat = new NumberFormatInfo();
            numberFormat.NumberDecimalSeparator = ",";
            numberFormat.NumberGroupSeparator = ".";
            numberFormat.NumberDecimalDigits = 2;
            return numberFormat;
        }
    }
}

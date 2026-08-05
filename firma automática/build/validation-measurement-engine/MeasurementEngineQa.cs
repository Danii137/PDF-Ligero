using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace FirmaAutomatica
{
    internal static class MeasurementEngineQa
    {
        private const double TightTolerance = 0.000000001D;

        public static int Main(string[] arguments)
        {
            var report = new StringBuilder();
            try
            {
                Run(report);
                report.AppendLine("RESULTADO=PASS");
                PersistReport(arguments, report.ToString());
                Console.Write(report.ToString());
                return 0;
            }
            catch (Exception exception)
            {
                report.AppendLine("RESULTADO=FAIL");
                report.AppendLine(
                    exception.GetType().FullName + ": " + exception.Message);
                PersistReport(arguments, report.ToString());
                Console.Error.Write(report.ToString());
                return 1;
            }
        }

        private static void Run(StringBuilder report)
        {
            TestDistance(report);
            TestTriangle(report);
            TestGeometryUnitMatrix(report);
            TestPolygon(report);
            TestScaleOneHundred(report);
            TestKnownDistance(report);
            TestDegenerateGeometry(report);
            TestImmutabilityAndClone(report);
            TestSpanishFormatting(report);
            TestInvalidNumbers(report);
        }

        private static void TestDistance(StringBuilder report)
        {
            var calibration = PdfMeasurementCalibration.FromKnownDistance(
                1D,
                1D,
                PdfMeasurementUnit.Millimeter);
            var measurement = new PdfPageMeasurement(
                0,
                PdfMeasurementKind.Distance,
                new[]
                {
                    new PdfMeasurementPoint(0D, 0D),
                    new PdfMeasurementPoint(3D, 4D)
                });

            AssertNear(5D, measurement.LengthPdfPoints, TightTolerance);
            AssertNear(
                5D,
                measurement.Calculate(
                    calibration,
                    PdfMeasurementUnit.Millimeter),
                TightTolerance);
            report.AppendLine("DISTANCIA_3_4_5=PASS");
        }

        private static void TestTriangle(StringBuilder report)
        {
            var points = new[]
            {
                new PdfMeasurementPoint(0D, 0D),
                new PdfMeasurementPoint(3D, 0D),
                new PdfMeasurementPoint(3D, 4D)
            };
            var perimeter = new PdfPageMeasurement(
                1,
                PdfMeasurementKind.Perimeter,
                points);
            var area = new PdfPageMeasurement(
                1,
                PdfMeasurementKind.Area,
                points);

            AssertNear(12D, perimeter.PerimeterPdfPoints, TightTolerance);
            AssertNear(6D, area.AreaPdfSquarePoints, TightTolerance);
            report.AppendLine("TRIANGULO_PERIMETRO_AREA=PASS");
        }

        private static void TestGeometryUnitMatrix(StringBuilder report)
        {
            var calibration = PdfMeasurementCalibration.FromKnownDistance(
                1D,
                1D,
                PdfMeasurementUnit.Millimeter);
            var points = new[]
            {
                new PdfMeasurementPoint(0D, 0D),
                new PdfMeasurementPoint(3D, 0D),
                new PdfMeasurementPoint(3D, 4D)
            };
            var distance = new PdfPageMeasurement(
                0,
                PdfMeasurementKind.Distance,
                new[]
                {
                    new PdfMeasurementPoint(0D, 0D),
                    new PdfMeasurementPoint(3D, 4D)
                });
            var perimeter = new PdfPageMeasurement(
                0,
                PdfMeasurementKind.Perimeter,
                points);
            var area = new PdfPageMeasurement(
                0,
                PdfMeasurementKind.Area,
                points);

            AssertNear(
                5D,
                distance.Calculate(
                    calibration,
                    PdfMeasurementUnit.Millimeter),
                TightTolerance);
            AssertNear(
                0.5D,
                distance.Calculate(
                    calibration,
                    PdfMeasurementUnit.Centimeter),
                TightTolerance);
            AssertNear(
                0.005D,
                distance.Calculate(
                    calibration,
                    PdfMeasurementUnit.Meter),
                TightTolerance);

            AssertNear(
                12D,
                perimeter.Calculate(
                    calibration,
                    PdfMeasurementUnit.Millimeter),
                TightTolerance);
            AssertNear(
                1.2D,
                perimeter.Calculate(
                    calibration,
                    PdfMeasurementUnit.Centimeter),
                TightTolerance);
            AssertNear(
                0.012D,
                perimeter.Calculate(
                    calibration,
                    PdfMeasurementUnit.Meter),
                TightTolerance);

            AssertNear(
                6D,
                area.Calculate(
                    calibration,
                    PdfMeasurementUnit.Millimeter),
                TightTolerance);
            AssertNear(
                0.06D,
                area.Calculate(
                    calibration,
                    PdfMeasurementUnit.Centimeter),
                TightTolerance);
            AssertNear(
                0.000006D,
                area.Calculate(
                    calibration,
                    PdfMeasurementUnit.Meter),
                TightTolerance);
            report.AppendLine("GEOMETRIAS_X_UNIDADES_3X3=PASS");
        }

        private static void TestPolygon(StringBuilder report)
        {
            var rectangle = new[]
            {
                new PdfMeasurementPoint(1000000000D, -1000000000D),
                new PdfMeasurementPoint(1000000020D, -1000000000D),
                new PdfMeasurementPoint(1000000020D, -999999990D),
                new PdfMeasurementPoint(1000000000D, -999999990D)
            };
            var geometry = new PdfPageMeasurement(
                2,
                PdfMeasurementKind.Area,
                rectangle);

            AssertNear(200D, geometry.AreaPdfSquarePoints, TightTolerance);
            AssertNear(60D, geometry.PerimeterPdfPoints, TightTolerance);
            report.AppendLine("POLIGONO_TRASLADADO_ESTABLE=PASS");
        }

        private static void TestScaleOneHundred(StringBuilder report)
        {
            var calibration = PdfMeasurementCalibration.FromScale(100D);
            AssertNear(
                100D * 25.4D / 72D,
                calibration.RealMillimetersPerPdfPoint,
                TightTolerance);
            AssertNear(
                2.54D,
                calibration.ConvertLength(
                    72D,
                    PdfMeasurementUnit.Meter),
                TightTolerance);
            AssertEqual("Escala 1:100", calibration.Description);
            report.AppendLine("ESCALA_1_100=PASS");
        }

        private static void TestKnownDistance(StringBuilder report)
        {
            var calibration = PdfMeasurementCalibration.FromKnownDistance(
                144D,
                5D,
                PdfMeasurementUnit.Meter);
            var distance = new PdfPageMeasurement(
                3,
                PdfMeasurementKind.Distance,
                new[]
                {
                    new PdfMeasurementPoint(0D, 0D),
                    new PdfMeasurementPoint(72D, 0D)
                });
            var square = new PdfPageMeasurement(
                3,
                PdfMeasurementKind.Area,
                new[]
                {
                    new PdfMeasurementPoint(0D, 0D),
                    new PdfMeasurementPoint(72D, 0D),
                    new PdfMeasurementPoint(72D, 72D),
                    new PdfMeasurementPoint(0D, 72D)
                });

            AssertNear(
                2.5D,
                distance.Calculate(calibration, PdfMeasurementUnit.Meter),
                TightTolerance);
            AssertNear(
                6.25D,
                square.Calculate(calibration, PdfMeasurementUnit.Meter),
                TightTolerance);
            AssertTrue(
                calibration.Description.IndexOf(
                    "5,00 m",
                    StringComparison.Ordinal) >= 0,
                "La descripción de calibración no conserva el formato español.");
            report.AppendLine("CALIBRACION_CONOCIDA=PASS");
        }

        private static void TestDegenerateGeometry(StringBuilder report)
        {
            var calibration = PdfMeasurementCalibration.FromScale(1D);
            var empty = new PdfPageMeasurement(
                0,
                PdfMeasurementKind.Distance,
                new PdfMeasurementPoint[0]);
            var onePoint = new PdfPageMeasurement(
                0,
                PdfMeasurementKind.Perimeter,
                new[] { new PdfMeasurementPoint(8D, 9D) });
            var collinear = new PdfPageMeasurement(
                0,
                PdfMeasurementKind.Area,
                new[]
                {
                    new PdfMeasurementPoint(0D, 0D),
                    new PdfMeasurementPoint(1D, 1D),
                    new PdfMeasurementPoint(2D, 2D)
                });

            AssertNear(
                0D,
                empty.Calculate(
                    calibration,
                    PdfMeasurementUnit.Millimeter),
                TightTolerance);
            AssertNear(0D, onePoint.PerimeterPdfPoints, TightTolerance);
            AssertNear(0D, collinear.AreaPdfSquarePoints, TightTolerance);
            report.AppendLine("GEOMETRIAS_DEGENERADAS=PASS");
        }

        private static void TestImmutabilityAndClone(StringBuilder report)
        {
            var source = new List<PdfMeasurementPoint>
            {
                new PdfMeasurementPoint(0D, 0D),
                new PdfMeasurementPoint(2D, 0D)
            };
            var geometry = new PdfPageMeasurement(
                4,
                PdfMeasurementKind.Distance,
                source);
            source.Add(new PdfMeasurementPoint(100D, 100D));

            AssertEqual(2, geometry.Points.Count);
            AssertThrows<NotSupportedException>(
                delegate
                {
                    ((IList<PdfMeasurementPoint>)geometry.Points).Add(
                        new PdfMeasurementPoint(3D, 0D));
                });

            var clone = geometry.Clone();
            AssertTrue(
                !object.ReferenceEquals(geometry, clone),
                "El clon de geometría debe ser una instancia independiente.");
            AssertNear(
                geometry.LengthPdfPoints,
                clone.LengthPdfPoints,
                TightTolerance);

            var calibration = PdfMeasurementCalibration.FromScale(50D);
            var calibrationClone = calibration.Clone();
            AssertTrue(
                !object.ReferenceEquals(calibration, calibrationClone),
                "El clon de calibración debe ser una instancia independiente.");
            AssertNear(
                calibration.RealMillimetersPerPdfPoint,
                calibrationClone.RealMillimetersPerPdfPoint,
                TightTolerance);
            report.AppendLine("INMUTABILIDAD_Y_CLON=PASS");
        }

        private static void TestSpanishFormatting(StringBuilder report)
        {
            AssertEqual(
                "5,00 m",
                PdfMeasurementFormatter.FormatLength(
                    5D,
                    PdfMeasurementUnit.Meter));
            AssertEqual(
                "1.234,50 cm²",
                PdfMeasurementFormatter.FormatArea(
                    1234.5D,
                    PdfMeasurementUnit.Centimeter));
            report.AppendLine("FORMATO_ES_ES=PASS");
        }

        private static void TestInvalidNumbers(StringBuilder report)
        {
            AssertThrows<ArgumentOutOfRangeException>(
                delegate { PdfMeasurementCalibration.FromScale(0D); });
            AssertThrows<ArgumentOutOfRangeException>(
                delegate
                {
                    PdfMeasurementCalibration.FromScale(double.NaN);
                });
            AssertThrows<ArgumentOutOfRangeException>(
                delegate
                {
                    PdfMeasurementCalibration.FromKnownDistance(
                        0D,
                        1D,
                        PdfMeasurementUnit.Meter);
                });
            AssertThrows<OverflowException>(
                delegate
                {
                    PdfMeasurementCalibration.FromKnownDistance(
                        1D,
                        double.MaxValue,
                        PdfMeasurementUnit.Meter);
                });
            AssertThrows<OverflowException>(
                delegate
                {
                    PdfMeasurementCalibration.FromScale(1000000000D)
                        .ConvertLength(
                            double.MaxValue,
                            PdfMeasurementUnit.Millimeter);
                });
            AssertThrows<ArgumentOutOfRangeException>(
                delegate
                {
                    new PdfMeasurementPoint(double.PositiveInfinity, 0D);
                });
            AssertThrows<ArgumentOutOfRangeException>(
                delegate
                {
                    new PdfMeasurementPoint(1000000000001D, 0D);
                });
            AssertThrows<ArgumentOutOfRangeException>(
                delegate
                {
                    new PdfPageMeasurement(
                        -1,
                        PdfMeasurementKind.Distance,
                        new PdfMeasurementPoint[0]);
                });
            report.AppendLine("VALIDACION_NO_FINITO_RANGO=PASS");
        }

        private static void PersistReport(
            string[] arguments,
            string report)
        {
            if (arguments == null ||
                arguments.Length == 0 ||
                string.IsNullOrWhiteSpace(arguments[0]))
            {
                return;
            }

            Directory.CreateDirectory(arguments[0]);
            File.WriteAllText(
                Path.Combine(arguments[0], "qa-report.txt"),
                report,
                new UTF8Encoding(false));
        }

        private static void AssertNear(
            double expected,
            double actual,
            double tolerance)
        {
            if (Math.Abs(expected - actual) > tolerance)
            {
                throw new InvalidOperationException(
                    "Esperado " +
                    expected.ToString("R", CultureInfo.InvariantCulture) +
                    ", obtenido " +
                    actual.ToString("R", CultureInfo.InvariantCulture) +
                    ".");
            }
        }

        private static void AssertEqual(string expected, string actual)
        {
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Esperado '" + expected + "', obtenido '" + actual + "'.");
            }
        }

        private static void AssertEqual(int expected, int actual)
        {
            if (expected != actual)
            {
                throw new InvalidOperationException(
                    "Esperado " + expected + ", obtenido " + actual + ".");
            }
        }

        private static void AssertTrue(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static void AssertThrows<TException>(Action action)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }

            throw new InvalidOperationException(
                "Se esperaba " + typeof(TException).FullName + ".");
        }
    }
}

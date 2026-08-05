using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;

namespace FirmaAutomatica
{
    internal sealed class SignatureAppearanceProfile
    {
        public SignatureAppearanceProfile(string signerName, string distinguishedName, string reason, byte[] graphicBytes)
        {
            SignerName = signerName ?? string.Empty;
            DistinguishedName = distinguishedName ?? string.Empty;
            Reason = reason ?? string.Empty;
            GraphicBytes = graphicBytes;
        }

        public string SignerName { get; private set; }

        public string DistinguishedName { get; private set; }

        public string Reason { get; private set; }

        public byte[] GraphicBytes { get; private set; }
    }

    internal sealed class SignatureAppearanceData
    {
        public SignatureAppearanceData(string signerName, string distinguishedName, string reason, DateTime signedAt, byte[] graphicBytes)
        {
            SignerName = signerName ?? string.Empty;
            DistinguishedName = distinguishedName ?? string.Empty;
            Reason = reason ?? string.Empty;
            SignedAt = signedAt;
            GraphicBytes = graphicBytes;
        }

        public string SignerName { get; private set; }

        public string DistinguishedName { get; private set; }

        public string Reason { get; private set; }

        public DateTime SignedAt { get; private set; }

        public byte[] GraphicBytes { get; private set; }
    }

    internal static class SignatureAppearanceRenderer
    {
        private const float SmallWidthPoints = 135f;
        private const float SmallHeightPoints = 52f;
        private const int MinimumStrongPixelsPerComponent = 8;
        private const int MinimumSoftPixelsPerComponent = 20;
        private const int SoftExpansionDepth = 1;
        private const int OutputBackgroundAlpha = 48;
        private const int PreviewBackgroundAlpha = 76;
        private const int OutputTextPanelAlpha = 78;
        private const int PreviewTextPanelAlpha = 108;
        private static readonly Color DefaultSignatureBlue = Color.FromArgb(33, 40, 194);
        private static readonly StringFormat TextStringFormat = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Near,
            Trimming = StringTrimming.None,
            FormatFlags = StringFormatFlags.MeasureTrailingSpaces
        };

        public static void Draw(Graphics graphics, Rectangle bounds, SignatureAppearanceData data, bool transparentBackground)
        {
            if (graphics == null || bounds.Width <= 0 || bounds.Height <= 0 || data == null)
            {
                return;
            }

            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            var backgroundColor = transparentBackground
                ? Color.FromArgb(OutputBackgroundAlpha, 255, 255, 255)
                : Color.FromArgb(PreviewBackgroundAlpha, 255, 255, 255);
            using (var backgroundBrush = new SolidBrush(backgroundColor))
            {
                graphics.FillRectangle(backgroundBrush, bounds);
            }

            if (bounds.Width < 104 || bounds.Height < 36)
            {
                DrawGraphicOnly(graphics, Rectangle.Inflate(bounds, -4, -4), data, 0.995f);
                return;
            }

            var padding = Math.Max(3, Math.Min(bounds.Width, bounds.Height) / 36);
            var inner = Rectangle.Inflate(bounds, -padding, -padding);
            if (inner.Width < 40 || inner.Height < 30)
            {
                DrawGraphicOnly(graphics, inner, data, 0.995f);
                return;
            }

            var aspectRatio = inner.Width / (float)Math.Max(1, inner.Height);
            if (aspectRatio < 1.55f && inner.Height >= 66)
            {
                DrawStackedLayout(graphics, inner, data, padding, transparentBackground);
                return;
            }

            DrawHorizontalLayout(graphics, inner, data, padding, transparentBackground);
        }

        public static iTextSharp.text.Image CreatePdfGraphic(float widthPoints, float heightPoints, SignatureAppearanceData data)
        {
            var scale = widthPoints < SmallWidthPoints || heightPoints < SmallHeightPoints ? 18f : 15f;
            var width = Math.Max(900, (int)Math.Round(widthPoints * scale));
            var height = Math.Max(300, (int)Math.Round(heightPoints * scale));
            var widthCap = 3800;
            var heightCap = 1500;
            if (width > widthCap || height > heightCap)
            {
                var ratio = Math.Min(widthCap / (float)width, heightCap / (float)height);
                width = Math.Max(900, (int)Math.Round(width * ratio));
                height = Math.Max(300, (int)Math.Round(height * ratio));
            }

            using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            using (var graphics = Graphics.FromImage(bitmap))
            using (var stream = new MemoryStream())
            {
                graphics.Clear(Color.Transparent);
                Draw(graphics, new Rectangle(0, 0, width - 1, height - 1), data, true);
                bitmap.Save(stream, ImageFormat.Png);
                return iTextSharp.text.Image.GetInstance(stream.ToArray());
            }
        }

        public static string FormatAdobeLikeDate(DateTime signedAt)
        {
            var local = signedAt.Kind == DateTimeKind.Unspecified ? signedAt : signedAt.ToLocalTime();
            var offset = TimeZoneInfo.Local.GetUtcOffset(local);
            var sign = offset < TimeSpan.Zero ? "-" : "+";
            var absolute = offset.Duration();
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:yyyy.MM.dd HH:mm:ss} {1}{2:00}'{3:00}'",
                local,
                sign,
                absolute.Hours,
                absolute.Minutes);
        }

        private static void DrawHorizontalLayout(Graphics graphics, Rectangle inner, SignatureAppearanceData data, int padding, bool transparentBackground)
        {
            var compact = inner.Width < 280 || inner.Height < 78;
            var leftWidth = Math.Max(78, (int)Math.Round(inner.Width * (compact ? 0.48f : 0.44f)));
            var leftRect = new Rectangle(inner.Left, inner.Top, leftWidth, inner.Height);
            var separatorGap = Math.Max(2, padding / 3);
            var rightRect = new Rectangle(
                leftRect.Right + padding,
                inner.Top + 1,
                inner.Right - leftRect.Right - padding,
                Math.Max(24, inner.Height - 2));

            if (rightRect.Width < 70)
            {
                DrawGraphicOnly(graphics, inner, data, 0.99f);
                return;
            }

            using (var separatorPen = new Pen(Color.FromArgb(98, 178, 178, 178), 1))
            using (var textPanelBrush = new SolidBrush(Color.FromArgb(transparentBackground ? OutputTextPanelAlpha : PreviewTextPanelAlpha, 255, 255, 255)))
            {
                graphics.FillRectangle(textPanelBrush, rightRect);
                graphics.DrawLine(separatorPen, rightRect.Left - separatorGap, inner.Top + 2, rightRect.Left - separatorGap, inner.Bottom - 2);
            }

            DrawGraphicOnly(graphics, leftRect, data, 0.995f);
            DrawRightText(graphics, rightRect, data);
        }

        private static void DrawStackedLayout(Graphics graphics, Rectangle inner, SignatureAppearanceData data, int padding, bool transparentBackground)
        {
            var topHeight = Math.Max(34, (int)Math.Round(inner.Height * 0.56f));
            var topRect = new Rectangle(inner.Left, inner.Top, inner.Width, topHeight);
            var bottomRect = new Rectangle(inner.Left, topRect.Bottom + padding, inner.Width, inner.Bottom - topRect.Bottom - padding);

            if (bottomRect.Height < 24)
            {
                DrawGraphicOnly(graphics, inner, data, 0.995f);
                return;
            }

            using (var textPanelBrush = new SolidBrush(Color.FromArgb(transparentBackground ? OutputTextPanelAlpha : PreviewTextPanelAlpha, 255, 255, 255)))
            {
                graphics.FillRectangle(textPanelBrush, bottomRect);
            }

            DrawGraphicOnly(graphics, topRect, data, 0.995f);
            DrawRightText(graphics, bottomRect, data);
        }

        private static void DrawGraphicOnly(Graphics graphics, Rectangle targetRect, SignatureAppearanceData data, float usageRatio)
        {
            if (data.GraphicBytes == null || data.GraphicBytes.Length == 0)
            {
                using (var fallbackFont = CreateFont("Segoe UI", Math.Max(11f, targetRect.Height * 0.22f), FontStyle.Bold))
                using (var fallbackBrush = new SolidBrush(Color.FromArgb(30, 50, 160)))
                {
                    var format = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center,
                        Trimming = StringTrimming.EllipsisCharacter
                    };
                    graphics.DrawString(data.SignerName, fallbackFont, fallbackBrush, targetRect, format);
                }

                return;
            }

            using (var stream = new MemoryStream(data.GraphicBytes))
            using (var image = Image.FromStream(stream))
            {
                var fitted = FitRect(targetRect, image.Width, image.Height, usageRatio, 0.5f, 0.58f);
                graphics.DrawImage(image, fitted);
            }
        }

        private static void DrawRightText(Graphics graphics, Rectangle rightRect, SignatureAppearanceData data)
        {
            if (graphics == null || data == null || rightRect.Width <= 0 || rightRect.Height <= 0)
            {
                return;
            }

            var compact = rightRect.Width < 280 || rightRect.Height < 88;
            var tiny = rightRect.Width < 180 || rightRect.Height < 58;
            var textBounds = Rectangle.Inflate(rightRect, -4, -2);
            var baseSize = tiny
                ? 12.8f
                : compact
                    ? Math.Min(textBounds.Height * 0.19f, textBounds.Width * 0.065f)
                    : Math.Min(textBounds.Height * 0.145f, textBounds.Width * 0.05f);
            var minimumBaseSize = tiny ? 9.8f : compact ? 11.8f : 13.2f;
            var textColor = Color.FromArgb(14, 14, 14);
            var baseGap = tiny ? 2f : compact ? 4f : 6f;

            using (var textBrush = new SolidBrush(textColor))
            {
                List<RenderedLine> lines = null;
                Font contextFont = null;
                Font signerFont = null;
                Font sectionFont = null;
                Font bodyFont = null;
                Font footerFont = null;

                try
                {
                    while (baseSize >= minimumBaseSize)
                    {
                        if (contextFont != null)
                        {
                            contextFont.Dispose();
                        }

                        if (signerFont != null)
                        {
                            signerFont.Dispose();
                        }

                        if (sectionFont != null)
                        {
                            sectionFont.Dispose();
                        }

                        if (bodyFont != null)
                        {
                            bodyFont.Dispose();
                        }

                        if (footerFont != null)
                        {
                            footerFont.Dispose();
                        }

                        contextFont = CreatePreferredFont(new[] { "Century Gothic", "Arial", "Segoe UI" }, baseSize, FontStyle.Regular);
                        signerFont = CreatePreferredFont(new[] { "Century Gothic", "Arial", "Segoe UI" }, baseSize, FontStyle.Bold);
                        sectionFont = CreatePreferredFont(new[] { "Century Gothic", "Arial", "Segoe UI" }, baseSize, FontStyle.Bold);
                        bodyFont = CreatePreferredFont(new[] { "Century Gothic", "Arial", "Segoe UI" }, baseSize, FontStyle.Regular);
                        footerFont = CreatePreferredFont(new[] { "Century Gothic", "Arial", "Segoe UI" }, baseSize, FontStyle.Bold);
                        lines = BuildStyledRightTextLines(data, contextFont, signerFont, sectionFont, bodyFont, footerFont, graphics, textBounds.Width, compact, tiny);

                        var totalHeight = CalculateTotalHeight(graphics, lines, baseGap);
                        if (totalHeight <= textBounds.Height)
                        {
                            break;
                        }

                        baseSize -= tiny ? 0.3f : compact ? 0.35f : 0.4f;
                    }

                    var renderableLines = lines == null
                        ? new List<RenderedLine>()
                        : lines.Where(line => line != null && line.Font != null).ToList();
                    if (renderableLines.Count == 0)
                    {
                        DrawRightTextFallback(graphics, textBounds, data, textBrush);
                        return;
                    }

                    var totalHeightFinal = CalculateTotalHeight(graphics, renderableLines, baseGap);
                    var weightedGap = renderableLines.Sum(line => line.GapAfterMultiplier);
                    var extraPerWeight = weightedGap > 0f && totalHeightFinal < textBounds.Height
                        ? (textBounds.Height - totalHeightFinal) / weightedGap
                        : 0f;
                    var y = (float)textBounds.Top;

                    foreach (var line in renderableLines)
                    {
                        var lineHeight = MeasureHeight(graphics, line.Font);
                        var layoutRect = new RectangleF(textBounds.Left, y, textBounds.Width, lineHeight + 4);
                        graphics.DrawString(line.Text, line.Font, textBrush, layoutRect, TextStringFormat);
                        y += lineHeight + (baseGap + extraPerWeight) * line.GapAfterMultiplier;
                        if (y > textBounds.Bottom + 2)
                        {
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Write("Fallo al dibujar el bloque derecho de la firma: " + ex.Message);
                    DrawRightTextFallback(graphics, textBounds, data, textBrush);
                }
                finally
                {
                    if (contextFont != null)
                    {
                        contextFont.Dispose();
                    }

                    if (signerFont != null)
                    {
                        signerFont.Dispose();
                    }

                    if (sectionFont != null)
                    {
                        sectionFont.Dispose();
                    }

                    if (bodyFont != null)
                    {
                        bodyFont.Dispose();
                    }

                    if (footerFont != null)
                    {
                        footerFont.Dispose();
                    }
                }
            }
        }

        private static int CalculateTotalHeight(Graphics graphics, IList<RenderedLine> lines, float baseGap)
        {
            if (lines == null || lines.Count == 0)
            {
                return 0;
            }

            return (int)Math.Ceiling(lines
                .Where(line => line != null)
                .Sum(line => MeasureHeight(graphics, line.Font) + (baseGap * line.GapAfterMultiplier)));
        }

        private static List<RenderedLine> BuildStyledRightTextLines(
            SignatureAppearanceData data,
            Font contextFont,
            Font signerFont,
            Font sectionFont,
            Font bodyFont,
            Font footerFont,
            Graphics graphics,
            int maxWidth,
            bool compact,
            bool tiny)
        {
            var lines = new List<RenderedLine>();
            if (tiny)
            {
                AppendWrappedLine(lines, "Firmado digitalmente por " + data.SignerName, signerFont, graphics, maxWidth, 0.55f);
            }
            else
            {
                AppendWrappedLine(lines, "Firmado digitalmente por", contextFont, graphics, maxWidth, 0.28f);
                AppendWrappedLine(lines, data.SignerName, signerFont, graphics, maxWidth, compact ? 0.42f : 0.48f, 2);
            }

            if (!tiny)
            {
                lines.Add(new RenderedLine("Nombre de reconocimiento (DN):", sectionFont, 0.26f));

                var maxDnLines = compact ? 4 : 5;
                foreach (var line in BuildDnLines(data.DistinguishedName, bodyFont, graphics, maxWidth, maxDnLines))
                {
                    lines.Add(new RenderedLine(line, bodyFont, compact ? 0.18f : 0.22f));
                }
            }

            lines.Add(new RenderedLine("Fecha: " + FormatAdobeLikeDate(data.SignedAt), footerFont, 0f));
            return lines;
        }

        private static Rectangle FitRect(Rectangle target, int sourceWidth, int sourceHeight, float usageRatio, float horizontalBias, float verticalBias)
        {
            if (sourceWidth <= 0 || sourceHeight <= 0)
            {
                return target;
            }

            var workRect = Rectangle.Inflate(target, -(int)Math.Round(target.Width * (1f - usageRatio) / 2f), -(int)Math.Round(target.Height * (1f - usageRatio) / 2f));
            var ratio = Math.Min((float)workRect.Width / sourceWidth, (float)workRect.Height / sourceHeight);
            var width = Math.Max(1, (int)Math.Round(sourceWidth * ratio));
            var height = Math.Max(1, (int)Math.Round(sourceHeight * ratio));
            var x = workRect.Left + (int)Math.Round((workRect.Width - width) * ClampFloat(horizontalBias, 0f, 1f));
            var y = workRect.Top + (int)Math.Round((workRect.Height - height) * ClampFloat(verticalBias, 0f, 1f));
            return new Rectangle(x, y, width, height);
        }

        private static float ClampFloat(float value, float min, float max)
        {
            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
        }

        private static void AppendWrappedLine(List<RenderedLine> lines, string text, Font font, Graphics graphics, int maxWidth, float gapAfterMultiplier)
        {
            AppendWrappedLine(lines, text, font, graphics, maxWidth, gapAfterMultiplier, int.MaxValue);
        }

        private static void AppendWrappedLine(List<RenderedLine> lines, string text, Font font, Graphics graphics, int maxWidth, float gapAfterMultiplier, int maxLines)
        {
            var wrappedLines = WrapText(text, maxWidth, font, graphics).ToList();
            if (wrappedLines.Count > maxLines)
            {
                wrappedLines = wrappedLines.Take(maxLines).ToList();
                var overflow = string.Join(" ", WrapText(text, maxWidth, font, graphics).Skip(maxLines - 1));
                wrappedLines[maxLines - 1] = FitWithEllipsis(overflow, maxWidth, font, graphics);
            }

            for (var index = 0; index < wrappedLines.Count; index++)
            {
                var isLast = index == wrappedLines.Count - 1;
                lines.Add(new RenderedLine(wrappedLines[index], font, isLast ? gapAfterMultiplier : Math.Min(0.24f, gapAfterMultiplier)));
            }
        }

        private static string FitWithEllipsis(string text, int maxWidth, Font font, Graphics graphics)
        {
            var candidate = text.Trim();
            if (MeasureWidth(graphics, candidate, font) <= maxWidth)
            {
                return candidate;
            }

            while (candidate.Length > 3 && MeasureWidth(graphics, candidate + "...", font) > maxWidth)
            {
                candidate = candidate.Substring(0, candidate.Length - 1).TrimEnd();
            }

            return candidate + "...";
        }

        private static IEnumerable<string> BuildDnLines(string distinguishedName, Font font, Graphics graphics, int maxWidth, int maxLines)
        {
            var parts = BuildOrderedDnParts(distinguishedName).ToList();
            var lines = new List<string>();
            var current = string.Empty;

            foreach (var part in parts)
            {
                var piece = part + ",";
                if (string.IsNullOrEmpty(current))
                {
                    current = piece;
                    continue;
                }

                var candidate = current + " " + piece;
                if (MeasureWidth(graphics, candidate, font) <= maxWidth)
                {
                    current = candidate;
                }
                else
                {
                    lines.Add(current);
                    current = piece;
                }
            }

            if (!string.IsNullOrEmpty(current))
            {
                lines.Add(current.TrimEnd(','));
            }

            if (lines.Count > maxLines)
            {
                lines = lines.Take(maxLines).ToList();
                var last = lines[lines.Count - 1];
                while (last.Length > 3 && MeasureWidth(graphics, last + "...", font) > maxWidth)
                {
                    last = last.Substring(0, last.Length - 1);
                }

                lines[lines.Count - 1] = last + "...";
            }

            return lines;
        }

        private static IEnumerable<string> BuildOrderedDnParts(string distinguishedName)
        {
            var parsed = ParseDistinguishedName(distinguishedName);
            var parts = new List<string>();

            AppendDnPart(parts, parsed, "C", "c");
            AppendDnPart(parts, parsed, "SERIALNUMBER", "serialNumber");
            AppendDnPart(parts, parsed, "G", "givenName");
            AppendDnPart(parts, parsed, "GN", "givenName");
            AppendDnPart(parts, parsed, "GIVENNAME", "givenName");
            AppendDnPart(parts, parsed, "SN", "sn");
            AppendDnPart(parts, parsed, "CN", "cn");

            foreach (var pair in parsed.Where(pair => !IsPreferredDnKey(pair.Key)))
            {
                parts.Add(pair.Value.Label + "=" + pair.Value.Value);
            }

            return parts;
        }

        private static Dictionary<string, DnPart> ParseDistinguishedName(string distinguishedName)
        {
            var map = new Dictionary<string, DnPart>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(distinguishedName))
            {
                return map;
            }

            foreach (var rawPart in distinguishedName.Split(','))
            {
                var part = rawPart.Trim();
                var separator = part.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                var key = part.Substring(0, separator).Trim();
                var value = part.Substring(separator + 1).Trim();
                if (!map.ContainsKey(key))
                {
                    map[key] = new DnPart(key, value);
                }
            }

            return map;
        }

        private static void AppendDnPart(List<string> parts, Dictionary<string, DnPart> dn, string key, string label)
        {
            DnPart part;
            if (dn.TryGetValue(key, out part))
            {
                parts.Add(label + "=" + part.Value);
            }
        }

        private static bool IsPreferredDnKey(string key)
        {
            return string.Equals(key, "C", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(key, "SERIALNUMBER", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(key, "G", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(key, "GN", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(key, "GIVENNAME", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(key, "SN", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(key, "CN", StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> WrapText(string text, int maxWidth, Font font, Graphics graphics)
        {
            var lines = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
            {
                lines.Add(string.Empty);
                return lines;
            }

            var words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var current = string.Empty;

            foreach (var word in words)
            {
                var candidate = string.IsNullOrEmpty(current) ? word : current + " " + word;
                if (MeasureWidth(graphics, candidate, font) <= maxWidth || string.IsNullOrEmpty(current))
                {
                    current = candidate;
                }
                else
                {
                    lines.Add(current);
                    current = word;
                }
            }

            if (!string.IsNullOrEmpty(current))
            {
                lines.Add(current);
            }

            return lines;
        }

        private static Font CreateFont(string familyName, float size, FontStyle style)
        {
            try
            {
                return new Font(familyName, size, style, GraphicsUnit.Pixel);
            }
            catch
            {
                return new Font(SystemFonts.DefaultFont.FontFamily, size, style, GraphicsUnit.Pixel);
            }
        }

        private static Font CreatePreferredFont(IEnumerable<string> familyNames, float size, FontStyle style)
        {
            foreach (var familyName in familyNames)
            {
                try
                {
                    return new Font(familyName, size, style, GraphicsUnit.Pixel);
                }
                catch
                {
                }
            }

            return CreateFont(SystemFonts.MessageBoxFont.FontFamily.Name, size, style);
        }

        private static int MeasureWidth(Graphics graphics, string text, Font font)
        {
            if (graphics == null)
            {
                return 0;
            }

            var safeFont = font ?? SystemFonts.MessageBoxFont;
            return (int)Math.Ceiling(graphics.MeasureString(text ?? string.Empty, safeFont, int.MaxValue, TextStringFormat).Width);
        }

        private static int MeasureHeight(Graphics graphics, Font font)
        {
            if (graphics == null)
            {
                return 0;
            }

            var safeFont = font ?? SystemFonts.MessageBoxFont;
            return (int)Math.Ceiling(graphics.MeasureString("Ag", safeFont, int.MaxValue, TextStringFormat).Height);
        }

        private static void DrawRightTextFallback(Graphics graphics, Rectangle bounds, SignatureAppearanceData data, Brush textBrush)
        {
            if (graphics == null || data == null || textBrush == null || bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            var contextSize = Math.Max(11f, Math.Min(bounds.Height * 0.12f, bounds.Width * 0.04f));
            var signerSize = Math.Max(contextSize, Math.Min(bounds.Height * 0.15f, bounds.Width * 0.045f));
            var footerSize = Math.Max(10f, contextSize);

            using (var contextFont = CreatePreferredFont(new[] { "Century Gothic", "Arial", "Segoe UI" }, contextSize, FontStyle.Regular))
            using (var signerFont = CreatePreferredFont(new[] { "Century Gothic", "Arial", "Segoe UI" }, signerSize, FontStyle.Bold))
            using (var footerFont = CreatePreferredFont(new[] { "Century Gothic", "Arial", "Segoe UI" }, footerSize, FontStyle.Bold))
            {
                var y = (float)bounds.Top;
                DrawFallbackLine(graphics, "Firmado digitalmente por", contextFont, textBrush, bounds, ref y);
                DrawFallbackLine(graphics, data.SignerName, signerFont, textBrush, bounds, ref y);
                DrawFallbackLine(graphics, "Fecha: " + FormatAdobeLikeDate(data.SignedAt), footerFont, textBrush, bounds, ref y);
            }
        }

        private static void DrawFallbackLine(Graphics graphics, string text, Font font, Brush brush, Rectangle bounds, ref float y)
        {
            if (graphics == null || font == null || brush == null)
            {
                return;
            }

            var lineHeight = MeasureHeight(graphics, font);
            var layoutRect = new RectangleF(bounds.Left, y, bounds.Width, lineHeight + 4);
            graphics.DrawString(text ?? string.Empty, font, brush, layoutRect, TextStringFormat);
            y += lineHeight + 4f;
        }

        private sealed class RenderedLine
        {
            public RenderedLine(string text, Font font, float gapAfterMultiplier)
            {
                Text = text;
                Font = font;
                GapAfterMultiplier = gapAfterMultiplier;
            }

            public string Text { get; private set; }

            public Font Font { get; private set; }

            public float GapAfterMultiplier { get; private set; }
        }

        private sealed class DnPart
        {
            public DnPart(string label, string value)
            {
                Label = label;
                Value = value;
            }

            public string Label { get; private set; }

            public string Value { get; private set; }
        }
    }

    internal static class AdobeSignatureGraphicLoader
    {
        private const int MinimumStrongPixelsPerComponent = 8;
        private const int MinimumSoftPixelsPerComponent = 20;
        private const int SoftExpansionDepth = 1;
        private const int MaximumBranchLength = 10;
        private static readonly Color DefaultSignatureBlue = Color.FromArgb(33, 40, 194);

        public static byte[] TryLoadGraphicBytes()
        {
            return TryLoadGraphicBytes(null);
        }

        public static byte[] TryLoadGraphicBytes(string preferredGraphicPath)
        {
            try
            {
                var preferredGraphic = TryLoadGraphicFileBytes(preferredGraphicPath);
                if (preferredGraphic != null && preferredGraphic.Length > 0)
                {
                    AppLog.Write("Grafica de firma cargada desde la configuracion del certificado.");
                    return preferredGraphic;
                }

                var customGraphic = TryLoadCustomGraphicBytes();
                if (customGraphic != null && customGraphic.Length > 0)
                {
                    AppLog.Write("Grafica de firma cargada desde PNG personalizado.");
                    return customGraphic;
                }

                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var acrobatRoot = System.IO.Path.Combine(appData, "Adobe", "Acrobat");
                if (!Directory.Exists(acrobatRoot))
                {
                    return null;
                }

                var acrodataPath = Directory
                    .GetFiles(acrobatRoot, "appearances.acrodata", SearchOption.AllDirectories)
                    .Select(path => new FileInfo(path))
                    .OrderByDescending(info => info.LastWriteTimeUtc)
                    .Select(info => info.FullName)
                    .FirstOrDefault();

                if (string.IsNullOrWhiteSpace(acrodataPath))
                {
                    return null;
                }

                using (var reader = new PdfReader(acrodataPath))
                {
                    for (var pageIndex = 1; pageIndex <= reader.NumberOfPages; pageIndex++)
                    {
                        var page = reader.GetPageN(pageIndex);
                        var bytes = FindFirstImage(page.GetAsDict(PdfName.RESOURCES));
                        if (bytes != null && bytes.Length > 0)
                        {
                            var cleaned = CleanSignatureGraphic(bytes);
                            AppLog.Write("Grafica de Acrobat cargada desde: " + acrodataPath);
                            return cleaned ?? bytes;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Write("No se pudo cargar la grafica de Acrobat: " + ex.Message);
            }

            return null;
        }

        private static byte[] TryLoadCustomGraphicBytes()
        {
            var candidatePaths = new[]
            {
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "firma_limpia.png"),
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "firma.png"),
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "signature.png")
            };

            foreach (var path in candidatePaths.Where(File.Exists))
            {
                var graphic = TryLoadGraphicFileBytes(path);
                if (graphic != null && graphic.Length > 0)
                {
                    AppLog.Write("Se usara la grafica personalizada: " + path);
                    return graphic;
                }
            }

            return null;
        }

        private static byte[] TryLoadGraphicFileBytes(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            try
            {
                using (var stream = File.OpenRead(path))
                using (var image = Image.FromStream(stream, false, true))
                using (var normalized = NormalizeCustomGraphic(image))
                using (var output = new MemoryStream())
                {
                    normalized.Save(output, ImageFormat.Png);
                    return output.ToArray();
                }
            }
            catch (Exception ex)
            {
                AppLog.Write("No se pudo cargar la grafica personalizada " + path + ": " + ex.Message);
            }

            return null;
        }

        private static byte[] FindFirstImage(PdfDictionary resources)
        {
            if (resources == null)
            {
                return null;
            }

            var xobj = resources.GetAsDict(PdfName.XOBJECT);
            if (xobj == null)
            {
                return null;
            }

            foreach (PdfName name in xobj.Keys)
            {
                var stream = xobj.GetDirectObject(name) as PRStream;
                if (stream == null)
                {
                    continue;
                }

                var subtype = stream.GetAsName(PdfName.SUBTYPE);
                if (PdfName.IMAGE.Equals(subtype))
                {
                    var image = new PdfImageObject(stream);
                    return image.GetImageAsBytes();
                }

                if (PdfName.FORM.Equals(subtype))
                {
                    var nested = FindFirstImage(stream.GetAsDict(PdfName.RESOURCES));
                    if (nested != null && nested.Length > 0)
                    {
                        return nested;
                    }
                }
            }

            return null;
        }

        private static byte[] CleanSignatureGraphic(byte[] imageBytes)
        {
            try
            {
                using (var sourceStream = new MemoryStream(imageBytes))
                using (var sourceBitmap = new Bitmap(sourceStream))
                {
                    var width = sourceBitmap.Width;
                    var height = sourceBitmap.Height;
                    var strongMask = new bool[width, height];
                    var softMask = new bool[width, height];
                    var colors = new Color[width, height];

                    for (var x = 0; x < width; x++)
                    {
                        for (var y = 0; y < height; y++)
                        {
                            var pixel = sourceBitmap.GetPixel(x, y);
                            colors[x, y] = pixel;

                            strongMask[x, y] = IsStrongSignaturePixel(pixel);
                            softMask[x, y] = strongMask[x, y] || IsSoftSignaturePixel(pixel);
                        }
                    }

                    var refined = KeepMeaningfulComponents(softMask, strongMask, width, height);
                    refined = PruneShortBranches(refined, width, height);
                    var signatureColor = ResolveSignatureColor(colors, refined, strongMask, width, height);

                    using (var cleanedBitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
                    {
                        var minX = width;
                        var minY = height;
                        var maxX = -1;
                        var maxY = -1;

                        for (var x = 0; x < width; x++)
                        {
                            for (var y = 0; y < height; y++)
                            {
                                if (!refined[x, y])
                                {
                                    cleanedBitmap.SetPixel(x, y, Color.Transparent);
                                    continue;
                                }

                                var pixel = colors[x, y];
                                var alpha = ResolveAlpha(pixel, strongMask[x, y]);
                                cleanedBitmap.SetPixel(x, y, Color.FromArgb(alpha, signatureColor.R, signatureColor.G, signatureColor.B));

                                if (x < minX) minX = x;
                                if (y < minY) minY = y;
                                if (x > maxX) maxX = x;
                                if (y > maxY) maxY = y;
                            }
                        }

                        if (maxX < minX || maxY < minY)
                        {
                            return imageBytes;
                        }

                        minX = Math.Max(0, minX - 3);
                        minY = Math.Max(0, minY - 3);
                        maxX = Math.Min(width - 1, maxX + 3);
                        maxY = Math.Min(height - 1, maxY + 3);

                        var cropWidth = (maxX - minX) + 1;
                        var cropHeight = (maxY - minY) + 1;

                        using (var croppedBitmap = new Bitmap(cropWidth, cropHeight, PixelFormat.Format32bppArgb))
                        using (var graphics = Graphics.FromImage(croppedBitmap))
                        using (var outputStream = new MemoryStream())
                        {
                            graphics.Clear(Color.Transparent);
                            graphics.DrawImage(
                                cleanedBitmap,
                                new Rectangle(0, 0, cropWidth, cropHeight),
                                new Rectangle(minX, minY, cropWidth, cropHeight),
                                GraphicsUnit.Pixel);
                            croppedBitmap.Save(outputStream, ImageFormat.Png);
                            return outputStream.ToArray();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Write("No se pudo limpiar la grafica de Acrobat: " + ex.Message);
                return imageBytes;
            }
        }

        private static Bitmap NormalizeCustomGraphic(Image sourceImage)
        {
            var sourceBitmap = new Bitmap(sourceImage);
            var width = sourceBitmap.Width;
            var height = sourceBitmap.Height;
            var minX = width;
            var minY = height;
            var maxX = -1;
            var maxY = -1;

            for (var x = 0; x < width; x++)
            {
                for (var y = 0; y < height; y++)
                {
                    var pixel = sourceBitmap.GetPixel(x, y);
                    if (pixel.A <= 4)
                    {
                        continue;
                    }

                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
            }

            if (maxX < minX || maxY < minY)
            {
                return sourceBitmap;
            }

            minX = Math.Max(0, minX - 2);
            minY = Math.Max(0, minY - 2);
            maxX = Math.Min(width - 1, maxX + 2);
            maxY = Math.Min(height - 1, maxY + 2);

            var cropWidth = (maxX - minX) + 1;
            var cropHeight = (maxY - minY) + 1;
            var cropped = new Bitmap(cropWidth, cropHeight, PixelFormat.Format32bppArgb);

            using (var graphics = Graphics.FromImage(cropped))
            {
                graphics.Clear(Color.Transparent);
                graphics.DrawImage(
                    sourceBitmap,
                    new Rectangle(0, 0, cropWidth, cropHeight),
                    new Rectangle(minX, minY, cropWidth, cropHeight),
                    GraphicsUnit.Pixel);
            }

            sourceBitmap.Dispose();
            return cropped;
        }

        private static bool[,] KeepMeaningfulComponents(bool[,] softMask, bool[,] strongMask, int width, int height)
        {
            var kept = new bool[width, height];
            var visited = new bool[width, height];
            var dx = new[] { -1, -1, -1, 0, 0, 1, 1, 1 };
            var dy = new[] { -1, 0, 1, -1, 1, -1, 0, 1 };
            var components = new List<Component>();

            for (var x = 0; x < width; x++)
            {
                for (var y = 0; y < height; y++)
                {
                    if (!softMask[x, y] || visited[x, y])
                    {
                        continue;
                    }

                    var pixels = new List<Point>();
                    var queue = new Queue<Point>();
                    queue.Enqueue(new Point(x, y));
                    visited[x, y] = true;
                    var strongPixels = 0;
                    var minX = x;
                    var maxX = x;
                    var minY = y;
                    var maxY = y;

                    while (queue.Count > 0)
                    {
                        var point = queue.Dequeue();
                        pixels.Add(point);
                        if (strongMask[point.X, point.Y])
                        {
                            strongPixels++;
                        }

                        if (point.X < minX) minX = point.X;
                        if (point.X > maxX) maxX = point.X;
                        if (point.Y < minY) minY = point.Y;
                        if (point.Y > maxY) maxY = point.Y;

                        for (var index = 0; index < dx.Length; index++)
                        {
                            var nx = point.X + dx[index];
                            var ny = point.Y + dy[index];
                            if (nx < 0 || ny < 0 || nx >= width || ny >= height || visited[nx, ny] || !softMask[nx, ny])
                            {
                                continue;
                            }

                            visited[nx, ny] = true;
                            queue.Enqueue(new Point(nx, ny));
                        }
                    }

                    components.Add(new Component(pixels, strongPixels, minX, minY, maxX, maxY));
                }
            }

            var keptComponents = components
                .Where(component =>
                    component.StrongPixels >= MinimumStrongPixelsPerComponent &&
                    component.Pixels.Count >= MinimumSoftPixelsPerComponent)
                .OrderByDescending(component => component.StrongPixels * 8 + component.Pixels.Count)
                .Take(8)
                .ToList();

            foreach (var component in keptComponents)
            {
                ExpandFromStrongPixels(component, softMask, strongMask, kept, width, height, dx, dy);
            }

            return kept;
        }

        private static bool[,] PruneShortBranches(bool[,] mask, int width, int height)
        {
            var pruned = (bool[,])mask.Clone();
            var dx = new[] { -1, -1, -1, 0, 0, 1, 1, 1 };
            var dy = new[] { -1, 0, 1, -1, 1, -1, 0, 1 };

            for (var pass = 0; pass < 2; pass++)
            {
                var pointsToRemove = new HashSet<int>();
                for (var x = 0; x < width; x++)
                {
                    for (var y = 0; y < height; y++)
                    {
                        if (!pruned[x, y] || CountNeighbors(pruned, x, y, width, height, dx, dy) != 1)
                        {
                            continue;
                        }

                        var branch = TraceBranch(pruned, x, y, width, height, dx, dy);
                        if (branch == null)
                        {
                            continue;
                        }

                        foreach (var point in branch)
                        {
                            pointsToRemove.Add((point.Y * width) + point.X);
                        }
                    }
                }

                if (pointsToRemove.Count == 0)
                {
                    break;
                }

                foreach (var encodedPoint in pointsToRemove)
                {
                    var x = encodedPoint % width;
                    var y = encodedPoint / width;
                    pruned[x, y] = false;
                }
            }

            return pruned;
        }

        private static bool IsStrongSignaturePixel(Color pixel)
        {
            var blueDominance = pixel.B - Math.Max(pixel.R, pixel.G);
            var saturation = Math.Max(pixel.R, Math.Max(pixel.G, pixel.B)) - Math.Min(pixel.R, Math.Min(pixel.G, pixel.B));
            return pixel.B >= 146 && blueDominance >= 58 && saturation >= 46 && pixel.R <= 190 && pixel.G <= 190;
        }

        private static bool IsSoftSignaturePixel(Color pixel)
        {
            var blueDominance = pixel.B - Math.Max(pixel.R, pixel.G);
            var saturation = Math.Max(pixel.R, Math.Max(pixel.G, pixel.B)) - Math.Min(pixel.R, Math.Min(pixel.G, pixel.B));
            return pixel.B >= 128 && blueDominance >= 38 && saturation >= 32 && pixel.R <= 210 && pixel.G <= 210;
        }

        private static void ExpandFromStrongPixels(
            Component component,
            bool[,] softMask,
            bool[,] strongMask,
            bool[,] kept,
            int width,
            int height,
            int[] dx,
            int[] dy)
        {
            var visited = new bool[width, height];
            var queue = new Queue<PixelStep>();

            foreach (var point in component.Pixels)
            {
                if (!strongMask[point.X, point.Y])
                {
                    continue;
                }

                visited[point.X, point.Y] = true;
                kept[point.X, point.Y] = true;
                queue.Enqueue(new PixelStep(point.X, point.Y, 0));
            }

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (current.Depth >= SoftExpansionDepth)
                {
                    continue;
                }

                for (var index = 0; index < dx.Length; index++)
                {
                    var nx = current.X + dx[index];
                    var ny = current.Y + dy[index];
                    if (nx < component.MinX || ny < component.MinY || nx > component.MaxX || ny > component.MaxY)
                    {
                        continue;
                    }

                    if (nx < 0 || ny < 0 || nx >= width || ny >= height || visited[nx, ny] || !softMask[nx, ny])
                    {
                        continue;
                    }

                    visited[nx, ny] = true;
                    kept[nx, ny] = true;
                    queue.Enqueue(new PixelStep(nx, ny, current.Depth + 1));
                }
            }
        }

        private static List<Point> TraceBranch(bool[,] mask, int startX, int startY, int width, int height, int[] dx, int[] dy)
        {
            var path = new List<Point>();
            var previous = new Point(-1, -1);
            var current = new Point(startX, startY);

            while (true)
            {
                path.Add(current);
                if (path.Count > MaximumBranchLength)
                {
                    return null;
                }

                var nextPoints = GetNeighborPoints(mask, current.X, current.Y, width, height, dx, dy, previous);
                if (nextPoints.Count == 0)
                {
                    return path.Count <= MaximumBranchLength ? path : null;
                }

                if (nextPoints.Count > 1)
                {
                    path.RemoveAt(path.Count - 1);
                    return path.Count > 0 && path.Count <= MaximumBranchLength ? path : null;
                }

                previous = current;
                current = nextPoints[0];
            }
        }

        private static List<Point> GetNeighborPoints(bool[,] mask, int x, int y, int width, int height, int[] dx, int[] dy, Point exclude)
        {
            var neighbors = new List<Point>();
            for (var index = 0; index < dx.Length; index++)
            {
                var nx = x + dx[index];
                var ny = y + dy[index];
                if (nx < 0 || ny < 0 || nx >= width || ny >= height || !mask[nx, ny])
                {
                    continue;
                }

                if (exclude.X == nx && exclude.Y == ny)
                {
                    continue;
                }

                neighbors.Add(new Point(nx, ny));
            }

            return neighbors;
        }

        private static int CountNeighbors(bool[,] mask, int x, int y, int width, int height, int[] dx, int[] dy)
        {
            var count = 0;
            for (var index = 0; index < dx.Length; index++)
            {
                var nx = x + dx[index];
                var ny = y + dy[index];
                if (nx < 0 || ny < 0 || nx >= width || ny >= height || !mask[nx, ny])
                {
                    continue;
                }

                count++;
            }

            return count;
        }

        private static Color ResolveSignatureColor(Color[,] colors, bool[,] refinedMask, bool[,] strongMask, int width, int height)
        {
            long red = 0;
            long green = 0;
            long blue = 0;
            long count = 0;

            for (var x = 0; x < width; x++)
            {
                for (var y = 0; y < height; y++)
                {
                    if (!refinedMask[x, y] || !strongMask[x, y])
                    {
                        continue;
                    }

                    var pixel = colors[x, y];
                    red += pixel.R;
                    green += pixel.G;
                    blue += pixel.B;
                    count++;
                }
            }

            if (count == 0)
            {
                return DefaultSignatureBlue;
            }

            var average = Color.FromArgb(
                ClampByte((int)Math.Round(red / (double)count)),
                ClampByte((int)Math.Round(green / (double)count)),
                ClampByte((int)Math.Round(blue / (double)count)));

            return Color.FromArgb(
                ClampByte(Math.Min((int)average.R, 56)),
                ClampByte(Math.Min((int)average.G, 72)),
                ClampByte(Math.Max((int)average.B, 180)));
        }

        private static int ResolveAlpha(Color pixel, bool strongPixel)
        {
            if (strongPixel)
            {
                return 255;
            }

            var blueDominance = pixel.B - Math.Max(pixel.R, pixel.G);
            var brightness = (pixel.R + pixel.G + pixel.B) / 3;
            var alpha = 78 + (blueDominance * 3) + ((255 - brightness) / 2);
            return ClampByte(alpha);
        }

        private static int ClampByte(int value)
        {
            if (value < 0)
            {
                return 0;
            }

            if (value > 255)
            {
                return 255;
            }

            return value;
        }

        private sealed class Component
        {
            public Component(List<Point> pixels, int strongPixels, int minX, int minY, int maxX, int maxY)
            {
                Pixels = pixels;
                StrongPixels = strongPixels;
                MinX = minX;
                MinY = minY;
                MaxX = maxX;
                MaxY = maxY;
            }

            public List<Point> Pixels { get; private set; }

            public int StrongPixels { get; private set; }

            public int MinX { get; private set; }

            public int MinY { get; private set; }

            public int MaxX { get; private set; }

            public int MaxY { get; private set; }
        }

        private struct PixelStep
        {
            public int X;
            public int Y;
            public int Depth;

            public PixelStep(int x, int y, int depth)
            {
                X = x;
                Y = y;
                Depth = depth;
            }
        }
    }
}

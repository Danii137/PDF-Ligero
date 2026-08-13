using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using iTextSharp.text;
using iTextSharp.text.pdf;
using PdfRectangle = iTextSharp.text.Rectangle;

namespace FirmaAutomatica
{
    internal sealed class PdfAnnotationSaveResult
    {
        internal PdfAnnotationSaveResult(
            string outputPath,
            int annotationCount,
            bool digitalSignaturesInvalidated)
        {
            OutputPath = outputPath;
            AnnotationCount = annotationCount;
            DigitalSignaturesInvalidated = digitalSignaturesInvalidated;
        }

        public string OutputPath { get; private set; }

        public int AnnotationCount { get; private set; }

        public bool DigitalSignaturesInvalidated { get; private set; }
    }

    /// <summary>
    /// Escribe las marcas como anotaciones PDF de verdad.
    ///
    /// Son anotaciones estandar, no dibujos fundidos con la pagina: se ven en
    /// Acrobat y en el movil, quien reciba el documento puede borrarlas, y el
    /// contenido original no se toca. Se añaden en una revision incremental,
    /// igual que hace la edicion de texto, de modo que el archivo de partida
    /// nunca se sobrescribe.
    /// </summary>
    internal static class PdfAnnotationService
    {
        public const string DigitalSignatureWarning =
            "La firma digital anterior permanece incrustada, pero el " +
            "documento cambia al añadir las marcas, asi que dejara de " +
            "considerarse valida.";

        /// <summary>
        /// Lee las marcas que ya tiene un PDF.
        ///
        /// Hace falta porque PDFium, el motor con el que la aplicacion pinta las
        /// paginas, NO dibuja anotaciones: se comprobo escribiendolas de las dos
        /// formas posibles, con la caja de apariencia en coordenadas de pagina y
        /// en el origen, y en ningun caso aparecen en pantalla. Y esa version de
        /// PDFium esta congelada: la ultima compilacion compatible con el
        /// envoltorio es de 2018.
        ///
        /// Asi que las marcas se guardan como anotaciones de verdad, para que se
        /// vean en Acrobat y en el movil, pero dentro de PDF Ligero las dibuja
        /// la propia aplicacion sobre la pagina. De paso, se ven igual mientras
        /// se dibujan y despues de guardar.
        ///
        /// No propaga excepciones: un PDF cuyas anotaciones no se puedan leer se
        /// tiene que poder abrir igualmente.
        /// </summary>
        public static IList<PdfAnnotationItem> Read(string pdfPath)
        {
            var leidas = new List<PdfAnnotationItem>();
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
            {
                return leidas;
            }

            PdfReader reader = null;
            try
            {
                reader = new PdfReader(pdfPath);
                for (var page = 1; page <= reader.NumberOfPages; page++)
                {
                    var pageDictionary = reader.GetPageN(page);
                    if (pageDictionary == null)
                    {
                        continue;
                    }

                    var annots = pageDictionary.GetAsArray(PdfName.ANNOTS);
                    if (annots == null)
                    {
                        continue;
                    }

                    var transform = PdfTextPageTransform.Create(reader, page);
                    for (var i = 0; i < annots.Size; i++)
                    {
                        var item = ReadOne(annots.GetAsDict(i), transform, page);
                        if (item != null)
                        {
                            leidas.Add(item);
                        }
                    }
                }
            }
            catch (Exception)
            {
                return leidas;
            }
            finally
            {
                if (reader != null)
                {
                    reader.Close();
                }
            }

            return leidas;
        }

        private static PdfAnnotationItem ReadOne(
            PdfDictionary annotation,
            PdfTextPageTransform transform,
            int page)
        {
            if (annotation == null)
            {
                return null;
            }

            var subtype = annotation.Get(PdfName.SUBTYPE) as PdfName;
            if (subtype == null)
            {
                return null;
            }

            PdfAnnotationItem item;
            if (PdfName.INK.Equals(subtype))
            {
                item = ReadInk(annotation, transform, page);
            }
            else if (PdfName.HIGHLIGHT.Equals(subtype))
            {
                item = new PdfAnnotationItem(PdfAnnotationKind.Highlight, page);
                item.Area = ReadArea(annotation, transform);
                item.Opacity = ReadOpacity(annotation, 0.4F);
            }
            else if (PdfName.TEXT.Equals(subtype))
            {
                item = new PdfAnnotationItem(PdfAnnotationKind.Note, page);
                item.Area = ReadArea(annotation, transform);
            }
            else
            {
                // Enlaces, widgets de formulario y firmas no son marcas nuestras.
                return null;
            }

            if (item == null)
            {
                return null;
            }

            var color = annotation.GetAsArray(PdfName.C);
            if (color != null && color.Size >= 3)
            {
                item.Color = Color.FromArgb(
                    ToByte(color.GetAsNumber(0)),
                    ToByte(color.GetAsNumber(1)),
                    ToByte(color.GetAsNumber(2)));
            }

            var contents = annotation.GetAsString(PdfName.CONTENTS);
            if (contents != null)
            {
                item.Contents = contents.ToUnicodeString();
            }

            var author = annotation.GetAsString(PdfName.T);
            if (author != null)
            {
                item.Author = author.ToUnicodeString();
            }

            var border = annotation.GetAsDict(PdfName.BS);
            if (border != null)
            {
                var width = border.GetAsNumber(PdfName.W);
                if (width != null)
                {
                    item.WidthPoints = Math.Max(0.5F, width.FloatValue);
                }
            }

            return item.IsEmpty() ? null : item;
        }

        private static PdfAnnotationItem ReadInk(
            PdfDictionary annotation,
            PdfTextPageTransform transform,
            int page)
        {
            var inkList = annotation.GetAsArray(PdfName.INKLIST);
            if (inkList == null || inkList.Size == 0)
            {
                return null;
            }

            var item = new PdfAnnotationItem(PdfAnnotationKind.Ink, page);
            for (var i = 0; i < inkList.Size; i++)
            {
                var stroke = inkList.GetAsArray(i);
                if (stroke == null || stroke.Size < 4)
                {
                    continue;
                }

                item.BeginStroke();
                for (var p = 0; p + 1 < stroke.Size; p += 2)
                {
                    var x = stroke.GetAsNumber(p);
                    var y = stroke.GetAsNumber(p + 1);
                    if (x == null || y == null)
                    {
                        continue;
                    }

                    var visual = transform.RawToVisual(
                        x.FloatValue,
                        y.FloatValue);
                    item.AddPoint(new PointF(visual.X, visual.Y));
                }
            }

            item.DropEmptyStrokes();
            return item;
        }

        private static RectangleF ReadArea(
            PdfDictionary annotation,
            PdfTextPageTransform transform)
        {
            var rect = annotation.GetAsArray(PdfName.RECT);
            if (rect == null || rect.Size < 4)
            {
                return RectangleF.Empty;
            }

            var a = transform.RawToVisual(
                rect.GetAsNumber(0).FloatValue,
                rect.GetAsNumber(1).FloatValue);
            var b = transform.RawToVisual(
                rect.GetAsNumber(2).FloatValue,
                rect.GetAsNumber(3).FloatValue);

            return new RectangleF(
                Math.Min(a.X, b.X),
                Math.Min(a.Y, b.Y),
                Math.Abs(b.X - a.X),
                Math.Abs(b.Y - a.Y));
        }

        private static float ReadOpacity(
            PdfDictionary annotation,
            float fallback)
        {
            var ca = annotation.GetAsNumber(PdfName.CA);
            return ca == null ? fallback : Clamp(ca.FloatValue, 0.1F, 1F);
        }

        private static int ToByte(PdfNumber number)
        {
            if (number == null)
            {
                return 0;
            }

            var valor = (int)Math.Round(number.FloatValue * 255F);
            return Math.Max(0, Math.Min(255, valor));
        }

        /// <summary>
        /// Escribe las marcas en outputPath. No modifica el original.
        /// </summary>
        public static PdfAnnotationSaveResult Save(
            string sourcePdfPath,
            string outputPath,
            PdfAnnotationBatch batch,
            PdfEditViewIdentity expectedView)
        {
            if (string.IsNullOrWhiteSpace(sourcePdfPath))
            {
                throw new ArgumentException(
                    "Se necesita el PDF de origen.",
                    "sourcePdfPath");
            }
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new ArgumentException(
                    "Se necesita la ruta de salida.",
                    "outputPath");
            }
            if (batch == null)
            {
                throw new ArgumentNullException("batch");
            }
            if (!batch.HasPending)
            {
                throw new InvalidOperationException(
                    "No hay ninguna marca que guardar.");
            }

            var sourcePath = Path.GetFullPath(sourcePdfPath);
            var target = Path.GetFullPath(outputPath);
            if (string.Equals(
                    sourcePath,
                    target,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Las marcas no pueden sobrescribir el PDF de origen.");
            }

            var temporaryPath = Path.Combine(
                Path.GetDirectoryName(target) ?? string.Empty,
                "." + Path.GetFileName(target) + "." +
                Guid.NewGuid().ToString("N") + ".tmp");

            var signaturesInvalidated = false;
            var written = 0;
            try
            {
                written = WriteAnnotatedPdf(
                    sourcePath,
                    temporaryPath,
                    batch,
                    expectedView,
                    out signaturesInvalidated);

                if (File.Exists(target))
                {
                    File.Delete(target);
                }

                File.Move(temporaryPath, target);
            }
            finally
            {
                TryDelete(temporaryPath);
            }

            return new PdfAnnotationSaveResult(
                target,
                written,
                signaturesInvalidated);
        }

        private static int WriteAnnotatedPdf(
            string sourcePath,
            string temporaryPath,
            PdfAnnotationBatch batch,
            PdfEditViewIdentity expectedView,
            out bool signaturesInvalidated)
        {
            PdfReader reader = null;
            PdfStamper stamper = null;
            FileStream output = null;
            signaturesInvalidated = false;

            try
            {
                reader = new PdfReader(sourcePath);
                if (!reader.IsOpenedWithFullPermissions)
                {
                    throw new UnauthorizedAccessException(
                        "El PDF esta protegido y no permite añadir marcas.");
                }
                if (ContainsXfa(reader))
                {
                    throw new NotSupportedException(
                        "Este PDF usa formularios XFA y la herramienta no " +
                        "puede escribir en el sin arriesgarse a dañarlo.");
                }

                signaturesInvalidated =
                    reader.AcroFields != null &&
                    reader.AcroFields.GetSignatureNames().Count > 0;

                var sourceXmpMetadata = reader.Metadata;

                output = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    1024 * 1024,
                    FileOptions.SequentialScan | FileOptions.WriteThrough);

                // Revision incremental: el contenido anterior se conserva tal
                // cual y solo se añaden los objetos nuevos.
                stamper = new PdfStamper(reader, output, '\0', true);
                stamper.Writer.CloseStream = false;
                stamper.RotateContents = false;
                stamper.MoreInfo = CloneInfo(reader.Info);

                // Igual que en el editor de texto: sin esto PdfStamper regenera
                // el paquete XMP y pierde propiedades que no sabe reconstruir.
                if (sourceXmpMetadata != null && sourceXmpMetadata.Length > 0)
                {
                    stamper.XmpMetadata = sourceXmpMetadata;
                }

                var escritas = 0;
                foreach (var item in batch.Items)
                {
                    if (item.IsEmpty())
                    {
                        continue;
                    }
                    if (item.PageNumber < 1 ||
                        item.PageNumber > reader.NumberOfPages)
                    {
                        continue;
                    }

                    var annotation = Build(stamper, reader, item);
                    if (annotation == null)
                    {
                        continue;
                    }

                    stamper.AddAnnotation(annotation, item.PageNumber);
                    escritas++;
                }

                if (escritas == 0)
                {
                    throw new InvalidOperationException(
                        "Ninguna de las marcas se pudo escribir.");
                }

                stamper.Close();
                stamper = null;
                output.Flush(true);
                return escritas;
            }
            finally
            {
                if (stamper != null)
                {
                    try
                    {
                        stamper.Close();
                    }
                    catch (Exception)
                    {
                    }
                }
                if (output != null)
                {
                    output.Dispose();
                }
                if (reader != null)
                {
                    reader.Close();
                }
            }
        }

        private static PdfAnnotation Build(
            PdfStamper stamper,
            PdfReader reader,
            PdfAnnotationItem item)
        {
            var transform = PdfTextPageTransform.Create(
                reader,
                item.PageNumber);

            if (item.Kind == PdfAnnotationKind.Ink)
            {
                return BuildInk(stamper, transform, item);
            }
            if (item.Kind == PdfAnnotationKind.Highlight)
            {
                return BuildHighlight(stamper, transform, item);
            }

            return BuildNote(stamper, transform, item);
        }

        private static PdfAnnotation BuildInk(
            PdfStamper stamper,
            PdfTextPageTransform transform,
            PdfAnnotationItem item)
        {
            var listas = new List<float[]>();
            foreach (var stroke in item.Strokes)
            {
                if (stroke.Count < 2)
                {
                    continue;
                }

                var puntos = new float[stroke.Count * 2];
                for (var i = 0; i < stroke.Count; i++)
                {
                    var crudo = transform.VisualToRaw(
                        stroke[i].X,
                        stroke[i].Y);
                    puntos[i * 2] = crudo.X;
                    puntos[(i * 2) + 1] = crudo.Y;
                }

                listas.Add(puntos);
            }

            if (listas.Count == 0)
            {
                return null;
            }

            var rect = ToRawRectangle(transform, item.GetBounds());
            var annotation = PdfAnnotation.CreateInk(
                stamper.Writer,
                rect,
                item.Describe(),
                listas.ToArray());

            annotation.Color = ToBaseColor(item.Color);
            annotation.BorderStyle = new PdfBorderDictionary(
                Math.Max(0.5F, item.WidthPoints),
                PdfBorderDictionary.STYLE_SOLID);

            // Apariencia dibujada a mano. PDFium, que es el motor con el que la
            // aplicacion pinta las paginas, NO genera apariencias: solo dibuja
            // lo que venga en /AP. Sin esto la marca existe en el archivo pero
            // no se ve al reabrirlo, que es justo lo que no queremos.
            var appearance = CreateAppearance(stamper, item.PageNumber, rect);
            appearance.SetColorStroke(ToBaseColor(item.Color));
            appearance.SetLineWidth(Math.Max(0.5F, item.WidthPoints));
            appearance.SetLineCap(PdfContentByte.LINE_CAP_ROUND);
            appearance.SetLineJoin(PdfContentByte.LINE_JOIN_ROUND);
            if (item.Opacity < 0.999F)
            {
                var estado = new PdfGState();
                estado.StrokeOpacity = Clamp(item.Opacity, 0.1F, 1F);
                appearance.SetGState(estado);
            }

            foreach (var puntos in listas)
            {
                appearance.MoveTo(puntos[0], puntos[1]);
                for (var i = 2; i + 1 < puntos.Length; i += 2)
                {
                    appearance.LineTo(puntos[i], puntos[i + 1]);
                }
            }

            appearance.Stroke();
            annotation.SetAppearance(PdfName.N, appearance);

            ApplyCommon(annotation, item);
            return annotation;
        }

        private static PdfAnnotation BuildHighlight(
            PdfStamper stamper,
            PdfTextPageTransform transform,
            PdfAnnotationItem item)
        {
            var rect = ToRawRectangle(transform, item.Area);

            // Los quadPoints van en el orden que pide el formato: superior
            // izquierda, superior derecha, inferior izquierda, inferior derecha.
            var quad = new[]
            {
                rect.Left, rect.Top,
                rect.Right, rect.Top,
                rect.Left, rect.Bottom,
                rect.Right, rect.Bottom
            };

            var annotation = PdfAnnotation.CreateMarkup(
                stamper.Writer,
                rect,
                item.Describe(),
                PdfAnnotation.MARKUP_HIGHLIGHT,
                quad);
            annotation.Color = ToBaseColor(item.Color);
            ApplyCommon(annotation, item);

            // Apariencia propia, por dos motivos: PDFium no genera ninguna, y
            // el modo de fusion multiplicar deja leer el texto por debajo en
            // vez de taparlo.
            var appearance = CreateAppearance(stamper, item.PageNumber, rect);
            var estado = new PdfGState();
            estado.BlendMode = PdfGState.BM_MULTIPLY;
            estado.FillOpacity = Clamp(item.Opacity, 0.1F, 1F);
            appearance.SetGState(estado);
            appearance.SetColorFill(ToBaseColor(item.Color));
            appearance.Rectangle(
                rect.Left,
                rect.Bottom,
                rect.Width,
                rect.Height);
            appearance.Fill();
            annotation.SetAppearance(PdfName.N, appearance);

            return annotation;
        }

        private static PdfAnnotation BuildNote(
            PdfStamper stamper,
            PdfTextPageTransform transform,
            PdfAnnotationItem item)
        {
            var area = item.Area;
            if (area.Width <= 0.01F || area.Height <= 0.01F)
            {
                // Una nota es un icono de tamaño fijo; basta con su ancla.
                area = new RectangleF(area.X, area.Y, 20F, 20F);
            }

            var rect = ToRawRectangle(transform, area);
            var annotation = PdfAnnotation.CreateText(
                stamper.Writer,
                rect,
                string.IsNullOrEmpty(item.Author) ? "Nota" : item.Author,
                item.Contents ?? string.Empty,
                false,
                "Comment");
            annotation.Color = ToBaseColor(item.Color);

            // Icono dibujado: un bocadillo con tres renglones. PDFium tampoco
            // pinta el icono estandar de las notas, asi que sin esto la nota
            // quedaria invisible dentro de la propia aplicacion.
            var appearance = CreateAppearance(stamper, item.PageNumber, rect);
            appearance.SetColorFill(ToBaseColor(item.Color));
            appearance.SetColorStroke(new BaseColor(60, 60, 60));
            appearance.SetLineWidth(0.7F);

            var x = rect.Left;
            var y = rect.Bottom;
            var ancho = rect.Width;
            var alto = rect.Height;
            var cuerpo = alto * 0.72F;

            appearance.RoundRectangle(
                x + (ancho * 0.08F),
                y + (alto * 0.24F),
                ancho * 0.84F,
                cuerpo,
                Math.Min(ancho, alto) * 0.18F);
            appearance.FillStroke();

            // Rabito del bocadillo
            appearance.MoveTo(x + (ancho * 0.30F), y + (alto * 0.26F));
            appearance.LineTo(x + (ancho * 0.24F), y + (alto * 0.04F));
            appearance.LineTo(x + (ancho * 0.48F), y + (alto * 0.26F));
            appearance.ClosePathFillStroke();

            appearance.SetColorStroke(new BaseColor(255, 255, 255));
            appearance.SetLineWidth(Math.Max(0.6F, alto * 0.05F));
            for (var linea = 0; linea < 3; linea++)
            {
                var altura = y + (alto * (0.42F + (linea * 0.14F)));
                appearance.MoveTo(x + (ancho * 0.22F), altura);
                appearance.LineTo(x + (ancho * 0.78F), altura);
            }
            appearance.Stroke();

            annotation.SetAppearance(PdfName.N, appearance);

            ApplyCommon(annotation, item);
            return annotation;
        }

        /// <summary>
        /// Lienzo para la apariencia de una anotacion.
        ///
        /// La caja se fija al mismo rectangulo que la anotacion y en
        /// coordenadas de pagina, de modo que se dibuja con las mismas
        /// coordenadas que se calcularon para la marca, sin desplazamientos.
        /// Se pide a traves del contenido de la pagina porque iText prohibe
        /// usar Writer.DirectContent cuando se trabaja con un PdfStamper.
        /// </summary>
        private static PdfAppearance CreateAppearance(
            PdfStamper stamper,
            int pageNumber,
            PdfRectangle rect)
        {
            var appearance = stamper
                .GetOverContent(pageNumber)
                .CreateAppearance(rect.Width, rect.Height);
            appearance.BoundingBox = rect;
            return appearance;
        }

        /// <summary>
        /// Autor y fecha. Sin ellos Acrobat lista las marcas sin nombre y no se
        /// sabe quien las puso.
        /// </summary>
        private static void ApplyCommon(
            PdfAnnotation annotation,
            PdfAnnotationItem item)
        {
            if (!string.IsNullOrEmpty(item.Author))
            {
                annotation.Title = item.Author;
            }

            // Sin el indicador de impresion algunos visores omiten la marca
            // al renderizar y al imprimir.
            annotation.Flags = PdfAnnotation.FLAGS_PRINT;
            annotation.Put(PdfName.M, new PdfDate());

            if (item.Opacity < 0.999F)
            {
                annotation.Put(
                    PdfName.CA,
                    new PdfNumber(Clamp(item.Opacity, 0.1F, 1F)));
            }

            if (!string.IsNullOrEmpty(item.Contents))
            {
                annotation.Put(
                    PdfName.CONTENTS,
                    new PdfString(item.Contents, PdfObject.TEXT_UNICODE));
            }
        }

        private static PdfRectangle ToRawRectangle(
            PdfTextPageTransform transform,
            RectangleF visual)
        {
            var a = transform.VisualToRaw(visual.Left, visual.Top);
            var b = transform.VisualToRaw(visual.Right, visual.Bottom);
            return new PdfRectangle(
                Math.Min(a.X, b.X),
                Math.Min(a.Y, b.Y),
                Math.Max(a.X, b.X),
                Math.Max(a.Y, b.Y));
        }

        private static BaseColor ToBaseColor(Color color)
        {
            return new BaseColor(color.R, color.G, color.B);
        }

        private static float Clamp(float value, float min, float max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        private static bool ContainsXfa(PdfReader reader)
        {
            var catalog = reader.Catalog;
            if (catalog == null)
            {
                return false;
            }

            var acroForm = catalog.GetAsDict(PdfName.ACROFORM);
            return acroForm != null && acroForm.Get(PdfName.XFA) != null;
        }

        private static IDictionary<string, string> CloneInfo(
            IDictionary<string, string> info)
        {
            var copia = new Dictionary<string, string>(StringComparer.Ordinal);
            if (info == null)
            {
                return copia;
            }

            foreach (var pareja in info)
            {
                copia[pareja.Key] = pareja.Value;
            }

            return copia;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
            }
        }
    }
}

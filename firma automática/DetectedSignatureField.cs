using System;

namespace FirmaAutomatica
{
    internal sealed class DetectedSignatureField
    {
        public DetectedSignatureField(string fieldName, int pageNumber, float left, float bottom, float right, float top)
        {
            FieldName = fieldName ?? string.Empty;
            PageNumber = pageNumber;
            Left = Math.Min(left, right);
            Right = Math.Max(left, right);
            Bottom = Math.Min(bottom, top);
            Top = Math.Max(bottom, top);
        }

        public string FieldName { get; private set; }

        public int PageNumber { get; private set; }

        public float Left { get; private set; }

        public float Bottom { get; private set; }

        public float Right { get; private set; }

        public float Top { get; private set; }
    }
}

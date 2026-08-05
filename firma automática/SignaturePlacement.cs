using System;

namespace FirmaAutomatica
{
    internal sealed class SignaturePlacement
    {
        public string SourcePath { get; set; }

        public string ExistingFieldName { get; set; }

        public int PageNumber { get; set; }

        public float Left { get; set; }

        public float Bottom { get; set; }

        public float Right { get; set; }

        public float Top { get; set; }

        public DateTime SignedAt { get; set; }
    }
}

using System.Reflection;
using System.Runtime.InteropServices;

// El ejecutable no llevaba ningun metadato: Windows lo mostraba como version
// 0.0.0.0, sin producto ni copyright. Para distribuirlo hace falta que se
// identifique, y la firma Authenticode gana reputacion ante SmartScreen cuando
// el binario declara quien lo publica.
//
// AssemblyCopyright cumple ademas el aviso legal que la AGPL v3 pide mostrar en
// las versiones modificadas: ver LICENCIAS.md.

[assembly: AssemblyTitle("PDF Ligero")]
[assembly: AssemblyDescription(
    "Visor y herramientas de PDF para Windows: combinar, organizar paginas, " +
    "OCR local, marcadores, comparar y medir planos, editar texto, rellenar " +
    "formularios y firmar digitalmente.")]
[assembly: AssemblyProduct("PDF Ligero")]
[assembly: AssemblyCompany("AGOIN")]
[assembly: AssemblyCopyright(
    "Copyright (C) 2026 AGOIN. Licencia AGPL v3. Incorpora iTextSharp " +
    "(AGPL v3) de iText Group NV.")]

[assembly: ComVisible(false)]

[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

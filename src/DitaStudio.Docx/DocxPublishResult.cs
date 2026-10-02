using System.Globalization;
using System.Text.RegularExpressions;
using DitaStudio.Core.IO;
using DitaStudio.Core.Model;
using DitaStudio.Core.Project;
using DitaStudio.Core.Publishing;
using DitaStudio.Docx.Styling;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace DitaStudio.Docx;

public sealed class DocxPublishResult
{
    public DocxPublishResult(string outputFile, IReadOnlyList<string> warnings)
    {
        OutputFile = outputFile;
        Warnings = warnings;
    }

    public string OutputFile { get; }

    public IReadOnlyList<string> Warnings { get; }
}

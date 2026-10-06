namespace AnyDoc;

/// <summary>
/// Input format, named after the extension that identifies it. Container
/// variants that share a parser (<c>.docm</c>, <c>.xlsm</c>, <c>.ppsx</c>, ...)
/// map onto these via <see cref="AnyDocConverter.DetectFormat"/> or
/// <see cref="AnyDocConverter.FormatFromExtension"/>.
/// </summary>
/// <remarks>The values are the native library's format indices: keep the order.</remarks>
public enum Format
{
    /// <summary>Binary Word 97-2003 (<c>.doc</c>).</summary>
    Doc = 0,
    /// <summary>WordprocessingML (<c>.docx</c>, <c>.docm</c>).</summary>
    Docx = 1,
    /// <summary>OpenDocument Text (<c>.odt</c>).</summary>
    Odt = 2,
    /// <summary>PDF. Scanned or image-only pages need OCR; see <see cref="NeedsOcrException"/>.</summary>
    Pdf = 3,
    /// <summary>Binary PowerPoint 97-2003 (<c>.ppt</c>, <c>.pps</c>, <c>.pot</c>).</summary>
    Ppt = 4,
    /// <summary>PresentationML (<c>.pptx</c>, <c>.pptm</c>, <c>.ppsx</c>, <c>.ppsm</c>).</summary>
    Pptx = 5,
    /// <summary>Rich Text Format (<c>.rtf</c>).</summary>
    Rtf = 6,
    /// <summary>EPUB 2 and 3 (<c>.epub</c>).</summary>
    Epub = 7,
    /// <summary>Excel workbooks: <c>.xlsx</c>, <c>.xlsm</c>, <c>.xlsb</c> and <c>.xls</c>.</summary>
    Xlsx = 8,
    /// <summary>OpenDocument Spreadsheet (<c>.ods</c>).</summary>
    Ods = 9,
    /// <summary>OpenDocument Presentation (<c>.odp</c>).</summary>
    Odp = 10,
    /// <summary>Delimiter-separated text (<c>.csv</c>). Carries no signature, so it has to be named.</summary>
    Csv = 11,
}

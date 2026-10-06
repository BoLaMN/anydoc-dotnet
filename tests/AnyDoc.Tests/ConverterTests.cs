using System.Text;

namespace AnyDoc.Tests;

public class ConverterTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    /// <summary>The upstream insta snapshot body: everything after the front matter.</summary>
    private static string Snapshot(string fixture)
    {
        var name = "snapshots__" + fixture.Replace("/", "__", StringComparison.Ordinal) + ".snap";
        var text = File.ReadAllText(Fixture(Path.Combine("snapshots", name)));
        var body = text.IndexOf("\n---\n", 4, StringComparison.Ordinal) + "\n---\n".Length;
        return text[body..].TrimEnd();
    }

    // The Markdown must match upstream's own snapshots byte for byte, which
    // proves the FFI round trip loses nothing.
    [Theory]
    [InlineData("docx/handmade-tables.docx")]
    [InlineData("docx/text.docx")]
    [InlineData("csv/handmade-semicolon.csv")]
    [InlineData("pdf/text.pdf")]
    [InlineData("rtf/text.rtf")]
    public void PathConversionMatchesUpstreamSnapshot(string fixture)
    {
        Assert.Equal(Snapshot(fixture), AnyDocConverter.ToMarkdown(Fixture(fixture)).TrimEnd());
    }

    // PDFsharp-imported pages keep their fonts in a Form XObject with an
    // indirect /Resources; without the pdf-inspector fix (see native/Cargo.toml)
    // this reads "7ULSOH*DUDJH".
    [Fact]
    public void FormXObjectFontsReadThroughTheirCMap()
    {
        Assert.Contains("Triple Garage", AnyDocConverter.ToMarkdown(Fixture("pdf/form-xobject-indirect-resources.pdf")));
    }

    [Fact]
    public void BytesConversionDetectsTheFormat()
    {
        var bytes = File.ReadAllBytes(Fixture("docx/handmade-tables.docx"));
        Assert.Equal(Snapshot("docx/handmade-tables.docx"), AnyDocConverter.ToMarkdown(bytes).TrimEnd());
    }

    [Fact]
    public void CsvHasToBeNamed()
    {
        var bytes = File.ReadAllBytes(Fixture("csv/handmade-semicolon.csv"));
        Assert.Throws<UnsupportedException>(() => AnyDocConverter.ToMarkdown(bytes));
        Assert.Equal(Snapshot("csv/handmade-semicolon.csv"), AnyDocConverter.ToMarkdown(bytes, Format.Csv).TrimEnd());
    }

    [Fact]
    public void ScannedPagesAreReported()
    {
        var bytes = File.ReadAllBytes(Fixture("pdf/handmade-mixed.pdf"));
        var error = Assert.Throws<NeedsOcrException>(() => AnyDocConverter.ToMarkdown(bytes, Format.Pdf));
        Assert.Equal("needsOcr", error.Code);
        Assert.Equal([2], error.Pages);
        Assert.Equal(2, error.PageCount);
    }

    [Fact]
    public void MalformedDocumentsCarryTheUpstreamMessage()
    {
        var error = Assert.Throws<MalformedException>(
            () => AnyDocConverter.ToMarkdown(Fixture("malformed/empty--errors.docx")));
        Assert.Equal(Snapshot("malformed/empty--errors.docx"), "ERROR: " + error.Message);
    }

    [Fact]
    public void MissingFileThrowsIOException()
    {
        Assert.Throws<IOException>(() => AnyDocConverter.ToMarkdown(Fixture("nope/missing.docx")));
    }

    [Fact]
    public void EmptyInputIsUnsupported()
    {
        Assert.Throws<UnsupportedException>(() => AnyDocConverter.ToMarkdown(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void NulInTextSurvivesTheBoundary()
    {
        var csv = Encoding.UTF8.GetBytes("a,b\nx\0y,z\n");
        Assert.Contains("x\0y", AnyDocConverter.ToMarkdown(csv, Format.Csv));
    }

    [Fact]
    public void FormatsAreDetected()
    {
        Assert.Equal(Format.Docx, AnyDocConverter.DetectFormat(File.ReadAllBytes(Fixture("docx/text.docx"))));
        Assert.Equal(Format.Pdf, AnyDocConverter.DetectFormat(File.ReadAllBytes(Fixture("pdf/text.pdf"))));
        Assert.Null(AnyDocConverter.DetectFormat(File.ReadAllBytes(Fixture("csv/handmade-semicolon.csv"))));
        Assert.Equal(Format.Xlsx, AnyDocConverter.FormatFromExtension(".XLSM"));
        Assert.Equal(Format.Pptx, AnyDocConverter.FormatFromExtension("ppsx"));
        Assert.Null(AnyDocConverter.FormatFromExtension("nope"));
        Assert.Equal(Format.Csv, AnyDocConverter.FormatFromPath("/data/sheet.csv"));
        Assert.Null(AnyDocConverter.FormatFromPath("/data/README"));
    }

    [Fact]
    public void FormatEnumCoversEveryNativeIndex()
    {
        foreach (var format in Enum.GetValues<Format>())
        {
            Assert.Equal(format, AnyDocConverter.FormatFromExtension(format.ToString().ToLowerInvariant()));
        }
    }

    [Fact]
    public void ConversionIsThreadSafe()
    {
        var bytes = File.ReadAllBytes(Fixture("docx/handmade-tables.docx"));
        var expected = AnyDocConverter.ToMarkdown(bytes);
        Parallel.For(0, 64, _ => Assert.Equal(expected, AnyDocConverter.ToMarkdown(bytes)));
    }
}

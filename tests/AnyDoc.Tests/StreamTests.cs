namespace AnyDoc.Tests;

public class StreamTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    /// <summary>A stream that can only be read forward, like a network body.</summary>
    private sealed class ForwardOnly(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task FileStreamMatchesPathConversion()
    {
        var path = Fixture("docx/handmade-tables.docx");
        await using var stream = File.OpenRead(path);
        Assert.Equal(AnyDocConverter.ToMarkdown(path), await AnyDocConverter.ToMarkdownAsync(stream));
    }

    [Fact]
    public async Task ForwardOnlyStreamConverts()
    {
        var path = Fixture("docx/handmade-tables.docx");
        await using var stream = new ForwardOnly(File.OpenRead(path));
        Assert.Equal(AnyDocConverter.ToMarkdown(path), await AnyDocConverter.ToMarkdownAsync(stream));
    }

    [Fact]
    public async Task MemoryStreamIsReadFromItsPosition()
    {
        var document = File.ReadAllBytes(Fixture("csv/handmade-semicolon.csv"));
        var stream = new MemoryStream();
        stream.Write("junk before the document"u8);
        var start = stream.Position;
        stream.Write(document);
        stream.Position = start;

        var markdown = await AnyDocConverter.ToMarkdownAsync(stream, Format.Csv);

        Assert.Equal(AnyDocConverter.ToMarkdown(document, Format.Csv), markdown);
        Assert.Equal(stream.Length, stream.Position);
    }

    [Fact]
    public async Task DocumentModelFromStream()
    {
        await using var stream = File.OpenRead(Fixture("docx/handmade-ole.docx"));
        var document = await AnyDocConverter.ToDocumentAsync(stream, Format.Docx);
        Assert.Contains(document.Assets, a => a.MediaType == "application/vnd.ms-ole-object");
    }

    [Fact]
    public async Task CancelledReadsThrow()
    {
        await using var stream = new ForwardOnly(File.OpenRead(Fixture("docx/text.docx")));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => AnyDocConverter.ToMarkdownAsync(stream, cancellationToken: new CancellationToken(true)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => AnyDocConverter.ToDocumentAsync(stream, cancellationToken: new CancellationToken(true)));
    }

    [Fact]
    public async Task ConversionErrorsSurfaceFromStreams()
    {
        await Assert.ThrowsAsync<UnsupportedException>(() => AnyDocConverter.ToMarkdownAsync(new MemoryStream()));
    }
}

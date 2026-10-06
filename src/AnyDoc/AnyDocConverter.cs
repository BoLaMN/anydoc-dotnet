namespace AnyDoc;

/// <summary>What happens to a PDF whose pages need OCR.</summary>
public enum Ocr
{
    /// <summary>Throw <see cref="NeedsOcrException"/> naming the pages (the default).</summary>
    Reject,

    /// <summary>
    /// Send the whole document to Firecrawl Parse instead, keyless unless a
    /// key is given. Documents anydoc converts itself never leave the machine.
    /// </summary>
    Hosted,
}

/// <summary>Options for the <c>ToMarkdownAsync</c> overloads.</summary>
public sealed record ConvertOptions
{
    /// <summary>What happens to a PDF whose pages need OCR.</summary>
    public Ocr Ocr { get; init; } = Ocr.Reject;

    /// <summary>Firecrawl API key for <see cref="Ocr.Hosted"/>, else <c>FIRECRAWL_API_KEY</c>, else keyless.</summary>
    public string? ApiKey { get; init; }

    /// <summary>Firecrawl API URL for <see cref="Ocr.Hosted"/>, else <c>FIRECRAWL_API_URL</c>, else <c>https://api.firecrawl.dev</c>.</summary>
    public string? ApiUrl { get; init; }

    /// <summary>The client <see cref="Ocr.Hosted"/> posts with, else a shared one with a 300 second timeout.</summary>
    public HttpClient? HttpClient { get; init; }
}

/// <summary>Converts documents to GitHub-Flavored Markdown.</summary>
/// <remarks>
/// Conversion runs synchronously on the calling thread and is thread-safe.
/// The <c>ToMarkdownAsync</c> overloads are only asynchronous for the
/// network round trip of <see cref="Ocr.Hosted"/>.
/// </remarks>
public static class AnyDocConverter
{
    /// <summary>
    /// Convert a document file to Markdown. The format is detected from the
    /// file content; the extension is the fallback for signature-less formats
    /// (CSV) and unrecognizable containers.
    /// </summary>
    /// <exception cref="ConvertException">The document could not be converted.</exception>
    /// <exception cref="IOException">The file could not be read.</exception>
    public static string ToMarkdown(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Native.ToMarkdown(Path.GetFullPath(path));
    }

    /// <summary>
    /// Convert an in-memory document to Markdown. Without a format, it is
    /// detected from the content, which signature-less formats (CSV) have to
    /// name explicitly.
    /// </summary>
    /// <exception cref="ConvertException">The document could not be converted.</exception>
    public static string ToMarkdown(ReadOnlySpan<byte> data, Format? format = null) =>
        Native.ToMarkdown(data, format);

    /// <summary>
    /// <see cref="ToMarkdown(string)"/>, sending documents that need OCR to
    /// Firecrawl Parse when <see cref="ConvertOptions.Ocr"/> is <see cref="Ocr.Hosted"/>.
    /// </summary>
    /// <exception cref="HostedException">Firecrawl Parse could not convert the document.</exception>
    public static async Task<string> ToMarkdownAsync(
        string path, ConvertOptions? options = null, CancellationToken cancellationToken = default)
    {
        try
        {
            return ToMarkdown(path);
        }
        catch (NeedsOcrException) when (options?.Ocr == Ocr.Hosted)
        {
            var data = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            return await HostedOcr.ParseAsync(data, Path.GetFileName(path), options, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// <see cref="ToMarkdown(ReadOnlySpan{byte}, Format?)"/>, sending documents
    /// that need OCR to Firecrawl Parse when <see cref="ConvertOptions.Ocr"/>
    /// is <see cref="Ocr.Hosted"/>.
    /// </summary>
    /// <exception cref="HostedException">Firecrawl Parse could not convert the document.</exception>
    public static async Task<string> ToMarkdownAsync(
        ReadOnlyMemory<byte> data,
        Format? format = null,
        ConvertOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return ToMarkdown(data.Span, format);
        }
        catch (NeedsOcrException) when (options?.Ocr == Ocr.Hosted)
        {
            return await HostedOcr.ParseAsync(data, "document.pdf", options, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Parse an in-memory document into the document model, which also
    /// carries the embedded assets. Without a format, it is detected from the
    /// content.
    /// </summary>
    /// <remarks>
    /// Unsupported for <see cref="Format.Pdf"/>: PDF conversion produces
    /// Markdown directly and has no document-model form.
    /// </remarks>
    /// <exception cref="ConvertException">The document could not be parsed.</exception>
    public static Document ToDocument(ReadOnlySpan<byte> data, Format? format = null) =>
        Native.ToDocument(data, format);

    /// <summary>
    /// Detect the format from the content itself: the signature each container
    /// specification designates. Null for signature-less formats (CSV) and
    /// anything unrecognized.
    /// </summary>
    public static Format? DetectFormat(ReadOnlySpan<byte> data) => Native.FormatFromBytes(data);

    /// <summary>The format an extension names, with or without a leading dot.</summary>
    public static Format? FormatFromExtension(string extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        return Native.FormatFromExtension(extension.TrimStart('.'));
    }

    /// <summary>The format a path's extension names.</summary>
    public static Format? FormatFromPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var extension = Path.GetExtension(path);
        return extension.Length == 0 ? null : FormatFromExtension(extension);
    }
}

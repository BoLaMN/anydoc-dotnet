namespace AnyDoc;

/// <summary>
/// A complete conversion was impossible. Catch this to handle every kind of
/// failure, or one of the subclasses to single one out. An unreadable file
/// throws <see cref="IOException"/> instead.
/// </summary>
public class ConvertException : Exception
{
    /// <summary>
    /// Stable, machine-readable name of the failure, as the Node binding
    /// publishes it: <c>unsupported</c>, <c>needsOcr</c>, <c>malformed</c>,
    /// <c>encrypted</c>, <c>resourceLimit</c>, <c>missingPart</c>,
    /// <c>hosted</c>, <c>invalidArgument</c> or <c>panic</c>.
    /// </summary>
    public string Code { get; }

    /// <summary>Create an exception with a code and message.</summary>
    public ConvertException(string code, string message, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
    }

    internal static Exception FromNative(NativeError error) => error.Code switch
    {
        "unsupported" => new UnsupportedException(error.Message),
        "needsOcr" => new NeedsOcrException(error.Message, error.Pages ?? [], error.PageCount ?? 0),
        "malformed" => new MalformedException(error.Message, error.Part),
        "encrypted" => new EncryptedException(error.Message),
        "resourceLimit" => new ResourceLimitException(error.Message, error.Limit ?? ""),
        "missingPart" => new MissingPartException(error.Message, error.Part ?? ""),
        "io" => new IOException(error.Message),
        // A code added later surfaces as the base class until it is named here.
        _ => new ConvertException(error.Code, error.Message),
    };
}

/// <summary>The format is unknown, or cannot be converted at all.</summary>
public sealed class UnsupportedException(string message)
    : ConvertException("unsupported", message);

/// <summary>
/// Pages of a PDF are scanned or image-only and need OCR, which anydoc does
/// not do locally. Pass <see cref="Ocr.Hosted"/> to send such documents to
/// Firecrawl Parse instead.
/// </summary>
public sealed class NeedsOcrException(string message, IReadOnlyList<int> pages, int pageCount)
    : ConvertException("needsOcr", message)
{
    /// <summary>1-indexed pages that need OCR.</summary>
    public IReadOnlyList<int> Pages { get; } = pages;

    /// <summary>Pages in the document.</summary>
    public int PageCount { get; } = pageCount;
}

/// <summary>
/// The document is structurally unusable: no meaningful content could be
/// extracted.
/// </summary>
public sealed class MalformedException(string message, string? part)
    : ConvertException("malformed", message)
{
    /// <summary>The package part or stream at fault, or null when no single part is.</summary>
    public string? Part { get; } = part;
}

/// <summary>The document is encrypted or password-protected.</summary>
public sealed class EncryptedException(string message)
    : ConvertException("encrypted", message);

/// <summary>
/// A fixed safety limit was crossed: decompression, nesting depth, node
/// count, repeat expansion, or retained asset bytes.
/// </summary>
public sealed class ResourceLimitException(string message, string limit)
    : ConvertException("resourceLimit", message)
{
    /// <summary>The name of the limit that was hit.</summary>
    public string Limit { get; } = limit;
}

/// <summary>A part required for any meaningful output is absent.</summary>
public sealed class MissingPartException(string message, string part)
    : ConvertException("missingPart", message)
{
    /// <summary>The part or stream that was absent.</summary>
    public string Part { get; } = part;
}

/// <summary><see cref="Ocr.Hosted"/> could not get the document through Firecrawl Parse.</summary>
public sealed class HostedException(string message, int? statusCode = null, Exception? inner = null)
    : ConvertException("hosted", message, inner)
{
    /// <summary>The HTTP status Firecrawl Parse answered with, when it answered.</summary>
    public int? StatusCode { get; } = statusCode;
}

/// <summary>The error object the native library writes on failure.</summary>
internal sealed record NativeError(
    string Code,
    string Message,
    int[]? Pages = null,
    int? PageCount = null,
    string? Part = null,
    string? Limit = null);

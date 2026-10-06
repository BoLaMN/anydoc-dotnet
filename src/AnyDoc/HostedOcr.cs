using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace AnyDoc;

/// <summary>
/// Firecrawl Parse, for documents that need OCR. The whole document goes,
/// not only the pages that need it: Parse has no page selection.
/// </summary>
internal static class HostedOcr
{
    private const string DefaultApiUrl = "https://api.firecrawl.dev";

    private static readonly Lazy<HttpClient> SharedClient =
        new(() => new HttpClient { Timeout = TimeSpan.FromSeconds(300) });

    public static async Task<string> ParseAsync(
        ReadOnlyMemory<byte> data, string filename, ConvertOptions options, CancellationToken cancellationToken)
    {
        var apiKey = options.ApiKey ?? Environment.GetEnvironmentVariable("FIRECRAWL_API_KEY");
        var apiUrl = NonEmpty(options.ApiUrl) ?? NonEmpty(Environment.GetEnvironmentVariable("FIRECRAWL_API_URL"))
            ?? DefaultApiUrl;
        var keyed = !string.IsNullOrEmpty(apiKey);

        using var request = new HttpRequestMessage(HttpMethod.Post, apiUrl.TrimEnd('/') + "/v2/parse")
        {
            Content = Body(data, filename),
        };
        if (keyed)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        int status;
        JsonElement reply;
        try
        {
            var client = options.HttpClient ?? SharedClient.Value;
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            status = (int)response.StatusCode;
            var body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            reply = Json(body);
        }
        catch (HttpRequestException error)
        {
            throw new HostedException($"Firecrawl Parse: {error.Message}", inner: error);
        }
        catch (TaskCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw new HostedException("Firecrawl Parse: request timed out", inner: error);
        }

        if (status != 200 || reply.ValueKind != JsonValueKind.Object
            || !(reply.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.True))
        {
            var detail = reply.ValueKind == JsonValueKind.Object
                && reply.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String
                && error.GetString() is { Length: > 0 } message
                ? message
                : $"HTTP {status}";
            throw new HostedException(Describe(status, detail, keyed), status);
        }
        if (!(reply.TryGetProperty("data", out var payload) && payload.ValueKind == JsonValueKind.Object
              && payload.TryGetProperty("markdown", out var markdownElement)
              && markdownElement.ValueKind == JsonValueKind.String
              && markdownElement.GetString() is { Length: > 0 } markdown))
        {
            throw new HostedException("Firecrawl Parse returned no Markdown", status);
        }
        return markdown.EndsWith('\n') ? markdown : markdown + "\n";
    }

    private static MultipartFormDataContent Body(ReadOnlyMemory<byte> data, string filename)
    {
        var options = new MemoryStream();
        using (var writer = new Utf8JsonWriter(options))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("parsers");
            writer.WriteStartObject();
            writer.WriteString("type", "pdf");
            writer.WriteString("mode", "auto");
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteString("origin", $"anydoc@{Version()}");
            writer.WriteEndObject();
        }
        filename = filename.Replace('"', '_').Replace('\r', '_').Replace('\n', '_');

        var body = new MultipartFormDataContent();
        body.Add(new ByteArrayContent(options.ToArray()), "options");
        var file = new ReadOnlyMemoryContent(data);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        body.Add(file, "file", filename);
        return body;
    }

    private static JsonElement Json(byte[] body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? document.RootElement.Clone()
                : default;
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static string Describe(int status, string detail, bool keyed) => status switch
    {
        401 => $"Firecrawl Parse rejected the API key: {detail}",
        402 => $"Firecrawl Parse is out of credits: {detail}",
        429 when keyed => $"Firecrawl Parse rate limit reached: {detail}",
        429 => $"Firecrawl Parse keyless limit reached, set FIRECRAWL_API_KEY: {detail}",
        _ => $"Firecrawl Parse: {detail}",
    };

    private static string? NonEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static string Version() =>
        typeof(HostedOcr).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0]
        ?? "unknown";
}

using System.Net;
using System.Text;

namespace AnyDoc.Tests;

public class HostedOcrTests
{
    private static byte[] Scanned() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "pdf", "handmade-scanned.pdf"));

    /// <summary>Records the request and answers with a canned reply.</summary>
    private sealed class FakeParse(HttpStatusCode status, string reply) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(reply) };
        }
    }

    [Fact]
    public async Task RejectIsTheDefault()
    {
        await Assert.ThrowsAsync<NeedsOcrException>(() => AnyDocConverter.ToMarkdownAsync(Scanned()));
    }

    [Fact]
    public async Task HostedPostsTheDocumentToParse()
    {
        var parse = new FakeParse(HttpStatusCode.OK, """{"success":true,"data":{"markdown":"# Scanned"}}""");
        var options = new ConvertOptions
        {
            Ocr = Ocr.Hosted,
            ApiKey = "test-key",
            ApiUrl = "https://parse.example/",
            HttpClient = new HttpClient(parse),
        };

        var markdown = await AnyDocConverter.ToMarkdownAsync(Scanned(), options: options);

        Assert.Equal("# Scanned\n", markdown);
        Assert.Equal(HttpMethod.Post, parse.Request!.Method);
        Assert.Equal("https://parse.example/v2/parse", parse.Request.RequestUri!.ToString());
        Assert.Equal("Bearer", parse.Request.Headers.Authorization!.Scheme);
        Assert.Contains("name=options", parse.Body);
        Assert.Contains("\"parsers\":[{\"type\":\"pdf\",\"mode\":\"auto\"}]", parse.Body);
        Assert.Contains("\"origin\":\"anydoc@", parse.Body);
        Assert.Contains("filename=document.pdf", parse.Body);
        Assert.Contains("Content-Type: application/pdf", parse.Body);
    }

    [Fact]
    public async Task KeylessSendsNoAuthorization()
    {
        var parse = new FakeParse(HttpStatusCode.OK, """{"success":true,"data":{"markdown":"ok\n"}}""");
        var options = new ConvertOptions { Ocr = Ocr.Hosted, ApiKey = "", HttpClient = new HttpClient(parse) };

        Assert.Equal("ok\n", await AnyDocConverter.ToMarkdownAsync(Scanned(), options: options));
        Assert.Null(parse.Request!.Headers.Authorization);
    }

    [Theory]
    [InlineData(HttpStatusCode.PaymentRequired, """{"success":false,"error":"no credits"}""", "Firecrawl Parse is out of credits: no credits")]
    [InlineData(HttpStatusCode.TooManyRequests, """{"error":"slow down"}""", "Firecrawl Parse keyless limit reached, set FIRECRAWL_API_KEY: slow down")]
    [InlineData(HttpStatusCode.BadGateway, "<html>", "Firecrawl Parse: HTTP 502")]
    [InlineData(HttpStatusCode.OK, """{"success":true,"data":{}}""", "Firecrawl Parse returned no Markdown")]
    public async Task FailuresThrowHostedException(HttpStatusCode status, string reply, string message)
    {
        var options = new ConvertOptions
        {
            Ocr = Ocr.Hosted,
            ApiKey = "",
            HttpClient = new HttpClient(new FakeParse(status, reply)),
        };

        var error = await Assert.ThrowsAsync<HostedException>(
            () => AnyDocConverter.ToMarkdownAsync(Scanned(), options: options));
        Assert.Equal(message, error.Message);
        Assert.Equal("hosted", error.Code);
    }

    [Fact]
    public async Task LocalDocumentsNeverLeaveTheMachine()
    {
        var parse = new FakeParse(HttpStatusCode.OK, "{}");
        var options = new ConvertOptions { Ocr = Ocr.Hosted, HttpClient = new HttpClient(parse) };
        var text = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "pdf", "text.pdf"));

        await AnyDocConverter.ToMarkdownAsync(text, options: options);

        Assert.Null(parse.Request);
    }
}

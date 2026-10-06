# AnyDoc.Net

Unofficial .NET bindings for [anydoc](https://github.com/firecrawl/anydoc), the Rust library that converts Word, PowerPoint, Excel, OpenDocument, RTF, EPUB, CSV and PDF documents to GitHub-Flavored Markdown.

```csharp
using AnyDoc;

// From a file path (format detected from content, extension as fallback):
string markdown = AnyDocConverter.ToMarkdown("report.docx");

// From bytes; signature-less formats (CSV) have to be named:
string fromBytes = AnyDocConverter.ToMarkdown(bytes);
string fromCsv = AnyDocConverter.ToMarkdown(csvBytes, Format.Csv);

// Scanned PDFs: send them to Firecrawl Parse instead of throwing NeedsOcrException.
string scanned = await AnyDocConverter.ToMarkdownAsync("scan.pdf",
    new ConvertOptions { Ocr = Ocr.Hosted });   // ApiKey, else FIRECRAWL_API_KEY, else keyless

// Or stop at the document model, which also carries embedded assets:
Document document = AnyDocConverter.ToDocument(bytes);
foreach (var block in document.Blocks)
{
    if (block is HeadingBlock { Level: 1 } heading) { /* ... */ }
}
```

Failures throw a `ConvertException` subclass: `UnsupportedException`, `NeedsOcrException` (`Pages`, `PageCount`), `MalformedException` (`Part`), `EncryptedException`, `ResourceLimitException` (`Limit`), `MissingPartException` (`Part`), `HostedException`. An unreadable file throws `IOException`. `Code` carries the same string codes the Node binding uses.

## Layout

| Path | What |
| --- | --- |
| `native/` | `anydoc-ffi`: a Rust `cdylib` exposing a C ABI over the `anydoc` crate. Results cross as (pointer, length) UTF-8 buffers; errors and the document model cross as JSON in the Node binding's shape. |
| `src/AnyDoc/` | The `net8.0` library: `[LibraryImport]` P/Invoke, typed exceptions, the document model (System.Text.Json source-generated, polymorphic on `kind`), and hosted OCR over `HttpClient`. |
| `tests/AnyDoc.Tests/` | xunit tests. Markdown is compared byte for byte against upstream anydoc's own insta snapshots (fixtures copied from the upstream repo, MIT). |

## Building

Needs Rust ≥ 1.88 and the .NET 8+ SDK (tests target `net10.0`).

```bash
./build.sh            # cargo build --release, stage the native lib, dotnet test
./build.sh --no-test  # just build and stage
dotnet pack src/AnyDoc -c Release
```

`build.sh` builds for the current machine only and stages the library under `src/AnyDoc/runtimes/<rid>/native/`, which is where the package picks it up.

## CI and releases

`.github/workflows/ci.yml` runs on every push and pull request:

1. **native**: builds and tests the Rust library and the .NET binding on `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`, `win-x64` and `win-arm64`, each on its own runner.
2. **pack**: gathers all six native libraries into one `AnyDoc.Net` package, checks that all six are inside, then installs it into a fresh console app and converts a PDF with it.
3. **release** (tags only): pushing a tag such as `v0.2.5` creates a GitHub Release with the `.nupkg` and a zip of each platform's native library. Tags with a suffix (`v0.3.0-beta.1`) are marked as prereleases. If the repository has a `NUGET_API_KEY` secret, the package is also pushed to nuget.org.

```bash
git tag v0.2.5 && git push origin v0.2.5
```

The tag sets the package version; untagged builds are versioned `<csproj Version>-ci.<run number>`.

## Keeping in step with upstream

- `native/Cargo.toml` patches `pdf-inspector` to 1.25.2 plus a fix for fonts inside Form XObjects whose `/Resources` is an indirect reference, taken from a fork commit until firecrawl/pdf-inspector releases it. Without the fix, pages imported by PDFsharp (common in newspaper page PDFs) come out as letter-shifted gibberish. Remove the `[patch.crates-io]` entry once upstream ships the fix.
- The PDF snapshot (`snapshots__pdf__text.pdf.snap`) is regenerated for that backend, so it no longer matches upstream anydoc's, which was recorded with 1.14.2. The newer backend fixes right-to-left text order and `%` in place of "Th" ligatures, but merges one heading into the line before it. The other snapshots are still upstream's, unchanged.
- The `Format` enum's values are the native library's format indices. Both sides list them in the order the Python binding uses; tests on both sides fail if they drift.

## License

MIT (see `LICENSE`). The test fixtures and snapshots under `tests/AnyDoc.Tests/fixtures/` are copied from [firecrawl/anydoc](https://github.com/firecrawl/anydoc) under its MIT license (`tests/AnyDoc.Tests/fixtures/LICENSE-anydoc`), except `pdf/form-xobject-indirect-resources.pdf`, which is synthetic and embeds a subset of DejaVu Sans Bold (Bitstream Vera license). This project is not affiliated with Firecrawl.

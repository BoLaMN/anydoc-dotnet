using System.Text;

namespace AnyDoc.Tests;

public class DocumentTests
{
    private static byte[] Fixture(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", name));

    private static IEnumerable<Block> AllBlocks(IEnumerable<Block> blocks)
    {
        foreach (var block in blocks)
        {
            yield return block;
            var children = block switch
            {
                BlockQuoteBlock quote => quote.Blocks,
                ListBlock list => list.List.Items.SelectMany(item => item.Blocks),
                TableBlock table => table.Table.Grid.SelectMany(row => row).OfType<OriginSlot>()
                    .SelectMany(slot => slot.Cell.Blocks),
                _ => [],
            };
            foreach (var child in AllBlocks(children))
            {
                yield return child;
            }
        }
    }

    [Fact]
    public void TablesKeepTheirGrid()
    {
        var document = AnyDocConverter.ToDocument(Fixture("docx/handmade-tables.docx"));
        var tables = AllBlocks(document.Blocks).OfType<TableBlock>().Select(t => t.Table).ToList();
        Assert.NotEmpty(tables);
        var first = tables[0];
        Assert.Equal(1, first.HeaderRows);
        Assert.Equal(TableKind.Data, first.Kind);
        var header = first.Grid[0].OfType<OriginSlot>().Select(slot => slot.Cell).ToList();
        var texts = header.Select(cell => string.Concat(
            cell.Blocks.OfType<ParagraphBlock>().SelectMany(p => p.Content).OfType<TextInline>().Select(t => t.Text)));
        Assert.Equal(["Head A", "Head B", "Head C"], texts);
        // Spans surface as covered slots pointing back at their origin.
        Assert.Contains(tables, t => t.Grid.SelectMany(row => row).OfType<CoveredSlot>().Any());
    }

    [Fact]
    public void EmbeddedPayloadsBecomeAssets()
    {
        var document = AnyDocConverter.ToDocument(Fixture("docx/handmade-ole.docx"), Format.Docx);
        var ole = Assert.Single(document.Assets, a => a.MediaType == "application/vnd.ms-ole-object");
        Assert.Equal(Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("DOCX-OLE-PAYLOAD", 4))), ole.Data);
    }

    [Fact]
    public void RtfPicturesDecodeAsPng()
    {
        var document = AnyDocConverter.ToDocument(Fixture("rtf/text.rtf"));
        var png = Assert.Single(document.Assets, a => a.MediaType == "image/png");
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], png.Data[..4]);
    }

    [Fact]
    public void TextKeepsItsStyle()
    {
        var document = AnyDocConverter.ToDocument(Fixture("docx/text.docx"));
        var runs = AllBlocks(document.Blocks)
            .SelectMany(b => b switch
            {
                ParagraphBlock p => p.Content,
                HeadingBlock h => h.Content,
                _ => [],
            })
            .OfType<TextInline>()
            .ToList();
        Assert.NotEmpty(runs);
        Assert.Contains(runs, r => r.Style.Bold || r.Style.Italic);
    }

    [Fact]
    public void PdfHasNoDocumentModel()
    {
        Assert.Throws<UnsupportedException>(() => AnyDocConverter.ToDocument(Fixture("pdf/text.pdf")));
    }
}

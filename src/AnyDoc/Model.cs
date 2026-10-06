using System.Text.Json.Serialization;

namespace AnyDoc;

// The document model, as the Node binding publishes it. Polymorphic types
// read their concrete type from the `kind` property the native library
// writes first.

/// <summary>A parsed document: its content, notes and embedded assets.</summary>
public sealed record Document(
    IReadOnlyList<Block> Blocks,
    IReadOnlyList<Note> Notes,
    IReadOnlyList<Asset> Assets);

/// <summary>A footnote or endnote body, referenced by <see cref="NoteRefInline"/>.</summary>
public sealed record Note(string Id, NoteKind Kind, IReadOnlyList<Block> Blocks);

/// <summary>Where a note's body is placed in the source.</summary>
public enum NoteKind
{
    /// <summary>A footnote.</summary>
    Footnote,
    /// <summary>An endnote.</summary>
    Endnote,
}

/// <summary>An embedded payload: an image, or an OLE object.</summary>
/// <param name="Id">Referenced by <see cref="AssetImage.AssetId"/>.</param>
/// <param name="MediaType">MIME type, e.g. <c>image/png</c>.</param>
/// <param name="OriginPart">The package part or stream it came from.</param>
/// <param name="Data">The payload bytes.</param>
public sealed record Asset(int Id, string MediaType, string OriginPart, byte[] Data);

/// <summary>A block-level element.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(HeadingBlock), "heading")]
[JsonDerivedType(typeof(ParagraphBlock), "paragraph")]
[JsonDerivedType(typeof(ListBlock), "list")]
[JsonDerivedType(typeof(TableBlock), "table")]
[JsonDerivedType(typeof(BlockQuoteBlock), "blockQuote")]
[JsonDerivedType(typeof(CodeBlock), "codeBlock")]
[JsonDerivedType(typeof(RuleBlock), "rule")]
[JsonDerivedType(typeof(MathBlock), "math")]
public abstract record Block;

/// <summary>A heading, level 1 to 6, with its anchor when the source names one.</summary>
public sealed record HeadingBlock(int Level, string? Anchor, IReadOnlyList<Inline> Content) : Block;

/// <summary>A paragraph.</summary>
public sealed record ParagraphBlock(IReadOnlyList<Inline> Content) : Block;

/// <summary>A bulleted or numbered list.</summary>
public sealed record ListBlock(List List) : Block;

/// <summary>A table.</summary>
public sealed record TableBlock(Table Table) : Block;

/// <summary>A block quote.</summary>
public sealed record BlockQuoteBlock(IReadOnlyList<Block> Blocks) : Block;

/// <summary>A code block, with its language when known.</summary>
public sealed record CodeBlock(string? Lang, string Text) : Block;

/// <summary>A horizontal rule.</summary>
public sealed record RuleBlock : Block;

/// <summary>Display math, as TeX.</summary>
public sealed record MathBlock(string Text) : Block;

/// <summary>An inline element.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TextInline), "text")]
[JsonDerivedType(typeof(LinkInline), "link")]
[JsonDerivedType(typeof(ImageInline), "image")]
[JsonDerivedType(typeof(AnchorInline), "anchor")]
[JsonDerivedType(typeof(NoteRefInline), "noteRef")]
[JsonDerivedType(typeof(LineBreakInline), "lineBreak")]
[JsonDerivedType(typeof(MathInline), "math")]
[JsonDerivedType(typeof(CheckboxInline), "checkbox")]
public abstract record Inline;

/// <summary>A run of text in one style.</summary>
public sealed record TextInline(string Text, Style Style) : Inline;

/// <summary>A hyperlink or cross-reference.</summary>
public sealed record LinkInline(IReadOnlyList<Inline> Content, LinkTarget Target) : Inline;

/// <summary>An image.</summary>
public sealed record ImageInline(string Alt, ImageSource Source) : Inline;

/// <summary>A link target position.</summary>
public sealed record AnchorInline(string Anchor) : Inline;

/// <summary>A reference to the <see cref="Note"/> with this id.</summary>
public sealed record NoteRefInline(string NoteId) : Inline;

/// <summary>A hard line break.</summary>
public sealed record LineBreakInline : Inline;

/// <summary>Inline math, as TeX.</summary>
public sealed record MathInline(string Text) : Inline;

/// <summary>A task-list checkbox.</summary>
public sealed record CheckboxInline(bool Checked) : Inline;

/// <summary>Character formatting of a text run.</summary>
public sealed record Style(bool Bold, bool Italic, bool Strike, bool Code);

/// <summary>Where a link points.</summary>
public sealed record LinkTarget(LinkTargetKind Kind, string Value);

/// <summary>What a <see cref="LinkTarget"/> value is.</summary>
public enum LinkTargetKind
{
    /// <summary>An absolute URL.</summary>
    External,
    /// <summary>A path relative to the document.</summary>
    Relative,
    /// <summary>An anchor within the document.</summary>
    Anchor,
}

/// <summary>Where an image's data is.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ExternalImage), "external")]
[JsonDerivedType(typeof(AssetImage), "asset")]
[JsonDerivedType(typeof(UnavailableImage), "unavailable")]
public abstract record ImageSource;

/// <summary>An image linked by URL.</summary>
public sealed record ExternalImage(string Url) : ImageSource;

/// <summary>An image embedded as the <see cref="Asset"/> with this id.</summary>
public sealed record AssetImage(int AssetId) : ImageSource;

/// <summary>An image whose data could not be retained.</summary>
public sealed record UnavailableImage : ImageSource;

/// <summary>A list and its items.</summary>
/// <param name="Marker">The marker style.</param>
/// <param name="Start">The first item's number, for ordered lists.</param>
/// <param name="Items">The items.</param>
public sealed record List(MarkerKind Marker, long Start, IReadOnlyList<ListItem> Items);

/// <summary>A list item, with the source's own marker label when it has one.</summary>
public sealed record ListItem(IReadOnlyList<Block> Blocks, string? MarkerLabel);

/// <summary>List marker style.</summary>
public enum MarkerKind
{
    /// <summary>Unordered.</summary>
    Bullet,
    /// <summary>1, 2, 3.</summary>
    Decimal,
    /// <summary>a, b, c.</summary>
    LowerAlpha,
    /// <summary>A, B, C.</summary>
    UpperAlpha,
    /// <summary>i, ii, iii.</summary>
    LowerRoman,
    /// <summary>I, II, III.</summary>
    UpperRoman,
}

/// <summary>
/// A table as a rectangular grid: each position holds either the cell that
/// starts there or a pointer to the cell whose span covers it.
/// </summary>
public sealed record Table(IReadOnlyList<IReadOnlyList<CellSlot>> Grid, int HeaderRows, TableKind Kind);

/// <summary>What a table is for.</summary>
public enum TableKind
{
    /// <summary>Tabular data.</summary>
    Data,
    /// <summary>Page layout rather than data.</summary>
    Layout,
}

/// <summary>A grid position.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(OriginSlot), "origin")]
[JsonDerivedType(typeof(CoveredSlot), "covered")]
public abstract record CellSlot;

/// <summary>The position a cell starts at.</summary>
public sealed record OriginSlot(Cell Cell) : CellSlot;

/// <summary>A position covered by the span of the cell at (<paramref name="OriginRow"/>, <paramref name="OriginCol"/>).</summary>
public sealed record CoveredSlot(int OriginRow, int OriginCol) : CellSlot;

/// <summary>A table cell and its span.</summary>
public sealed record Cell(IReadOnlyList<Block> Blocks, int ColSpan, int RowSpan);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(Document))]
[JsonSerializable(typeof(NativeError))]
internal sealed partial class ModelJsonContext : JsonSerializerContext;

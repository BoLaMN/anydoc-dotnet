//! C ABI over anydoc, consumed by the .NET binding through P/Invoke.
//!
//! Every export returns a status (`ANYDOC_OK` or `ANYDOC_ERROR`) and writes
//! an owned UTF-8 buffer to `out`: the Markdown or document JSON on success,
//! an error JSON object (`code`, `message`, plus `pages`/`pageCount`, `part`
//! or `limit` where the variant carries them) on failure. The caller frees it
//! with `anydoc_buffer_free`. Buffers are (pointer, length) rather than
//! NUL-terminated, so a document whose text holds a NUL survives intact.
//!
//! Formats cross the boundary as their index in `FORMATS`; `-1` asks for
//! detection. The .NET `Format` enum mirrors that order.

use std::panic::{AssertUnwindSafe, catch_unwind};

use anydoc::model;
use base64::Engine;
use serde_json::{Map, Value, json};

pub const ANYDOC_OK: i32 = 0;
pub const ANYDOC_ERROR: i32 = 1;

/// Format names, as the extension that identifies each format, in the same
/// order as the Python binding. The index is the ABI value.
const FORMATS: [(&str, anydoc::Format); 12] = [
    ("doc", anydoc::Format::Doc),
    ("docx", anydoc::Format::Docx),
    ("odt", anydoc::Format::Odt),
    ("pdf", anydoc::Format::Pdf),
    ("ppt", anydoc::Format::Ppt),
    ("pptx", anydoc::Format::Pptx),
    ("rtf", anydoc::Format::Rtf),
    ("epub", anydoc::Format::Epub),
    ("xlsx", anydoc::Format::Excel),
    ("ods", anydoc::Format::Ods),
    ("odp", anydoc::Format::Odp),
    ("csv", anydoc::Format::Csv),
];

/// An owned byte buffer handed to the caller.
#[repr(C)]
pub struct AnydocBuffer {
    pub ptr: *mut u8,
    pub len: usize,
}

impl AnydocBuffer {
    #[cfg(test)]
    const EMPTY: AnydocBuffer = AnydocBuffer { ptr: std::ptr::null_mut(), len: 0 };

    fn from_vec(bytes: Vec<u8>) -> Self {
        let len = bytes.len();
        let ptr = Box::into_raw(bytes.into_boxed_slice()) as *mut u8;
        AnydocBuffer { ptr, len }
    }
}

/// Free a buffer an export wrote to `out`. Null is a no-op.
///
/// # Safety
/// `buffer` must come from this library and be freed once.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn anydoc_buffer_free(buffer: AnydocBuffer) {
    if !buffer.ptr.is_null() {
        drop(unsafe { Box::from_raw(std::ptr::slice_from_raw_parts_mut(buffer.ptr, buffer.len)) });
    }
}

/// Convert a document file to Markdown. `path` is UTF-8.
///
/// # Safety
/// `path` must point to `path_len` readable bytes; `out` must be writable.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn anydoc_to_markdown(
    path: *const u8,
    path_len: usize,
    out: *mut AnydocBuffer,
) -> i32 {
    let path = unsafe { bytes(path, path_len) };
    run(out, || {
        let path = std::str::from_utf8(path).map_err(|_| invalid("path is not valid UTF-8"))?;
        anydoc::to_markdown(path).map(String::into_bytes).map_err(convert_error)
    })
}

/// Convert an in-memory document to Markdown. `format` is a `FORMATS`
/// index, or `-1` to detect it from the content.
///
/// # Safety
/// `data` must point to `len` readable bytes; `out` must be writable.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn anydoc_to_markdown_bytes(
    data: *const u8,
    len: usize,
    format: i32,
    out: *mut AnydocBuffer,
) -> i32 {
    let data = unsafe { bytes(data, len) };
    run(out, || {
        let format = parse_format(format)?;
        anydoc::to_markdown_bytes(data, format).map(String::into_bytes).map_err(convert_error)
    })
}

/// Parse an in-memory document into the document model, serialized as JSON
/// in the shape the Node binding publishes. Asset bytes are base64.
///
/// # Safety
/// `data` must point to `len` readable bytes; `out` must be writable.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn anydoc_to_document_json(
    data: *const u8,
    len: usize,
    format: i32,
    out: *mut AnydocBuffer,
) -> i32 {
    let data = unsafe { bytes(data, len) };
    run(out, || {
        let format = parse_format(format)?;
        let parsed = anydoc::to_document(data, format).map_err(convert_error)?;
        Ok(document(&parsed).to_string().into_bytes())
    })
}

/// The `FORMATS` index the content's signature names, or `-1`.
///
/// # Safety
/// `data` must point to `len` readable bytes.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn anydoc_format_from_bytes(data: *const u8, len: usize) -> i32 {
    let data = unsafe { bytes(data, len) };
    catch_unwind(|| format_index(anydoc::Format::from_bytes(data))).unwrap_or(-1)
}

/// The `FORMATS` index a UTF-8 extension (no leading dot) names, or `-1`.
///
/// # Safety
/// `ext` must point to `len` readable bytes.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn anydoc_format_from_extension(ext: *const u8, len: usize) -> i32 {
    let ext = unsafe { bytes(ext, len) };
    catch_unwind(|| {
        std::str::from_utf8(ext).ok().and_then(anydoc::Format::from_extension)
    })
    .map(format_index)
    .unwrap_or(-1)
}

unsafe fn bytes<'a>(ptr: *const u8, len: usize) -> &'a [u8] {
    if ptr.is_null() || len == 0 { &[] } else { unsafe { std::slice::from_raw_parts(ptr, len) } }
}

/// Run an export body, writing its result or error JSON to `out`. A panic
/// must not unwind into the caller, so it becomes a `panic` error.
fn run(out: *mut AnydocBuffer, body: impl FnOnce() -> Result<Vec<u8>, Value>) -> i32 {
    let result = catch_unwind(AssertUnwindSafe(body)).unwrap_or_else(|payload| {
        let message = payload
            .downcast_ref::<&str>()
            .map(|s| s.to_string())
            .or_else(|| payload.downcast_ref::<String>().cloned())
            .unwrap_or_else(|| "anydoc panicked".into());
        Err(json!({ "code": "panic", "message": message }))
    });
    let (status, buffer) = match result {
        Ok(bytes) => (ANYDOC_OK, AnydocBuffer::from_vec(bytes)),
        Err(error) => (ANYDOC_ERROR, AnydocBuffer::from_vec(error.to_string().into_bytes())),
    };
    if out.is_null() {
        unsafe { anydoc_buffer_free(buffer) };
    } else {
        unsafe { out.write(buffer) };
    }
    status
}

fn invalid(message: &str) -> Value {
    json!({ "code": "invalidArgument", "message": message })
}

fn parse_format(index: i32) -> Result<Option<anydoc::Format>, Value> {
    if index == -1 {
        return Ok(None);
    }
    usize::try_from(index)
        .ok()
        .and_then(|i| FORMATS.get(i))
        .map(|(_, format)| Some(*format))
        .ok_or_else(|| invalid(&format!("unknown format index {index}")))
}

fn format_index(format: Option<anydoc::Format>) -> i32 {
    format
        .and_then(|format| FORMATS.iter().position(|(_, f)| *f == format))
        .map_or(-1, |i| i as i32)
}

fn convert_error(error: anydoc::ConvertError) -> Value {
    let mut out = Map::new();
    out.insert("code".into(), error.code().into());
    out.insert("message".into(), error.to_string().into());
    match &error {
        anydoc::ConvertError::NeedsOcr { pages, page_count } => {
            out.insert("pages".into(), json!(pages));
            out.insert("pageCount".into(), json!(page_count));
        }
        anydoc::ConvertError::Malformed { part, .. } => {
            out.insert("part".into(), json!(part));
        }
        anydoc::ConvertError::ResourceLimit { limit, .. } => {
            out.insert("limit".into(), json!(limit));
        }
        anydoc::ConvertError::MissingPart { part } => {
            out.insert("part".into(), json!(part));
        }
        _ => {}
    }
    Value::Object(out)
}

// Document model -> JSON, in the Node binding's shape. Polymorphic objects
// lead with `kind` so System.Text.Json can read it as the type discriminator.

fn document(doc: &model::Document) -> Value {
    let engine = base64::engine::general_purpose::STANDARD;
    json!({
        "blocks": blocks(&doc.blocks),
        "notes": doc.notes.iter().map(|note| json!({
            "id": note.id,
            "kind": match note.kind {
                model::NoteKind::Footnote => "footnote",
                model::NoteKind::Endnote => "endnote",
            },
            "blocks": blocks(&note.blocks),
        })).collect::<Vec<_>>(),
        "assets": doc.assets.iter().map(|asset| json!({
            "id": asset.id.0,
            "mediaType": asset.media_type,
            "originPart": asset.origin_part,
            "data": engine.encode(&asset.bytes),
        })).collect::<Vec<_>>(),
    })
}

fn blocks(blocks: &[model::Block]) -> Vec<Value> {
    blocks.iter().map(block).collect()
}

fn block(block: &model::Block) -> Value {
    match block {
        model::Block::Heading { level, anchor, content } => json!({
            "kind": "heading",
            "level": level,
            "anchor": anchor,
            "content": inlines(content),
        }),
        model::Block::Paragraph(content) => json!({
            "kind": "paragraph",
            "content": inlines(content),
        }),
        model::Block::List(l) => json!({ "kind": "list", "list": list(l) }),
        model::Block::Table(t) => json!({ "kind": "table", "table": table(t) }),
        model::Block::BlockQuote(inner) => json!({
            "kind": "blockQuote",
            "blocks": blocks(inner),
        }),
        model::Block::CodeBlock { lang, text } => json!({
            "kind": "codeBlock",
            "lang": lang,
            "text": text,
        }),
        model::Block::Rule => json!({ "kind": "rule" }),
        model::Block::Math(text) => json!({ "kind": "math", "text": text }),
    }
}

fn inlines(inlines: &[model::Inline]) -> Vec<Value> {
    inlines.iter().map(inline).collect()
}

fn inline(inline: &model::Inline) -> Value {
    match inline {
        model::Inline::Text { text, style } => json!({
            "kind": "text",
            "text": text,
            "style": {
                "bold": style.bold,
                "italic": style.italic,
                "strike": style.strike,
                "code": style.code,
            },
        }),
        model::Inline::Link { content, target } => {
            let (kind, value) = match target {
                model::LinkTarget::External(v) => ("external", v),
                model::LinkTarget::Relative(v) => ("relative", v),
                model::LinkTarget::Anchor(v) => ("anchor", v),
            };
            json!({
                "kind": "link",
                "content": inlines(content),
                "target": { "kind": kind, "value": value },
            })
        }
        model::Inline::Image { alt, source } => json!({
            "kind": "image",
            "alt": alt,
            "source": match source {
                model::ImageSource::External(url) => json!({ "kind": "external", "url": url }),
                model::ImageSource::Asset(id) => json!({ "kind": "asset", "assetId": id.0 }),
                model::ImageSource::Unavailable => json!({ "kind": "unavailable" }),
            },
        }),
        model::Inline::Anchor(anchor) => json!({ "kind": "anchor", "anchor": anchor }),
        model::Inline::NoteRef(id) => json!({ "kind": "noteRef", "noteId": id }),
        model::Inline::LineBreak => json!({ "kind": "lineBreak" }),
        model::Inline::Math(text) => json!({ "kind": "math", "text": text }),
        model::Inline::Checkbox(checked) => json!({ "kind": "checkbox", "checked": checked }),
    }
}

fn list(list: &model::List) -> Value {
    json!({
        "marker": match list.marker {
            model::MarkerKind::Bullet => "bullet",
            model::MarkerKind::Decimal => "decimal",
            model::MarkerKind::LowerAlpha => "lowerAlpha",
            model::MarkerKind::UpperAlpha => "upperAlpha",
            model::MarkerKind::LowerRoman => "lowerRoman",
            model::MarkerKind::UpperRoman => "upperRoman",
        },
        "start": list.start,
        "items": list.items.iter().map(|item| json!({
            "blocks": blocks(&item.blocks),
            "markerLabel": item.marker_label,
        })).collect::<Vec<_>>(),
    })
}

fn table(table: &model::Table) -> Value {
    json!({
        "grid": table.grid.iter().map(|row| row.iter().map(|slot| match slot {
            model::CellSlot::Origin(cell) => json!({
                "kind": "origin",
                "cell": {
                    "blocks": blocks(&cell.blocks),
                    "colSpan": cell.col_span,
                    "rowSpan": cell.row_span,
                },
            }),
            model::CellSlot::Covered { origin_row, origin_col } => json!({
                "kind": "covered",
                "originRow": origin_row,
                "originCol": origin_col,
            }),
        }).collect::<Vec<_>>()).collect::<Vec<_>>(),
        "headerRows": table.header_rows,
        "kind": match table.kind {
            model::TableKind::Data => "data",
            model::TableKind::Layout => "layout",
        },
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    /// The .NET `Format` enum is numbered by this table; a reorder breaks it.
    #[test]
    fn format_indices_are_stable() {
        let names: Vec<&str> = FORMATS.iter().map(|(n, _)| *n).collect();
        assert_eq!(
            names,
            ["doc", "docx", "odt", "pdf", "ppt", "pptx", "rtf", "epub", "xlsx", "ods", "odp", "csv"]
        );
    }

    #[test]
    fn errors_round_trip_through_buffers() {
        let mut out = AnydocBuffer::EMPTY;
        let status = unsafe { anydoc_to_markdown_bytes(b"x".as_ptr(), 1, 99, &mut out) };
        assert_eq!(status, ANYDOC_ERROR);
        let text = unsafe { std::slice::from_raw_parts(out.ptr, out.len) };
        let error: Value = serde_json::from_slice(text).unwrap();
        assert_eq!(error["code"], "invalidArgument");
        unsafe { anydoc_buffer_free(out) };
    }
}

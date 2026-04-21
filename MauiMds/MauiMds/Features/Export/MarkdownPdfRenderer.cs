using MauiMds.Models;
using MauiMds.Pdf;

namespace MauiMds.Features.Export;

/// <summary>
/// Translates parsed <see cref="MarkdownBlock"/> objects into PDF drawing calls.
/// Owned entirely by the app layer so it can reference both MauiMds.Core and MauiMds.Pdf.
/// </summary>
internal sealed class MarkdownPdfRenderer
{
    private readonly PdfLayoutEngine _layout = new();

    // Font sizes (pt)
    private static readonly float[] HeaderSizes = [0f, 24f, 20f, 16f, 14f, 12f, 11f]; // index 1–6
    private const float BodySize     = 11f;
    private const float CodeSize     = 9f;
    private const float SmallSize    = 9f;
    private const float LineHeight   = 16f;
    private const float CodeLineH    = 13f;

    public void Render(PdfDocument document, IEnumerable<MarkdownBlock> blocks)
    {
        var page = document.AddPage();
        foreach (var block in blocks)
            page = RenderBlock(document, page, block);
    }

    // ── Block dispatch ────────────────────────────────────────────────────────

    private PdfPage RenderBlock(PdfDocument doc, PdfPage page, MarkdownBlock block) =>
        block.Type switch
        {
            BlockType.Header        => RenderHeader(doc, page, block),
            BlockType.Paragraph     => RenderParagraph(doc, page, block),
            BlockType.BulletListItem   => RenderBullet(doc, page, block),
            BlockType.OrderedListItem  => RenderOrdered(doc, page, block),
            BlockType.TaskListItem     => RenderTask(doc, page, block),
            BlockType.CodeBlock        => RenderCode(doc, page, block),
            BlockType.BlockQuote       => RenderBlockQuote(doc, page, block),
            BlockType.Table            => RenderTable(doc, page, block),
            BlockType.HorizontalRule   => RenderRule(doc, page),
            BlockType.Image            => RenderImagePlaceholder(doc, page, block),
            BlockType.FrontMatter      => RenderFrontMatter(doc, page, block),
            BlockType.Footnote         => RenderFootnote(doc, page, block),
            _ => page
        };

    // ── Header ────────────────────────────────────────────────────────────────

    private PdfPage RenderHeader(PdfDocument doc, PdfPage page, MarkdownBlock block)
    {
        var level      = Math.Clamp(block.HeaderLevel, 1, 6);
        var fontSize   = HeaderSizes[level];
        var lineH      = fontSize * 1.3f;
        var topMargin  = level <= 2 ? 18f : level <= 4 ? 12f : 8f;
        var botMargin  = level <= 2 ? 10f : 6f;

        var spans = PdfInlineParser.Parse(block.Content, PdfStandardFont.HelveticaBold);
        var lines = _layout.WrapSpans(spans, page.ContentWidth, fontSize);

        page = EnsureSpace(doc, page, topMargin + lines.Count * lineH + botMargin);
        page.Advance(topMargin);

        foreach (var line in lines)
        {
            page.DrawTextLine(page.ContentLeft, page.CurrentY, line.Runs, fontSize);
            page.Advance(lineH);
        }

        page.Advance(botMargin);
        return page;
    }

    // ── Paragraph ─────────────────────────────────────────────────────────────

    private PdfPage RenderParagraph(PdfDocument doc, PdfPage page, MarkdownBlock block)
    {
        if (string.IsNullOrWhiteSpace(block.Content)) return page;

        var spans = PdfInlineParser.Parse(block.Content);
        var lines = _layout.WrapSpans(spans, page.ContentWidth, BodySize);
        if (lines.Count == 0) return page;

        page = EnsureSpace(doc, page, lines.Count * LineHeight + 8f);

        foreach (var line in lines)
        {
            page.DrawTextLine(page.ContentLeft, page.CurrentY, line.Runs, BodySize);
            page.Advance(LineHeight);
        }

        page.Advance(8f);
        return page;
    }

    // ── Lists ─────────────────────────────────────────────────────────────────

    private PdfPage RenderBullet(PdfDocument doc, PdfPage page, MarkdownBlock block)
    {
        var (textX, textWidth) = ListIndent(page, block.ListLevel);

        var spans = PdfInlineParser.Parse(block.Content);
        var lines = _layout.WrapSpans(spans, textWidth, BodySize);
        if (lines.Count == 0) return page;

        page = EnsureSpace(doc, page, lines.Count * LineHeight + 3f);
        page.DrawText(textX - 10f, page.CurrentY, "-", PdfStandardFont.HelveticaBold, BodySize);
        DrawWrappedLines(page, textX, lines);
        page.Advance(3f);
        return page;
    }

    private PdfPage RenderOrdered(PdfDocument doc, PdfPage page, MarkdownBlock block)
    {
        var (textX, textWidth) = ListIndent(page, block.ListLevel);
        var label = $"{block.OrderedNumber}.";

        var spans = PdfInlineParser.Parse(block.Content);
        var lines = _layout.WrapSpans(spans, textWidth, BodySize);
        if (lines.Count == 0) return page;

        page = EnsureSpace(doc, page, lines.Count * LineHeight + 3f);
        page.DrawText(textX - 18f, page.CurrentY, label, PdfStandardFont.Helvetica, BodySize);
        DrawWrappedLines(page, textX, lines);
        page.Advance(3f);
        return page;
    }

    private PdfPage RenderTask(PdfDocument doc, PdfPage page, MarkdownBlock block)
    {
        var (textX, textWidth) = ListIndent(page, block.ListLevel);
        var marker = block.IsChecked ? "[x]" : "[ ]";

        var spans = PdfInlineParser.Parse(block.Content);
        var lines = _layout.WrapSpans(spans, textWidth, BodySize);
        if (lines.Count == 0) return page;

        page = EnsureSpace(doc, page, lines.Count * LineHeight + 3f);
        page.DrawText(textX - 20f, page.CurrentY, marker, PdfStandardFont.Courier, BodySize);
        DrawWrappedLines(page, textX, lines);
        page.Advance(3f);
        return page;
    }

    // ── Code block ────────────────────────────────────────────────────────────

    private PdfPage RenderCode(PdfDocument doc, PdfPage page, MarkdownBlock block)
    {
        var codeLines  = block.Content.Split('\n');
        const float pad = 6f;
        var blockH = pad * 2 + codeLines.Length * CodeLineH;

        // Start a new page if the block won't fit at all (cap check at 120pt)
        page = EnsureSpace(doc, page, Math.Min(blockH, 120f));

        // If it still won't fit after a page break, just draw from the top
        if (!page.HasRoomFor(blockH))
            page = doc.AddPage();

        var boxBottom = page.CurrentY - blockH;
        page.FillRect(page.ContentLeft, boxBottom, page.ContentWidth, blockH, PdfColor.CodeBackground);
        page.Advance(pad);

        foreach (var codeLine in codeLines)
        {
            page.DrawText(page.ContentLeft + pad, page.CurrentY, codeLine, PdfStandardFont.Courier, CodeSize);
            page.Advance(CodeLineH);
        }

        page.Advance(pad + 8f);
        return page;
    }

    // ── Block quote ───────────────────────────────────────────────────────────

    private PdfPage RenderBlockQuote(PdfDocument doc, PdfPage page, MarkdownBlock block)
    {
        const float indent = 18f;
        var textX     = page.ContentLeft + indent;
        var textWidth = page.ContentWidth - indent;

        var spans = PdfInlineParser.Parse(block.Content, PdfStandardFont.HelveticaOblique);
        var lines = _layout.WrapSpans(spans, textWidth, BodySize);
        if (lines.Count == 0) return page;

        page = EnsureSpace(doc, page, lines.Count * LineHeight + 8f);
        var topY = page.CurrentY;

        foreach (var line in lines)
        {
            page.DrawTextLine(textX, page.CurrentY, line.Runs, BodySize);
            page.Advance(LineHeight);
        }

        page.VerticalLine(page.ContentLeft + 3f, page.CurrentY, topY, 2.5f, PdfColor.QuoteBar);
        page.Advance(8f);
        return page;
    }

    // ── Table ─────────────────────────────────────────────────────────────────

    private PdfPage RenderTable(PdfDocument doc, PdfPage page, MarkdownBlock block)
    {
        if (block.TableHeaders.Count == 0) return page;

        var colCount  = block.TableHeaders.Count;
        var colWidth  = page.ContentWidth / colCount;
        const float rowH = 14f;
        const float pad  = 4f;
        const float headerH = rowH + pad * 2;

        page = EnsureSpace(doc, page, headerH + 40f); // ensure at least header + a couple rows

        // Header row
        page.FillRect(page.ContentLeft, page.CurrentY - headerH, page.ContentWidth, headerH, PdfColor.LightGrey);
        page.StrokeRect(page.ContentLeft, page.CurrentY - headerH, page.ContentWidth, headerH, 0.4f, PdfColor.MediumGrey);

        for (var col = 0; col < colCount; col++)
        {
            var cellX = page.ContentLeft + col * colWidth + pad;
            var cellText = block.TableHeaders[col];
            page.DrawText(cellX, page.CurrentY - pad - rowH * 0.75f, cellText, PdfStandardFont.HelveticaBold, SmallSize);
        }

        page.Advance(headerH);

        // Data rows
        foreach (var row in block.TableRows)
        {
            var rowH2 = rowH + pad;
            page = EnsureSpace(doc, page, rowH2);
            page.StrokeRect(page.ContentLeft, page.CurrentY - rowH2, page.ContentWidth, rowH2, 0.3f, PdfColor.MediumGrey);

            for (var col = 0; col < colCount; col++)
            {
                var cellX    = page.ContentLeft + col * colWidth + pad;
                var cellText = col < row.Count ? row[col] : string.Empty;
                page.DrawText(cellX, page.CurrentY - pad - rowH * 0.75f, cellText, PdfStandardFont.Helvetica, SmallSize);
            }

            page.Advance(rowH2);
        }

        page.Advance(8f);
        return page;
    }

    // ── Horizontal rule ───────────────────────────────────────────────────────

    private static PdfPage RenderRule(PdfDocument doc, PdfPage page)
    {
        page = EnsureSpace(doc, page, 16f);
        page.Advance(6f);
        page.HorizontalLine(page.ContentLeft, page.CurrentY, page.ContentWidth, 0.5f, PdfColor.MediumGrey);
        page.Advance(10f);
        return page;
    }

    // ── Image placeholder ─────────────────────────────────────────────────────

    private PdfPage RenderImagePlaceholder(PdfDocument doc, PdfPage page, MarkdownBlock block)
    {
        var label = string.IsNullOrWhiteSpace(block.ImageAltText)
            ? "[Image]"
            : $"[Image: {block.ImageAltText}]";

        page = EnsureSpace(doc, page, LineHeight + 8f);
        page.DrawText(page.ContentLeft, page.CurrentY, label, PdfStandardFont.HelveticaOblique, BodySize, PdfColor.DarkGrey);
        page.Advance(LineHeight + 8f);
        return page;
    }

    // ── Front matter ──────────────────────────────────────────────────────────

    private PdfPage RenderFrontMatter(PdfDocument doc, PdfPage page, MarkdownBlock block)
    {
        var fmLines = block.Content.Split('\n');
        const float pad = 5f;
        var blockH  = pad * 2 + fmLines.Length * CodeLineH;

        page = EnsureSpace(doc, page, blockH + 8f);
        page.FillRect(page.ContentLeft, page.CurrentY - blockH, page.ContentWidth, blockH, PdfColor.LightGrey);
        page.Advance(pad);

        foreach (var fmLine in fmLines)
        {
            page.DrawText(page.ContentLeft + pad, page.CurrentY, fmLine, PdfStandardFont.Courier, SmallSize, PdfColor.DarkGrey);
            page.Advance(CodeLineH);
        }

        page.Advance(pad + 8f);
        return page;
    }

    // ── Footnote ──────────────────────────────────────────────────────────────

    private PdfPage RenderFootnote(PdfDocument doc, PdfPage page, MarkdownBlock block)
    {
        var label = $"[{block.FootnoteId}] {block.Content}";

        var spans = PdfInlineParser.Parse(label);
        var lines = _layout.WrapSpans(spans, page.ContentWidth, SmallSize);
        if (lines.Count == 0) return page;

        page = EnsureSpace(doc, page, lines.Count * (SmallSize + 3f) + 4f);

        foreach (var line in lines)
        {
            page.DrawTextLine(page.ContentLeft, page.CurrentY, line.Runs, SmallSize);
            page.Advance(SmallSize + 3f);
        }

        page.Advance(4f);
        return page;
    }

    // ── Utilities ─────────────────────────────────────────────────────────────

    private static PdfPage EnsureSpace(PdfDocument doc, PdfPage page, float needed)
    {
        if (!page.HasRoomFor(needed))
            return doc.AddPage();
        return page;
    }

    private static (float textX, float textWidth) ListIndent(PdfPage page, int level)
    {
        var indent = 20f + Math.Max(0, level) * 14f;
        return (page.ContentLeft + indent, page.ContentWidth - indent);
    }

    private static void DrawWrappedLines(PdfPage page, float x, List<PdfTextLine> lines)
    {
        foreach (var line in lines)
        {
            page.DrawTextLine(x, page.CurrentY, line.Runs, BodySize);
            page.Advance(LineHeight);
        }
    }
}

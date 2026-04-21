using System.Text;

namespace MauiMds.Pdf;

/// <summary>
/// Serialises a <see cref="PdfDocument"/> to a PDF 1.4 byte array.
///
/// Object layout (fixed):
///   1  – Catalog
///   2  – Pages tree node
///   3  – Font F1 (Helvetica)
///   4  – Font F2 (Helvetica-Bold)
///   5  – Font F3 (Helvetica-Oblique)
///   6  – Font F4 (Helvetica-BoldOblique)
///   7  – Font F5 (Courier)
///   8  – Font F6 (Courier-Bold)
///   Per page i (0-indexed):
///   9 + 2i     – Page dictionary
///   9 + 2i + 1 – Page content stream
/// </summary>
public static class PdfWriter
{
    private static readonly Encoding Latin1 = Encoding.Latin1;

    public static byte[] Write(PdfDocument document)
    {
        var pageCount = document.Pages.Count;
        var totalObjects = 8 + 2 * pageCount;

        // Pre-encode content streams so we know their byte lengths
        var contentBytes = document.Pages
            .Select(p => Latin1.GetBytes(p.GetContentString()))
            .ToArray();

        // Page dict object IDs: page 0 → obj 9, page 1 → obj 11, …
        var pageObjIds = Enumerable.Range(0, pageCount).Select(i => 9 + 2 * i).ToArray();

        var offsets = new long[totalObjects + 1]; // 1-indexed; 0 is the free-list head
        using var ms = new MemoryStream(capacity: 4096 + contentBytes.Sum(b => b.Length));

        // ── Header ────────────────────────────────────────────────────────────
        // The second line contains high-byte chars to signal binary content to tools.
        Write(ms, "%PDF-1.4\n%\xe2\xe3\xcf\xd3\n");

        // ── Fixed objects 1–8 ─────────────────────────────────────────────────
        offsets[1] = ms.Position;
        WriteObject(ms, 1, "<< /Type /Catalog /Pages 2 0 R >>");

        var kids = string.Join(" ", pageObjIds.Select(id => $"{id} 0 R"));
        offsets[2] = ms.Position;
        WriteObject(ms, 2, $"<< /Type /Pages /Kids [{kids}] /Count {pageCount} >>");

        offsets[3] = ms.Position; WriteObject(ms, 3, FontDict("Helvetica"));
        offsets[4] = ms.Position; WriteObject(ms, 4, FontDict("Helvetica-Bold"));
        offsets[5] = ms.Position; WriteObject(ms, 5, FontDict("Helvetica-Oblique"));
        offsets[6] = ms.Position; WriteObject(ms, 6, FontDict("Helvetica-BoldOblique"));
        offsets[7] = ms.Position; WriteObject(ms, 7, FontDict("Courier"));
        offsets[8] = ms.Position; WriteObject(ms, 8, FontDict("Courier-Bold"));

        // ── Per-page objects ──────────────────────────────────────────────────
        for (var i = 0; i < pageCount; i++)
        {
            var pageDictId = 9 + 2 * i;
            var contentId  = pageDictId + 1;

            offsets[pageDictId] = ms.Position;
            WriteObject(ms, pageDictId, PageDict(document, contentId));

            offsets[contentId] = ms.Position;
            WriteStreamObject(ms, contentId, contentBytes[i]);
        }

        // ── Cross-reference table ─────────────────────────────────────────────
        var xrefOffset = ms.Position;
        WriteXRef(ms, totalObjects, offsets);

        // ── Trailer ───────────────────────────────────────────────────────────
        Write(ms, $"trailer\n<< /Size {totalObjects + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string FontDict(string baseFontName) =>
        $"<< /Type /Font /Subtype /Type1 /BaseFont /{baseFontName} /Encoding /WinAnsiEncoding >>";

    private static string PageDict(PdfDocument doc, int contentId) =>
        $"<< /Type /Page /Parent 2 0 R " +
        $"/MediaBox [0 0 {doc.PageWidth:F0} {doc.PageHeight:F0}] " +
        "/Resources << /Font << /F1 3 0 R /F2 4 0 R /F3 5 0 R /F4 6 0 R /F5 7 0 R /F6 8 0 R >> >> " +
        $"/Contents {contentId} 0 R >>";

    private static void WriteObject(Stream s, int id, string dict)
    {
        Write(s, $"{id} 0 obj\n{dict}\nendobj\n");
    }

    private static void WriteStreamObject(Stream s, int id, byte[] data)
    {
        Write(s, $"{id} 0 obj\n<< /Length {data.Length} >>\nstream\n");
        s.Write(data);
        // data already ends with \n from PdfContentBuilder; endstream follows immediately
        Write(s, "endstream\nendobj\n");
    }

    private static void WriteXRef(Stream s, int totalObjects, long[] offsets)
    {
        var sb = new StringBuilder();
        sb.Append($"xref\n0 {totalObjects + 1}\n");
        sb.Append("0000000000 65535 f\r\n"); // free-list head (object 0)
        for (var id = 1; id <= totalObjects; id++)
            sb.Append($"{offsets[id]:D10} 00000 n\r\n"); // 20 bytes per entry
        Write(s, sb.ToString());
    }

    private static void Write(Stream s, string text) =>
        s.Write(Latin1.GetBytes(text));
}

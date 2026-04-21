using System.Text;
using MauiMds.Pdf;

namespace MauiMds.Pdf.Tests.Core;

[TestClass]
public sealed class PdfWriterTests
{
    [TestMethod]
    public void Write_EmptyDocument_ProducesValidPdfHeader()
    {
        var doc   = new PdfDocument();
        doc.AddPage(); // PDF must have at least one page
        var bytes = doc.ToBytes();
        var text  = Encoding.ASCII.GetString(bytes, 0, 8);
        Assert.AreEqual("%PDF-1.4", text);
    }

    [TestMethod]
    public void Write_EmptyDocument_ContainsEofMarker()
    {
        var doc  = new PdfDocument();
        doc.AddPage();
        var text = Encoding.Latin1.GetString(doc.ToBytes());
        Assert.IsTrue(text.Contains("%%EOF"), "PDF must end with %%EOF marker.");
    }

    [TestMethod]
    public void Write_EmptyDocument_ContainsXRefTable()
    {
        var doc  = new PdfDocument();
        doc.AddPage();
        var text = Encoding.Latin1.GetString(doc.ToBytes());
        Assert.IsTrue(text.Contains("xref"), "PDF must contain a cross-reference table.");
        Assert.IsTrue(text.Contains("trailer"), "PDF must contain a trailer.");
        Assert.IsTrue(text.Contains("startxref"), "PDF must contain a startxref entry.");
    }

    [TestMethod]
    public void Write_SinglePage_ContainsCatalogAndPagesDicts()
    {
        var doc  = new PdfDocument();
        doc.AddPage();
        var text = Encoding.Latin1.GetString(doc.ToBytes());
        Assert.IsTrue(text.Contains("/Type /Catalog"), "Must contain a Catalog object.");
        Assert.IsTrue(text.Contains("/Type /Pages"),   "Must contain a Pages tree node.");
        Assert.IsTrue(text.Contains("/Type /Page"),    "Must contain at least one Page dict.");
    }

    [TestMethod]
    public void Write_SinglePage_ContainsAllSixFonts()
    {
        var doc  = new PdfDocument();
        doc.AddPage();
        var text = Encoding.Latin1.GetString(doc.ToBytes());
        Assert.IsTrue(text.Contains("/BaseFont /Helvetica"),           "Helvetica font missing.");
        Assert.IsTrue(text.Contains("/BaseFont /Helvetica-Bold"),      "Helvetica-Bold font missing.");
        Assert.IsTrue(text.Contains("/BaseFont /Helvetica-Oblique"),   "Helvetica-Oblique font missing.");
        Assert.IsTrue(text.Contains("/BaseFont /Courier"),             "Courier font missing.");
    }

    [TestMethod]
    public void Write_MultiplePages_ContainsCorrectPageCount()
    {
        var doc = new PdfDocument();
        doc.AddPage();
        doc.AddPage();
        doc.AddPage();
        var text = Encoding.Latin1.GetString(doc.ToBytes());
        Assert.IsTrue(text.Contains("/Count 3"), "Page count must be 3.");
    }

    [TestMethod]
    public void Write_NonEmpty_ByteArrayIsNonEmpty()
    {
        var doc = new PdfDocument();
        doc.AddPage();
        var bytes = doc.ToBytes();
        Assert.IsTrue(bytes.Length > 0);
    }

    [TestMethod]
    public void Write_XRefEntries_AreExactly20BytesEach()
    {
        var doc  = new PdfDocument();
        doc.AddPage();
        var text = Encoding.Latin1.GetString(doc.ToBytes());

        var xrefIdx = text.IndexOf("xref\n", StringComparison.Ordinal);
        Assert.IsTrue(xrefIdx >= 0);

        // Skip past "xref\n" and the "0 N\n" count line
        var afterXref    = xrefIdx + "xref\n".Length;
        var countLineEnd = text.IndexOf('\n', afterXref) + 1;

        // First entry must be "0000000000 65535 f\r\n" = 20 bytes
        var firstEntry = text.Substring(countLineEnd, 20);
        Assert.AreEqual("0000000000 65535 f\r\n", firstEntry);
    }

    [TestMethod]
    public void Write_PageWithText_ContentStreamIsNonEmpty()
    {
        var doc  = new PdfDocument();
        var page = doc.AddPage();
        page.DrawText(60f, 700f, "Hello PDF", PdfStandardFont.Helvetica, 12f);
        var text = Encoding.Latin1.GetString(doc.ToBytes());
        Assert.IsTrue(text.Contains("BT"), "Content stream must start a text block.");
        Assert.IsTrue(text.Contains("Hello PDF"), "Content stream must contain the drawn text.");
    }

    [TestMethod]
    public void AddPage_Returns_PageWithCorrectContentWidth()
    {
        var doc  = new PdfDocument();
        var page = doc.AddPage();
        Assert.AreEqual(doc.ContentWidth, page.ContentWidth, delta: 0.01f);
    }
}

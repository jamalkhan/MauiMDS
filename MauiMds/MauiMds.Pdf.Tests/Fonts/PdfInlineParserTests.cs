using MauiMds.Pdf;

namespace MauiMds.Pdf.Tests.Fonts;

[TestClass]
public sealed class PdfInlineParserTests
{
    [TestMethod]
    public void Parse_PlainText_ReturnsSingleHelveticaSpan()
    {
        var spans = PdfInlineParser.Parse("hello world");
        Assert.AreEqual(1, spans.Count);
        Assert.AreEqual("hello world", spans[0].Text);
        Assert.AreEqual(PdfStandardFont.Helvetica, spans[0].Font);
    }

    [TestMethod]
    public void Parse_BoldDoubleAsterisk_ReturnsBoldSpan()
    {
        var spans = PdfInlineParser.Parse("before **bold** after");
        Assert.AreEqual(3, spans.Count);
        Assert.AreEqual("before ", spans[0].Text);
        Assert.AreEqual(PdfStandardFont.HelveticaBold, spans[1].Font);
        Assert.AreEqual("bold", spans[1].Text);
        Assert.AreEqual(" after", spans[2].Text);
    }

    [TestMethod]
    public void Parse_BoldDoubleUnderscore_ReturnsBoldSpan()
    {
        var spans = PdfInlineParser.Parse("__bold__");
        Assert.AreEqual(1, spans.Count);
        Assert.AreEqual(PdfStandardFont.HelveticaBold, spans[0].Font);
        Assert.AreEqual("bold", spans[0].Text);
    }

    [TestMethod]
    public void Parse_ItalicSingleAsterisk_ReturnsItalicSpan()
    {
        var spans = PdfInlineParser.Parse("*italic*");
        Assert.AreEqual(1, spans.Count);
        Assert.AreEqual(PdfStandardFont.HelveticaOblique, spans[0].Font);
        Assert.AreEqual("italic", spans[0].Text);
    }

    [TestMethod]
    public void Parse_InlineCode_ReturnsCourierSpan()
    {
        var spans = PdfInlineParser.Parse("use `code` here");
        Assert.AreEqual(3, spans.Count);
        Assert.AreEqual(PdfStandardFont.Courier, spans[1].Font);
        Assert.AreEqual("code", spans[1].Text);
    }

    [TestMethod]
    public void Parse_Link_ExtractsLinkTextWithBlueColor()
    {
        var spans = PdfInlineParser.Parse("[click here](https://example.com)");
        Assert.AreEqual(1, spans.Count);
        Assert.AreEqual("click here", spans[0].Text);
        Assert.AreEqual(PdfColor.LinkBlue.R, spans[0].Color.R, delta: 0.01f);
    }

    [TestMethod]
    public void Parse_UnclosedBold_TreatsMarkersAsLiteralText()
    {
        var spans = PdfInlineParser.Parse("no **close");
        // Text before the unclosed marker is flushed separately; the key invariant is that
        // no span uses a bold font — unclosed markers must not change the font.
        Assert.IsTrue(spans.All(s => s.Font == PdfStandardFont.Helvetica), "Unclosed bold must not produce a bold span.");
        var combined = string.Concat(spans.Select(s => s.Text));
        Assert.IsTrue(combined.Contains("**"), "The ** literal must appear in the output text.");
    }

    [TestMethod]
    public void Parse_EmptyString_ReturnsEmptyList()
    {
        var spans = PdfInlineParser.Parse(string.Empty);
        Assert.AreEqual(0, spans.Count);
    }

    [TestMethod]
    public void Parse_AdjacentFormats_ParsedIndependently()
    {
        var spans = PdfInlineParser.Parse("**bold** and *italic*");
        Assert.IsTrue(spans.Any(s => s.Font == PdfStandardFont.HelveticaBold));
        Assert.IsTrue(spans.Any(s => s.Font == PdfStandardFont.HelveticaOblique));
    }

    [TestMethod]
    public void Parse_CustomBaseFont_UsesThatFontForPlainText()
    {
        var spans = PdfInlineParser.Parse("plain", PdfStandardFont.HelveticaOblique);
        Assert.AreEqual(PdfStandardFont.HelveticaOblique, spans[0].Font);
    }
}

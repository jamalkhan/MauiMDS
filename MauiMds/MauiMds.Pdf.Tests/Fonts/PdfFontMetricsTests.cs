using MauiMds.Pdf;

namespace MauiMds.Pdf.Tests.Fonts;

[TestClass]
public sealed class PdfFontMetricsTests
{
    [TestMethod]
    public void MeasureChar_Space_Helvetica_ReturnsCorrectWidth()
    {
        var width = PdfFontMetrics.MeasureChar(' ', PdfStandardFont.Helvetica, 1000f);
        Assert.AreEqual(278f, width, delta: 0.01f);
    }

    [TestMethod]
    public void MeasureChar_Courier_IsMonospace()
    {
        var a = PdfFontMetrics.MeasureChar('a', PdfStandardFont.Courier, 10f);
        var m = PdfFontMetrics.MeasureChar('M', PdfStandardFont.Courier, 10f);
        var i = PdfFontMetrics.MeasureChar('i', PdfStandardFont.Courier, 10f);
        Assert.AreEqual(a, m, delta: 0.01f);
        Assert.AreEqual(a, i, delta: 0.01f);
    }

    [TestMethod]
    public void MeasureChar_Scales_WithFontSize()
    {
        var w10 = PdfFontMetrics.MeasureChar('A', PdfStandardFont.Helvetica, 10f);
        var w20 = PdfFontMetrics.MeasureChar('A', PdfStandardFont.Helvetica, 20f);
        Assert.AreEqual(w10 * 2f, w20, delta: 0.01f);
    }

    [TestMethod]
    public void MeasureString_EmptyString_ReturnsZero()
    {
        var width = PdfFontMetrics.MeasureString(string.Empty, PdfStandardFont.Helvetica, 12f);
        Assert.AreEqual(0f, width);
    }

    [TestMethod]
    public void MeasureString_SingleChar_MatchesMeasureChar()
    {
        var charWidth   = PdfFontMetrics.MeasureChar('H', PdfStandardFont.Helvetica, 11f);
        var stringWidth = PdfFontMetrics.MeasureString("H", PdfStandardFont.Helvetica, 11f);
        Assert.AreEqual(charWidth, stringWidth, delta: 0.01f);
    }

    [TestMethod]
    public void MeasureString_MultipleChars_SumsIndividualWidths()
    {
        const float size = 12f;
        var expected = PdfFontMetrics.MeasureChar('H', PdfStandardFont.Helvetica, size)
                     + PdfFontMetrics.MeasureChar('i', PdfStandardFont.Helvetica, size);
        var actual = PdfFontMetrics.MeasureString("Hi", PdfStandardFont.Helvetica, size);
        Assert.AreEqual(expected, actual, delta: 0.01f);
    }

    [TestMethod]
    public void MeasureChar_HelveticaBold_WiderThanRegularForTypicalChars()
    {
        var regular = PdfFontMetrics.MeasureChar('B', PdfStandardFont.Helvetica, 12f);
        var bold    = PdfFontMetrics.MeasureChar('B', PdfStandardFont.HelveticaBold, 12f);
        Assert.IsTrue(bold >= regular, "Bold should be at least as wide as regular.");
    }

    [TestMethod]
    public void MeasureChar_OutOfRangeChar_ReturnsFallbackNotZero()
    {
        // Characters outside ASCII 32–126 should return a fallback, not throw or return 0
        var width = PdfFontMetrics.MeasureChar('\x01', PdfStandardFont.Helvetica, 12f);
        Assert.IsTrue(width > 0f);
    }
}

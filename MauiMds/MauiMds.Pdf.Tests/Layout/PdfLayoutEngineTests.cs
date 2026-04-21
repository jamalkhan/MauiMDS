using MauiMds.Pdf;

namespace MauiMds.Pdf.Tests.Layout;

[TestClass]
public sealed class PdfLayoutEngineTests
{
    private static readonly PdfLayoutEngine Engine = new();

    private static List<PdfTextLine> Wrap(string text, float lineWidth, float fontSize = 12f)
    {
        var spans = new[] { new PdfInlineSpan(text, PdfStandardFont.Helvetica) };
        return Engine.WrapSpans(spans, lineWidth, fontSize);
    }

    [TestMethod]
    public void WrapSpans_ShortText_FitsInOneLine()
    {
        var lines = Wrap("Hi", lineWidth: 400f);
        Assert.AreEqual(1, lines.Count);
    }

    [TestMethod]
    public void WrapSpans_EmptySpans_ReturnsEmpty()
    {
        var lines = Engine.WrapSpans([], lineWidth: 400f, fontSize: 12f);
        Assert.AreEqual(0, lines.Count);
    }

    [TestMethod]
    public void WrapSpans_VeryNarrowWidth_WrapsEachWord()
    {
        // Width of 1pt forces each word onto its own line
        var lines = Wrap("one two three", lineWidth: 1f);
        Assert.AreEqual(3, lines.Count);
    }

    [TestMethod]
    public void WrapSpans_ExplicitNewline_SplitsLines()
    {
        var lines = Wrap("line one\nline two", lineWidth: 500f);
        Assert.AreEqual(2, lines.Count);
    }

    [TestMethod]
    public void WrapSpans_MultipleSpans_CombinesOntoSameLine()
    {
        var spans = new[]
        {
            new PdfInlineSpan("Hello ", PdfStandardFont.Helvetica),
            new PdfInlineSpan("world", PdfStandardFont.HelveticaBold)
        };
        var lines = Engine.WrapSpans(spans, lineWidth: 500f, fontSize: 12f);
        // Both spans fit; they should all land on one line (exact run count depends on tokenisation)
        Assert.AreEqual(1, lines.Count);
        Assert.IsTrue(lines[0].Runs.Any(r => r.Font == PdfStandardFont.HelveticaBold && r.Text.Contains("world")));
    }

    [TestMethod]
    public void WrapSpans_LineNotEmpty_HasAtLeastOneRun()
    {
        var lines = Wrap("some text here", lineWidth: 400f);
        Assert.IsTrue(lines.All(l => !l.IsEmpty));
    }

    [TestMethod]
    public void WrapSpans_WrappedLine_DoesNotStartWithSpace()
    {
        // Force a wrap by using a very long word that doesn't fit after the first
        var lines = Wrap("short averylongwordthatpushesitover", lineWidth: 60f, fontSize: 11f);
        if (lines.Count > 1)
        {
            var firstRunText = lines[1].Runs.First().Text;
            Assert.IsFalse(firstRunText.StartsWith(' '), "Continuation line should not start with a space.");
        }
    }

    [TestMethod]
    public void WrapSpans_LargeWidth_SingleLine()
    {
        var lines = Wrap("this entire sentence fits on one line", lineWidth: 10000f);
        Assert.AreEqual(1, lines.Count);
    }
}

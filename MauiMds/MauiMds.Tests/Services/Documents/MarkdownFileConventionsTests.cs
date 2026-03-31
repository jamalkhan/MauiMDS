using MauiMds.Services;

namespace MauiMds.Tests.Services.Documents;

[TestClass]
public sealed class MarkdownFileConventionsTests
{
    [TestMethod]
    public void EnsureMarkdownExtension_AppendsDefaultExtensionWhenMissing()
    {
        var result = MarkdownFileConventions.EnsureMarkdownExtension("notes");

        Assert.AreEqual("notes.mds", result);
    }

    [TestMethod]
    public void DetectNewLine_PrefersWindowsSequenceWhenPresent()
    {
        var result = MarkdownFileConventions.DetectNewLine("a\r\nb\n");

        Assert.AreEqual("\r\n", result);
    }

    [TestMethod]
    public void NormalizeNewLines_ConvertsMixedContentToRequestedDelimiter()
    {
        var result = MarkdownFileConventions.NormalizeNewLines("a\r\nb\rc\n", "\n");

        Assert.AreEqual("a\nb\nc\n", result);
    }
}

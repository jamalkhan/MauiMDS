using System.Text;
using MauiMds.Services;

namespace MauiMds.Core.Tests.Services.Documents;

[TestClass]
public sealed class MarkdownFileConventionsTests
{
    // ── EnsureMarkdownExtension ───────────────────────────────────────────────

    [TestMethod]
    public void EnsureMarkdownExtension_AppendsDefaultExtensionWhenMissing()
    {
        Assert.AreEqual("notes.mds", MarkdownFileConventions.EnsureMarkdownExtension("notes"));
    }

    [TestMethod]
    public void EnsureMarkdownExtension_AlreadyHasMdsExtension_Unchanged()
    {
        Assert.AreEqual("notes.mds", MarkdownFileConventions.EnsureMarkdownExtension("notes.mds"));
    }

    [TestMethod]
    public void EnsureMarkdownExtension_AlreadyHasMdExtension_Unchanged()
    {
        Assert.AreEqual("readme.md", MarkdownFileConventions.EnsureMarkdownExtension("readme.md"));
    }

    [TestMethod]
    public void EnsureMarkdownExtension_ExtensionIsCaseInsensitive()
    {
        Assert.AreEqual("notes.MDS", MarkdownFileConventions.EnsureMarkdownExtension("notes.MDS"));
        Assert.AreEqual("notes.MD", MarkdownFileConventions.EnsureMarkdownExtension("notes.MD"));
    }

    // ── DetectNewLine ─────────────────────────────────────────────────────────

    [TestMethod]
    public void DetectNewLine_PrefersWindowsSequenceWhenPresent()
    {
        Assert.AreEqual("\r\n", MarkdownFileConventions.DetectNewLine("a\r\nb\n"));
    }

    [TestMethod]
    public void DetectNewLine_UnixOnly_ReturnsLineFeed()
    {
        Assert.AreEqual("\n", MarkdownFileConventions.DetectNewLine("a\nb\nc"));
    }

    [TestMethod]
    public void DetectNewLine_OldMacCr_ReturnsCarriageReturn()
    {
        Assert.AreEqual("\r", MarkdownFileConventions.DetectNewLine("a\rb\rc"));
    }

    [TestMethod]
    public void DetectNewLine_NoneFound_ReturnsLineFeed()
    {
        Assert.AreEqual("\n", MarkdownFileConventions.DetectNewLine("no newlines here"));
    }

    // ── NormalizeNewLines ─────────────────────────────────────────────────────

    [TestMethod]
    public void NormalizeNewLines_ConvertsMixedContentToRequestedDelimiter()
    {
        Assert.AreEqual("a\nb\nc\n", MarkdownFileConventions.NormalizeNewLines("a\r\nb\rc\n", "\n"));
    }

    [TestMethod]
    public void NormalizeNewLines_AlreadyNormalized_ReturnsUnchanged()
    {
        var input = "line one\nline two\nline three";
        Assert.AreEqual(input, MarkdownFileConventions.NormalizeNewLines(input, "\n"));
    }

    [TestMethod]
    public void NormalizeNewLines_CrLfToLf_ConvertsAll()
    {
        Assert.AreEqual("a\nb\nc", MarkdownFileConventions.NormalizeNewLines("a\r\nb\r\nc", "\n"));
    }

    [TestMethod]
    public void NormalizeNewLines_LfToCrLf_ConvertsAll()
    {
        Assert.AreEqual("a\r\nb\r\nc", MarkdownFileConventions.NormalizeNewLines("a\nb\nc", "\r\n"));
    }

    // ── EnsureValidFileName ───────────────────────────────────────────────────

    [TestMethod]
    public void EnsureValidFileName_ValidName_ReturnsTrimmed()
    {
        Assert.AreEqual("notes", MarkdownFileConventions.EnsureValidFileName("  notes  ", allowEmpty: false));
    }

    [TestMethod]
    public void EnsureValidFileName_EmptyWhenAllowed_ReturnsEmpty()
    {
        Assert.AreEqual(string.Empty, MarkdownFileConventions.EnsureValidFileName(string.Empty, allowEmpty: true));
    }

    [TestMethod]
    public void EnsureValidFileName_EmptyWhenNotAllowed_Throws()
    {
        Assert.ThrowsException<InvalidOperationException>(
            () => MarkdownFileConventions.EnsureValidFileName(string.Empty, allowEmpty: false));
    }

    // ── ValidateExtension ─────────────────────────────────────────────────────

    [TestMethod]
    public void ValidateExtension_MdsExtension_DoesNotThrow()
    {
        MarkdownFileConventions.ValidateExtension("/tmp/notes.mds");
    }

    [TestMethod]
    public void ValidateExtension_MdExtension_DoesNotThrow()
    {
        MarkdownFileConventions.ValidateExtension("/tmp/readme.md");
    }

    [TestMethod]
    public void ValidateExtension_InvalidExtension_Throws()
    {
        Assert.ThrowsException<InvalidOperationException>(
            () => MarkdownFileConventions.ValidateExtension("/tmp/notes.txt"));
    }

    // ── ResolveEncoding ───────────────────────────────────────────────────────

    [TestMethod]
    public void ResolveEncoding_ValidName_ReturnsEncoding()
    {
        var encoding = MarkdownFileConventions.ResolveEncoding("utf-8");
        Assert.AreEqual(Encoding.UTF8.CodePage, encoding.CodePage);
    }

    [TestMethod]
    public void ResolveEncoding_InvalidName_FallsBackToUtf8()
    {
        var encoding = MarkdownFileConventions.ResolveEncoding("not-a-real-encoding");
        Assert.AreEqual(Encoding.UTF8.CodePage, encoding.CodePage);
    }

    // ── DetectEncoding ────────────────────────────────────────────────────────

    [TestMethod]
    public void DetectEncoding_Utf8Bom_ReturnsUtf8()
    {
        var bom = new byte[] { 0xEF, 0xBB, 0xBF, (byte)'a' };
        Assert.AreEqual(Encoding.UTF8.CodePage, MarkdownFileConventions.DetectEncoding(bom).CodePage);
    }

    [TestMethod]
    public void DetectEncoding_NoBom_ReturnsUtf8()
    {
        var bytes = Encoding.UTF8.GetBytes("plain text");
        Assert.AreEqual(Encoding.UTF8.CodePage, MarkdownFileConventions.DetectEncoding(bytes).CodePage);
    }

    [TestMethod]
    public void DetectEncoding_Utf16LeBom_ReturnsUnicode()
    {
        var bom = new byte[] { 0xFF, 0xFE, 0x00, 0x00 };
        Assert.AreEqual(Encoding.Unicode.CodePage, MarkdownFileConventions.DetectEncoding(bom).CodePage);
    }
}

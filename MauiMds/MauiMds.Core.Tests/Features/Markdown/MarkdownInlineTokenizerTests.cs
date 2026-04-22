using MauiMds.Features.Markdown;

namespace MauiMds.Core.Tests.Features.Markdown;

[TestClass]
public sealed class MarkdownInlineTokenizerTests
{
    [TestMethod]
    public void Tokenize_PlainText_ReturnsSinglePlainToken()
    {
        var tokens = MarkdownInlineTokenizer.Tokenize("Hello world");

        Assert.AreEqual(1, tokens.Count);
        Assert.AreEqual(InlineTokenStyle.Plain, tokens[0].Style);
        Assert.AreEqual("Hello world", tokens[0].Text);
    }

    [TestMethod]
    public void Tokenize_EmptyString_ReturnsEmptyList()
    {
        var tokens = MarkdownInlineTokenizer.Tokenize(string.Empty);

        Assert.AreEqual(0, tokens.Count);
    }

    [TestMethod]
    public void Tokenize_BoldText_EmitsBoldToken()
    {
        var tokens = MarkdownInlineTokenizer.Tokenize("**bold**");

        Assert.AreEqual(1, tokens.Count);
        Assert.AreEqual(InlineTokenStyle.Bold, tokens[0].Style);
        Assert.AreEqual("bold", tokens[0].Text);
    }

    [TestMethod]
    public void Tokenize_ItalicAsterisk_EmitsItalicToken()
    {
        var tokens = MarkdownInlineTokenizer.Tokenize("*italic*");

        Assert.AreEqual(1, tokens.Count);
        Assert.AreEqual(InlineTokenStyle.Italic, tokens[0].Style);
        Assert.AreEqual("italic", tokens[0].Text);
    }

    [TestMethod]
    public void Tokenize_ItalicUnderscore_EmitsItalicToken()
    {
        var tokens = MarkdownInlineTokenizer.Tokenize("_italic_");

        Assert.AreEqual(1, tokens.Count);
        Assert.AreEqual(InlineTokenStyle.Italic, tokens[0].Style);
        Assert.AreEqual("italic", tokens[0].Text);
    }

    [TestMethod]
    public void Tokenize_Underline_EmitsUnderlineToken()
    {
        var tokens = MarkdownInlineTokenizer.Tokenize("__underline__");

        Assert.AreEqual(1, tokens.Count);
        Assert.AreEqual(InlineTokenStyle.Underline, tokens[0].Style);
        Assert.AreEqual("underline", tokens[0].Text);
    }

    [TestMethod]
    public void Tokenize_Strikethrough_EmitsStrikethroughToken()
    {
        var tokens = MarkdownInlineTokenizer.Tokenize("~~struck~~");

        Assert.AreEqual(1, tokens.Count);
        Assert.AreEqual(InlineTokenStyle.Strikethrough, tokens[0].Style);
        Assert.AreEqual("struck", tokens[0].Text);
    }

    [TestMethod]
    public void Tokenize_Highlight_EmitsHighlightToken()
    {
        var tokens = MarkdownInlineTokenizer.Tokenize("==highlighted==");

        Assert.AreEqual(1, tokens.Count);
        Assert.AreEqual(InlineTokenStyle.Highlight, tokens[0].Style);
        Assert.AreEqual("highlighted", tokens[0].Text);
    }

    [TestMethod]
    public void Tokenize_InlineCode_EmitsInlineCodeToken()
    {
        var tokens = MarkdownInlineTokenizer.Tokenize("`code`");

        Assert.AreEqual(1, tokens.Count);
        Assert.AreEqual(InlineTokenStyle.InlineCode, tokens[0].Style);
        Assert.AreEqual("code", tokens[0].Text);
    }

    [TestMethod]
    public void Tokenize_Link_EmitsLinkTokenWithTarget()
    {
        var tokens = MarkdownInlineTokenizer.Tokenize("[click here](https://example.com)");

        Assert.AreEqual(1, tokens.Count);
        Assert.AreEqual(InlineTokenStyle.Link, tokens[0].Style);
        Assert.AreEqual("click here", tokens[0].Text);
        Assert.AreEqual("https://example.com", tokens[0].Target);
    }

    [TestMethod]
    public void Tokenize_LinkWithTitle_StripsTitle()
    {
        var tokens = MarkdownInlineTokenizer.Tokenize("[text](https://example.com \"My Title\")");

        Assert.AreEqual(1, tokens.Count);
        Assert.AreEqual(InlineTokenStyle.Link, tokens[0].Style);
        Assert.AreEqual("https://example.com", tokens[0].Target);
    }

    [TestMethod]
    public void Tokenize_FootnoteReference_EmitsFootnoteReferenceToken()
    {
        var tokens = MarkdownInlineTokenizer.Tokenize("[^1]");

        Assert.AreEqual(1, tokens.Count);
        Assert.AreEqual(InlineTokenStyle.FootnoteReference, tokens[0].Style);
        Assert.AreEqual("[1]", tokens[0].Text);
    }

    [TestMethod]
    public void Tokenize_Superscript_EmitsSuperscriptToken()
    {
        var tokens = MarkdownInlineTokenizer.Tokenize("^sup^");

        Assert.AreEqual(1, tokens.Count);
        Assert.AreEqual(InlineTokenStyle.Superscript, tokens[0].Style);
        Assert.AreEqual("sup", tokens[0].Text);
    }

    [TestMethod]
    public void Tokenize_Subscript_EmitsSubscriptToken()
    {
        var tokens = MarkdownInlineTokenizer.Tokenize("~sub~");

        Assert.AreEqual(1, tokens.Count);
        Assert.AreEqual(InlineTokenStyle.Subscript, tokens[0].Style);
        Assert.AreEqual("sub", tokens[0].Text);
    }

    [TestMethod]
    public void Tokenize_EscapedCharacter_EmitsLiteralPlainToken()
    {
        var tokens = MarkdownInlineTokenizer.Tokenize("\\*");

        Assert.AreEqual(1, tokens.Count);
        Assert.AreEqual(InlineTokenStyle.Plain, tokens[0].Style);
        Assert.AreEqual("*", tokens[0].Text);
    }

    [TestMethod]
    public void Tokenize_AngleBracketHttpLink_EmitsLinkToken()
    {
        var tokens = MarkdownInlineTokenizer.Tokenize("<https://example.com>");

        Assert.AreEqual(1, tokens.Count);
        Assert.AreEqual(InlineTokenStyle.Link, tokens[0].Style);
        Assert.AreEqual("https://example.com", tokens[0].Text);
        Assert.AreEqual("https://example.com", tokens[0].Target);
    }

    [TestMethod]
    public void Tokenize_AngleBracketEmail_EmitsMailtoLink()
    {
        var tokens = MarkdownInlineTokenizer.Tokenize("<user@example.com>");

        Assert.AreEqual(1, tokens.Count);
        Assert.AreEqual(InlineTokenStyle.Link, tokens[0].Style);
        Assert.AreEqual("user@example.com", tokens[0].Text);
        Assert.AreEqual("mailto:user@example.com", tokens[0].Target);
    }

    [TestMethod]
    public void Tokenize_HtmlEntity_DecodesAmpersand()
    {
        var tokens = MarkdownInlineTokenizer.Tokenize("&amp;");

        var text = string.Concat(tokens.Select(t => t.Text));
        StringAssert.Contains(text, "&");
        Assert.IsFalse(text.Contains("&amp;"), "HTML entity should be decoded");
    }

    [TestMethod]
    public void Tokenize_HtmlEntity_DecodesLtGt()
    {
        var tokens = MarkdownInlineTokenizer.Tokenize("&lt;tag&gt;");

        var text = string.Concat(tokens.Select(t => t.Text));
        StringAssert.Contains(text, "<tag>");
    }

    [TestMethod]
    public void Tokenize_HtmlBrTag_ConvertedToNewline()
    {
        var tokens = MarkdownInlineTokenizer.Tokenize("line<br>next");

        var text = string.Concat(tokens.Select(t => t.Text));
        StringAssert.Contains(text, "\n");
    }

    [TestMethod]
    public void Tokenize_BareUrl_EmitsAutolink()
    {
        var tokens = MarkdownInlineTokenizer.Tokenize("Visit https://example.com today");

        var linkToken = tokens.FirstOrDefault(t => t.Style == InlineTokenStyle.Link);
        Assert.IsNotNull(linkToken, "Expected a link token for bare URL");
        Assert.AreEqual("https://example.com", linkToken.Text);
    }

    [TestMethod]
    public void Tokenize_MixedContent_EmitsCorrectSequence()
    {
        var tokens = MarkdownInlineTokenizer.Tokenize("Hello **world** and `code`");

        Assert.IsTrue(tokens.Any(t => t.Style == InlineTokenStyle.Plain && t.Text.Contains("Hello")));
        Assert.IsTrue(tokens.Any(t => t.Style == InlineTokenStyle.Bold && t.Text == "world"));
        Assert.IsTrue(tokens.Any(t => t.Style == InlineTokenStyle.InlineCode && t.Text == "code"));
    }

    [TestMethod]
    public void Tokenize_UnderlineBeforeItalicUnderscore_CorrectPriority()
    {
        // __underline__ should match before _italic_ due to ordering
        var tokens = MarkdownInlineTokenizer.Tokenize("__underline__");

        Assert.AreEqual(1, tokens.Count);
        Assert.AreEqual(InlineTokenStyle.Underline, tokens[0].Style);
    }

    [TestMethod]
    public void Tokenize_UnmatchedDelimiter_EmitsAsPlainText()
    {
        var tokens = MarkdownInlineTokenizer.Tokenize("just *star");

        var text = string.Concat(tokens.Select(t => t.Text));
        StringAssert.Contains(text, "*");
        Assert.IsFalse(tokens.Any(t => t.Style == InlineTokenStyle.Italic));
    }
}

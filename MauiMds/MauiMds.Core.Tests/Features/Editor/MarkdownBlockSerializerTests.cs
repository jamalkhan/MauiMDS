using MauiMds.Features.Editor;
using MauiMds.Models;

namespace MauiMds.Core.Tests.Features.Editor;

[TestClass]
public sealed class MarkdownBlockSerializerTests
{
    private static MarkdownBlockSerializer CreateSerializer() => new();

    [TestMethod]
    public void Serialize_ProducesMarkdownForSupportedAndFallbackBlocks()
    {
        var serializer = CreateSerializer();
        MarkdownBlock[] blocks =
        [
            new MarkdownBlock { Type = BlockType.Header, HeaderLevel = 2, Content = "Title" },
            new MarkdownBlock { Type = BlockType.Paragraph, Content = "Body text" },
            new MarkdownBlock { Type = BlockType.BulletListItem, ListLevel = 1, Content = "Nested bullet" },
            new MarkdownBlock { Type = BlockType.TaskListItem, Content = "Done item", IsChecked = true },
            new MarkdownBlock { Type = BlockType.BlockQuote, QuoteLevel = 2, Content = "Quoted line" },
            new MarkdownBlock { Type = BlockType.CodeBlock, CodeLanguage = "csharp", Content = "Console.WriteLine(\"hi\");" },
            new MarkdownBlock { Type = BlockType.Image, ImageAltText = "alt", ImageSource = "image.png" }
        ];

        var markdown = serializer.Serialize(blocks, "\n");

        StringAssert.Contains(markdown, "## Title");
        StringAssert.Contains(markdown, "Body text");
        StringAssert.Contains(markdown, "  - Nested bullet");
        StringAssert.Contains(markdown, "- [x] Done item");
        StringAssert.Contains(markdown, "> > Quoted line");
        StringAssert.Contains(markdown, "```csharp\nConsole.WriteLine(\"hi\");\n```");
        StringAssert.Contains(markdown, "![alt](image.png)");
    }

    [TestMethod]
    public void Serialize_TableAndFrontMatter_RoundTripsReasonably()
    {
        var serializer = CreateSerializer();
        MarkdownBlock[] blocks =
        [
            new MarkdownBlock { Type = BlockType.FrontMatter, Content = "title: Test" },
            new MarkdownBlock
            {
                Type = BlockType.Table,
                TableHeaders = ["Name", "Value"],
                TableAlignments = [MarkdownAlignment.Left, MarkdownAlignment.Right],
                TableRows = [["A", "1"]]
            }
        ];

        var markdown = serializer.Serialize(blocks, "\n");

        StringAssert.Contains(markdown, "---\ntitle: Test\n---");
        StringAssert.Contains(markdown, "| Name | Value |");
        StringAssert.Contains(markdown, "| :--- | ---: |");
        StringAssert.Contains(markdown, "| A | 1 |");
    }

    // ── New tests ────────────────────────────────────────────────────────────

    [TestMethod]
    public void Serialize_EmptyBlockList_ReturnsEmptyString()
    {
        var result = CreateSerializer().Serialize([]);

        Assert.AreEqual(string.Empty, result);
    }

    [TestMethod]
    public void Serialize_HorizontalRule_EmitsDashes()
    {
        var result = CreateSerializer().Serialize(
            [new MarkdownBlock { Type = BlockType.HorizontalRule }], "\n");

        Assert.AreEqual("---", result);
    }

    [TestMethod]
    public void Serialize_OrderedListItem_EmitsNumberPrefix()
    {
        var result = CreateSerializer().SerializeBlock(
            new MarkdownBlock { Type = BlockType.OrderedListItem, OrderedNumber = 3, Content = "Third item" });

        Assert.AreEqual("3. Third item", result);
    }

    [TestMethod]
    public void Serialize_OrderedListItem_NestedWithIndent()
    {
        var result = CreateSerializer().SerializeBlock(
            new MarkdownBlock { Type = BlockType.OrderedListItem, OrderedNumber = 1, ListLevel = 1, Content = "Nested" });

        Assert.AreEqual("  1. Nested", result);
    }

    [TestMethod]
    public void Serialize_Footnote_EmitsFootnoteSyntax()
    {
        var result = CreateSerializer().SerializeBlock(
            new MarkdownBlock { Type = BlockType.Footnote, FootnoteId = "1", Content = "Footnote body" });

        Assert.AreEqual("[^1]: Footnote body", result);
    }

    [TestMethod]
    public void Serialize_DefinitionTermAndDetail_EmitsCorrectSyntax()
    {
        MarkdownBlock[] blocks =
        [
            new MarkdownBlock { Type = BlockType.DefinitionTerm, Content = "Apple" },
            new MarkdownBlock { Type = BlockType.DefinitionDetail, Content = "A fruit" }
        ];

        var markdown = CreateSerializer().Serialize(blocks, "\n");

        StringAssert.Contains(markdown, "Apple");
        StringAssert.Contains(markdown, ": A fruit");
    }

    [TestMethod]
    public void Serialize_Admonition_EmitsGitHubAlertSyntax()
    {
        var result = CreateSerializer().SerializeBlock(
            new MarkdownBlock { Type = BlockType.Admonition, AdmonitionType = "WARNING", Content = "Be careful." },
            "\n");

        StringAssert.Contains(result, "> [!WARNING]");
        StringAssert.Contains(result, "> Be careful.");
    }

    [TestMethod]
    public void Serialize_TaskListItem_Unchecked_EmitsEmptyBracket()
    {
        var result = CreateSerializer().SerializeBlock(
            new MarkdownBlock { Type = BlockType.TaskListItem, Content = "Pending task", IsChecked = false });

        Assert.AreEqual("- [ ] Pending task", result);
    }

    [TestMethod]
    public void Serialize_WindowsNewLine_UsedThroughout()
    {
        MarkdownBlock[] blocks =
        [
            new MarkdownBlock { Type = BlockType.Header, HeaderLevel = 1, Content = "Title" },
            new MarkdownBlock { Type = BlockType.Paragraph, Content = "Body" }
        ];

        var markdown = CreateSerializer().Serialize(blocks, "\r\n");

        Assert.IsTrue(markdown.Contains("\r\n"), "Expected CRLF separators between blocks");
        Assert.IsFalse(markdown.Contains("\r\n\r\n\n"), "Should not mix CRLF with LF");
    }

    [TestMethod]
    public void Serialize_Admonition_WithCustomTitle_EmitsTitleOnTypeLine()
    {
        var result = CreateSerializer().SerializeBlock(
            new MarkdownBlock
            {
                Type = BlockType.Admonition,
                AdmonitionType = "NOTE",
                AdmonitionTitle = "My Custom Title",
                Content = "Some body text."
            }, "\n");

        StringAssert.Contains(result, "> [!NOTE] My Custom Title");
        StringAssert.Contains(result, "> Some body text.");
    }

    [TestMethod]
    public void Serialize_Admonition_WithoutTitle_EmitsTypeOnly()
    {
        var result = CreateSerializer().SerializeBlock(
            new MarkdownBlock
            {
                Type = BlockType.Admonition,
                AdmonitionType = "WARNING",
                AdmonitionTitle = string.Empty,
                Content = "Be careful."
            }, "\n");

        StringAssert.Contains(result, "> [!WARNING]");
        Assert.IsFalse(result.Contains("> [!WARNING] "), "Should not have trailing space after type when no title");
    }

    [TestMethod]
    public void Serialize_ImageWithTitle_EmitsFullImageSyntax()
    {
        var result = CreateSerializer().SerializeBlock(
            new MarkdownBlock { Type = BlockType.Image, ImageAltText = "Logo", ImageSource = "logo.png", ImageTitle = "Company Logo" });

        Assert.AreEqual("![Logo](logo.png \"Company Logo\")", result);
    }

    [TestMethod]
    public void Serialize_TableAlignment_CenterEmitsColonOnBothSides()
    {
        var serializer = CreateSerializer();
        MarkdownBlock[] blocks =
        [
            new MarkdownBlock
            {
                Type = BlockType.Table,
                TableHeaders = ["Left", "Center", "Right"],
                TableAlignments = [MarkdownAlignment.Left, MarkdownAlignment.Center, MarkdownAlignment.Right],
                TableRows = [["a", "b", "c"]]
            }
        ];

        var markdown = serializer.Serialize(blocks, "\n");

        StringAssert.Contains(markdown, "| :--- | :---: | ---: |");
    }

    [TestMethod]
    public void Serialize_BlockQuote_MultiLine_EmitsPrefixOnEachLine()
    {
        var result = CreateSerializer().SerializeBlock(
            new MarkdownBlock { Type = BlockType.BlockQuote, QuoteLevel = 1, Content = "line one\nline two" },
            "\n");

        StringAssert.Contains(result, "> line one");
        StringAssert.Contains(result, "> line two");
        Assert.AreEqual(2, result.Split('\n').Length, "Expected exactly two prefixed lines");
    }
}

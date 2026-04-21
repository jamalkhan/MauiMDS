using MauiMds.Models;
using MauiMds.Processors;
using MauiMds.Tests.TestHelpers;

namespace MauiMds.Tests.Processors;

[TestClass]
public sealed class MdsParserTests
{
    [TestMethod]
    public void Parse_RecognizesCoreMarkdownStructures()
    {
        var parser = new MdsParser(new TestLogger<MdsParser>());
        const string markdown = """
---
title: Test
---

# Heading

Paragraph text

> Quote line

- bullet
1. ordered
- [x] done

| Name | Value |
| --- | ---: |
| A | 1 |

```csharp
Console.WriteLine("hi");
```

[^1]: Footnote text
""";

        var blocks = parser.Parse(markdown);

        CollectionAssert.IsSubsetOf(
            new[]
            {
                BlockType.FrontMatter,
                BlockType.Header,
                BlockType.Paragraph,
                BlockType.BlockQuote,
                BlockType.BulletListItem,
                BlockType.OrderedListItem,
                BlockType.TaskListItem,
                BlockType.Table,
                BlockType.CodeBlock,
                BlockType.Footnote
            },
            blocks.Select(block => block.Type).ToArray());

        Assert.AreEqual("title: Test", blocks[0].Content);
        Assert.AreEqual(1, blocks[1].HeaderLevel);
        Assert.AreEqual("Heading", blocks[1].Content);
        Assert.AreEqual("Paragraph text", blocks[2].Content);
        Assert.AreEqual("Quote line", blocks[3].Content.Trim());
        Assert.IsTrue(blocks[6].IsChecked);
        Assert.AreEqual("csharp", blocks.Single(block => block.Type == BlockType.CodeBlock).CodeLanguage);
        Assert.AreEqual("Footnote text", blocks.Single(block => block.Type == BlockType.Footnote).Content);
    }

    [TestMethod]
    public void Parse_MergesAdjacentParagraphLines()
    {
        var parser = new MdsParser(new TestLogger<MdsParser>());

        var blocks = parser.Parse("first line\nsecond line\n\nthird");

        Assert.AreEqual(2, blocks.Count);
        Assert.AreEqual("first line second line", blocks[0].Content);
        Assert.AreEqual("third", blocks[1].Content);
    }

    [TestMethod]
    public void Parse_TwoSpaceLineBreak_InsertsNewlineInParagraph()
    {
        var parser = new MdsParser(new TestLogger<MdsParser>());

        var blocks = parser.Parse("line one  \nline two");

        Assert.AreEqual(1, blocks.Count);
        Assert.AreEqual(BlockType.Paragraph, blocks[0].Type);
        Assert.IsTrue(blocks[0].Content.Contains('\n'), "Expected hard line break (\\n) in paragraph content");
    }

    [TestMethod]
    public void Parse_Admonition_DetectsNoteType()
    {
        var parser = new MdsParser(new TestLogger<MdsParser>());

        var blocks = parser.Parse("> [!NOTE]\n> This is a note.");

        Assert.AreEqual(1, blocks.Count);
        Assert.AreEqual(BlockType.Admonition, blocks[0].Type);
        Assert.AreEqual("NOTE", blocks[0].AdmonitionType);
        Assert.IsTrue(blocks[0].Content.Contains("This is a note."));
    }

    [TestMethod]
    public void Parse_Admonition_DetectsWarningType()
    {
        var parser = new MdsParser(new TestLogger<MdsParser>());

        var blocks = parser.Parse("> [!WARNING]\n> Be careful.");

        Assert.AreEqual(1, blocks.Count);
        Assert.AreEqual(BlockType.Admonition, blocks[0].Type);
        Assert.AreEqual("WARNING", blocks[0].AdmonitionType);
    }

    [TestMethod]
    public void Parse_DefinitionList_EmitsTermAndDetail()
    {
        var parser = new MdsParser(new TestLogger<MdsParser>());

        var blocks = parser.Parse("Apple\n: A fruit\n: Also a company");

        Assert.AreEqual(3, blocks.Count);
        Assert.AreEqual(BlockType.DefinitionTerm, blocks[0].Type);
        Assert.AreEqual("Apple", blocks[0].Content);
        Assert.AreEqual(BlockType.DefinitionDetail, blocks[1].Type);
        Assert.AreEqual("A fruit", blocks[1].Content);
        Assert.AreEqual(BlockType.DefinitionDetail, blocks[2].Type);
        Assert.AreEqual("Also a company", blocks[2].Content);
    }

    [TestMethod]
    public void Parse_ReferenceLinkDefinition_ResolvesInParagraph()
    {
        var parser = new MdsParser(new TestLogger<MdsParser>());

        var blocks = parser.Parse("See [the guide][guide] for details.\n\n[guide]: https://example.com");

        var paragraph = blocks.First(b => b.Type == BlockType.Paragraph);
        Assert.IsTrue(paragraph.Content.Contains("[the guide](https://example.com)"),
            $"Expected resolved link, got: {paragraph.Content}");
    }

    [TestMethod]
    public void Parse_ReferenceLinkImplicit_ResolvesWithSameKey()
    {
        var parser = new MdsParser(new TestLogger<MdsParser>());

        var blocks = parser.Parse("Click [here][] to continue.\n\n[here]: https://example.com");

        var paragraph = blocks.First(b => b.Type == BlockType.Paragraph);
        Assert.IsTrue(paragraph.Content.Contains("[here](https://example.com)"),
            $"Expected resolved implicit reference link, got: {paragraph.Content}");
    }

    [TestMethod]
    public void Parse_ImageWithTitle_ExtractsTitle()
    {
        var parser = new MdsParser(new TestLogger<MdsParser>());

        var blocks = parser.Parse("![alt text](image.png \"My Title\")");

        Assert.AreEqual(1, blocks.Count);
        Assert.AreEqual(BlockType.Image, blocks[0].Type);
        Assert.AreEqual("alt text", blocks[0].ImageAltText);
        Assert.AreEqual("image.png", blocks[0].ImageSource);
        Assert.AreEqual("My Title", blocks[0].ImageTitle);
    }

    [TestMethod]
    public void Parse_ImageWithoutTitle_HasEmptyTitle()
    {
        var parser = new MdsParser(new TestLogger<MdsParser>());

        var blocks = parser.Parse("![alt](image.png)");

        Assert.AreEqual(1, blocks.Count);
        Assert.AreEqual(BlockType.Image, blocks[0].Type);
        Assert.AreEqual(string.Empty, blocks[0].ImageTitle);
    }

    [TestMethod]
    public void Parse_NestedTableInBlockquote_PopulatesChildren()
    {
        var parser = new MdsParser(new TestLogger<MdsParser>());

        var blocks = parser.Parse("> | Name | Value |\n> | --- | --- |\n> | A | 1 |");

        Assert.AreEqual(1, blocks.Count);
        Assert.AreEqual(BlockType.BlockQuote, blocks[0].Type);
        Assert.IsTrue(blocks[0].Children.Count > 0, "Expected nested children for blockquote containing a table");
        Assert.IsTrue(blocks[0].Children.Any(c => c.Type == BlockType.Table), "Expected a Table child block");
    }

    [TestMethod]
    public void Parse_BlockQuoteWithoutTable_HasNoChildren()
    {
        var parser = new MdsParser(new TestLogger<MdsParser>());

        var blocks = parser.Parse("> Simple quote text");

        Assert.AreEqual(1, blocks.Count);
        Assert.AreEqual(BlockType.BlockQuote, blocks[0].Type);
        Assert.AreEqual(0, blocks[0].Children.Count);
    }
}

using MauiMds.Models;
using MauiMds.Processors;
using MauiMds.Core.Tests.TestHelpers;

namespace MauiMds.Core.Tests.Processors;

[TestClass]
public sealed class MdsParserTests
{
    private static MdsParser CreateParser() => new(new TestLogger<MdsParser>());

    [TestMethod]
    public void Parse_RecognizesCoreMarkdownStructures()
    {
        var parser = CreateParser();
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
        var parser = CreateParser();

        var blocks = parser.Parse("first line\nsecond line\n\nthird");

        Assert.AreEqual(2, blocks.Count);
        Assert.AreEqual("first line second line", blocks[0].Content);
        Assert.AreEqual("third", blocks[1].Content);
    }

    [TestMethod]
    public void Parse_TwoSpaceLineBreak_InsertsNewlineInParagraph()
    {
        var parser = CreateParser();

        var blocks = parser.Parse("line one  \nline two");

        Assert.AreEqual(1, blocks.Count);
        Assert.AreEqual(BlockType.Paragraph, blocks[0].Type);
        Assert.IsTrue(blocks[0].Content.Contains('\n'), "Expected hard line break (\\n) in paragraph content");
    }

    [TestMethod]
    public void Parse_Admonition_DetectsNoteType()
    {
        var parser = CreateParser();

        var blocks = parser.Parse("> [!NOTE]\n> This is a note.");

        Assert.AreEqual(1, blocks.Count);
        Assert.AreEqual(BlockType.Admonition, blocks[0].Type);
        Assert.AreEqual("NOTE", blocks[0].AdmonitionType);
        Assert.IsTrue(blocks[0].Content.Contains("This is a note."));
    }

    [TestMethod]
    public void Parse_Admonition_DetectsWarningType()
    {
        var parser = CreateParser();

        var blocks = parser.Parse("> [!WARNING]\n> Be careful.");

        Assert.AreEqual(1, blocks.Count);
        Assert.AreEqual(BlockType.Admonition, blocks[0].Type);
        Assert.AreEqual("WARNING", blocks[0].AdmonitionType);
    }

    [TestMethod]
    public void Parse_DefinitionList_EmitsTermAndDetail()
    {
        var parser = CreateParser();

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
        var parser = CreateParser();

        var blocks = parser.Parse("See [the guide][guide] for details.\n\n[guide]: https://example.com");

        var paragraph = blocks.First(b => b.Type == BlockType.Paragraph);
        Assert.IsTrue(paragraph.Content.Contains("[the guide](https://example.com)"),
            $"Expected resolved link, got: {paragraph.Content}");
    }

    [TestMethod]
    public void Parse_ReferenceLinkImplicit_ResolvesWithSameKey()
    {
        var parser = CreateParser();

        var blocks = parser.Parse("Click [here][] to continue.\n\n[here]: https://example.com");

        var paragraph = blocks.First(b => b.Type == BlockType.Paragraph);
        Assert.IsTrue(paragraph.Content.Contains("[here](https://example.com)"),
            $"Expected resolved implicit reference link, got: {paragraph.Content}");
    }

    [TestMethod]
    public void Parse_ImageWithTitle_ExtractsTitle()
    {
        var parser = CreateParser();

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
        var parser = CreateParser();

        var blocks = parser.Parse("![alt](image.png)");

        Assert.AreEqual(1, blocks.Count);
        Assert.AreEqual(BlockType.Image, blocks[0].Type);
        Assert.AreEqual(string.Empty, blocks[0].ImageTitle);
    }

    [TestMethod]
    public void Parse_NestedTableInBlockquote_PopulatesChildren()
    {
        var parser = CreateParser();

        var blocks = parser.Parse("> | Name | Value |\n> | --- | --- |\n> | A | 1 |");

        Assert.AreEqual(1, blocks.Count);
        Assert.AreEqual(BlockType.BlockQuote, blocks[0].Type);
        Assert.IsTrue(blocks[0].Children.Count > 0, "Expected nested children for blockquote containing a table");
        Assert.IsTrue(blocks[0].Children.Any(c => c.Type == BlockType.Table), "Expected a Table child block");
    }

    [TestMethod]
    public void Parse_BlockQuoteWithoutTable_HasNoChildren()
    {
        var parser = CreateParser();

        var blocks = parser.Parse("> Simple quote text");

        Assert.AreEqual(1, blocks.Count);
        Assert.AreEqual(BlockType.BlockQuote, blocks[0].Type);
        Assert.AreEqual(0, blocks[0].Children.Count);
    }

    // ── New tests ────────────────────────────────────────────────────────────

    [TestMethod]
    public void Parse_Headers_DetectsAllSixLevels()
    {
        var parser = CreateParser();
        var markdown = "# H1\n## H2\n### H3\n#### H4\n##### H5\n###### H6";

        var blocks = parser.Parse(markdown);

        var headers = blocks.Where(b => b.Type == BlockType.Header).ToList();
        Assert.AreEqual(6, headers.Count);
        for (var level = 1; level <= 6; level++)
        {
            var header = headers[level - 1];
            Assert.AreEqual(level, header.HeaderLevel, $"Expected HeaderLevel {level}");
            Assert.AreEqual($"H{level}", header.Content);
        }
    }

    [TestMethod]
    public void Parse_HorizontalRule_DetectedForDashesStarsAndUnderscores()
    {
        var parser = CreateParser();

        foreach (var rule in new[] { "---", "***", "___", "----", "* * *" })
        {
            var blocks = parser.Parse(rule);
            Assert.AreEqual(1, blocks.Count, $"Expected 1 block for rule: '{rule}'");
            Assert.AreEqual(BlockType.HorizontalRule, blocks[0].Type, $"Expected HorizontalRule for: '{rule}'");
        }
    }

    [TestMethod]
    public void Parse_EmptyDocument_ReturnsEmptyList()
    {
        var parser = CreateParser();

        Assert.AreEqual(0, parser.Parse(string.Empty).Count);
        Assert.AreEqual(0, parser.Parse("   ").Count);
        Assert.AreEqual(0, parser.Parse("\n\n\n").Count);
    }

    [TestMethod]
    public void Parse_CodeBlock_WithoutLanguage_EmitsEmptyCodeLanguage()
    {
        var parser = CreateParser();

        var blocks = parser.Parse("```\nsome code\n```");

        Assert.AreEqual(1, blocks.Count);
        Assert.AreEqual(BlockType.CodeBlock, blocks[0].Type);
        Assert.AreEqual(string.Empty, blocks[0].CodeLanguage);
        Assert.AreEqual("some code", blocks[0].Content.Trim());
    }

    [TestMethod]
    public void Parse_NestedBulletList_CarriesCorrectListLevels()
    {
        var parser = CreateParser();

        var blocks = parser.Parse("- top\n  - nested\n    - deep");

        var items = blocks.Where(b => b.Type == BlockType.BulletListItem).ToList();
        Assert.AreEqual(3, items.Count);
        Assert.AreEqual(0, items[0].ListLevel, "top-level item should be level 0");
        Assert.AreEqual(1, items[1].ListLevel, "one-indent item should be level 1");
        Assert.AreEqual(2, items[2].ListLevel, "two-indent item should be level 2");
    }

    [TestMethod]
    public void Parse_OrderedList_EmitsCorrectNumbersAndContent()
    {
        var parser = CreateParser();

        var blocks = parser.Parse("1. First\n2. Second\n3. Third");

        var items = blocks.Where(b => b.Type == BlockType.OrderedListItem).ToList();
        Assert.AreEqual(3, items.Count);
        Assert.AreEqual(1, items[0].OrderedNumber);
        Assert.AreEqual("First", items[0].Content);
        Assert.AreEqual(2, items[1].OrderedNumber);
        Assert.AreEqual(3, items[2].OrderedNumber);
        Assert.AreEqual("Third", items[2].Content);
    }

    [TestMethod]
    public void Parse_TaskListItem_UncheckedState()
    {
        var parser = CreateParser();

        var blocks = parser.Parse("- [ ] not done\n- [x] done");

        var items = blocks.Where(b => b.Type == BlockType.TaskListItem).ToList();
        Assert.AreEqual(2, items.Count);
        Assert.IsFalse(items[0].IsChecked, "Expected unchecked task item");
        Assert.IsTrue(items[1].IsChecked, "Expected checked task item");
        Assert.AreEqual("not done", items[0].Content);
        Assert.AreEqual("done", items[1].Content);
    }

    [TestMethod]
    public void Parse_FrontMatterOnly_ReturnsOneFrontMatterBlock()
    {
        var parser = CreateParser();

        var blocks = parser.Parse("---\ntitle: Solo\nauthor: Test\n---\n");

        Assert.AreEqual(1, blocks.Count);
        Assert.AreEqual(BlockType.FrontMatter, blocks[0].Type);
        Assert.IsTrue(blocks[0].Content.Contains("title: Solo"));
    }

    [TestMethod]
    public void Parse_NoFrontMatter_DoesNotEmitFrontMatterBlock()
    {
        var parser = CreateParser();

        var blocks = parser.Parse("# Just a heading\n\nAnd a paragraph.");

        Assert.IsFalse(blocks.Any(b => b.Type == BlockType.FrontMatter), "Should have no FrontMatter block");
    }

    [TestMethod]
    public void Parse_BackslashLineBreak_InsertsHardBreakInParagraph()
    {
        var parser = CreateParser();

        var blocks = parser.Parse("line one\\\nline two");

        Assert.AreEqual(1, blocks.Count);
        Assert.AreEqual(BlockType.Paragraph, blocks[0].Type);
        Assert.IsTrue(blocks[0].Content.Contains('\n'), "Expected hard line break from trailing backslash");
        Assert.IsFalse(blocks[0].Content.Contains('\\'), "Trailing backslash should be stripped from content");
    }

    [TestMethod]
    public void Parse_Admonition_WithCustomTitle_StoresTitleSeparately()
    {
        var parser = CreateParser();

        var blocks = parser.Parse("> [!NOTE] My Custom Title\n> Some content here.");

        Assert.AreEqual(1, blocks.Count);
        Assert.AreEqual(BlockType.Admonition, blocks[0].Type);
        Assert.AreEqual("NOTE", blocks[0].AdmonitionType);
        Assert.AreEqual("My Custom Title", blocks[0].AdmonitionTitle);
        Assert.IsTrue(blocks[0].Content.Contains("Some content here."));
        Assert.IsFalse(blocks[0].Content.Contains("My Custom Title"), "Title should not bleed into Content");
    }

    [TestMethod]
    public void Parse_Admonition_WithoutTitle_HasEmptyAdmonitionTitle()
    {
        var parser = CreateParser();

        var blocks = parser.Parse("> [!WARNING]\n> Watch out.");

        Assert.AreEqual(1, blocks.Count);
        Assert.AreEqual(BlockType.Admonition, blocks[0].Type);
        Assert.AreEqual(string.Empty, blocks[0].AdmonitionTitle);
        Assert.IsTrue(blocks[0].Content.Contains("Watch out."));
    }

    [TestMethod]
    public void Parse_MultipleFootnotes_AllEmitted()
    {
        var parser = CreateParser();

        var blocks = parser.Parse("[^1]: First footnote\n[^2]: Second footnote\n[^abc]: Named footnote");

        var footnotes = blocks.Where(b => b.Type == BlockType.Footnote).ToList();
        Assert.AreEqual(3, footnotes.Count);
        Assert.IsTrue(footnotes.Any(f => f.FootnoteId == "1" && f.Content == "First footnote"));
        Assert.IsTrue(footnotes.Any(f => f.FootnoteId == "2" && f.Content == "Second footnote"));
        Assert.IsTrue(footnotes.Any(f => f.FootnoteId == "abc" && f.Content == "Named footnote"));
    }
}

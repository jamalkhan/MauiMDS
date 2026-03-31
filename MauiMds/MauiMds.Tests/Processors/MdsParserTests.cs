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
}

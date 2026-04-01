using MauiMds.Features.Editor;
using MauiMds.Models;

namespace MauiMds.Tests.Features.Editor;

[TestClass]
public sealed class MarkdownBlockSerializerTests
{
    [TestMethod]
    public void Serialize_ProducesMarkdownForSupportedAndFallbackBlocks()
    {
        var serializer = new MarkdownBlockSerializer();
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
        var serializer = new MarkdownBlockSerializer();
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
}

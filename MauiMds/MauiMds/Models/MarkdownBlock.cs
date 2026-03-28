namespace MauiMds.Models;

public enum BlockType
{
    Header,
    Paragraph,
    BulletListItem,
    BlockQuote,
    CodeBlock,
    Table
}

public class MarkdownBlock
{
    public BlockType Type { get; set; }
    public int HeaderLevel { get; set; } = 0;   // 1 for #, 2 for ##
    public string Content { get; set; } = string.Empty;
    public string CodeLanguage { get; set; } = string.Empty;
    public List<string> TableHeaders { get; set; } = [];
    public List<List<string>> TableRows { get; set; } = [];
}

namespace MauiMds.Models;

public enum BlockType
{
    Header,
    Paragraph,
    BulletListItem
}

public class MarkdownBlock
{
    public BlockType Type { get; set; }
    public int HeaderLevel { get; set; } = 0;   // 1 for #, 2 for ##
    public string Content { get; set; } = string.Empty;
}
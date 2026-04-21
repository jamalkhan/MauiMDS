namespace MauiMds.Models;

public enum BlockType
{
    FrontMatter,
    Header,
    Paragraph,
    BulletListItem,
    OrderedListItem,
    TaskListItem,
    BlockQuote,
    CodeBlock,
    Table,
    HorizontalRule,
    Image,
    Footnote,
    Admonition,
    DefinitionTerm,
    DefinitionDetail
}

public enum MarkdownAlignment
{
    Left,
    Center,
    Right
}

public class MarkdownBlock
{
    public BlockType Type { get; set; }
    public int HeaderLevel { get; set; } = 0;
    public string Content { get; set; } = string.Empty;
    public string CodeLanguage { get; set; } = string.Empty;
    public int ListLevel { get; set; }
    public int OrderedNumber { get; set; }
    public bool IsChecked { get; set; }
    public int QuoteLevel { get; set; } = 1;
    public string ImageSource { get; set; } = string.Empty;
    public string ImageAltText { get; set; } = string.Empty;
    public string ImageTitle { get; set; } = string.Empty;
    public string FootnoteId { get; set; } = string.Empty;
    public string AdmonitionType { get; set; } = string.Empty;
    public List<string> TableHeaders { get; set; } = [];
    public List<List<string>> TableRows { get; set; } = [];
    public List<MarkdownAlignment> TableAlignments { get; set; } = [];
    public List<MarkdownBlock> Children { get; set; } = [];
}

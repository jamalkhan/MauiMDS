using MauiMds.Models;
using Microsoft.Extensions.Logging;

namespace MauiMds.Processors;

public class MdsParser
{
    private readonly ILogger<MdsParser> _logger;

    public MdsParser(ILogger<MdsParser> logger)
    {
        _logger = logger;
    }

    public List<MarkdownBlock> Parse(string content)
    {
        _logger.LogInformation("Starting markdown parse. CharacterCount: {CharacterCount}", content.Length);
        var blocks = new List<MarkdownBlock>();
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        MarkdownBlock? currentParagraph = null;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            // Header (# or ##)
            if (line.StartsWith("#"))
            {
                if (currentParagraph != null)
                {
                    blocks.Add(currentParagraph);
                    currentParagraph = null;
                }

                int level = 0;
                while (level < line.Length && line[level] == '#') level++;

                string text = line.Substring(level).Trim();
                blocks.Add(new MarkdownBlock
                {
                    Type = BlockType.Header,
                    HeaderLevel = Math.Min(level, 2),
                    Content = text
                });
            }
            // Bullet points (* or -)
            else if (line.StartsWith("* ") || line.StartsWith("- "))
            {
                if (currentParagraph != null)
                {
                    blocks.Add(currentParagraph);
                    currentParagraph = null;
                }

                string bulletText = line.Substring(2).Trim();
                blocks.Add(new MarkdownBlock
                {
                    Type = BlockType.BulletListItem,
                    Content = bulletText
                });
            }
            // Paragraph
            else
            {
                if (currentParagraph == null)
                {
                    currentParagraph = new MarkdownBlock
                    {
                        Type = BlockType.Paragraph,
                        Content = line
                    };
                }
                else
                {
                    currentParagraph.Content += " " + line;
                }
            }
        }

        if (currentParagraph != null)
            blocks.Add(currentParagraph);

        _logger.LogInformation("Completed markdown parse. LineCount: {LineCount}, BlockCount: {BlockCount}", lines.Length, blocks.Count);
        return blocks;
    }
}

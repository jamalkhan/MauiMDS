using System.Text;
using System.Text.RegularExpressions;

namespace MauiMds.Controls;

public sealed class VisualEditorDocumentController
{
    private readonly Stack<string> _undoStack = new();
    private readonly Stack<string> _redoStack = new();

    public void RecordTextChange(string previousText, string currentText)
    {
        if (!string.Equals(previousText, currentText, StringComparison.Ordinal))
        {
            _undoStack.Push(previousText);
            _redoStack.Clear();
        }
    }

    public string? Undo(string currentText)
    {
        if (_undoStack.Count == 0)
        {
            return null;
        }

        _redoStack.Push(currentText);
        return _undoStack.Pop();
    }

    public string? Redo(string currentText)
    {
        if (_redoStack.Count == 0)
        {
            return null;
        }

        _undoStack.Push(currentText);
        return _redoStack.Pop();
    }

    public RichTextEditResult ApplyHeaderPrefix(string text, int cursor, int selectionLength, int level)
    {
        return ApplyBlockTransform(
            text,
            cursor,
            selectionLength,
            level switch
            {
                1 => RichTextBlockKind.Header1,
                2 => RichTextBlockKind.Header2,
                _ => RichTextBlockKind.Header3
            });
    }

    public RichTextEditResult ApplyBlockTransform(string text, int cursor, int selectionLength, RichTextBlockKind kind)
    {
        if (string.IsNullOrEmpty(text))
        {
            return RichTextEditResult.NoChange(text, cursor, selectionLength);
        }

        var start = Math.Clamp(cursor, 0, text.Length);
        var length = Math.Clamp(selectionLength, 0, text.Length - start);

        return length > 0
            ? ApplySelectionTransform(text, start, length, kind)
            : ApplyCurrentBlockTransform(text, start, kind);
    }

    public RichTextFindResult FindNext(string text, int cursor, int selectionLength, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return RichTextFindResult.NotFound;
        }

        var start = Math.Clamp(cursor + selectionLength, 0, text.Length);
        var index = text.IndexOf(query, start, StringComparison.OrdinalIgnoreCase);
        if (index < 0 && start > 0)
        {
            index = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        }

        return index < 0
            ? RichTextFindResult.NotFound
            : new RichTextFindResult(true, index, query.Length);
    }

    public RichTextBlockKind DetermineCurrentBlockKind(string text, int cursor)
    {
        if (string.IsNullOrEmpty(text))
        {
            return RichTextBlockKind.Paragraph;
        }

        var clampedCursor = Math.Clamp(cursor, 0, text.Length);
        if (IsInsideCodeFence(text, clampedCursor))
        {
            return RichTextBlockKind.Code;
        }

        var (lineStart, lineLength) = GetCurrentLineRange(text, clampedCursor);
        var line = text.Substring(lineStart, lineLength).TrimStart();
        if (line.StartsWith("### ", StringComparison.Ordinal))
        {
            return RichTextBlockKind.Header3;
        }

        if (line.StartsWith("## ", StringComparison.Ordinal))
        {
            return RichTextBlockKind.Header2;
        }

        if (line.StartsWith("# ", StringComparison.Ordinal))
        {
            return RichTextBlockKind.Header1;
        }

        if (Regex.IsMatch(line, @"^[-*]\s\[[ xX]\]\s"))
        {
            return RichTextBlockKind.Task;
        }

        if (line.StartsWith("> ", StringComparison.Ordinal))
        {
            return RichTextBlockKind.Quote;
        }

        if (Regex.IsMatch(line, @"^[-*]\s"))
        {
            return RichTextBlockKind.Bullet;
        }

        return RichTextBlockKind.Paragraph;
    }

    public static string StripKnownMarkdownPrefix(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("### ", StringComparison.Ordinal))
        {
            return trimmed[4..].Trim();
        }

        if (trimmed.StartsWith("## ", StringComparison.Ordinal))
        {
            return trimmed[3..].Trim();
        }

        if (trimmed.StartsWith("# ", StringComparison.Ordinal))
        {
            return trimmed[2..].Trim();
        }

        if (Regex.IsMatch(trimmed, @"^[-*]\s\[[ xX]\]\s"))
        {
            return Regex.Replace(trimmed, @"^[-*]\s\[[ xX]\]\s", string.Empty).Trim();
        }

        if (trimmed.StartsWith("> ", StringComparison.Ordinal))
        {
            return trimmed[2..].Trim();
        }

        if (Regex.IsMatch(trimmed, @"^[-*]\s"))
        {
            return Regex.Replace(trimmed, @"^[-*]\s", string.Empty).Trim();
        }

        if (trimmed.StartsWith("```", StringComparison.Ordinal) && trimmed.EndsWith("```", StringComparison.Ordinal))
        {
            return trimmed.Trim('`').Trim();
        }

        return trimmed;
    }

    public static (int Start, int Length) GetCurrentLineRange(string text, int cursor)
    {
        var lineStart = cursor;
        while (lineStart > 0 && text[lineStart - 1] != '\n')
        {
            lineStart--;
        }

        var lineEnd = cursor;
        while (lineEnd < text.Length && text[lineEnd] != '\n')
        {
            lineEnd++;
        }

        return (lineStart, lineEnd - lineStart);
    }

    public static string GetLeadingWhitespace(string line)
    {
        var index = 0;
        while (index < line.Length && char.IsWhiteSpace(line[index]) && line[index] != '\n' && line[index] != '\r')
        {
            index++;
        }

        return index == 0 ? string.Empty : line[..index];
    }

    public static string FormatBlock(RichTextBlockKind kind, string text, int listLevel)
    {
        return kind switch
        {
            RichTextBlockKind.Header1 => $"# {text}",
            RichTextBlockKind.Header2 => $"## {text}",
            RichTextBlockKind.Header3 => $"### {text}",
            RichTextBlockKind.Bullet => $"{new string(' ', Math.Max(0, listLevel - 1) * 2)}- {text}",
            RichTextBlockKind.Task => $"{new string(' ', Math.Max(0, listLevel - 1) * 2)}- [ ] {text}",
            RichTextBlockKind.Quote => $"> {text}",
            RichTextBlockKind.Code => $"```{Environment.NewLine}{text}{Environment.NewLine}```",
            _ => text
        };
    }

    private RichTextEditResult ApplySelectionTransform(string text, int start, int length, RichTextBlockKind kind)
    {
        var selected = text.Substring(start, length);
        var normalizedSelection = StripKnownMarkdownPrefix(selected.Trim());
        if (string.IsNullOrWhiteSpace(normalizedSelection))
        {
            return RichTextEditResult.NoChange(text, start, length);
        }

        var before = text[..start].TrimEnd();
        var after = text[(start + length)..].TrimStart();
        var transformed = FormatBlock(kind, normalizedSelection, 1);

        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(before))
        {
            builder.Append(before);
            builder.AppendLine();
            builder.AppendLine();
        }

        builder.Append(transformed);

        if (!string.IsNullOrWhiteSpace(after))
        {
            builder.AppendLine();
            builder.AppendLine();
            builder.Append(after);
        }

        var updated = builder.ToString();
        return new RichTextEditResult(updated, updated.IndexOf(transformed, StringComparison.Ordinal), transformed.Length, true);
    }

    private RichTextEditResult ApplyCurrentBlockTransform(string text, int cursor, RichTextBlockKind kind)
    {
        var (lineStart, lineLength) = GetCurrentLineRange(text, cursor);
        var line = text.Substring(lineStart, lineLength);
        var indent = GetLeadingWhitespace(line);
        var stripped = StripKnownMarkdownPrefix(line.Trim());
        var updatedLine = $"{indent}{FormatBlock(kind, stripped, 1)}";
        var updatedText = text.Remove(lineStart, lineLength).Insert(lineStart, updatedLine);
        return new RichTextEditResult(updatedText, Math.Min(lineStart + updatedLine.Length, updatedText.Length), 0, true);
    }

    private static bool IsInsideCodeFence(string text, int cursor)
    {
        var segment = text[..Math.Clamp(cursor, 0, text.Length)];
        var fenceCount = Regex.Matches(segment, @"^```", RegexOptions.Multiline).Count;
        return fenceCount % 2 == 1;
    }
}

public enum RichTextBlockKind
{
    Paragraph,
    Header1,
    Header2,
    Header3,
    Bullet,
    Task,
    Quote,
    Code
}

public readonly record struct RichTextEditResult(string Text, int CursorPosition, int SelectionLength, bool Changed)
{
    public static RichTextEditResult NoChange(string text, int cursorPosition, int selectionLength) => new(text, cursorPosition, selectionLength, false);
}

public readonly record struct RichTextFindResult(bool Found, int CursorPosition, int SelectionLength)
{
    public static RichTextFindResult NotFound => new(false, 0, 0);
}

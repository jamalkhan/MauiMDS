using System.Text;
using Microsoft.Extensions.Logging;

namespace MauiMds.Services;

public static class MarkdownFileConventions
{
    public static readonly string[] AllowedExtensions = [".mds", ".md"];
    public const string ExampleDocumentName = "example.mds";

    public static string EnsureValidFileName(string? fileName, bool allowEmpty)
    {
        var trimmed = (fileName ?? string.Empty).Trim();
        if (allowEmpty && string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new InvalidOperationException("File name cannot be empty.");
        }

        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            if (trimmed.Contains(invalidChar))
            {
                throw new InvalidOperationException("The file name contains invalid characters.");
            }
        }

        return trimmed;
    }

    public static void ValidateExtension(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        if (!AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Markdown files must use the .md or .mds extension.");
        }
    }

    public static string EnsureMarkdownExtension(string fileName)
    {
        if (AllowedExtensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase))
        {
            return fileName;
        }

        return $"{Path.GetFileNameWithoutExtension(fileName)}.mds";
    }

    public static Encoding ResolveEncoding(string encodingName, ILogger? logger = null)
    {
        try
        {
            return Encoding.GetEncoding(encodingName);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Unknown encoding '{EncodingName}', falling back to UTF-8", encodingName);
            return Encoding.UTF8;
        }
    }

    public static Encoding DetectEncoding(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode;
        }

        return Encoding.UTF8;
    }

    public static string DetectNewLine(string content)
    {
        if (content.Contains("\r\n", StringComparison.Ordinal))
        {
            return "\r\n";
        }

        return content.Contains('\r') ? "\r" : "\n";
    }

    public static string NormalizeNewLines(string content, string newLine)
    {
        return content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\n", newLine, StringComparison.Ordinal);
    }
}

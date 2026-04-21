using System.Text;

namespace MauiMds.Pdf;

/// <summary>
/// Parses markdown inline syntax from a Content string into typed spans.
/// Handles **bold**, *italic*, __bold__, _italic_, `code`, and [link](url).
/// </summary>
public static class PdfInlineParser
{
    public static List<PdfInlineSpan> Parse(string content, PdfStandardFont baseFont = PdfStandardFont.Helvetica)
    {
        var spans = new List<PdfInlineSpan>();
        var buf = new StringBuilder();
        var i = 0;

        while (i < content.Length)
        {
            // **bold**
            if (i + 1 < content.Length && content[i] == '*' && content[i + 1] == '*')
            {
                FlushBuffer(spans, buf, baseFont);
                i += 2;
                var end = content.IndexOf("**", i, StringComparison.Ordinal);
                if (end >= 0)
                {
                    spans.Add(new PdfInlineSpan(content[i..end], MakeBold(baseFont)));
                    i = end + 2;
                }
                else
                {
                    buf.Append("**");
                }
            }
            // __bold__
            else if (i + 1 < content.Length && content[i] == '_' && content[i + 1] == '_')
            {
                FlushBuffer(spans, buf, baseFont);
                i += 2;
                var end = content.IndexOf("__", i, StringComparison.Ordinal);
                if (end >= 0)
                {
                    spans.Add(new PdfInlineSpan(content[i..end], MakeBold(baseFont)));
                    i = end + 2;
                }
                else
                {
                    buf.Append("__");
                }
            }
            // *italic*
            else if (content[i] == '*')
            {
                FlushBuffer(spans, buf, baseFont);
                i += 1;
                var end = content.IndexOf('*', i);
                if (end >= 0)
                {
                    spans.Add(new PdfInlineSpan(content[i..end], MakeItalic(baseFont)));
                    i = end + 1;
                }
                else
                {
                    buf.Append('*');
                }
            }
            // _italic_ — only when preceded by start-of-string or space
            else if (content[i] == '_' && (i == 0 || content[i - 1] == ' '))
            {
                FlushBuffer(spans, buf, baseFont);
                i += 1;
                var end = content.IndexOf('_', i);
                if (end >= 0 && (end + 1 >= content.Length || content[end + 1] == ' ' || content[end + 1] == '\n'))
                {
                    spans.Add(new PdfInlineSpan(content[i..end], MakeItalic(baseFont)));
                    i = end + 1;
                }
                else
                {
                    buf.Append('_');
                }
            }
            // `code`
            else if (content[i] == '`')
            {
                FlushBuffer(spans, buf, baseFont);
                i += 1;
                var end = content.IndexOf('`', i);
                if (end >= 0)
                {
                    spans.Add(new PdfInlineSpan(content[i..end], PdfStandardFont.Courier));
                    i = end + 1;
                }
                else
                {
                    buf.Append('`');
                }
            }
            // [text](url) — render text only, in link colour
            else if (content[i] == '[')
            {
                var textEnd = content.IndexOf(']', i + 1);
                if (textEnd >= 0 && textEnd + 1 < content.Length && content[textEnd + 1] == '(')
                {
                    var urlEnd = content.IndexOf(')', textEnd + 2);
                    if (urlEnd >= 0)
                    {
                        FlushBuffer(spans, buf, baseFont);
                        spans.Add(new PdfInlineSpan(content[(i + 1)..textEnd], baseFont, PdfColor.LinkBlue));
                        i = urlEnd + 1;
                        continue;
                    }
                }
                buf.Append(content[i]);
                i++;
            }
            else
            {
                buf.Append(content[i]);
                i++;
            }
        }

        FlushBuffer(spans, buf, baseFont);
        return spans;
    }

    private static void FlushBuffer(List<PdfInlineSpan> spans, StringBuilder buf, PdfStandardFont font)
    {
        if (buf.Length == 0) return;
        spans.Add(new PdfInlineSpan(buf.ToString(), font));
        buf.Clear();
    }

    private static PdfStandardFont MakeBold(PdfStandardFont font) => font switch
    {
        PdfStandardFont.HelveticaOblique => PdfStandardFont.HelveticaBoldOblique,
        _ => PdfStandardFont.HelveticaBold
    };

    private static PdfStandardFont MakeItalic(PdfStandardFont font) => font switch
    {
        PdfStandardFont.HelveticaBold => PdfStandardFont.HelveticaBoldOblique,
        _ => PdfStandardFont.HelveticaOblique
    };
}

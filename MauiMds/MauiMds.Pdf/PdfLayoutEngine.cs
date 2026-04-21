namespace MauiMds.Pdf;

/// <summary>
/// Word-wraps a sequence of inline spans into lines that fit within a given width.
/// </summary>
public sealed class PdfLayoutEngine
{
    public List<PdfTextLine> WrapSpans(IEnumerable<PdfInlineSpan> spans, float lineWidth, float fontSize)
    {
        var lines = new List<PdfTextLine>();
        var currentRuns = new List<PdfTextRun>();
        var currentWidth = 0f;

        foreach (var span in spans)
        {
            if (string.IsNullOrEmpty(span.Text)) continue;

            var tokens = Tokenize(span.Text);

            foreach (var token in tokens)
            {
                if (token == "\n")
                {
                    lines.Add(new PdfTextLine(currentRuns));
                    currentRuns = [];
                    currentWidth = 0f;
                    continue;
                }

                var tokenWidth = PdfFontMetrics.MeasureString(token, span.Font, fontSize);

                if (currentWidth + tokenWidth > lineWidth && currentWidth > 0)
                {
                    lines.Add(new PdfTextLine(currentRuns));
                    currentRuns = [];
                    currentWidth = 0f;

                    var trimmed = token.TrimStart();
                    if (string.IsNullOrEmpty(trimmed)) continue;

                    currentRuns.Add(new PdfTextRun(trimmed, span.Font, span.Color));
                    currentWidth += PdfFontMetrics.MeasureString(trimmed, span.Font, fontSize);
                }
                else
                {
                    currentRuns.Add(new PdfTextRun(token, span.Font, span.Color));
                    currentWidth += tokenWidth;
                }
            }
        }

        if (currentRuns.Count > 0)
            lines.Add(new PdfTextLine(currentRuns));

        return lines;
    }

    /// <summary>
    /// Splits text into word tokens. Each token is either "\n", a bare word (first word of
    /// text or after a newline), or " word" (space(s) + word) so the space cost is bundled
    /// with the word it precedes — making it easy to strip leading space when wrapping.
    /// </summary>
    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var i = 0;

        while (i < text.Length)
        {
            if (text[i] == '\n')
            {
                tokens.Add("\n");
                i++;
            }
            else if (text[i] == ' ')
            {
                // Collect run of spaces, then attach the following word
                var spaceStart = i;
                while (i < text.Length && text[i] == ' ') i++;

                if (i >= text.Length || text[i] == '\n')
                {
                    // Trailing spaces — add as-is (they'll be trimmed on wrap anyway)
                    tokens.Add(text[spaceStart..i]);
                }
                else
                {
                    // Spaces + next word form one token
                    var wordStart = i;
                    while (i < text.Length && text[i] != ' ' && text[i] != '\n') i++;
                    tokens.Add(text[spaceStart..i]);
                }
                // (token recorded above)
            }
            else
            {
                var wordStart = i;
                while (i < text.Length && text[i] != ' ' && text[i] != '\n') i++;
                tokens.Add(text[wordStart..i]);
                // (token recorded above)
            }
        }

        return tokens;
    }
}

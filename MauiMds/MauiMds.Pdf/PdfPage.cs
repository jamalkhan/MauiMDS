namespace MauiMds.Pdf;

/// <summary>
/// Represents one page in a PDF document. Tracks a Y cursor (in PDF bottom-left coords,
/// starting near the top of the page and decreasing as content is added).
/// </summary>
public sealed class PdfPage
{
    private readonly PdfContentBuilder _content = new();
    private readonly float _marginBottom;

    public float ContentLeft { get; }
    public float ContentRight { get; }
    public float ContentWidth { get; }
    public float CurrentY { get; private set; }

    internal PdfPage(float pageWidth, float pageHeight, float marginLeft, float marginRight, float marginTop, float marginBottom)
    {
        _marginBottom = marginBottom;
        ContentLeft = marginLeft;
        ContentRight = pageWidth - marginRight;
        ContentWidth = pageWidth - marginLeft - marginRight;
        CurrentY = pageHeight - marginTop;
    }

    public bool HasRoomFor(float height) => CurrentY - height >= _marginBottom;

    public void Advance(float points) => CurrentY -= points;

    // ── Filled rectangle ──────────────────────────────────────────────────────

    public void FillRect(float x, float y, float width, float height, PdfColor color)
    {
        _content.SaveGraphicsState();
        _content.SetFillColor(color);
        _content.DrawRectangle(x, y, width, height);
        _content.Fill();
        _content.RestoreGraphicsState();
    }

    // ── Stroked rectangle ─────────────────────────────────────────────────────

    public void StrokeRect(float x, float y, float width, float height, float lineWidth, PdfColor color)
    {
        _content.SaveGraphicsState();
        _content.SetLineWidth(lineWidth);
        _content.SetStrokeColor(color);
        _content.DrawRectangle(x, y, width, height);
        _content.Stroke();
        _content.RestoreGraphicsState();
    }

    // ── Lines ─────────────────────────────────────────────────────────────────

    public void HorizontalLine(float x, float y, float width, float lineWidth, PdfColor color)
    {
        _content.SaveGraphicsState();
        _content.SetLineWidth(lineWidth);
        _content.SetStrokeColor(color);
        _content.MoveTo(x, y);
        _content.LineTo(x + width, y);
        _content.Stroke();
        _content.RestoreGraphicsState();
    }

    public void VerticalLine(float x, float yBottom, float yTop, float lineWidth, PdfColor color)
    {
        _content.SaveGraphicsState();
        _content.SetLineWidth(lineWidth);
        _content.SetStrokeColor(color);
        _content.MoveTo(x, yBottom);
        _content.LineTo(x, yTop);
        _content.Stroke();
        _content.RestoreGraphicsState();
    }

    // ── Text ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Draws a single line of mixed-font/mixed-color runs inside one BT..ET block.
    /// </summary>
    public void DrawTextLine(float x, float y, IReadOnlyList<PdfTextRun> runs, float fontSize)
    {
        if (runs.Count == 0) return;

        _content.BeginText();
        _content.SetTextPosition(x, y);

        PdfStandardFont? lastFont = null;
        PdfColor? lastColor = null;

        foreach (var run in runs)
        {
            if (string.IsNullOrEmpty(run.Text)) continue;

            if (lastFont != run.Font)
            {
                _content.SetFont(run.Font, fontSize);
                lastFont = run.Font;
            }

            if (lastColor is null || !ColorEquals(lastColor.Value, run.Color))
            {
                _content.SetFillColor(run.Color);
                lastColor = run.Color;
            }

            _content.ShowText(run.Text);
        }

        _content.EndText();
    }

    /// <summary>
    /// Draws plain text with a single font/color.
    /// </summary>
    public void DrawText(float x, float y, string text, PdfStandardFont font, float fontSize, PdfColor? color = null)
    {
        if (string.IsNullOrEmpty(text)) return;
        _content.BeginText();
        _content.SetFont(font, fontSize);
        _content.SetFillColor(color ?? PdfColor.Black);
        _content.SetTextPosition(x, y);
        _content.ShowText(text);
        _content.EndText();
    }

    internal string GetContentString() => _content.Build();

    private static bool ColorEquals(PdfColor a, PdfColor b) =>
        Math.Abs(a.R - b.R) < 0.001f &&
        Math.Abs(a.G - b.G) < 0.001f &&
        Math.Abs(a.B - b.B) < 0.001f;
}

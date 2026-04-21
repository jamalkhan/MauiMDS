using System.Text;

namespace MauiMds.Pdf;

/// <summary>
/// Builds the PDF content stream string from high-level drawing primitives.
/// All coordinates use the PDF bottom-left origin convention.
/// </summary>
internal sealed class PdfContentBuilder
{
    private readonly StringBuilder _sb = new();

    public void BeginText() => _sb.Append("BT\n");
    public void EndText() => _sb.Append("ET\n");

    public void SetFont(PdfStandardFont font, float size) =>
        _sb.Append($"/{PdfFontNames.ResourceName(font)} {size:F2} Tf\n");

    public void SetFillColor(PdfColor color) =>
        _sb.Append($"{color.R:F3} {color.G:F3} {color.B:F3} rg\n");

    public void SetStrokeColor(PdfColor color) =>
        _sb.Append($"{color.R:F3} {color.G:F3} {color.B:F3} RG\n");

    // Sets absolute text matrix (no rotation/scaling)
    public void SetTextPosition(float x, float y) =>
        _sb.Append($"1 0 0 1 {x:F2} {y:F2} Tm\n");

    public void ShowText(string text) =>
        _sb.Append($"({EscapePdfString(text)}) Tj\n");

    public void SetLineWidth(float width) =>
        _sb.Append($"{width:F2} w\n");

    public void DrawRectangle(float x, float y, float width, float height) =>
        _sb.Append($"{x:F2} {y:F2} {width:F2} {height:F2} re\n");

    public void Fill() => _sb.Append("f\n");
    public void Stroke() => _sb.Append("S\n");

    public void MoveTo(float x, float y) => _sb.Append($"{x:F2} {y:F2} m\n");
    public void LineTo(float x, float y) => _sb.Append($"{x:F2} {y:F2} l\n");

    public void SaveGraphicsState() => _sb.Append("q\n");
    public void RestoreGraphicsState() => _sb.Append("Q\n");

    public string Build() => _sb.ToString();

    private static string EscapePdfString(string text)
    {
        var sb = new StringBuilder(text.Length + 4);
        foreach (var c in text)
        {
            if (c == '\\') sb.Append("\\\\");
            else if (c == '(') sb.Append("\\(");
            else if (c == ')') sb.Append("\\)");
            else if (c >= 32 && c <= 126) sb.Append(c);
            else if (c > 126 && c <= 255) sb.Append(c); // Latin-1 passthrough
            else sb.Append('?');                          // Unsupported: replace
        }
        return sb.ToString();
    }
}

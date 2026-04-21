namespace MauiMds.Pdf;

public sealed class PdfInlineSpan(string text, PdfStandardFont font, PdfColor? color = null)
{
    public string Text { get; } = text;
    public PdfStandardFont Font { get; } = font;
    public PdfColor Color { get; } = color ?? PdfColor.Black;
}

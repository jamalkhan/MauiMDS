namespace MauiMds.Pdf;

public sealed class PdfTextRun(string text, PdfStandardFont font, PdfColor color)
{
    public string Text { get; } = text;
    public PdfStandardFont Font { get; } = font;
    public PdfColor Color { get; } = color;
}

public sealed class PdfTextLine(IEnumerable<PdfTextRun> runs)
{
    public IReadOnlyList<PdfTextRun> Runs { get; } = runs.ToList();
    public bool IsEmpty => Runs.Count == 0 || Runs.All(r => string.IsNullOrEmpty(r.Text));
}

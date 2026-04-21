namespace MauiMds.Pdf;

public sealed class PdfDocument
{
    private readonly List<PdfPage> _pages = [];

    // A4 page dimensions in points (1 pt = 1/72 inch)
    public float PageWidth { get; init; } = 595f;
    public float PageHeight { get; init; } = 842f;

    public float MarginLeft { get; init; } = 60f;
    public float MarginRight { get; init; } = 60f;
    public float MarginTop { get; init; } = 72f;
    public float MarginBottom { get; init; } = 72f;

    public float ContentWidth => PageWidth - MarginLeft - MarginRight;

    public IReadOnlyList<PdfPage> Pages => _pages;

    public PdfPage AddPage()
    {
        var page = new PdfPage(PageWidth, PageHeight, MarginLeft, MarginRight, MarginTop, MarginBottom);
        _pages.Add(page);
        return page;
    }

    public byte[] ToBytes() => PdfWriter.Write(this);
}

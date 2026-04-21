namespace MauiMds.Pdf;

public readonly struct PdfColor(float r, float g, float b)
{
    public float R { get; } = r;
    public float G { get; } = g;
    public float B { get; } = b;

    public static PdfColor Black => new(0f, 0f, 0f);
    public static PdfColor White => new(1f, 1f, 1f);
    public static PdfColor LightGrey => new(0.92f, 0.92f, 0.92f);
    public static PdfColor MediumGrey => new(0.72f, 0.72f, 0.72f);
    public static PdfColor DarkGrey => new(0.45f, 0.45f, 0.45f);
    public static PdfColor CodeBackground => new(0.95f, 0.95f, 0.95f);
    public static PdfColor QuoteBar => new(0.76f, 0.76f, 0.76f);
    public static PdfColor LinkBlue => new(0.10f, 0.10f, 0.70f);
}

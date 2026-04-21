namespace MauiMds.Pdf;

/// <summary>
/// AFM character width tables for PDF Standard 14 fonts.
/// Widths are in 1/1000 units — multiply by font size to get points.
/// Source: Adobe Font Metrics files (public domain).
/// </summary>
public static class PdfFontMetrics
{
    // Helvetica AFM widths for printable ASCII 32–126 (95 entries)
    private static readonly int[] HelveticaWidths =
    [
        278, 278, 355, 556, 556, 889, 667, 222, 333, 333, 389, 584, 278, 333, 278, 278, // 32-47
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556, // 48-63
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778, // 64-79
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556, // 80-95
        222, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556, // 96-111
        556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584        // 112-126
    ];

    // Helvetica-Bold AFM widths for printable ASCII 32–126
    private static readonly int[] HelveticaBoldWidths =
    [
        278, 333, 474, 556, 556, 889, 722, 278, 333, 333, 389, 584, 278, 333, 278, 278, // 32-47
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 333, 333, 584, 584, 584, 611, // 48-63
        975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778, // 64-79
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556, // 80-95
        278, 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611, // 96-111
        611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584        // 112-126
    ];

    // Courier is monospace: all characters 600 units wide
    private const int CourierWidth = 600;
    private const int FallbackWidth = 556;

    public static float MeasureChar(char c, PdfStandardFont font, float fontSize)
    {
        var widthTable = GetWidthTable(font);
        var idx = (int)c - 32;
        int raw;

        if (widthTable is null)
        {
            raw = CourierWidth;
        }
        else if (idx >= 0 && idx < widthTable.Length)
        {
            raw = widthTable[idx];
        }
        else
        {
            raw = FallbackWidth;
        }

        return raw * fontSize / 1000f;
    }

    public static float MeasureString(string text, PdfStandardFont font, float fontSize)
    {
        if (string.IsNullOrEmpty(text)) return 0f;
        var total = 0f;
        foreach (var c in text)
            total += MeasureChar(c, font, fontSize);
        return total;
    }

    private static int[]? GetWidthTable(PdfStandardFont font) => font switch
    {
        PdfStandardFont.Helvetica or PdfStandardFont.HelveticaOblique => HelveticaWidths,
        PdfStandardFont.HelveticaBold or PdfStandardFont.HelveticaBoldOblique => HelveticaBoldWidths,
        _ => null  // Courier variants: monospace, handled by fallback
    };
}

namespace MauiMds.Pdf;

public enum PdfStandardFont
{
    Helvetica,
    HelveticaBold,
    HelveticaOblique,
    HelveticaBoldOblique,
    Courier,
    CourierBold
}

internal static class PdfFontNames
{
    internal static string ResourceName(PdfStandardFont font) => font switch
    {
        PdfStandardFont.Helvetica => "F1",
        PdfStandardFont.HelveticaBold => "F2",
        PdfStandardFont.HelveticaOblique => "F3",
        PdfStandardFont.HelveticaBoldOblique => "F4",
        PdfStandardFont.Courier => "F5",
        PdfStandardFont.CourierBold => "F6",
        _ => "F1"
    };

    internal static string BaseFontName(PdfStandardFont font) => font switch
    {
        PdfStandardFont.Helvetica => "Helvetica",
        PdfStandardFont.HelveticaBold => "Helvetica-Bold",
        PdfStandardFont.HelveticaOblique => "Helvetica-Oblique",
        PdfStandardFont.HelveticaBoldOblique => "Helvetica-BoldOblique",
        PdfStandardFont.Courier => "Courier",
        PdfStandardFont.CourierBold => "Courier-Bold",
        _ => "Helvetica"
    };
}
